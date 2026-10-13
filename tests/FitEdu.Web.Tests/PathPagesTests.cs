using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
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
/// The training path pages: the catalogue of path cards (or the empty state), and the path detail
/// with its progress hero and journey timeline, whose steps show completed, next and locked states.
/// </summary>
public class PathPagesTests : BunitContext
{
    private static readonly Guid PathId = Guid.Parse("3c1f2a9e-5b7d-4e8a-9c0b-1d2e3f4a5b6c");
    private static readonly Guid FirstCourseId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondCourseId = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdCourseId = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    private readonly IPathConsumer _pathConsumer = Substitute.For<IPathConsumer>();
    private readonly BunitAuthorizationContext _authorization;

    public PathPagesTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
        Services.AddSingleton(_pathConsumer);
        _authorization = AddAuthorization();
        UseCulture("en");
    }

    // ---- Paths --------------------------------------------------------------------------------

    [Fact]
    public void Paths_WithPaths_RendersOneCardPerPathInTheGrid()
    {
        _pathConsumer.GetPathsAsync(Arg.Any<string>()).Returns(
        [
            new LearningPathListItemDto(Guid.NewGuid(), "Beginner Start", "Build the habit.", null, "Strength", 4, 1),
            new LearningPathListItemDto(Guid.NewGuid(), "Move Better", null, null, null, 4, 0),
        ]);

        var page = Render<Paths>();

        page.Find("h1").TextContent.ShouldBe(PathRes.PathsTitle);
        page.Find("h2.visually-hidden").TextContent.ShouldBe(PathRes.PathsListHeading); // no skip from h1 to the card h3s
        page.Find(".paths-list").ClassList.ShouldContain("grid-auto");
        page.FindAll(".paths-list .path-card").Count.ShouldBe(2);
        page.FindAll(".empty-state").ShouldBeEmpty();
    }

    [Fact]
    public void Paths_NoPaths_ShowsTheEmptyStateWithACoursesLink()
    {
        _pathConsumer.GetPathsAsync(Arg.Any<string>()).Returns([]);

        var page = Render<Paths>();

        page.Find(".empty-state__title").TextContent.ShouldBe(PathRes.NoPathsYet);
        page.Find("a.empty-state__action").GetAttribute("href").ShouldBe("/courses");
        page.FindAll(".path-card").ShouldBeEmpty();
    }

    // ---- PathDetail ---------------------------------------------------------------------------

    [Fact]
    public void Steps_RenderMarkersWithStates()
    {
        _authorization.SetAuthorized("alice@fitedu.local");
        ReturnPath(Step(FirstCourseId, 1, isCompleted: true), Step(SecondCourseId, 2, isNext: true), Step(ThirdCourseId, 3, isLocked: true));

        var page = RenderDetail();

        var steps = page.FindAll("ol.journey > li.path-step");
        steps.Count.ShouldBe(3);
        steps[0].ClassList.ShouldContain("step-completed");
        steps[0].QuerySelector(".step-marker .bi-check-lg").ShouldNotBeNull();
        steps[1].ClassList.ShouldContain("step-next");
        steps[1].GetAttribute("aria-current").ShouldBe("step");
        steps[1].QuerySelector(".step-marker")!.TextContent.Trim().ShouldBe("2");
        steps[2].ClassList.ShouldContain("step-locked");
        steps[2].QuerySelector(".step-marker .bi-lock-fill").ShouldNotBeNull();
        steps[0].HasAttribute("aria-current").ShouldBeFalse();
    }

    [Fact]
    public void Locked_HasNoCourseLink()
    {
        _authorization.SetAuthorized("alice@fitedu.local");
        ReturnPath(Step(FirstCourseId, 1, isNext: true), Step(SecondCourseId, 2, isLocked: true));

        var page = RenderDetail();

        var steps = page.FindAll("li.path-step");
        steps[0].QuerySelector("a.step-link")!.GetAttribute("href").ShouldBe($"/courses/{FirstCourseId}");
        steps[1].QuerySelectorAll("a").ShouldBeEmpty();
        steps[1].QuerySelector(".step-hint")!.TextContent.ShouldBe(PathRes.StepLockedHint);
    }

    [Fact]
    public void CompletedAndLocked_KeepsTheCourseLinkWithoutTheLockHint()
    {
        // Courses are shared between paths: one finished through another path can be locked here.
        _authorization.SetAuthorized("alice@fitedu.local");
        ReturnPath(Step(FirstCourseId, 1, isNext: true), Step(SecondCourseId, 2, isCompleted: true, isLocked: true));

        var page = RenderDetail();

        var step = page.FindAll("li.path-step")[1];
        step.QuerySelector(".step-marker .bi-check-lg").ShouldNotBeNull();
        step.QuerySelector(".badge-completed").ShouldNotBeNull();
        step.QuerySelector("a.step-link")!.GetAttribute("href").ShouldBe($"/courses/{SecondCourseId}");
        step.QuerySelector(".step-hint").ShouldBeNull();
    }

    [Fact]
    public void StepLink_NamesItsCourseForScreenReaders()
    {
        ReturnPath(Step(FirstCourseId, 1));

        var page = RenderDetail();

        page.Find("a.step-link .visually-hidden").TextContent.ShouldContain("Course 1");
    }

    [Fact]
    public void SignedIn_HeroShowsTheRingAndContinuesWithTheNextCourse()
    {
        _authorization.SetAuthorized("alice@fitedu.local");
        ReturnPath(Step(FirstCourseId, 1, isCompleted: true), Step(SecondCourseId, 2, isNext: true), Step(ThirdCourseId, 3, isLocked: true));

        var page = RenderDetail();

        var ring = page.Find(".path-hero [role=progressbar]");
        ring.GetAttribute("aria-valuenow").ShouldBe("33");
        ring.GetAttribute("aria-label").ShouldBe("1 of 3 completed");
        page.Find(".path-hero .progress-ring__text").TextContent.ShouldBe("1/3");
        var nextLink = page.Find("a.path-hero__cta");
        nextLink.GetAttribute("href").ShouldBe($"/courses/{SecondCourseId}");
        nextLink.TextContent.ShouldContain(PathRes.ContinuePath);
        page.FindAll(".path-signin").ShouldBeEmpty();
    }

    [Fact]
    public void SignedIn_NothingDoneYet_OffersToStartThePath()
    {
        _authorization.SetAuthorized("alice@fitedu.local");
        ReturnPath(Step(FirstCourseId, 1, isNext: true), Step(SecondCourseId, 2, isLocked: true));

        var page = RenderDetail();

        page.Find("a.path-hero__cta").TextContent.ShouldContain(PathRes.StartPath);
    }

    [Fact]
    public void SignedIn_AllCompleted_ShowsTheCompletedChipInsteadOfALink()
    {
        _authorization.SetAuthorized("alice@fitedu.local");
        ReturnPath(Step(FirstCourseId, 1, isCompleted: true), Step(SecondCourseId, 2, isCompleted: true));

        var page = RenderDetail();

        page.Find(".path-hero__done").TextContent.ShouldContain(PathRes.PathCompleted);
        page.FindAll("a.path-hero__cta").ShouldBeEmpty();
        page.Find(".path-hero [role=progressbar]").GetAttribute("aria-valuenow").ShouldBe("100");
    }

    [Fact]
    public void Guest_SeesTheSignInPromptAndEveryCourseLink()
    {
        ReturnPath(Step(FirstCourseId, 1), Step(SecondCourseId, 2));

        var page = RenderDetail();

        page.Find(".path-signin__text").TextContent.ShouldBe(PathRes.SignInToTrackProgress);
        page.Find("a.path-signin__action").GetAttribute("href").ShouldBe("/login");
        page.FindAll(".path-hero [role=progressbar]").ShouldBeEmpty();
        page.FindAll("a.step-link").Count.ShouldBe(2);
    }

    [Fact]
    public void Greek_ShowsGreekStepLabelsAndCourseCount()
    {
        UseCulture("el");
        _authorization.SetAuthorized("alice@fitedu.local");
        ReturnPath(Step(FirstCourseId, 1, isNext: true), Step(SecondCourseId, 2, isLocked: true));

        var page = RenderDetail();

        page.Find(".step-kicker").TextContent.ShouldBe("Βήμα 1");
        page.Find(".path-hero__count").TextContent.ShouldContain("2 προγράμματα");
        page.Find(".badge-next").TextContent.ShouldBe("Επόμενο");
        page.Find(".journey-title").TextContent.ShouldBe("Τα προγράμματα της διαδρομής");
    }

    [Fact]
    public void UnknownPath_ShowsNotFoundWithABackLink()
    {
        _pathConsumer.GetPathAsync(PathId, Arg.Any<string>()).Returns((LearningPathDetailDto?)null);

        var page = RenderDetail();

        page.Find(".empty-state__title").TextContent.ShouldBe(PathRes.PathNotFound);
        page.Find("a.empty-state__action").GetAttribute("href").ShouldBe("/paths");
        page.FindAll("ol.journey").ShouldBeEmpty();
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    private IRenderedComponent<PathDetail> RenderDetail() =>
        Render<PathDetail>(parameters => parameters.Add(page => page.Id, PathId));

    private void ReturnPath(params LearningPathStepDto[] steps) =>
        _pathConsumer.GetPathAsync(PathId, Arg.Any<string>())
            .Returns(new LearningPathDetailDto(PathId, "Beginner Start", "Four courses to build the habit.", null, "Strength", steps));

    private static LearningPathStepDto Step(Guid courseId, int order, bool isCompleted = false, bool isLocked = false, bool isNext = false) =>
        new(courseId, $"Course {order}", order, isCompleted, isLocked, isNext);
}
