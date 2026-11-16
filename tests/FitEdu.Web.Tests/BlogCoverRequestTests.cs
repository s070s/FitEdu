using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// P05-10: the site's policy only loads images from the site itself, so a blog cover must be a file the site has;
/// a full or protocol-relative URL is refused in the admin's language instead of saving a cover that never shows.
/// </summary>
public class BlogCoverRequestTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public BlogCoverRequestTests(CustomWebAppFactory factory) => _factory = factory;

    private static SaveBlogArticleRequest Article(string? cover) => new(
        "Title", null, "cover-" + Guid.NewGuid().ToString("N"), "Summary", null, "<p>Body</p>", null, cover, "Author", null, false);

    private static async Task<List<string>> ErrorsOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Values.SelectMany(messages => messages).ToList();

    [Theory]
    [InlineData("https://example.com/cover.png")]
    [InlineData("http://example.com/cover.png")]
    [InlineData("//cdn.example.com/cover.png")]
    [InlineData(" //host/x.png")]
    [InlineData("/\\host/x.png")]
    [InlineData("blog/covers/x.png?next=https://example.com")]
    public async Task Create_CoverThatIsNotASiteFile_Returns400WithTheMessage(string cover)
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PostAsJsonAsync("/api/admin/blog", Article(cover));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorsOf(response)).ShouldContain(ErrorMessagesRes.CoverImageMustBeSiteFile);
    }

    [Fact]
    public async Task Update_CoverThatIsAFullUrl_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PutAsJsonAsync($"/api/admin/blog/{Guid.NewGuid()}", Article("https://example.com/cover.png"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_CoverRefusal_IsInGreekWhenAsked()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("el-GR");

        var response = await client.PostAsJsonAsync("/api/admin/blog", Article("https://example.com/cover.png"));

        (await ErrorsOf(response)).ShouldContain("Η εικόνα εξωφύλλου πρέπει να ανέβει στον ιστότοπο.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("blog/covers/0123456789abcdef0123456789abcdef.png")]
    [InlineData("/images/blog/progressive-overload.webp")]
    public async Task Create_CoverThatIsAStoredOrShippedFileOrNone_IsAccepted(string? cover)
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PostAsJsonAsync("/api/admin/blog", Article(cover));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
