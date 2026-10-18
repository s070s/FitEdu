using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using FitEdu.Application.DTOs;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace FitEdu.Web.Tests;

/// <summary>
/// Live smoke test against a locally running Ollama — guarded by FITEDU_OLLAMA_LIVE=1 so it
/// self-passes (effectively skips) in CI and on machines without Ollama. Run with:
///   FITEDU_OLLAMA_LIVE=1 dotnet test --filter AssistantLiveSmoke
/// Unloads the chat model first so the app has to load it (the cold-start path), boots its own
/// factory with the assistant enabled (the shared "web" host keeps it off), waits for the
/// bootstrap to reach Ready, then asserts that real chat turns in English and Greek stream
/// tokens and finish with a done event.
/// </summary>
public class AssistantLiveSmokeTests(ITestOutputHelper output)
{
    private const string OllamaBaseUrl = "http://localhost:11434";
    private const string ChatModel = "qwen3:1.7b";

    private static bool LiveEnabled =>
        Environment.GetEnvironmentVariable("FITEDU_OLLAMA_LIVE") == "1";

    private static readonly (string Language, string Question)[] Questions =
    [
        ("en", "What programmes do you offer?"),
        ("el", "Ποια προγράμματα έχετε;"),
        ("en", "Find me an exercise for the chest.")
    ];

    private sealed class LiveAssistantFactory : CustomWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Re-enable the assistant for this host only (the base factory pins the
            // environment variable off for every other test).
            builder.UseSetting("Assistant:Enabled", "true");
        }
    }

    [Fact]
    public async Task Chat_AgainstLocalOllama_StreamsTokensAndCompletes()
    {
        if (!LiveEnabled)
        {
            output.WriteLine("FITEDU_OLLAMA_LIVE != 1 — live smoke test skipped.");
            return;
        }

        await UnloadChatModelAsync();

        await using var factory = new LiveAssistantFactory();
        var client = await factory.CreateAuthenticatedClientAsync("Student");
        client.Timeout = TimeSpan.FromMinutes(5);

        // Wait for the bootstrap supervisor to report Ready (models may still be pulling).
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(4);
        AssistantStatusDto? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await client.GetFromJsonAsync<AssistantStatusDto>("/api/assistant/status");
            output.WriteLine($"status: {status?.State} {status?.Progress}");
            if (status is { State: "Ready" })
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        status.ShouldNotBeNull();
        status!.State.ShouldBe("Ready", $"assistant never became Ready (last: {status.State} {status.Progress})");

        foreach (var (language, question) in Questions)
            await AskAsync(client, language, question);
    }

    /// <summary>Asks one question and asserts the answer streams and completes.</summary>
    private async Task AskAsync(HttpClient client, string language, string question)
    {
        var response = await client.PostAsJsonAsync($"/api/assistant/chat?lang={language}",
            new AssistantChatRequest([new AssistantMessageDto("user", question)]));
        response.EnsureSuccessStatusCode();

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var parser = SseParser.Create(stream, (string _, ReadOnlySpan<byte> data) =>
            JsonSerializer.Deserialize<AssistantStreamEvent>(data, jsonOptions)!);

        var kinds = new List<string>();
        var text = new System.Text.StringBuilder();
        await foreach (var item in parser.EnumerateAsync())
        {
            kinds.Add(item.Data.Kind);
            if (item.Data.Kind == "token")
                text.Append(item.Data.Text);
        }

        output.WriteLine($"[{language}] {question} -> events: {string.Join(",", kinds.Distinct())}; answer: {text}");
        kinds.ShouldContain("token");
        kinds.Last().ShouldBe("done");
        text.Length.ShouldBeGreaterThan(0);
    }

    /// <summary>keep_alive 0 tells Ollama to drop the model from memory now.</summary>
    private static async Task UnloadChatModelAsync()
    {
        using var ollama = new HttpClient { BaseAddress = new Uri(OllamaBaseUrl) };
        var response = await ollama.PostAsJsonAsync("/api/generate", new { model = ChatModel, keep_alive = 0 });
        response.EnsureSuccessStatusCode();
    }
}
