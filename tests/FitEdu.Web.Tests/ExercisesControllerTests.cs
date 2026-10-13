using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;
using FitEdu.Shared.Resources.Messages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The exercise library is public: every read endpoint answers anonymous callers (like
/// <see cref="SearchControllerTests"/>) and only shows published exercises. Each test seeds its
/// own unique slugs because the class shares one InMemory database.
/// </summary>
public class ExercisesControllerTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ExercisesControllerTests(CustomWebAppFactory factory) => _factory = factory;

    private const string GreekCulture = "el-GR";

    private static string UniqueSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<Exercise> SeedExerciseAsync(string slug, bool published = true, string primaryMuscles = "chest")
    {
        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameEn = slug,
            Category = "strength",
            Level = "beginner",
            Equipment = "body only",
            PrimaryMuscles = primaryMuscles,
            SecondaryMuscles = string.Empty,
            InstructionsEn = "Step one.",
            IsPublished = published
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Exercises.Add(exercise);
        await db.SaveChangesAsync();
        return exercise;
    }

    [Fact]
    public async Task GetExercises_Anonymous_ReturnsPublishedOnly()
    {
        var client = _factory.CreateClient();
        var muscle = UniqueSlug("muscle");
        var published = await SeedExerciseAsync(UniqueSlug("published"), primaryMuscles: muscle);
        var hidden = await SeedExerciseAsync(UniqueSlug("hidden"), published: false, primaryMuscles: muscle);

        var response = await client.GetAsync($"/api/exercises?muscle={muscle}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ExerciseListItemDto>>();
        result!.Items.ShouldContain(item => item.Slug == published.Slug);
        result.Items.ShouldNotContain(item => item.Slug == hidden.Slug);
    }

    [Fact]
    public async Task GetExercise_Anonymous_ReturnsDetail()
    {
        var client = _factory.CreateClient();
        var exercise = await SeedExerciseAsync(UniqueSlug("detail"));

        var detail = await client.GetFromJsonAsync<ExerciseDetailDto>($"/api/exercises/{exercise.Slug}");

        detail.ShouldNotBeNull();
        detail.Id.ShouldBe(exercise.Id);
        detail.Image0Url.ShouldBe($"/images/exercises/{exercise.Slug}/0.webp");
    }

    [Fact]
    public async Task GetExercise_UnknownSlug_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/exercises/{UniqueSlug("missing")}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetExercise_UnknownSlug_ExplainsInTheRequestLanguage()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(GreekCulture);
        var expectedDetail = ErrorMessagesRes.ResourceManager.GetString(
            nameof(ErrorMessagesRes.Exercise_NotFound), new CultureInfo(GreekCulture));

        var response = await client.GetAsync($"/api/exercises/{UniqueSlug("missing")}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.ShouldBe(expectedDetail);
    }

    [Fact]
    public async Task GetExercise_Unpublished_Returns404()
    {
        var client = _factory.CreateClient();
        var exercise = await SeedExerciseAsync(UniqueSlug("draft"), published: false);

        var response = await client.GetAsync($"/api/exercises/{exercise.Slug}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFacets_Anonymous_ReturnsCounts()
    {
        var client = _factory.CreateClient();
        var muscle = UniqueSlug("muscle");
        await SeedExerciseAsync(UniqueSlug("facet"), primaryMuscles: muscle);

        var facets = await client.GetFromJsonAsync<ExerciseFacetsDto>("/api/exercises/facets");

        facets.ShouldNotBeNull();
        facets.Muscles.ShouldContain(facet => facet.Value == muscle && facet.Count == 1);
        facets.Levels.ShouldContain(facet => facet.Value == "beginner");
    }

    [Fact]
    public async Task GetLessonsUsingExercise_Anonymous_ReturnsPrescribingLessons()
    {
        var client = _factory.CreateClient();
        var exercise = await SeedExerciseAsync(UniqueSlug("used"));
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "Course", IsPublished = true };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "Module", SortOrder = 1, CourseId = course.Id };
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "Lesson", SortOrder = 1 };
        lesson.Exercises.Add(new LessonExercise { Id = Guid.NewGuid(), ExerciseId = exercise.Id, SortOrder = 1 });
        module.Lessons.Add(lesson);
        course.Modules.Add(module);
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });

        var lessons = await client.GetFromJsonAsync<List<LessonUsingExerciseDto>>($"/api/exercises/{exercise.Slug}/lessons");

        var usage = lessons.ShouldHaveSingleItem();
        usage.LessonId.ShouldBe(lesson.Id);
        usage.CourseTitle.ShouldBe("Course");
    }

    [Fact]
    public async Task Sitemap_ListsTheLibraryAndEachPublishedExercise()
    {
        // The only sitemap request in this class (the XML is cached for 30 minutes), so the
        // exercises seeded here are always in it.
        var client = _factory.CreateClient();
        var published = await SeedExerciseAsync(UniqueSlug("sitemap"));
        var hidden = await SeedExerciseAsync(UniqueSlug("sitemap-draft"), published: false);

        var xml = await client.GetStringAsync("/sitemap.xml");

        xml.ShouldContain("<loc>https://tests.local/exercises</loc>");
        xml.ShouldContain($"<loc>https://tests.local/exercises/{published.Slug}</loc>");
        xml.ShouldNotContain(hidden.Slug);
    }

    [Fact]
    public async Task Robots_AllowsTheExerciseLibrary()
    {
        var client = _factory.CreateClient();

        var robots = await client.GetStringAsync("/robots.txt");

        robots.ShouldContain("Allow: /exercises");
    }
}
