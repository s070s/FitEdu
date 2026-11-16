using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FitEdu.TestSupport;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// P03-6: a same-host reverse proxy that sends no X-Forwarded-For makes every visitor look like
/// loopback, which the rate limiters never limit; the app says so once in its log.
/// </summary>
public class LoopbackProxyWarningTests : IClassFixture<ProductionWebAppFactory>
{
    private const string Secret = "loopback-test-secret-that-is-long-enough-0123456";

    private readonly ProductionWebAppFactory _factory;

    public LoopbackProxyWarningTests(ProductionWebAppFactory factory) => _factory = factory;

    private static DefaultHttpContext RequestFrom(string? address, string? forwardedFor = null, string? sentSecret = null)
    {
        var marker = new SelfCallMarker(Secret, new Uri("https://app.example.test/"));
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(marker).BuildServiceProvider()
        };
        httpContext.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        if (forwardedFor is not null)
            httpContext.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (sentSecret is not null)
            httpContext.Request.Headers[SelfCallMarker.HeaderName] = sentSecret;

        return httpContext;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void LoopbackRequest_WithoutMarkerOrForwardedFor_IsReported(string address)
    {
        LoopbackProxyWarning.IsUnforwardedLoopback(RequestFrom(address)).ShouldBeTrue();
    }

    [Fact]
    public void LoopbackRequest_WithTheSelfCallSecret_IsTheAppsOwnCall()
    {
        LoopbackProxyWarning.IsUnforwardedLoopback(RequestFrom("127.0.0.1", sentSecret: Secret)).ShouldBeFalse();
    }

    [Fact]
    public void LoopbackRequest_WithAWrongSecret_IsReported()
    {
        LoopbackProxyWarning.IsUnforwardedLoopback(RequestFrom("127.0.0.1", sentSecret: "guess")).ShouldBeTrue();
    }

    [Fact]
    public void LoopbackRequest_WithForwardedFor_IsAProperlyConfiguredProxy()
    {
        LoopbackProxyWarning.IsUnforwardedLoopback(RequestFrom("127.0.0.1", forwardedFor: "203.0.113.7")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8::5")]
    [InlineData(null)]
    public void OutsideOrAddresslessRequest_IsNotReported(string? address)
    {
        LoopbackProxyWarning.IsUnforwardedLoopback(RequestFrom(address)).ShouldBeFalse();
    }

    private (HttpClient Client, RecordingLogger<LoopbackProxyWarning> Log, IDisposable App) ProductionHostSeenFrom(string address)
    {
        var log = new RecordingLogger<LoopbackProxyWarning>();
        var app = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogger<LoopbackProxyWarning>>(log);
            // First in the list = first in the pipeline, so the address is set before the warning looks at it.
            services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new RemoteAddressStartupFilter(IPAddress.Parse(address))));
        }));
        return (app.CreateClient(), log, app);
    }

    [Fact]
    public async Task ProductionHost_LogsTheLoopbackWarningOnce()
    {
        var (client, log, app) = ProductionHostSeenFrom("127.0.0.1");
        using var _ = app;

        await client.GetAsync("/health/live");
        await client.GetAsync("/health/live");

        log.Warnings.Count.ShouldBe(1);
        log.Warnings[0].ShouldContain("X-Forwarded-For");
    }

    [Fact]
    public async Task ProductionHost_ProxyThatSendsForwardedFor_LogsNothing()
    {
        var (client, log, app) = ProductionHostSeenFrom("127.0.0.1");
        using var _ = app;
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        await client.GetAsync("/health/live");

        log.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task DevelopmentHost_LogsNothing()
    {
        var log = new RecordingLogger<LoopbackProxyWarning>();
        using var developmentFactory = new CustomWebAppFactory();
        using var developmentApp = developmentFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogger<LoopbackProxyWarning>>(log);
            services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new RemoteAddressStartupFilter(IPAddress.Loopback)));
        }));

        await developmentApp.CreateClient().GetAsync("/health/live");

        log.Warnings.ShouldBeEmpty();
    }
}
