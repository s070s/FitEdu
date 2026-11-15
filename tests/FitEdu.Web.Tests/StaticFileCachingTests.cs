using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitEdu.TestSupport;
using FitEdu.Web.Startup;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task ShippedImage_IsServedWithItsImageContentType()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/images/hero-home.webp");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
        (await response.Content.ReadAsByteArrayAsync()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task UnknownPage_Get_Is404_Not405()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/no-such-page")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/images/no-such-image.webp")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnknownApiUrl_Post_Is404()
    {
        var client = _factory.CreateClient();

        (await client.PostAsync("/api/no-such-endpoint", content: null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // A real API route keeps answering a wrong method with 405 (no catch-all route takes it over).
    [Fact]
    public async Task RealApiRoute_WrongMethod_Is405()
    {
        var client = _factory.CreateClient();

        (await client.DeleteAsync("/api/blog/summaries")).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    // The static asset endpoint short-circuits before authentication, so a stale auth cookie (here: the user
    // was disabled after signing in) neither costs a user lookup nor gets a sign-out Set-Cookie next to "immutable".
    [Fact]
    public async Task FingerprintedStylesheet_ForADisabledUsersCookie_HasNoSetCookie()
    {
        var email = $"static-cookie-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        var authCookie = await SignInCookieAsync(email);
        await DisableUserAsync(email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var stylesheetUrl = await FirstLocalHrefAsync(client, StylesheetHref());

        using var request = new HttpRequestMessage(HttpMethod.Get, stylesheetUrl);
        request.Headers.Add("Cookie", authCookie);
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
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
    public async Task HomePage_PreloadsTheExactFontUrlTheIconStylesheetRequests()
    {
        var client = _factory.CreateClient();
        var iconStylesheet = Path.Combine(RepoPaths.WebRoot, "lib", "bootstrap-icons", "font", "bootstrap-icons.min.css");
        var fontUrl = Regex.Match(await File.ReadAllTextAsync(iconStylesheet), "fonts/bootstrap-icons[.]woff2[?][A-Za-z0-9]+");
        fontUrl.Success.ShouldBeTrue("the icon stylesheet names no versioned woff2 url");

        var body = await client.GetStringAsync("/");

        body.ShouldContain("href=\"/lib/bootstrap-icons/font/" + fontUrl.Value + "\"");
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

    private async Task<string> SignInCookieAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var loginResult = await authService.LoginAsync(new LoginRequestDto { Email = email, Password = CustomWebAppFactory.TestPassword });
        loginResult.Success.ShouldBeTrue();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var response = await client.GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(loginResult.Token!)}&returnUrl=%2F");
        var setCookie = response.Headers.GetValues("Set-Cookie").First(value => value.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal));
        return setCookie.Split(';')[0];
    }

    private async Task DisableUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user!.IsEnabled = false;
        (await userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
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

/// <summary>A Production host: the only way to see Release-only behaviour (the endpoint manifest's real headers).</summary>
public class ProductionWebAppFactory : CustomWebAppFactory
{
    protected override string EnvironmentName => "Production";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Startup validation demands an SMTP host and a real self URL outside Development; nothing here sends mail.
        builder.UseSetting("Email:Smtp:Host", "smtp.test.invalid");
        builder.UseSetting("SelfBaseUrl", "https://app.test.invalid");
        base.ConfigureWebHost(builder);
    }
}

/// <summary>Cache headers and 404s as the deployed (non-Development) host serves them.</summary>
public partial class ProductionStaticFileTests : IClassFixture<ProductionWebAppFactory>
{
    private readonly ProductionWebAppFactory _factory;

    public ProductionStaticFileTests(ProductionWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ShippedImage_IsCachedForAWeek()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/images/hero-home.webp");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().Replace(" ", "").ShouldBe(StaticFileCaching.ImageCacheControl);
    }

    // The Production host in the test run reads the build-time asset manifest, whose endpoints always answer
    // "no-cache" (the "immutable" headers only apply to a published layout; see
    // FingerprintedStylesheet_IsCachedForGood_InTheAssetManifest). So this only proves the fingerprinted
    // stylesheet is found and served, and never gets the image cache header.
    [Fact]
    public async Task FingerprintedStylesheet_IsServed()
    {
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/");
        var match = FingerprintedStylesheetPattern().Match(body);
        match.Success.ShouldBeTrue("the home page links no fingerprinted app stylesheet");

        var response = await client.GetAsync("/" + match.Groups["href"].Value);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/css");
        response.Headers.CacheControl?.ToString().ShouldNotContain("604800");
    }

    [Fact]
    public async Task UnknownApiUrl_Post_Is404()
    {
        var client = _factory.CreateClient();

        (await client.PostAsync("/api/no-such", content: null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnknownPage_Get_Is404()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/no-such-page")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [GeneratedRegex("href=\"(?<href>css/app[.][A-Za-z0-9]+[.]css)\"")]
    private static partial Regex FingerprintedStylesheetPattern();
}
