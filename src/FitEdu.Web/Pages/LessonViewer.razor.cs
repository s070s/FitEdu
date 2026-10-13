using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Interfaces;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class LessonViewer
{
    /// <summary>Id of the exercise list, the target of the exercise-count link in the header.</summary>
    private const string WorkoutSectionId = "lesson-workout";

    private const int MaxPercent = 100;

    private const string MyCertificatesPath = "/my-certificates";
    private const string PricingPath = "/pricing";

    [Parameter]
    public Guid LessonId
    {
        get; set;
    }

    [Inject] private ICourseConsumer CourseService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private ILogger<LessonViewer> _logger { get; set; } = default!;

    private LessonDetailDto? _lesson;
    private LessonCompletionResultDto? _completionResult;
    private List<LessonSummaryDto> _courseLessons = [];
    private CourseProgress? _progress;
    private bool _loading = true;
    private bool _completing;
    private string? _error;
    private bool _loadFailed;
    private string? _completionError;
    private bool _mediaError;
    private string? _assetToken;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    // Seed text repeats the lesson title as its first heading; the page shows it once, as the h1.
    private string ContentHtml => LessonMarkdown.Render(_lesson?.Content, _lesson?.Title);

    private IReadOnlyList<LessonExerciseDto> Exercises => _lesson?.Exercises ?? [];

    private int TotalSets => Exercises.Sum(exercise => exercise.Sets ?? 0);

    // The completion bar sticks to the bottom of the screen while the lesson is still to do, and right
    // after a completion so its confirmation and the next-lesson button stay in view (the progress
    // details sit in the flow below it). A lesson completed earlier shows the bar in the flow.
    private bool IsActionBarDocked => _lesson is { IsCompleted: false } || _completionResult is not null;

    // After the last workout: the certificate when the plan includes one, otherwise the upgrade to Pro.
    private bool CertificateIncluded => _completionResult?.CertificateIncluded == true;

    private string CertificateHref => CertificateIncluded ? MyCertificatesPath : PricingPath;

    private string CertificateLinkText => CertificateIncluded ? LessonRes.ViewCertificate : LessonRes.UpgradeToPro;

    private string CertificateNote
    {
        get
        {
            if (_completionResult?.CertificatePending == true)
                return LessonRes.CertificatePending;

            return CertificateIncluded ? LessonRes.CertificateReady : LessonRes.CertificateWithPro;
        }
    }

    /// <summary>The lesson's 1-based place in the course ("Lesson 3 of 12"), or 0 when the outline did not load.</summary>
    private int LessonNumber => _lesson is null ? 0 : IndexOfLesson(_lesson.Id) + 1;

    private string WorkoutHref => $"{LessonHref(LessonId)}#{WorkoutSectionId}";

    protected override async Task OnParametersSetAsync()
    {
        await LoadLesson();
    }

    private async Task LoadLesson()
    {
        _loading = true;
        _error = null;
        _loadFailed = false;
        _completionResult = null;
        _completionError = null;
        _mediaError = false;
        _courseLessons = [];
        _progress = null;

        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            _assetToken = await AuthService.CreateLessonAssetTokenAsync(authState.User, LessonId);
            _lesson = await CourseService.GetLessonAsync(LessonId, CurrentLang);
            if (_lesson is null)
                _error = LessonRes.LessonNotFoundOrNotEnrolled;
            else
                await LoadCourseOutline(_lesson.CourseId);
        }
        catch (Exception ex)
        {
            _error = LessonRes.FailedToLoadLesson;
            _loadFailed = true;
            LogLessonLoadFailed(_logger, ex, LessonId);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Loads the course outline for the lesson position, the progress bar and the neighbouring lessons'
    /// titles. These are extras: when the outline is missing or fails to load, the lesson still shows.
    /// </summary>
    private async Task LoadCourseOutline(Guid courseId)
    {
        try
        {
            var course = await CourseService.GetCourseAsync(courseId, CurrentLang);
            if (course is null)
                return;

            _courseLessons = course.Modules
                .OrderBy(module => module.SortOrder)
                .SelectMany(module => module.Lessons.OrderBy(lesson => lesson.SortOrder))
                .ToList();
            _progress = CourseProgress.From(course.CompletedLessons, course.TotalLessons, course.ProgressPercent);
        }
        catch (Exception ex)
        {
            LogCourseOutlineFailed(_logger, ex, courseId);
        }
    }

    private async Task MarkComplete()
    {
        _completing = true;
        _completionError = null;
        try
        {
            var result = await CourseService.CompleteLessonAsync(LessonId);
            if (result?.Success == true && _lesson is not null)
            {
                _completionResult = result;
                _progress = CourseProgress.From(result.CompletedLessons, result.TotalLessons, result.ProgressPercent);
                _lesson = _lesson with
                {
                    IsCompleted = true
                };
            }
            else
            {
                _completionError = LessonRes.FailedToSaveCompletion;
            }
        }
        catch (Exception ex)
        {
            _completionError = LessonRes.FailedToSaveCompletion;
            LogCompletionFailed(_logger, ex, LessonId);
        }
        finally
        {
            _completing = false;
        }
    }

    private void OnMediaError()
    {
        _mediaError = true;
    }

    private static string LessonHref(Guid lessonId) => $"/lessons/{lessonId}";

    private static string CourseHref(Guid courseId) => $"/courses/{courseId}";

    private int IndexOfLesson(Guid lessonId)
    {
        for (var index = 0; index < _courseLessons.Count; index++)
        {
            if (_courseLessons[index].Id == lessonId)
                return index;
        }

        return -1;
    }

    /// <summary>The title of a neighbouring lesson, or null when there is none or the outline did not load.</summary>
    private string? LessonTitle(Guid? lessonId)
    {
        if (lessonId is null)
            return null;

        var index = IndexOfLesson(lessonId.Value);
        return index < 0 ? null : _courseLessons[index].Title;
    }

    private static bool IsVideo(LessonDetailDto lesson) => lesson.ContentType == (int)ContentType.Video;

    private static bool IsPdf(LessonDetailDto lesson) => lesson.ContentType == (int)ContentType.Pdf;

    /// <summary>
    /// Returns true when the content is a YouTube URL rather than an uploaded file path.
    /// </summary>
    private static bool IsYouTubeUrl(string? url) =>
        !string.IsNullOrEmpty(url) &&
        (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
          url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Absolute URL used by the &lt;video&gt; element to stream the uploaded lesson video
    /// through the authorized asset endpoint.
    /// </summary>
    private string VideoAssetUrl
    {
        get
        {
            var apiBase = Navigation.BaseUri.TrimEnd('/');
            var url = $"{apiBase}/api/lessons/{LessonId}/asset?type=video";
            if (!string.IsNullOrEmpty(_assetToken))
                url += $"&assetToken={Uri.EscapeDataString(_assetToken)}";
            return url;
        }
    }

    /// <summary>
    /// Absolute URL used by the &lt;iframe&gt; element to stream the uploaded lesson PDF
    /// through the authorized asset endpoint.
    /// </summary>
    private string PdfAssetUrl
    {
        get
        {
            var apiBase = Navigation.BaseUri.TrimEnd('/');
            var url = $"{apiBase}/api/lessons/{LessonId}/asset?type=pdf";
            if (!string.IsNullOrEmpty(_assetToken))
                url += $"&assetToken={Uri.EscapeDataString(_assetToken)}";
            return url;
        }
    }

    /// <summary>
    /// Converts YouTube watch/short/youtu.be URLs into embed-friendly URLs.
    /// Returns the original URL unchanged if it's already an embed URL or non-YouTube.
    /// </summary>
    private static string? ToEmbedUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        // Already an embed URL
        if (url.Contains("/embed/", StringComparison.OrdinalIgnoreCase))
            return url;

        // https://www.youtube.com/watch?v=VIDEO_ID or https://youtube.com/watch?v=VIDEO_ID
        if (url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var videoId = query["v"];
                if (!string.IsNullOrEmpty(videoId))
                    return $"https://www.youtube.com/embed/{videoId}";
            }
        }

        // https://youtu.be/VIDEO_ID
        if (url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var videoId = uri.AbsolutePath.TrimStart('/');
                if (!string.IsNullOrEmpty(videoId))
                    return $"https://www.youtube.com/embed/{videoId}";
            }
        }

        // https://www.youtube.com/shorts/VIDEO_ID
        if (url.Contains("/shorts/", StringComparison.OrdinalIgnoreCase))
        {
            var idx = url.IndexOf("/shorts/", StringComparison.OrdinalIgnoreCase);
            var videoId = url[(idx + 8)..].Split('?')[0].Split('/')[0];
            if (!string.IsNullOrEmpty(videoId))
                return $"https://www.youtube.com/embed/{videoId}";
        }

        return url;
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load lesson {LessonId}.")]
    private static partial void LogLessonLoadFailed(ILogger logger, Exception exception, Guid lessonId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load the outline of course {CourseId}.")]
    private static partial void LogCourseOutlineFailed(ILogger logger, Exception exception, Guid courseId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mark lesson {LessonId} as complete.")]
    private static partial void LogCompletionFailed(ILogger logger, Exception exception, Guid lessonId);

    /// <summary>Course progress as the bar shows it: lessons done, lessons in total and a whole percent.</summary>
    private sealed record CourseProgress(int CompletedLessons, int TotalLessons, int Percent)
    {
        public static CourseProgress From(int completedLessons, int totalLessons, double percent) =>
            new(completedLessons, totalLessons, (int)Math.Round(Math.Clamp(percent, 0, MaxPercent)));
    }
}
