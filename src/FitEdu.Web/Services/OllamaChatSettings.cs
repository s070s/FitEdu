using Microsoft.Extensions.AI;
using OllamaSharp.Models.Chat;

namespace FitEdu.Web.Services;

/// <summary>Ollama-specific settings for the coach's chat requests.</summary>
public static class OllamaChatSettings
{
    /// <summary>
    /// qwen3 "thinks" before it answers by default, and the thinking counts against
    /// <c>Assistant:MaxOutputTokens</c>, so answers (Greek ones above all) stopped mid-sentence.
    /// The coach answers short questions, so it runs without thinking: complete answers, faster.
    /// A request factory set by the caller is left alone.
    /// </summary>
    public static void TurnOffThinking(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.RawRepresentationFactory ??= _ => new ChatRequest { Think = false };
    }
}
