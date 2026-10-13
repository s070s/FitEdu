using Microsoft.AspNetCore.Components;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Domain.Enums;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class CourseDetail
{
    // "What you'll train" lists the first module summaries, enough to scan at a glance.
    private const int MaxOutcomes = 4;
    private const string LessonPathPrefix = "/lessons/";

    [Parameter]
    public Guid CourseId
    {
        get; set;
    }

    [Inject] private ICourseConsumer CourseService { get; set; } = default!;
    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private ILogger<CourseDetail> _logger { get; set; } = default!;

    private CourseDetailDto? _course;
    private SubscriptionTier _userTier = SubscriptionTier.Free;
    private bool _loading = true;
    private bool _enrolling;
    private string? _error;
    private string? _enrollError;

    // View data prepared once per load (PrepareView).
    private List<ModuleDto> _modules = [];
    private List<string> _outcomes = [];
    private IReadOnlyList<string> _equipmentLabels = [];
    private LessonSummaryDto? _firstLesson;
    private LessonSummaryDto? _nextLesson;
    private Guid? _openModuleId;
    private int _totalMinutes;

    private CourseReviewsResponseDto? _reviewsData;
    private bool _showReviewForm;
    private bool _submittingReview;
    private int _formRating = 5;
    private string _formBody = string.Empty;
    private string? _reviewError;

    /// <summary>The kinds of lesson the outline tells apart, each with its own icon.</summary>
    private enum LessonKind
    {
        Reading,
        Video,
        Pdf,
        Workout
    }

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    private bool NeedsUpgrade => _course is { IsEnrolled: false } && _userTier < _course.RequiredTier;

    private int ProgressPercentRounded => _course is null ? 0 : (int)Math.Round(_course.ProgressPercent);

    /// <summary>Where the main button leads an enrolled member: the next lesson, or the first once all are done.</summary>
    private LessonSummaryDto? ActionLesson => _nextLesson ?? _firstLesson;

    private string ActionLabel
    {
        get
        {
            if (_nextLesson is null)
                return CourseRes.TrainAgain;

            return _course?.CompletedLessons > 0 ? CourseRes.ContinueTraining : CourseRes.StartTraining;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        var tierTask = SubscriptionService.GetStatusAsync();
        await Task.WhenAll(LoadCourse(), LoadReviewsAsync(), tierTask);

        var status = await tierTask;
        if (status is not null)
            _userTier = status.Tier;
    }

    private async Task LoadReviewsAsync()
    {
        try
        {
            _reviewsData = await CourseService.GetReviewsAsync(CourseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load reviews for course {CourseId}.", CourseId);
        }
    }

    private void OpenReviewForm()
    {
        _formRating = _reviewsData?.MyReview?.Rating ?? 5;
        _formBody = _reviewsData?.MyReview?.Body ?? string.Empty;
        _reviewError = null;
        _showReviewForm = true;
    }

    private void CloseReviewForm() => _showReviewForm = false;

    private async Task SubmitReviewAsync()
    {
        if (string.IsNullOrWhiteSpace(_formBody))
            return;

        _submittingReview = true;
        _reviewError = null;
        try
        {
            var result = await CourseService.SaveReviewAsync(CourseId, new SaveCourseReviewRequest(_formRating, _formBody.Trim()));
            if (result is not null)
            {
                _showReviewForm = false;
                await LoadReviewsAsync();
            }
            else
            {
                _reviewError = ReviewRes.SubmitError;
            }
        }
        catch (Exception ex)
        {
            _reviewError = ReviewRes.SubmitError;
            _logger.LogError(ex, "Failed to submit review for course {CourseId}.", CourseId);
        }
        finally
        {
            _submittingReview = false;
        }
    }

    private async Task LoadCourse()
    {
        _loading = true;
        _error = null;
        _enrollError = null;

        try
        {
            _course = await CourseService.GetCourseAsync(CourseId, CurrentLang);
            if (_course is null)
                _error = ErrorMessagesRes.CourseNotFound;
            else
                PrepareView(_course);
        }
        catch (Exception ex)
        {
            _error = ErrorMessagesRes.FailedToLoadCourse;
            _logger.LogError(ex, "Failed to load course {CourseId}.", CourseId);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Sorts the outline once and works out the totals, the outcomes and the next lesson.</summary>
    private void PrepareView(CourseDetailDto course)
    {
        _modules = course.Modules
            .OrderBy(module => module.SortOrder)
            .Select(module => module with { Lessons = module.Lessons.OrderBy(lesson => lesson.SortOrder).ToList() })
            .ToList();

        var lessons = _modules.SelectMany(module => module.Lessons).ToList();
        _firstLesson = lessons.FirstOrDefault();
        _nextLesson = lessons.FirstOrDefault(lesson => !lesson.IsCompleted);
        _totalMinutes = lessons.Sum(lesson => lesson.DurationMinutes ?? 0);
        _equipmentLabels = CourseLabels.EquipmentList(course.Equipment);

        // Module descriptions are the sanitized rich text the module body shows too.
        _outcomes = _modules
            .Select(module => module.Description)
            .OfType<string>()
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Take(MaxOutcomes)
            .ToList();

        // Open the module the member trains next, or the first one.
        var nextModule = _modules.FirstOrDefault(module => module.Lessons.Any(lesson => lesson.Id == _nextLesson?.Id));
        _openModuleId = (nextModule ?? _modules.FirstOrDefault())?.Id;
    }

    private async Task EnrollInCourse()
    {
        _enrolling = true;
        _enrollError = null;
        try
        {
            var result = await CourseService.EnrollAsync(CourseId);
            if (result?.Success == true)
            {
                await LoadCourse();
            }
            else
            {
                _enrollError = result?.Message ?? ErrorMessagesRes.FailedToEnroll;
            }
        }
        catch (Exception ex)
        {
            _enrollError = ErrorMessagesRes.FailedToEnroll;
            _logger.LogError(ex, "Failed to enroll in course {CourseId}.", CourseId);
        }
        finally
        {
            _enrolling = false;
        }
    }

    private static int ModuleMinutes(ModuleDto module) =>
        module.Lessons.Sum(lesson => lesson.DurationMinutes ?? 0);

    private static int CompletedCount(ModuleDto module) =>
        module.Lessons.Count(lesson => lesson.IsCompleted);

    private static bool IsModuleDone(ModuleDto module) =>
        module.Lessons.Count > 0 && module.Lessons.All(lesson => lesson.IsCompleted);

    /// <summary>A lesson with prescribed exercises is a workout; any other follows its content type.</summary>
    private static LessonKind KindOf(LessonSummaryDto lesson)
    {
        if (lesson.ExerciseCount > 0)
            return LessonKind.Workout;

        return (ContentType)lesson.ContentType switch
        {
            ContentType.Video => LessonKind.Video,
            ContentType.Pdf => LessonKind.Pdf,
            _ => LessonKind.Reading
        };
    }

    private static string KindIcon(LessonKind kind) => kind switch
    {
        LessonKind.Workout => "bi-lightning-charge-fill",
        LessonKind.Video => "bi-play-fill",
        LessonKind.Pdf => "bi-file-earmark-pdf",
        _ => "bi-book"
    };

    private static string KindLabel(LessonKind kind) => kind switch
    {
        LessonKind.Workout => CourseRes.LessonKind_Workout,
        LessonKind.Video => CourseRes.LessonKind_Video,
        LessonKind.Pdf => CourseRes.LessonKind_Pdf,
        _ => CourseRes.LessonKind_Reading
    };

    /// <summary>Lesson rows link to the lesson once the member is enrolled; before that they are plain text.</summary>
    private string? LessonHref(LessonSummaryDto lesson) =>
        _course?.IsEnrolled == true ? LessonPathPrefix + lesson.Id : null;
}
