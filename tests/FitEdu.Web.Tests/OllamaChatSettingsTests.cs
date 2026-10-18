using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Live check 2026-10-18: qwen3 "thinks" before answering by default, and the thinking counted
/// against the 500-token answer limit, so Greek answers stopped mid-sentence. The coach's
/// requests to Ollama must switch thinking off. The request goes through OllamaSharp's own
/// chat client, so the test fails if the library stops honouring the setting.
/// </summary>
public class OllamaChatSettingsTests
{
    /// <summary>Records the request body and answers like Ollama's /api/chat.</summary>
    private sealed class RecordingOllamaHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            const string answer = """{"model":"qwen3:1.7b","created_at":"2026-10-18T00:00:00Z","message":{"role":"assistant","content":"ok"},"done":true,"done_reason":"stop"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer + "\n", Encoding.UTF8, "application/x-ndjson") };
        }
    }

    [Fact]
    public async Task CoachRequests_TurnThinkingOff()
    {
        using var handler = new RecordingOllamaHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var ollama = new OllamaApiClient(httpClient, "qwen3:1.7b");
        var options = new ChatOptions { MaxOutputTokens = 500 };

        OllamaChatSettings.TurnOffThinking(options);
        await ((IChatClient)ollama).GetResponseAsync("Ποια προγράμματα έχετε;", options);

        handler.RequestBody.ShouldNotBeNull().ShouldContain("\"think\":false");
    }

    [Fact]
    public void CoachRequests_KeepAnExistingRequestFactory()
    {
        Func<IChatClient, object?> existing = _ => null;
        var options = new ChatOptions { RawRepresentationFactory = existing };

        OllamaChatSettings.TurnOffThinking(options);

        options.RawRepresentationFactory.ShouldBeSameAs(existing);
    }
}
