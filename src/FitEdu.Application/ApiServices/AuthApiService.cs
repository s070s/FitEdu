using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    IEmailService emailService) : IAuthApiService
{
    public async Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request, Func<string, string, string?> buildConfirmUrl)
    {
        var user = request.ToNewUser();

        // Parental consent placeholder: if under 18, flag for future handling
        if (user.Age.HasValue && user.Age < 18)
        {
            // TODO: Implement parental consent flow. For now, allow registration but log.
            LogUnderEighteenRegistered(logger, user.Id);
        }

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            LogRegistrationFailed(logger, request.Email, string.Join(", ", result.Errors.Select(e => e.Description)));

            // Map duplicate-account errors to a generic message to prevent account enumeration.
            // Password-policy errors (too short, no digits, etc.) are safe to surface verbatim.
            var safeErrors = result.Errors.Select(e =>
                e.Code is "DuplicateUserName" or "DuplicateEmail"
                    ? ErrorMessagesRes.RegistrationFailedGeneric
                    : e.Description);

            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Errors = safeErrors });
        }

        // Assign default role
        await userManager.AddToRoleAsync(user, "Student");

        // Assign Free subscription plan
        await subscriptionService.AssignFreePlanAsync(user.Id);

        // Generate email confirmation token
        var confirmToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmUrl = buildConfirmUrl(user.Id, confirmToken);

        // NOTE: confirmUrl resolves to
        // the JSON API action below (/api/auth/confirm-email), not a user-facing Blazor page,
        // and in Development StubEmailService only writes it to the log. For production, add a
        // /confirm-email page and deliver this link through a real email provider.
        //
        // REL-2: the account/role/Free-plan are already committed above — a transient SMTP
        // failure here must not turn into a 500 after the account exists (the user would be
        // stuck: can't re-register with a duplicate email, and never got the confirmation link).
        // Log and swallow instead; registration still reports success.
        LogUserRegistered(logger, user.Id);
        try
        {
            await emailService.SendEmailConfirmationAsync(user.Email!, confirmUrl!, user.PreferredLanguage);
        }
        catch (Exception ex)
        {
            LogConfirmationEmailFailed(logger, ex, user.Id);
        }

        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto
        {
            Success = true,
            Message = SuccessMessagesRes.RegistrationSuccessfulCheckEmail
        });
    }

    public async Task<ServiceResult<AuthResponseDto>> ConfirmEmailAsync(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidConfirmationLink });

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<AuthResponseDto>.NotFound(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.UserNotFound });

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            LogEmailConfirmationFailed(logger, userId, string.Join(", ", result.Errors.Select(e => e.Description)));
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Errors = result.Errors.Select(e => e.Description) });
        }

        LogEmailConfirmed(logger, user.Id);
        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto { Success = true, Message = SuccessMessagesRes.EmailConfirmedSuccessfully });
    }

    public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginRequestDto request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            LogLoginUnknownUser(logger, request.Email);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidCredentials });
        }

        if (!await userManager.IsEmailConfirmedAsync(user))
        {
            LogLoginUnconfirmedEmail(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidCredentials });
        }

        if (!user.IsEnabled)
        {
            LogLoginDisabledUser(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountDisabledContactSupport });
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                LogUserLockedOut(logger, user.Id);
                return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountLocked });
            }
            LogInvalidPassword(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidCredentials });
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
        var tokenHash = HashToken(request.RefreshToken);

        var stored = await context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (stored is null || stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            LogRefreshInvalidToken(logger);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidOrExpiredRefreshToken });
        }

        // SEC-1 reuse detection: a token is only ever revoked by rotation (below) or by one of
        // the other guards in this method — so presenting an already-revoked token again means
        // it was stolen and the legitimate rotation already happened. Sever the whole descendant
        // chain rather than just this one token, since the thief may already hold a later token too.
        if (stored.RevokedAt is not null)
        {
            await RevokeTokenChainAsync(stored);
            LogRefreshTokenReuse(logger, stored.UserId);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidOrExpiredRefreshToken });
        }

        var user = stored.User;

        if (!user.IsEnabled)
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
            LogRefreshDisabledUser(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountIsDisabled });
        }

        // SEC-1: a password reset (self-service or admin-forced) rotates SecurityStamp but this
        // token was minted before that — reject it instead of letting a stolen token keep working
        // for its full remaining lifetime after the victim thinks they've secured their account.
        if (stored.SecurityStampAtIssuance != user.SecurityStamp)
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
            LogRefreshStaleSecurityStamp(logger, user.Id);
            return ServiceResult<AuthResponseDto>.Unauthorized(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidOrExpiredRefreshToken });
        }

        // Rotate: revoke the old token and issue a new pair
        var newRefreshTokenPlain = tokenService.GenerateRefreshToken();
        var newEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(newRefreshTokenPlain),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
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

    public async Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request, Func<string, string, string> buildResetUrl)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || !await userManager.IsEmailConfirmedAsync(user))
        {
            // Don't reveal if user exists
            return new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetLinkSent };
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = buildResetUrl(user.Email!, token);

        // The email uses the language of the page the request came from. It is not saved: this request
        // is anonymous, so anyone who knows the email could flip it. The next sign-in saves it.
        var requestLanguage = UserLanguage.FromCulture(CultureInfo.CurrentUICulture);

        LogPasswordResetRequested(logger, user.Id);
        await emailService.SendPasswordResetAsync(user.Email!, resetUrl, requestLanguage);

        return new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetLinkSent };
    }

    public async Task<ServiceResult<AuthResponseDto>> ResetPasswordAsync(ResetPasswordRequestDto request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            // Don't reveal if user exists
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidRequest });
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            LogPasswordResetFailed(logger, user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
            // Return generic message — specific errors would confirm account existence or reveal policy hints.
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidRequest });
        }

        // SEC-1: ResetPasswordAsync rotated SecurityStamp, which RefreshAsync's stamp check will
        // now reject on its own — this bulk revoke just makes that outcome immediate/explicit
        // instead of waiting for the next refresh attempt to discover the mismatch. Tracked
        // mutation + SaveChanges (not ExecuteUpdateAsync) so this runs on every provider,
        // including the EF InMemory provider integration tests use.
        var activeTokens = await context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync();
        foreach (var token in activeTokens)
            token.RevokedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

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
            user.Age,
            user.Status.ToString(),
            [.. roles]);
    }

    public async Task<ServiceResult<AuthResponseDto>> DevConfirmEmailAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
            return ServiceResult<AuthResponseDto>.NotFound(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.UserNotFound });

        user.EmailConfirmed = true;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            LogDevConfirmEmailFailed(logger, user.Id, errors);
            return ServiceResult<AuthResponseDto>.BadRequest(new AuthResponseDto { Success = false, Message = $"Confirm failed: {errors}" });
        }

        LogDevEmailConfirmed(logger, user.Id);
        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto { Success = true, Message = "Email confirmed (dev mode)" });
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

        var devActiveTokens = await context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync();
        foreach (var token in devActiveTokens)
            token.RevokedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        LogDevPasswordReset(logger, user.Id);
        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto { Success = true, Message = "Password reset (dev mode)" });
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Information, Message = "Under-18 user registered: {UserId}. Parental consent not yet implemented.")]
    private static partial void LogUnderEighteenRegistered(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registration failed for {Email}: {Errors}")]
    private static partial void LogRegistrationFailed(ILogger logger, string email, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered. Confirmation email queued.")]
    private static partial void LogUserRegistered(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send confirmation email for user {UserId} after registration.")]
    private static partial void LogConfirmationEmailFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email confirmation failed for {UserId}: {Errors}")]
    private static partial void LogEmailConfirmationFailed(ILogger logger, string userId, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email confirmed for user {UserId}")]
    private static partial void LogEmailConfirmed(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login attempt for non-existent user: {Email}")]
    private static partial void LogLoginUnknownUser(ILogger logger, string email);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh attempt for disabled user {UserId}.")]
    private static partial void LogRefreshDisabledUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh attempt with stale security stamp for user {UserId} (password changed since issuance).")]
    private static partial void LogRefreshStaleSecurityStamp(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refresh token rotated for user {UserId}.")]
    private static partial void LogRefreshTokenRotated(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset requested for user {UserId}. Reset email queued.")]
    private static partial void LogPasswordResetRequested(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password reset failed for user {UserId}: {Errors}")]
    private static partial void LogPasswordResetFailed(ILogger logger, string userId, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset for user {UserId}")]
    private static partial void LogPasswordReset(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DevConfirmEmail: UpdateAsync failed for user {UserId}: {Errors}")]
    private static partial void LogDevConfirmEmailFailed(ILogger logger, string userId, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email confirmed for user {UserId} (dev mode)")]
    private static partial void LogDevEmailConfirmed(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset for user {UserId} (dev mode)")]
    private static partial void LogDevPasswordReset(ILogger logger, string userId);
}
