using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class MediaAndAnalyticsIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public MediaAndAnalyticsIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Media_DisallowedFolder_Returns404()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/media/secret/file.png")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Media_AllowedFolderMissingFile_Returns404()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/media/blog/covers/missing.png")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Media_DisallowedExtension_Returns404()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/media/blog/covers/file.txt")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Media_ServedFile_CarriesSandboxingCsp()
    {
        // Write a file through the same storage the controller reads from, into a public folder.
        string relPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            using var ms = new MemoryStream([0x89, 0x50, 0x4E, 0x47]); // "‰PNG" header bytes — content is irrelevant
            relPath = await storage.SaveFileAsync(ms, "csp-probe.png", "blog/covers");
        }

        try
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync($"/api/media/{relPath}");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            response.Headers.GetValues("Content-Security-Policy").Single()
                .ShouldBe("default-src 'none'; style-src 'unsafe-inline'; sandbox");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await storage.DeleteFileAsync(relPath);
        }
    }

    [Fact]
    public async Task Analytics_Admin_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        (await client.GetAsync("/api/admin/analytics/summary")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Analytics_CourseStats_ListTheMostEnrolledCourseFirst()
    {
        var quietCourse = new Course { Id = Guid.NewGuid(), TitleEn = "Analytics quiet course", IsPublished = true };
        var popularCourse = new Course { Id = Guid.NewGuid(), TitleEn = "Analytics popular course", IsPublished = true };
        await _factory.SeedAsync(async db =>
        {
            db.Courses.AddRange(quietCourse, popularCourse);
            db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = "analytics-a", CourseId = quietCourse.Id });
            for (var member = 0; member < 3; member++)
                db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = $"analytics-b{member}", CourseId = popularCourse.Id });
            await db.SaveChangesAsync();
        });
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var summary = await client.GetFromJsonAsync<AnalyticsSummaryDto>("/api/admin/analytics/summary");

        var enrolmentCounts = summary!.CourseStats.Select(stat => stat.EnrollmentCount).ToList();
        enrolmentCounts.ShouldBe(enrolmentCounts.OrderByDescending(count => count).ToList());
        var courseIds = summary.CourseStats.Select(stat => stat.CourseId).ToList();
        courseIds.IndexOf(popularCourse.Id).ShouldBeLessThan(courseIds.IndexOf(quietCourse.Id));
    }

    [Fact]
    public async Task Analytics_Student_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        (await client.GetAsync("/api/admin/analytics/summary")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
