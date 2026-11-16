using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using FitEdu.Application.Common;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Hubs;

/// <summary>
/// Re-checks the signed-in account on every hub method call. The hubs only check the account when a
/// connection opens, and <see cref="Services.LiveConnectionRegistry"/> closes the open connections when an
/// admin disables the account, changes its roles or sets its password (each of those rotates the security
/// stamp). This filter is the guard for the actions themselves: a call from a disabled, deleted or stale
/// session closes the connection and is refused, even if the connection was missed.
/// </summary>
public partial class ActiveSessionHubFilter : IHubFilter
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ActiveSessionHubFilter> _logger;

    public ActiveSessionHubFilter(
        UserManager<ApplicationUser> userManager,
        ILogger<ActiveSessionHubFilter> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var userId = invocationContext.Context.UserIdentifier;
        var user = userId is null ? null : await _userManager.FindByIdAsync(userId);
        var sessionStamp = invocationContext.Context.User?.FindFirstValue(UserClaimsBuilder.SecurityStampClaim);

        if (user is null || !user.IsEnabled || user.SecurityStamp != sessionStamp)
        {
            LogSessionRejected(_logger, userId ?? "(none)");
            invocationContext.Context.Abort();
            // A stale session of a working account is told to sign in again; only a disabled account is told so.
            throw new HubException(user is { IsEnabled: true } ? ErrorMessagesRes.NotAuthenticated : ErrorMessagesRes.AccountDisabled);
        }

        return await next(invocationContext);
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hub: Closed the connection of user {UserId}: the account is disabled, gone or its session is out of date.")]
    private static partial void LogSessionRejected(ILogger logger, string userId);
}
