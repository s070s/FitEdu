using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>Exact security header values, shared by the Development and Production host tests.</summary>
internal static class SecurityHeaderAssertions
{
    /// <summary>The only CDN path the policy allows: Quill 2.0.3, the admin rich-text editor.</summary>
    public const string QuillSource = "https://cdn.jsdelivr.net/npm/quill@2.0.3/";

    public static void ShouldCarryTheSecurityHeaders(this HttpResponseMessage response, bool development)
    {
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
        response.Headers.GetValues("Permissions-Policy").ShouldBe(["camera=(self), microphone=(self), display-capture=(self), geolocation=()"]);

        // One policy header only: the framework's own frame-ancestors one on interactive pages would be a second.
        var policy = ParsePolicy(response.Headers.GetValues("Content-Security-Policy").Single());
        policy.Keys.Order().ShouldBe(
        [
            "base-uri", "connect-src", "default-src", "font-src", "form-action", "frame-ancestors", "frame-src",
            "img-src", "media-src", "object-src", "script-src", "style-src"
        ]);
        policy["default-src"].ShouldBe(["'self'"]);
        policy["script-src"].ShouldBe(["'self'", QuillSource]);
        // Blazor's reconnect popup builds a <style> element and many components bind style="@expr", so 'unsafe-inline' stays here.
        policy["style-src"].ShouldBe(["'self'", "'unsafe-inline'", QuillSource]);
        policy["font-src"].ShouldBe(["'self'", "data:"]);
        policy["img-src"].ShouldBe(["'self'", "data:", "blob:"]);
        policy["media-src"].ShouldBe(["'self'", "blob:"]);
        policy["frame-src"].ShouldBe(["'self'", "https://www.youtube.com", "https://www.youtube-nocookie.com"]);
        policy["object-src"].ShouldBe(["'none'"]);
        policy["base-uri"].ShouldBe(["'self'"]);
        policy["form-action"].ShouldBe(["'self'"]);
        policy["frame-ancestors"].ShouldBe(["'none'"]);
        // The development tools (browser link, hot reload) must never reach a production policy.
        policy["connect-src"].ShouldBe(development ? ["'self'", "http://localhost:*", "ws://localhost:*"] : ["'self'"]);

        policy["script-src"].ShouldNotContain(source => source.StartsWith("'unsafe-", StringComparison.Ordinal));
        var jsDelivrSources = policy
            .SelectMany(directive => directive.Value.Select(source => (Directive: directive.Key, Source: source)))
            .Where(entry => entry.Source.Contains("jsdelivr", StringComparison.Ordinal))
            .ToList();
        jsDelivrSources.ShouldBe([("script-src", QuillSource), ("style-src", QuillSource)], ignoreOrder: true);
    }

    /// <summary>A page without an editor links no CDN file and no Quill file; only the local loader script is there.</summary>
    public static void ShouldNotLoadQuillFromTheCdn(this string pageHtml)
    {
        pageHtml.ShouldContain("js/quill-interop");
        pageHtml.ShouldNotContain("jsdelivr", Case.Insensitive);
        pageHtml.ShouldNotContain("quill.js", Case.Insensitive);
        pageHtml.ShouldNotContain("quill.snow", Case.Insensitive);
    }

    private static Dictionary<string, string[]> ParsePolicy(string header) => header
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(directive => directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToDictionary(parts => parts[0], parts => parts[1..]);
}

/// <summary>A Production host reached over HTTPS under an allowed, non-localhost host name, whose blog service fails.</summary>
public sealed class ProductionSecurityHeadersFactory : ProductionWebAppFactory
{
    public const string HostName = "app.test.invalid";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // appsettings.json allows only localhost; added after it, so this value wins.
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = HostName }));
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // The real exception handler answers the failure, so the 500 comes from the production pipeline, not a test endpoint.
            var blog = Substitute.For<IBlogArticleService>();
            blog.GetPublishedSummariesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("the blog store is down"));
            services.RemoveAll<IBlogArticleService>();
            services.AddSingleton(blog);
        });
    }

    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{HostName}") });
}

/// <summary>The Development host: the looser connect-src, and no HSTS (a browser must not pin localhost to HTTPS).</summary>
public class DevelopmentSecurityHeadersTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public DevelopmentSecurityHeadersTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ApiResponse_CarriesTheExactSecurityHeaders()
    {
        var response = await _factory.CreateClient().GetAsync("/api/categories");

        response.ShouldCarryTheSecurityHeaders(development: true);
    }

    [Fact]
    public async Task PageResponse_CarriesTheExactSecurityHeaders()
    {
        var response = await _factory.CreateClient().GetAsync("/pricing");

        response.ShouldCarryTheSecurityHeaders(development: true);
    }

    [Fact]
    public async Task Development_SendsNoHsts()
    {
        var response = await _factory.CreateClient().GetAsync("/api/blog/summaries");

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task HomePage_LoadsNoQuillAndNoCdn()
    {
        var body = await _factory.CreateClient().GetStringAsync("/");

        body.ShouldNotLoadQuillFromTheCdn();
    }
}

/// <summary>The headers the deployed (Production) host sends.</summary>
public class ProductionSecurityHeadersTests : IClassFixture<ProductionSecurityHeadersFactory>
{
    private readonly ProductionSecurityHeadersFactory _factory;

    public ProductionSecurityHeadersTests(ProductionSecurityHeadersFactory factory) => _factory = factory;

    [Fact]
    public async Task ApiResponse_CarriesTheExactSecurityHeaders()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/api/categories");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.ShouldCarryTheSecurityHeaders(development: false);
    }

    [Fact]
    public async Task PageResponse_CarriesTheExactSecurityHeaders()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/pricing");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.ShouldCarryTheSecurityHeaders(development: false);
    }

    [Fact]
    public async Task ShippedImage_CarriesTheExactSecurityHeaders()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/images/hero-home.webp");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.ShouldCarryTheSecurityHeaders(development: false);
    }

    [Fact]
    public async Task Https_SendsHstsForOneYear_WithoutSubdomainsOrPreload()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/api/categories");

        response.Headers.GetValues("Strict-Transport-Security").ShouldBe(["max-age=31536000"]);
    }

    [Fact]
    public async Task UnhandledException_Is500ProblemDetails_StillWithTheSecurityHeaders()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/api/blog/summaries");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldNotContain("the blog store is down");
        // The exception handler clears the response, so the headers have to be set as the response starts.
        response.ShouldCarryTheSecurityHeaders(development: false);
    }

    [Fact]
    public async Task HomePage_LoadsNoQuillAndNoCdn()
    {
        var body = await _factory.CreateHttpsClient().GetStringAsync("/");

        body.ShouldNotLoadQuillFromTheCdn();
    }
}

/// <summary>quill-interop.js loads Quill itself, so what it asks the CDN for has to match the Content-Security-Policy.</summary>
public class QuillLazyLoadTests
{
    private static readonly string Script = File.ReadAllText(Path.Combine(RepoPaths.WebRoot, "js", "quill-interop.js"));

    [Theory]
    [InlineData("dist/quill.js", "sha256-9hV8cqybP1HN6tQmM1aIoCexJAXZ1qTardOKZ2stf/I=")]
    [InlineData("dist/quill.snow.css", "sha256-HHlIzROqkvrGOQMZvB5eRhgj2hcVGdOnaNtWFk+HFjY=")]
    public void QuillFile_IsLoadedFromTheAllowedPath_WithItsIntegrityHash(string file, string integrity)
    {
        Script.ShouldContain($"'{SecurityHeaderAssertions.QuillSource}{file}'");
        Script.ShouldContain($"'{integrity}'");
    }

    [Fact]
    public void QuillFiles_AreRequestedAsAnonymousCorsCalls_SoTheIntegrityCheckCanRun()
    {
        Script.ShouldContain("crossOrigin = 'anonymous'");
    }

    [Fact]
    public void QuillInterop_NamesNoOtherCdnAddress()
    {
        var addresses = Regex.Matches(Script, "https://[^'\" ]+").Select(match => match.Value).ToList();

        addresses.ShouldNotBeEmpty();
        addresses.ShouldAllBe(address => address.StartsWith(SecurityHeaderAssertions.QuillSource, StringComparison.Ordinal));
    }
}
