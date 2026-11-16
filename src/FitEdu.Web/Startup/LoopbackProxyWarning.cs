using System.Net;

namespace FitEdu.Web.Startup;

/// <summary>
/// P03-6: the rate limiters never limit a loopback caller, because the app's own calls come from
/// there. A reverse proxy on the same host that forgets <c>X-Forwarded-For</c> would therefore make
/// every visitor look like loopback and leave the app without rate limiting, with no sign of it. Runs
/// ahead of the forwarded-headers middleware and logs ONE warning per process (outside Development)
/// when a loopback request carries neither the app's own secret header nor X-Forwarded-For.
/// </summary>
public sealed class LoopbackProxyWarning(ILogger<LoopbackProxyWarning> logger) : IStartupFilter
{
    private const string ForwardedForHeader = "X-Forwarded-For";

    private int _warned;

    /// <summary>A loopback request that is none of the app's own calls and names no forwarded client.</summary>
    public static bool IsUnforwardedLoopback(HttpContext httpContext)
    {
        var remoteAddress = httpContext.Connection.RemoteIpAddress;
        var isLoopback = remoteAddress is not null && PublicRateLimit.PartitionKeyFor(remoteAddress) is null;

        return isLoopback
            && !httpContext.Request.Headers.ContainsKey(ForwardedForHeader)
            && !SelfCallMarker.IsSelfCall(httpContext);
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((httpContext, nextMiddleware) =>
        {
            WarnOnce(httpContext);
            return nextMiddleware(httpContext);
        });
        next(app);
    };

    private void WarnOnce(HttpContext httpContext)
    {
        // A plain read first: after the warning every request skips the check and the atomic write.
        if (Volatile.Read(ref _warned) != 0)
            return;
        if (!IsUnforwardedLoopback(httpContext))
            return;

        // Exchange makes the first request the only one that logs, even when requests arrive together.
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            StartupLog.LoopbackWithoutForwardedFor(logger);
    }
}
