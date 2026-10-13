using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace FitEdu.Web.Layout;

public partial class NavMenu : IDisposable
{
    private const string EscapeKey = "Escape";

    [Parameter] public RenderFragment? TrailingContent { get; set; }

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private IReadOnlyList<NavItem> _items = [];
    private List<NavItem> _headerItems = [];
    private bool _menuOpen;
    private ElementReference _menuToggle;

    protected override void OnInitialized()
    {
        // The panel closes on any navigation, including the browser back button.
        Navigation.LocationChanged += HandleLocationChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        _items = await NavItems.ForAuthenticationStateAsync(AuthenticationStateTask);
        _headerItems = _items.Where(item => item.InHeader).ToList();
    }

    /// <summary>The menu button hides on wide screens when every link already sits in the header.</summary>
    private string MenuToggleClass
    {
        get
        {
            var everyItemIsInline = _headerItems.Count == _items.Count;
            return everyItemIsInline ? "site-menu-toggle site-menu-toggle--compact-only" : "site-menu-toggle";
        }
    }

    private static string HeaderLinkClass(NavItem item) =>
        item.Highlight ? "site-link site-link--cta" : "site-link";

    private void ToggleMenu() => _menuOpen = !_menuOpen;

    private void CloseMenu() => _menuOpen = false;

    /// <summary>Escape closes the panel from the menu button or from inside the panel.</summary>
    private async Task HandleMenuKeyDown(KeyboardEventArgs keyboardEvent)
    {
        if (keyboardEvent.Key != EscapeKey || !_menuOpen)
        {
            return;
        }

        CloseMenu();
        // Without this, focus falls back to the page body when the panel disappears.
        await _menuToggle.FocusAsync();
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs eventArgs)
    {
        if (!_menuOpen)
        {
            return;
        }

        _menuOpen = false;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Navigation.LocationChanged -= HandleLocationChanged;
        GC.SuppressFinalize(this);
    }
}
