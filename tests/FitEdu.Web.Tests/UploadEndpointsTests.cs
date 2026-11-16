using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Shared.Resources.Messages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// P05-13 / P05-15 / P05-17: every upload endpoint runs the one validator (declared type, size, first bytes) and names
/// the stored file by a GUID; a refusal is a 400 in the caller's language.
/// </summary>
public class UploadEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private const string GreekCulture = "el-GR";

    private readonly CustomWebAppFactory _factory;

    public UploadEndpointsTests(CustomWebAppFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Lesson Lesson)> AdminWithALessonAsync()
    {
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = Guid.NewGuid() };
        var module = new Module { Id = lesson.ModuleId, TitleEn = "M", CourseId = Guid.NewGuid() };
        module.Lessons.Add(lesson);
        var course = new Course { Id = module.CourseId, TitleEn = "C" };
        course.Modules.Add(module);
        await _factory.SeedAsync(async db =>
        {
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });
        return (await _factory.CreateAuthenticatedClientAsync("Admin"), lesson);
    }

    private static async Task<string?> DetailOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail;

    // ── Profile avatar ──

    [Fact]
    public async Task Avatar_ValidPng_IsStoredUnderAGuidName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");
        using var content = UploadSamples.Multipart(UploadSamples.Png(), "image/png", "My holiday photo ΦΩΤΟ.png");

        var response = await client.PostAsync("/api/profile/avatar", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AvatarUploadResultDto>();
        result!.AvatarPath.ShouldMatch("^avatars/[0-9a-f]{32}\\.png$");
        File.Exists(Path.Combine(_factory.UploadsPath, result.AvatarPath)).ShouldBeTrue();
    }

    [Fact]
    public async Task Avatar_SvgDeclaredAsPng_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");
        using var content = UploadSamples.Multipart(UploadSamples.Svg(), "image/png", "evil.png");

        (await client.PostAsync("/api/profile/avatar", content)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Avatar_OverFiveMegabytes_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");
        using var content = UploadSamples.Multipart(UploadSamples.PngOfLength(5 * 1024 * 1024 + 1), "image/png");

        (await client.PostAsync("/api/profile/avatar", content)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ── Testimonial avatar ──

    [Fact]
    public async Task TestimonialAvatar_SvgDeclaredAsPng_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        using var content = UploadSamples.Multipart(UploadSamples.Svg(), "image/png");

        var response = await client.PostAsync($"/api/admin/testimonials/{Guid.NewGuid()}/upload/avatar", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TestimonialAvatar_WrongType_AnswersInGreekWhenAsked()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(GreekCulture);
        using var content = UploadSamples.Multipart(UploadSamples.Png(), "text/plain");

        var response = await client.PostAsync($"/api/admin/testimonials/{Guid.NewGuid()}/upload/avatar", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailOf(response)).ShouldBe("Επιτρέπονται μόνο αρχεία εικόνας (jpeg, png, gif, webp).");
    }

    [Fact]
    public async Task TestimonialAvatar_WrongType_AnswersInEnglishByDefault()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        using var content = UploadSamples.Multipart(UploadSamples.Png(), "text/plain");

        var response = await client.PostAsync($"/api/admin/testimonials/{Guid.NewGuid()}/upload/avatar", content);

        (await DetailOf(response)).ShouldBe(ErrorMessagesRes.OnlyImageFilesAllowed);
    }

    // ── Blog cover (checked no type at all before) ──

    [Theory]
    [InlineData("text/plain")]
    [InlineData("image/svg+xml")]
    [InlineData("image/avif")]
    public async Task BlogCover_NotAnAllowedImageType_Returns400(string contentType)
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        using var content = UploadSamples.Multipart(UploadSamples.Png(), contentType);

        (await client.PostAsync($"/api/admin/blog/{Guid.NewGuid()}/upload/cover", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BlogCover_SvgDeclaredAsPng_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        using var content = UploadSamples.Multipart(UploadSamples.Svg(), "image/png");

        (await client.PostAsync($"/api/admin/blog/{Guid.NewGuid()}/upload/cover", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BlogCover_SevenMegabytePng_IsWithinTheCoverLimit()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        var articleId = Guid.NewGuid();
        await _factory.SeedAsync(async db =>
        {
            db.BlogArticles.Add(new BlogArticle
            {
                Id = articleId, TitleEn = "T", Slug = "cover-limit-" + articleId.ToString("N"), SummaryEn = "S",
                ContentEn = "C", AuthorName = "A"
            });
            await db.SaveChangesAsync();
        });
        using var content = UploadSamples.Multipart(UploadSamples.PngOfLength(7 * 1024 * 1024), "image/png");

        var response = await client.PostAsync($"/api/admin/blog/{articleId}/upload/cover", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BlogCoverUploadResultDto>();
        result!.CoverImageUrl.ShouldMatch("^blog/covers/[0-9a-f]{32}\\.png$");
    }

    // ── Site background (checked no type at all before) ──

    [Theory]
    [InlineData("text/plain")]
    [InlineData("image/svg+xml")]
    public async Task Background_NotAnAllowedImageType_Returns400(string contentType)
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        using var content = UploadSamples.Multipart(UploadSamples.Png(), contentType);

        (await client.PostAsync("/api/admin/site/background-image", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Background_SvgDeclaredAsPng_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        using var content = UploadSamples.Multipart(UploadSamples.Svg(), "image/png");

        (await client.PostAsync("/api/admin/site/background-image", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ── Lesson PDF and video ──

    [Fact]
    public async Task LessonPdf_HtmlDeclaredAsPdf_Returns400()
    {
        var (client, lesson) = await AdminWithALessonAsync();
        using var content = UploadSamples.Multipart("<html><script>alert(1)</script></html>"u8.ToArray(), "application/pdf");

        (await client.PostAsync($"/api/admin/lessons/{lesson.Id}/upload/pdf", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LessonPdf_ValidPdf_IsStoredUnderAGuidName()
    {
        var (client, lesson) = await AdminWithALessonAsync();
        using var content = UploadSamples.Multipart(UploadSamples.Pdf(), "application/pdf", "Άσκηση 1.pdf");

        var response = await client.PostAsync($"/api/admin/lessons/{lesson.Id}/upload/pdf", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LessonPdfUploadResultDto>();
        result!.PdfPath.ShouldMatch("^lessons/pdf/[0-9a-f]{32}\\.pdf$");
    }

    [Fact]
    public async Task LessonVideo_TextDeclaredAsMp4_Returns400()
    {
        var (client, lesson) = await AdminWithALessonAsync();
        using var content = UploadSamples.Multipart("not a video at all, just text"u8.ToArray(), "video/mp4");

        (await client.PostAsync($"/api/admin/lessons/{lesson.Id}/upload/video", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LessonVideo_WrongType_AnswersInGreekWhenAsked()
    {
        var (client, lesson) = await AdminWithALessonAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(GreekCulture);
        using var content = UploadSamples.Multipart(UploadSamples.Mp4(), "application/pdf");

        var response = await client.PostAsync($"/api/admin/lessons/{lesson.Id}/upload/video", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailOf(response)).ShouldBe("Επιτρέπονται μόνο αρχεία βίντεο (mp4, webm, ogg).");
    }

    [Fact]
    public async Task LessonVideo_ValidMp4_IsStoredWithTheMp4Extension()
    {
        var (client, lesson) = await AdminWithALessonAsync();
        using var content = UploadSamples.Multipart(UploadSamples.Mp4(), "video/mp4", "clip.MOV");

        var response = await client.PostAsync($"/api/admin/lessons/{lesson.Id}/upload/video", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LessonVideoUploadResultDto>();
        Regex.IsMatch(result!.VideoPath, "^lessons/video/[0-9a-f]{32}\\.mp4$").ShouldBeTrue();
    }
}
