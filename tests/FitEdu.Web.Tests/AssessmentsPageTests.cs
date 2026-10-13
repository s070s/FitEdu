using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>bUnit tests for the self-check catalogue: cards, completed marks and the upgrade prompt.</summary>
public class AssessmentsPageTests : BunitContext
{
    private readonly IAssessmentConsumer _assessmentConsumer = Substitute.For<IAssessmentConsumer>();
    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();
    private readonly ICategoryConsumer _categoryConsumer = Substitute.For<ICategoryConsumer>();

    public AssessmentsPageTests()
    {
        Services.AddSingleton(_assessmentConsumer);
        Services.AddSingleton(_subscriptionConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton(_categoryConsumer);
        _categoryConsumer.GetCategoriesAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new List<CategoryDto>());
    }

    private void GivenAccess(bool hasAccess) =>
        _subscriptionConsumer.GetStatusAsync().Returns(new UserSubscriptionStatusDto(
            SubscriptionTier.Plus, "Plus", DateTime.UtcNow, null, true,
            new PlanFeaturesDto { AssessmentAccess = hasAccess }));

    private static AssessmentDefinitionDto Definition(string title) => new(
        Guid.NewGuid(), title.ToLowerInvariant(), title, "A short check.",
        """{"questions":[{"id":"q1"},{"id":"q2"},{"id":"q3"}]}""", true,
        DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, "Readiness");

    [Fact]
    public void WithAccess_RendersCardsAndMarksCompletedOnes()
    {
        GivenAccess(true);
        var done = Definition("Sleep");
        var open = Definition("Mobility");
        _assessmentConsumer.GetAssessmentsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>())
            .Returns(new PagedResult<AssessmentDefinitionDto>([done, open], 2, 1, 10));
        _assessmentConsumer.GetMySubmissionsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AssessmentSubmissionDto>(
                [new(Guid.NewGuid(), done.Id, done.Title, "{}", null, DateTimeOffset.UtcNow)], 1, 1, 100));

        var cut = Render<Assessments>();

        cut.FindAll(".assessment-card").Count.ShouldBe(2);
        cut.FindAll(".assessment-card--completed").Count.ShouldBe(1);
        cut.Find(".assessment-card a.stretched-link").GetAttribute("href").ShouldBe($"/assessments/{done.Id}");
        cut.Find(".assessments-progress .progress-ring__text").TextContent.ShouldBe("1/2");
    }

    [Fact]
    public void WithoutAccess_ShowsTheUpgradePrompt()
    {
        GivenAccess(false);

        var cut = Render<Assessments>();

        cut.Find(".upgrade-prompt a").GetAttribute("href").ShouldBe("/pricing");
        cut.FindAll(".assessment-card").ShouldBeEmpty();
    }
}
