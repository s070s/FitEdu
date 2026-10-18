using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using FitEdu.Application.ApiServices;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Infrastructure.Data;
using FitEdu.Infrastructure.Seeding;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// Data-logic tests for each assistant tool against the InMemory provider, plus the
/// tool-surface contract (seven tools, none for an empty identity, no user-id parameters).
/// The exercise search runs on the real exercise library seeded into SQLite.
/// </summary>
public class AssistantToolsTests : IClassFixture<RealExerciseLibraryDatabase>
{
    private const string UserId = "student-1";
    private const string OtherUserId = "student-2";

    private readonly RealExerciseLibraryDatabase _library;

    public AssistantToolsTests(RealExerciseLibraryDatabase library)
    {
        _library = library;
    }

    private static AssistantTools NewTools(ApplicationDbContext db) => new(db, new ExerciseService(db, new MemoryCache(new MemoryCacheOptions())));

    private static Course NewCourse(string titleEn, string? titleEl = null, bool published = true,
        Category? category = null, params int[] lessonsPerModule)
    {
        var course = new Course
        {
            Id = Guid.NewGuid(),
            TitleEn = titleEn,
            TitleEl = titleEl,
            DescriptionEn = $"About {titleEn}",
            IsPublished = published,
            Category = category
        };
        foreach (var lessonCount in lessonsPerModule)
        {
            var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
            for (var i = 0; i < lessonCount; i++)
                module.Lessons.Add(new Lesson { Id = Guid.NewGuid(), TitleEn = $"L{i}" });
            course.Modules.Add(module);
        }
        return course;
    }

    [Fact]
    public void GetToolsForUser_EmptyIdentity_ReturnsNoTools()
    {
        using var db = DbContextFactory.CreateInMemory();

        NewTools(db).GetToolsForUser("", "en").ShouldBeEmpty();
        NewTools(db).GetToolsForUser("  ", "en").ShouldBeEmpty();
    }

    [Fact]
    public void GetToolsForUser_Authenticated_ExposesTheSevenSelfServiceTools()
    {
        using var db = DbContextFactory.CreateInMemory();

        var tools = NewTools(db).GetToolsForUser(UserId, "en");

        tools.Select(t => t.Name).ShouldBe([
            "get_my_enrollments", "get_my_progress", "get_my_assessment_results",
            "get_subscription_status", "search_courses", "search_exercises", "recommend_courses"]);
    }

    [Fact]
    public async Task GetMyEnrollments_ReturnsOwnCoursesLocalized()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var mine = NewCourse("Bodyweight Basics", "Βασικές ασκήσεις με βάρος σώματος");
        var other = NewCourse("Other Course");
        db.Courses.AddRange(mine, other);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = mine.Id });
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = OtherUserId, CourseId = other.Id });
        await db.SaveChangesAsync();

        var en = await NewTools(db).GetMyEnrollmentsAsync(UserId, isEl: false);
        var el = await NewTools(db).GetMyEnrollmentsAsync(UserId, isEl: true);

        en.ShouldHaveSingleItem().Course.ShouldBe("Bodyweight Basics");
        el.ShouldHaveSingleItem().Course.ShouldBe("Βασικές ασκήσεις με βάρος σώματος");
    }

    [Fact]
    public async Task GetMyProgress_CountsCompletedAndTotalLessons()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = NewCourse("Core & Stability", lessonsPerModule: [2, 3]); // 5 lessons total
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        var lessons = course.Modules.SelectMany(m => m.Lessons).Take(2).ToList();
        foreach (var lesson in lessons)
            db.LessonCompletions.Add(new LessonCompletion { Id = Guid.NewGuid(), UserId = UserId, LessonId = lesson.Id });
        // Another user's completion must not count.
        db.LessonCompletions.Add(new LessonCompletion
        {
            Id = Guid.NewGuid(),
            UserId = OtherUserId,
            LessonId = course.Modules.SelectMany(m => m.Lessons).Last().Id
        });
        await db.SaveChangesAsync();

        var progress = await NewTools(db).GetMyProgressAsync(UserId, isEl: false);

        var row = progress.ShouldHaveSingleItem();
        row.CompletedLessons.ShouldBe(2);
        row.TotalLessons.ShouldBe(5);
    }

    [Fact]
    public async Task GetMyAssessmentResults_ReturnsRecentTitlesOnly()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var definition = new AssessmentDefinition { Id = Guid.NewGuid(), Key = "k", TitleEn = "Training Readiness", SchemaJson = "{}" };
        db.AssessmentDefinitions.Add(definition);
        for (var i = 0; i < 7; i++)
            db.AssessmentSubmissions.Add(new AssessmentSubmission
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                AssessmentDefinitionId = definition.Id,
                AnswersJson = "{\"secret\":\"answer\"}",
                SubmittedAt = new DateTimeOffset(2024, 1, i + 1, 0, 0, 0, TimeSpan.Zero)
            });
        await db.SaveChangesAsync();

        var results = await NewTools(db).GetMyAssessmentResultsAsync(UserId, isEl: false);

        results.Count.ShouldBe(5); // capped, most recent first
        results[0].SubmittedOn.ShouldBe("2024-01-07");
        results.ShouldAllBe(r => r.Assessment == "Training Readiness");
    }

    [Fact]
    public async Task GetSubscriptionStatus_ActiveSubscription_ReportsPlan_OtherwiseFree()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro Monthly", Tier = SubscriptionTier.Pro };
        db.SubscriptionPlans.Add(plan);
        db.UserSubscriptions.Add(new UserSubscription
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            SubscriptionPlanId = plan.Id,
            IsActive = true,
            ExpiresAt = new DateTime(2026, 12, 31)
        });
        await db.SaveChangesAsync();

        var subscribed = await NewTools(db).GetSubscriptionStatusAsync(UserId);
        var free = await NewTools(db).GetSubscriptionStatusAsync(OtherUserId);

        subscribed.Plan.ShouldBe("Pro Monthly");
        subscribed.Tier.ShouldBe("Pro");
        subscribed.ExpiresOn.ShouldBe("2026-12-31");
        free.Plan.ShouldBe("Free");
        free.ExpiresOn.ShouldBeNull();
    }

    [Fact]
    public async Task SearchCourses_MatchesPublishedOnly_CapsResults()
    {
        await using var db = DbContextFactory.CreateInMemory();
        for (var i = 0; i < 8; i++)
            db.Courses.Add(NewCourse($"Kettlebell Skills {i}"));
        db.Courses.Add(NewCourse("Kettlebell Draft", published: false));
        db.Courses.Add(NewCourse("Unrelated"));
        await db.SaveChangesAsync();

        var hits = await NewTools(db).SearchCoursesAsync("Kettlebell", maxResults: 99, isEl: false);

        hits.Count.ShouldBe(5); // clamped to the cap
        hits.ShouldAllBe(h => h.Title.StartsWith("Kettlebell Skills"));
    }

    [Fact]
    public async Task SearchExercises_RealLibrary_FiltersByNameMuscleAndEquipment_CapsResults()
    {
        var tools = NewTools(_library.Db);

        // The model may capitalise filter values; the library stores them in lower case.
        var hits = await tools.SearchExercisesAsync("squat", "Quadriceps", " Barbell ", maxResults: 99, language: "en");

        hits.Count.ShouldBe(5); // 30 barbell squats work the quadriceps; clamped to the cap
        hits[0].Name.ShouldBe("Barbell Full Squat"); // ordered by English name
        hits.ShouldAllBe(hit => hit.Name.Contains("Squat") && hit.Equipment == "barbell");
        hits.ShouldAllBe(hit => hit.PrimaryMuscles.Contains("quadriceps"));
        hits[0].Page.ShouldBe("/exercises/barbell-full-squat");
    }

    [Fact]
    public async Task SearchExercises_RealLibrary_MuscleOnlyInGreek_ReturnsGreekNames()
    {
        var tools = NewTools(_library.Db);

        var hits = await tools.SearchExercisesAsync(query: "", muscle: "chest", equipment: "body only", maxResults: 2, language: "el");

        // The first two by English name are Clock Push-Up and Incline Push-Up; both have Greek names.
        hits.Count.ShouldBe(2);
        hits.ShouldAllBe(hit => hit.PrimaryMuscles.Contains("chest") && hit.Equipment == "body only");
        hits.Select(hit => hit.Page).ShouldBe(["/exercises/clock-push-up", "/exercises/incline-push-up"]);
        hits.Select(hit => hit.Name).ShouldBe(["Push-up «ρολόι»", "Push-up με τα χέρια ψηλά"]);
    }

    [Fact]
    public async Task SearchExercisesTool_Description_ListsExactlyTheRealLibraryMuscleAndEquipmentValues()
    {
        // The filters match whole English values only, so the model must be told the real vocabulary.
        var facets = await new ExerciseService(_library.Db, new MemoryCache(new MemoryCacheOptions())).GetFacetsAsync();

        AssistantTools.MuscleFilterValues.ShouldBe(facets.Muscles.Select(facet => facet.Value));
        AssistantTools.EquipmentFilterValues.ShouldBe(facets.Equipment.Select(facet => facet.Value));
        var description = NewTools(_library.Db).GetToolsForUser(UserId, "el").Single(tool => tool.Name == "search_exercises").Description;
        description.ShouldContain(string.Join(", ", AssistantTools.MuscleFilterValues));
        description.ShouldContain(string.Join(", ", AssistantTools.EquipmentFilterValues));
    }

    [Fact]
    public async Task SearchExercisesTool_InvokedWithOnlyAMuscle_UsesDefaultsForTheOtherInputs()
    {
        // The model calls the tool with only the inputs it needs, e.g. {"muscle":"lats"}.
        var tool = FindTool(NewTools(_library.Db), "search_exercises");

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["muscle"] = "chest" });

        var hits = JsonSerializer.SerializeToElement(result);
        hits.GetArrayLength().ShouldBe(5); // the default cap
        hits.EnumerateArray().ShouldAllBe(hit => hit.GetRawText().Contains("chest") && hit.GetRawText().Contains("/exercises/"));
        RequiredInputs(tool).ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchCoursesTool_InvokedWithOnlyAQuery_UsesTheDefaultCap()
    {
        await using var db = DbContextFactory.CreateInMemory();
        for (var i = 0; i < 7; i++)
            db.Courses.Add(NewCourse($"Mobility Flow {i}"));
        await db.SaveChangesAsync();
        var tool = FindTool(NewTools(db), "search_courses");

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "Mobility" });

        JsonSerializer.SerializeToElement(result).GetArrayLength().ShouldBe(5);
        RequiredInputs(tool).ShouldBe(["query"]); // a course search always needs a keyword
    }

    private static AIFunction FindTool(AssistantTools tools, string name) =>
        tools.GetToolsForUser(UserId, "en").OfType<AIFunction>().Single(tool => tool.Name == name);

    /// <summary>The inputs the JSON schema sent to the model marks as required.</summary>
    private static List<string?> RequiredInputs(AIFunction tool)
    {
        if (!tool.JsonSchema.TryGetProperty("required", out var required))
            return [];

        return required.EnumerateArray().Select(input => input.GetString()).ToList();
    }

    [Fact]
    public async Task SearchExercises_NoQueryAndNoFilter_ReturnsNothing()
    {
        var tools = NewTools(_library.Db);

        var hits = await tools.SearchExercisesAsync(" ", muscle: null, equipment: null, maxResults: null, language: "en");

        hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task RecommendCourses_PrefersOwnCategories_ExcludesEnrolled_FallsBackToAnyPublished()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var strength = new Category { Id = Guid.NewGuid(), NameEn = "Strength" };
        var enrolled = NewCourse("Strength Foundations", category: strength);
        var sameCategory = NewCourse("Lower Body Power", category: strength);
        var otherCategory = NewCourse("Nutrition Basics", category: new Category { Id = Guid.NewGuid(), NameEn = "Nutrition" });
        db.Courses.AddRange(enrolled, sameCategory, otherCategory);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = enrolled.Id });
        await db.SaveChangesAsync();

        var forMe = await NewTools(db).RecommendCoursesAsync(UserId, isEl: false);
        var forNewUser = await NewTools(db).RecommendCoursesAsync(OtherUserId, isEl: false);

        forMe.ShouldHaveSingleItem().Title.ShouldBe("Lower Body Power"); // same category, not enrolled
        forNewUser.Select(c => c.Title).ShouldBe(["Lower Body Power", "Nutrition Basics", "Strength Foundations"]);
    }
}

/// <summary>
/// The real exercise library (src/FitEdu.Shared/JSON/Exercises) seeded once into SQLite by the
/// real seeder, so the exercise search runs on the data members actually search.
/// </summary>
public sealed class RealExerciseLibraryDatabase : IAsyncLifetime
{
    public ApplicationDbContext Db { get; } = DbContextFactory.CreateSqlite();

    public async Task InitializeAsync()
    {
        var logger = new RecordingLogger();
        await ExerciseSeeder.SeedFromJsonAsync(Db, RepoPaths.ExerciseSeedFolder, logger);
        logger.Errors.ShouldBeEmpty();
        Db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
}
