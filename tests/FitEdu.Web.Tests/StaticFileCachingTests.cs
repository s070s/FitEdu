using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>PERF-12: shipped images are cached for a week. PERF-01: CSS/JS/fonts are compressed and fingerprinted.</summary>
public partial class StaticFileCachingTests : IClassFixture<CustomWebAppFactory>
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
    public async Task WebManifest_IsServedAsAManifest()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/manifest+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"display\": \"standalone\"");
    }

    [Fact]
    public async Task FingerprintedStylesheet_IsServedCompressed()
    {
        var client = _factory.CreateClient();
        var stylesheetUrl = await FirstLocalHrefAsync(client, StylesheetHref());

        using var request = new HttpRequestMessage(HttpMethod.Get, stylesheetUrl);
        request.Headers.AcceptEncoding.ParseAdd("br, gzip");
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldNotBeEmpty();
    }

    // MapStaticAssets answers "no-cache" in Development (the test host) and can only read the shipped
    // files from a published layout, so the long-lived caching is checked in the build's endpoint
    // manifest, which is exactly what the production host serves headers from.
    [Fact]
    public async Task FingerprintedStylesheet_IsCachedForGood_InTheAssetManifest()
    {
        var client = _factory.CreateClient();
        var stylesheetUrl = await FirstLocalHrefAsync(client, StylesheetHref());
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "FitEdu.Web.staticwebassets.endpoints.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));

        var cacheControlValues = manifest.RootElement.GetProperty("Endpoints").EnumerateArray()
            .Where(endpoint => "/" + endpoint.GetProperty("Route").GetString() == stylesheetUrl)
            .SelectMany(endpoint => endpoint.GetProperty("ResponseHeaders").EnumerateArray())
            .Where(header => header.GetProperty("Name").GetString() == "Cache-Control")
            .Select(header => header.GetProperty("Value").GetString())
            .ToList();

        cacheControlValues.ShouldNotBeEmpty();
        cacheControlValues.ShouldAllBe(value => value!.Contains("immutable") && value.Contains("max-age=31536000"));
    }

    [Fact]
    public async Task HomePage_LinksTheLocalIconFont_NotJsDelivrIcons()
    {
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync("/");

        body.ShouldMatch("href=\"[^\"]*lib/bootstrap-icons/font/bootstrap-icons[.]min[.][A-Za-z0-9]+[.]css\"");
        body.ShouldNotContain("bootstrap-icons@");
        body.ShouldContain("rel=\"preload\" as=\"font\"");
    }

    [Fact]
    public async Task IconFont_IsServedWithTheUrlTheStylesheetAsks()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/lib/bootstrap-icons/font/fonts/bootstrap-icons.woff2?dd67030699838ea613ee6dbda90effa6");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("font/woff2");
    }

    [Fact]
    public async Task StaticResponses_StillCarrySecurityHeaders_AndIconsNeedNoCdnFontSource()
    {
        var client = _factory.CreateClient();
        var stylesheetUrl = await FirstLocalHrefAsync(client, StylesheetHref());

        var response = await client.GetAsync(stylesheetUrl);

        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        var contentSecurityPolicy = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
        contentSecurityPolicy.ShouldContain("font-src 'self' data:;");
    }

    private static Regex StylesheetHref() => LocalStylesheetPattern();

    private static async Task<string> FirstLocalHrefAsync(HttpClient client, Regex pattern)
    {
        var body = await client.GetStringAsync("/");
        var match = pattern.Match(body);
        match.Success.ShouldBeTrue("the home page links no local app stylesheet");
        return "/" + match.Groups["href"].Value;
    }

    [GeneratedRegex("href=\"(?<href>css/app[.][A-Za-z0-9]+[.]css)\"")]
    private static partial Regex LocalStylesheetPattern();
}
