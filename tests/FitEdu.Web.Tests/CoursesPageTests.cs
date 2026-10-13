using System.Globalization;
using Bunit;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
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
/// and the goal, level and equipment chips that live in the address (?goal=…).
/// </summary>
public class CoursesPageTests : BunitContext
{
    private const int CoursesOnPage = 3;

    private readonly ICourseConsumer _courseConsumer = Substitute.For<ICourseConsumer>();
    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();
    private readonly ICategoryConsumer _categoryConsumer = Substitute.For<ICategoryConsumer>();

    public CoursesPageTests()
    {
        UseCulture("en");
        Services.AddSingleton(_courseConsumer);
        Services.AddSingleton(_subscriptionConsumer);
        Services.AddSingleton(_categoryConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere

        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Pro, int.MaxValue));
        _categoryConsumer.GetCategoriesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns([new CategoryDto(Guid.NewGuid(), "Strength", 4)]);
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
        ReturnCourses([Course("Enrolled course") with { IsEnrolled = true }]);

        var page = Render<Courses>();

        page.Find(".plan-limit-full .slots-full-label").TextContent.ShouldNotBeNullOrWhiteSpace();
        var upgradeLink = page.Find(".plan-limit-banner a.plan-limit-banner__action");
        upgradeLink.GetAttribute("href").ShouldBe("/pricing");
        upgradeLink.ClassList.ShouldNotContain("btn-cta--ghost");
    }

    [Fact]
    public void PlanWithFreeSlots_ShowsLimitWithoutFullNotice()
    {
        _subscriptionConsumer.GetStatusAsync().Returns(Status(SubscriptionTier.Plus, maxCourses: 10));

        var page = Render<Courses>();

        page.Find(".plan-limit-banner").TextContent.ShouldContain("Programme limit on the Plus plan: 10");
        page.FindAll(".plan-limit-full").ShouldBeEmpty();
        page.Find(".plan-limit-banner__action").ClassList.ShouldContain("btn-cta--ghost");
    }

    [Fact]
    public void UnlimitedPlan_HasNoBanner()
    {
        var page = Render<Courses>();

        page.FindAll(".plan-limit-banner").ShouldBeEmpty();
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
        NavigateTo("/courses?level=Advanced");
        var page = Render<Courses>();

        page.Find(".empty-state .courses-clear-filters").Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/courses");
        page.WaitForAssertion(() => _courseConsumer.Received().GetCoursesAsync(
            1, Arg.Any<int>(), "en", null, string.Empty, null, null, null));
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

        var page = Render<Courses>();

        page.Find(".plan-limit-banner__text").TextContent.ShouldContain("Όριο προγραμμάτων στο πλάνο «Plus»: 10");
        page.Find(".plan-limit-banner__action").TextContent.Trim().ShouldBe("Αναβάθμιση πλάνου");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    private void NavigateTo(string relativeUri) =>
        Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUri);

    private void ReturnCourses(List<CourseListItemDto> courses) =>
        _courseConsumer.GetCoursesAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<FitnessGoal?>(), Arg.Any<CourseLevel?>(), Arg.Any<string?>())
            .Returns(new PagedResult<CourseListItemDto>(courses, courses.Count, 1, 12));

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
}
