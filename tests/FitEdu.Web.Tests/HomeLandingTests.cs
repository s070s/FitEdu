using System.Globalization;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the anonymous landing page: the hero photo, the numbers strip, the goal cards,
/// how it works, the featured courses, member stories, blog and the closing call to action; and the
/// career-era sections and inline SVG icons are gone.
/// </summary>
public class HomeLandingTests : BunitContext
{
    private const string HomeRazorPath = "src/FitEdu.Web/Pages/Home.razor";

    private readonly ITestimonialConsumer _testimonialConsumer = Substitute.For<ITestimonialConsumer>();
    private readonly IBlogConsumer _blogConsumer = Substitute.For<IBlogConsumer>();
    private readonly ILandingConsumer _landingConsumer = Substitute.For<ILandingConsumer>();

    public HomeLandingTests()
    {
        Services.AddSingleton(_testimonialConsumer);
        Services.AddSingleton(_blogConsumer);
        Services.AddSingleton(_landingConsumer);
        Services.AddSingleton(Substitute.For<ICourseConsumer>());
        Services.AddSingleton(Substitute.For<IProfileConsumer>());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddBunitPersistentComponentState();
        AddAuthorization();
        UseCulture("en");

        IReadOnlyList<TestimonialDto> testimonials =
            [new(Guid.NewGuid(), "Eleni Papadopoulou", "Ran her first 5K", null, "I ran 5K without stopping.", null, 1)];
        IReadOnlyList<BlogArticleSummaryDto> articles =
            [new(Guid.NewGuid(), "Progressive overload explained", "progressive-overload-explained", "Add a little each week.",
                "/images/blog/progressive-overload-explained.webp", "FitEdu", ["Strength"], DateTimeOffset.UtcNow)];
        _testimonialConsumer.GetActiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(testimonials);
        _blogConsumer.GetSummariesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(articles);
        _landingConsumer.GetOverviewAsync(Arg.Any<string>()).Returns(Overview());
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

    [Fact]
    public void Hero_IsAnEagerPhotoWithStartFreeAndBrowseExercisesLinks()
    {
        var page = Render<Home>();

        var photo = page.Find(".hero-section img");
        photo.GetAttribute("src").ShouldBe("/images/hero-home.webp");
        photo.GetAttribute("fetchpriority").ShouldBe("high");
        photo.HasAttribute("loading").ShouldBeFalse();
        photo.GetAttribute("width").ShouldNotBeNullOrEmpty();
        photo.GetAttribute("height").ShouldNotBeNullOrEmpty();
        page.FindAll(".hero-cta a").Select(link => link.GetAttribute("href")).ShouldBe(["/register", "/exercises"]);
    }

    [Fact]
    public void Hero_UsesTheAdminPhotoWhenOneIsSet()
    {
        _landingConsumer.GetOverviewAsync(Arg.Any<string>()).Returns(Overview(hasCustomHero: true));

        var page = Render<Home>();

        page.Find(".hero-section img").GetAttribute("src").ShouldBe("/api/site/background-image");
    }

    [Fact]
    public void StatsStrip_ShowsTheFourNumbersInThePageLanguage()
    {
        UseCulture("el");

        var page = Render<Home>();

        var values = page.FindAll(".stats-strip .stat-tile__value").Select(value => value.TextContent).ToList();
        values.ShouldBe(["19", "873", "131", "1.240"]);
    }

    [Fact]
    public void StatsStrip_IsHiddenWhenTheNumbersAreUnavailable()
    {
        _landingConsumer.GetOverviewAsync(Arg.Any<string>()).Returns((LandingOverviewDto?)null);

        var page = Render<Home>();

        page.FindAll(".stats-strip").ShouldBeEmpty();
        page.FindAll(".section-featured").ShouldBeEmpty();
        page.Find(".hero-section img").GetAttribute("src").ShouldBe("/images/hero-home.webp");
    }

    [Fact]
    public void GoalCards_LinkToTheCatalogFilteredByEachGoal()
    {
        var page = Render<Home>();

        var links = page.FindAll(".goal-card").Select(card => card.GetAttribute("href")).ToList();
        links.Count.ShouldBe(Enum.GetValues<FitnessGoal>().Length);
        links.ShouldContain("/courses?goal=FatLoss");
        page.FindAll(".goal-card__label").Select(label => label.TextContent).ShouldContain(CourseRes.Goal_FatLoss);
    }

    [Fact]
    public void HowItWorks_HasThreeSteps()
    {
        var page = Render<Home>();

        page.FindAll(".how-step").Count.ShouldBe(3);
        page.Find(".how-step__title").TextContent.ShouldBe(HomeRes.HowStep1Title);
    }

    [Fact]
    public void FeaturedCourses_RenderAsCourseCards()
    {
        var page = Render<Home>();

        page.FindAll(".section-featured .course-card").Count.ShouldBe(2);
        page.Find(".section-featured a.course-card-link").TextContent.ShouldBe("Strength basics");
    }

    [Fact]
    public void NoTestimonials_HidesTheStoriesSection()
    {
        _testimonialConsumer.GetActiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<Home>();

        page.FindAll(".section-story").ShouldBeEmpty();
    }

    [Fact]
    public void GreekPage_AsksEveryApiForGreekContent()
    {
        UseCulture("el");

        Render<Home>();

        _testimonialConsumer.Received().GetActiveAsync("el", Arg.Any<CancellationToken>());
        _blogConsumer.Received().GetSummariesAsync(Arg.Any<int>(), "el", Arg.Any<CancellationToken>());
        _landingConsumer.Received().GetOverviewAsync("el");
    }

    [Theory]
    [InlineData(".section-problem")]
    [InlineData(".section-solution")]
    [InlineData(".section-method")]
    [InlineData(".state-card")]
    [InlineData(".story-card")]
    public void AnonymousLanding_HasNoCareerEraSections(string sectionSelector)
    {
        var page = Render<Home>();

        page.FindAll(sectionSelector).ShouldBeEmpty();
    }

    [Fact]
    public void HomeMarkup_UsesIconFontGlyphsNotInlineSvg()
    {
        var markup = File.ReadAllText(Path.Combine(FitEdu.TestSupport.RepoPaths.RepoRoot, HomeRazorPath));

        markup.ShouldNotContain("<svg");
        markup.ShouldNotContain("onerror=");
    }

    private static LandingOverviewDto Overview(bool hasCustomHero = false) => new(
        CourseCount: 19,
        LessonCount: 131,
        ExerciseCount: 873,
        MemberCount: 1240,
        FeaturedCourses:
        [
            new(Guid.NewGuid(), "Strength basics", "Build a base.", false, 12, Level: CourseLevel.Beginner),
            new(Guid.NewGuid(), "Mobility flow", null, false, 8, Level: CourseLevel.Intermediate)
        ],
        HasCustomHeroImage: hasCustomHero);

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }
}
