using System.Net;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>PERF-12: shipped images are cached for a week; other static files keep the default.</summary>
public class StaticFileCachingTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public StaticFileCachingTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ShippedImage_IsCachedForAWeek()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/images/hero-home.webp");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().Replace(" ", "").ShouldBe(StaticFileCaching.ImageCacheControl);
    }

    [Fact]
    public async Task OtherStaticFile_KeepsTheDefaultCaching()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldBeNull();
    }

    [Fact]
    public async Task WebManifest_IsServedAsAManifest()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/manifest+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"display\": \"standalone\"");
    }
}
