using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;

#pragma warning disable CA1716 // The Shared namespace is the app's component root; renaming it would break every Razor import.
namespace FitEdu.Web.Shared.Components.Assistant;

public partial class AssistantWidget : IDisposable
{
    // The current question plus the two exchanges before it. Live check 2026-10-18: with a long
    // history, earlier exercise talk pulled a programme question into an exercise answer.
    private const int MaxHistoryMessages = 5;
    private static readonly TimeSpan RenderThrottle = TimeSpan.FromMilliseconds(100);

    [Inject] private IAssistantConsumer Consumer { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private bool _open;
    private AssistantStatusDto? _status;
    private CancellationTokenSource? _statusPollCts;
    private bool _accessChecked;
    private readonly List<AssistantMessageDto> _messages = [];
    private bool _isStreaming;
    private List<AssistantSourceDto> _sources = [];
    private ElementReference _inputRef;
    private CancellationTokenSource? _streamCts;
    private DateTime _lastRender = DateTime.MinValue;

    private static string Lang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    // Status check happens once after the circuit is fully interactive (not during prerender,
    // when there is no live connection to stream a response over anyway), and only for signed-in
    // users — the launcher is invisible to anonymous visitors (see AuthorizeView in the markup),
    // so pinging the API for them would just be a wasted 401.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _accessChecked)
            return;

        _accessChecked = true;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated != true)
            return;

        _status = await Consumer.GetStatusAsync();
        StateHasChanged();

        // While the assistant bootstraps (starting, Ollama probing, model download), keep polling so the
        // widget flips to interactive the moment the state hits Ready — no refresh needed.
        _statusPollCts = new CancellationTokenSource();
        _ = PollStatusWhileBootstrappingAsync(_statusPollCts.Token);
    }

    private async Task PollStatusWhileBootstrappingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested
                   && _status is { State: "Starting" or "DownloadingModels" or "OllamaUnreachable" })
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                _status = await Consumer.GetStatusAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // Circuit going away — nothing to update.
        }
    }

    private void ToggleOpen() => _open = !_open;

    private void StartNewChat()
    {
        _messages.Clear();
        _sources = [];
    }

    private Task AskSuggested(string question) => SendAsync(question);

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey && !_isStreaming)
            await Send();
    }

    private async Task Send()
    {
        // Read the input value from the DOM only on send (PERF-6) — no per-keystroke binding.
        var text = await JS.InvokeAsync<string>("inputInterop.read", _inputRef);
        if (string.IsNullOrWhiteSpace(text) || _isStreaming)
            return;
        await JS.InvokeVoidAsync("inputInterop.clear", _inputRef);
        await SendAsync(text.Trim());
    }

    private async Task SendAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _isStreaming)
            return;

        _messages.Add(new AssistantMessageDto("user", text));
        _messages.Add(new AssistantMessageDto("assistant", ""));
        _sources = [];
        _isStreaming = true;
        StateHasChanged();

        var history = _messages.Take(_messages.Count - 1).ToList();
        if (history.Count > MaxHistoryMessages)
            history = history.Skip(history.Count - MaxHistoryMessages).ToList();

        _streamCts = new CancellationTokenSource();
        var content = new StringBuilder();
        var errored = false;
        string? errorText = null;

        try
        {
            await foreach (var streamEvent in Consumer.StreamChatAsync(new AssistantChatRequest(history), Lang, _streamCts.Token))
            {
                switch (streamEvent.Kind)
                {
                    case "token":
                        content.Append(streamEvent.Text);
                        UpdateLastMessage(content.ToString());
                        ThrottledRender();
                        break;
                    case "truncated":
                        // The answer hit the length limit: end it at its last full sentence.
                        var shortened = CutAtLastSentence(content.ToString());
                        content.Clear().Append(shortened);
                        UpdateLastMessage(shortened);
                        break;
                    case "sources":
                        _sources = streamEvent.Sources ?? [];
                        break;
                    case "error":
                        errored = true;
                        errorText = streamEvent.Text;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Widget closed or navigated away mid-stream — nothing left to show.
        }

        if (errored || content.Length == 0)
            UpdateLastMessage(string.IsNullOrWhiteSpace(errorText) ? AssistantRes.ErrorGeneric : errorText);

        _isStreaming = false;
        StateHasChanged();
    }

    /// <summary>Cuts a cut-off answer back to its last full sentence or line; adds "…" when there is none.</summary>
    private static string CutAtLastSentence(string text)
    {
        var end = SentenceEnd().Match(text);
        if (!end.Success || end.Index == 0)
            return text.TrimEnd() + "…";

        return text[..end.Index].TrimEnd();
    }

    // The last ". ! ? ; …" right after a letter (so a list number like "3." does not count), or
    // the last line break. ";" is the Greek question mark.
    [GeneratedRegex(@"(?<=\p{L}[.!?;…])(?=\s|$)|\n", RegexOptions.RightToLeft)]
    private static partial Regex SentenceEnd();

    private void UpdateLastMessage(string text)
    {
        if (_messages.Count == 0)
            return;

        _messages[^1] = _messages[^1] with { Content = text };
    }

    private void ThrottledRender()
    {
        var now = DateTime.UtcNow;
        if (now - _lastRender < RenderThrottle)
            return;

        _lastRender = now;
        StateHasChanged();
    }

    public void Dispose()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _statusPollCts?.Cancel();
        _statusPollCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
