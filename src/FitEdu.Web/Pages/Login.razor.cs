using Microsoft.AspNetCore.Components;
using FitEdu.Web.Interfaces;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Pages;

public partial class Login
{
    private const string HomePath = "/";
    private const string RegisterPath = "/register";

    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<Login> Logger { get; set; } = default!;

    /// <summary>Where to go after signing in, e.g. the programme a guest was looking at.</summary>
    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }

    private LoginRequestDto loginRequest = new();
    private string? errorMessage;
    private bool isLoading;
    private bool showPassword;

    // Tracks whether the most recent failure is due to unconfirmed email and which email is pending
    private bool unconfirmedEmailPending;
    private string pendingUnconfirmedEmail = string.Empty;

    /// <summary>The sign-up page, keeping the return address so a new member comes back too.</summary>
    private string RegisterHref => LinkWithReturnUrl(RegisterPath, ReturnUrl);

    private string passwordInputType => showPassword ? "text" : "password";
    private string passwordIconClass => showPassword ? "bi bi-eye-slash" : "bi bi-eye";

    private void TogglePasswordVisibility()
    {
        showPassword = !showPassword;
    }

    private async Task HandleLogin()
    {
        isLoading = true;
        errorMessage = null;
        unconfirmedEmailPending = false;
        pendingUnconfirmedEmail = string.Empty;

        // Only the email is trimmed: a password is sent exactly as typed, as the sign-up and the API take it.
        loginRequest.Email = loginRequest.Email?.Trim() ?? string.Empty;

        try
        {
            var result = await AuthService.LoginAsync(loginRequest);

            if (result.Success)
            {
                // Navigate forceLoad so the fresh HTTP request can set the auth cookie.
                Navigation.NavigateTo(
                    $"/auth/complete?ticket={Uri.EscapeDataString(result.Token!)}&returnUrl={Uri.EscapeDataString(LocalReturnPath(ReturnUrl))}",
                    forceLoad: true);
                return; // navigation is underway — don't touch component state
            }
            else
            {
                // Preserve original message
                errorMessage = result.Message ?? ErrorMessagesRes.LoginError;

                // Detect "email not confirmed" failure and surface targeted guidance.
                // Compared against the resx value (not a hardcoded English substring) so this still
                // matches once AuthService's message is localized to the request's culture.
                if (result.Message == ErrorMessagesRes.EmailNotConfirmedError)
                {
                    unconfirmedEmailPending = true;
                    pendingUnconfirmedEmail = loginRequest.Email;
                    // Hide generic error when showing the targeted unconfirmed-email UI
                    errorMessage = null;
                }
            }
        }
        catch (Exception ex)
        {
            LogLoginFailed(Logger, ex);
            errorMessage = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }

        isLoading = false;
    }

    /// <summary>
    /// The return address when it is a path on this site, otherwise the home page. "//host" and
    /// "/\host" are rejected because browsers read both as another site (an open redirect).
    /// </summary>
    public static string LocalReturnPath(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/'))
            return HomePath;

        var pointsToAnotherSite = returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal);
        return pointsToAnotherSite ? HomePath : returnUrl;
    }

    /// <summary>
    /// A link to <paramref name="pagePath"/> that carries the return address ("/login?returnUrl=%2Fcourses%2F42"),
    /// or the bare page path when there is no local return address.
    /// </summary>
    public static string LinkWithReturnUrl(string pagePath, string? returnUrl)
    {
        var localPath = LocalReturnPath(returnUrl);
        if (localPath == HomePath)
            return pagePath;

        return $"{pagePath}?returnUrl={Uri.EscapeDataString(localPath)}";
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error during login.")]
    private static partial void LogLoginFailed(ILogger logger, Exception exception);
}
