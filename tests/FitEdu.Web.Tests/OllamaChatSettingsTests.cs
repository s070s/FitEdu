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
/// against the 500-token answer limit, so Greek answers stopped mid-sentence; and Ollama unloaded
/// the model after 5 idle minutes, so the next answer waited up to a minute for a reload. The
/// requests go through OllamaSharp's own chat client, so the tests fail if the library stops
/// honouring the settings.
/// </summary>
public class OllamaChatSettingsTests
{
    /// <summary>Records the request body and answers like Ollama's /api/chat.</summary>
    private sealed class RecordingOllamaHandler(string doneReason = "stop") : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var answer = $$"""{"model":"qwen3:8b","created_at":"2026-10-18T00:00:00Z","message":{"role":"assistant","content":"ok"},"done":true,"done_reason":"{{doneReason}}"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer + "\n", Encoding.UTF8, "application/x-ndjson") };
        }
    }

    private static async Task<(ChatResponse Response, string? RequestBody)> AskAsync(string doneReason = "stop")
    {
        using var handler = new RecordingOllamaHandler(doneReason);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var ollama = new OllamaApiClient(httpClient, "qwen3:8b");
        var options = new ChatOptions { MaxOutputTokens = 800 };

        OllamaChatSettings.Apply(options);
        var response = await ((IChatClient)ollama).GetResponseAsync("Ποια προγράμματα έχετε;", options);
        return (response, handler.RequestBody);
    }

    [Fact]
    public async Task CoachRequests_TurnThinkingOff()
    {
        var (_, requestBody) = await AskAsync();

        requestBody.ShouldNotBeNull().ShouldContain("\"think\":false");
    }

    [Fact]
    public async Task CoachRequests_KeepTheModelLoaded()
    {
        var (_, requestBody) = await AskAsync();

        requestBody.ShouldNotBeNull().ShouldContain($"\"keep_alive\":\"{OllamaChatSettings.KeepModelLoadedFor}\"");
    }

    [Fact]
    public async Task AnswerCutOffByTheLengthLimit_IsReportedAsLength()
    {
        // AssistantService relies on this to tell the widget to end the answer at a full sentence.
        var (response, _) = await AskAsync(doneReason: "length");

        response.FinishReason.ShouldBe(ChatFinishReason.Length);
    }

    [Fact]
    public void CoachRequests_KeepAnExistingRequestFactory()
    {
        Func<IChatClient, object?> existing = _ => null;
        var options = new ChatOptions { RawRepresentationFactory = existing };

        OllamaChatSettings.Apply(options);

        options.RawRepresentationFactory.ShouldBeSameAs(existing);
    }
}
