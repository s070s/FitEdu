using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Application.Common;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// Title-fallback queries use the same casing as the seeded titles: InMemory's .Contains is
/// ordinal (case-sensitive), unlike SQL Server's default case-insensitive collation — matches
/// the same documented caveat as UserSearchExtensionsTests.
/// </summary>
public class SiteSearchServiceTests
{
    private static (SiteSearchService svc, IAssistantRetrievalService retrieval, AssistantRuntimeState state) NewService(
        ApplicationDbContext db, AssistantAvailability availability = AssistantAvailability.Ready)
    {
        var retrieval = Substitute.For<IAssistantRetrievalService>();
        var state = new AssistantRuntimeState();
        state.Set(availability);

        var svc = new SiteSearchService(db, retrieval, state, NullLogger<SiteSearchService>.Instance);
        return (svc, retrieval, state);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyWithoutCallingRetrieval()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (svc, retrieval, _) = NewService(db);

        var result = await svc.SearchAsync("   ", "en", 8);

        result.Hits.ShouldBeEmpty();
        await retrieval.DidNotReceiveWithAnyArgs().SearchGroupedAsync(default!, default!, default);
    }

    [Fact]
    public async Task SearchAsync_Ready_UsesSemanticResultsAndFlagsAsSemanticUsed()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (svc, retrieval, _) = NewService(db);
        retrieval.SearchGroupedAsync("strength", "en", 8, Arg.Any<CancellationToken>())
            .Returns([new AssistantSearchHit(AssistantSourceType.Course, "Strength Start", "courses/1", "snippet")]);

        var result = await svc.SearchAsync("strength", "en", 8);

        result.SemanticSearchUsed.ShouldBeTrue();
        var hit = result.Hits.ShouldHaveSingleItem();
        hit.SourceType.ShouldBe("Course");
        hit.Title.ShouldBe("Strength Start");
        hit.Url.ShouldBe("courses/1");
    }

    [Theory]
    [InlineData(AssistantAvailability.Disabled)]
    [InlineData(AssistantAvailability.OllamaUnreachable)]
    [InlineData(AssistantAvailability.DownloadingModels)]
    public async Task SearchAsync_NotReady_SkipsSemanticAndUsesTitleFallback(AssistantAvailability availability)
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Strength Start", IsPublished = true });
        await db.SaveChangesAsync();
        var (svc, retrieval, _) = NewService(db, availability);

        var result = await svc.SearchAsync("Strength", "en", 8);

        result.SemanticSearchUsed.ShouldBeFalse();
        result.Hits.ShouldHaveSingleItem().Title.ShouldBe("Strength Start");
        await retrieval.DidNotReceiveWithAnyArgs().SearchGroupedAsync(default!, default!, default);
    }

    [Fact]
    public async Task SearchAsync_ReadyButRetrievalThrows_FallsBackToTitleSearch()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Strength Start", IsPublished = true });
        await db.SaveChangesAsync();
        var (svc, retrieval, _) = NewService(db);
        retrieval.SearchGroupedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ollama down mid-request"));

        var result = await svc.SearchAsync("Strength", "en", 8);

        result.SemanticSearchUsed.ShouldBeFalse();
        result.Hits.ShouldHaveSingleItem().Title.ShouldBe("Strength Start");
    }

    [Fact]
    public async Task SearchAsync_ReadyButSemanticFindsNothing_FallsBackToTitleSearch()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Strength Start", IsPublished = true });
        await db.SaveChangesAsync();
        var (svc, retrieval, _) = NewService(db);
        retrieval.SearchGroupedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await svc.SearchAsync("Strength", "en", 8);

        result.SemanticSearchUsed.ShouldBeFalse();
        result.Hits.ShouldHaveSingleItem().Title.ShouldBe("Strength Start");
    }

    [Fact]
    public async Task TitleFallback_MatchesAcrossCoursesAssessmentsAndArticles_PublishedOnly()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Strength Foundations", IsPublished = true });
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Strength Draft (unpublished)", IsPublished = false });
        db.AssessmentDefinitions.Add(new AssessmentDefinition { Id = Guid.NewGuid(), Key = "k1", TitleEn = "Strength Readiness Assessment", SchemaJson = "{}", IsPublished = true });
        db.BlogArticles.Add(new BlogArticle
        {
            Id = Guid.NewGuid(), TitleEn = "5 Strength Tips", Slug = "strength-tips", SummaryEn = "s", ContentEn = "c",
            AuthorName = "A", IsPublished = true
        });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, AssistantAvailability.Disabled);

        var result = await svc.SearchAsync("Strength", "en", 20);

        result.Hits.Count.ShouldBe(3); // unpublished course excluded
        result.Hits.ShouldContain(h => h.SourceType == "Course" && h.Title == "Strength Foundations");
        result.Hits.ShouldContain(h => h.SourceType == "Assessment" && h.Title == "Strength Readiness Assessment");
        result.Hits.ShouldContain(h => h.SourceType == "BlogArticle" && h.Title == "5 Strength Tips");
    }

    private static Exercise PublishedExercise(string slug, string nameEn, string? nameEl = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameEn = nameEn,
            NameEl = nameEl,
            Category = "strength",
            Level = "beginner",
            PrimaryMuscles = "chest",
            SecondaryMuscles = string.Empty,
            InstructionsEn = "Step.",
            IsPublished = true
        };

    [Fact]
    public async Task TitleFallback_MatchesPublishedExercisesByName_LinkingToTheirSlug()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var hidden = PublishedExercise("barbell-squat-draft", "Barbell Squat Draft");
        hidden.IsPublished = false;
        db.Exercises.AddRange(PublishedExercise("barbell-squat", "Barbell Squat"), hidden);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, AssistantAvailability.Disabled);

        var result = await svc.SearchAsync("Squat", "en", 8);

        var hit = result.Hits.ShouldHaveSingleItem();
        hit.SourceType.ShouldBe("Exercise");
        hit.Title.ShouldBe("Barbell Squat");
        hit.Url.ShouldBe("exercises/barbell-squat");
    }

    [Fact]
    public async Task TitleFallback_Greek_MatchesGreekExerciseNameAndShowsIt()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Exercises.Add(PublishedExercise("plank", "Plank", nameEl: "Σανίδα"));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, AssistantAvailability.Disabled);

        var result = await svc.SearchAsync("Σανίδα", "el", 8);

        var hit = result.Hits.ShouldHaveSingleItem();
        hit.Title.ShouldBe("Σανίδα");
        hit.Url.ShouldBe("exercises/plank");
    }

    private static void AddTwoPowerMatchesPerSource(ApplicationDbContext db)
    {
        for (var number = 1; number <= 2; number++)
        {
            db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = $"Power Course {number}", IsPublished = true });
            db.AssessmentDefinitions.Add(new AssessmentDefinition
            {
                Id = Guid.NewGuid(), Key = $"power-{number}", TitleEn = $"Power Check {number}", SchemaJson = "{}", IsPublished = true
            });
            db.BlogArticles.Add(new BlogArticle
            {
                Id = Guid.NewGuid(), TitleEn = $"Power Tips {number}", Slug = $"power-tips-{number}", SummaryEn = "s",
                ContentEn = "c", AuthorName = "A", IsPublished = true
            });
            db.Exercises.Add(PublishedExercise($"power-clean-{number}", $"Power Clean {number}"));
        }
    }

    [Fact]
    public async Task TitleFallback_ShortLimit_KeepsAHitFromEverySource_GroupedBySource()
    {
        // The search box asks for 5: the best hit of every source comes before anyone's second hit,
        // so exercises (queried last) are not the first to be cut.
        await using var db = DbContextFactory.CreateInMemory();
        AddTwoPowerMatchesPerSource(db);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, AssistantAvailability.Disabled);

        var result = await svc.SearchAsync("Power", "en", 5);

        result.Hits.Select(hit => hit.Title)
            .ShouldBe(["Power Course 1", "Power Course 2", "Power Check 1", "Power Tips 1", "Power Clean 1"]);
    }

    [Fact]
    public async Task TitleFallback_OnlyOneSourceMatches_FillsTheWholeLimitFromIt()
    {
        await using var db = DbContextFactory.CreateInMemory();
        for (var number = 1; number <= 8; number++)
            db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = $"Power Course {number}", IsPublished = true });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, AssistantAvailability.Disabled);

        var result = await svc.SearchAsync("Power", "en", 20);

        result.Hits.Count.ShouldBe(8);
    }

    [Fact]
    public async Task TitleFallback_NoMatch_ReturnsEmptyHits()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Strength Start", IsPublished = true });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, AssistantAvailability.Disabled);

        (await svc.SearchAsync("zzz-no-match", "en", 8)).Hits.ShouldBeEmpty();
    }
}
