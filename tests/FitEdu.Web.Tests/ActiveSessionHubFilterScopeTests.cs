using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FitEdu.Web.Hubs;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The account re-check belongs on the chat and call hubs only. Added globally it also ran on Blazor's
/// own circuit hub, where a guest has no account and the sign-in cookie has no bearer stamp claim, so
/// every circuit was refused and no interactive page (the login page included) ever started.
/// </summary>
public class ActiveSessionHubFilterScopeTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ActiveSessionHubFilterScopeTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void ChatAndCallHubs_RunTheFilter()
    {
        FilterTypes<HubOptions<ChatHub>>().ShouldContain(typeof(ActiveSessionHubFilter));
        FilterTypes<HubOptions<CallHub>>().ShouldContain(typeof(ActiveSessionHubFilter));
    }

    [Fact]
    public void GlobalHubOptions_UsedByTheBlazorCircuitHub_DoNotRunTheFilter()
    {
        // Every hub without its own options, Blazor's circuit hub among them, copies the global filters.
        FilterTypes<HubOptions>().ShouldNotContain(typeof(ActiveSessionHubFilter));
    }

    /// <summary>
    /// The filter list and the filter type it wraps are internal to SignalR, so they are read by
    /// reflection; a renamed member fails the test with a clear message instead of passing silently.
    /// </summary>
    private List<Type> FilterTypes<TOptions>() where TOptions : HubOptions
    {
        var options = _factory.Services.GetRequiredService<IOptions<TOptions>>().Value;
        var filtersProperty = typeof(HubOptions).GetProperty("HubFilters", BindingFlags.Instance | BindingFlags.NonPublic);
        filtersProperty.ShouldNotBeNull("SignalR no longer has HubOptions.HubFilters; update this test.");

        var filters = (IEnumerable<IHubFilter>?)filtersProperty.GetValue(options) ?? [];
        var filterTypes = new List<Type>();
        foreach (var filter in filters)
        {
            var wrappedType = filter.GetType().GetField("_filterType", BindingFlags.Instance | BindingFlags.NonPublic);
            filterTypes.Add(wrappedType?.GetValue(filter) as Type ?? filter.GetType());
        }

        return filterTypes;
    }
}
