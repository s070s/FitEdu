using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using FitEdu.Application.DTOs;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources.Messages;
using System.Security.Claims;
using System.Text.Json;

namespace FitEdu.Infrastructure.Services;

/// <summary>
/// SSR-compatible auth service.
/// Authentication state is maintained via HttpOnly cookies rather than localStorage.
///
/// IMPORTANT — Blazor Server circuit constraint:
/// HttpContext.SignInAsync / SignOutAsync write Set-Cookie response headers, which is
/// impossible once the Blazor circuit has taken over the connection (the HTTP response
/// has already been committed to the browser).
///
/// To work around this, all cookie-writing operations are deferred.  The Blazor
/// component calls LoginAsync / ImpersonateAsync, receives a
/// redirect URL containing a short-lived signed DataProtection token, then issues a
/// forceLoad navigation.  The /auth/complete minimal endpoint is invoked on a fresh HTTP
/// request (response not yet started), decodes the token, performs the DB lookup to
/// rebuild the principal, calls SignInAsync, and redirects the browser.  Leaving an
/// impersonation needs no token: the banner navigates straight to /auth/exit-impersonation.
/// </summary>
public partial class AuthService : IAuthService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuthApiService _authApiService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AuthService> _logger;
    private readonly ITokenService _tokenService;
    private readonly IAuthAttemptLimiter _attemptLimiter;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly ITimeLimitedDataProtector _lessonAssetProtector;

    /// <summary>Cookie name used to store the admin's user ID during impersonation.</summary>
    public const string AdminBackupCookieName = ".FitEdu.AdminUserId";

    /// <summary>Data Protection purpose string — changing this invalidates all in-flight tokens.</summary>
    public const string ProtectorPurpose = "FitEdu.AuthCompletion.v2";

    /// <summary>Data Protection purpose for the admin backup cookie value. Changing this invalidates all active impersonation sessions.</summary>
    public const string AdminBackupCookieProtectorPurpose = "FitEdu.AdminBackupCookie.v1";

    /// <summary>Data Protection purpose for lesson-asset tokens (SEC-2). Changing this invalidates all in-flight asset links.</summary>
    public const string LessonAssetProtectorPurpose = "FitEdu.LessonAsset.v1";

    /// <summary>How long a sign-in ticket can be opened at /auth/complete (once; see <see cref="AuthCompletionTicket.TicketId"/>).</summary>
    public static readonly TimeSpan SignInTicketLifetime = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan LessonAssetTokenLifetime = TimeSpan.FromMinutes(10);

    public AuthService(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuthApiService authApiService,
        ApplicationDbContext context,
        IDataProtectionProvider dataProtectionProvider,
        ITokenService tokenService,
        IAuthAttemptLimiter attemptLimiter,
        ILogger<AuthService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _signInManager = signInManager;
        _authApiService = authApiService;
        _context = context;
        _logger = logger;
        _tokenService = tokenService;
        _attemptLimiter = attemptLimiter;
        _protector = dataProtectionProvider
            .CreateProtector(ProtectorPurpose)
            .ToTimeLimitedDataProtector();
        _lessonAssetProtector = dataProtectionProvider
            .CreateProtector(LessonAssetProtectorPurpose)
            .ToTimeLimitedDataProtector();
    }

    private HttpContext HttpContext => _httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("HttpContext is not available.");

    /// <inheritdoc />
    /// <remarks>
    /// On success, <see cref="AuthResponseDto.Token"/> contains a signed DataProtection
    /// token.  Navigate to <c>/auth/complete?ticket={Token}&amp;returnUrl=…</c>
    /// with <c>forceLoad: true</c> to complete cookie issuance on a fresh HTTP request.
    /// </remarks>
    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request)
    {
        // The page runs inside the circuit, past the API's rate limiter, so it counts its own attempts.
        if (!AttemptAllowed(request.Email))
            return TooManyAttempts();

        // Clear the EF Core change tracker so this call always reads fresh data from the
        // database.  In Blazor Server the DbContext is scoped to the circuit lifetime
        // (long-lived), so without this a previously-loaded entity (e.g. from a failed
        // login attempt before the email was confirmed) would be returned stale from the
        // identity map and IsEmailConfirmedAsync would still see the old false value.
        _context.ChangeTracker.Clear();

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Spend the time of a real password check, so the answer does not reveal that the email has no account.
            UnknownUserPasswordCheck.Run(request.Password);
            LogLoginUnknownUser(_logger);
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidCredentials };
        }

        // "Not confirmed" is only told to someone who knows the password, so a guess cannot find unconfirmed
        // accounts. (CheckPasswordSignInAsync refuses an unconfirmed account before it hashes anything.)
        if (!await _userManager.IsEmailConfirmedAsync(user))
        {
            var passwordIsRight = await _userManager.CheckPasswordAsync(user, request.Password);
            return new AuthResponseDto
            {
                Success = false,
                Message = passwordIsRight ? ErrorMessagesRes.EmailNotConfirmedError : ErrorMessagesRes.InvalidCredentials
            };
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
                return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountLocked };
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidCredentials };
        }

        // "Disabled" is only told to someone who knows the password; a wrong guess learns nothing about the account.
        if (!user.IsEnabled)
        {
            LogLoginDisabledUser(_logger, user.Id);
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountDisabledContactSupport };
        }

        // The language the member signs in from is kept for the emails an admin may send them later.
        await PreferredLanguageSaver.SaveRequestLanguageAsync(_userManager, user, _logger);

        var stamp = await _userManager.GetSecurityStampAsync(user);
        var token = CreateSignInToken(userId: user.Id, adminBackupId: null, securityStamp: stamp, rememberMe: request.RememberMe);
        LogSignInTokenIssued(_logger, user.Id);
        return new AuthResponseDto { Success = true, Token = token };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the URL the caller should navigate to (with forceLoad: true) to actually
    /// clear the auth cookie on a fresh HTTP request.
    /// </remarks>
    public Task<string> LogoutAsync()
    {
        return Task.FromResult("/auth/signout?returnUrl=%2F");
    }

    /// <summary>
    /// Registers through the same flow as the API (Student role, Free plan, confirmation email); the page
    /// keeps telling the visitor when the email is already taken.
    /// </summary>
    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request) =>
        AttemptAllowed(request.Email)
            ? ToDto(await _authApiService.RegisterFromSignUpPageAsync(request))
            : TooManyAttempts();

    /// <inheritdoc />
    /// <remarks>
    /// On success, <see cref="AuthResponseDto.Token"/> contains a signed DataProtection
    /// token.  Navigate to <c>/auth/complete?ticket={Token}&amp;returnUrl=…</c>
    /// with <c>forceLoad: true</c>.
    /// </remarks>
    public async Task<AuthResponseDto> ImpersonateAsync(ClaimsPrincipal admin, string userId)
    {
        var adminId = admin.FindFirstValue(ClaimTypes.NameIdentifier);
        if (admin.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(adminId))
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.NotAuthenticated };

        // The admin pages are guarded already; this check keeps the service safe whoever calls it.
        if (!admin.IsInRole("Admin"))
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccessDenied };

        var target = await _userManager.FindByIdAsync(userId);
        if (target is null)
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.UserNotFound };

        var targetRoles = await _userManager.GetRolesAsync(target);
        if (!targetRoles.Contains("Student"))
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.ImpersonationOnlyStudents };

        if (!target.IsEnabled)
            return new AuthResponseDto { Success = false, Message = ErrorMessagesRes.AccountDisabled };

        var stamp = await _userManager.GetSecurityStampAsync(target);
        // Session-only: the impersonated session ends with the browser, like the backup cookie that leads back to the admin.
        var token = CreateSignInToken(userId: target.Id, adminBackupId: adminId, securityStamp: stamp, rememberMe: false);
        LogImpersonationTokenIssued(_logger, adminId, userId);
        return new AuthResponseDto { Success = true, Token = token };
    }

    /// <summary>
    /// Generates a short-lived JWT from HttpContext.User claims.
    /// Only safe to call during HTTP requests (SSR render, API controllers).
    /// Use <see cref="GetTokenAsync(ClaimsPrincipal)"/> inside Blazor Server circuits.
    /// </summary>
    public Task<string?> GetTokenAsync() => GetTokenAsync(HttpContext.User);

    /// <summary>
    /// Generates a short-lived JWT from the supplied principal — no HttpContext needed.
    /// Safe to call inside Blazor Server circuits; pass AuthenticationState.User.
    /// </summary>
    public Task<string?> GetTokenAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(_tokenService.SignClaims(principal.Claims));
    }

    /// <inheritdoc />
    public Task<string> CreateLessonAssetTokenAsync(ClaimsPrincipal principal, Guid lessonId)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID not found in claims.");

        var ticket = new LessonAssetTicket(userId, lessonId);
        var payload = JsonSerializer.Serialize(ticket);
        return Task.FromResult(_lessonAssetProtector.Protect(payload, lifetime: LessonAssetTokenLifetime));
    }

    /// <summary>Emails a reset link when the email belongs to a confirmed account; always answers the same.</summary>
    public Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request) =>
        AttemptAllowed(request.Email)
            ? _authApiService.ForgotPasswordAsync(request)
            : Task.FromResult(TooManyAttempts());

    public async Task<AuthResponseDto> ResetPasswordAsync(ResetPasswordRequestDto request) =>
        ToDto(await _authApiService.ResetPasswordAsync(request));

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    // One budget per email (case and surrounding spaces do not matter) plus one for everyone.
    private bool AttemptAllowed(string? email) =>
        _attemptLimiter.TryAcquire((email ?? string.Empty).Trim().ToUpperInvariant());

    private static AuthResponseDto TooManyAttempts() =>
        new() { Success = false, Message = ErrorMessagesRes.TooManyAttempts };

    // The API service embeds its outcome in Value on success and failure alike.
    private static AuthResponseDto ToDto(ServiceResult<AuthResponseDto> result) =>
        result.Value ?? new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidRequest };

    /// <summary>
    /// Creates a short-lived signed DataProtection token encoding the user ID and
    /// any impersonation metadata.  The /auth/complete endpoint decodes this token,
    /// performs a DB lookup, builds the <see cref="ClaimsPrincipal"/>, and calls
    /// <c>HttpContext.SignInAsync</c> on a fresh HTTP request.
    /// </summary>
    /// <param name="userId">The ID of the user to sign in as.</param>
    /// <param name="adminBackupId">
    /// If non-null, the admin's user ID to store in the backup cookie (impersonation start).
    /// </param>
    /// <param name="rememberMe">
    /// If true, /auth/complete issues a persistent auth cookie (7-day expiry). If false, it issues
    /// a session cookie that the browser discards on close, regardless of the "Remember Me" checkbox.
    /// </param>
    private string CreateSignInToken(string userId, string? adminBackupId, string securityStamp, bool rememberMe)
    {
        var ticket = new AuthCompletionTicket(userId, adminBackupId, securityStamp, rememberMe, Guid.NewGuid());
        var payload = JsonSerializer.Serialize(ticket);
        return _protector.Protect(payload, lifetime: SignInTicketLifetime);
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login attempt for a non-existent user.")]
    private static partial void LogLoginUnknownUser(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for disabled user: {UserId}")]
    private static partial void LogLoginDisabledUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} validated — sign-in token issued.")]
    private static partial void LogSignInTokenIssued(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} issued impersonation token for user {UserId}.")]
    private static partial void LogImpersonationTokenIssued(ILogger logger, string adminId, string userId);
}
