using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Globalization;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;

namespace FitEdu.Web.Layout;

public partial class SiteSearchBox : IDisposable
{
    private const int FlyoutLimit = 5;

    // Long enough for a tap on a result link to land before the flyout closes.
    private const int BlurCloseDelayMilliseconds = 200;

    [Inject] private ISearchConsumer Consumer { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private string _query = string.Empty;
    private bool _open;
    private bool _loading;
    private SiteSearchResultDto? _result;
    private CancellationTokenSource? _debounceCts;

    // Phones: the field is hidden behind an icon button until opened.
    private bool _expanded;
    private bool _focusInputAfterRender;
    private ElementReference _input;

    // Bumped on every focus inside the box, so a blur followed by focus elsewhere inside it
    // (toggle button to input, input to a result link) does not close anything.
    private int _focusVersion;

    private static string Lang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    private void HandleFocus() => _open = true;

    private void HandleFocusIn() => _focusVersion++;

    private async Task HandleFocusOut()
    {
        var versionAtBlur = _focusVersion;
        await Task.Delay(BlurCloseDelayMilliseconds);
        if (_focusVersion != versionAtBlur)
        {
            return; // focus moved to another element inside the box
        }

        Close();
        StateHasChanged();
    }

    private void Close()
    {
        _open = false;
        _expanded = false;
    }

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        _focusInputAfterRender = _expanded;
        if (!_expanded)
        {
            _open = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusInputAfterRender)
        {
            return;
        }

        _focusInputAfterRender = false;
        await _input.FocusAsync();
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && _query.Trim().Length > 0)
        {
            Close();
            Navigation.NavigateTo($"/search?q={Uri.EscapeDataString(_query.Trim())}");
        }
        else if (e.Key == "Escape")
        {
            Close();
        }
    }

    private async Task HandleInput(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? string.Empty;
        _open = true;

        var previous = _debounceCts;
        _debounceCts = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();

        if (_query.Trim().Length == 0)
        {
            _result = null;
            return;
        }

        try
        {
            await Task.Delay(300, _debounceCts.Token);
            _loading = true;
            StateHasChanged();
            _result = await Consumer.SearchAsync(_query.Trim(), FlyoutLimit, Lang);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer keystroke — leave the old cts's state alone
        }
        finally
        {
            _loading = false;
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
