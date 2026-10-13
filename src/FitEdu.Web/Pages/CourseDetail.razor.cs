using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Domain.Enums;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using System.Globalization;

namespace FitEdu.Web.Pages;

/// <summary>
/// One programme's page. Guests can open it: they see the outline and the approved reviews, and
/// the action card offers sign-up and log-in (which brings them back here) in place of Join.
/// </summary>
public partial class CourseDetail
{
    // "What you'll train" lists the first module summaries, enough to scan at a glance.
    private const int MaxOutcomes = 4;
    // Search engines show about 160 characters of a page description.
    private const int MetaDescriptionLength = 160;
    private const string LessonPathPrefix = "/lessons/";
    private const string CoursePathPrefix = "/courses/";
    private const string LoginPath = "/login";
    private const string RegisterPath = "/register";

    [Parameter]
    public Guid CourseId
    {
        get; set;
    }

    [Inject] private ICourseConsumer CourseService { get; set; } = default!;
    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private ILogger<CourseDetail> _logger { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    // Present only while prerendering, so a missing item can answer 404.
    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    private CourseDetailDto? _course;
    private SubscriptionTier _userTier = SubscriptionTier.Free;
    private bool _isGuest;
    private bool _loading = true;
    private bool _enrolling;
    private string? _error;
    private string? _enrollError;

    // View data prepared once per load (PrepareView).
    private List<ModuleDto> _modules = [];
    private List<string> _outcomes = [];
    private HashSet<Guid> _outcomeModuleIds = [];
    private IReadOnlyList<string> _equipmentLabels = [];
    private LessonSummaryDto? _firstLesson;
    private LessonSummaryDto? _nextLesson;
    private Guid? _openModuleId;
    private int _totalMinutes;
    private string _metaDescription = string.Empty;

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

    /// <summary>The log-in page, set to come back to this programme afterwards.</summary>
    private string LoginHref => Login.LinkWithReturnUrl(LoginPath, CoursePathPrefix + CourseId);

    /// <summary>The sign-up page, set to come back to this programme after the first log-in.</summary>
    private string RegisterHref => Login.LinkWithReturnUrl(RegisterPath, CoursePathPrefix + CourseId);

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
        _isGuest = !await AuthenticationStateTask.IsSignedInAsync();

        // A guest has no subscription to ask about.
        var tierTask = _isGuest ? Task.FromResult<UserSubscriptionStatusDto?>(null) : SubscriptionService.GetStatusAsync();
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
            var lookup = await CourseService.LookupCourseAsync(CourseId, CurrentLang);
            _course = lookup.Item;
            if (_course is null)
            {
                // A failed request offers Try again and answers 503, so crawlers keep the address.
                _error = lookup.RequestFailed ? ErrorMessagesRes.FailedToLoadCourse : ErrorMessagesRes.CourseNotFound;
                PrerenderStatus.SetMissing(HttpContext, lookup.RequestFailed);
            }
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
        _metaDescription = HtmlText.Summary(course.Description, MetaDescriptionLength);
        _modules = course.Modules
            .OrderBy(module => module.SortOrder)
            .Select(module => module with { Lessons = module.Lessons.OrderBy(lesson => lesson.SortOrder).ToList() })
            .ToList();

        var lessons = _modules.SelectMany(module => module.Lessons).ToList();
        _firstLesson = lessons.FirstOrDefault();
        _nextLesson = lessons.FirstOrDefault(lesson => !lesson.IsCompleted);
        _totalMinutes = lessons.Sum(lesson => lesson.DurationMinutes ?? 0);
        _equipmentLabels = CourseLabels.EquipmentList(course.Equipment);

        // The outcomes are the first module descriptions (sanitized rich text); the outline leaves
        // those modules' descriptions out, so the page does not say the same thing twice.
        var outcomeModules = _modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Description))
            .Take(MaxOutcomes)
            .ToList();
        _outcomes = outcomeModules.Select(module => module.Description!).ToList();
        _outcomeModuleIds = outcomeModules.Select(module => module.Id).ToHashSet();

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
