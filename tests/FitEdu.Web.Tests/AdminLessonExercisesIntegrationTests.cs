using System.Net;
using System.Net.Http.Json;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// HTTP coverage for the admin fitness fields: lesson exercise rows round-trip through
/// PUT/GET /api/admin/lessons, and unknown exercise slugs or out-of-range values are rejected with 400.
/// </summary>
public class AdminLessonExercisesIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AdminLessonExercisesIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    private static Exercise NewExercise(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        NameEn = $"{slug} name",
        Category = "strength",
        Level = "beginner",
        PrimaryMuscles = "quadriceps",
        SecondaryMuscles = string.Empty,
        InstructionsEn = "Step one.",
        IsPublished = true
    };

    private static string UniqueSlug(string name) => $"{name}-{Guid.NewGuid():N}";

    private static SaveLessonRequest LessonRequest(Guid moduleId, List<LessonExerciseEditDto>? exercises) =>
        new("Leg day", null, null, null, null, 30, 1, moduleId, exercises);

    private async Task<Lesson> SeedLessonAsync()
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "Strength basics" };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "Week 1", CourseId = course.Id };
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "Leg day", ModuleId = module.Id };
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            db.Modules.Add(module);
            db.Lessons.Add(lesson);
            await db.SaveChangesAsync();
        });
        return lesson;
    }

    [Fact]
    public async Task UpdateLesson_WithExercises_RoundTripsInOrder()
    {
        var squatSlug = UniqueSlug("squat");
        var plankSlug = UniqueSlug("plank");
        await _factory.SeedAsync(async db =>
        {
            db.Exercises.AddRange(NewExercise(squatSlug), NewExercise(plankSlug));
            await db.SaveChangesAsync();
        });
        var lesson = await SeedLessonAsync();
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PutAsJsonAsync($"/api/admin/lessons/{lesson.Id}", LessonRequest(lesson.ModuleId,
        [
            new LessonExerciseEditDto(plankSlug, Reps: "30 s"),
            new LessonExerciseEditDto(squatSlug, Sets: 3, Reps: "8–12", RestSeconds: 90, NotesEn: "Brace", NotesEl: "Σφίξτε")
        ]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reloaded = await client.GetFromJsonAsync<AdminLessonDto>($"/api/admin/lessons/{lesson.Id}");
        reloaded!.Exercises!.Select(row => row.Slug).ShouldBe([plankSlug, squatSlug]);
        reloaded.Exercises![1].Sets.ShouldBe(3);
        reloaded.Exercises![1].NotesEl.ShouldBe("Σφίξτε");
        reloaded.Exercises![1].Name.ShouldBe($"{squatSlug} name");
    }

    [Fact]
    public async Task CreateLesson_UnknownExerciseSlug_Returns400()
    {
        var lesson = await SeedLessonAsync();
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PostAsJsonAsync("/api/admin/lessons",
            LessonRequest(lesson.ModuleId, [new LessonExerciseEditDto("no-such-move")]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("no-such-move");
    }

    [Fact]
    public async Task UpdateLesson_SetsOutOfRange_Returns400()
    {
        var squatSlug = UniqueSlug("squat");
        await _factory.SeedAsync(async db =>
        {
            db.Exercises.Add(NewExercise(squatSlug));
            await db.SaveChangesAsync();
        });
        var lesson = await SeedLessonAsync();
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PutAsJsonAsync($"/api/admin/lessons/{lesson.Id}",
            LessonRequest(lesson.ModuleId, [new LessonExerciseEditDto(squatSlug, Sets: 0)]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCourse_UnknownCoverExerciseSlug_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        var request = new SaveCourseRequest("Cover test", null, null, null, SubscriptionTier.Free,
            CoverExerciseSlug: "no-such-move");

        var response = await client.PostAsJsonAsync("/api/admin/courses", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCourse_WithFitnessFields_Returns201WithThem()
    {
        var coverSlug = UniqueSlug("squat");
        await _factory.SeedAsync(async db =>
        {
            db.Exercises.Add(NewExercise(coverSlug));
            await db.SaveChangesAsync();
        });
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        var request = new SaveCourseRequest("Strength 101", null, null, null, SubscriptionTier.Free,
            Level: CourseLevel.Intermediate, Goal: FitnessGoal.Strength, Equipment: "barbell",
            EstimatedWeeks: 6, SessionsPerWeek: 3, CoverExerciseSlug: coverSlug);

        var response = await client.PostAsJsonAsync("/api/admin/courses", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<AdminCourseDto>();
        created!.Level.ShouldBe(CourseLevel.Intermediate);
        created.Goal.ShouldBe(FitnessGoal.Strength);
        created.CoverExerciseSlug.ShouldBe(coverSlug);
    }

    [Fact]
    public async Task PublishLesson_EndpointIsGone_LessonsFollowTheirCourse()
    {
        // Students see every lesson of a published course, so a per-lesson publish flag has no effect.
        var lesson = await SeedLessonAsync();
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PostAsync($"/api/admin/lessons/{lesson.Id}/publish", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
