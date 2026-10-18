using Microsoft.Extensions.AI;
using OllamaSharp.Models.Chat;

namespace FitEdu.Web.Services;

/// <summary>Ollama-specific settings for the coach's chat requests.</summary>
public static class OllamaChatSettings
{
    /// <summary>How long Ollama keeps the chat model loaded after a request (its default is 5
    /// minutes). Reloading qwen3:8b takes up to a minute, which made the first answer after a
    /// quiet spell very slow; the cost is GPU memory held while the app runs.</summary>
    public const string KeepModelLoadedFor = "1h";

    /// <summary>
    /// Applies the coach's request settings:
    /// <list type="bullet">
    /// <item>Thinking off. qwen3 "thinks" before it answers by default, and the thinking counts
    /// against <c>Assistant:MaxOutputTokens</c>, so answers (Greek ones above all) stopped
    /// mid-sentence. The coach answers short questions, so it runs without thinking.</item>
    /// <item>The model stays loaded for <see cref="KeepModelLoadedFor"/>.</item>
    /// </list>
    /// A request factory set by the caller is left alone.
    /// </summary>
    public static void Apply(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.RawRepresentationFactory ??= _ => new ChatRequest { Think = false, KeepAlive = KeepModelLoadedFor };
    }
}
