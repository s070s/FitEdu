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
/// bUnit tests for the anonymous landing page: the hero, member stories, blog and closing call to
/// action render, and the career-era sections (problem, solution, method, founder story) are gone.
/// </summary>
public class HomeLandingTests : BunitContext
{
    private readonly ITestimonialConsumer _testimonialConsumer = Substitute.For<ITestimonialConsumer>();
    private readonly IBlogConsumer _blogConsumer = Substitute.For<IBlogConsumer>();

    public HomeLandingTests()
    {
        Services.AddSingleton(_testimonialConsumer);
        Services.AddSingleton(_blogConsumer);
        Services.AddSingleton(Substitute.For<ICourseConsumer>());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddBunitPersistentComponentState();
        AddAuthorization();

        IReadOnlyList<TestimonialDto> testimonials =
            [new(Guid.NewGuid(), "Eleni Papadopoulou", "Ran her first 5K", null, "I ran 5K without stopping.", null, 1)];
        IReadOnlyList<BlogArticleSummaryDto> articles =
            [new(Guid.NewGuid(), "Progressive overload explained", "progressive-overload-explained", "Add a little each week.",
                "/images/blog/progressive-overload-explained.webp", "FitEdu", ["Strength"], DateTimeOffset.UtcNow)];
        _testimonialConsumer.GetActiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(testimonials);
        _blogConsumer.GetSummariesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(articles);
    }

    [Fact]
    public void AnonymousLanding_RendersHeroStoriesBlogAndCallToAction()
    {
        var page = Render<Home>();

        page.Find(".hero-title").TextContent.ShouldContain(HomeRes.HeroTitleLine1);
        page.Find(".hero-cta .btn-cta").TextContent.ShouldContain(HomeRes.HeroCtaButton);
        page.Find(".testimonials-grid blockquote").TextContent.ShouldContain("I ran 5K without stopping.");
        page.FindAll(".blog-card").Count.ShouldBe(1);
        page.Find(".cta-title").TextContent.ShouldContain(HomeRes.CtaTitle);
    }

    [Theory]
    [InlineData(".section-problem")]
    [InlineData(".section-solution")]
    [InlineData(".section-method")]
    [InlineData(".state-card")]
    public void AnonymousLanding_HasNoCareerEraSections(string sectionSelector)
    {
        var page = Render<Home>();

        page.FindAll(sectionSelector).ShouldBeEmpty();
    }

    [Fact]
    public void AnonymousLanding_HasOnlyTheMemberStoriesQuoteCard()
    {
        var page = Render<Home>();

        // The founder-story card was the only quote card outside the testimonials grid.
        page.FindAll(".story-card").Count.ShouldBe(page.FindAll(".testimonials-grid .story-card").Count);
    }
}
