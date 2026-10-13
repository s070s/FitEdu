using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace FitEdu.Web.Layout;

public partial class BottomNav
{
    [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private IReadOnlyList<NavItem> _tabs = [];

    protected override async Task OnParametersSetAsync()
    {
        var items = await NavItems.ForAuthenticationStateAsync(AuthenticationStateTask);
        _tabs = items.Where(item => item.InBottomNav).ToList();
    }
}
