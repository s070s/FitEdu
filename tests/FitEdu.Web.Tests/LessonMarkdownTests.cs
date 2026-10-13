using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The lesson text converter: the Markdown subset the seed lessons use, editor HTML passed through,
/// and the title heading the page already shows.
/// </summary>
public class LessonMarkdownTests
{
    [Fact]
    public void NumberedList_ClosesWithOl()
    {
        var html = LessonMarkdown.Render("1. Brace\n2. Lift");

        html.ShouldBe("<ol><li>Brace</li><li>Lift</li></ol>");
    }

    [Fact]
    public void BulletList_ClosesWithUl()
    {
        var html = LessonMarkdown.Render("- one\n- two");

        html.ShouldBe("<ul><li>one</li><li>two</li></ul>");
    }

    [Fact]
    public void NumberedListFollowedByBullets_ClosesEachListWithItsOwnTag()
    {
        var html = LessonMarkdown.Render("1. Warm up\n- Easier: smaller range");

        html.ShouldBe("<ol><li>Warm up</li></ol><ul><li>Easier: smaller range</li></ul>");
    }

    [Fact]
    public void NumberedList_AcceptsTwoDigitNumbers()
    {
        var html = LessonMarkdown.Render("9. Ninth\n10. Tenth");

        html.ShouldBe("<ol><li>Ninth</li><li>Tenth</li></ol>");
    }

    [Fact]
    public void DecimalNumberAtLineStart_StaysAParagraph()
    {
        var html = LessonMarkdown.Render("1.5 kg is the smallest jump.");

        html.ShouldBe("<p>1.5 kg is the smallest jump.</p>");
    }

    [Fact]
    public void HeadingsParagraphsAndBold_RenderAsHtml()
    {
        var html = LessonMarkdown.Render("## Set-up\n\nKeep your **ribs down**.\n### Cues");

        html.ShouldBe("<h2>Set-up</h2><p>Keep your <strong>ribs down</strong>.</p><h3>Cues</h3>");
    }

    [Fact]
    public void BlankLine_EndsTheListWithoutAddingLineBreaks()
    {
        var html = LessonMarkdown.Render("- one\n\nAfter the list");

        html.ShouldBe("<ul><li>one</li></ul><p>After the list</p>");
    }

    [Fact]
    public void WindowsLineEndings_AreHandled()
    {
        var html = LessonMarkdown.Render("1. a\r\n2. b\r\n");

        html.ShouldBe("<ol><li>a</li><li>b</li></ol>");
    }

    [Fact]
    public void LeadingHeadingEqualToPageTitle_IsLeftOut()
    {
        var html = LessonMarkdown.Render("# The Push-Up, Done Right\n\nBody text", "The Push-Up, Done Right");

        html.ShouldBe("<p>Body text</p>");
    }

    [Fact]
    public void LeadingHeadingWithAnotherTitle_IsKept()
    {
        var html = LessonMarkdown.Render("# Warm-up\nBody text", "Workout A");

        html.ShouldBe("<h1>Warm-up</h1><p>Body text</p>");
    }

    [Fact]
    public void TitleHeadingLaterInTheText_IsKept()
    {
        var html = LessonMarkdown.Render("Intro\n# Workout A", "Workout A");

        html.ShouldBe("<p>Intro</p><h1>Workout A</h1>");
    }

    [Fact]
    public void EditorHtml_PassesThroughUnchanged()
    {
        const string editorHtml = "<p><strong>bold</strong></p>\n1. not a list";

        LessonMarkdown.Render(editorHtml, "Any title").ShouldBe(editorHtml);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void EmptyContent_RendersNothing(string? content)
    {
        LessonMarkdown.Render(content).ShouldBeEmpty();
    }
}
