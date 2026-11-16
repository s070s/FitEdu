using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using FitEdu.Application.ApiServices;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class AuthServiceTests
{
    private sealed record Harness(
        AuthService Svc,
        UserManager<ApplicationUser> Um,
        SignInManager<ApplicationUser> Sm,
        ISubscriptionService Subs,
        IEmailService Email,
        ITokenService Tokens,
        EphemeralDataProtectionProvider Dp,
        IHttpContextAccessor Accessor,
        RecordingLogger<AuthService> Log);

    private static Harness Build(IAuthAttemptLimiter? attemptLimiter = null, CircuitClientAddress? clientAddress = null)
    {
        var um = IdentityMocks.MockUserManager();
        um.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        um.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        var sm = IdentityMocks.MockSignInManager(um);
        var subs = Substitute.For<ISubscriptionService>();
        var email = Substitute.For<IEmailService>();
        var tokens = Substitute.For<ITokenService>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        var dp = new EphemeralDataProtectionProvider();
        var ctx = DbContextFactory.CreateInMemory();
        var log = new RecordingLogger<AuthService>();
        // The register, forgot and reset flows live in AuthApiService; the real one runs behind AuthService.
        var authApi = new AuthApiService(um, sm, tokens, subs, new RecordingLogger<AuthApiService>(), ctx, email,
            Options.Create(new AppOptions { BaseUrl = "https://site.test" }), TimeProvider.System,
            new MemoryCache(new MemoryCacheOptions()));

        var svc = new AuthService(accessor, um, sm, authApi, ctx, dp, tokens,
            attemptLimiter ?? new AuthAttemptLimiter(), clientAddress ?? new CircuitClientAddress(), log);
        return new Harness(svc, um, sm, subs, email, tokens, dp, accessor, log);
    }

    private static ApplicationUser User(bool enabled = true) =>
        new() { Id = "u1", Email = "u@x.com", UserName = "u@x.com", FirstName = "F", LastName = "L", IsEnabled = enabled };

    private static LoginRequestDto Login() => new() { Email = "u@x.com", Password = "Password1" };

    // ---- LoginAsync ----------------------------------------------------------

    [Fact]
    public async Task Login_UnknownEmail_Fails()
    {
        var h = Build();
        h.Um.FindByEmailAsync("u@x.com").Returns((ApplicationUser?)null);

        var result = await h.Svc.LoginAsync(Login());

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe("Incorrect email or password.");
    }

    [Fact]
    public async Task Login_EmailNotConfirmed_Fails()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(false);
        h.Um.CheckPasswordAsync(user, "Password1").Returns(true);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe("Email not confirmed.");
    }

    [Fact]
    public async Task Login_EmailNotConfirmedWrongPassword_ReturnsInvalidCredentials()
    {
        // "Not confirmed" is only told to someone who knows the password, so a guess cannot find unconfirmed accounts.
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(false);
        h.Um.CheckPasswordAsync(user, "Password1").Returns(false);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);
    }

    private static ApplicationUser UnconfirmedUserWithLockout(Harness h, bool lockoutEnabled = true)
    {
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(false);
        h.Um.SupportsUserLockout.Returns(true);
        h.Um.GetLockoutEnabledAsync(user).Returns(lockoutEnabled);
        return user;
    }

    [Fact]
    public async Task Login_EmailNotConfirmedWrongPassword_CountsAsAFailedAttempt()
    {
        var h = Build();
        var user = UnconfirmedUserWithLockout(h);
        h.Um.CheckPasswordAsync(user, "Password1").Returns(false);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);

        await h.Um.Received(1).AccessFailedAsync(user);
    }

    [Fact]
    public async Task Login_EmailNotConfirmedWrongPasswordThatLocksTheAccount_AnswersAccountLocked()
    {
        var h = Build();
        var user = UnconfirmedUserWithLockout(h);
        h.Um.CheckPasswordAsync(user, "Password1").Returns(false);
        h.Um.IsLockedOutAsync(user).Returns(false, true);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe(ErrorMessagesRes.AccountLocked);
    }

    [Fact]
    public async Task Login_EmailNotConfirmedAndLockedOut_AnswersAccountLockedWithoutCheckingThePassword()
    {
        var h = Build();
        var user = UnconfirmedUserWithLockout(h);
        h.Um.IsLockedOutAsync(user).Returns(true);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe(ErrorMessagesRes.AccountLocked);

        await h.Um.DidNotReceiveWithAnyArgs().CheckPasswordAsync(default!, default!);
        await h.Um.DidNotReceiveWithAnyArgs().AccessFailedAsync(default!);
    }

    [Fact]
    public async Task Login_EmailNotConfirmedRightPassword_IsNotAFailedAttempt()
    {
        var h = Build();
        var user = UnconfirmedUserWithLockout(h);
        h.Um.CheckPasswordAsync(user, "Password1").Returns(true);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe(ErrorMessagesRes.EmailNotConfirmedError);

        await h.Um.DidNotReceiveWithAnyArgs().AccessFailedAsync(default!);
    }

    [Fact]
    public async Task Login_EmailNotConfirmedWrongPasswordWithLockoutDisabledForTheUser_IsNotCounted()
    {
        var h = Build();
        var user = UnconfirmedUserWithLockout(h, lockoutEnabled: false);
        h.Um.CheckPasswordAsync(user, "Password1").Returns(false);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);

        await h.Um.DidNotReceiveWithAnyArgs().AccessFailedAsync(default!);
    }

    [Fact]
    public async Task Login_DisabledAccount_Fails()
    {
        var h = Build();
        var user = User(enabled: false);
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.Success);

        (await h.Svc.LoginAsync(Login())).Message!.ShouldContain("disabled");
    }

    [Fact]
    public async Task Login_DisabledAccountWrongPassword_ReturnsInvalidCredentials()
    {
        var h = Build();
        var user = User(enabled: false);
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.Failed);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_OverLimit_RejectsWithoutCheckingPassword()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(false);
        var h = Build(limiter);

        var result = await h.Svc.LoginAsync(Login());

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(ErrorMessagesRes.TooManyAttempts);
        await h.Um.DidNotReceiveWithAnyArgs().FindByEmailAsync(default!);
        await h.Sm.DidNotReceiveWithAnyArgs().CheckPasswordSignInAsync(default!, default!, default);
    }

    [Fact]
    public async Task Register_OverLimit_RejectsWithoutCreatingAnAccount()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(false);
        var h = Build(limiter);

        var result = await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com", Password = "Password1", ConfirmPassword = "Password1",
            FirstName = "F", LastName = "L", GdprConsent = true
        });

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(ErrorMessagesRes.TooManyAttempts);
        await h.Um.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!);
    }

    [Fact]
    public async Task ForgotPassword_OverLimit_RejectsWithoutSendingAnEmail()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(false);
        var h = Build(limiter);

        var result = await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "u@x.com" });

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(ErrorMessagesRes.TooManyAttempts);
        await h.Email.DidNotReceiveWithAnyArgs().SendPasswordResetAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Login_AttemptsAreCountedPerEmailIgnoringCaseAndSpaces()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(true);
        var h = Build(limiter);
        h.Um.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        await h.Svc.LoginAsync(new LoginRequestDto { Email = "  U@X.com ", Password = "Password1" });

        limiter.Received(1).TryAcquire("U@X.COM", Arg.Any<string?>());
    }

    [Fact]
    public async Task Login_AttemptsAreCountedPerClientAddressOfTheCircuit()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(true);
        var h = Build(limiter, new CircuitClientAddress { Key = "203.0.113.7" });
        h.Um.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        await h.Svc.LoginAsync(Login());

        limiter.Received(1).TryAcquire("U@X.COM", "203.0.113.7");
    }

    [Fact]
    public async Task Login_CircuitWithoutAClientAddress_CountsAsTheUnknownAddress()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(true);
        var h = Build(limiter);
        h.Um.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        await h.Svc.LoginAsync(Login());

        limiter.Received(1).TryAcquire("U@X.COM", null);
    }

    [Fact]
    public async Task Login_Branches_NeverLogTheEmail()
    {
        const string privateEmail = "private.person@secret-mail.test";
        var h = Build();
        var user = User(enabled: false);
        user.Email = privateEmail;
        var login = new LoginRequestDto { Email = privateEmail, Password = "Password1" };

        h.Um.FindByEmailAsync(privateEmail).Returns((ApplicationUser?)null);
        await h.Svc.LoginAsync(login);

        h.Um.FindByEmailAsync(privateEmail).Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Um.GetSecurityStampAsync(user).Returns("stamp-1");
        h.Sm.CheckPasswordSignInAsync(user, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Success);
        await h.Svc.LoginAsync(login); // disabled, right password
        user.IsEnabled = true;
        await h.Svc.LoginAsync(login); // signed in

        var logged = h.Log.Information.Concat(h.Log.Warnings).Concat(h.Log.Errors).ToList();
        logged.ShouldNotBeEmpty();
        logged.ShouldAllBe(entry => !entry.Contains(privateEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_BadPassword_Fails()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.Failed);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe("Incorrect email or password.");
    }

    [Fact]
    public async Task Login_LockedOut_Fails()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.LockedOut);

        (await h.Svc.LoginAsync(Login())).Message.ShouldBe("Account locked. Try again later.");
    }

    [Fact]
    public async Task Login_Success_ReturnsProtectedTicketEncodingUserId()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Um.GetSecurityStampAsync(user).Returns("stamp-1");
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.Success);

        var result = await h.Svc.LoginAsync(Login());

        result.Success.ShouldBeTrue();
        result.Token.ShouldNotBeNull();
        var payload = h.Dp.CreateProtector(AuthService.ProtectorPurpose).ToTimeLimitedDataProtector().Unprotect(result.Token!);
        var ticket = System.Text.Json.JsonSerializer.Deserialize<AuthCompletionTicket>(payload);
        ticket!.UserId.ShouldBe("u1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_Success_EncodesRememberMeInToken(bool rememberMe)
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Um.GetSecurityStampAsync(user).Returns("stamp-1");
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.Success);

        var request = Login();
        request.RememberMe = rememberMe;
        var result = await h.Svc.LoginAsync(request);

        var payload = h.Dp.CreateProtector(AuthService.ProtectorPurpose).ToTimeLimitedDataProtector().Unprotect(result.Token!);
        var ticket = System.Text.Json.JsonSerializer.Deserialize<AuthCompletionTicket>(payload);
        ticket!.RememberMe.ShouldBe(rememberMe);
    }

    [Fact]
    public async Task Login_OnTheGreekSite_SavesGreekAsTheStoredLanguage()
    {
        var h = Build();
        var user = User(); // stored language: English (the default)
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Um.GetSecurityStampAsync(user).Returns("stamp-1");
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.Success);
        using var cultureScope = new CultureScope("el-GR");

        (await h.Svc.LoginAsync(Login())).Success.ShouldBeTrue();

        user.PreferredLanguage.ShouldBe(UserLanguage.Greek);
        await h.Um.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task Login_BadPassword_KeepsTheStoredLanguage()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Sm.CheckPasswordSignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(SignInResult.Failed);
        using var cultureScope = new CultureScope("el-GR");

        await h.Svc.LoginAsync(Login());

        user.PreferredLanguage.ShouldBe(UserLanguage.English);
        await h.Um.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    // ---- RegisterAsync -------------------------------------------------------

    [Fact]
    public async Task Register_Success_AssignsRolePlanAndReturnsMessage()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Student").Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        var result = await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            FirstName = "F",
            LastName = "L",
            DateOfBirth = new DateTime(1998, 4, 12),
            GdprConsent = true
        });

        result.Success.ShouldBeTrue();
        result.Message.ShouldBe(SuccessMessagesRes.RegistrationSuccessfulCheckEmail);
        await h.Subs.Received().AssignFreePlanAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Register_Success_SendsTheConfirmationEmail()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            FirstName = "F",
            LastName = "L",
            DateOfBirth = new DateTime(1998, 4, 12),
            GdprConsent = true
        });

        await h.Email.Received(1).SendEmailConfirmationAsync("u@x.com", Arg.Is<string>(link => link.StartsWith("https://site.test/confirm-email?userId=")),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_DuplicateEmail_StillTellsThePageTheEmailIsTaken()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(
            IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "Email 'u@x.com' is already taken." }));

        var result = await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            FirstName = "F",
            LastName = "L",
            DateOfBirth = new DateTime(1998, 4, 12),
            GdprConsent = true
        });

        result.Success.ShouldBeFalse();
        result.Errors!.ShouldContain("Email 'u@x.com' is already taken.");
    }

    [Fact]
    public async Task Register_CreateFails_ReturnsErrors()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));

        var result = await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com",
            Password = "weak",
            ConfirmPassword = "weak",
            FirstName = "F",
            LastName = "L",
            DateOfBirth = new DateTime(1998, 4, 12),
            GdprConsent = true
        });

        result.Success.ShouldBeFalse();
        result.Errors!.ShouldContain("Password too weak");
    }

    [Fact]
    public async Task Register_WithAdvancedStatus_AssignsAdvancedStatusToCreatedUser()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Student").Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            FirstName = "F",
            LastName = "L",
            DateOfBirth = new DateTime(1998, 4, 12),
            GdprConsent = true,
            Status = UserStatus.Advanced
        });

        createdUser!.Status.ShouldBe(UserStatus.Advanced);
    }

    [Fact]
    public async Task Register_WithUnknownStatus_CoercesToBeginner()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Student").Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            FirstName = "F",
            LastName = "L",
            DateOfBirth = new DateTime(1998, 4, 12),
            GdprConsent = true,
            Status = UserStatus.Unknown
        });

        createdUser!.Status.ShouldBe(UserStatus.Beginner);
    }

    [Fact]
    public async Task Register_WithGoal_SavesGoalOnCreatedUser()
    {
        var h = Build();
        ApplicationUser? createdUser = null;
        h.Um.CreateAsync(Arg.Do<ApplicationUser>(u => createdUser = u), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Student").Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        await h.Svc.RegisterAsync(new RegisterRequestDto
        {
            Email = "u@x.com",
            Password = "Password1",
            ConfirmPassword = "Password1",
            FirstName = "F",
            LastName = "L",
            DateOfBirth = new DateTime(1998, 4, 12),
            GdprConsent = true,
            Goal = FitnessGoal.Mobility
        });

        createdUser!.Goal.ShouldBe(FitnessGoal.Mobility);
    }

    // ---- Forgot / Reset ------------------------------------------------------

    [Fact]
    public async Task ForgotPassword_UnknownUser_ReturnsGenericSuccess()
    {
        var h = Build();
        h.Um.FindByEmailAsync("u@x.com").Returns((ApplicationUser?)null);

        var result = await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "u@x.com" });

        result.Success.ShouldBeTrue();
        result.Message!.ShouldContain("If an account uses that email");
    }

    [Fact]
    public async Task ForgotPassword_ConfirmedUser_SendsResetEmail()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Um.GeneratePasswordResetTokenAsync(user).Returns("reset-token");

        var result = await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "u@x.com" });

        result.Success.ShouldBeTrue();
        await h.Email.Received(1).SendPasswordResetAsync("u@x.com", "https://site.test/reset-password?email=u%40x.com&token=reset-token",
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendConfirmation_UnconfirmedUser_SendsTheConfirmationLink()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(false);
        h.Um.GenerateEmailConfirmationTokenAsync(user).Returns("confirm-token");

        var result = await h.Svc.ResendConfirmationAsync("u@x.com");

        result.Success.ShouldBeTrue();
        result.Message.ShouldBe(SuccessMessagesRes.ConfirmationEmailResent);
        await h.Email.Received(1).SendEmailConfirmationAsync("u@x.com", Arg.Is<string>(link => link.Contains("confirm-token", StringComparison.Ordinal)), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendConfirmation_OverLimit_RejectsWithoutSendingAnEmail()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(false);
        var h = Build(limiter);

        var result = await h.Svc.ResendConfirmationAsync("u@x.com");

        result.Success.ShouldBeFalse();
        result.Failure.ShouldBe(AuthFailure.TooManyAttempts);
        await h.Um.DidNotReceiveWithAnyArgs().FindByEmailAsync(default!);
        await h.Email.DidNotReceiveWithAnyArgs().SendEmailConfirmationAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ResetPassword_KnownUserBadToken_ReturnsSameGenericMessageAsUnknownUser()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.FindByEmailAsync("nobody@x.com").Returns((ApplicationUser?)null);
        h.Um.ResetPasswordAsync(user, "bad-token", Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));

        var knownUser = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = "u@x.com", Token = "bad-token", NewPassword = "Password1", ConfirmPassword = "Password1"
        });
        var unknownUser = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = "nobody@x.com", Token = "bad-token", NewPassword = "Password1", ConfirmPassword = "Password1"
        });

        knownUser.Success.ShouldBeFalse();
        knownUser.Message.ShouldBe(unknownUser.Message);
        knownUser.Errors.ShouldBeNull();
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_Fails()
    {
        var h = Build();
        h.Um.FindByEmailAsync("u@x.com").Returns((ApplicationUser?)null);

        var result = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = "u@x.com",
            Token = "t",
            NewPassword = "Password1",
            ConfirmPassword = "Password1"
        });

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe("Invalid request.");
    }

    [Fact]
    public async Task ResetPassword_Success()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.ResetPasswordAsync(user, "t", "Password1").Returns(IdentityResult.Success);

        var result = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = "u@x.com",
            Token = "t",
            NewPassword = "Password1",
            ConfirmPassword = "Password1"
        });

        result.Success.ShouldBeTrue();
    }

    // ---- Every refusal names its reason in Failure, so a page never compares translated text (P02-18) ----

    [Fact]
    public async Task Failure_LoginUnknownEmailOrWrongPassword_IsInvalidCredentials()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Sm.CheckPasswordSignInAsync(user, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Failed);
        h.Um.FindByEmailAsync("nobody@x.com").Returns((ApplicationUser?)null);

        (await h.Svc.LoginAsync(Login())).Failure.ShouldBe(AuthFailure.InvalidCredentials);
        (await h.Svc.LoginAsync(new LoginRequestDto { Email = "nobody@x.com", Password = "Password1" })).Failure
            .ShouldBe(AuthFailure.InvalidCredentials);
    }

    [Fact]
    public async Task Failure_LoginUnconfirmedAccount_IsEmailNotConfirmedOnlyForTheRightPassword()
    {
        var h = Build();
        var user = UnconfirmedUserWithLockout(h);
        h.Um.CheckPasswordAsync(user, "Password1").Returns(true);
        h.Um.CheckPasswordAsync(user, "Wrong-Pass-9").Returns(false);

        (await h.Svc.LoginAsync(Login())).Failure.ShouldBe(AuthFailure.EmailNotConfirmed);
        (await h.Svc.LoginAsync(new LoginRequestDto { Email = "u@x.com", Password = "Wrong-Pass-9" })).Failure
            .ShouldBe(AuthFailure.InvalidCredentials);
    }

    [Fact]
    public async Task Failure_LoginLockedOut_IsAccountLocked_ForConfirmedAndUnconfirmedAccounts()
    {
        var unconfirmed = Build();
        var unconfirmedUser = UnconfirmedUserWithLockout(unconfirmed);
        unconfirmed.Um.IsLockedOutAsync(unconfirmedUser).Returns(true);
        var confirmed = Build();
        var confirmedUser = User();
        confirmed.Um.FindByEmailAsync("u@x.com").Returns(confirmedUser);
        confirmed.Um.IsEmailConfirmedAsync(confirmedUser).Returns(true);
        confirmed.Sm.CheckPasswordSignInAsync(confirmedUser, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.LockedOut);

        (await unconfirmed.Svc.LoginAsync(Login())).Failure.ShouldBe(AuthFailure.AccountLocked);
        (await confirmed.Svc.LoginAsync(Login())).Failure.ShouldBe(AuthFailure.AccountLocked);
    }

    [Fact]
    public async Task Failure_LoginDisabledAccountRightPassword_IsAccountDisabled()
    {
        var h = Build();
        var user = User(enabled: false);
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.IsEmailConfirmedAsync(user).Returns(true);
        h.Sm.CheckPasswordSignInAsync(user, Arg.Any<string>(), Arg.Any<bool>()).Returns(SignInResult.Success);

        (await h.Svc.LoginAsync(Login())).Failure.ShouldBe(AuthFailure.AccountDisabled);
    }

    [Fact]
    public async Task Failure_OverTheAttemptLimit_IsTooManyAttempts_OnLoginRegisterAndForgot()
    {
        var limiter = Substitute.For<IAuthAttemptLimiter>();
        limiter.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(false);
        var h = Build(limiter);

        (await h.Svc.LoginAsync(Login())).Failure.ShouldBe(AuthFailure.TooManyAttempts);
        (await h.Svc.RegisterAsync(new RegisterRequestDto { Email = "u@x.com" })).Failure.ShouldBe(AuthFailure.TooManyAttempts);
        (await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "u@x.com" })).Failure.ShouldBe(AuthFailure.TooManyAttempts);
    }

    [Fact]
    public async Task Failure_ResetPasswordUnknownEmailOrRejectedToken_IsLinkInvalid()
    {
        var h = Build();
        var user = User();
        h.Um.FindByEmailAsync("u@x.com").Returns(user);
        h.Um.FindByEmailAsync("nobody@x.com").Returns((ApplicationUser?)null);
        h.Um.ResetPasswordAsync(user, "bad-token", Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));

        var known = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto { Email = "u@x.com", Token = "bad-token", NewPassword = "Password1", ConfirmPassword = "Password1" });
        var unknown = await h.Svc.ResetPasswordAsync(new ResetPasswordRequestDto { Email = "nobody@x.com", Token = "bad-token", NewPassword = "Password1", ConfirmPassword = "Password1" });

        known.Failure.ShouldBe(AuthFailure.LinkInvalid);
        unknown.Failure.ShouldBe(AuthFailure.LinkInvalid);
    }

    // ---- Misc ----------------------------------------------------------------

    [Fact]
    public async Task Logout_ReturnsSignoutUrl()
    {
        (await Build().Svc.LogoutAsync()).ShouldBe("/auth/signout?returnUrl=%2F");
    }

    private static ClaimsPrincipal AdminPrincipal() =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "admin-1"), new Claim(ClaimTypes.Role, "Admin")], "cookie"));

    [Fact]
    public async Task Impersonate_TargetMissing_Fails()
    {
        var h = Build();
        h.Um.FindByIdAsync("target").Returns((ApplicationUser?)null);

        var result = await h.Svc.ImpersonateAsync(AdminPrincipal(), "target");

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(ErrorMessagesRes.UserNotFound);
    }

    [Theory]
    [InlineData("en-GB", "Only member accounts can be impersonated.")]
    [InlineData("el-GR", "Η σύνδεση ως χρήστης επιτρέπεται μόνο σε λογαριασμούς μελών.")]
    public async Task Impersonate_NonStudent_MessageFollowsCulture(string culture, string expected)
    {
        var h = Build();
        var target = User();
        h.Um.FindByIdAsync("u1").Returns(target);
        h.Um.GetRolesAsync(target).Returns(["Admin"]);
        using var cultureScope = new CultureScope(culture);

        var result = await h.Svc.ImpersonateAsync(AdminPrincipal(), "u1");

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(expected);
    }

    [Fact]
    public async Task Impersonate_CallerIsNotAnAdmin_Fails()
    {
        var h = Build();
        var target = User();
        h.Um.FindByIdAsync("u1").Returns(target);
        h.Um.GetRolesAsync(target).Returns(["Student"]);
        var student = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "u2"), new Claim(ClaimTypes.Role, "Student")], "cookie"));

        var result = await h.Svc.ImpersonateAsync(student, "u1");

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(ErrorMessagesRes.AccessDenied);
    }

    [Fact]
    public async Task Impersonate_NobodySignedIn_ReportsNotSignedIn()
    {
        var h = Build();

        var result = await h.Svc.ImpersonateAsync(new ClaimsPrincipal(new ClaimsIdentity()), "u1");

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(ErrorMessagesRes.NotAuthenticated);
    }

    [Fact]
    public async Task Impersonate_DisabledTarget_Fails()
    {
        var h = Build();
        var target = User(enabled: false);
        h.Um.FindByIdAsync("u1").Returns(target);
        h.Um.GetRolesAsync(target).Returns(["Student"]);

        var result = await h.Svc.ImpersonateAsync(AdminPrincipal(), "u1");

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(ErrorMessagesRes.AccountDisabled);
    }

    [Fact]
    public async Task Impersonate_Student_IssuesASessionOnlyTicketCarryingTheAdmin()
    {
        var h = Build();
        var target = User();
        h.Um.FindByIdAsync("u1").Returns(target);
        h.Um.GetRolesAsync(target).Returns(["Student"]);
        h.Um.GetSecurityStampAsync(target).Returns("stamp-1");

        var result = await h.Svc.ImpersonateAsync(AdminPrincipal(), "u1");

        result.Success.ShouldBeTrue();
        var payload = h.Dp.CreateProtector(AuthService.ProtectorPurpose).ToTimeLimitedDataProtector().Unprotect(result.Token!);
        var ticket = System.Text.Json.JsonSerializer.Deserialize<AuthCompletionTicket>(payload)!;
        ticket.UserId.ShouldBe("u1");
        ticket.AdminBackupId.ShouldBe("admin-1");
        ticket.RememberMe.ShouldBeFalse();
    }

    // ---- GetTokenAsync: signed by the one token service ----

    [Fact]
    public async Task GetToken_AuthenticatedPrincipal_IsSignedByTheTokenService()
    {
        var h = Build();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "cookie"));
        h.Tokens.SignClaims(Arg.Any<IEnumerable<Claim>>()).Returns("signed-token");

        (await h.Svc.GetTokenAsync(principal)).ShouldBe("signed-token");
        h.Tokens.Received(1).SignClaims(Arg.Is<IEnumerable<Claim>>(claims => claims.Any(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "u1")));
    }

    [Fact]
    public async Task GetToken_AnonymousPrincipal_ReturnsNull()
    {
        var h = Build();

        (await h.Svc.GetTokenAsync(new ClaimsPrincipal(new ClaimsIdentity()))).ShouldBeNull();
        h.Tokens.DidNotReceiveWithAnyArgs().SignClaims(default!);
    }

    // ---- CreateLessonAssetTokenAsync (SEC-2) ----------------------------------

    [Fact]
    public async Task CreateLessonAssetToken_EncodesUserAndLesson()
    {
        var h = Build();
        var lessonId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));

        var token = await h.Svc.CreateLessonAssetTokenAsync(principal, lessonId);

        var payload = h.Dp.CreateProtector(AuthService.LessonAssetProtectorPurpose)
            .ToTimeLimitedDataProtector().Unprotect(token);
        var ticket = System.Text.Json.JsonSerializer.Deserialize<LessonAssetTicket>(payload);
        ticket!.UserId.ShouldBe("u1");
        ticket.LessonId.ShouldBe(lessonId);
    }

    [Fact]
    public async Task CreateLessonAssetToken_UnauthenticatedPrincipal_Throws()
    {
        var h = Build();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Svc.CreateLessonAssetTokenAsync(principal, Guid.NewGuid()));
    }
}
