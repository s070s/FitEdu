using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class LessonAssetsIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public LessonAssetsIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    private async Task<string> MintAssetTokenAsync(string userId, Guid lessonId)
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        return await authService.CreateLessonAssetTokenAsync(principal, lessonId);
    }

    private static (Course course, Lesson lesson) BuildCourseWithLesson(
        string? pdfPath = null, string? videoPath = null, bool published = true)
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C", IsPublished = published };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = module.Id, PdfPath = pdfPath, VideoPath = videoPath };
        module.Lessons.Add(lesson);
        course.Modules.Add(module);
        return (course, lesson);
    }

    /// <summary>Saves a file through the storage the host reads from (the factory's own uploads folder).</summary>
    private async Task<string> StoreFileAsync(byte[] bytes, string contentType, string folder)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        using var stream = new MemoryStream(bytes);
        return await storage.SaveFileAsync(stream, contentType, folder);
    }

    /// <summary>A student enrolled in a published course whose one lesson has the given stored files.</summary>
    private async Task<(HttpClient Client, Lesson Lesson, string UserId)> EnrolledStudentAsync(
        string? pdfPath = null, string? videoPath = null, bool published = true)
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        var (course, lesson) = BuildCourseWithLesson(pdfPath, videoPath, published);
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = userId, CourseId = course.Id });
            await db.SaveChangesAsync();
        });
        return (client, lesson, userId);
    }

    // P05-1: the lesson viewer shows the PDF in an iframe of this site, so only the PDF response may be framed, and
    // only by the same origin; every other response keeps DENY and frame-ancestors 'none'.

    [Fact]
    public async Task PdfAsset_MayBeFramedBySameOriginOnly()
    {
        var pdfPath = await StoreFileAsync(UploadSamples.Pdf(), "application/pdf", "lessons/pdf");
        var (client, lesson, _) = await EnrolledStudentAsync(pdfPath: pdfPath);

        var response = await client.GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["SAMEORIGIN"]);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe(["frame-ancestors 'self'"]);
    }

    [Fact]
    public async Task VideoAsset_KeepsDenyAndFrameAncestorsNone()
    {
        var videoPath = await StoreFileAsync(UploadSamples.Mp4(), "video/mp4", "lessons/video");
        var (client, lesson, _) = await EnrolledStudentAsync(videoPath: videoPath);

        var response = await client.GetAsync($"/api/lessons/{lesson.Id}/asset?type=video");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task OtherApiResponse_KeepsDenyAndFrameAncestorsNone()
    {
        var response = await _factory.CreateClient().GetAsync("/api/categories");

        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Asset_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync($"/api/lessons/{Guid.NewGuid()}/asset?type=pdf"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Asset_LessonMissing_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        (await client.GetAsync($"/api/lessons/{Guid.NewGuid()}/asset?type=pdf"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Asset_NotEnrolled_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");
        var (course, lesson) = BuildCourseWithLesson(pdfPath: "lessons/pdf/x.pdf");
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });

        (await client.GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Asset_EnrolledButNoPdf_Returns404()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        var (course, lesson) = BuildCourseWithLesson(pdfPath: null);
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = userId, CourseId = course.Id });
            await db.SaveChangesAsync();
        });

        (await client.GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Asset_EnrolledInvalidType_Returns404()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        var (course, lesson) = BuildCourseWithLesson(pdfPath: "lessons/pdf/x.pdf");
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = userId, CourseId = course.Id });
            await db.SaveChangesAsync();
        });

        (await client.GetAsync($"/api/lessons/{lesson.Id}/asset?type=bogus"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Asset_ValidAssetToken_NoAuthHeader_Returns200WithTheFile()
    {
        // SEC-2: the browser <video>/<iframe> case: no Authorization header at all, just the short-lived,
        // single-lesson-scoped assetToken LessonViewer mints via IAuthService.
        var pdfPath = await StoreFileAsync(UploadSamples.Pdf(), "application/pdf", "lessons/pdf");
        var (_, lesson, userId) = await EnrolledStudentAsync(pdfPath: pdfPath);
        var token = await MintAssetTokenAsync(userId, lesson.Id);

        var response = await _factory.CreateClient()
            .GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf&assetToken={Uri.EscapeDataString(token)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(UploadSamples.Pdf());
    }

    [Fact]
    public async Task Asset_ValidAssetToken_FileNotOnDisk_Returns404()
    {
        var userId = Guid.NewGuid().ToString("N");
        var (course, lesson) = BuildCourseWithLesson(pdfPath: "lessons/pdf/not-there.pdf");
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = userId, CourseId = course.Id });
            await db.SaveChangesAsync();
        });
        var token = await MintAssetTokenAsync(userId, lesson.Id);

        var response = await _factory.CreateClient()
            .GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf&assetToken={Uri.EscapeDataString(token)}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private string MintTokenThatExpiredAt(string userId, Guid lessonId, DateTimeOffset expiration)
    {
        using var scope = _factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(AuthService.LessonAssetProtectorPurpose)
            .ToTimeLimitedDataProtector();
        return protector.Protect(JsonSerializer.Serialize(new LessonAssetTicket(userId, lessonId)), expiration);
    }

    [Fact]
    public async Task Asset_ExpiredAssetToken_Returns401()
    {
        var pdfPath = await StoreFileAsync(UploadSamples.Pdf(), "application/pdf", "lessons/pdf");
        var (_, lesson, userId) = await EnrolledStudentAsync(pdfPath: pdfPath);
        var expired = MintTokenThatExpiredAt(userId, lesson.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await _factory.CreateClient()
            .GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf&assetToken={Uri.EscapeDataString(expired)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Asset_TamperedAssetToken_Returns401()
    {
        var pdfPath = await StoreFileAsync(UploadSamples.Pdf(), "application/pdf", "lessons/pdf");
        var (_, lesson, userId) = await EnrolledStudentAsync(pdfPath: pdfPath);
        var token = await MintAssetTokenAsync(userId, lesson.Id);
        var middle = token.Length / 2;
        var tampered = token[..middle] + (token[middle] == 'A' ? 'B' : 'A') + token[(middle + 1)..];

        var response = await _factory.CreateClient()
            .GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf&assetToken={Uri.EscapeDataString(tampered)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Asset_EnrolledMember_GetsTheBytesWithValidatorsAndPrivateCaching()
    {
        var pdfPath = await StoreFileAsync(UploadSamples.Pdf(), "application/pdf", "lessons/pdf");
        var (client, lesson, _) = await EnrolledStudentAsync(pdfPath: pdfPath);

        var response = await client.GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(UploadSamples.Pdf());
        response.Headers.ETag.ShouldNotBeNull();
        response.Content.Headers.LastModified.ShouldNotBeNull();
        response.Headers.AcceptRanges.ShouldContain("bytes");
        response.Headers.CacheControl!.Private.ShouldBeTrue();
        response.Headers.CacheControl.NoCache.ShouldBeTrue();
    }

    [Fact]
    public async Task Asset_RangeRequest_Returns206WithThePartialBody()
    {
        var videoPath = await StoreFileAsync(UploadSamples.Mp4(), "video/mp4", "lessons/video");
        var (client, lesson, _) = await EnrolledStudentAsync(videoPath: videoPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/lessons/{lesson.Id}/asset?type=video");
        request.Headers.Range = new RangeHeaderValue(4, 7);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange!.From.ShouldBe(4);
        response.Content.Headers.ContentRange.To.ShouldBe(7);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(UploadSamples.Mp4()[4..8]);
    }

    [Fact]
    public async Task Asset_IfNoneMatchWithTheETag_Returns304()
    {
        var pdfPath = await StoreFileAsync(UploadSamples.Pdf(), "application/pdf", "lessons/pdf");
        var (client, lesson, _) = await EnrolledStudentAsync(pdfPath: pdfPath);
        var url = $"/api/lessons/{lesson.Id}/asset?type=pdf";
        var first = await client.GetAsync(url);
        using var repeat = new HttpRequestMessage(HttpMethod.Get, url);
        repeat.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        var response = await client.SendAsync(repeat);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
    }

    // P05-16: the lessons of an unpublished programme are not there for members, so neither are their files.
    [Fact]
    public async Task Asset_UnpublishedCourse_Returns404EvenForAnEnrolledMember()
    {
        var pdfPath = await StoreFileAsync(UploadSamples.Pdf(), "application/pdf", "lessons/pdf");
        var (client, lesson, _) = await EnrolledStudentAsync(pdfPath: pdfPath, published: false);

        var response = await client.GetAsync($"/api/lessons/{lesson.Id}/asset?type=pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // P05-17: the refusal is in the language the caller asked for.
    [Fact]
    public async Task Asset_Anonymous_AnswersInGreekWhenAsked()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("el-GR");

        var response = await client.GetAsync($"/api/lessons/{Guid.NewGuid()}/asset?type=pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe("Δεν έχεις συνδεθεί.");
    }

    [Fact]
    public async Task Asset_AssetTokenForDifferentLesson_Returns401()
    {
        var userId = Guid.NewGuid().ToString("N");
        var (course, lesson) = BuildCourseWithLesson(pdfPath: "lessons/pdf/x.pdf");
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = userId, CourseId = course.Id });
            await db.SaveChangesAsync();
        });
        var tokenForOtherLesson = await MintAssetTokenAsync(userId, Guid.NewGuid());

        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/lessons/{lesson.Id}/asset?type=pdf&assetToken={Uri.EscapeDataString(tokenForOtherLesson)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Asset_GarbageAssetToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/lessons/{Guid.NewGuid()}/asset?type=pdf&assetToken=garbage");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
