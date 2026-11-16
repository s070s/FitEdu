using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>Exact security header values, shared by the Development and Production host tests.</summary>
internal static class SecurityHeaderAssertions
{
    public static void ShouldCarryTheBaselineHeaders(this HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
        response.Headers.GetValues("Permissions-Policy").ShouldBe(["camera=(self), microphone=(self), display-capture=(self), geolocation=()"]);
        response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
    }
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

/// <summary>The Development host sends no HSTS: a browser must not pin localhost to HTTPS.</summary>
public class DevelopmentSecurityHeadersTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public DevelopmentSecurityHeadersTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Development_SendsNoHsts()
    {
        var response = await _factory.CreateClient().GetAsync("/api/blog/summaries");

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }
}

/// <summary>The headers the deployed (Production) host sends.</summary>
public class ProductionSecurityHeadersTests : IClassFixture<ProductionSecurityHeadersFactory>
{
    private readonly ProductionSecurityHeadersFactory _factory;

    public ProductionSecurityHeadersTests(ProductionSecurityHeadersFactory factory) => _factory = factory;

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
        response.ShouldCarryTheBaselineHeaders();
    }
}
