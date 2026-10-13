using System.Net;
using System.Net.Http.Json;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class CoursesIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CoursesIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetCourses_Student_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        (await client.GetAsync("/api/courses")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCourses_GoalLevelAndEquipmentFilters_ReturnOnlyMatchingCourses()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");
        var equipment = $"gear{Guid.NewGuid():N}";
        var matching = new Course
        {
            Id = Guid.NewGuid(), TitleEn = "Match", IsPublished = true,
            Goal = FitnessGoal.Mobility, Level = CourseLevel.Advanced, Equipment = $"mat,{equipment}"
        };
        var wrongGoal = new Course
        {
            Id = Guid.NewGuid(), TitleEn = "Wrong goal", IsPublished = true,
            Goal = FitnessGoal.Strength, Level = CourseLevel.Advanced, Equipment = equipment
        };
        await _factory.SeedAsync(async db =>
        {
            db.Courses.AddRange(matching, wrongGoal);
            await db.SaveChangesAsync();
        });

        var result = await client.GetFromJsonAsync<PagedResult<CourseListItemDto>>(
            $"/api/courses?goal=Mobility&level=Advanced&equipment={equipment}");

        result!.Items.ShouldHaveSingleItem().Id.ShouldBe(matching.Id);
    }

    [Fact]
    public async Task GetCourses_UnknownGoal_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.GetAsync("/api/courses?goal=Flying");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Enroll_AsAdmin_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PostAsync($"/api/courses/{Guid.NewGuid()}/enroll", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
