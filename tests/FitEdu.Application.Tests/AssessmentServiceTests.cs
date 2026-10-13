using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace FitEdu.Application.Tests;

public class AssessmentServiceTests
{
    private const string UserId = "user-1";

    private const string SchemaJson =
        """{"questions":[{"id":"q1","type":"text","required":true},{"id":"q2","type":"text","required":false}]}""";

    private static AssessmentService NewService(ApplicationDbContext db, SubscriptionTier tier = SubscriptionTier.Pro)
    {
        var subs = Substitute.For<ISubscriptionService>();
        subs.GetUserStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserSubscriptionStatusDto(
                tier, tier.ToString(), DateTime.UtcNow, null, true,
                new PlanFeaturesDto { AssessmentAccess = true }));

        return new AssessmentService(db, subs, new MemoryCache(new MemoryCacheOptions()), NullLogger<AssessmentService>.Instance);
    }

    private static AssessmentDefinition Published(string schemaJson = SchemaJson) => new()
    {
        Id = Guid.NewGuid(),
        Key = $"key-{Guid.NewGuid():N}",
        TitleEn = "Assessment",
        SchemaJson = schemaJson,
        IsPublished = true,
        RequiredTier = SubscriptionTier.Free
    };

    // ---- SubmitAssessmentAsync (DQ-2) -----------------------------------------

    [Fact]
    public async Task Submit_ValidAnswers_Succeeds()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published();
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).SubmitAssessmentAsync(
            UserId, assessment.Id, new SubmitAssessmentRequest("""{"q1":"answer","q2":""}""", null));

        result.IsSuccess.ShouldBeTrue();
        (await db.AssessmentSubmissions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Submit_MissingOptionalAnswer_Succeeds()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published();
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).SubmitAssessmentAsync(
            UserId, assessment.Id, new SubmitAssessmentRequest("""{"q1":"answer"}""", null));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Submit_MalformedAnswersJson_ReturnsBadRequestAndDoesNotPersist()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published();
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).SubmitAssessmentAsync(
            UserId, assessment.Id, new SubmitAssessmentRequest("not json", null));

        result.StatusCode.ShouldBe(400);
        (await db.AssessmentSubmissions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Submit_UnknownAnswerKey_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published();
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).SubmitAssessmentAsync(
            UserId, assessment.Id, new SubmitAssessmentRequest("""{"q1":"answer","not-a-question":"x"}""", null));

        result.StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task Submit_MissingRequiredAnswer_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published();
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).SubmitAssessmentAsync(
            UserId, assessment.Id, new SubmitAssessmentRequest("""{"q1":"   ","q2":"answer"}""", null));

        result.StatusCode.ShouldBe(400);
    }

    [Theory]
    [InlineData("""{"q1":"[]"}""", 400)]                                   // a multi-select with nothing ticked
    [InlineData("""{"q1":"[\"Chest pain, pressure or discomfort\"]"}""", 200)] // a JSON-array multi-select answer
    public async Task Submit_RequiredMultiSelectAnswer_CountsAnEmptyListAsMissing(string answersJson, int expectedStatus)
    {
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published();
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).SubmitAssessmentAsync(
            UserId, assessment.Id, new SubmitAssessmentRequest(answersJson, null));

        result.StatusCode.ShouldBe(expectedStatus);
    }

    [Fact]
    public async Task Submit_CorruptStoredSchema_DoesNotBlockSubmission()
    {
        // DQ-4 (schema corruption) shouldn't compound into DQ-2 rejecting every submission
        // for an assessment whose schema is already broken.
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published(schemaJson: "not json");
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).SubmitAssessmentAsync(
            UserId, assessment.Id, new SubmitAssessmentRequest("""{"anything":"goes"}""", null));

        result.IsSuccess.ShouldBeTrue();
    }

    // ---- GetAssessmentAsync: language resolution ------------------------------

    private const string SectionedSchemaJson =
        """{"id":"x","title":"Check","titleEl":"Έλεγχος","version":"1.0","sections":[{"id":"s1","title":"Energy","titleEl":"Ενέργεια","questions":[{"id":"q1","type":"rating","label":"How rested?","labelEl":"Πόσο ξεκούραστος;","min":1,"max":5,"minLabel":"Tired","minLabelEl":"Κουρασμένος","maxLabel":"Fresh","maxLabelEl":"Φρέσκος","required":true}]},{"id":"s2","title":"Habits","questions":[{"id":"q2","type":"multiselect","label":"Which?","options":["a","b"],"optionsEl":["α","β"]}]}]}""";

    private static async Task<JsonElement> ResolvedSchemaAsync(string lang)
    {
        await using var db = DbContextFactory.CreateInMemory();
        var assessment = Published(SectionedSchemaJson);
        db.AssessmentDefinitions.Add(assessment);
        await db.SaveChangesAsync();

        var result = await NewService(db).GetAssessmentAsync(UserId, assessment.Id, lang);

        result.IsSuccess.ShouldBeTrue();
        return JsonDocument.Parse(result.Value!.SchemaJson).RootElement.Clone();
    }

    [Fact]
    public async Task GetAssessment_Greek_ResolvesSectionTitlesAndRatingLabels()
    {
        var schema = await ResolvedSchemaAsync("el");

        var sections = schema.GetProperty("sections");
        sections.GetArrayLength().ShouldBe(2);
        sections[0].GetProperty("title").GetString().ShouldBe("Ενέργεια");
        sections[0].GetProperty("questionIds")[0].GetString().ShouldBe("q1");
        // A section without titleEl falls back to the English title.
        sections[1].GetProperty("title").GetString().ShouldBe("Habits");

        var rating = schema.GetProperty("questions")[0];
        rating.GetProperty("label").GetString().ShouldBe("Πόσο ξεκούραστος;");
        rating.GetProperty("minLabel").GetString().ShouldBe("Κουρασμένος");
        rating.GetProperty("maxLabel").GetString().ShouldBe("Φρέσκος");
        rating.TryGetProperty("minLabelEl", out _).ShouldBeFalse();
        schema.GetProperty("questions")[1].GetProperty("options")[0].GetString().ShouldBe("α");
        schema.GetProperty("title").GetString().ShouldBe("Έλεγχος");
    }

    [Fact]
    public async Task GetAssessment_English_KeepsEnglishTextAndFlatQuestions()
    {
        var schema = await ResolvedSchemaAsync("en");

        schema.GetProperty("sections")[0].GetProperty("title").GetString().ShouldBe("Energy");
        var questions = schema.GetProperty("questions");
        questions.GetArrayLength().ShouldBe(2);
        questions[0].GetProperty("minLabel").GetString().ShouldBe("Tired");
        questions[1].GetProperty("type").GetString().ShouldBe("multiselect");
        questions[1].GetProperty("options")[1].GetString().ShouldBe("b");
    }
}
