using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The "auth" limiter counts each outside address on its own, so one caller using up its attempts
/// cannot lock every other visitor out of sign-in, and never limits the app's own loopback calls.
/// </summary>
public class AuthRateLimitTests
{
    private static DefaultHttpContext ContextFrom(string? address)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        return httpContext;
    }

    [Fact]
    public void PartitionFor_DifferentAddresses_AreDifferentBuckets()
    {
        var first = AuthRateLimit.PartitionFor(ContextFrom("203.0.113.7"));
        var second = AuthRateLimit.PartitionFor(ContextFrom("203.0.113.8"));
        var firstAgain = AuthRateLimit.PartitionFor(ContextFrom("203.0.113.7"));

        first.PartitionKey.ShouldNotBe(second.PartitionKey);
        first.PartitionKey.ShouldBe(firstAgain.PartitionKey);
    }

    [Fact]
    public void Limiter_AddressThatUsedItsAttempts_DoesNotBlockAnotherAddress()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(AuthRateLimit.PartitionFor);
        var busyCaller = ContextFrom("203.0.113.7");
        var otherCaller = ContextFrom("203.0.113.8");

        for (var attempt = 0; attempt < AuthRateLimit.PermitsPerWindow; attempt++)
            limiter.AttemptAcquire(busyCaller).IsAcquired.ShouldBeTrue();

        limiter.AttemptAcquire(busyCaller).IsAcquired.ShouldBeFalse();
        limiter.AttemptAcquire(otherCaller).IsAcquired.ShouldBeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData(null)]
    public void Limiter_LoopbackAndAddresslessCalls_AreNeverLimited(string? address)
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(AuthRateLimit.PartitionFor);
        var caller = ContextFrom(address);

        for (var attempt = 0; attempt < AuthRateLimit.PermitsPerWindow * 3; attempt++)
            limiter.AttemptAcquire(caller).IsAcquired.ShouldBeTrue();
    }
}
