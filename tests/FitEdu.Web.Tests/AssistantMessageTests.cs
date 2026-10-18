using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Web.Shared.Components.Assistant;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The coach is told not to use markdown, but the small local model still adds bold marks and
/// headings now and then; the chat bubble shows plain text, so members saw the raw ** and #.
/// </summary>
public class AssistantMessageTests : BunitContext
{
    public AssistantMessageTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    }

    private string RenderedText(string role, string content) =>
        Render<AssistantMessage>(parameters => parameters
                .Add(message => message.Role, role)
                .Add(message => message.Content, content))
            .Find(".assistant-message").TextContent.Trim();

    [Fact]
    public void CoachAnswer_BoldAndHeadingMarks_AreRemoved() =>
        RenderedText("assistant", "## Chest\nTry the **push-up** and the __bench press__.")
            .ShouldBe("Chest\nTry the push-up and the bench press.");

    [Fact]
    public void CoachAnswer_LinksAndDividers_ShowAsPlainText() =>
        // Live check 2026-10-18: answers showed [Squat](/exercises/bodyweight-squat) and --- lines.
        RenderedText("assistant", "1. [Squat](/exercises/bodyweight-squat)\n---\n2. Lunge")
            .ShouldBe("1. Squat\n\n2. Lunge");

    [Fact]
    public void CoachAnswer_ListsAndSingleAsterisks_AreKept() =>
        RenderedText("assistant", "- 3 sets\n- RPE 7\nA*B")
            .ShouldBe("- 3 sets\n- RPE 7\nA*B");

    [Fact]
    public void MemberMessage_IsShownExactlyAsTyped() =>
        RenderedText("user", "Is **this** ok?")
            .ShouldBe("Is **this** ok?");
}
