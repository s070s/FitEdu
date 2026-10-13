using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The course page: hero chips, the outline (lesson kind, minutes and exercise count), the
/// "What you'll train" list and the action card for each enrolment state. The tests run as a
/// signed-in member unless they say otherwise; a guest gets sign-up and log-in links instead.
/// </summary>
public class CourseDetailPageTests : BunitContext
{
    private static readonly Guid CourseId = Guid.Parse("0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0");

    private readonly ICourseConsumer _courseService = Substitute.For<ICourseConsumer>();
    private readonly ISubscriptionConsumer _subscriptionService = Substitute.For<ISubscriptionConsumer>();
    private readonly BunitAuthorizationContext _authorization;

    public CourseDetailPageTests()
    {
        _authorization = AddAuthorization();
        _authorization.SetAuthorized("alice.johnson@fitedu.local");
        Services.AddSingleton(_courseService);
        Services.AddSingleton(_subscriptionService);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere

        _courseService.GetReviewsAsync(CourseId).Returns((CourseReviewsResponseDto?)null);
        UsePlan(SubscriptionTier.Free);
    }

    // ---- Hero ---------------------------------------------------------------------------------

    [Fact]
    public void Hero_ShowsTheTitleChipsAndProgrammeFacts()
    {
        UseCulture("en");
        UseCourse(Course());

        var page = RenderPage();

        page.Find("h1").TextContent.ShouldBe("Strength Foundations");
        page.Find(".course-hero__eyebrow").TextContent.ShouldContain("Strength");
        var chips = page.Find(".course-hero__chips").TextContent;
        chips.ShouldContain("Intermediate");
        chips.ShouldContain("Muscle gain");
        chips.ShouldContain("6 weeks · 3× per week");
        chips.ShouldContain("Dumbbells, Bands");
        var facts = page.Find(".course-hero__facts").TextContent;
        facts.ShouldContain("2 modules");
        facts.ShouldContain("3 lessons");
        facts.ShouldContain("62 min");
        page.Find(".course-hero__media img").GetAttribute("loading").ShouldBe("eager");
    }

    [Fact]
    public void Greek_ShowsGreekLabelsAndSingularCounts()
    {
        UseCulture("el");
        UseCourse(Course());

        var page = RenderPage();

        page.Find(".course-hero__chips").TextContent.ShouldContain("Μυϊκή ανάπτυξη");
        var secondModuleMeta = page.FindAll(".module__meta")[1].TextContent;
        secondModuleMeta.ShouldContain("1 μάθημα");
        secondModuleMeta.ShouldContain("12 λεπ.");
        page.FindAll(".lesson-exercises").Select(count => count.TextContent).ShouldBe(["6 ασκήσεις", "1 άσκηση"]);
        page.Find(".course-cta__enrol").TextContent.Trim().ShouldBe("Γράψου στο πρόγραμμα");
    }

    // ---- Outcomes and outline -----------------------------------------------------------------

    [Fact]
    public void Outcomes_ListTheFirstFourModuleDescriptions()
    {
        UseCulture("en");
        var modules = Enumerable.Range(1, 6)
            .Select(number => Module($"Module {number}", number == 2 ? null : $"Outcome {number}", Lesson($"Lesson {number}")))
            .ToArray();
        UseCourse(Course(modules: modules));

        var page = RenderPage();

        page.FindAll(".course-outcomes__item").Select(item => item.TextContent.Trim())
            .ShouldBe(["Outcome 1", "Outcome 3", "Outcome 4", "Outcome 5"]);
    }

    [Fact]
    public void Outline_ShowsEachLessonsKindMinutesAndExerciseCount()
    {
        UseCulture("en");
        UseCourse(Course());

        var page = RenderPage();

        var rows = page.FindAll(".lesson-item");
        rows.Count.ShouldBe(3);
        rows[0].QuerySelector(".lesson-item__icon--reading").ShouldNotBeNull();
        rows[0].TextContent.ShouldContain("Reading");
        rows[1].QuerySelector(".lesson-item__icon--workout").ShouldNotBeNull();
        rows[1].TextContent.ShouldContain("Workout");
        rows[1].TextContent.ShouldContain("40 min");
        rows[1].TextContent.ShouldContain("6 exercises");
        rows[2].QuerySelector(".lesson-item__icon--workout").ShouldNotBeNull(); // a video with exercises is a workout
        rows[2].TextContent.ShouldContain("1 exercise");
    }

    [Fact]
    public void Outline_VideoAndPdfLessonsGetTheirOwnKind()
    {
        UseCulture("en");
        UseCourse(Course(modules:
        [
            Module("Extras", null,
                Lesson("Technique video", contentType: ContentType.Video),
                Lesson("Printable plan", contentType: ContentType.Pdf))
        ]));

        var page = RenderPage();

        page.Find(".lesson-item__icon--video").ShouldNotBeNull();
        page.Find(".lesson-item__icon--pdf").ShouldNotBeNull();
        page.Find(".lessons-list").TextContent.ShouldContain("PDF guide");
    }

    // ---- Action card --------------------------------------------------------------------------

    [Fact]
    public void NotEnrolled_LessonsAreNotLinks_AndTheCardOffersToJoin()
    {
        UseCulture("en");
        UseCourse(Course());

        var page = RenderPage();

        page.FindAll("a.lesson-item").Count.ShouldBe(3);
        page.FindAll("a.lesson-item[href]").ShouldBeEmpty();
        page.Find(".course-outline__note").TextContent.ShouldContain("Join the programme to open its lessons.");
        page.Find(".course-cta__enrol").TextContent.Trim().ShouldBe("Join the programme");
        page.FindAll(".enroll-upgrade-notice").ShouldBeEmpty();
        page.FindAll("[role=progressbar]").ShouldBeEmpty();
    }

    [Fact]
    public void Enrolled_LinksEveryLessonAndContinuesWithTheNextOne()
    {
        UseCulture("en");
        var course = Course(enrolled: true, completedLessonTitles: ["How this programme works"]);
        UseCourse(course);
        var nextLesson = course.Modules[0].Lessons[1];

        var page = RenderPage();

        page.FindAll("a.lesson-item[href^='/lessons/']").Count.ShouldBe(3);
        page.Find(".lesson-item--next").GetAttribute("href").ShouldBe($"/lessons/{nextLesson.Id}");
        page.Find(".course-cta__next-title").TextContent.ShouldBe(nextLesson.Title);
        var action = page.Find("a.course-cta__action");
        action.GetAttribute("href").ShouldBe($"/lessons/{nextLesson.Id}");
        action.TextContent.ShouldContain("Continue training");
        var ring = page.Find(".course-cta [role=progressbar]");
        ring.GetAttribute("aria-valuenow").ShouldBe("33");
        ring.GetAttribute("aria-label").ShouldBe("1 of 3 lessons done (33%)");
        page.FindAll(".course-outline__note").ShouldBeEmpty();
    }

    [Fact]
    public void Enrolled_OpensTheModuleOfTheNextLesson()
    {
        UseCulture("en");
        UseCourse(Course(enrolled: true, completedLessonTitles: ["How this programme works", "Workout A: Full body"]));

        var page = RenderPage();

        var modules = page.FindAll("details.module");
        modules[0].HasAttribute("open").ShouldBeFalse();
        modules[0].ClassList.ShouldContain("module--done");
        modules[1].HasAttribute("open").ShouldBeTrue();
        page.FindAll(".module__progress").Select(progress => progress.TextContent).ShouldBe(["2/2 done", "0/1 done"]);
    }

    [Fact]
    public void Enrolled_NothingDoneYet_StartsTraining()
    {
        UseCulture("en");
        UseCourse(Course(enrolled: true));

        var page = RenderPage();

        page.Find("a.course-cta__action").TextContent.ShouldContain("Start training");
        page.FindAll("details.module")[0].HasAttribute("open").ShouldBeTrue();
    }

    [Fact]
    public void Completed_OffersToTrainAgainFromTheFirstLesson()
    {
        UseCulture("en");
        var course = Course(enrolled: true, completedLessonTitles: ["How this programme works", "Workout A: Full body", "Hinge technique"])
            with { IsCompleted = true };
        UseCourse(course);

        var page = RenderPage();

        page.Find(".completed-badge").TextContent.ShouldContain("Programme completed");
        var action = page.Find("a.course-cta__action");
        action.TextContent.ShouldContain("Train again");
        action.GetAttribute("href").ShouldBe($"/lessons/{course.Modules[0].Lessons[0].Id}");
        page.FindAll(".course-cta__next").ShouldBeEmpty();
        page.FindAll(".lesson-item.completed").Count.ShouldBe(3);
    }

    [Fact]
    public void PlanTooLow_ShowsTheUpgradeNoticeLinkingToPricing()
    {
        UseCulture("en");
        UseCourse(Course(tier: SubscriptionTier.Pro));

        var page = RenderPage();

        page.Find(".enroll-upgrade-notice").TextContent.ShouldContain("Included in the Pro plan and above.");
        page.Find("a.course-cta__action").GetAttribute("href").ShouldBe("/pricing");
        page.FindAll(".course-cta__enrol").ShouldBeEmpty();
        page.Find(".course-hero__tier").ClassList.ShouldContain("tier-pro");
    }

    [Fact]
    public void Enrol_Succeeds_ReloadsTheCourseAsEnrolled()
    {
        UseCulture("en");
        _courseService.GetCourseAsync(CourseId, Arg.Any<string>()).Returns(Course(), Course(enrolled: true));
        _courseService.EnrollAsync(CourseId).Returns(new EnrollmentResultDto(true, null, Guid.NewGuid()));
        var page = RenderPage();

        page.Find(".course-cta__enrol").Click();

        page.Find("a.course-cta__action").TextContent.ShouldContain("Start training");
        page.FindAll("a.lesson-item[href]").Count.ShouldBe(3);
    }

    [Fact]
    public void Enrol_Refused_ShowsTheServerMessageAndThePlansLink()
    {
        UseCulture("en");
        UseCourse(Course());
        _courseService.EnrollAsync(CourseId).Returns(new EnrollmentResultDto(false, "Your plan allows one course.", null));
        var page = RenderPage();

        page.Find(".course-cta__enrol").Click();

        var notice = page.Find(".enroll-error-notice");
        notice.TextContent.ShouldContain("Your plan allows one course.");
        notice.QuerySelector("a")!.GetAttribute("href").ShouldBe("/pricing");
    }

    [Fact]
    public void Guest_GetsSignUpAndLogInInPlaceOfJoin()
    {
        UseCulture("en");
        _authorization.SetNotAuthorized();
        UseCourse(Course(tier: SubscriptionTier.Pro));
        var review = new CourseReviewDto(Guid.NewGuid(), "Maria", 5, "Great plan", DateTimeOffset.UtcNow);
        _courseService.GetReviewsAsync(CourseId)
            .Returns(new CourseReviewsResponseDto([review], new CourseRatingSummaryDto(5, 1), null));

        var page = RenderPage();

        var signUp = page.Find("a.course-cta__signup");
        signUp.TextContent.Trim().ShouldBe("Sign up free to start");
        signUp.GetAttribute("href").ShouldBe("/register");
        page.Find("a.course-cta__login").GetAttribute("href").ShouldBe($"/login?returnUrl=%2Fcourses%2F{CourseId}");
        page.FindAll(".course-cta__enrol").ShouldBeEmpty();
        page.FindAll(".enroll-upgrade-notice").ShouldBeEmpty();
        page.FindAll("a.lesson-item[href]").ShouldBeEmpty();
        page.FindAll(".review-form, .review-open").ShouldBeEmpty();
        page.Find(".review-list .review-body").TextContent.ShouldBe("Great plan");
        _subscriptionService.DidNotReceive().GetStatusAsync();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Dock_KeepsRoomForTheAssistantLauncherOnlyForMembers(bool signedIn, bool expectGuestDock)
    {
        // The assistant launcher sits at the dock's right end, but only signed-in users get it.
        UseCulture("en");
        if (!signedIn)
            _authorization.SetNotAuthorized();
        UseCourse(Course());

        var page = RenderPage();

        page.Find("aside.course-cta").ClassList.Contains("course-cta--guest").ShouldBe(expectGuestDock);
    }

    [Fact]
    public void Guest_Greek_SignUpTextIsGreek()
    {
        UseCulture("el");
        _authorization.SetNotAuthorized();
        UseCourse(Course());

        var page = RenderPage();

        page.Find("a.course-cta__signup").TextContent.Trim().ShouldBe("Κάνε δωρεάν εγγραφή για να ξεκινήσεις");
        page.Find("a.course-cta__login").TextContent.Trim().ShouldBe("Σύνδεση");
    }

    // ---- Load states --------------------------------------------------------------------------

    [Fact]
    public void LoadFails_ShowsTheErrorWithTryAgainAndBackLinks()
    {
        UseCulture("en");
        _courseService.GetCourseAsync(CourseId, Arg.Any<string>())
            .Returns(Task.FromException<CourseDetailDto?>(new HttpRequestException("boom")));

        var page = RenderPage();

        page.Find(".course-state .text-danger").TextContent.ShouldNotBeNullOrWhiteSpace();
        page.Find(".course-state button").TextContent.ShouldNotBeNullOrWhiteSpace();
        page.Find(".course-state a").GetAttribute("href").ShouldBe("/courses");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private IRenderedComponent<CourseDetail> RenderPage() =>
        Render<CourseDetail>(parameters => parameters.Add(page => page.CourseId, CourseId));

    private void UseCourse(CourseDetailDto course) =>
        _courseService.GetCourseAsync(CourseId, Arg.Any<string>()).Returns(course);

    private void UsePlan(SubscriptionTier tier) =>
        _subscriptionService.GetStatusAsync().Returns(
            new UserSubscriptionStatusDto(tier, tier.ToString(), DateTime.UtcNow, null, true, null));

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    /// <summary>Two modules, three lessons, 62 minutes: a reading, a 6-exercise workout and a 1-exercise video.</summary>
    private static ModuleDto[] DefaultModules() =>
    [
        Module("Weeks 1–2: Learn the patterns", "<p>Build clean technique before you add load.</p>",
            Lesson("How this programme works", minutes: 10),
            Lesson("Workout A: Full body", minutes: 40, exerciseCount: 6)),
        Module("Weeks 3–4: Add load", "Progress in small, safe steps.",
            Lesson("Hinge technique", minutes: 12, exerciseCount: 1, contentType: ContentType.Video))
    ];

    private static CourseDetailDto Course(
        bool enrolled = false,
        string[]? completedLessonTitles = null,
        SubscriptionTier tier = SubscriptionTier.Free,
        params ModuleDto[] modules)
    {
        var completedTitles = completedLessonTitles ?? [];
        var markedModules = (modules.Length == 0 ? DefaultModules() : modules)
            .Select(module => module with
            {
                Lessons = module.Lessons.Select(lesson => lesson with { IsCompleted = completedTitles.Contains(lesson.Title) }).ToList()
            })
            .ToList();
        var totalLessons = markedModules.Sum(module => module.Lessons.Count);
        var completedLessons = completedTitles.Length;
        var progressPercent = totalLessons == 0 ? 0 : Math.Round(100.0 * completedLessons / totalLessons, 1);

        return new CourseDetailDto(
            CourseId,
            "Strength Foundations",
            "<p>Learn the essential lifts.</p>",
            IsEnrolled: enrolled,
            IsCompleted: false,
            completedLessons,
            totalLessons,
            progressPercent,
            markedModules,
            tier,
            CourseLevel.Intermediate,
            FitnessGoal.MuscleGain,
            Equipment: "dumbbells,bands",
            EstimatedWeeks: 6,
            SessionsPerWeek: 3,
            CoverImageUrl: "/images/exercises/barbell-squat/0.webp",
            CategoryName: "Strength");
    }

    private static ModuleDto Module(string title, string? description, params LessonSummaryDto[] lessons) =>
        new(Guid.NewGuid(), title, description, SortOrder: 1, lessons.Select((lesson, index) => lesson with { SortOrder = index + 1 }).ToList());

    private static LessonSummaryDto Lesson(
        string title,
        int? minutes = 10,
        int exerciseCount = 0,
        ContentType contentType = ContentType.Text) =>
        new(Guid.NewGuid(), title, (int)contentType, minutes, SortOrder: 1, IsCompleted: false, exerciseCount);
}
