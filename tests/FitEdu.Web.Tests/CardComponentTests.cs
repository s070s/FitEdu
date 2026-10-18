using System.Globalization;
using Bunit;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.TestSupport;
using FitEdu.Web.Shared.Components.Cards;
using FitEdu.Web.Shared.Components.Layout;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using WebErrorEventArgs = Microsoft.AspNetCore.Components.Web.ErrorEventArgs;

namespace FitEdu.Web.Tests;

/// <summary>
/// The shared cards (course, blog, testimonial, path) and the empty state that the redesigned pages
/// use instead of their own copies. Covers links, Greek labels and the fallbacks for missing data.
/// </summary>
public class CardComponentTests : BunitContext
{
    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private static readonly Guid CourseId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    public CardComponentTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
    }

    // ---- CourseCard ---------------------------------------------------------------------------

    [Fact]
    public void CourseCard_Open_LinksToTheCourseWithLevelScheduleAndLessons()
    {
        UseCulture("en");

        var card = RenderCourseCard(Course());

        var titleLink = card.Find("a.course-card-link");
        titleLink.GetAttribute("href").ShouldBe($"/courses/{CourseId}");
        titleLink.ClassList.ShouldContain("stretched-link"); // the whole card is the link
        card.FindAll("a").Count.ShouldBe(1);
        card.Find(".course-card").ClassList.ShouldNotContain("course-locked");
        card.Find(".level-badge").TextContent.Trim().ShouldEndWith("Intermediate");
        card.Find(".course-card__chips").TextContent.ShouldContain("8 weeks · 3× per week");
        card.Find(".course-card__lessons").TextContent.ShouldContain("12 lessons");
        card.Find(".course-card__category").TextContent.ShouldContain("Strength");
        card.Find(".tier-badge").ClassList.ShouldContain("tier-plus");
        card.Find(".course-card__description").TextContent.ShouldBe("Build a base of strength.");
        card.FindAll(".locked-badge").ShouldBeEmpty();
    }

    [Fact]
    public void CourseCard_Locked_LinksToPricingWithTheUpgradeLabel()
    {
        UseCulture("en");

        var card = RenderCourseCard(Course(), isLocked: true);

        card.Find("a.course-card-link").GetAttribute("href").ShouldBe("/pricing");
        card.Find(".course-card").ClassList.ShouldContain("course-locked");
        card.Find(".locked-badge").TextContent.ShouldContain("Upgrade to Plus");
    }

    [Fact]
    public void CourseCard_Enrolled_ShowsTheEnrolledBadge()
    {
        UseCulture("el");

        var card = RenderCourseCard(Course() with { IsEnrolled = true });

        card.Find(".enrolled-badge").TextContent.ShouldContain("Έχεις γραφτεί");
    }

    [Fact]
    public void CourseCard_Greek_ShowsGreekLevelScheduleAndLessons()
    {
        UseCulture("el");

        var card = RenderCourseCard(Course() with { CategoryName = "Δύναμη" });

        card.Find(".level-badge").TextContent.Trim().ShouldEndWith("Μεσαίο");
        card.Find(".course-card__chips").TextContent.ShouldContain("8 εβδομάδες · 3× την εβδομάδα");
        card.Find(".course-card__lessons").TextContent.ShouldContain("12 μαθήματα");
        card.Find(".course-card__category .bi").ClassList.ShouldContain("bi-lightning-charge");
    }

    [Fact]
    public void CourseCard_OneLessonAndOneWeek_UsesTheSingular()
    {
        UseCulture("el");

        var card = RenderCourseCard(Course() with { TotalLessons = 1, EstimatedWeeks = 1 });

        card.Find(".course-card__chips").TextContent.ShouldContain("1 εβδομάδα · 3× την εβδομάδα");
        card.Find(".course-card__lessons").TextContent.Trim().ShouldEndWith("1 μάθημα");
    }

    [Fact]
    public void CourseCard_CompactWithoutScheduleOrCover_HidesDescriptionAndShowsTheCategoryIcon()
    {
        var course = Course() with { EstimatedWeeks = null, SessionsPerWeek = null, CoverImageUrl = null };

        var card = RenderCourseCard(course, compact: true);

        card.Find(".course-card").ClassList.ShouldContain("course-card--compact");
        card.FindAll(".course-card__description").ShouldBeEmpty();
        card.FindAll(".course-card__chips .chip").Count.ShouldBe(1); // the level only
        card.Find(".course-cover__icon").ClassList.ShouldContain("bi-lightning-charge");
    }

    [Fact]
    public void CourseCard_WithCover_ShowsTheCoverPhoto()
    {
        var card = RenderCourseCard(Course() with { CoverImageUrl = "/images/exercises/barbell-squat/0.webp" });

        card.Find(".course-cover img").GetAttribute("src").ShouldBe("/images/exercises/barbell-squat/0.webp");
    }

    // ---- BlogCard -----------------------------------------------------------------------------

    [Fact]
    public void BlogCard_UploadedCover_UsesTheMediaApiUrl()
    {
        var card = RenderBlogCard(Article() with { CoverImageUrl = "blog/covers/a.jpg" });

        var cover = card.Find(".blog-card__cover img");
        cover.GetAttribute("src").ShouldBe("/api/media/blog/covers/a.jpg");
        cover.GetAttribute("loading").ShouldBe("lazy");
        cover.GetAttribute("width").ShouldBe("800");
        cover.GetAttribute("height").ShouldBe("450");
    }

    [Fact]
    public void BlogCard_ShippedCover_KeepsTheSiteRootPath()
    {
        var card = RenderBlogCard(Article());

        card.Find(".blog-card__cover img").GetAttribute("src").ShouldBe("/images/blog/progressive-overload-explained.webp");
    }

    [Fact]
    public void BlogCard_NoCover_ShowsThePlaceholderIcon()
    {
        var card = RenderBlogCard(Article() with { CoverImageUrl = null });

        card.FindAll(".blog-card__cover img").ShouldBeEmpty();
        card.FindAll(".blog-card__cover-icon").Count.ShouldBe(1);
    }

    [Fact]
    public void BlogCard_LinksToTheArticleWithTwoTagsAuthorAndDate()
    {
        UseCulture("el");

        var card = RenderBlogCard(Article());

        var titleLink = card.Find("a.blog-card__link");
        titleLink.GetAttribute("href").ShouldBe("/blog/progressive-overload-explained");
        titleLink.ClassList.ShouldContain("stretched-link");
        card.FindAll(".blog-card__tags li").Select(tag => tag.TextContent).ShouldBe(["strength", "programming"]);
        card.Find(".blog-card__meta").TextContent.ShouldContain("FitEdu");
        card.Find(".blog-card__meta time").GetAttribute("datetime").ShouldBe("2026-10-01");
        card.Find(".blog-card__more").TextContent.ShouldContain("Διάβασε περισσότερα");
    }

    [Fact]
    public void BlogCard_Unpublished_ShowsNoDate()
    {
        var card = RenderBlogCard(Article() with { PublishedAt = null, Tags = [] });

        card.FindAll("time").ShouldBeEmpty();
        card.FindAll(".blog-card__tags").ShouldBeEmpty();
    }

    // ---- TestimonialCard ----------------------------------------------------------------------

    [Fact]
    public void TestimonialCard_ShowsQuotePhotoNameAndRole()
    {
        var card = RenderTestimonialCard(Testimonial());

        card.Find("figure > blockquote").TextContent.ShouldContain("I ran 5K without stopping.");
        // The caption is the figure's last direct child, so it names the quote's author.
        card.Find("figure > figcaption:last-child").TextContent.ShouldContain("Alice Johnson");
        card.Find(".testimonial-card__avatar img").GetAttribute("src").ShouldBe("/api/media/testimonials/avatars/alice.jpg");
        card.Find(".testimonial-card__name").TextContent.ShouldBe("Alice Johnson");
        card.Find(".testimonial-card__role").TextContent.ShouldBe("Ran her first 5K · FitEdu member");
    }

    [Fact]
    public void TestimonialCard_AvatarFailsToLoad_ShowsTheInitials()
    {
        var card = RenderTestimonialCard(Testimonial());

        card.Find(".testimonial-card__avatar img").TriggerEvent("onerror", new WebErrorEventArgs());

        card.FindAll(".testimonial-card__avatar img").ShouldBeEmpty();
        card.Find(".testimonial-card__avatar span").TextContent.ShouldBe("AJ");
    }

    [Fact]
    public void TestimonialCard_NoAvatarOrRole_ShowsInitialsAndJustTheName()
    {
        var card = RenderTestimonialCard(Testimonial() with { AvatarUrl = null, RoleOrTitle = null, CompanyOrContext = " " });

        card.Find(".testimonial-card__initials").TextContent.ShouldBe("AJ");
        card.FindAll(".testimonial-card__role").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Alice Johnson", "AJ")]
    [InlineData("maria  papadopoulou kosta", "MP")]
    [InlineData("Νίκος", "Ν")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    public void TestimonialCard_Initials_TakesTheFirstLetterOfTheFirstTwoWords(string? fullName, string expected)
    {
        TestimonialCard.Initials(fullName).ShouldBe(expected);
    }

    // ---- PathCard -----------------------------------------------------------------------------

    [Fact]
    public void PathCard_HalfDone_ShowsRingStepsAndProgressInGreek()
    {
        UseCulture("el");
        var path = PathItem(stepCount: 4, completedSteps: 2);

        var card = RenderPathCard(path);

        var titleLink = card.Find("a.path-card__link");
        titleLink.GetAttribute("href").ShouldBe($"/paths/{path.Id}");
        titleLink.ClassList.ShouldContain("stretched-link");
        var ring = card.Find("[role=progressbar]");
        ring.GetAttribute("aria-valuenow").ShouldBe("50");
        ring.GetAttribute("aria-label").ShouldBe("2 από 4 ολοκληρώθηκαν");
        // Courses ("προγράμματα"), not the lessons ("μαθήματα") a course card counts.
        card.Find(".path-card__steps").TextContent.ShouldContain("4 προγράμματα");
        card.Find(".path-card__progress").TextContent.ShouldBe("2 από 4 ολοκληρώθηκαν");
        card.Find(".path-card__more").TextContent.ShouldContain("Δες την κατεύθυνση");
        card.Find(".chip").TextContent.ShouldContain("Κινητικότητα");
    }

    [Theory]
    [InlineData("en", "1 programme")]
    [InlineData("el", "1 πρόγραμμα")]
    public void PathCard_OneStep_UsesTheSingular(string culture, string expectedSteps)
    {
        UseCulture(culture);

        var card = RenderPathCard(PathItem(stepCount: 1, completedSteps: 0));

        card.Find(".path-card__steps").TextContent.Trim().ShouldEndWith(expectedSteps);
    }

    [Fact]
    public void PathCard_NotStartedOrEmpty_ShowsAnEmptyRingAndNoProgressLine()
    {
        var card = RenderPathCard(PathItem(stepCount: 0, completedSteps: 0) with { CategoryName = null, Description = null });

        card.Find("[role=progressbar]").GetAttribute("aria-valuenow").ShouldBe("0");
        card.FindAll(".path-card__progress").ShouldBeEmpty();
        card.FindAll(".chip").ShouldBeEmpty();
        card.FindAll(".path-card__description").ShouldBeEmpty();
    }

    // ---- EmptyState ---------------------------------------------------------------------------

    [Fact]
    public void EmptyState_WithAction_ShowsIconTitleTextAndLink()
    {
        var state = Render<EmptyState>(parameters => parameters
            .Add(component => component.Icon, "bi-signpost-split")
            .Add(component => component.Title, "No training paths yet.")
            .Add(component => component.Text, "Check back soon.")
            .Add(component => component.ActionHref, "/courses")
            .Add(component => component.ActionLabel, "Browse courses"));

        state.Find(".empty-state__icon .bi").ClassList.ShouldContain("bi-signpost-split");
        state.Find(".empty-state__title").TextContent.ShouldBe("No training paths yet.");
        state.Find(".empty-state__text").TextContent.ShouldBe("Check back soon.");
        var action = state.Find("a.empty-state__action");
        action.GetAttribute("href").ShouldBe("/courses");
        action.TextContent.ShouldBe("Browse courses");
    }

    [Fact]
    public void EmptyState_HrefWithoutLabel_ShowsNoLinkButKeepsChildContent()
    {
        var state = Render<EmptyState>(parameters => parameters
            .Add(component => component.Title, "Nothing here")
            .Add(component => component.ActionHref, "/courses")
            .AddChildContent("<button type=\"button\" class=\"retry\">Retry</button>"));

        state.FindAll("a").ShouldBeEmpty();
        state.FindAll(".empty-state__text").ShouldBeEmpty();
        state.Find(".empty-state__icon .bi").ClassList.ShouldContain("bi-inbox");
        state.Find("button.retry").TextContent.ShouldBe("Retry");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    private IRenderedComponent<CourseCard> RenderCourseCard(CourseListItemDto course, bool isLocked = false, bool compact = false) =>
        Render<CourseCard>(parameters => parameters
            .Add(component => component.Course, course)
            .Add(component => component.IsLocked, isLocked)
            .Add(component => component.Compact, compact));

    private IRenderedComponent<BlogCard> RenderBlogCard(BlogArticleSummaryDto article) =>
        Render<BlogCard>(parameters => parameters.Add(component => component.Article, article));

    private IRenderedComponent<TestimonialCard> RenderTestimonialCard(TestimonialDto testimonial) =>
        Render<TestimonialCard>(parameters => parameters.Add(component => component.Testimonial, testimonial));

    private IRenderedComponent<PathCard> RenderPathCard(LearningPathListItemDto path) =>
        Render<PathCard>(parameters => parameters.Add(component => component.Path, path));

    private static CourseListItemDto Course() => new(
        CourseId,
        "Strength Foundations",
        "<p>Build a base of strength.</p>",
        IsEnrolled: false,
        TotalLessons: 12,
        RequiredTier: SubscriptionTier.Plus,
        CategoryName: "Strength",
        Level: CourseLevel.Intermediate,
        EstimatedWeeks: 8,
        SessionsPerWeek: 3);

    // Noon UTC: the local date is the same in every time zone from UTC-11 to UTC+11.
    private static BlogArticleSummaryDto Article() => new(
        Guid.NewGuid(),
        "Progressive overload, explained",
        "progressive-overload-explained",
        "Add a little each week.",
        "/images/blog/progressive-overload-explained.webp",
        "FitEdu",
        ["strength", "programming", "basics"],
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    private static TestimonialDto Testimonial() => new(
        Guid.NewGuid(),
        "Alice Johnson",
        "Ran her first 5K",
        "FitEdu member",
        "I ran 5K without stopping.",
        "/api/media/testimonials/avatars/alice.jpg",
        DisplayOrder: 1);

    private static LearningPathListItemDto PathItem(int stepCount, int completedSteps) => new(
        Guid.NewGuid(),
        "Move Better",
        "Mobility first, then strength.",
        CategoryId: null,
        CategoryName: "Κινητικότητα",
        stepCount,
        completedSteps);

    protected override void Dispose(bool disposing)
    {
        _cultureScope.Dispose();
        base.Dispose(disposing);
    }
}
