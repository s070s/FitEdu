using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>A Production host reached over HTTPS under an allowed, non-localhost host name.</summary>
public sealed class ProductionSecurityHeadersFactory : ProductionWebAppFactory
{
    public const string HostName = "app.test.invalid";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // appsettings.json allows only localhost; added after it, so this value wins.
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = HostName }));
        base.ConfigureWebHost(builder);
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
}
