using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit component tests for LessonViewer (TEST-4) — the completion flow, load/error
/// states, and the private markdown/YouTube-embed rendering logic that no other test in
/// the suite exercises.
/// </summary>
public class LessonViewerTests : BunitContext
{
    private readonly ICourseConsumer _courseService = Substitute.For<ICourseConsumer>();
    private readonly IAuthService _authService = Substitute.For<IAuthService>();
    private readonly Guid _lessonId = Guid.NewGuid();

    public LessonViewerTests()
    {
        Services.AddSingleton(_courseService);
        Services.AddSingleton(_authService);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        Services.AddSingleton(authStateProvider);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        _authService.GetTokenAsync(Arg.Any<ClaimsPrincipal>()).Returns((string?)null);
    }

    private static LessonDetailDto Lesson(
        int contentType = 1,
        string? content = "Hello",
        bool isCompleted = false,
        Guid? previousLessonId = null,
        Guid? nextLessonId = null,
        IReadOnlyList<LessonExerciseDto>? exercises = null) => new(
        Guid.NewGuid(),
        "Intro to Testing",
        contentType,
        content,
        PdfPath: null,
        DurationMinutes: 10,
        isCompleted,
        Guid.NewGuid(),
        "Module 1",
        Guid.NewGuid(),
        "Course 1",
        previousLessonId,
        nextLessonId,
        exercises);

    private static LessonExerciseDto Prescription(string slug, string name, int? sets, string? reps) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        slug,
        name,
        Equipment: "body only",
        PrimaryMuscles: ["chest"],
        sets,
        reps,
        RestSeconds: 60,
        Notes: null,
        Image0Url: $"/images/exercises/{slug}/0.webp",
        Image1Url: $"/images/exercises/{slug}/1.webp",
        Instructions: ["Brace", "Lower"]);

    /// <summary>A course whose only module holds the given lessons in this order; one lesson is done.</summary>
    private static CourseDetailDto Outline(LessonDetailDto lesson, params LessonSummaryDto[] lessons) => new(
        lesson.CourseId,
        lesson.CourseTitle,
        Description: null,
        IsEnrolled: true,
        IsCompleted: false,
        CompletedLessons: 1,
        TotalLessons: lessons.Length,
        ProgressPercent: Math.Round(100.0 / lessons.Length, 1),
        Modules: [new ModuleDto(lesson.ModuleId, lesson.ModuleTitle, null, 1, [.. lessons])],
        RequiredTier: SubscriptionTier.Free);

    private static LessonSummaryDto Summary(Guid id, string title, int sortOrder) =>
        new(id, title, ContentType: 1, DurationMinutes: 10, sortOrder, IsCompleted: sortOrder == 1);

    [Fact]
    public void Loading_ShowsSpinnerBeforeLessonResolves()
    {
        var tcs = new TaskCompletionSource<LessonDetailDto?>();
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(tcs.Task);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".pulse-loader").ShouldNotBeNull();
    }

    [Fact]
    public void LessonMissing_ShowsNotFoundError()
    {
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns((LessonDetailDto?)null);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".text-danger").TextContent.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void LoadThrows_ShowsFailedToLoadError()
    {
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>())
            .Returns(Task.FromException<LessonDetailDto?>(new HttpRequestException("boom")));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".text-danger").ShouldNotBeNull();
    }

    [Fact]
    public void LessonLoaded_RendersTitleAndBreadcrumb()
    {
        var lesson = Lesson();
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find("h1").TextContent.ShouldBe(lesson.Title);
        cut.Find(".module-name").TextContent.ShouldBe(lesson.ModuleTitle);
    }

    [Fact]
    public void MarkComplete_Success_ReplacesButtonWithCompletedIndicator()
    {
        var lesson = Lesson(isCompleted: false);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.CompleteLessonAsync(_lessonId).Returns(
            new LessonCompletionResultDto(true, "ok", CompletedLessons: 2, TotalLessons: 5, ProgressPercent: 40, CourseCompleted: false));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));
        cut.Find(".btn-success").Click();

        cut.Find(".completed-indicator").ShouldNotBeNull();
        cut.Find(".progress-update").TextContent.ShouldContain("2");
    }

    [Fact]
    public void MarkComplete_Failure_ShowsCompletionError()
    {
        var lesson = Lesson(isCompleted: false);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.CompleteLessonAsync(_lessonId).Returns((LessonCompletionResultDto?)null);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));
        cut.Find(".btn-success").Click();

        cut.Find(".lesson-actions .text-danger").ShouldNotBeNull();
    }

    [Fact]
    public void AlreadyCompleted_ShowsIndicatorNotButton()
    {
        var lesson = Lesson(isCompleted: true);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".completed-indicator").ShouldNotBeNull();
        cut.FindAll(".btn-success").ShouldBeEmpty();
    }

    [Fact]
    public void VideoLesson_YouTubeUrl_RendersEmbedIframe()
    {
        var lesson = Lesson(contentType: 2, content: "https://www.youtube.com/watch?v=abc123XYZ");
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        var iframe = cut.Find(".video-container iframe");
        iframe.GetAttribute("src").ShouldBe("https://www.youtube.com/embed/abc123XYZ");
    }

    [Fact]
    public void VideoLesson_UploadedFile_RendersVideoElementNotIframe()
    {
        var lesson = Lesson(contentType: 2, content: "/media/lesson-video.mp4");
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.FindAll(".video-container iframe").ShouldBeEmpty();
        cut.Find(".video-container video").ShouldNotBeNull();
    }

    [Fact]
    public void TextLesson_MarkdownContent_RendersHeadingAndListItems()
    {
        var lesson = Lesson(contentType: 1, content: "# Heading\n- one\n- two");
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        var content = cut.Find(".text-content");
        content.InnerHtml.ShouldContain("<h1>Heading</h1>");
        content.QuerySelectorAll("li").Length.ShouldBe(2);
    }

    [Fact]
    public void TextLesson_AlreadyHtmlContent_PassesThroughUnchanged()
    {
        var lesson = Lesson(contentType: 1, content: "<p><strong>bold</strong> text</p>");
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".text-content").InnerHtml.ShouldContain("<strong>bold</strong>");
    }

    [Fact]
    public void NoNextLesson_ShowsBackToCourseButton()
    {
        var lesson = Lesson(nextLessonId: null);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".lesson-navigation .btn-primary, .lesson-navigation .btn-secondary:last-child")
            .ShouldNotBeNull();
        cut.FindAll(".lesson-navigation .btn-primary").ShouldBeEmpty();
    }

    [Fact]
    public void HasNextLesson_ShowsNextLessonButton()
    {
        var lesson = Lesson(nextLessonId: Guid.NewGuid());
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".lesson-navigation .btn-primary").ShouldNotBeNull();
    }

    [Fact]
    public void TextLesson_NumberedList_ClosesWithOl()
    {
        var lesson = Lesson(contentType: 1, content: "1. Brace\n2. Lift");
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        var html = cut.Find(".text-content").InnerHtml;
        html.ShouldContain("</ol>");
        html.ShouldNotContain("</ul>");
    }

    [Fact]
    public void TextLesson_TitleHeading_IsShownOnceAsThePageHeading()
    {
        var lesson = Lesson(contentType: 1, content: "# Intro to Testing\n\nWhy tests matter.");
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.FindAll("h1").Count.ShouldBe(1);
        cut.Find(".text-content").InnerHtml.Trim().ShouldBe("<p>Why tests matter.</p>");
    }

    [Fact]
    public void VideoLesson_HasNoInlineScriptHandler()
    {
        var lesson = Lesson(contentType: 2, content: "/media/lesson-video.mp4");
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        // The CSP blocks inline handlers, so oncontextmenu="return false;" only logged an error.
        cut.Find(".video-container video").HasAttribute("oncontextmenu").ShouldBeFalse();
    }

    [Fact]
    public void WorkoutLesson_RendersOneNumberedExerciseCardPerPrescription()
    {
        var lesson = Lesson(exercises:
        [
            Prescription("pushups", "Push-up", sets: 3, reps: "6–10"),
            Prescription("plank", "Plank", sets: 2, reps: "30 s")
        ]);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        var cards = cut.FindAll(".lesson-workout__list > li .exercise-card");
        cards.Count.ShouldBe(2);
        cards.Select(card => card.QuerySelector(".exercise-card__position")?.TextContent).ShouldBe(["01", "02"]);
        cards.Select(card => card.QuerySelector(".exercise-card__name")?.TextContent.Trim()).ShouldBe(["Push-up", "Plank"]);
        cards[1].QuerySelectorAll(".exercise-card__dose dd")[1].TextContent.ShouldBe("30 s");
        cut.Find(".lesson-workout__summary").TextContent.ShouldContain("5"); // 3 + 2 sets in total
        cut.Find(".lesson-meta__workout-link").GetAttribute("href").ShouldBe($"/lessons/{_lessonId}#lesson-workout");
    }

    [Fact]
    public void LessonWithoutExercises_RendersNoWorkoutSection()
    {
        var lesson = Lesson();
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.FindAll(".lesson-workout").ShouldBeEmpty();
        cut.FindAll(".lesson-meta__workout-link").ShouldBeEmpty();
    }

    [Fact]
    public void CourseOutline_ShowsLessonPositionProgressAndNeighbourTitles()
    {
        var previousId = Guid.NewGuid();
        var nextId = Guid.NewGuid();
        var lesson = Lesson(previousLessonId: previousId, nextLessonId: nextId);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.GetCourseAsync(lesson.CourseId, Arg.Any<string>()).Returns(Outline(lesson,
            Summary(previousId, "Warm-up basics", 1),
            Summary(lesson.Id, lesson.Title, 2),
            Summary(nextId, "Workout B", 3)));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        var position = cut.Find(".lesson-header__position").TextContent;
        position.ShouldContain("2");
        position.ShouldContain("3");
        cut.Find(".lesson-progress__track").GetAttribute("aria-valuenow").ShouldBe("33");
        cut.Find(".lesson-navigation .btn-secondary .lesson-navigation__title").TextContent.ShouldBe("Warm-up basics");
        cut.Find(".lesson-navigation .btn-primary .lesson-navigation__title").TextContent.ShouldBe("Workout B");
    }

    [Fact]
    public void CourseOutlineMissing_LessonStillRenders()
    {
        var lesson = Lesson(nextLessonId: Guid.NewGuid());
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.GetCourseAsync(lesson.CourseId, Arg.Any<string>()).Returns((CourseDetailDto?)null);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find("h1").TextContent.ShouldBe(lesson.Title);
        cut.FindAll(".lesson-progress").ShouldBeEmpty();
        cut.FindAll(".lesson-header__position").ShouldBeEmpty();
        cut.FindAll(".lesson-navigation .btn-primary .lesson-navigation__title").ShouldBeEmpty();
    }

    [Fact]
    public void CourseOutlineThrows_LessonStillRenders()
    {
        var lesson = Lesson();
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.GetCourseAsync(lesson.CourseId, Arg.Any<string>())
            .Returns(Task.FromException<CourseDetailDto?>(new HttpRequestException("boom")));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find("h1").TextContent.ShouldBe(lesson.Title);
        cut.FindAll(".lesson-progress").ShouldBeEmpty();
        cut.Find(".btn-success").ShouldNotBeNull();
    }

    [Fact]
    public void MarkComplete_UpdatesTheProgressBarAndKeepsTheBarDocked()
    {
        var lesson = Lesson(isCompleted: false);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.GetCourseAsync(lesson.CourseId, Arg.Any<string>()).Returns(Outline(lesson,
            Summary(Guid.NewGuid(), "Warm-up basics", 1),
            Summary(lesson.Id, lesson.Title, 2),
            Summary(Guid.NewGuid(), "Workout B", 3),
            Summary(Guid.NewGuid(), "Workout C", 4),
            Summary(Guid.NewGuid(), "Workout D", 5)));
        _courseService.CompleteLessonAsync(_lessonId).Returns(
            new LessonCompletionResultDto(true, "ok", CompletedLessons: 2, TotalLessons: 5, ProgressPercent: 40, CourseCompleted: false));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));
        cut.Find(".lesson-progress__track").GetAttribute("aria-valuenow").ShouldBe("20");
        cut.Find(".lesson-actions").ClassList.ShouldContain("sticky-bottom-bar");

        cut.Find(".btn-success").Click();

        cut.Find(".lesson-progress__track").GetAttribute("aria-valuenow").ShouldBe("40");
        // The bar stays docked so the confirmation is seen, and Next becomes the main action.
        cut.Find(".lesson-actions").ClassList.ShouldContain("sticky-bottom-bar");
        cut.Find(".lesson-navigation").ClassList.ShouldContain("lesson-navigation--ready");
    }

    [Fact]
    public void LessonCompletedEarlier_ShowsTheCompletionBarInTheFlow()
    {
        var lesson = Lesson(isCompleted: true);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".lesson-actions").ClassList.ShouldNotContain("sticky-bottom-bar");
    }

    [Fact]
    public void MarkComplete_DocksOnlyTheIndicatorAndNextLesson()
    {
        var nextLessonId = Guid.NewGuid();
        var lesson = Lesson(isCompleted: false, nextLessonId: nextLessonId);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.CompleteLessonAsync(_lessonId).Returns(
            new LessonCompletionResultDto(true, "ok", CompletedLessons: 4, TotalLessons: 5, ProgressPercent: 80,
                CourseCompleted: false));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));
        cut.Find(".btn-success").Click();

        var dock = cut.Find(".lesson-actions--docked");
        dock.QuerySelector(".completed-indicator").ShouldNotBeNull();
        dock.QuerySelector(".lesson-actions__next")!.GetAttribute("href").ShouldBe($"/lessons/{nextLessonId}");
        dock.QuerySelector(".progress-update").ShouldBeNull();
        dock.QuerySelector(".course-celebration").ShouldBeNull();
        cut.Find(".progress-update").ShouldNotBeNull();
    }

    [Theory]
    [InlineData(true, false, "/my-certificates")]
    [InlineData(true, true, "/my-certificates")]
    [InlineData(false, false, "/pricing")]
    public void MarkComplete_LastWorkout_CelebratesInTheDockWithTheCertificateOrTheUpgrade(
        bool certificateIncluded, bool certificatePending, string expectedHref)
    {
        // On a phone the dock is in view when the last workout is ticked off; the page end is far below.
        var lesson = Lesson(isCompleted: false, nextLessonId: null);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.CompleteLessonAsync(_lessonId).Returns(
            new LessonCompletionResultDto(true, "ok", CompletedLessons: 5, TotalLessons: 5, ProgressPercent: 100,
                CourseCompleted: true, CertificatePending: certificatePending, CertificateIncluded: certificateIncluded));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));
        cut.Find(".btn-success").Click();

        var celebration = cut.Find(".lesson-actions--docked .course-celebration");
        celebration.QuerySelector(".course-celebration__title")!.TextContent.ShouldBe(LessonRes.CourseCompleted);
        celebration.QuerySelector(".course-celebration__certificate")!.GetAttribute("href").ShouldBe(expectedHref);
        celebration.QuerySelector(".course-celebration__course")!.GetAttribute("href").ShouldBe($"/courses/{lesson.CourseId}");
    }

    [Fact]
    public void MarkComplete_LastWorkoutWithoutCertificatePlan_SaysCertificatesComeWithPro()
    {
        var lesson = Lesson(isCompleted: false, nextLessonId: null);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);
        _courseService.CompleteLessonAsync(_lessonId).Returns(
            new LessonCompletionResultDto(true, "ok", CompletedLessons: 5, TotalLessons: 5, ProgressPercent: 100,
                CourseCompleted: true));

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));
        cut.Find(".btn-success").Click();

        cut.Find(".course-celebration__note").TextContent.ShouldBe(LessonRes.CertificateWithPro);
        cut.Find(".course-celebration__certificate").TextContent.ShouldBe(LessonRes.UpgradeToPro);
    }

    [Fact]
    public void NavigationLinks_PointAtTheNeighbourLessonsAndTheCourse()
    {
        var previousId = Guid.NewGuid();
        var lesson = Lesson(previousLessonId: previousId, nextLessonId: null);
        _courseService.GetLessonAsync(_lessonId, Arg.Any<string>()).Returns(lesson);

        var cut = Render<LessonViewer>(p => p.Add(c => c.LessonId, _lessonId));

        cut.Find(".lesson-navigation .btn-secondary").GetAttribute("href").ShouldBe($"/lessons/{previousId}");
        cut.Find(".lesson-navigation .btn-secondary:last-child").GetAttribute("href").ShouldBe($"/courses/{lesson.CourseId}");
        cut.Find(".lesson-nav-top__course").GetAttribute("href").ShouldBe($"/courses/{lesson.CourseId}");
    }
}
