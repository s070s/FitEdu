using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FitEdu.Application.Common;
using FitEdu.Web.Interfaces;
using FitEdu.Web.Startup;
using System.Security.Claims;

namespace FitEdu.Web.Layout;

public partial class ImpersonationBanner : IDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private ILogger<ImpersonationBanner> _logger { get; set; } = default!;

    private bool _isImpersonating;
    private string _impersonatedEmail = "";
    private string _impersonatedName = "";

    protected override async Task OnInitializedAsync()
    {
        AuthState.AuthenticationStateChanged += OnAuthStateChangedAsync;
        await RefreshAsync(await AuthState.GetAuthenticationStateAsync());
    }

    private async void OnAuthStateChangedAsync(Task<AuthenticationState> task)
    {
        try
        {
            await RefreshAsync(await task);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            LogImpersonationRefreshFailed(_logger, ex);
        }
    }

    private Task RefreshAsync(AuthenticationState state)
    {
        // Read the impersonatedBy claim directly from the cascaded principal —
        // avoids touching HttpContext which is null inside a Blazor Server circuit.
        _isImpersonating = state.User.FindFirst(UserClaimsBuilder.ImpersonatedByClaim) is not null;
        if (_isImpersonating)
        {
            _impersonatedEmail = state.User.FindFirst(ClaimTypes.Email)?.Value
                                 ?? state.User.FindFirst("email")?.Value ?? "";
            var firstName = state.User.FindFirst("firstName")?.Value ?? "";
            var lastName = state.User.FindFirst("lastName")?.Value ?? "";
            _impersonatedName = $"{firstName} {lastName}".Trim();
        }
        else
        {
            _impersonatedEmail = "";
            _impersonatedName = "";
        }

        return Task.CompletedTask;
    }

    // Leaving needs a fresh HTTP request (it writes cookies), so the banner navigates to the endpoint,
    // which checks the session's impersonatedBy claim against the backup cookie.
    private void Exit() => Navigation.NavigateTo(InfrastructureEndpointsExtensions.ExitImpersonationPath, forceLoad: true);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        AuthState.AuthenticationStateChanged -= OnAuthStateChangedAsync;
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Error refreshing impersonation state.")]
    private static partial void LogImpersonationRefreshFailed(ILogger logger, Exception exception);
}
