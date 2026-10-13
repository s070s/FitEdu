using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using FitEdu.TestSupport;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The loopback API calls carry the member's UI language (Accept-Language), so the API answers in
/// the language of the page.
/// </summary>
public class AcceptLanguageHandlerTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AcceptLanguageHandlerTests(CustomWebAppFactory factory) => _factory = factory;

    private static StubHttpMessageHandler OkResponder() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK));

    [Fact]
    public async Task Handler_SendsTheCurrentUiLanguage()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");
        var recorder = OkResponder();
        using var client = new HttpClient(new AcceptLanguageHandler { InnerHandler = recorder });

        await client.GetAsync("http://localhost/api/thing");

        recorder.LastRequest.ShouldNotBeNull().Headers.AcceptLanguage.ToString().ShouldBe("el-GR");
    }

    [Fact]
    public async Task Handler_KeepsALanguageTheCallerSetItself()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");
        var recorder = OkResponder();
        using var client = new HttpClient(new AcceptLanguageHandler { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/thing");
        request.Headers.AcceptLanguage.ParseAdd("en-GB");

        await client.SendAsync(request);

        recorder.LastRequest.ShouldNotBeNull().Headers.AcceptLanguage.ToString().ShouldBe("en-GB");
    }

    [Fact]
    public async Task AppHttpClients_SendTheCurrentUiLanguage()
    {
        var recorder = OkResponder();
        using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = recorder))));
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");
        var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("SelfClient");

        await client.GetAsync("api/thing");

        recorder.LastRequest.ShouldNotBeNull().Headers.AcceptLanguage.ToString().ShouldBe("el-GR");
    }
}
