using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class AuthApiServiceTests
{
    private sealed record Harness(AuthApiService Svc, UserManager<ApplicationUser> Um, SignInManager<ApplicationUser> Sm, ApplicationDbContext Db, ITokenService Tok, IEmailService Email);

    private static Harness Build()
    {
        var um = IdentityMocks.MockUserManager();
        um.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        var sm = IdentityMocks.MockSignInManager(um);
        var tok = Substitute.For<ITokenService>();
        tok.GenerateAccessTokenAsync(Arg.Any<ApplicationUser>())
            .Returns(Task.FromResult(("access-token", DateTime.UtcNow.AddMinutes(15))));
        tok.GenerateRefreshToken().Returns(_ => "new-plain-refresh-" + Guid.NewGuid().ToString("N"));
        var subs = Substitute.For<ISubscriptionService>();
        var email = Substitute.For<IEmailService>();
        var db = DbContextFactory.CreateInMemory();

        var svc = new AuthApiService(um, sm, tok, subs, NullLogger<AuthApiService>.Instance, db, email);
        return new Harness(svc, um, sm, db, tok, email);
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

        var result = await h.Svc.RegisterAsync(RegisterRequest(), (userId, token) => $"https://x/confirm?userId={userId}&token={token}");

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_EmailSendSucceeds_ReturnsSuccess()
    {
        var h = Build();
        h.Um.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        h.Um.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns("token");

        var result = await h.Svc.RegisterAsync(RegisterRequest(), (userId, token) => $"https://x/confirm?userId={userId}&token={token}");

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

        await h.Svc.RegisterAsync(RegisterRequest(), (userId, token) => $"https://x/confirm?userId={userId}&token={token}");

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

        await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = member.Email! }, (email, token) => $"https://x/reset?token={token}");

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

        await h.Svc.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = member.Email! }, (email, token) => $"https://x/reset?token={token}");

        await h.Email.Received(1).SendPasswordResetAsync(
            member.Email!, Arg.Any<string>(), UserLanguage.English, Arg.Any<CancellationToken>());
        await h.Um.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
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
        await h.Svc.RegisterAsync(request, (userId, token) => $"https://x/confirm?userId={userId}&token={token}");

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
        await h.Svc.RegisterAsync(request, (userId, token) => $"https://x/confirm?userId={userId}&token={token}");

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
        await h.Svc.RegisterAsync(request, (userId, token) => $"https://x/confirm?userId={userId}&token={token}");

        createdUser!.Goal.ShouldBe(FitnessGoal.Strength);
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
