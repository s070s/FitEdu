using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Shared.Components.Assistant;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The coach panel shows the member's question and the streamed answer in their bubbles.
/// Live check 2026-10-18: the widget passed Content="message.Content" without @, so every
/// bubble showed that literal text and every bubble was styled as the coach's.
/// </summary>
public class AssistantWidgetTests : BunitContext
{
    private readonly IAssistantConsumer _consumer = Substitute.For<IAssistantConsumer>();

    public AssistantWidgetTests()
    {
        Services.AddSingleton(_consumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization().SetAuthorized("alice.johnson@fitedu.local");

        _consumer.GetStatusAsync().Returns(new AssistantStatusDto(true, "qwen3:1.7b", "Ready"));
        _consumer.StreamChatAsync(Arg.Any<AssistantChatRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Answer("Try the ", "push-up."));
    }

    private static async IAsyncEnumerable<AssistantStreamEvent> Answer(params string[] tokens)
    {
        await Task.Yield();
        foreach (var token in tokens)
            yield return new AssistantStreamEvent("token", token);
        yield return new AssistantStreamEvent("done");
    }

    [Fact]
    public void CoachStillStarting_ShowsWarmingUp_ThenTheChatOnceReady()
    {
        // Live check 2026-10-18: while the app loaded the model, the status read "Disabled", which
        // the widget treats as final, so it said "unavailable" until the page was reloaded.
        _consumer.GetStatusAsync().Returns(
            new AssistantStatusDto(false, null, "Starting"),
            new AssistantStatusDto(true, "qwen3:1.7b", "Ready"));
        var widget = Render<AssistantWidget>();

        widget.Find(".assistant-launcher").Click();

        widget.WaitForAssertion(() => widget.Find(".assistant-offline").TextContent.Trim().ShouldBe(AssistantRes.WarmingUp));
        widget.WaitForElement(".assistant-suggestions button", TimeSpan.FromSeconds(10)); // the widget re-checks every 5 s
    }

    [Fact]
    public void AskingASuggestedQuestion_ShowsTheQuestionAndTheAnswer()
    {
        var widget = Render<AssistantWidget>();

        widget.Find(".assistant-launcher").Click();
        widget.WaitForElement(".assistant-suggestions button").Click();

        widget.WaitForAssertion(() =>
        {
            widget.Find(".assistant-message-user").TextContent.Trim().ShouldBe(AssistantRes.SuggestedQuestion1);
            widget.Find(".assistant-message-bot").TextContent.Trim().ShouldBe("Try the push-up.");
        });
    }
}
