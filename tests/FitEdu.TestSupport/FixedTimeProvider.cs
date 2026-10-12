namespace FitEdu.TestSupport;

/// <summary>
/// A clock that always reads the given moment, so date-dependent code gives the same result on every run.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
