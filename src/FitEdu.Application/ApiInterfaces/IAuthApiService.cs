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

    /// <param name="request">The email to send a reset link to (built from <c>App:BaseUrl</c>).</param>
    Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request);
    Task<ServiceResult<AuthResponseDto>> ResetPasswordAsync(ResetPasswordRequestDto request);
    Task<CurrentUserDto?> GetCurrentUserAsync(string userId);

    Task<ServiceResult<AuthResponseDto>> DevResetPasswordAsync(DevResetPasswordRequestDto request);
}
