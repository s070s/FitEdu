using System.Threading.RateLimiting;
using FitEdu.Application.ApiInterfaces;

namespace FitEdu.Infrastructure.Services;

/// <summary>
/// One fixed window per email, one per client address and one for all attempts together, checked in that
/// order. The per-address window keeps one anonymous client from using up the global budget for everyone;
/// the global window is only a flood cap. Clients without a known address (or on loopback) share one
/// "unknown" address window. The windows live in memory of this process, like the API's other rate
/// limiters (a single instance). Register as a singleton: the counters must outlive each Blazor circuit.
/// </summary>
public sealed class AuthAttemptLimiter : IAuthAttemptLimiter, IDisposable
{
    public const int DefaultPerEmailPermits = 10;

    public const int DefaultPerAddressPermits = 20;

    public const int DefaultGlobalPermits = 1000;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private const string GlobalPartitionKey = "all";

    // The address window of every client whose address is not known.
    private const string UnknownAddressPartitionKey = "unknown";

    private readonly PartitionedRateLimiter<Attempt> _limiter;

    public AuthAttemptLimiter(
        int perEmailPermits = DefaultPerEmailPermits,
        int perAddressPermits = DefaultPerAddressPermits,
        int globalPermits = DefaultGlobalPermits)
    {
        var perEmail = PartitionedRateLimiter.Create<Attempt, string>(attempt =>
            RateLimitPartition.GetFixedWindowLimiter(attempt.Email, _ => WindowOptions(perEmailPermits)));
        var perAddress = PartitionedRateLimiter.Create<Attempt, string>(attempt =>
            RateLimitPartition.GetFixedWindowLimiter(
                attempt.ClientAddress ?? UnknownAddressPartitionKey, _ => WindowOptions(perAddressPermits)));
        var global = PartitionedRateLimiter.Create<Attempt, string>(_ =>
            RateLimitPartition.GetFixedWindowLimiter(GlobalPartitionKey, _ => WindowOptions(globalPermits)));

        _limiter = PartitionedRateLimiter.CreateChained(perEmail, perAddress, global);
    }

    public bool TryAcquire(string normalizedEmail, string? clientAddress)
    {
        using var lease = _limiter.AttemptAcquire(new Attempt(normalizedEmail, clientAddress));
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();

    private static FixedWindowRateLimiterOptions WindowOptions(int permits) => new()
    {
        PermitLimit = permits,
        Window = Window,
        QueueLimit = 0
    };

    private readonly record struct Attempt(string Email, string? ClientAddress);
}
