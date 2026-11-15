namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Counts the sign-in, sign-up and forgot-password attempts the Blazor pages make inside the circuit,
/// which never pass the API's "auth" rate limiter.
/// </summary>
public interface IAuthAttemptLimiter
{
    /// <summary>
    /// True when the attempt may go ahead. <paramref name="normalizedEmail"/> picks the per-email budget and
    /// <paramref name="clientAddress"/> the per-address budget (null or loopback: one shared "unknown" budget);
    /// one global flood cap applies to all attempts together.
    /// </summary>
    bool TryAcquire(string normalizedEmail, string? clientAddress);
}
