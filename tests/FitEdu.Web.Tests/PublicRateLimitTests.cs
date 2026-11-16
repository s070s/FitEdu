using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The "public" limiter counts each outside address on its own, never limits the app's own
/// loopback calls (every guest's page reaches the API through them) and guards every anonymous read.
/// </summary>
public class PublicRateLimitTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public PublicRateLimitTests(CustomWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void PartitionKeyFor_Loopback_IsNotLimited(string address)
    {
        PublicRateLimit.PartitionKeyFor(IPAddress.Parse(address)).ShouldBeNull();
    }

    [Fact]
    public void PartitionKeyFor_NoAddress_IsNotLimited()
    {
        PublicRateLimit.PartitionKeyFor(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8::5", "2001:db8::/64")]
    public void PartitionKeyFor_OutsideAddress_IsItsOwnBucket(string address, string expectedKey)
    {
        PublicRateLimit.PartitionKeyFor(IPAddress.Parse(address)).ShouldBe(expectedKey);
    }

    // A home connection gets a whole /64 from its provider, so every address in it is one caller.
    [Fact]
    public void PartitionKeyFor_Ipv6AddressesInOneSlash64_ShareABucket()
    {
        var first = PublicRateLimit.PartitionKeyFor(IPAddress.Parse("2001:db8:1:2::5"));
        var second = PublicRateLimit.PartitionKeyFor(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));

        second.ShouldBe(first);
    }

    [Fact]
    public void PartitionKeyFor_Ipv6AddressesInDifferentSlash64s_AreDifferentBuckets()
    {
        var first = PublicRateLimit.PartitionKeyFor(IPAddress.Parse("2001:db8:1:2::5"));
        var second = PublicRateLimit.PartitionKeyFor(IPAddress.Parse("2001:db8:1:3::5"));

        second.ShouldNotBe(first);
    }

    [Theory]
    [InlineData("api/exercises", "GET")]
    [InlineData("api/exercises/facets", "GET")]
    [InlineData("api/exercises/{slug}", "GET")]
    [InlineData("api/exercises/{slug}/lessons", "GET")]
    [InlineData("api/landing", "GET")]
    [InlineData("api/courses", "GET")]
    [InlineData("api/courses/{courseId:guid}", "GET")]
    [InlineData("api/courses/{courseId:guid}/reviews", "GET")]
    [InlineData("api/categories", "GET")]
    [InlineData("api/paths", "GET")]
    [InlineData("api/paths/{id:guid}", "GET")]
    [InlineData("api/blog/summaries", "GET")]
    [InlineData("api/blog/{slug}", "GET")]
    [InlineData("api/testimonials", "GET")]
    [InlineData("api/search", "GET")]
    [InlineData("api/subscriptions/plans", "GET")]
    [InlineData("api/certificates/verify/{verificationId:guid}", "GET")]
    [InlineData("/sitemap.xml", "GET")]
    public void AnonymousReads_UseThePublicPolicy(string route, string method)
    {
        PolicyFor(route, method).ShouldBe(PublicRateLimit.PolicyName);
    }

    // The browser fetches these with every page, so a per-address limit would break pages behind a
    // shared address (an office or a mobile network); README lists them as exempt.
    [Theory]
    [InlineData("api/site/background-image", "GET")]
    [InlineData("api/media/{*filePath}", "GET")]
    public void FilesTheBrowserFetchesPerPage_AreDeliberatelyNotLimited(string route, string method)
    {
        PolicyFor(route, method).ShouldBeNull();
    }

    [Theory]
    [InlineData("api/courses/{courseId:guid}/enroll", "POST")]
    [InlineData("api/courses/lessons/{lessonId:guid}", "GET")]
    public void MemberActions_DoNotUseThePublicPolicy(string route, string method)
    {
        PolicyFor(route, method).ShouldNotBe(PublicRateLimit.PolicyName);
    }

    private string? PolicyFor(string route, string method)
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var endpoint = endpoints.Single(candidate =>
            candidate.RoutePattern.RawText == route &&
            (candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));

        return endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
    }
}
