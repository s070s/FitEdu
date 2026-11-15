using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FitEdu.Application.DTOs;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Web.Extensions;
using System.Security.Claims;

namespace FitEdu.Web.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("Authentication")]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class AuthController : ControllerBase
{
    private readonly IAuthApiService _authApiService;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthApiService authApiService, IWebHostEnvironment env)
    {
        _authApiService = authApiService;
        _env = env;
    }

    /// <summary>
    /// Register a new user. Assigns Student role by default.
    /// Email confirmation is required before login.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto request)
    {
        var result = await _authApiService.RegisterAsync(request);
        return result.ToEmbeddedActionResult();
    }

    /// <summary>
    /// Confirm user email address. POST, because a confirmation changes the account: the emailed
    /// link opens the site's /confirm-email page, which posts the userId and token here.
    /// </summary>
    [HttpPost("confirm-email")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> ConfirmEmail([FromBody] ConfirmEmailRequestDto request)
    {
        var result = await _authApiService.ConfirmEmailAsync(request.UserId, request.Token);
        return result.ToEmbeddedActionResult();
    }

    /// <summary>
    /// Login with email and password. Returns JWT access token.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        var result = await _authApiService.LoginAsync(request);
        return result.ToEmbeddedActionResult();
    }

    /// <summary>
    /// Exchange a valid refresh token for a new JWT access token and rotated refresh token.
    /// The submitted refresh token is revoked; a fresh pair is returned.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshTokenRequestDto request)
    {
        var result = await _authApiService.RefreshAsync(request);
        return result.ToEmbeddedActionResult();
    }

    /// <summary>
    /// Request a password reset. Always answers with the same generic success; when the email belongs
    /// to a confirmed account, a reset link is emailed.
    /// </summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        var dto = await _authApiService.ForgotPasswordAsync(request);
        return Ok(dto);
    }

    /// <summary>
    /// Reset password using token from forgot-password flow.
    /// </summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        var result = await _authApiService.ResetPasswordAsync(request);
        return result.ToEmbeddedActionResult();
    }

    /// <summary>
    /// Get current user info. Requires authentication.
    /// </summary>
    /// <response code="200">The authenticated user's identity summary.</response>
    /// <response code="404">The user in the token no longer exists.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrentUserDto>> GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var dto = await _authApiService.GetCurrentUserAsync(userId);
        return dto is not null ? Ok(dto) : NotFound();
    }

    // NOTE: this dev-only endpoint is the sign-in fixture of the Playwright e2e suite (tests/e2e/global-setup.mjs
    // sets the test members' password through it); no page uses it. It is compiled out of Release builds
    // (#if DEBUG) and additionally gated by IsDevelopment() at runtime, so it provides no fallback in
    // production, where the emailed reset link is the only way. The old dev/confirm-email twin is gone:
    // in Development, confirm an account through the link the stub email service logs.
#if DEBUG
    /// <summary>
    /// Dev-only: Reset password without token (development mode only). The e2e suite's sign-in fixture.
    /// </summary>
    [HttpPost("dev/reset-password")]
    public async Task<ActionResult<AuthResponseDto>> DevResetPassword([FromBody] DevResetPasswordRequestDto request)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var result = await _authApiService.DevResetPasswordAsync(request);
        return result.ToEmbeddedActionResult();
    }
#endif
}
