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
    [InlineData("2001:db8::5", "2001:db8::5")]
    public void PartitionKeyFor_OutsideAddress_IsItsOwnBucket(string address, string expectedKey)
    {
        PublicRateLimit.PartitionKeyFor(IPAddress.Parse(address)).ShouldBe(expectedKey);
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
    public void AnonymousReads_UseThePublicPolicy(string route, string method)
    {
        PolicyFor(route, method).ShouldBe(PublicRateLimit.PolicyName);
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
