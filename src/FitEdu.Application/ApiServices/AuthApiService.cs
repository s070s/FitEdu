using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources.Messages;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FitEdu.Application.ApiServices;

/// <summary>
/// JSON API authentication backing <c>AuthController</c>.
/// </summary>
public partial class AuthApiService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService,
    ISubscriptionService subscriptionService,
    ILogger<AuthApiService> logger,
    IApplicationDbContext context,
    IEmailService emailService,
    IOptions<AppOptions> appOptions,
    TimeProvider timeProvider,
    IMemoryCache cache) : IAuthApiService
{
    private const string StudentRole = "Student";

    // Identity's password-rule errors (PasswordTooShort, PasswordRequiresDigit, ...) and our PasswordTooCommon.
    private const string PasswordErrorCodePrefix = "Password";

    // One email of each kind per account in this time, however often it is asked for, so the forgotten-password
    // and resend forms cannot be used to flood a member's inbox.
    private static readonly TimeSpan EmailCooldown = TimeSpan.FromMinutes(2);
    private const string ConfirmationEmailKind = "confirm";
    private const string ResetEmailKind = "reset";

    // Serialises the check-and-claim of an email slot, so a burst of parallel requests sends one email, not all.
    private static readonly Lock EmailSlotLock = new();

    /// <summary>
    /// Registration for the JSON API. A taken email is answered with the same generic error as any other
    /// failure, so the endpoint cannot be used to find out which emails have an account.
    /// </summary>
    public Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request) =>
        RegisterCoreAsync(request, revealTakenEmail: false);

    /// <summary>
    /// Registration for the sign-up page: the same flow, but a taken email is reported as such
    /// (owner decision D1: the page tells the visitor to use another email or log in).
    /// </summary>
    public Task<ServiceResult<AuthResponseDto>> RegisterFromSignUpPageAsync(RegisterRequestDto request) =>
        RegisterCoreAsync(request, revealTakenEmail: true);

    // The one registration flow behind both entry points: create the account, make it a Student on the
    // Free plan, email the confirmation link. Account, role and plan are all-or-nothing.
    private async Task<ServiceResult<AuthResponseDto>> RegisterCoreAsync(RegisterRequestDto request, bool revealTakenEmail)
    {
        var user = request.ToNewUser();

        var ageRefusal = CheckSignUpAge(user.DateOfBirth);
        if (ageRefusal is not null)
            return ServiceResult<AuthResponseDto>.BadRequest(ageRefusal);

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // The error codes, not the descriptions: a description can hold the email address.
            LogRegistrationFailed(logger, string.Join(", ", result.Errors.Select(e => e.Code)));

            // Duplicate-account errors become a generic message on the API to prevent account enumeration.
            // Password-policy errors (too short, no digits, etc.) are safe to surface verbatim.
            var safeErrors = result.Errors.Select(e =>
                !revealTakenEmail && e.Code is "DuplicateUserName" or "DuplicateEmail"
                    ? ErrorMessagesRes.RegistrationFailedGeneric
                    : e.Description);

            var emailTaken = revealTakenEmail && result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto
            {
                Success = false,
                Errors = safeErrors,
                Failure = emailTaken ? AuthFailure.EmailTaken : null
            });
        }

        var confirmToken = await TrySetUpNewAccountAsync(user);
        if (confirmToken is null)
        {
            // Half an account would be stuck: it could not register again (the email is taken) and
            // could not sign in. Remove it so the visitor can simply try again.
            await RemoveHalfCreatedAccountAsync(user);
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto
            {
                Success = false,
                Message = ErrorMessagesRes.RegistrationFailedGeneric
            });
        }

        LogUserRegistered(logger, user.Id);
        // The sign-up email starts the account's cooldown too, so a quick tap on "resend" does not send a second one.
        // The slot is free for a new account, so the result needs no check.
        TryClaimEmailSlot(ConfirmationEmailKind, user.Id);
        await SendConfirmationEmailAsync(user, confirmToken, user.PreferredLanguage);

        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto
        {
            Success = true,
            Message = SuccessMessagesRes.RegistrationSuccessfulCheckEmail
        });
    }

    // A sign-up needs a date of birth and an age of RegisterRequestDto.MinimumSignUpAge, counted on the members'
    // date. A date in the future is also too young, and comparing dates avoids age arithmetic on a far-future
    // year. Null when the date is fine, otherwise the refusal to return (no account is created).
    private AuthResponseDto? CheckSignUpAge(DateOnly? dateOfBirth)
    {
        if (dateOfBirth is null)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = ErrorMessagesRes.Auth_DateOfBirthRequired,
                Failure = AuthFailure.DateOfBirthRequired
            };
        }

        var latestAllowedBirthDate = SiteClock.Today(timeProvider).AddYears(-RegisterRequestDto.MinimumSignUpAge);
        if (dateOfBirth.Value <= latestAllowedBirthDate)
            return null;

        return new AuthResponseDto
        {
            Success = false,
            Message = ErrorMessagesRes.SignUpMinimumAge,
            Failure = AuthFailure.UnderMinimumAge
        };
    }

    // Student role, Free plan and the confirmation token of a just-created account. Returns the token,
    // or null when any of them failed (logged), so the caller can undo the account.
    private async Task<string?> TrySetUpNewAccountAsync(ApplicationUser user)
    {
        try
        {
            var roleResult = await userManager.AddToRoleAsync(user, StudentRole);
            if (!roleResult.Succeeded)
            {
                LogRegistrationRoleFailed(logger, user.Id, string.Join(", ", roleResult.Errors.Select(e => e.Code)));
                return null;
            }

            await subscriptionService.AssignFreePlanAsync(user.Id);
            return await userManager.GenerateEmailConfirmationTokenAsync(user);
        }
        catch (Exception ex)
        {
            LogRegistrationSetupFailed(logger, ex, user.Id);
            return null;
        }
    }

    private async Task RemoveHalfCreatedAccountAsync(ApplicationUser user)
    {
        try
        {
            var deleteResult = await userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
                LogRegistrationCleanupFailed(logger, user.Id, string.Join(", ", deleteResult.Errors.Select(e => e.Code)));
        }
        catch (Exception ex)
        {
            LogRegistrationCleanupThrew(logger, ex, user.Id);
        }
    }

    // Emails the confirmation link of an account.
    //
    // NOTE: the link points at the site's /confirm-email page, which confirms the userId and token in
    // process. In Development StubEmailService only writes it to the log.
    //
    // REL-2: the account/role/Free-plan may already be committed — a transient email failure here must not
    // turn into a 500 (the member would be stuck: can't re-register with a duplicate email, and never got the
    // confirmation link). Log and swallow instead; the caller still reports success.
    private async Task SendConfirmationEmailAsync(ApplicationUser user, string confirmToken, string language)
    {
        var confirmUrl = AuthLinks.ConfirmEmail(appOptions.Value.BaseUrl, user.Id, confirmToken);
        try
        {
            await emailService.SendEmailConfirmationAsync(user.Email!, confirmUrl, language);
        }
        catch (Exception ex)
        {
            LogConfirmationEmailFailed(logger, ex, user.Id);
        }
    }

    // A new confirmation link for an account that has not confirmed yet: a member whose first email is lost,
    // or who asks for a password reset instead. Silent when this account got one in the last two minutes.
    private async Task SendNewConfirmationLinkAsync(ApplicationUser user, string language)
    {
        if (!TryClaimEmailSlot(ConfirmationEmailKind, user.Id))
            return;

        var confirmToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        LogConfirmationEmailResent(logger, user.Id);
        await SendConfirmationEmailAsync(user, confirmToken, language);
    }

    // True, and starts the cooldown, when this account may be emailed this kind of message now.
    private bool TryClaimEmailSlot(string kind, string userId)
    {
        var key = $"auth-email:{kind}:{userId}";
        var now = timeProvider.GetUtcNow();

        lock (EmailSlotLock)
        {
            // The time is stored and compared with the injected clock; the cache's own expiry only frees the memory.
            if (cache.TryGetValue(key, out DateTimeOffset sentAt) && now - sentAt < EmailCooldown)
                return false;

            cache.Set(key, now, EmailCooldown);
            return true;
        }
    }

    public async Task<ServiceResult<AuthResponseDto>> ConfirmEmailAsync(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidConfirmationLink, Failure = AuthFailure.LinkInvalid });

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<AuthResponseDto>.NotFound(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.UserNotFound, Failure = AuthFailure.LinkInvalid });

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            LogEmailConfirmationFailed(logger, userId, string.Join(", ", result.Errors.Select(e => e.Code)));
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Errors = result.Errors.Select(e => e.Description), Failure = AuthFailure.LinkInvalid });
        }

        LogEmailConfirmed(logger, user.Id);
        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto { Success = true, Message = SuccessMessagesRes.EmailConfirmedSuccessfully });
    }

    public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginRequestDto request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            // Spend the time of a real password check, so the answer does not reveal that the email has no account.
            UnknownUserPasswordCheck.Run(request.Password);
            LogLoginUnknownUser(logger);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidCredentials, Failure = AuthFailure.InvalidCredentials });
        }

        // An unconfirmed account answers like a wrong password, and like a confirmed account once it is locked
        // out. With RequireConfirmedEmail the sign-in check below refuses such an account (NotAllowed) before
        // it hashes the password or counts a failure, so both are done here instead: the answer, the time and
        // the lockout then match those of a confirmed account.
        if (!await userManager.IsEmailConfirmedAsync(user))
        {
            var outcome = await UnconfirmedAccountPasswordCheck.RunAsync(userManager, user, request.Password);
            LogLoginUnconfirmedEmail(logger, user.Id);
            var lockedOut = outcome == UnconfirmedPasswordOutcome.LockedOut;
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto
            {
                Success = false,
                Message = lockedOut ? ErrorMessagesRes.AccountLocked : ErrorMessagesRes.InvalidCredentials,
                Failure = lockedOut ? AuthFailure.AccountLocked : AuthFailure.InvalidCredentials
            });
        }

        // The password comes next: a disabled account is only reported as such to someone who knows its
        // password, so a wrong guess never learns anything about the account.
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                LogUserLockedOut(logger, user.Id);
                return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountLocked, Failure = AuthFailure.AccountLocked });
            }
            LogInvalidPassword(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidCredentials, Failure = AuthFailure.InvalidCredentials });
        }

        if (!user.IsEnabled)
        {
            LogLoginDisabledUser(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountDisabledContactSupport, Failure = AuthFailure.AccountDisabled });
        }

        // The language the member signs in from is kept for the emails an admin may send them later.
        await PreferredLanguageSaver.SaveRequestLanguageAsync(userManager, user, logger);

        var (token, expiration) = await tokenService.GenerateAccessTokenAsync(user);
        var refreshToken = tokenService.GenerateRefreshToken();

        // Store refresh token in database
        var refreshTokenExpiration = request.RememberMe
            ? DateTimeOffset.UtcNow.AddDays(30)
            : DateTimeOffset.UtcNow.AddDays(7);

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = refreshTokenExpiration,
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStampAtIssuance = user.SecurityStamp
        };

        context.RefreshTokens.Add(refreshTokenEntity);
        await context.SaveChangesAsync();

        LogUserLoggedIn(logger, user.Id);

        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto
        {
            Success = true,
            Token = token,
            RefreshToken = refreshToken,
            Expiration = expiration
        });
    }

    public async Task<ServiceResult<AuthResponseDto>> RefreshAsync(RefreshTokenRequestDto request)
    {
        try
        {
            return await RefreshCoreAsync(request);
        }
        catch (DbUpdateConcurrencyException)
        {
            // REL-8: RevokedAt is a concurrency token, so when two requests rotate the same token at once
            // only the first save wins. The loser must not mint a second live token pair from one token.
            LogRefreshRaced(logger);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidOrExpiredRefreshToken, Failure = AuthFailure.InvalidRefreshToken });
        }
    }

    private async Task<ServiceResult<AuthResponseDto>> RefreshCoreAsync(RefreshTokenRequestDto request)
    {
        var tokenHash = HashToken(request.RefreshToken);

        var stored = await context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (stored is null || stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            LogRefreshInvalidToken(logger);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidOrExpiredRefreshToken, Failure = AuthFailure.InvalidRefreshToken });
        }

        // SEC-1 reuse detection: a token is only ever revoked by rotation (below) or by one of
        // the other guards in this method — so presenting an already-revoked token again means
        // it was stolen and the legitimate rotation already happened. Sever the whole descendant
        // chain rather than just this one token, since the thief may already hold a later token too.
        if (stored.RevokedAt is not null)
        {
            await RevokeTokenChainAsync(stored);
            LogRefreshTokenReuse(logger, stored.UserId);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidOrExpiredRefreshToken, Failure = AuthFailure.InvalidRefreshToken });
        }

        var user = stored.User;

        if (!user.IsEnabled)
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
            LogRefreshDisabledUser(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountIsDisabled, Failure = AuthFailure.AccountDisabled });
        }

        // SEC-1: a password reset (self-service or admin-forced) rotates SecurityStamp but this
        // token was minted before that — reject it instead of letting a stolen token keep working
        // for its full remaining lifetime after the victim thinks they've secured their account.
        if (stored.SecurityStampAtIssuance != user.SecurityStamp)
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
            LogRefreshStaleSecurityStamp(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidOrExpiredRefreshToken, Failure = AuthFailure.InvalidRefreshToken });
        }

        // Rotate: revoke the old token and issue a new pair. The new token lives as long as the old one was
        // issued for (7 days, or 30 with "remember me"), so a remember-me session is not cut to 7 days.
        var newRefreshTokenPlain = tokenService.GenerateRefreshToken();
        var issuedLifetime = stored.ExpiresAt - stored.CreatedAt;
        var newEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(newRefreshTokenPlain),
            ExpiresAt = DateTimeOffset.UtcNow.Add(issuedLifetime),
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStampAtIssuance = user.SecurityStamp
        };

        stored.RevokedAt = DateTimeOffset.UtcNow;
        stored.ReplacedByTokenId = newEntity.Id;
        context.RefreshTokens.Add(newEntity);
        await context.SaveChangesAsync();

        var (accessToken, expiration) = await tokenService.GenerateAccessTokenAsync(user);

        LogRefreshTokenRotated(logger, user.Id);

        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto
        {
            Success = true,
            Token = accessToken,
            RefreshToken = newRefreshTokenPlain,
            Expiration = expiration
        });
    }

    /// <summary>
    /// SEC-1 reuse-detection response: walks the rotation chain forward from an already-revoked
    /// token that was just presented again, revoking every still-active descendant. Bounded by
    /// <c>MaxChainLength</c> so a corrupted/cyclic <c>ReplacedByTokenId</c> chain can't loop forever.
    /// </summary>
    private async Task RevokeTokenChainAsync(RefreshToken start)
    {
        const int maxChainLength = 50;
        var now = DateTimeOffset.UtcNow;
        var nextId = start.ReplacedByTokenId;
        var hops = 0;

        while (nextId is not null && hops++ < maxChainLength)
        {
            var next = await context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Id == nextId);
            if (next is null)
                break;

            if (next.RevokedAt is null)
                next.RevokedAt = now;

            nextId = next.ReplacedByTokenId;
        }

        await context.SaveChangesAsync();
    }

    public async Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            // Don't reveal if user exists
            return new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetLinkSent };
        }

        // The email uses the language of the page the request came from. It is not saved: this request
        // is anonymous, so anyone who knows the email could flip it. The next sign-in saves it.
        var requestLanguage = UserLanguage.FromCulture(CultureInfo.CurrentUICulture);

        if (!await userManager.IsEmailConfirmedAsync(user))
        {
            // The member never confirmed, so a reset link would not let them sign in. The email they get is the
            // confirmation link instead, and the answer stays the same, so it reveals nothing about the account.
            await SendNewConfirmationLinkAsync(user, requestLanguage);
            return new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetLinkSent };
        }

        if (!TryClaimEmailSlot(ResetEmailKind, user.Id))
            return new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetLinkSent };

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = AuthLinks.ResetPassword(appOptions.Value.BaseUrl, user.Email!, token);

        LogPasswordResetRequested(logger, user.Id);

        // Like registration (REL-2): a failed send must not turn into a 500 that only a real account
        // gets, because the answer would tell a visitor which emails have an account.
        try
        {
            await emailService.SendPasswordResetAsync(user.Email!, resetUrl, requestLanguage);
        }
        catch (Exception ex)
        {
            LogResetEmailFailed(logger, ex, user.Id);
        }

        return new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetLinkSent };
    }

    public async Task<AuthResponseDto> ResendConfirmationAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);

        // The same answer for an unknown email, a confirmed account and an unconfirmed one, so the endpoint
        // cannot be used to find out which emails have an account or which have confirmed.
        if (user != null && !await userManager.IsEmailConfirmedAsync(user))
        {
            // Like the forgotten-password email, in the language of the page the request came from.
            var requestLanguage = UserLanguage.FromCulture(CultureInfo.CurrentUICulture);
            await SendNewConfirmationLinkAsync(user, requestLanguage);
        }

        return new AuthResponseDto { Success = true, Message = SuccessMessagesRes.ConfirmationEmailResent };
    }

    public async Task<ServiceResult<AuthResponseDto>> ResetPasswordAsync(ResetPasswordRequestDto request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            // Don't reveal if user exists
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidRequest, Failure = AuthFailure.LinkInvalid });
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            LogPasswordResetFailed(logger, user.Id, string.Join(", ", result.Errors.Select(e => e.Code)));

            // Identity checks the reset token before the new password, so when only password rules failed
            // the link was valid and the caller may be told what is wrong with the password.
            var errors = result.Errors.ToList();
            if (errors.All(error => error.Code?.StartsWith(PasswordErrorCodePrefix, StringComparison.Ordinal) == true))
                return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Errors = errors.Select(error => error.Description) });

            // Anything else (an invalid or expired token) gets a generic message, which never confirms account existence.
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidRequest, Failure = AuthFailure.LinkInvalid });
        }

        // SEC-1: ResetPasswordAsync rotated SecurityStamp, which RefreshAsync's stamp check will
        // now reject on its own — this bulk revoke just makes that outcome immediate/explicit
        // instead of waiting for the next refresh attempt to discover the mismatch.
        await RefreshTokenRevocation.RevokeActiveTokensAsync(context, user.Id);

        LogPasswordReset(logger, user.Id);
        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetSuccessful });
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return null;

        var roles = await userManager.GetRolesAsync(user);

        return new CurrentUserDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.AgeOn(SiteClock.Today(timeProvider)),
            user.Status.ToString(),
            [.. roles]);
    }

    public async Task<ServiceResult<AuthResponseDto>> DevResetPasswordAsync(DevResetPasswordRequestDto request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
            return ServiceResult<AuthResponseDto>.NotFound(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.UserNotFound });

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!result.Succeeded)
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Errors = result.Errors.Select(e => e.Description) });

        await RefreshTokenRevocation.RevokeActiveTokensAsync(context, user.Id);

        LogDevPasswordReset(logger, user.Id);
        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto { Success = true, Message = "Password reset (dev mode)" });
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Registration failed: {ErrorCodes}")]
    private static partial void LogRegistrationFailed(ILogger logger, string errorCodes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registration of user {UserId} failed to assign the Student role: {ErrorCodes}")]
    private static partial void LogRegistrationRoleFailed(ILogger logger, string userId, string errorCodes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Registration of user {UserId} failed while setting up the account.")]
    private static partial void LogRegistrationSetupFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not remove the half-created account of user {UserId}: {ErrorCodes}")]
    private static partial void LogRegistrationCleanupFailed(ILogger logger, string userId, string errorCodes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not remove the half-created account of user {UserId}.")]
    private static partial void LogRegistrationCleanupThrew(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered. Confirmation email queued.")]
    private static partial void LogUserRegistered(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A new confirmation email was requested for user {UserId}. Email queued.")]
    private static partial void LogConfirmationEmailResent(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send the confirmation email for user {UserId}.")]
    private static partial void LogConfirmationEmailFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email confirmation failed for {UserId}: {Errors}")]
    private static partial void LogEmailConfirmationFailed(ILogger logger, string userId, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email confirmed for user {UserId}")]
    private static partial void LogEmailConfirmed(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login attempt for a non-existent user.")]
    private static partial void LogLoginUnknownUser(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for unconfirmed email: user {UserId}")]
    private static partial void LogLoginUnconfirmedEmail(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for disabled user: {UserId}")]
    private static partial void LogLoginDisabledUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} is locked out.")]
    private static partial void LogUserLockedOut(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid password for user {UserId}")]
    private static partial void LogInvalidPassword(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged in.")]
    private static partial void LogUserLoggedIn(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh attempt with invalid or expired token.")]
    private static partial void LogRefreshInvalidToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected for user {UserId} — revoked the token chain.")]
    private static partial void LogRefreshTokenReuse(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token was rotated by another request at the same time; this request is rejected.")]
    private static partial void LogRefreshRaced(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh attempt for disabled user {UserId}.")]
    private static partial void LogRefreshDisabledUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh attempt with stale security stamp for user {UserId} (password changed since issuance).")]
    private static partial void LogRefreshStaleSecurityStamp(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refresh token rotated for user {UserId}.")]
    private static partial void LogRefreshTokenRotated(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset requested for user {UserId}. Reset email queued.")]
    private static partial void LogPasswordResetRequested(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send the password reset email for user {UserId}.")]
    private static partial void LogResetEmailFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password reset failed for user {UserId}: {Errors}")]
    private static partial void LogPasswordResetFailed(ILogger logger, string userId, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset for user {UserId}")]
    private static partial void LogPasswordReset(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset for user {UserId} (dev mode)")]
    private static partial void LogDevPasswordReset(ILogger logger, string userId);
}
