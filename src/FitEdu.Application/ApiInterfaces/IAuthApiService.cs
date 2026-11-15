using FitEdu.Application.Common;
using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// JSON API authentication: register/login/refresh with JWT access + refresh tokens.
/// Distinct from <see cref="IAuthService"/>, which backs the Blazor Server cookie-based
/// SSR sign-in flow and returns redirect URLs instead of tokens.
/// </summary>
public interface IAuthApiService
{
    /// <summary>
    /// Registers a Student and emails the confirmation link (built from <c>App:BaseUrl</c>).
    /// </summary>
    Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request);

    /// <summary>
    /// The same registration for the sign-up page, except that a taken email is reported as such (the page
    /// tells the visitor to use another email or log in); the API hides it.
    /// </summary>
    Task<ServiceResult<AuthResponseDto>> RegisterFromSignUpPageAsync(RegisterRequestDto request);
    Task<ServiceResult<AuthResponseDto>> ConfirmEmailAsync(string userId, string token);
    Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginRequestDto request);
    Task<ServiceResult<AuthResponseDto>> RefreshAsync(RefreshTokenRequestDto request);

    /// <summary>
    /// Emails a reset link (built from <c>App:BaseUrl</c>) when the email belongs to a confirmed account, and
    /// a new confirmation link when it belongs to an unconfirmed one (a reset link could not sign them in).
    /// Always answers the same, and sends at most one email per account every two minutes.
    /// </summary>
    /// <param name="request">The email to send the link to.</param>
    Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request);

    /// <summary>
    /// Emails a new confirmation link (built from <c>App:BaseUrl</c>, in the request language) when the email
    /// belongs to an account that has not confirmed yet. Always the same success answer, whether the email is
    /// unknown, already confirmed or unconfirmed; at most one email per account every two minutes.
    /// </summary>
    Task<AuthResponseDto> ResendConfirmationAsync(string email);
    Task<ServiceResult<AuthResponseDto>> ResetPasswordAsync(ResetPasswordRequestDto request);
    Task<CurrentUserDto?> GetCurrentUserAsync(string userId);

    Task<ServiceResult<AuthResponseDto>> DevResetPasswordAsync(DevResetPasswordRequestDto request);
}
