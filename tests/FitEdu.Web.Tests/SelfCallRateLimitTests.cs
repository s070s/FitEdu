using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using FitEdu.TestSupport;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// A Production host whose own API consumers reach the in-memory test server. Outside Development the
/// app's base address is its real public one, so its own calls arrive from an outside address.
/// </summary>
public class ProductionSelfCallingWebAppFactory : ProductionWebAppFactory
{
    /// <summary>The secret the host is told to mark its own calls with.</summary>
    public const string SelfCallSecret = "self-call-test-secret-that-is-long-enough-1234567890";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The self-calls carry the host of SelfBaseUrl (app.test.invalid), which host filtering must accept.
        builder.UseSetting("AllowedHosts", "localhost;app.test.invalid");
        builder.UseSetting("SelfCall:Secret", SelfCallSecret);
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(SelfCallingWebAppFactory.RouteSelfCallsToTheTestServer);
    }
}

/// <summary>Reports every request as coming from one fixed address, like a visitor on the internet.</summary>
public sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = address;
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>
/// P03-1: in Production the app's own calls go to its public address, so they arrive from an outside
/// address; only the marker header keeps the page loads of all visitors from sharing one limit.
/// </summary>
public class SelfCallRateLimitTests : IClassFixture<ProductionSelfCallingWebAppFactory>
{
    private const string SelfCallHeader = "X-FitEdu-Self-Call";

    private readonly ProductionSelfCallingWebAppFactory _factory;

    public SelfCallRateLimitTests(ProductionSelfCallingWebAppFactory factory) => _factory = factory;

    // A fresh host per test, so each starts with unused limiter windows.
    private WebApplicationFactory<Program> HostSeenFromTheInternet() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            // First in the list = first in the pipeline, ahead of everything the app adds.
            services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(
                new RemoteAddressStartupFilter(IPAddress.Parse("203.0.113.10"))))));

    [Fact]
    public async Task OwnCalls_ArriveFromAnOutsideAddress_AreNeverLimited()
    {
        using var host = HostSeenFromTheInternet();
        // The loopback client every API consumer is built on (consumers themselves need a Blazor circuit).
        var ownClient = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("SelfClient");

        for (var call = 1; call <= PublicRateLimitPermits + 1; call++)
            (await ownClient.GetAsync("api/landing")).StatusCode.ShouldBe(HttpStatusCode.OK, $"own call {call}");
    }

    [Fact]
    public async Task OutsideCaller_IsStillLimited()
    {
        using var host = HostSeenFromTheInternet();
        var client = host.CreateClient();

        await AssertLimitIsReachedAsync(client);
    }

    [Fact]
    public async Task OutsideCaller_WithAWrongSelfCallHeader_IsStillLimited()
    {
        using var host = HostSeenFromTheInternet();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(SelfCallHeader, "not-the-secret");

        await AssertLimitIsReachedAsync(client);
    }

    [Fact]
    public async Task OutsideCaller_WhoLearnedTheSecretLength_ButNotTheSecret_IsStillLimited()
    {
        using var host = HostSeenFromTheInternet();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(SelfCallHeader, new string('x', ProductionSelfCallingWebAppFactory.SelfCallSecret.Length));

        await AssertLimitIsReachedAsync(client);
    }

    private const int PublicRateLimitPermits = PublicRateLimit.PermitsPerWindow;

    private static async Task AssertLimitIsReachedAsync(HttpClient client)
    {
        for (var call = 1; call <= PublicRateLimitPermits; call++)
            (await client.GetAsync("/api/landing")).StatusCode.ShouldBe(HttpStatusCode.OK, $"call {call}");

        (await client.GetAsync("/api/landing")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}

/// <summary>The marker sends its secret to the app's own address only, and checks it in constant time.</summary>
public class SelfCallMarkerTests
{
    private const string Secret = "unit-test-secret-that-is-long-enough-0123456789";
    private static readonly Uri OwnAddress = new("https://app.example.test/");

    private static HttpRequestMessage RequestTo(string address) => new(HttpMethod.Get, address);

    private static string? SentSecret(HttpRequestMessage request) =>
        request.Headers.TryGetValues(SelfCallMarker.HeaderName, out var values) ? values.Single() : null;

    [Theory]
    [InlineData("https://app.example.test/api/landing")]
    [InlineData("https://APP.example.test/api/landing")]
    [InlineData("https://app.example.test:443/api/landing")]
    public void Stamp_RequestToTheOwnAddress_SendsTheSecret(string address)
    {
        var request = RequestTo(address);

        new SelfCallMarker(Secret, OwnAddress).Stamp(request);

        SentSecret(request).ShouldBe(Secret);
    }

    [Theory]
    [InlineData("https://other.example.test/api/landing")]
    [InlineData("https://app.example.test.evil.test/api/landing")]
    [InlineData("https://app.example.test@evil.test/api/landing")]
    [InlineData("https://app.example.test:8443/api/landing")]
    [InlineData("http://app.example.test/api/landing")]
    [InlineData("https://localhost:7090/api/landing")]
    public void Stamp_RequestToAnotherAddress_SendsNothing(string address)
    {
        var request = RequestTo(address);

        new SelfCallMarker(Secret, OwnAddress).Stamp(request);

        SentSecret(request).ShouldBeNull();
    }

    [Fact]
    public void Stamp_RequestToAnotherAddress_RemovesAHeaderTheCallerSetItself()
    {
        var request = RequestTo("https://other.example.test/api/landing");
        request.Headers.Add(SelfCallMarker.HeaderName, "anything");

        new SelfCallMarker(Secret, OwnAddress).Stamp(request);

        SentSecret(request).ShouldBeNull();
    }

    [Fact]
    public void Stamp_RelativeRequest_SendsNothing()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/landing", UriKind.Relative));

        new SelfCallMarker(Secret, OwnAddress).Stamp(request);

        SentSecret(request).ShouldBeNull();
    }

    [Fact]
    public void WithoutAConfiguredSecret_EachStartMakesItsOwnLongSecret()
    {
        var first = RequestTo("https://app.example.test/");
        var second = RequestTo("https://app.example.test/");

        new SelfCallMarker(null, OwnAddress).Stamp(first);
        new SelfCallMarker(" ", OwnAddress).Stamp(second);

        SentSecret(first).ShouldNotBeNull().Length.ShouldBeGreaterThanOrEqualTo(SelfCallMarker.MinimumSecretLength);
        SentSecret(first).ShouldNotBe(SentSecret(second));
    }

    [Fact]
    public void ATooShortConfiguredSecret_IsRefusedWithoutNamingIt()
    {
        const string tooShort = "short-but-secret";

        var error = Should.Throw<InvalidOperationException>(() => new SelfCallMarker(tooShort, OwnAddress));

        error.Message.ShouldContain(SelfCallMarker.SecretSetting);
        error.Message.ShouldNotContain(tooShort);
    }

    [Theory]
    [InlineData(" long-enough-secret-with-a-leading-space-0123")]
    [InlineData("long-enough-secret-with-a-trailing-space-0123 ")]
    [InlineData("long-enough-secret-with-an-inner space-0123456")]
    [InlineData("long-enough-secret-with-a-non-ascii-é-0123456")]
    public void AConfiguredSecretThatAHeaderCannotCarryUnchanged_IsRefusedWithoutNamingIt(string secret)
    {
        secret.Length.ShouldBeGreaterThanOrEqualTo(SelfCallMarker.MinimumSecretLength);

        var error = Should.Throw<InvalidOperationException>(() => new SelfCallMarker(secret, OwnAddress));

        error.Message.ShouldContain(SelfCallMarker.SecretSetting);
        error.Message.ShouldNotContain(secret);
    }

    private static DefaultHttpContext ContextWith(SelfCallMarker marker, params string[] sentSecrets)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(marker).BuildServiceProvider()
        };
        if (sentSecrets.Length > 0)
            httpContext.Request.Headers[SelfCallMarker.HeaderName] = sentSecrets;

        return httpContext;
    }

    [Fact]
    public void IsSelfCall_MatchingSecret_IsTrue()
    {
        SelfCallMarker.IsSelfCall(ContextWith(new SelfCallMarker(Secret, OwnAddress), Secret)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData("")]
    [InlineData(Secret + "x")]
    public void IsSelfCall_WrongSecret_IsFalse(string sentSecret)
    {
        SelfCallMarker.IsSelfCall(ContextWith(new SelfCallMarker(Secret, OwnAddress), sentSecret)).ShouldBeFalse();
    }

    [Fact]
    public void IsSelfCall_NoHeader_IsFalse()
    {
        SelfCallMarker.IsSelfCall(ContextWith(new SelfCallMarker(Secret, OwnAddress))).ShouldBeFalse();
    }

    [Fact]
    public void IsSelfCall_HeaderSentTwice_IsFalse()
    {
        SelfCallMarker.IsSelfCall(ContextWith(new SelfCallMarker(Secret, OwnAddress), Secret, Secret)).ShouldBeFalse();
    }

    [Fact]
    public void IsSelfCall_HostWithoutAMarker_IsFalse()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[SelfCallMarker.HeaderName] = Secret;

        SelfCallMarker.IsSelfCall(httpContext).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Limiters_NeverLimitAMarkedCall_ButLimitAnOutsideCallerWithAnyOtherHeader(bool sendsTheSecret)
    {
        var httpContext = ContextWith(new SelfCallMarker(Secret, OwnAddress), sendsTheSecret ? Secret : "guess");
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        using var publicLimiter = PartitionedRateLimiter.Create<HttpContext, string>(PublicRateLimit.PartitionFor);
        using var authLimiter = PartitionedRateLimiter.Create<HttpContext, string>(AuthRateLimit.PartitionFor);

        var publicLimitReached = Enumerable.Range(0, PublicRateLimit.PermitsPerWindow * 2)
            .Any(_ => !publicLimiter.AttemptAcquire(httpContext).IsAcquired);
        var authLimitReached = Enumerable.Range(0, AuthRateLimit.PermitsPerWindow * 2)
            .Any(_ => !authLimiter.AttemptAcquire(httpContext).IsAcquired);

        publicLimitReached.ShouldBe(!sendsTheSecret);
        authLimitReached.ShouldBe(!sendsTheSecret);
    }
}

/// <summary>Every HttpClient of the app carries the marker to its own address and to no other host.</summary>
public class SelfCallHandlerTests : IClassFixture<CustomWebAppFactory>
{
    private const string Secret = "handler-test-secret-that-is-long-enough-012345";

    private readonly CustomWebAppFactory _factory;

    public SelfCallHandlerTests(CustomWebAppFactory factory) => _factory = factory;

    private (WebApplicationFactory<Program> App, StubHttpMessageHandler Recorder) AppRecordingItsRequests()
    {
        var recorder = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var app = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(SelfCallMarker.SecretSetting, Secret);
            builder.ConfigureTestServices(services => services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = recorder)));
        });
        return (app, recorder);
    }

    [Fact]
    public async Task LoopbackClient_SendsTheSecretToTheOwnAddress()
    {
        var (app, recorder) = AppRecordingItsRequests();
        using var _ = app;
        var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("SelfClient");

        await client.GetAsync("api/landing");

        recorder.LastRequest.ShouldNotBeNull().Headers.GetValues(SelfCallMarker.HeaderName).ShouldBe([Secret]);
    }

    [Fact]
    public async Task AnyOtherClientOfTheApp_NeverSendsTheSecretToAnotherHost()
    {
        var (app, recorder) = AppRecordingItsRequests();
        using var _ = app;
        var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();

        await client.GetAsync("https://other.example.test/api/landing");

        recorder.LastRequest.ShouldNotBeNull().Headers.Contains(SelfCallMarker.HeaderName).ShouldBeFalse();
    }
}
