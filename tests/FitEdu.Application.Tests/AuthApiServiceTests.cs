using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class AuthApiServiceTests
{
    private const string SiteBaseUrl = "https://site.test";

    private sealed record Harness(AuthApiService Svc, UserManager<ApplicationUser> Um, SignInManager<ApplicationUser> Sm, ApplicationDbContext Db, ITokenService Tok, IEmailService Email,
        ISubscriptionService Subs, RecordingLogger<AuthApiService> Log);

    // dbName: two harnesses with the same name share one database, like two requests at the same time.
    private static Harness Build(string? dbName = null, TimeProvider? timeProvider = null)
    {
        var um = IdentityMocks.MockUserManager();
        um.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        um.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        um.DeleteAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        var sm = IdentityMocks.MockSignInManager(um);
        var tok = Substitute.For<ITokenService>();
        tok.GenerateAccessTokenAsync(Arg.Any<ApplicationUser>())
            .Returns(Task.FromResult(("access-token", DateTime.UtcNow.AddMinutes(15))));
        tok.GenerateRefreshToken().Returns(_ => "new-plain-refresh-" + Guid.NewGuid().ToString("N"));
        var subs = Substitute.For<ISubscriptionService>();
        var email = Substitute.For<IEmailService>();
        var db = DbContextFactory.CreateInMemory(dbName);

        var log = new RecordingLogger<AuthApiService>();

        var svc = new AuthApiService(um, sm, tok, subs, log, db, email,
            Options.Create(new AppOptions { BaseUrl = SiteBaseUrl }), timeProvider ?? TimeProvider.System);
        return new Harness(svc, um, sm, db, tok, email, subs, log);
    }

    private static ApplicationUser User(string id = "u1", bool enabled = true, string stamp = "stamp-1") =>
        new() { Id = id, Email = "u@x.com", UserName = "u@x.com", FirstName = "F", LastName = "L", IsEnabled = enabled, SecurityStamp = stamp };

    // HashToken is private static — reflect into it so tests can seed a RefreshToken row whose
    // TokenHash matches what the service will compute for a given plaintext token.
    private static string HashToken(string plain)
    {
        var method = typeof(AuthApiService).GetMethod("HashToken", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, [plain])!;
    }

    // Include(rt => rt.User) needs a real matching row in the DB — mocking UserManager alone
    // isn't enough (same EF InMemory "silently drops rows on unresolvable required navigation"
    // gotcha as elsewhere in this suite). Idempotent so callers can seed the same user once.
    private static void EnsureUserInDb(ApplicationDbContext db, ApplicationUser user)
    {
        if (!db.Users.Local.Any(u => u.Id == user.Id))
            db.Users.Add(user);
    }

    private static RefreshToken Seed(ApplicationDbContext db, ApplicationUser user, string plainToken,
        string? securityStampAtIssuance, DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null, Guid? replacedByTokenId = null)
    {
        EnsureUserInDb(db, user);
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(plainToken),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStampAtIssuance = securityStampAtIssuance,
            RevokedAt = revokedAt,
            ReplacedByTokenId = replacedByTokenId
        };
        db.RefreshTokens.Add(token);
        return token;
    }

    // ---- RegisterAsync: REL-2 ---------------------------------------------------

    private static RegisterRequestDto RegisterRequest() => new()
    {
        Email = "u@x.com",
        Password = "Password1!",
        ConfirmPassword = "Password1!",
        FirstName = "F",
        LastName = "L",
        GdprConsent = true
    };

    [Fact]
    public async Task Register_EmailSendThrows_StillReturnsSuccess()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");
        h.Email.SendEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("SMTP unreachable")));

        var result = await h.Svc.RegisterAsync(RegisterRequest());

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_EmailSendSucceeds_ReturnsSuccess()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        var result = await h.Svc.RegisterAsync(RegisterRequest());

        result.IsSuccess.ShouldBeTrue();
        await h.Email.Received(1).SendEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_OnTheGreekSite_StoresGreekAndSendsTheGreekConfirmation()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");
        using var cultureScope = new CultureScope("el-GR");

        await h.Svc.RegisterAsync(RegisterRequest());

        createdUser!.PreferredLanguage.ShouldBe(UserLanguage.Greek);
        await h.Email.Received(1).SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<string>(), UserLanguage.Greek, Arg.Any<CancellationToken>());
    }

    // ---- LoginAsync: a correct password saves the language the member signs in from ----

    [Fact]
    public async Task Login_OnTheGreekSite_SavesGreekAsTheStoredLanguage()
    {
        var h = Build();
        var member = User(); // stored language: English (the default)
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Success);
        using var cultureScope = new CultureScope("el-GR");

        var result = await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        result.IsSuccess.ShouldBeTrue();
        member.PreferredLanguage.ShouldBe(UserLanguage.Greek);
        await h.Um.Received(1).UpdateAsync(member);
    }

    // ---- ForgotPasswordAsync: the member asks, so the request language is used ----
    // The request is anonymous, so it never changes the stored language: anyone who knows the
    // email could otherwise flip it. The next sign-in saves the member's language instead.

    [Fact]
    public async Task ForgotPassword_OnTheGreekSite_SendsTheGreekResetAndKeepsTheStoredLanguage()
    {
        var h = Build();
        var member = User(); // stored language: English (the default)
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Um.GeneratePasswordResetTokenAsync(member).Returns("reset-token");
        using var cultureScope = new CultureScope("el-GR");

        await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = member.Email! });

        await h.Email.Received(1).SendPasswordResetAsync(
            member.Email!, Arg.Any<string>(), UserLanguage.Greek, Arg.Any<CancellationToken>());
        member.PreferredLanguage.ShouldBe(UserLanguage.English);
        await h.Um.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task ForgotPassword_InTheStoredLanguage_DoesNotRewriteTheUser()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Um.GeneratePasswordResetTokenAsync(member).Returns("reset-token");
        using var cultureScope = new CultureScope("en-GB");

        await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = member.Email! });

        await h.Email.Received(1).SendPasswordResetAsync(
            member.Email!, Arg.Any<string>(), UserLanguage.English, Arg.Any<CancellationToken>());
        await h.Um.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    // ---- Emailed links: built from the configured site address, every value escaped ----

    [Fact]
    public async Task Register_ConfirmationLink_StartsWithTheConfiguredSiteAddressAndEscapesTheToken()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("a+b/c==");

        await h.Svc.RegisterAsync(RegisterRequest());

        await h.Email.Received(1).SendEmailConfirmationAsync(
            Arg.Any<string>(),
            $"{SiteBaseUrl}/confirm-email?userId={createdUser!.Id}&token=a%2Bb%2Fc%3D%3D",
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgotPassword_PlusAddress_LinkEscapesEmail()
    {
        var h = Build();
        var member = User();
        member.Email = "a+b@test.com";
        h.Um.FindByEmailAsync(member.Email).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Um.GeneratePasswordResetTokenAsync(member).Returns("reset-token");

        await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = member.Email });

        await h.Email.Received(1).SendPasswordResetAsync(
            member.Email,
            $"{SiteBaseUrl}/reset-password?email=a%2Bb%40test.com&token=reset-token",
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgotPassword_EmailSendThrows_StillReturnsGenericSuccess()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Um.GeneratePasswordResetTokenAsync(member).Returns("reset-token");
        h.Email.SendPasswordResetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("SMTP unreachable")));

        var result = await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = member.Email! });

        // The same answer as for an unknown email, so a failed send cannot reveal which emails have an account.
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe(SuccessMessagesRes.PasswordResetLinkSent);
    }

    [Fact]
    public async Task Register_WithIntermediateStatus_AssignsIntermediateStatusToCreatedUser()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        var request = RegisterRequest();
        request.Status = UserStatus.Intermediate;
        await h.Svc.RegisterAsync(request);

        createdUser!.Status.ShouldBe(UserStatus.Intermediate);
    }

    [Fact]
    public async Task Register_WithUnknownStatus_CoercesToBeginner()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        var request = RegisterRequest();
        request.Status = UserStatus.Unknown;
        await h.Svc.RegisterAsync(request);

        createdUser!.Status.ShouldBe(UserStatus.Beginner);
    }

    [Fact]
    public async Task Register_WithGoal_SavesGoalOnCreatedUser()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        var request = RegisterRequest();
        request.Goal = FitnessGoal.Strength;
        await h.Svc.RegisterAsync(request);

        createdUser!.Goal.ShouldBe(FitnessGoal.Strength);
    }

    // ---- Registration is all or nothing (P01-14) ----

    [Fact]
    public async Task Register_AssignPlanThrows_DeletesUserAndReturnsError()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Subs.AssignFreePlanAsync(Arg.Any<string>())
            .Returns(_ => Task.FromException(new InvalidOperationException("database unreachable")));

        var result = await h.Svc.RegisterAsync(RegisterRequest());

        result.IsSuccess.ShouldBeFalse();
        result.Value!.Success.ShouldBeFalse();
        result.Value.Message.ShouldBe(ErrorMessagesRes.RegistrationFailedGeneric);
        await h.Um.Received(1).DeleteAsync(Arg.Any<ApplicationUser>());
        await h.Email.DidNotReceiveWithAnyArgs().SendEmailConfirmationAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Register_RoleAssignmentFails_DeletesUserAndReturnsError()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Student")
            .Returns(IdentityResult.Failed(new IdentityError { Code = "RoleMissing", Description = "Role STUDENT does not exist." }));

        var result = await h.Svc.RegisterAsync(RegisterRequest());

        result.IsSuccess.ShouldBeFalse();
        await h.Um.Received(1).DeleteAsync(Arg.Any<ApplicationUser>());
        await h.Subs.DidNotReceive().AssignFreePlanAsync(Arg.Any<string>());
    }

    // ---- Sign-up page: the one visible difference from the API is the taken email (owner decision D1) ----

    [Fact]
    public async Task RegisterFromSignUpPage_DuplicateEmail_ReportsTheTakenEmail()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(
            IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "Email 'u@x.com' is already taken." }));

        var result = await h.Svc.RegisterFromSignUpPageAsync(RegisterRequest());

        result.Value!.Errors.ShouldBe(["Email 'u@x.com' is already taken."]);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsTheGenericMessageOnTheApi()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(
            IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "Email 'u@x.com' is already taken." }));

        var result = await h.Svc.RegisterAsync(RegisterRequest());

        result.Value!.Errors.ShouldBe([ErrorMessagesRes.RegistrationFailedGeneric]);
    }

    [Fact]
    public async Task RegisterFromSignUpPage_Success_SendsTheConfirmationEmail()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        var result = await h.Svc.RegisterFromSignUpPageAsync(RegisterRequest());

        result.IsSuccess.ShouldBeTrue();
        await h.Subs.Received(1).AssignFreePlanAsync(Arg.Any<string>());
        await h.Email.Received(1).SendEmailConfirmationAsync("u@x.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- The under-18 notice follows the members' date, not the server's UTC date (P01-19) ----

    [Fact]
    public async Task Register_BirthdayAlreadyReachedInTheMembersZone_IsNotLoggedAsUnderEighteen()
    {
        // 22:30 UTC on 14 June is already 15 June in Athens: a member born on 15 June 2008 is 18 there, 17 in UTC.
        var h = Build(timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 6, 14, 22, 30, 0, TimeSpan.Zero)));
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");
        var request = RegisterRequest();
        request.DateOfBirth = new DateTime(2008, 6, 15);

        await h.Svc.RegisterAsync(request);

        h.Log.Information.ShouldNotContain(entry => entry.Contains("Under-18", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Register_StillSeventeenInTheMembersZone_LogsTheUnderEighteenNotice()
    {
        var h = Build(timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 6, 14, 10, 0, 0, TimeSpan.Zero)));
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");
        var request = RegisterRequest();
        request.DateOfBirth = new DateTime(2008, 6, 15);

        await h.Svc.RegisterAsync(request);

        h.Log.Information.ShouldContain(entry => entry.Contains("Under-18", StringComparison.Ordinal));
    }

    // ---- Logs never hold an email address (P01-1) ----

    private const string PrivateEmail = "private.person@secret-mail.test";

    private static List<string> LoggedText(Harness h) => [.. h.Log.Information, .. h.Log.Warnings, .. h.Log.Errors];

    [Fact]
    public async Task Register_Branches_NeverLogTheEmail()
    {
        var h = Build();
        var request = RegisterRequest();
        request.Email = PrivateEmail;
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Failed(
            new IdentityError { Code = "DuplicateEmail", Description = $"Email '{PrivateEmail}' is already taken." }));
        await h.Svc.RegisterAsync(request);

        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");
        h.Email.SendEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("SMTP unreachable")));
        await h.Svc.RegisterAsync(request);

        LoggedText(h).ShouldNotBeEmpty();
        LoggedText(h).ShouldAllBe(entry => !entry.Contains(PrivateEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_Branches_NeverLogTheEmail()
    {
        var h = Build();
        var member = User();
        member.Email = PrivateEmail;
        var login = new LoginRequestDto { Email = PrivateEmail, Password = "Password1!" };

        // unknown email
        h.Um.FindByEmailAsync(PrivateEmail).Returns((ApplicationUser?)null);
        await h.Svc.LoginAsync(login);

        h.Um.FindByEmailAsync(PrivateEmail).Returns(member);

        // email not confirmed (the real SignInManager answers NotAllowed before it looks at the password)
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.NotAllowed);
        h.Um.IsEmailConfirmedAsync(member).Returns(false);
        await h.Svc.LoginAsync(login);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);

        // wrong password, locked out, disabled (right password), success
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Failed);
        await h.Svc.LoginAsync(login);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.LockedOut);
        await h.Svc.LoginAsync(login);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Success);
        member.IsEnabled = false;
        await h.Svc.LoginAsync(login);
        member.IsEnabled = true;
        await h.Svc.LoginAsync(login);

        LoggedText(h).ShouldNotBeEmpty();
        LoggedText(h).ShouldAllBe(entry => !entry.Contains(PrivateEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ForgotAndResetPassword_Branches_NeverLogTheEmail()
    {
        var h = Build();
        var member = User();
        member.Email = PrivateEmail;

        // unknown email, then a confirmed account whose reset email cannot be sent
        h.Um.FindByEmailAsync(PrivateEmail).Returns((ApplicationUser?)null);
        await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = PrivateEmail });
        await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto { Email = PrivateEmail, Token = "t", NewPassword = "Password1!", ConfirmPassword = "Password1!" });

        h.Um.FindByEmailAsync(PrivateEmail).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Um.GeneratePasswordResetTokenAsync(member).Returns("reset-token");
        h.Email.SendPasswordResetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("SMTP unreachable")));
        await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = PrivateEmail });

        // reset with a bad token, then a good one
        h.Um.ResetPasswordAsync(member, "bad", Arg.Any<string>()).Returns(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));
        await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto { Email = PrivateEmail, Token = "bad", NewPassword = "Password1!", ConfirmPassword = "Password1!" });
        h.Um.ResetPasswordAsync(member, "good", Arg.Any<string>()).Returns(IdentityResult.Success);
        await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto { Email = PrivateEmail, Token = "good", NewPassword = "Password1!", ConfirmPassword = "Password1!" });

        LoggedText(h).ShouldNotBeEmpty();
        LoggedText(h).ShouldAllBe(entry => !entry.Contains(PrivateEmail, StringComparison.OrdinalIgnoreCase));
    }

    // ---- LoginAsync: "disabled" is only told after the right password (P01-11) ----

    [Fact]
    public async Task Login_DisabledUserWrongPassword_ReturnsInvalidCredentials()
    {
        var h = Build();
        var member = User(enabled: false);
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Failed);

        var result = await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Wrong-Pass-9!" });

        result.IsSuccess.ShouldBeFalse();
        result.Value!.Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_DisabledUserRightPassword_SaysTheAccountIsDisabled()
    {
        var h = Build();
        var member = User(enabled: false);
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Success);

        var result = await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        result.IsSuccess.ShouldBeFalse();
        result.Value!.Message.ShouldBe(ErrorMessagesRes.AccountDisabledContactSupport);
        await h.Tok.DidNotReceive().GenerateAccessTokenAsync(Arg.Any<ApplicationUser>());
    }

    // ---- LoginAsync: outcomes and the refresh-token lifetime ----

    private static LoginRequestDto LoginRequest(string password = "Password1!", bool rememberMe = false) =>
        new() { Email = "u@x.com", Password = password, RememberMe = rememberMe };

    [Fact]
    public async Task Login_UnknownEmail_Returns401InvalidCredentials()
    {
        var h = Build();
        h.Um.FindByEmailAsync("u@x.com").Returns((ApplicationUser?)null);

        var result = await h.Svc.LoginAsync(LoginRequest());

        result.StatusCode.ShouldBe(401);
        result.Value!.Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_LockedOutAccount_SaysTheAccountIsLocked()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.LockedOut);

        var result = await h.Svc.LoginAsync(LoginRequest());

        result.StatusCode.ShouldBe(401);
        result.Value!.Message.ShouldBe(ErrorMessagesRes.AccountLocked);
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 30)]
    public async Task Login_Success_StoresARefreshTokenForSevenDaysOrThirtyWithRememberMe(bool rememberMe, int days)
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Success);

        var result = await h.Svc.LoginAsync(LoginRequest(rememberMe: rememberMe));

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Token.ShouldBe("access-token");
        var stored = await h.Db.RefreshTokens.SingleAsync();
        stored.TokenHash.ShouldBe(HashToken(result.Value.RefreshToken!));
        stored.SecurityStampAtIssuance.ShouldBe(member.SecurityStamp);
        (stored.ExpiresAt - stored.CreatedAt).ShouldBe(TimeSpan.FromDays(days), TimeSpan.FromSeconds(5));
    }

    // ---- ConfirmEmailAsync ----

    [Theory]
    [InlineData("", "token")]
    [InlineData("u1", "")]
    public async Task ConfirmEmail_MissingValue_Returns400(string userId, string token)
    {
        var h = Build();

        var result = await h.Svc.ConfirmEmailAsync(userId, token);

        result.StatusCode.ShouldBe(400);
        result.Value!.Message.ShouldBe(ErrorMessagesRes.InvalidConfirmationLink);
    }

    [Fact]
    public async Task ConfirmEmail_UnknownUser_Returns404()
    {
        var h = Build();
        h.Um.FindByIdAsync("u1").Returns((ApplicationUser?)null);

        (await h.Svc.ConfirmEmailAsync("u1", "token")).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task ConfirmEmail_RejectedToken_Returns400WithTheIdentityErrors()
    {
        var h = Build();
        var member = User();
        h.Um.FindByIdAsync("u1").Returns(member);
        h.Um.ConfirmEmailAsync(member, "bad").Returns(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));

        var result = await h.Svc.ConfirmEmailAsync("u1", "bad");

        result.StatusCode.ShouldBe(400);
        result.Value!.Errors.ShouldBe(["Invalid token."]);
    }

    [Fact]
    public async Task ConfirmEmail_ValidToken_ConfirmsTheEmail()
    {
        var h = Build();
        var member = User();
        h.Um.FindByIdAsync("u1").Returns(member);
        h.Um.ConfirmEmailAsync(member, "good").Returns(IdentityResult.Success);

        var result = await h.Svc.ConfirmEmailAsync("u1", "good");

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Message.ShouldBe(SuccessMessagesRes.EmailConfirmedSuccessfully);
    }

    // ---- Development-only helpers (the controller exposes them only in Debug builds on a Development host) ----

    [Fact]
    public async Task DevConfirmEmail_UnknownUser_Returns404()
    {
        var h = Build();
        h.Um.FindByEmailAsync("u@x.com").Returns((ApplicationUser?)null);

        (await h.Svc.DevConfirmEmailAsync("u@x.com")).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task DevConfirmEmail_KnownUser_MarksTheEmailConfirmed()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);

        var result = await h.Svc.DevConfirmEmailAsync(member.Email!);

        result.IsSuccess.ShouldBeTrue();
        member.EmailConfirmed.ShouldBeTrue();
        await h.Um.Received(1).UpdateAsync(member);
    }

    [Fact]
    public async Task DevConfirmEmail_UpdateFails_Returns400()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.UpdateAsync(member).Returns(IdentityResult.Failed(new IdentityError { Code = "Boom", Description = "boom" }));

        (await h.Svc.DevConfirmEmailAsync(member.Email!)).StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task DevResetPassword_UnknownUser_Returns404()
    {
        var h = Build();
        h.Um.FindByEmailAsync("u@x.com").Returns((ApplicationUser?)null);

        var result = await h.Svc.DevResetPasswordAsync(new DevResetPasswordRequestDto { Email = "u@x.com", NewPassword = "Another-Pass-1!" });

        result.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task DevResetPassword_KnownUser_ResetsThePasswordAndRevokesTheirTokens()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.GeneratePasswordResetTokenAsync(member).Returns("reset-token");
        h.Um.ResetPasswordAsync(member, "reset-token", "Another-Pass-1!").Returns(IdentityResult.Success);
        Seed(h.Db, member, "plain-1", member.SecurityStamp);
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.DevResetPasswordAsync(new DevResetPasswordRequestDto { Email = member.Email!, NewPassword = "Another-Pass-1!" });

        result.IsSuccess.ShouldBeTrue();
        (await h.Db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DevResetPassword_RejectedPassword_Returns400WithTheErrors()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.GeneratePasswordResetTokenAsync(member).Returns("reset-token");
        h.Um.ResetPasswordAsync(member, "reset-token", "weak")
            .Returns(IdentityResult.Failed(new IdentityError { Code = "PasswordTooShort", Description = "Too short." }));

        var result = await h.Svc.DevResetPasswordAsync(new DevResetPasswordRequestDto { Email = member.Email!, NewPassword = "weak" });

        result.StatusCode.ShouldBe(400);
        result.Value!.Errors.ShouldBe(["Too short."]);
    }

    // ---- LoginAsync: an unconfirmed account takes the same password check (R3) ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_UnconfirmedAccount_ChecksThePasswordAndAlwaysAnswersInvalidCredentials(bool passwordIsRight)
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(false);
        // With RequireConfirmedEmail the real SignInManager answers NotAllowed before it hashes the password.
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.NotAllowed);
        h.Um.CheckPasswordAsync(member, "Password1!").Returns(passwordIsRight);

        var result = await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        result.IsSuccess.ShouldBeFalse();
        result.Value!.Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);
        await h.Um.Received(1).CheckPasswordAsync(member, "Password1!");
        await h.Tok.DidNotReceive().GenerateAccessTokenAsync(Arg.Any<ApplicationUser>());
    }

    private static ApplicationUser UnconfirmedMemberWithLockout(Harness h, bool lockoutEnabled = true)
    {
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(false);
        h.Um.SupportsUserLockout.Returns(true);
        h.Um.GetLockoutEnabledAsync(member).Returns(lockoutEnabled);
        return member;
    }

    [Fact]
    public async Task Login_UnconfirmedAccountWrongPassword_CountsAsAFailedAttempt()
    {
        var h = Build();
        var member = UnconfirmedMemberWithLockout(h);
        h.Um.CheckPasswordAsync(member, "Password1!").Returns(false);

        var result = await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        result.Value!.Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);
        await h.Um.Received(1).AccessFailedAsync(member);
    }

    [Fact]
    public async Task Login_UnconfirmedAccountWrongPasswordThatLocksTheAccount_AnswersAccountLocked()
    {
        var h = Build();
        var member = UnconfirmedMemberWithLockout(h);
        h.Um.CheckPasswordAsync(member, "Password1!").Returns(false);
        h.Um.IsLockedOutAsync(member).Returns(false, true);

        var result = await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        result.IsSuccess.ShouldBeFalse();
        result.Value!.Message.ShouldBe(ErrorMessagesRes.AccountLocked);
    }

    [Fact]
    public async Task Login_UnconfirmedAccountAlreadyLockedOut_AnswersAccountLockedWithoutCheckingThePassword()
    {
        var h = Build();
        var member = UnconfirmedMemberWithLockout(h);
        h.Um.IsLockedOutAsync(member).Returns(true);

        var result = await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        result.Value!.Message.ShouldBe(ErrorMessagesRes.AccountLocked);
        await h.Um.DidNotReceiveWithAnyArgs().CheckPasswordAsync(default!, default!);
        await h.Um.DidNotReceiveWithAnyArgs().AccessFailedAsync(default!);
    }

    [Fact]
    public async Task Login_UnconfirmedAccountRightPasswordOrLockoutDisabled_IsNotCounted()
    {
        var h = Build();
        var member = UnconfirmedMemberWithLockout(h);
        h.Um.CheckPasswordAsync(member, "Password1!").Returns(true);
        await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        var other = Build();
        var otherMember = UnconfirmedMemberWithLockout(other, lockoutEnabled: false);
        other.Um.CheckPasswordAsync(otherMember, "Password1!").Returns(false);
        await other.Svc.LoginAsync(new LoginRequestDto { Email = otherMember.Email!, Password = "Password1!" });

        await h.Um.DidNotReceiveWithAnyArgs().AccessFailedAsync(default!);
        await other.Um.DidNotReceiveWithAnyArgs().AccessFailedAsync(default!);
    }

    [Fact]
    public async Task Login_UnconfirmedAccountRightPassword_ResetsTheFailedAttemptCount()
    {
        // As Identity does for a confirmed account: a right password clears the earlier wrong guesses.
        var h = Build();
        var member = UnconfirmedMemberWithLockout(h);
        h.Um.CheckPasswordAsync(member, "Password1!").Returns(true);

        await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Password1!" });

        await h.Um.Received(1).ResetAccessFailedCountAsync(member);
    }

    [Fact]
    public async Task Login_ConfirmedAccount_HashesThePasswordOnce()
    {
        var h = Build();
        var member = User();
        h.Um.FindByEmailAsync(member.Email!).Returns(member);
        h.Um.IsEmailConfirmedAsync(member).Returns(true);
        h.Sm.CheckPasswordSignInAsync(member, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Failed);

        await h.Svc.LoginAsync(new LoginRequestDto { Email = member.Email!, Password = "Wrong-Pass-9!" });

        await h.Um.DidNotReceive().CheckPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    // ---- RefreshAsync: rotation keeps the lifetime the token was issued with (P01-8) ----

    [Fact]
    public async Task Refresh_RememberMeToken_KeepsThirtyDayWindow()
    {
        var h = Build();
        var user = User();
        var issuedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var token = Seed(h.Db, user, "plain-1", user.SecurityStamp, expiresAt: issuedAt.AddDays(30));
        token.CreatedAt = issuedAt;
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-1" });

        result.IsSuccess.ShouldBeTrue();
        var rotated = await h.Db.RefreshTokens.SingleAsync(rt => rt.Id != token.Id);
        (rotated.ExpiresAt - rotated.CreatedAt).ShouldBe(TimeSpan.FromDays(30), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Refresh_ShortLivedToken_KeepsItsSevenDayWindow()
    {
        var h = Build();
        var user = User();
        var issuedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var token = Seed(h.Db, user, "plain-1", user.SecurityStamp, expiresAt: issuedAt.AddDays(7));
        token.CreatedAt = issuedAt;
        await h.Db.SaveChangesAsync();

        await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-1" });

        var rotated = await h.Db.RefreshTokens.SingleAsync(rt => rt.Id != token.Id);
        (rotated.ExpiresAt - rotated.CreatedAt).ShouldBe(TimeSpan.FromDays(7), TimeSpan.FromSeconds(1));
    }

    // ---- RefreshAsync: baseline behaviour -------------------------------------

    [Fact]
    public async Task Refresh_ValidToken_RotatesAndStampsNewToken()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        Seed(h.Db, user, "plain-1", securityStampAtIssuance: user.SecurityStamp);
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-1" });

        result.Value!.Success.ShouldBeTrue();
        result.Value.RefreshToken.ShouldNotBeNullOrEmpty();
        var rows = await h.Db.RefreshTokens.ToListAsync();
        rows.Count.ShouldBe(2);
        var oldRow = rows.Single(r => r.TokenHash == HashToken("plain-1"));
        oldRow.RevokedAt.ShouldNotBeNull();
        oldRow.ReplacedByTokenId.ShouldNotBeNull();
        var newRow = rows.Single(r => r.Id == oldRow.ReplacedByTokenId);
        newRow.SecurityStampAtIssuance.ShouldBe(user.SecurityStamp);
        newRow.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Refresh_UnknownToken_Rejects()
    {
        var h = Build();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "does-not-exist" });

        result.Value!.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Rejects()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        Seed(h.Db, user, "plain-1", user.SecurityStamp, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-1" });

        result.Value!.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_DisabledUser_RevokesTokenAndRejects()
    {
        var h = Build();
        var user = User(enabled: false);
        // SecurityStampAtIssuance must match, or the stamp check (which runs before the
        // enabled check) would also legitimately reject it — isolate the assertion.
        Seed(h.Db, user, "plain-1", user.SecurityStamp);
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-1" });

        result.Value!.Success.ShouldBeFalse();
        (await h.Db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
    }

    // ---- RefreshAsync: SEC-1 security-stamp check ------------------------------

    [Fact]
    public async Task Refresh_StaleSecurityStamp_RejectsAndRevokesToken()
    {
        // The user's password was reset (rotating SecurityStamp) after this token was issued.
        var h = Build();
        var user = User(stamp: "current-stamp");
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        Seed(h.Db, user, "plain-1", securityStampAtIssuance: "old-stamp-before-reset");
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-1" });

        result.Value!.Success.ShouldBeFalse();
        (await h.Db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refresh_NullSecurityStampAtIssuance_TreatedAsMismatch()
    {
        // Pre-SEC-1 rows have no stamp recorded — must not be treated as an automatic match.
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        Seed(h.Db, user, "plain-1", securityStampAtIssuance: null);
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-1" });

        result.Value!.Success.ShouldBeFalse();
    }

    // ---- RefreshAsync: two requests rotating the same token (REL-8) ----

    [Fact]
    public async Task Refresh_SameTokenRotatedByTwoRequestsAtOnce_OnlyOneSucceeds()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var first = Build(databaseName);
        var second = Build(databaseName);
        var user = User();
        Seed(first.Db, user, "plain-1", user.SecurityStamp);
        await first.Db.SaveChangesAsync();

        // The second request has already read the token when the first one rotates it.
        await second.Db.RefreshTokens.Include(rt => rt.User).SingleAsync();
        var request = new RefreshTokenRequestDto { RefreshToken = "plain-1" };

        var firstResult = await first.Svc.RefreshAsync(request);
        var secondResult = await second.Svc.RefreshAsync(request);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.IsSuccess.ShouldBeFalse();
        secondResult.StatusCode.ShouldBe(401);
        // The loser hands out no token pair. (InMemory keeps the row the losing save added, SQL Server
        // rolls the whole save back, so the stored rows are not asserted here.)
        secondResult.Value!.RefreshToken.ShouldBeNull();
        secondResult.Value.Token.ShouldBeNull();
    }

    // ---- RefreshAsync: SEC-1 reuse detection -----------------------------------

    [Fact]
    public async Task Refresh_ReusedRevokedToken_RevokesDescendantChain()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);

        // Simulate: token A was legitimately rotated to B, and B to C (both still active).
        // An attacker now replays the already-spent token A.
        var c = Seed(h.Db, user, "plain-c", user.SecurityStamp);
        var b = Seed(h.Db, user, "plain-b", user.SecurityStamp, revokedAt: DateTimeOffset.UtcNow, replacedByTokenId: c.Id);
        Seed(h.Db, user, "plain-a", user.SecurityStamp, revokedAt: DateTimeOffset.UtcNow, replacedByTokenId: b.Id);
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-a" });

        result.Value!.Success.ShouldBeFalse();
        // B was already revoked (legitimate rotation); C must now be revoked too (chain severed).
        (await h.Db.RefreshTokens.SingleAsync(rt => rt.Id == c.Id)).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refresh_ReusedRevokedTokenWithNoDescendant_RejectsWithoutThrowing()
    {
        // A token that was revoked directly (e.g. by the disabled-user or stale-stamp guards)
        // rather than via rotation has no ReplacedByTokenId — the chain walk must handle that.
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        Seed(h.Db, user, "plain-a", user.SecurityStamp, revokedAt: DateTimeOffset.UtcNow, replacedByTokenId: null);
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.RefreshAsync(new RefreshTokenRequestDto { RefreshToken = "plain-a" });

        result.Value!.Success.ShouldBeFalse();
    }

    // ---- ResetPasswordAsync: SEC-1 bulk revoke ---------------------------------

    [Fact]
    public async Task ResetPassword_Success_RevokesAllActiveRefreshTokensForThatUser()
    {
        // Also proves ExecuteUpdateAsync behaves correctly against the EF InMemory provider
        // with real matching rows (not just an empty result set).
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        h.Um.ResetPasswordAsync(user, "reset-token", "NewPassword1!").Returns(IdentityResult.Success);
        Seed(h.Db, user, "plain-1", user.SecurityStamp);
        Seed(h.Db, user, "plain-2", user.SecurityStamp);
        var otherUser = User(id: "u2");
        Seed(h.Db, otherUser, "plain-other", otherUser.SecurityStamp);
        await h.Db.SaveChangesAsync();

        var result = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email!,
            Token = "reset-token",
            NewPassword = "NewPassword1!",
            ConfirmPassword = "NewPassword1!"
        });

        result.Value!.Success.ShouldBeTrue();
        (await h.Db.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync())
            .ShouldAllBe(rt => rt.RevokedAt != null);
        (await h.Db.RefreshTokens.SingleAsync(rt => rt.UserId == otherUser.Id)).RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ResetPassword_ATokenIsRotatedByARefreshWhileRevoking_StillSucceedsAndRevokesTheRest()
    {
        // RevokedAt is a concurrency token (REL-8): a refresh that rotates one of the tokens between the
        // reset's read and its save must not turn the already-changed password into a 500.
        var databaseName = Guid.NewGuid().ToString("N");
        var reset = Build(databaseName);
        var refresh = Build(databaseName);
        var user = User();
        reset.Um.FindByEmailAsync(user.Email!).Returns(user);
        reset.Um.ResetPasswordAsync(user, "reset-token", "NewPassword1!").Returns(IdentityResult.Success);
        Seed(reset.Db, user, "plain-1", user.SecurityStamp);
        Seed(reset.Db, user, "plain-2", user.SecurityStamp);
        await reset.Db.SaveChangesAsync();

        var raced = false;
        reset.Db.SavingChanges += (_, _) =>
        {
            if (raced)
                return;
            raced = true;
            var rotated = refresh.Db.RefreshTokens.OrderBy(rt => rt.TokenHash).First();
            rotated.RevokedAt = DateTimeOffset.UtcNow;
            refresh.Db.SaveChanges();
        };

        var result = await reset.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email!, Token = "reset-token", NewPassword = "NewPassword1!", ConfirmPassword = "NewPassword1!"
        });

        raced.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
        using var check = DbContextFactory.CreateInMemory(databaseName);
        (await check.RefreshTokens.ToListAsync()).ShouldAllBe(rt => rt.RevokedAt != null);
    }

    [Theory]
    [InlineData("PasswordTooShort")]
    [InlineData("PasswordRequiresDigit")]
    [InlineData("PasswordRequiresLower")]
    [InlineData(CommonPasswordValidator.ErrorCode)]
    public async Task ResetPassword_PasswordRuleError_IsReturnedSoThePageCanShowIt(string errorCode)
    {
        // The reset token is checked before the new password, so a password error means the link was valid.
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        h.Um.ResetPasswordAsync(user, "reset-token", "weak")
            .Returns(IdentityResult.Failed(new IdentityError { Code = errorCode, Description = "Password rule: " + errorCode }));

        var result = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email!, Token = "reset-token", NewPassword = "weak", ConfirmPassword = "weak"
        });

        result.IsSuccess.ShouldBeFalse();
        result.Value!.Errors.ShouldBe(["Password rule: " + errorCode]);
    }

    [Fact]
    public async Task ResetPassword_InvalidTokenAlongsideAPasswordError_StaysGeneric()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        h.Um.ResetPasswordAsync(user, "bad-token", "weak").Returns(IdentityResult.Failed(
            new IdentityError { Code = "InvalidToken", Description = "Invalid token." },
            new IdentityError { Code = "PasswordTooShort", Description = "Too short." }));

        var result = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email!, Token = "bad-token", NewPassword = "weak", ConfirmPassword = "weak"
        });

        result.Value!.Message.ShouldBe(ErrorMessagesRes.InvalidRequest);
        result.Value.Errors.ShouldBeNull();
    }

    [Fact]
    public async Task ResetPassword_Failure_DoesNotRevokeTokens()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync(user.Email!).Returns(user);
        h.Um.ResetPasswordAsync(user, "bad-token", "x")
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Invalid token." }));
        Seed(h.Db, user, "plain-1", user.SecurityStamp);
        await h.Db.SaveChangesAsync();

        await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = user.Email!,
            Token = "bad-token",
            NewPassword = "x",
            ConfirmPassword = "x"
        });

        (await h.Db.RefreshTokens.SingleAsync()).RevokedAt.ShouldBeNull();
    }
}
