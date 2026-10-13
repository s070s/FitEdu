using System.Net;
using FitEdu.Domain.Entities;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Detail pages for a missing exercise, article or programme answer a real 404 (so crawlers drop
/// them) while still showing their friendly not-found view; existing ones answer 200.
/// </summary>
public class PageStatusCodeTests : IClassFixture<SelfCallingWebAppFactory>
{
    private readonly SelfCallingWebAppFactory _factory;

    public PageStatusCodeTests(SelfCallingWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ExerciseDetail_Missing_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/exercises/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("empty-state", Case.Sensitive);
    }

    [Fact]
    public async Task ExerciseDetail_Published_Returns200()
    {
        var slug = $"status-{Guid.NewGuid():N}";
        await _factory.SeedAsync(async db =>
        {
            db.Exercises.Add(new Exercise
            {
                Id = Guid.NewGuid(), Slug = slug, NameEn = "Status squat", Category = "strength", Level = "beginner",
                PrimaryMuscles = "quadriceps", SecondaryMuscles = string.Empty, InstructionsEn = "Squat.", IsPublished = true
            });
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/exercises/{slug}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BlogArticle_Missing_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/blog/no-such-article");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("blog-article-page", Case.Sensitive);
    }

    [Fact]
    public async Task BlogArticle_Published_Returns200()
    {
        var slug = $"status-{Guid.NewGuid():N}";
        await _factory.SeedAsync(async db =>
        {
            db.BlogArticles.Add(new BlogArticle
            {
                Id = Guid.NewGuid(), Slug = slug, TitleEn = "Status article", SummaryEn = "Summary.",
                ContentEn = "<p>Body.</p>", AuthorName = "Coach", IsPublished = true, PublishedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/blog/{slug}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CourseDetail_Missing_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/courses/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("course-state", Case.Sensitive);
    }

    [Fact]
    public async Task CourseDetail_Published_Returns200()
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "Status programme", IsPublished = true };
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/courses/{course.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Status programme");
    }
}
