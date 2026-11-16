using System.Net;
using System.Security.Cryptography;
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
        // Quill is vendored in wwwroot/lib/quill, so neither directive names a third-party host for it.
        policy["script-src"].ShouldBe(["'self'"]);
        // Blazor's reconnect popup builds a <style> element and many components bind style="@expr", so 'unsafe-inline' stays here.
        policy["style-src"].ShouldBe(["'self'", "'unsafe-inline'"]);
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
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldNotContain("jsdelivr", Case.Insensitive);
    }

    /// <summary>A page without an editor links no Quill file and no CDN address; only the local loader script is there.</summary>
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
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = HostName,
                // The test server has no listening address, so the HTTPS redirect needs its port named.
                ["HTTPS_PORT"] = "443"
            }));
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
        // The exception handler runs its own small pipeline, which needs HSTS too.
        response.Headers.GetValues("Strict-Transport-Security").ShouldBe(["max-age=31536000"]);
    }

    [Fact]
    public async Task HttpToHttpsRedirect_CarriesTheSecurityHeaders()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{ProductionSecurityHeadersFactory.HostName}"),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/pricing");

        response.StatusCode.ShouldBe(HttpStatusCode.RedirectKeepVerb);
        response.Headers.Location?.Scheme.ShouldBe("https");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task HomePage_LoadsNoQuillAndNoCdn()
    {
        var body = await _factory.CreateHttpsClient().GetStringAsync("/");

        body.ShouldNotLoadQuillFromTheCdn();
    }
}

/// <summary>quill-interop.js loads the vendored Quill itself, so what it asks for has to be the files in wwwroot/lib/quill with their integrity hashes.</summary>
public class QuillLazyLoadTests
{
    private const string QuillFolder = "lib/quill/2.0.3/";

    private static readonly string Script = File.ReadAllText(Path.Combine(RepoPaths.WebRoot, "js", "quill-interop.js"));

    [Theory]
    [InlineData("quill.js")]
    [InlineData("quill.snow.css")]
    public void QuillFile_IsLoadedFromTheVendoredFolder_WithItsIntegrityHash(string file)
    {
        var vendoredBytes = File.ReadAllBytes(Path.Combine(RepoPaths.WebRoot, "lib", "quill", "2.0.3", file));
        var integrity = "sha384-" + Convert.ToBase64String(SHA384.HashData(vendoredBytes));

        Script.ShouldContain($"'/{QuillFolder}{file}'");
        Script.ShouldContain($"'{integrity}'");
    }

    [Fact]
    public void QuillLicence_ShipsNextToTheVendoredFiles()
    {
        File.ReadAllText(Path.Combine(RepoPaths.WebRoot, "lib", "quill", "2.0.3", "LICENSE")).ShouldContain("Redistribution and use in source and binary forms");
    }

    [Fact]
    public void QuillFiles_AreRequestedAsAnonymousCorsCalls()
    {
        Script.ShouldContain("crossOrigin = 'anonymous'");
    }

    [Fact]
    public void QuillInterop_NamesNoOutsideAddress()
    {
        Regex.Matches(Script, "https?://[^'\" ]+").ShouldBeEmpty();
    }
}
