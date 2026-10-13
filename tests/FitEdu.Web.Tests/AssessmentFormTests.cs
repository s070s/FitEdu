using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text.Json;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the student self-check form: both multi-select spellings, the section steps,
/// the rating scale with its end labels and the required-answer checks.
/// </summary>
public class AssessmentFormTests : BunitContext
{
    private readonly IAssessmentConsumer _assessmentConsumer = Substitute.For<IAssessmentConsumer>();
    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();
    private readonly Guid _assessmentId = Guid.NewGuid();

    /// <summary>The shape the API sends: a section outline plus the flat question list.</summary>
    private const string SectionedSchema = """
        {"sections":[
            {"id":"s1","title":"Energy","questionIds":["q1"]},
            {"id":"s2","title":"Habits","questionIds":["q2"]}],
         "questions":[
            {"id":"q1","type":"rating","label":"How rested?","min":1,"max":5,"minLabel":"Tired","maxLabel":"Fresh","required":true},
            {"id":"q2","type":"text","label":"Anything else?","required":false}]}
        """;

    public AssessmentFormTests()
    {
        Services.AddSingleton(_assessmentConsumer);
        Services.AddSingleton(_subscriptionConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        _subscriptionConsumer.GetStatusAsync().Returns(new UserSubscriptionStatusDto(
            SubscriptionTier.Plus, "Plus", DateTime.UtcNow, null, true,
            new PlanFeaturesDto { AssessmentAccess = true }));
    }

    private IRenderedComponent<AssessmentForm> RenderWithSchema(string schemaJson)
    {
        var definition = new AssessmentDefinitionDto(
            _assessmentId, "check_v1", "Readiness Check", "A short check.", schemaJson, true,
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, "Readiness");
        _assessmentConsumer.GetAssessmentAsync(_assessmentId, Arg.Any<string>()).Returns(definition);

        return Render<AssessmentForm>(parameters => parameters.Add(page => page.AssessmentId, _assessmentId));
    }

    [Theory]
    [InlineData("multiselect")]
    [InlineData("multi-select")]
    public void Multiselect_BothSpellings_RenderCheckboxes(string type)
    {
        var schema = $$"""{"questions":[{"id":"q1","type":"{{type}}","label":"x","options":["a","b"],"required":true}]}""";

        var cut = RenderWithSchema(schema);

        cut.FindAll("input[type=checkbox]").Count.ShouldBe(2);
    }

    [Fact]
    public void Multiselect_OptionWithComma_IsAddedAndRemovedWhole()
    {
        var schema = """{"questions":[{"id":"q1","type":"multiselect","label":"x","options":["Walks, hikes","Yoga"],"required":true}]}""";
        _assessmentConsumer.SubmitAssessmentAsync(_assessmentId, Arg.Any<SubmitAssessmentRequest>())
            .Returns(new AssessmentSubmissionDto(Guid.NewGuid(), _assessmentId, "Readiness Check", "{}", null, DateTimeOffset.UtcNow));
        var cut = RenderWithSchema(schema);

        cut.FindAll("input[type=checkbox]")[0].Change(true);
        cut.FindAll("input[type=checkbox]")[1].Change(true);
        cut.FindAll("input[type=checkbox]")[0].Change(false);
        cut.Find("form").Submit();

        // The answer is a JSON array, so the stored answer keeps each option whole.
        SubmittedAnswer("q1").ShouldBe("""["Yoga"]""");
    }

    [Fact]
    public void Multiselect_OptionsWithCommas_AreStoredAsAJsonArray()
    {
        var schema = """{"questions":[{"id":"q1","type":"multiselect","label":"x","options":["Chest pain, pressure or discomfort","Dizziness"],"required":true}]}""";
        _assessmentConsumer.SubmitAssessmentAsync(_assessmentId, Arg.Any<SubmitAssessmentRequest>())
            .Returns(new AssessmentSubmissionDto(Guid.NewGuid(), _assessmentId, "Readiness Check", "{}", null, DateTimeOffset.UtcNow));
        var cut = RenderWithSchema(schema);

        cut.FindAll("input[type=checkbox]")[0].Change(true);
        cut.FindAll("input[type=checkbox]")[1].Change(true);
        cut.Find("form").Submit();

        MultiSelectAnswer.TryParse(SubmittedAnswer("q1")).ShouldBe(["Chest pain, pressure or discomfort", "Dizziness"]);
    }

    [Fact]
    public void Multiselect_AllOptionsUnticked_LeavesTheRequiredQuestionUnanswered()
    {
        var schema = """{"questions":[{"id":"q1","type":"multiselect","label":"x","options":["a","b"],"required":true}]}""";
        var cut = RenderWithSchema(schema);

        cut.FindAll("input[type=checkbox]")[0].Change(true);
        cut.FindAll("input[type=checkbox]")[0].Change(false);
        cut.Find("form").Submit();

        _assessmentConsumer.DidNotReceive().SubmitAssessmentAsync(Arg.Any<Guid>(), Arg.Any<SubmitAssessmentRequest>());
    }

    /// <summary>The answer the form sent for one question.</summary>
    private string SubmittedAnswer(string questionId)
    {
        var request = (SubmitAssessmentRequest)_assessmentConsumer.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IAssessmentConsumer.SubmitAssessmentAsync))
            .GetArguments()[1]!;
        var answers = JsonSerializer.Deserialize<Dictionary<string, string>>(request.AnswersJson)!;
        return answers[questionId];
    }

    [Fact]
    public void SectionedSchema_RendersSectionTitlesAndRatingEndLabels()
    {
        var cut = RenderWithSchema(SectionedSchema);

        cut.FindAll(".assessment-section__title").Select(title => title.TextContent).ShouldBe(["Energy", "Habits"]);
        cut.FindAll(".rating-scale__option").Count.ShouldBe(5);
        var ends = cut.Find(".rating-scale__ends").TextContent;
        ends.ShouldContain("Tired");
        ends.ShouldContain("Fresh");
    }

    [Fact]
    public void RatingScale_OneToTen_RendersEveryValueInTheWrappingGroup()
    {
        var schema = """{"questions":[{"id":"q1","type":"rating","label":"x","min":1,"max":10}]}""";

        var cut = RenderWithSchema(schema);

        cut.FindAll(".rating-scale > .rating-scale__option").Count.ShouldBe(10);
    }

    [Fact]
    public void Stepper_Next_AdvancesWhenTheSectionIsAnswered()
    {
        var cut = RenderWithSchema(SectionedSchema);
        cut.FindAll(".assessment-section")[0].ClassList.ShouldContain("is-active");

        cut.Find("#q-q1-4").Change("on");
        cut.Find(".assessment-stepper__next").Click();

        var sections = cut.FindAll(".assessment-section");
        sections[0].ClassList.ShouldNotContain("is-active");
        sections[1].ClassList.ShouldContain("is-active");
        cut.FindAll(".assessment-stepper__next").ShouldBeEmpty();
        cut.Find(".assessment-stepper__submit").ClassList.ShouldNotContain("assessment-stepper__submit--early");
    }

    [Fact]
    public void Stepper_Next_StaysWhenARequiredAnswerIsMissing()
    {
        var cut = RenderWithSchema(SectionedSchema);

        cut.Find(".assessment-stepper__next").Click();

        cut.FindAll(".assessment-section")[0].ClassList.ShouldContain("is-active");
        cut.FindAll(".question--invalid").Count.ShouldBe(1);
        cut.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Submit_MissingRequired_ShowsErrorAndDoesNotSubmit()
    {
        var cut = RenderWithSchema(SectionedSchema);

        cut.Find("form").Submit();

        cut.Find("[role=alert]").TextContent.ShouldNotBeNullOrWhiteSpace();
        _assessmentConsumer.DidNotReceive().SubmitAssessmentAsync(Arg.Any<Guid>(), Arg.Any<SubmitAssessmentRequest>());
    }
}
