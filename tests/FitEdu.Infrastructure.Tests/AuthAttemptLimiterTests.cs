using FitEdu.Infrastructure.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

/// <summary>
/// The limiter behind the sign-in, sign-up and forgot-password pages (which run inside the Blazor
/// circuit, outside the API's "auth" limiter): each email has its own budget, and one global
/// budget caps everything.
/// </summary>
public class AuthAttemptLimiterTests
{
    [Fact]
    public void TryAcquire_EmailOverItsBudget_IsRefusedWhileAnotherEmailIsNot()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 3, globalPermits: 100);

        for (var attempt = 0; attempt < 3; attempt++)
            limiter.TryAcquire("A@X.COM").ShouldBeTrue();

        limiter.TryAcquire("A@X.COM").ShouldBeFalse();
        limiter.TryAcquire("B@X.COM").ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_AllEmailsTogetherOverTheGlobalBudget_AreRefused()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 10, globalPermits: 4);

        for (var attempt = 0; attempt < 4; attempt++)
            limiter.TryAcquire($"user{attempt}@x.com").ShouldBeTrue();

        limiter.TryAcquire("fresh@x.com").ShouldBeFalse();
    }

    [Fact]
    public void TryAcquire_BlankEmail_StillCountsAgainstTheGlobalBudget()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 10, globalPermits: 2);

        limiter.TryAcquire("").ShouldBeTrue();
        limiter.TryAcquire("").ShouldBeTrue();
        limiter.TryAcquire("").ShouldBeFalse();
    }
}
