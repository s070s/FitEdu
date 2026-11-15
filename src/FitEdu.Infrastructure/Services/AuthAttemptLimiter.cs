using System.Threading.RateLimiting;
using FitEdu.Application.ApiInterfaces;

namespace FitEdu.Infrastructure.Services;

/// <summary>
/// One fixed window per email plus one window for all emails together, checked in that order. The
/// windows live in memory of this process, like the API's other rate limiters (a single instance).
/// Register as a singleton: the counters must outlive each Blazor circuit.
/// </summary>
public sealed class AuthAttemptLimiter : IAuthAttemptLimiter, IDisposable
{
    public const int DefaultPerEmailPermits = 10;

    public const int DefaultGlobalPermits = 100;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private const string GlobalPartitionKey = "all";

    private readonly PartitionedRateLimiter<string> _limiter;

    public AuthAttemptLimiter(int perEmailPermits = DefaultPerEmailPermits, int globalPermits = DefaultGlobalPermits)
    {
        var perEmail = PartitionedRateLimiter.Create<string, string>(email =>
            RateLimitPartition.GetFixedWindowLimiter(email, _ => WindowOptions(perEmailPermits)));
        var global = PartitionedRateLimiter.Create<string, string>(_ =>
            RateLimitPartition.GetFixedWindowLimiter(GlobalPartitionKey, _ => WindowOptions(globalPermits)));

        _limiter = PartitionedRateLimiter.CreateChained(perEmail, global);
    }

    public bool TryAcquire(string normalizedEmail)
    {
        using var lease = _limiter.AttemptAcquire(normalizedEmail);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();

    private static FixedWindowRateLimiterOptions WindowOptions(int permits) => new()
    {
        PermitLimit = permits,
        Window = Window,
        QueueLimit = 0
    };
}
