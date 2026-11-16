using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The public media endpoint and the site background: stored files are served with validators, ranges and caching that
/// fit their names (P05-18), and a file stored under an old Greek name still serves (P05-2).
/// </summary>
public class MediaServingTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public MediaServingTests(CustomWebAppFactory factory) => _factory = factory;

    private async Task<string> StoreAsync(byte[] bytes, string contentType, string folder)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        using var stream = new MemoryStream(bytes);
        return await storage.SaveFileAsync(stream, contentType, folder);
    }

    [Fact]
    public async Task Media_StoredFile_IsCachedForAYear_WithValidators_AndNoContentDisposition()
    {
        var path = await StoreAsync(UploadSamples.Png(), "image/png", "blog/covers");

        var response = await _factory.CreateClient().GetAsync($"/api/media/{path}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(UploadSamples.Png());
        response.Headers.GetValues("Cache-Control").ShouldBe(["public, max-age=31536000, immutable"]);
        response.Headers.ETag.ShouldNotBeNull();
        response.Content.Headers.LastModified.ShouldNotBeNull();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Content.Headers.ContentDisposition.ShouldBeNull();
    }

    [Fact]
    public async Task Media_IfNoneMatchWithTheETag_Returns304()
    {
        var path = await StoreAsync(UploadSamples.Png(), "image/png", "testimonials/avatars");
        var client = _factory.CreateClient();
        var first = await client.GetAsync($"/api/media/{path}");
        using var repeat = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{path}");
        repeat.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        var response = await client.SendAsync(repeat);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Media_RangeRequest_Returns206()
    {
        var path = await StoreAsync(UploadSamples.Png(), "image/png", "blog/covers");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{path}");
        request.Headers.Range = new RangeHeaderValue(0, 7);

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(UploadSamples.Png()[..8]);
    }

    [Fact]
    public async Task Media_FileStoredUnderAnOldGreekName_Returns200()
    {
        // Before the GUID names, a stored file kept the name the client gave it. A Greek name in Content-Disposition
        // made Kestrel fail the response; with no such header the file serves.
        const string oldName = "φωτογραφία_0123456789abcdef0123456789abcdef.JPG";
        var folder = Path.Combine(_factory.UploadsPath, "blog", "covers");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, oldName), [0xFF, 0xD8, 0xFF, 0xE0]);

        var response = await _factory.CreateClient().GetAsync($"/api/media/blog/covers/{Uri.EscapeDataString(oldName)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/jpeg");
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("avif")]
    public async Task Media_SvgAndAvifAreNotServed(string extension)
    {
        var folder = Path.Combine(_factory.UploadsPath, "blog", "covers");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, $"old.{extension}"), "<svg/>");

        var response = await _factory.CreateClient().GetAsync($"/api/media/blog/covers/old.{extension}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Media_InvalidPath_AnswersInGreekWhenAsked()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("el-GR");

        var response = await client.GetAsync("/api/media/blog/covers/a..b.png");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe("Μη έγκυρη διαδρομή αρχείου.");
    }

    [Fact]
    public async Task Background_IsCachedBriefly_WithValidators_AndAnswers304()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        using var content = UploadSamples.Multipart(UploadSamples.Png(), "image/png", "bg.png");
        var upload = await admin.PostAsync("/api/admin/site/background-image", content);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await upload.Content.ReadFromJsonAsync<BackgroundImageUploadResultDto>())!
            .BackgroundImagePath.ShouldMatch("^backgrounds/[0-9a-f]{32}\\.png$");
        var client = _factory.CreateClient();

        var first = await client.GetAsync("/api/site/background-image");
        using var repeat = new HttpRequestMessage(HttpMethod.Get, "/api/site/background-image");
        repeat.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(repeat);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.GetValues("Cache-Control").ShouldBe(["public, max-age=300"]);
        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }
}
