using System.Threading.RateLimiting;

namespace FitEdu.Web.Startup;

/// <summary>
/// The "auth" rate limiter for the sign-in API endpoints (register, login, refresh, forgot and reset
/// password, confirm email): a fixed window per caller IP address, so one caller who uses up its
/// attempts cannot lock every other visitor out (an IPv6 caller counts as its whole /64 network). Loopback and address-less calls (the app's own
/// calls, and the test server) are not limited, as for <see cref="PublicRateLimit"/>.
/// </summary>
public static class AuthRateLimit
{
    public const string PolicyName = "auth";

    public const int PermitsPerWindow = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Every loopback and address-less request shares this key; its limiter never rejects.
    private const string UnlimitedPartitionKey = "unlimited";

    public static RateLimitPartition<string> PartitionFor(HttpContext httpContext)
    {
        var partitionKey = PublicRateLimit.PartitionKeyForRequest(httpContext);
        if (partitionKey is null)
            return RateLimitPartition.GetNoLimiter(UnlimitedPartitionKey);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitsPerWindow,
            Window = Window,
            QueueLimit = 0
        });
    }
}
