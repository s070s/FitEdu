using Microsoft.AspNetCore.Components.Authorization;

namespace FitEdu.Web.Extensions;

/// <summary>Reads the cascading authentication state of a page that guests can open too.</summary>
public static class AuthenticationStateExtensions
{
    /// <summary>True for a signed-in user; false for a guest or when no authentication state cascades.</summary>
    public static async Task<bool> IsSignedInAsync(this Task<AuthenticationState>? authenticationStateTask)
    {
        if (authenticationStateTask is null)
            return false;

        var state = await authenticationStateTask;
        return state.User.Identity?.IsAuthenticated == true;
    }
}
