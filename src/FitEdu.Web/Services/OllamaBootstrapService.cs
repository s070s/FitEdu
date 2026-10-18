using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using FitEdu.Application.Common;

namespace FitEdu.Web.Services;

/// <summary>
/// Supervises the Ollama sidecar so a fresh machine needs zero manual model management:
/// probes <c>Assistant:BaseUrl</c>, auto-pulls any missing chat/embedding model (unless
/// <c>Assistant:AutoPullModels</c> is off), and drives <see cref="AssistantRuntimeState"/>
/// through OllamaUnreachable → DownloadingModels → Ready. Keeps supervising after Ready so
/// an Ollama restart (or late install) is detected and recovered from without an app restart.
/// Never throws out of <see cref="ExecuteAsync"/>.
/// </summary>
public sealed partial class OllamaBootstrapService(
    IOptions<AssistantOptions> assistantOptions,
    AssistantRuntimeState state,
    ILogger<OllamaBootstrapService> logger) : BackgroundService
{
    private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private readonly AssistantOptions _options = assistantOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return; // state stays Disabled

        var delay = MinDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await EnsureReadyAsync(stoppingToken))
                {
                    if (state.Status != AssistantAvailability.Ready)
                        LogAssistantReady(logger, _options.ChatModel, _options.EmbeddingModel);
                    state.Set(AssistantAvailability.Ready);
                    delay = MaxDelay; // supervision cadence once healthy
                }
                else
                {
                    delay = Grow(delay);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (state.Status != AssistantAvailability.OllamaUnreachable)
                    LogOllamaUnreachable(logger, _options.BaseUrl, ex.Message);
                state.Set(AssistantAvailability.OllamaUnreachable);
                delay = Grow(delay);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Probes Ollama and pulls whatever is missing. True when both models are present.</summary>
    private async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var api = new OllamaApiClient(new Uri(_options.BaseUrl));

        var missing = await GetMissingModelsAsync(api, cancellationToken);
        if (missing.Count == 0)
            return true;

        if (!_options.AutoPullModels)
        {
            state.Set(AssistantAvailability.OllamaUnreachable,
                $"missing model(s): {string.Join(", ", missing)} (AutoPullModels is off)");
            return false;
        }

        foreach (var model in missing)
        {
            LogPullingModel(logger, model);
            state.Set(AssistantAvailability.DownloadingModels, $"{model} — 0%");

            var lastLoggedPercent = -10d;
            await foreach (var progress in api.PullModelAsync(new PullModelRequest { Model = model }, cancellationToken))
            {
                if (progress is null)
                    continue;

                var percent = progress.Percent;
                state.Set(AssistantAvailability.DownloadingModels, $"{model} — {percent:0}%");
                if (percent - lastLoggedPercent >= 10)
                {
                    LogPullProgress(logger, model, percent, progress.Status);
                    lastLoggedPercent = percent;
                }
            }

            LogPulledModel(logger, model);
        }

        // Verify the pulls actually landed before reporting Ready.
        return (await GetMissingModelsAsync(api, cancellationToken)).Count == 0;
    }

    private async Task<List<string>> GetMissingModelsAsync(OllamaApiClient api, CancellationToken cancellationToken)
    {
        var local = (await api.ListLocalModelsAsync(cancellationToken)).Select(m => m.Name).ToList();
        return new[] { _options.ChatModel, _options.EmbeddingModel }
            .Where(required => !local.Any(name => ModelMatches(name, required)))
            .ToList();
    }

    /// <summary>"bge-m3" must match a local "bge-m3:latest"; tagged names must match exactly.</summary>
    private static bool ModelMatches(string localName, string required) =>
        string.Equals(localName, required, StringComparison.OrdinalIgnoreCase) ||
        (!required.Contains(':') && localName.StartsWith(required + ":", StringComparison.OrdinalIgnoreCase));

    private static TimeSpan Grow(TimeSpan delay) =>
        delay >= MaxDelay ? MaxDelay : TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxDelay.TotalSeconds));

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Information, Message = "OllamaBootstrap: assistant is Ready (chat={Chat}, embeddings={Embed}).")]
    private static partial void LogAssistantReady(ILogger logger, string chat, string embed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OllamaBootstrap: Ollama unreachable at {BaseUrl} ({Reason}) — retrying.")]
    private static partial void LogOllamaUnreachable(ILogger logger, string baseUrl, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "OllamaBootstrap: pulling {Model}…")]
    private static partial void LogPullingModel(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "OllamaBootstrap: {Model} — {Percent:0}% ({Status})")]
    private static partial void LogPullProgress(ILogger logger, string model, double percent, string? status);

    [LoggerMessage(Level = LogLevel.Information, Message = "OllamaBootstrap: pulled {Model}.")]
    private static partial void LogPulledModel(ILogger logger, string model);
}
