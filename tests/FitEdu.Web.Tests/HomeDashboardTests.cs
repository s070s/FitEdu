using System.Globalization;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the signed-in home page: the student's "Today's plan" (the next lesson of the
/// current programme), the training stats, the enrolled and recommended courses, and the explore
/// tiles for students and admins.
/// </summary>
public class HomeDashboardTests : BunitContext
{
    private static readonly Guid CurrentCourseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NextLessonId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Late on Tuesday 13 October in UTC; the dashboard date follows the UTC day like the streak.
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 23, 30, 0, TimeSpan.Zero);

    private readonly ICourseConsumer _courseConsumer = Substitute.For<ICourseConsumer>();
    private readonly IProfileConsumer _profileConsumer = Substitute.For<IProfileConsumer>();
    private readonly BunitAuthorizationContext _authorization;

    public HomeDashboardTests()
    {
        Services.AddSingleton(_courseConsumer);
        Services.AddSingleton(_profileConsumer);
        Services.AddSingleton(Substitute.For<ITestimonialConsumer>());
        Services.AddSingleton(Substitute.For<IBlogConsumer>());
        Services.AddSingleton(Substitute.For<ILandingConsumer>());
        Services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddBunitPersistentComponentState();
        _authorization = AddAuthorization();
        _authorization.SetAuthorized("alice.johnson@fitedu.local");
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
    }

    [Fact]
    public void Student_SeesTheNextLessonOfTheCurrentProgramme()
    {
        UseStudent(Stats(CurrentCourseId));

        var page = Render<Home>();

        page.Find(".dashboard-title").TextContent.ShouldBe("Welcome, Alice!");
        page.Find(".today-plan__lesson").TextContent.ShouldBe("Day 2: Push");
        page.Find(".today-plan__cta").GetAttribute("href").ShouldBe($"/lessons/{NextLessonId}");
        page.Find(".today-plan__progress").TextContent.ShouldContain("1 of 3 workouts done");
    }

    [Fact]
    public void Dashboard_EyebrowShowsTodaysUtcDate()
    {
        UseStudent(Stats(CurrentCourseId));

        var page = Render<Home>();

        page.Find(".dashboard-head .eyebrow").TextContent.ShouldBe("Tuesday 13 October");
    }

    [Fact]
    public void Student_SeesTheTrainingStats()
    {
        UseStudent(Stats(CurrentCourseId));

        var page = Render<Home>();

        var values = page.FindAll(".dashboard-stats .stat-tile__value").Select(value => value.TextContent).ToList();
        values.ShouldBe(["12", "345", "4", "2"]);
    }

    [Fact]
    public void Student_WithoutAProgramme_GetsTheBrowseCoursesEmptyState()
    {
        UseStudent(Stats(currentCourseId: null), catalog: []);

        var page = Render<Home>();

        page.FindAll(".today-plan__lesson").ShouldBeEmpty();
        page.Find(".today-plan--empty .empty-state__action").GetAttribute("href").ShouldBe("/courses");
        page.FindAll("#programmes-title").ShouldBeEmpty();
        page.Find(".today-plan--empty").TextContent.ShouldContain(HomeRes.TodayPlanEmpty);
    }

    [Fact]
    public void Student_WhoFinishedEveryProgramme_IsToldToPickTheNextOne()
    {
        // Enrolled courses but no programme in progress: every joined programme is finished.
        UseStudent(Stats(currentCourseId: null));

        var page = Render<Home>();

        var emptyPlan = page.Find(".today-plan--empty");
        emptyPlan.TextContent.ShouldContain(HomeRes.TodayPlanAllDone);
        emptyPlan.TextContent.ShouldNotContain(HomeRes.TodayPlanEmpty);
    }

    [Fact]
    public void Student_SeesEnrolledCoursesAndRecommendationsForTheirGoalFirst()
    {
        UseStudent(Stats(CurrentCourseId));

        var page = Render<Home>();

        var enrolled = page.FindAll("[aria-labelledby=programmes-title] .course-card").Count;
        enrolled.ShouldBe(2);
        var recommended = page.FindAll("[aria-labelledby=recommended-title] a.course-card-link")
            .Select(link => link.TextContent).ToList();
        recommended.First().ShouldBe("Mobility for runners");
        recommended.ShouldNotContain("Push day basics");
    }

    [Fact]
    public void Student_StatsUnavailable_StillRendersTheDashboard()
    {
        UseStudent(stats: null);

        var page = Render<Home>();

        page.FindAll(".dashboard-stats").ShouldBeEmpty();
        page.FindAll(".today-plan--empty").Count.ShouldBe(1);
        page.FindAll(".explore-tile").ShouldNotBeEmpty();
    }

    [Fact]
    public void Student_ExploreTilesSkipHome()
    {
        UseStudent(Stats(CurrentCourseId));

        var page = Render<Home>();

        var links = page.FindAll(".explore-tile").Select(tile => tile.GetAttribute("href")).ToList();
        links.ShouldContain("courses");
        links.ShouldNotContain(string.Empty);
    }

    [Fact]
    public void Admin_SeesAdminExploreTilesAndNoStudentDashboard()
    {
        _authorization.SetRoles("Admin");

        var page = Render<Home>();

        page.FindAll(".today-plan").ShouldBeEmpty();
        page.FindAll(".explore-tile").Select(tile => tile.GetAttribute("href")).ShouldContain("admin/users");
        _profileConsumer.DidNotReceive().GetTrainingStatsAsync();
    }

    private void UseStudent(TrainingStatsDto? stats, List<CourseListItemDto>? catalog = null)
    {
        _authorization.SetRoles("Student");
        _authorization.SetClaims(new Claim("firstName", "Alice"));
        _profileConsumer.GetTrainingStatsAsync().Returns(stats);
        var items = catalog ?? Catalog();
        _courseConsumer.GetCoursesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<FitnessGoal?>(), Arg.Any<CourseLevel?>(), Arg.Any<string?>())
            .Returns(new PagedResult<CourseListItemDto>(items, items.Count, 1, 100));
        _courseConsumer.GetCourseAsync(CurrentCourseId, Arg.Any<string>()).Returns(CurrentCourse());
    }

    private static TrainingStatsDto Stats(Guid? currentCourseId) =>
        new(12, 345, 4, currentCourseId, FitnessGoal.Mobility, CourseLevel.Beginner);

    private static List<CourseListItemDto> Catalog() =>
    [
        new(CurrentCourseId, "Push day basics", null, true, 3, Goal: FitnessGoal.Strength),
        new(Guid.NewGuid(), "Core foundations", null, true, 4, Goal: FitnessGoal.GeneralFitness),
        new(Guid.NewGuid(), "Heavy lifting", null, false, 6, AverageRating: 4.9, Goal: FitnessGoal.Strength, Level: CourseLevel.Advanced),
        new(Guid.NewGuid(), "Mobility for runners", null, false, 5, Goal: FitnessGoal.Mobility, Level: CourseLevel.Beginner),
    ];

    private static CourseDetailDto CurrentCourse() => new(
        CurrentCourseId, "Push day basics", null, IsEnrolled: true, IsCompleted: false,
        CompletedLessons: 1, TotalLessons: 3, ProgressPercent: 33,
        Modules:
        [
            new(Guid.NewGuid(), "Week 2", null, 2, [new(Guid.NewGuid(), "Day 3: Legs", 0, 30, 1, false)]),
            new(Guid.NewGuid(), "Week 1", null, 1,
            [
                new(NextLessonId, "Day 2: Push", 0, 25, 2, false),
                new(Guid.NewGuid(), "Day 1: Warm-up", 0, 15, 1, true),
            ]),
        ],
        RequiredTier: SubscriptionTier.Free);
}
