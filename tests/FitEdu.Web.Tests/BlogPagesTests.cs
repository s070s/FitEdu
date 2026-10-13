using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the blog index (magazine grid of the shared BlogCard, newest article featured)
/// and the article page (uploaded cover through the media API, reading time, not-found state).
/// </summary>
public class BlogPagesTests : BunitContext
{
    private const string Slug = "progressive-overload-explained";

    private readonly IBlogConsumer _blogConsumer = Substitute.For<IBlogConsumer>();

    public BlogPagesTests()
    {
        Services.AddSingleton(_blogConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddBunitPersistentComponentState();
    }

    [Fact]
    public void BlogIndex_RendersOneCardPerArticle_FirstOneFeatured()
    {
        IReadOnlyList<BlogArticleSummaryDto> articles = [Summary("First"), Summary("Second"), Summary("Third")];
        _blogConsumer.GetSummariesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(articles);

        var page = Render<Blog>();

        page.FindAll(".blog-grid .blog-card").Count.ShouldBe(3);
        var items = page.FindAll(".blog-grid__item");
        items[0].ClassList.ShouldContain("blog-grid__item--featured");
        items[1].ClassList.ShouldNotContain("blog-grid__item--featured");
    }

    [Fact]
    public void BlogIndex_NoArticles_ShowsEmptyState()
    {
        _blogConsumer.GetSummariesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<BlogArticleSummaryDto>?)[]);

        var page = Render<Blog>();

        page.Find(".empty-state__title").TextContent.ShouldBe(HomeRes.BlogIndexEmpty);
    }

    [Fact]
    public void BlogArticle_UploadedCover_UsesTheMediaApiUrl()
    {
        ReturnArticle(Article() with { CoverImageUrl = "blog/covers/a.jpg" });

        var page = RenderArticle();

        var cover = page.Find(".blog-article-cover img");
        cover.GetAttribute("src").ShouldBe("/api/media/blog/covers/a.jpg");
        cover.HasAttribute("width").ShouldBeTrue();
        cover.HasAttribute("height").ShouldBeTrue();
    }

    [Fact]
    public void BlogArticle_ShowsReadingTimeFromTheWordCount()
    {
        // 600 words at 200 words a minute.
        var content = "<p>" + string.Join(' ', Enumerable.Repeat("lift", 600)) + "</p>";
        ReturnArticle(Article() with { Content = content });

        var page = RenderArticle();

        page.Find(".blog-article-meta").TextContent.ShouldContain(HomeRes.BlogReadingTimeFormat.Replace("{0}", "3", StringComparison.Ordinal));
    }

    [Fact]
    public void BlogArticle_Missing_ShowsNotFoundWithLinkBackToTheBlog()
    {
        _blogConsumer.GetBySlugAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BlogArticleDto?)null);

        var page = RenderArticle();

        page.Find(".empty-state__title").TextContent.ShouldBe(HomeRes.BlogNotFoundTitle);
        page.Find(".empty-state__action").GetAttribute("href").ShouldBe("/blog");
    }

    private IRenderedComponent<BlogArticle> RenderArticle() =>
        Render<BlogArticle>(parameters => parameters.Add(component => component.Slug, Slug));

    private void ReturnArticle(BlogArticleDto article) =>
        _blogConsumer.GetBySlugAsync(Slug, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(article);

    private static BlogArticleSummaryDto Summary(string title) => new(
        Guid.NewGuid(), title, title.ToLowerInvariant(), "Add a little each week.",
        "/images/blog/progressive-overload-explained.webp", "FitEdu", ["Strength"], DateTimeOffset.UtcNow);

    private static BlogArticleDto Article() => new(
        Guid.NewGuid(), "Progressive overload explained", Slug, "Add a little each week.",
        "/images/blog/progressive-overload-explained.webp", "FitEdu", ["Strength"], DateTimeOffset.UtcNow,
        "<p>Short.</p>");
}
