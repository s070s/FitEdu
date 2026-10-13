using System.Text.RegularExpressions;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The app is dark only, so Bootstrap's light-theme outline buttons must be recoloured with the
/// app's tokens; Bootstrap's own grey and red text is too faint on the dark cards (WCAG AA).
/// </summary>
public class OutlineButtonThemeTests
{
    private static readonly string SharedComponentsCssPath =
        Path.Combine(RepoPaths.WebRoot, "css", "shared-components.css");

    [Theory]
    [InlineData("btn-outline-secondary", "var(--text-light-normal)")]
    [InlineData("btn-outline-danger", "var(--color-error)")]
    public void OutlineButton_UsesTheAppsReadableTextColour(string buttonClass, string expectedColour)
    {
        var rule = Regex.Match(SharedComponentsCss(), @"(?m)^\." + buttonClass + @"\s*\{([^}]*)\}");

        rule.Success.ShouldBeTrue($".{buttonClass} has no dark-theme rule");
        rule.Groups[1].Value.ShouldContain("color: " + expectedColour);
    }

    /// <summary>shared-components.css without its comments.</summary>
    private static string SharedComponentsCss() =>
        Regex.Replace(File.ReadAllText(SharedComponentsCssPath), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
}
