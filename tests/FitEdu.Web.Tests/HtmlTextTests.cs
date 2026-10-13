using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>Plain text from stored rich text, for word counts and page descriptions.</summary>
public class HtmlTextTests
{
    [Theory]
    [InlineData("<p>Build <strong>strength</strong> &amp; confidence.</p>", "Build strength & confidence.")]
    [InlineData("<p>One</p>\n\n<p>Two</p>", "One Two")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void ToPlainText_DropsTagsDecodesEntitiesAndJoinsSpaces(string? html, string expected)
    {
        HtmlText.ToPlainText(html).ShouldBe(expected);
    }

    [Fact]
    public void Summary_ShortText_StaysWhole()
    {
        HtmlText.Summary("<p>Short and clear.</p>", 160).ShouldBe("Short and clear.");
    }

    [Fact]
    public void Summary_LongText_IsCutAtAWordBreakWithAnEllipsis()
    {
        HtmlText.Summary("<p>Squat, hinge, push and pull every week.</p>", 20).ShouldBe("Squat, hinge, push…");
    }
}
