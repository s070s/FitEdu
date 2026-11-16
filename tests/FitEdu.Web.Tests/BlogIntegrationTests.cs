using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class BlogIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public BlogIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Summaries_Anonymous_Returns200()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/blog/summaries")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static SaveBlogArticleRequest Article(string slug) => new(
        "Title", null, slug, "Summary", null, "<p>Body</p>", null, null, "Author", null, false);

    [Theory]
    [InlineData("en", "An article with this slug already exists.")]
    [InlineData("el-GR", "Υπάρχει ήδη άρθρο με αυτό το slug.")]
    public async Task Create_SlugAlreadyUsed_Returns409InTheCallersLanguage(string language, string expected)
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var slug = "taken-" + Guid.NewGuid().ToString("N");
        (await client.PostAsJsonAsync("/api/admin/blog", Article(slug))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await client.PostAsJsonAsync("/api/admin/blog", Article(slug));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(expected);
    }

    [Fact]
    public async Task Update_SlugUsedByAnotherArticle_Returns409WithTheMessage()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        var takenSlug = "taken-" + Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/admin/blog", Article(takenSlug));
        var other = await (await client.PostAsJsonAsync("/api/admin/blog", Article("other-" + Guid.NewGuid().ToString("N"))))
            .Content.ReadFromJsonAsync<AdminBlogArticleDto>();

        var response = await client.PutAsJsonAsync($"/api/admin/blog/{other!.Id}", Article(takenSlug));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(ErrorMessagesRes.BlogSlugTaken);
    }

    [Fact]
    public async Task GetBySlug_Unknown_Returns404()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/blog/this-slug-does-not-exist")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
