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

    /// <summary>A stylesheet without its comments, so a rule named only in a comment does not count.</summary>
    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
}
