using System.Text.RegularExpressions;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Layout rules of the admin pages that no component test can see (a bUnit render has no stylesheet).
/// The checks read the real CSS files and pin the lines that keep a page inside the screen.
/// </summary>
public class AdminLayoutCssTests
{
    private static readonly string PagesFolder = Path.Combine(RepoPaths.RepoRoot, "src", "FitEdu.Web", "Pages");

    [Fact]
    public void AnalyticsPanels_TwoColumnLayout_LetsAWideTableShrinkInsteadOfStretchingTheColumn()
    {
        // A bare 3fr/2fr track grows to its content's minimum width, so a long course title pushes the
        // second panel off the screen; minmax(0, …) lets the table scroll inside its own panel.
        var css = StripComments(File.ReadAllText(Path.Combine(PagesFolder, "AdminAnalytics.razor.css")));

        css.ShouldContain("grid-template-columns: minmax(0, 3fr) minmax(0, 2fr);");
    }

    [Fact]
    public void AdminUsersStylesheet_HoldsNoEmptyBodyRuleAndNoTableRulesThatBelongToSharedComponents()
    {
        // The page's own markup holds no .table, and the themed table colours already live in
        // shared-components.css; scoped copies here matched nothing.
        var css = StripComments(File.ReadAllText(Path.Combine(PagesFolder, "AdminUsers.razor.css")));

        Regex.IsMatch(css, @"(?m)^\s*body\s*\{").ShouldBeFalse("the empty body rule is dead");
        Regex.IsMatch(css, @"(?m)^\s*\.table(-striped)?\b(?!-actions)").ShouldBeFalse("the .table rules belong to shared-components.css");
    }

    // ---- Tables that stack below 1200 px (FE03-ADMINUSERS-768) ------------------------------------

    private const string PhoneBreakpoint = "(max-width: 767.98px)";
    private const string BelowExtraLargeBreakpoint = "(max-width: 1199.98px)";
    private const string StackModifier = ".table-responsive--stack-xl";

    private static readonly string SharedComponentsCssPath = Path.Combine(RepoPaths.WebRoot, "css", "shared-components.css");
    private static readonly string ScrollableTableCssPath =
        Path.Combine(RepoPaths.RepoRoot, "src", "FitEdu.Web", "Shared", "Components", "Data", "ScrollableTable.razor.css");

    [Fact]
    public void StackedTable_BelowExtraLarge_GetsTheSameCardRulesAsPhones()
    {
        var css = StripComments(File.ReadAllText(SharedComponentsCssPath));
        var phoneCardRules = MediaBodies(css, PhoneBreakpoint).Single(body => body.Contains(".table-responsive > table {")).Trim();
        var stackedRules = MediaBodies(css, BelowExtraLargeBreakpoint).ShouldHaveSingleItem();

        // The modifier class stands in for the plain one; nothing else may differ, so the two copies cannot drift apart.
        stackedRules.Replace(StackModifier, ".table-responsive").ShouldContain(phoneCardRules);
    }

    [Fact]
    public void StackedTable_Rules_NeverReachATableWithoutTheModifier()
    {
        var css = StripComments(File.ReadAllText(SharedComponentsCssPath));
        var stackedRules = MediaBodies(css, BelowExtraLargeBreakpoint).ShouldHaveSingleItem();

        var selectorLines = stackedRules.Split('\n').Where(line => line.Contains(".table-responsive", StringComparison.Ordinal)).ToList();
        selectorLines.ShouldNotBeEmpty();
        selectorLines.ShouldAllBe(line => line.Contains(StackModifier, StringComparison.Ordinal),
            "a rule below 1200 px that names .table-responsive without the modifier would change every table");
    }

    [Fact]
    public void ScrollableTable_HeightLimit_StartsAt768ForPlainTablesAndAt1200ForStackedOnes()
    {
        var css = StripComments(File.ReadAllText(ScrollableTableCssPath));

        MediaBodies(css, "(min-width: 768px)").ShouldHaveSingleItem()
            .ShouldContain($".scrollable-table:not({StackModifier})");
        var fromExtraLarge = MediaBodies(css, "(min-width: 1200px)").ShouldHaveSingleItem();
        fromExtraLarge.ShouldContain($".scrollable-table{StackModifier}");
        fromExtraLarge.ShouldContain("max-height: var(--table-max-height, 60vh);");
        css.ShouldNotContain("\n.scrollable-table {", Case.Sensitive, "a limit outside a media block would reach every width");
    }

    [Fact]
    public void AdminUsersStylesheet_ActionsColumnMinimumWidth_AppliesOnlyWhereTheTableIsNotStacked()
    {
        var css = StripComments(File.ReadAllText(Path.Combine(PagesFolder, "AdminUsers.razor.css")));

        MediaBodies(css, "(min-width: 1200px)").ShouldHaveSingleItem().ShouldContain("min-width: 15rem;");
        Regex.Replace(css, @"@media[^{]*\{(?:[^{}]|\{[^{}]*\})*\}", string.Empty).ShouldNotContain("15rem", Case.Sensitive,
            "the 15rem minimum outside a media block would widen the stacked cards");
    }

    [Fact]
    public void AdminUsersStylesheet_ActionButtonsInTheStackedLayout_AreTouchSized()
    {
        var css = StripComments(File.ReadAllText(Path.Combine(PagesFolder, "AdminUsers.razor.css")));

        MediaBodies(css, BelowExtraLargeBreakpoint).ShouldHaveSingleItem().ShouldContain("min-height: 2.75rem;");
    }

    /// <summary>The bodies of every top-level <c>@media</c> block with exactly this condition.</summary>
    private static List<string> MediaBodies(string css, string condition)
    {
        var bodies = new List<string>();
        var opening = "@media " + condition + " {";
        var searchFrom = 0;
        while (css.IndexOf(opening, searchFrom, StringComparison.Ordinal) is var start and >= 0)
        {
            var bodyStart = start + opening.Length;
            var depth = 1;
            var position = bodyStart;
            while (depth > 0 && position < css.Length)
            {
                depth += css[position] switch { '{' => 1, '}' => -1, _ => 0 };
                position++;
            }

            bodies.Add(css[bodyStart..(position - 1)]);
            searchFrom = position;
        }

        return bodies;
    }

    /// <summary>A stylesheet without its comments, so a rule named only in a comment does not count.</summary>
    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
}
