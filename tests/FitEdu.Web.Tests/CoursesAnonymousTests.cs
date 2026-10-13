using System.Net;
using System.Net.Http.Json;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The course catalogue is public: a guest can list programmes, open one and read its reviews and
/// categories, but joining, opening a lesson and completing it still need a signed-in member.
/// </summary>
public class CoursesAnonymousTests : IClassFixture<CustomWebAppFactory>
{
    private const string LessonBody = "members-only-lesson-body";

    private readonly CustomWebAppFactory _factory;

    public CoursesAnonymousTests(CustomWebAppFactory factory) => _factory = factory;

    private async Task<(Course Course, Lesson Lesson)> SeedCourseWithLessonAsync()
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = $"Public-{Guid.NewGuid():N}", IsPublished = true };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "Week 1", SortOrder = 1, CourseId = course.Id };
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "Day 1", SortOrder = 1, ContentEn = LessonBody, DurationMinutes = 20 };
        module.Lessons.Add(lesson);
        course.Modules.Add(module);
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });
        return (course, lesson);
    }

    [Fact]
    public async Task GetCourses_Anonymous_Returns200WithNothingEnrolled()
    {
        var (course, _) = await SeedCourseWithLessonAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/courses?search={course.TitleEn}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<CourseListItemDto>>();
        var item = result!.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(course.Id);
        item.IsEnrolled.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCourse_Anonymous_Returns200WithTheOutlineButNoLessonContent()
    {
        var (course, lesson) = await SeedCourseWithLessonAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/courses/{course.Id}");
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.ShouldNotContain(LessonBody);
        var detail = await response.Content.ReadFromJsonAsync<CourseDetailDto>();
        detail!.IsEnrolled.ShouldBeFalse();
        detail.CompletedLessons.ShouldBe(0);
        detail.ProgressPercent.ShouldBe(0);
        var summary = detail.Modules.ShouldHaveSingleItem().Lessons.ShouldHaveSingleItem();
        summary.Id.ShouldBe(lesson.Id);
        summary.Title.ShouldBe("Day 1");
        summary.DurationMinutes.ShouldBe(20);
        summary.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCourse_Anonymous_UnknownId_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/courses/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Enroll_Anonymous_Returns401()
    {
        var (course, _) = await SeedCourseWithLessonAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/courses/{course.Id}/enroll", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLesson_Anonymous_Returns401()
    {
        var (_, lesson) = await SeedCourseWithLessonAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/courses/lessons/{lesson.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CompleteLesson_Anonymous_Returns401()
    {
        var (_, lesson) = await SeedCourseWithLessonAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/courses/lessons/{lesson.Id}/complete", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SaveReview_Anonymous_Returns401()
    {
        var (course, _) = await SeedCourseWithLessonAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/courses/{course.Id}/reviews", new SaveCourseReviewRequest(5, "Great"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCategories_Anonymous_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/categories?scope=courses&lang=en");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
