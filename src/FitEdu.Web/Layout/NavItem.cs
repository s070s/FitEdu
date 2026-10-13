using Microsoft.AspNetCore.Components.Routing;

namespace FitEdu.Web.Layout;

/// <summary>
/// One navigation destination. Every item is listed in the header menu panel; the flags pick the
/// few that also show inline in the wide header and as a tab in the mobile bottom bar.
/// </summary>
/// <param name="Href">Relative route; an empty string is the home page.</param>
/// <param name="Icon">Bootstrap Icons class from <see cref="AppIcons"/>.</param>
/// <param name="Label">Reads the label from the resources at render time, so it follows the culture.</param>
/// <param name="InHeader">Shown inline in the header on wide screens (1200 px and up).</param>
/// <param name="InBottomNav">Shown as a tab in the bottom bar on phones (below 768 px).</param>
/// <param name="ShortLabel">Optional shorter label for the tight places (inline header, tabs).</param>
/// <param name="Highlight">Drawn as the lime call-to-action pill in the header (e.g. Register).</param>
public sealed record NavItem(
    string Href,
    string Icon,
    Func<string> Label,
    bool InHeader = false,
    bool InBottomNav = false,
    Func<string>? ShortLabel = null,
    bool Highlight = false)
{
    /// <summary>Home matches only itself; every other link stays active on its sub-pages.</summary>
    public NavLinkMatch Match => Href.Length == 0 ? NavLinkMatch.All : NavLinkMatch.Prefix;

    /// <summary>The short label when there is one, otherwise the full label.</summary>
    public string CompactLabel => ShortLabel is null ? Label() : ShortLabel();
}
