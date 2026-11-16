using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Web.Consumers;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Lesson uploads (P05-4): a big video takes minutes, so it goes through its own client without the
/// 30-second consumer timeout and is bounded by one per-call timeout instead.
/// </summary>
public class AdminLessonConsumerTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AdminLessonConsumerTests(CustomWebAppFactory factory) => _factory = factory;

    private static ApiTokenProvider TokenProvider()
    {
        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        return new ApiTokenProvider(authState, Substitute.For<IAuthService>());
    }

    // Answers after a delay that stops early when the caller cancels, like a slow real response.
    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"videoPath\":\"lessons/clip.mp4\",\"pdfPath\":\"lessons/notes.pdf\"}", System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private static HttpClient Client(HttpMessageHandler handler, TimeSpan timeout) =>
        new(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = timeout };

    private static AdminLessonConsumer Build(HttpClient consumerClient, HttpClient uploadClient, TimeSpan? uploadTimeout = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AdminLessonConsumer.UploadClientName).Returns(uploadClient);
        return new AdminLessonConsumer(consumerClient, TokenProvider(), factory)
        {
            UploadTimeout = uploadTimeout ?? AdminLessonConsumer.DefaultUploadTimeout
        };
    }

    private static FakeBrowserFile SmallFile(string name, string contentType) => new(name, 1024, contentType);

    [Fact]
    public async Task UploadVideo_SlowerThanTheConsumerTimeout_StillSucceeds()
    {
        // The consumer client stands in for the 30-second client: it would cut this upload off.
        var consumerClient = Client(new SlowHandler(TimeSpan.FromMilliseconds(300)), TimeSpan.FromMilliseconds(50));
        var uploadHandler = new SlowHandler(TimeSpan.FromMilliseconds(300));
        var consumer = Build(consumerClient, Client(uploadHandler, Timeout.InfiniteTimeSpan));

        var path = await consumer.UploadVideoAsync(Guid.NewGuid(), SmallFile("clip.mp4", "video/mp4"));

        path.ShouldBe("lessons/clip.mp4");
        uploadHandler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task UploadPdf_SlowerThanTheConsumerTimeout_StillSucceeds()
    {
        var consumerClient = Client(new SlowHandler(TimeSpan.FromMilliseconds(300)), TimeSpan.FromMilliseconds(50));
        var consumer = Build(consumerClient, Client(new SlowHandler(TimeSpan.FromMilliseconds(300)), Timeout.InfiniteTimeSpan));

        var path = await consumer.UploadPdfAsync(Guid.NewGuid(), SmallFile("notes.pdf", "application/pdf"));

        path.ShouldBe("lessons/notes.pdf");
    }

    [Fact]
    public async Task UploadVideo_PastThePerCallTimeout_GivesUpWithoutThrowing()
    {
        var consumerClient = Client(new SlowHandler(TimeSpan.Zero), Timeout.InfiniteTimeSpan);
        var consumer = Build(consumerClient, Client(new SlowHandler(TimeSpan.FromSeconds(30)), Timeout.InfiniteTimeSpan),
            uploadTimeout: TimeSpan.FromMilliseconds(100));

        var path = await consumer.UploadVideoAsync(Guid.NewGuid(), SmallFile("clip.mp4", "video/mp4"));

        path.ShouldBeNull();
    }

    [Fact]
    public async Task UploadVideo_FileThatCannotBeRead_ReturnsNullInsteadOfThrowing()
    {
        var consumer = Build(Client(new SlowHandler(TimeSpan.Zero), Timeout.InfiniteTimeSpan),
            Client(new SlowHandler(TimeSpan.Zero), Timeout.InfiniteTimeSpan));
        var tooBig = new FakeBrowserFile("clip.mp4", long.MaxValue, "video/mp4");

        var path = await consumer.UploadVideoAsync(Guid.NewGuid(), tooBig);

        path.ShouldBeNull();
    }

    // Records the request it receives, so a test can read the headers the consumer put on it.
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"videoPath\":\"lessons/clip.mp4\"}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task UploadVideo_SendsTheSignedInUsersBearerToken()
    {
        // The upload client is not the consumer's own client, so the token must be on the request itself.
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));
        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync().Returns(Task.FromResult(new AuthenticationState(principal)));
        var authService = Substitute.For<IAuthService>();
        authService.GetTokenAsync(principal).Returns("lesson-upload-token");
        var uploadHandler = new RecordingHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AdminLessonConsumer.UploadClientName).Returns(Client(uploadHandler, Timeout.InfiniteTimeSpan));
        var consumer = new AdminLessonConsumer(
            Client(new SlowHandler(TimeSpan.Zero), Timeout.InfiniteTimeSpan), new ApiTokenProvider(authState, authService), factory);

        var path = await consumer.UploadVideoAsync(Guid.NewGuid(), SmallFile("clip.mp4", "video/mp4"));

        path.ShouldBe("lessons/clip.mp4");
        var authorization = uploadHandler.Request!.Headers.Authorization;
        authorization.ShouldNotBeNull();
        authorization.Scheme.ShouldBe("Bearer");
        authorization.Parameter.ShouldBe("lesson-upload-token");
    }

    [Fact]
    public void EachUploadCall_IsBoundedByThirtyMinutes()
    {
        AdminLessonConsumer.DefaultUploadTimeout.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void UploadClient_IsRegisteredWithoutAClientTimeout()
    {
        var client = _factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(AdminLessonConsumer.UploadClientName);

        client.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
        client.BaseAddress.ShouldNotBeNull();
    }
}
