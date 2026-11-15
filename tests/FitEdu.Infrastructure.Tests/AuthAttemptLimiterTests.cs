using FitEdu.Infrastructure.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

/// <summary>
/// The limiter behind the sign-in, sign-up and forgot-password pages (which run inside the Blazor
/// circuit, outside the API's "auth" limiter): each email and each client address has its own budget,
/// and one global budget caps everything.
/// </summary>
public class AuthAttemptLimiterTests
{
    private const string SomeAddress = "203.0.113.10";

    private const string OtherAddress = "203.0.113.11";

    [Fact]
    public void TryAcquire_EmailOverItsBudget_IsRefusedWhileAnotherEmailIsNot()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 3, perAddressPermits: 100, globalPermits: 100);

        for (var attempt = 0; attempt < 3; attempt++)
            limiter.TryAcquire("A@X.COM", SomeAddress).ShouldBeTrue();

        limiter.TryAcquire("A@X.COM", SomeAddress).ShouldBeFalse();
        limiter.TryAcquire("B@X.COM", SomeAddress).ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_AddressOverItsBudget_IsRefusedWhileAnotherAddressIsNot()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 100, perAddressPermits: 3, globalPermits: 100);

        for (var attempt = 0; attempt < 3; attempt++)
            limiter.TryAcquire($"user{attempt}@x.com", SomeAddress).ShouldBeTrue();

        limiter.TryAcquire("fresh@x.com", SomeAddress).ShouldBeFalse();
        limiter.TryAcquire("fresh@x.com", OtherAddress).ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_UnknownAddresses_ShareOnePartition()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 100, perAddressPermits: 2, globalPermits: 100);

        limiter.TryAcquire("a@x.com", null).ShouldBeTrue();
        limiter.TryAcquire("b@x.com", null).ShouldBeTrue();

        limiter.TryAcquire("c@x.com", null).ShouldBeFalse();
        limiter.TryAcquire("c@x.com", SomeAddress).ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_AllEmailsTogetherOverTheGlobalBudget_AreRefused()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 10, perAddressPermits: 100, globalPermits: 4);

        for (var attempt = 0; attempt < 4; attempt++)
            limiter.TryAcquire($"user{attempt}@x.com", $"203.0.113.{attempt}").ShouldBeTrue();

        limiter.TryAcquire("fresh@x.com", "203.0.113.99").ShouldBeFalse();
    }

    [Fact]
    public void TryAcquire_BlankEmail_StillCountsAgainstTheGlobalBudget()
    {
        using var limiter = new AuthAttemptLimiter(perEmailPermits: 10, perAddressPermits: 100, globalPermits: 2);

        limiter.TryAcquire("", SomeAddress).ShouldBeTrue();
        limiter.TryAcquire("", SomeAddress).ShouldBeTrue();
        limiter.TryAcquire("", SomeAddress).ShouldBeFalse();
    }
}
