using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The course catalogue: skeleton grid while loading, cards in the grid, the plan limit banner,
/// and the filters and page number that live in the address (?goal=…&amp;page=2). The tests run as a
/// signed-in member unless they say otherwise; guests see the catalogue without a plan.
/// </summary>
public class CoursesPageTests : BunitContext
{
    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private const int CoursesOnPage = 3;

    private readonly ICourseConsumer _courseConsumer = Substitute.For<ICourseConsumer>();
    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();
    private readonly ICategoryConsumer _categoryConsumer = Substitute.For<ICategoryConsumer>();
    private readonly BunitAuthorizationContext _authorization;
    private readonly Guid _strengthCategoryId = Guid.NewGuid();

    public CoursesPageTests()
    {
        UseCulture("en");
        _authorization = AddAuthorization();
        _authorization.SetAuthorized("alice.johnson@fitedu.local");
        Services.AddSingleton(_courseConsumer);
        Services.AddSingleton(_subscriptionConsumer);
        Services.AddSingleton(_categoryConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere

        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Pro, int.MaxValue));
        _categoryConsumer.GetCategoriesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns([new CategoryDto(_strengthCategoryId, "Strength", 4)]);
        ReturnCourses(Courses(CoursesOnPage));
    }

    [Fact]
    public void Loading_RendersGridSkeletonUnderTheFilters()
    {
        _courseConsumer.GetCoursesAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<FitnessGoal?>(), Arg.Any<CourseLevel?>(), Arg.Any<string?>())
            .Returns(new TaskCompletionSource<PagedResult<CourseListItemDto>>().Task);

        var page = Render<Courses>();

        page.Find(".skeleton-grid").ClassList.ShouldContain("grid-auto");
        page.FindAll(".skeleton-grid .course-skeleton").Count.ShouldBe(6);
        page.Find(".courses-results").GetAttribute("aria-busy").ShouldBe("true");
        page.FindAll(".category-filter-bar").Count.ShouldBe(1); // the filters stay put while the grid loads
        page.FindAll(".course-card").ShouldBeEmpty();
    }

    [Fact]
    public void Loaded_RendersCourseCardsInTheGrid()
    {
        var page = Render<Courses>();

        page.FindAll(".courses-grid .course-card").Count.ShouldBe(CoursesOnPage);
        page.FindAll("a.course-card-link").Count.ShouldBe(CoursesOnPage);
        page.FindAll(".skeleton-grid").ShouldBeEmpty();
        page.Find(".courses-toolbar__count").TextContent.Trim().ShouldBe("3 programmes");
    }

    [Fact]
    public void PlanLimitReached_ShowsFullBannerWithLimeUpgradeLink()
    {
        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Free, maxCourses: 1));
        _courseConsumer.CountEnrollmentsAsync().Returns(1);

        var page = Render<Courses>();

        page.Find(".plan-limit-full .slots-full-label").TextContent.ShouldNotBeNullOrWhiteSpace();
        var upgradeLink = page.Find(".plan-limit-banner a.plan-limit-banner__action");
        upgradeLink.GetAttribute("href").ShouldBe("/pricing");
        upgradeLink.ClassList.ShouldNotContain("btn-cta--ghost");
    }

    [Fact]
    public void PlanLimitReached_ByAnEnrolmentOffThisPage_StillShowsTheFullBanner()
    {
        // The visible page holds none of the member's enrolments; the count comes from the API.
        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Free, maxCourses: 1));
        _courseConsumer.CountEnrollmentsAsync().Returns(1);

        var page = Render<Courses>();

        page.FindAll(".plan-limit-full").Count.ShouldBe(1);
        page.Find(".plan-limit-banner__usage").TextContent.Trim().ShouldBe("Active programmes: 1 of 1");
    }

    [Fact]
    public void PlanWithFreeSlots_ShowsLimitAndUsageWithoutFullNotice()
    {
        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Plus, maxCourses: 10));
        _courseConsumer.CountEnrollmentsAsync().Returns(3);

        var page = Render<Courses>();

        page.Find(".plan-limit-banner").TextContent.ShouldContain("Programme limit on the Plus plan: 10");
        page.Find(".plan-limit-banner__usage").TextContent.Trim().ShouldBe("Active programmes: 3 of 10");
        page.FindAll(".plan-limit-full").ShouldBeEmpty();
        page.Find(".plan-limit-banner__action").ClassList.ShouldContain("btn-cta--ghost");
    }

    [Fact]
    public void PlanCountFailed_ShowsTheLimitWithoutUsageOrFullNotice()
    {
        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Free, maxCourses: 1));
        _courseConsumer.CountEnrollmentsAsync().Returns((int?)null);

        var page = Render<Courses>();

        page.FindAll(".plan-limit-banner").Count.ShouldBe(1);
        page.FindAll(".plan-limit-banner__usage").ShouldBeEmpty();
        page.FindAll(".plan-limit-full").ShouldBeEmpty();
    }

    [Fact]
    public void UnlimitedPlan_HasNoBanner()
    {
        var page = Render<Courses>();

        page.FindAll(".plan-limit-banner").ShouldBeEmpty();
    }

    [Fact]
    public void Guest_SeesTheCatalogueWithoutAPlanBanner()
    {
        _authorization.SetNotAuthorized();
        var paidCourse = Course("Pro course") with { RequiredTier = SubscriptionTier.Pro };
        ReturnCourses([Course("Free course"), paidCourse]);

        var page = Render<Courses>();

        page.FindAll(".courses-grid .course-card").Count.ShouldBe(2);
        page.FindAll(".plan-limit-banner").ShouldBeEmpty();
        page.FindAll("a.course-card-link").Select(link => link.GetAttribute("href")).ShouldContain("/pricing");
        _subscriptionConsumer.DidNotReceive().GetStatusAsync();
    }

    [Fact]
    public void AddressFilters_AreSentToTheApiAndShownAsPressedChips()
    {
        NavigateTo("/courses?goal=fatloss&level=Beginner&equipment=None");

        var page = Render<Courses>();

        _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, Arg.Any<string?>(), FitnessGoal.FatLoss, CourseLevel.Beginner, "none");
        PressedChipLabels(page).ShouldBe(["All", "Fat loss", "Beginner", "No equipment"]);
        page.Find(".category-filter-toggle__count").TextContent.ShouldBe("3");
    }

    [Fact]
    public void UnknownAddressFilters_AreIgnored()
    {
        NavigateTo("/courses?goal=42&level=Expert&equipment=dumbell");

        Render<Courses>();

        _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, Arg.Any<string?>(), null, null, null);
    }

    [Fact]
    public void GoalChip_PutsTheGoalInTheAddressAndReloads()
    {
        NavigateTo("/courses");
        var page = Render<Courses>();

        page.FindAll(".category-chip-option").Single(chip => chip.TextContent.Trim() == "Muscle gain").Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/courses?goal=MuscleGain");
        page.WaitForAssertion(() => _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, Arg.Any<string?>(), FitnessGoal.MuscleGain, null, null));
    }

    [Fact]
    public void NoMatches_ClearFiltersDropsEveryFilter()
    {
        ReturnCourses([]);
        NavigateTo($"/courses?level=Advanced&category={_strengthCategoryId}&search=yoga&page=2");
        var page = Render<Courses>();

        page.Find(".empty-state .courses-clear-filters").Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/courses");
        page.WaitForAssertion(() => _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, string.Empty, null, null, null));
    }

    [Fact]
    public void AddressCategorySearchAndPage_AreSentToTheApiAndShown()
    {
        ReturnCourses(Courses(CoursesOnPage), totalCount: 30, page: 2);
        NavigateTo($"/courses?category={_strengthCategoryId}&search=core&page=2");

        var page = Render<Courses>();

        _courseConsumer.Received().GetCoursesAsync(
            2, Arg.Any<int>(), "en", _strengthCategoryId, "core", null, null, null);
        page.Find(".category-filter-search").GetAttribute("value").ShouldBe("core");
        PressedChipLabels(page).ShouldContain(label => label.StartsWith("Strength", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedAddressCategoryAndPage_AreIgnored()
    {
        NavigateTo("/courses?category=not-a-guid&page=abc");

        Render<Courses>();

        _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, string.Empty, null, null, null);
    }

    [Fact]
    public void CategoryChip_PutsTheCategoryInTheAddressAndGoesBackToPageOne()
    {
        ReturnCourses(Courses(CoursesOnPage), totalCount: 30, page: 2);
        NavigateTo("/courses?page=2");
        var page = Render<Courses>();

        page.FindAll(".category-chip-option").First(chip => chip.TextContent.Contains("Strength", StringComparison.Ordinal)).Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith($"/courses?category={_strengthCategoryId}");
        page.WaitForAssertion(() => _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", _strengthCategoryId, string.Empty, null, null, null));
    }

    [Fact]
    public void Search_PutsTheSearchInTheAddressOnceTypingStops()
    {
        NavigateTo("/courses");
        var page = Render<Courses>();

        page.Find(".category-filter-search").Input("core");

        page.WaitForAssertion(() =>
            Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/courses?search=core"));
        page.WaitForAssertion(() => _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, "core", null, null, null));
    }

    [Fact]
    public void Search_KeepsTheTrailingSpaceInTheBoxOnceTypingStops()
    {
        NavigateTo("/courses");
        var page = Render<Courses>();

        page.Find(".category-filter-search").Input("full ");

        // The address holds the trimmed search, but the box keeps the space so the next word stays apart.
        page.WaitForAssertion(() => _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, "full", null, null, null));
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/courses?search=full");
        page.Find(".category-filter-search").GetAttribute("value").ShouldBe("full ");
    }

    [Fact]
    public void NextPage_PutsThePageInTheAddress()
    {
        ReturnCourses(Courses(CoursesOnPage), totalCount: 30);
        NavigateTo("/courses?goal=Strength");
        var page = Render<Courses>();

        var pageButtons = page.FindAll(".courses-pagination button");
        pageButtons[pageButtons.Count - 1].Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/courses?goal=Strength&page=2");
        page.WaitForAssertion(() => _courseConsumer.Received().GetCoursesAsync(
            2, Arg.Any<int>(), "en", null, string.Empty, FitnessGoal.Strength, null, null));
    }

    [Fact]
    public void EmptyCatalogue_ShowsEmptyStateWithoutFilters()
    {
        ReturnCourses([]);

        var page = Render<Courses>();

        page.Find(".empty-state__title").TextContent.ShouldBe("No programmes available at the moment.");
        page.FindAll(".category-filter-bar").ShouldBeEmpty();
    }

    [Fact]
    public void FiltersButton_OpensTheExtraGroupsOnPhones()
    {
        var page = Render<Courses>();
        page.Find(".category-filter-extra").ClassList.ShouldNotContain("is-open");

        page.Find(".category-filter-toggle").Click();

        page.Find(".category-filter-toggle").GetAttribute("aria-expanded").ShouldBe("true");
        page.Find(".category-filter-extra").ClassList.ShouldContain("is-open");
    }

    [Fact]
    public void Greek_ChipsAndCountUseGreekLabels()
    {
        UseCulture("el");

        var page = Render<Courses>();

        var chipLabels = page.FindAll(".category-chip-option").Select(chip => chip.TextContent.Trim()).ToList();
        chipLabels.ShouldContain("Απώλεια λίπους");
        chipLabels.ShouldContain("Αρχάριο");
        chipLabels.ShouldContain("Χωρίς εξοπλισμό");
        page.Find(".courses-toolbar__count").TextContent.Trim().ShouldBe("3 προγράμματα");
        _categoryConsumer.Received().GetCategoriesAsync("courses", "el");
    }

    [Fact]
    public void Greek_PlanBannerQuotesThePlanNameAndUsesSentenceCase()
    {
        UseCulture("el");
        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Plus, maxCourses: 10));
        _courseConsumer.CountEnrollmentsAsync().Returns(2);

        var page = Render<Courses>();

        page.Find(".plan-limit-banner__text").TextContent.ShouldContain("Όριο ενεργών προγραμμάτων στο πλάνο «Plus»: 10");
        page.Find(".plan-limit-banner__usage").TextContent.Trim().ShouldBe("Ενεργά προγράμματα: 2 από 10");
        page.Find(".plan-limit-banner__action").TextContent.Trim().ShouldBe("Αναβάθμισε το πλάνο σου");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    private void NavigateTo(string relativeUri) =>
        Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUri);

    private void ReturnCourses(List<CourseListItemDto> courses, int? totalCount = null, int page = 1) =>
        _courseConsumer.GetCoursesAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<FitnessGoal?>(), Arg.Any<CourseLevel?>(), Arg.Any<string?>())
            .Returns(new PagedResult<CourseListItemDto>(courses, totalCount ?? courses.Count, page, 12));

    private static List<string> PressedChipLabels(IRenderedComponent<Courses> page) =>
        page.FindAll(".category-chip-option[aria-pressed=true]")
            .Select(chip => chip.TextContent.Trim())
            .ToList();

    private static List<CourseListItemDto> Courses(int count) =>
        Enumerable.Range(1, count).Select(number => Course($"Course {number}")).ToList();

    private static CourseListItemDto Course(string title) =>
        new(Guid.NewGuid(), title, "A course.", IsEnrolled: false, TotalLessons: 6);

    private static UserSubscriptionStatusDto Status(SubscriptionTier tier, int maxCourses) =>
        new(tier, tier.ToString(), DateTime.UtcNow, null, true, new PlanFeaturesDto { MaxCourses = maxCourses });

    protected override void Dispose(bool disposing)
    {
        _cultureScope.Dispose();
        base.Dispose(disposing);
    }
}
