using System.Net;
using FitEdu.Domain.Entities;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The course pages are in the sitemap, so their prerendered head carries the search and social
/// tags: a canonical link without the filter query, Open Graph and a plain-text description.
/// </summary>
public class CourseSeoTests : IClassFixture<SelfCallingWebAppFactory>
{
    private const string SiteBaseUrl = "https://tests.local";

    private readonly SelfCallingWebAppFactory _factory;

    public CourseSeoTests(SelfCallingWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task CourseDetail_HeadCarriesCanonicalOpenGraphAndAPlainDescription()
    {
        var course = new Course
        {
            Id = Guid.NewGuid(),
            TitleEn = "SEO programme",
            DescriptionEn = "<p>Build <strong>strength</strong> &amp; confidence.</p>",
            IsPublished = true
        };
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync($"/courses/{course.Id}");

        body.ShouldContain($"<link rel=\"canonical\" href=\"{SiteBaseUrl}/courses/{course.Id}\"");
        body.ShouldContain("<meta property=\"og:title\" content=\"SEO programme\"");
        body.ShouldContain("<meta name=\"description\" content=\"Build strength &amp; confidence.\"");
    }

    [Fact]
    public async Task CourseList_CanonicalLeavesOutTheFilters()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/courses?search=core&page=2");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"<link rel=\"canonical\" href=\"{SiteBaseUrl}/courses\"");
    }
}
