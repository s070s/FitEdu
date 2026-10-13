using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using FitEdu.Web.Shared.Components.Layout;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The search and social tags each public page writes into the document head: absolute URLs on
/// the configured site (the same base the sitemap uses), a fitness description and the share
/// image by default, and a canonical link on the paths and pricing pages.
/// </summary>
public class SeoHeadTests : BunitContext
{
    private const string SiteBaseUrl = "https://tests.local";

    private readonly IPathConsumer _pathConsumer = Substitute.For<IPathConsumer>();
    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();

    public SeoHeadTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // HeadOutlet reads the current title through JS
        Services.AddSingleton(_pathConsumer);
        Services.AddSingleton(_subscriptionConsumer);
        AddAuthorization();
        _pathConsumer.GetPathsAsync(Arg.Any<string>()).Returns([]);
        _subscriptionConsumer.GetPlansAsync(Arg.Any<string>()).Returns([]);
    }

    private void UseSiteBaseUrl(string? baseUrl)
    {
        var settings = new Dictionary<string, string?> { ["Sitemap:BaseUrl"] = baseUrl };
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    /// <summary>Renders the head outlet first, as App.razor does, so the page's head content lands in it.</summary>
    private IRenderedComponent<HeadOutlet> RenderHeadOutlet() => Render<HeadOutlet>();

    private static string Attribute(IRenderedComponent<HeadOutlet> head, string selector, string attribute) =>
        head.Find(selector).GetAttribute(attribute) ?? string.Empty;

    [Fact]
    public void SitePaths_BecomeAbsoluteUrls_AndTheDefaultsFillTheGaps()
    {
        UseSiteBaseUrl(SiteBaseUrl + "/");
        var head = RenderHeadOutlet();

        Render<SeoHead>(parameters => parameters
            .Add(seo => seo.Title, "Training paths")
            .Add(seo => seo.CanonicalUrl, "/paths"));

        Attribute(head, "link[rel=canonical]", "href").ShouldBe("https://tests.local/paths");
        Attribute(head, "meta[property='og:url']", "content").ShouldBe("https://tests.local/paths");
        Attribute(head, "meta[property='og:image']", "content").ShouldBe("https://tests.local/images/og-home.jpg");
        Attribute(head, "meta[name=description]", "content").ShouldBe(HomeRes.HeroSubtitle);
        Attribute(head, "meta[property='og:site_name']", "content").ShouldBe("FitEdu");
        Attribute(head, "meta[name='twitter:card']", "content").ShouldBe("summary_large_image");
    }

    [Fact]
    public void AbsoluteUrls_AreKeptAsTheyAre()
    {
        UseSiteBaseUrl(SiteBaseUrl);
        var head = RenderHeadOutlet();

        Render<SeoHead>(parameters => parameters
            .Add(seo => seo.Title, "Article")
            .Add(seo => seo.Description, "About squats.")
            .Add(seo => seo.CanonicalUrl, "https://fitedu.com/blog/squats")
            .Add(seo => seo.ImageUrl, "https://images.example/squat.webp"));

        Attribute(head, "link[rel=canonical]", "href").ShouldBe("https://fitedu.com/blog/squats");
        Attribute(head, "meta[property='og:image']", "content").ShouldBe("https://images.example/squat.webp");
        Attribute(head, "meta[name=description]", "content").ShouldBe("About squats.");
    }

    [Fact]
    public void NoConfiguredSite_FallsBackToTheRequestBaseAddress()
    {
        UseSiteBaseUrl(null);
        var head = RenderHeadOutlet();

        Render<SeoHead>(parameters => parameters
            .Add(seo => seo.Title, "Pricing")
            .Add(seo => seo.CanonicalUrl, "/pricing"));

        Attribute(head, "link[rel=canonical]", "href").ShouldBe("http://localhost/pricing");
    }

    [Fact]
    public void PathsPage_WritesItsCanonicalUrl()
    {
        UseSiteBaseUrl(SiteBaseUrl);
        var head = RenderHeadOutlet();

        Render<Paths>();

        Attribute(head, "link[rel=canonical]", "href").ShouldBe("https://tests.local/paths");
        Attribute(head, "meta[property='og:title']", "content").ShouldBe(PathRes.PathsTitle);
    }

    [Fact]
    public void PathDetailPage_WritesThePathCanonicalUrlTitleAndDescription()
    {
        UseSiteBaseUrl(SiteBaseUrl);
        var pathId = Guid.NewGuid();
        _pathConsumer.GetPathAsync(pathId, Arg.Any<string>())
            .Returns(new LearningPathDetailDto(pathId, "Beginner Start", "Four courses to build the habit.", null, null, []));
        var head = RenderHeadOutlet();

        Render<PathDetail>(parameters => parameters.Add(page => page.Id, pathId));

        Attribute(head, "link[rel=canonical]", "href").ShouldBe($"https://tests.local/paths/{pathId}");
        Attribute(head, "meta[property='og:title']", "content").ShouldBe("Beginner Start");
        Attribute(head, "meta[name=description]", "content").ShouldBe("Four courses to build the habit.");
    }

    [Fact]
    public void PricingPage_WritesItsCanonicalUrlAndSubtitle()
    {
        UseSiteBaseUrl(SiteBaseUrl);
        var head = RenderHeadOutlet();

        Render<Pricing>();

        Attribute(head, "link[rel=canonical]", "href").ShouldBe("https://tests.local/pricing");
        Attribute(head, "meta[name=description]", "content").ShouldBe(PricingRes.Subtitle);
    }

    [Fact]
    public void HomePage_SharesTheSiteRootAndTheHomeImage()
    {
        UseSiteBaseUrl(SiteBaseUrl);
        Services.AddSingleton(Substitute.For<ITestimonialConsumer>());
        Services.AddSingleton(Substitute.For<IBlogConsumer>());
        Services.AddSingleton(Substitute.For<ICourseConsumer>());
        Services.AddSingleton(Substitute.For<ILandingConsumer>());
        Services.AddSingleton(Substitute.For<IProfileConsumer>());
        AddBunitPersistentComponentState();
        var head = RenderHeadOutlet();

        Render<Home>();

        Attribute(head, "link[rel=canonical]", "href").ShouldBe("https://tests.local/");
        Attribute(head, "meta[property='og:image']", "content").ShouldBe("https://tests.local/images/og-home.jpg");
        // A guest lands on the hero, so its photo is preloaded.
        Attribute(head, "link[rel=preload][as=image]", "href").ShouldBe("/images/hero-home.webp");
    }

    [Fact]
    public void PreloadImage_IsAddedOnlyWhenAPageAsksForOne()
    {
        UseSiteBaseUrl(SiteBaseUrl);
        var head = RenderHeadOutlet();

        var seo = Render<SeoHead>(parameters => parameters
            .Add(component => component.Title, "Pricing")
            .Add(component => component.CanonicalUrl, "/pricing"));
        head.FindAll("link[rel=preload]").ShouldBeEmpty();

        seo.Render(parameters => parameters.Add(component => component.PreloadImageUrl, "images/hero-home.webp"));

        var preload = head.Find("link[rel=preload]");
        preload.GetAttribute("as").ShouldBe("image");
        preload.GetAttribute("href").ShouldBe("images/hero-home.webp");
        preload.GetAttribute("fetchpriority").ShouldBe("high");
    }
}
