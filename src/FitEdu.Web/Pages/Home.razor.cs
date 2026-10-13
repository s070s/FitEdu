using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FitEdu.Web.Consumers;
using FitEdu.Web.Layout;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using System.Globalization;
using System.Security.Claims;

namespace FitEdu.Web.Pages;

public partial class Home : IDisposable
{
    private const string DefaultHeroImageUrl = "/images/hero-home.webp";
    private const string CustomHeroImageUrl = "/api/site/background-image";
    private const string CoursesByGoalUrl = "/courses?goal=";
    private const int BlogArticleCount = 3;
    private const int RecommendedCourseCount = 3;
    private const int EnrolledCourseCount = 6;
    private const string TodayFormat = "dddd d MMMM";

    // A handful of members is weak social proof, so the public numbers strip shows the tile from here.
    private const int MinimumMemberCountShown = 100;

    // Ranking weights for "Recommended for you": the goal matters more than the level.
    private const int GoalMatchScore = 2;
    private const int LevelMatchScore = 1;

    /// <summary>The "Pick your goal" cards: each goal with its icon, in display order.</summary>
    private static readonly IReadOnlyList<GoalChoice> GoalChoices =
    [
        new(FitnessGoal.Strength, "bi-lightning-charge"),
        new(FitnessGoal.MuscleGain, "bi-trophy"),
        new(FitnessGoal.FatLoss, "bi-fire"),
        new(FitnessGoal.Endurance, "bi-heart-pulse"),
        new(FitnessGoal.Mobility, "bi-person-arms-up"),
        new(FitnessGoal.Recovery, "bi-moon-stars"),
        new(FitnessGoal.Nutrition, "bi-egg-fried"),
        new(FitnessGoal.GeneralFitness, "bi-activity"),
    ];

    [Inject] private IBlogConsumer BlogConsumer { get; set; } = default!;
    [Inject] private ITestimonialConsumer TestimonialConsumer { get; set; } = default!;
    [Inject] private ICourseConsumer CourseConsumer { get; set; } = default!;
    [Inject] private ILandingConsumer LandingConsumer { get; set; } = default!;
    [Inject] private IProfileConsumer ProfileConsumer { get; set; } = default!;
    [Inject] private PersistentComponentState ApplicationState { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

    private bool _isAuthenticated;
    private bool _isStudent;
    private string? _authenticatedUserName;
    private bool _authRestored;

    // Guest landing
    private LandingOverviewDto? _landing;
    private bool _landingLoading = true;

    private IReadOnlyList<BlogArticleSummaryDto>? _blogSummaries;
    private bool _blogLoading = true;

    private IReadOnlyList<TestimonialDto>? _testimonials;
    private bool _testimonialsLoading = true;

    // Student dashboard
    private DashboardData? _dashboard;
    private bool _dashboardLoading = true;

    private PersistingComponentStateSubscription _persistSub;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    /// <summary>The admin's uploaded hero photo when there is one, otherwise the shipped photo.</summary>
    private string HeroImageUrl => _landing?.HasCustomHeroImage == true ? CustomHeroImageUrl : DefaultHeroImageUrl;

    /// <summary>
    /// Today's date for the dashboard eyebrow ("Tuesday 13 October") in the members' time zone: the
    /// same day the training streak counts, so the two never disagree around midnight.
    /// </summary>
    private string TodayLabel => SiteClock.Today(TimeProvider).ToString(TodayFormat, CultureInfo.CurrentCulture);

    private bool ShowMemberCount => _landing?.MemberCount >= MinimumMemberCountShown;

    // Restore ALL persisted state BEFORE the first render so the first interactive render
    // exactly matches the SSR pre-rendered HTML — seamless hydration, zero flash.
    // OnInitializedAsync fires after the first render and would introduce a loading-state
    // mismatch if GetAuthenticationStateAsync() ever suspends (intermittent flicker).
    public override async Task SetParametersAsync(ParameterView parameters)
    {
        RestoreAuthState();
        RestoreLandingState();
        RestoreDashboardState();
        await base.SetParametersAsync(parameters);
    }

    private void RestoreAuthState()
    {
        if (!ApplicationState.TryTakeFromJson<bool>("home-isAuthenticated", out var isAuth))
            return;

        _isAuthenticated = isAuth;
        ApplicationState.TryTakeFromJson<bool>("home-isStudent", out _isStudent);
        ApplicationState.TryTakeFromJson<string?>("home-userName", out _authenticatedUserName);
        _authRestored = true;
    }

    private void RestoreLandingState()
    {
        if (ApplicationState.TryTakeFromJson<LandingOverviewDto>("home-landing", out var landing))
        {
            _landing = landing;
            _landingLoading = false;
        }
        if (ApplicationState.TryTakeFromJson<List<TestimonialDto>>("home-testimonials", out var testimonials))
        {
            _testimonials = testimonials;
            _testimonialsLoading = false;
        }
        if (ApplicationState.TryTakeFromJson<List<BlogArticleSummaryDto>>("home-blog", out var blog))
        {
            _blogSummaries = blog;
            _blogLoading = false;
        }
    }

    private void RestoreDashboardState()
    {
        if (!ApplicationState.TryTakeFromJson<DashboardData>("home-dashboard", out var dashboard))
            return;

        _dashboard = dashboard;
        _dashboardLoading = false;
    }

    protected override async Task OnInitializedAsync()
    {
        _persistSub = ApplicationState.RegisterOnPersisting(PersistHomeData);

        // Resolve auth state only when not already restored from prerender persistence
        if (!_authRestored)
            await ResolveUserAsync();

        // Load only what wasn't already restored — flags are false when SetParametersAsync
        // successfully restored the data, so no duplicate API calls occur. The student dashboard
        // needs the training data; the anonymous landing needs its numbers, stories and blog.
        var tasks = new List<Task>();
        if (_isStudent)
        {
            if (_dashboardLoading) tasks.Add(LoadDashboardAsync());
        }
        else if (!_isAuthenticated)
        {
            if (_landingLoading) tasks.Add(LoadLandingAsync());
            if (_blogLoading) tasks.Add(LoadBlogAsync());
            if (_testimonialsLoading) tasks.Add(LoadTestimonialsAsync());
        }
        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    private async Task ResolveUserAsync()
    {
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        _isAuthenticated = state.User.Identity?.IsAuthenticated ?? false;
        _isStudent = state.User.IsInRole(NavItems.StudentRole);
        _authenticatedUserName = GreetingName(state.User);
    }

    /// <summary>
    /// The principal carries the name as separate firstName/lastName claims (UserClaimsBuilder) —
    /// there is no ClaimTypes.Name, so Identity.Name is always null. Greet by first name, falling back
    /// to full name then email so the header never reads "Welcome, !".
    /// </summary>
    private static string? GreetingName(ClaimsPrincipal user)
    {
        var firstName = user.FindFirst("firstName")?.Value;
        if (!string.IsNullOrWhiteSpace(firstName))
            return firstName;

        var fullName = $"{firstName} {user.FindFirst("lastName")?.Value}".Trim();
        return !string.IsNullOrWhiteSpace(fullName) ? fullName : user.FindFirst(ClaimTypes.Email)?.Value;
    }

    private Task PersistHomeData()
    {
        ApplicationState.PersistAsJson("home-isAuthenticated", _isAuthenticated);
        ApplicationState.PersistAsJson("home-isStudent", _isStudent);
        ApplicationState.PersistAsJson("home-userName", _authenticatedUserName);
        ApplicationState.PersistAsJson("home-landing", _landing);
        ApplicationState.PersistAsJson("home-testimonials", _testimonials);
        ApplicationState.PersistAsJson("home-blog", _blogSummaries);
        ApplicationState.PersistAsJson("home-dashboard", _dashboard);
        return Task.CompletedTask;
    }

    private async Task LoadLandingAsync()
    {
        try
        {
            _landing = await LandingConsumer.GetOverviewAsync(CurrentLang);
        }
        catch
        {
            // The numbers and showcase are non-critical — the hero falls back to the shipped photo.
        }
        finally
        {
            _landingLoading = false;
        }
    }

    private async Task LoadBlogAsync()
    {
        try
        {
            _blogSummaries = await BlogConsumer.GetSummariesAsync(count: BlogArticleCount, lang: CurrentLang);
        }
        catch
        {
            // Blog section is non-critical — silently skip if unavailable
        }
        finally
        {
            _blogLoading = false;
        }
    }

    private async Task LoadTestimonialsAsync()
    {
        try
        {
            _testimonials = await TestimonialConsumer.GetActiveAsync(CurrentLang);
        }
        catch
        {
            // Testimonials section is non-critical — silently skip if unavailable
        }
        finally
        {
            _testimonialsLoading = false;
        }
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            _dashboard = await BuildDashboardAsync();
        }
        catch
        {
            // The dashboard is non-critical — the explore tiles still lead everywhere.
        }
        finally
        {
            _dashboardLoading = false;
        }
    }

    private async Task<DashboardData> BuildDashboardAsync()
    {
        var statsTask = ProfileConsumer.GetTrainingStatsAsync();
        // The catalog carries an IsEnrolled flag; one page covers any plan's course cap.
        var catalogTask = CourseConsumer.GetCoursesAsync(page: 1, pageSize: 100, lang: CurrentLang);
        await Task.WhenAll(statsTask, catalogTask);

        var stats = await statsTask;
        var catalog = (await catalogTask)?.Items ?? [];
        var enrolled = catalog.Where(c => c.IsEnrolled).ToList();
        var todayPlan = await FindTodayPlanAsync(stats?.CurrentCourseId);

        return new DashboardData(
            stats,
            todayPlan,
            enrolled.Count,
            enrolled.Take(EnrolledCourseCount).ToList(),
            Recommend(catalog, stats));
    }

    /// <summary>
    /// Why there is no next workout: a member who joined programmes but has none in progress has
    /// finished them all, which deserves different words than never having joined one.
    /// </summary>
    private static string TodayPlanEmptyText(DashboardData? dashboard) =>
        dashboard?.EnrolledCount > 0 ? HomeRes.TodayPlanAllDone : HomeRes.TodayPlanEmpty;

    /// <summary>The first lesson not yet completed in the programme the member is following.</summary>
    private async Task<TodayPlan?> FindTodayPlanAsync(Guid? courseId)
    {
        if (courseId is not { } id)
            return null;

        var course = await CourseConsumer.GetCourseAsync(id, CurrentLang);
        if (course is null)
            return null;

        var nextLesson = course.Modules
            .OrderBy(m => m.SortOrder)
            .SelectMany(m => m.Lessons.OrderBy(l => l.SortOrder))
            .FirstOrDefault(l => !l.IsCompleted);
        if (nextLesson is null)
            return null;

        return new TodayPlan(course.Id, course.Title, course.CoverImageUrl, nextLesson.Id, nextLesson.Title,
            nextLesson.DurationMinutes, course.CompletedLessons, course.TotalLessons);
    }

    /// <summary>
    /// Courses the member has not started, best match first: the member's goal, then level, then the
    /// best rated. Without a goal or level this is simply the best rated courses.
    /// </summary>
    private static List<CourseListItemDto> Recommend(IReadOnlyList<CourseListItemDto> catalog, TrainingStatsDto? stats)
    {
        int MatchScore(CourseListItemDto course) =>
            (course.Goal == stats?.Goal ? GoalMatchScore : 0) + (course.Level == stats?.Level ? LevelMatchScore : 0);

        return catalog
            .Where(c => !c.IsEnrolled)
            .OrderByDescending(MatchScore)
            .ThenByDescending(c => c.AverageRating ?? 0)
            .ThenBy(c => c.Title, StringComparer.CurrentCulture)
            .Take(RecommendedCourseCount)
            .ToList();
    }

    /// <summary>The explore tiles: every destination of the user's role except Home itself.</summary>
    private static IEnumerable<NavItem> ExploreItems(ClaimsPrincipal user) =>
        NavItems.ForUser(user).Where(item => item.Href.Length > 0);

    private static string GoalUrl(FitnessGoal goal) => CoursesByGoalUrl + goal;

    /// <summary>Whole numbers in the page language ("1,240" in English, "1.240" in Greek).</summary>
    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static int PercentDone(TodayPlan plan) =>
        plan.TotalLessons == 0 ? 0 : 100 * plan.CompletedLessons / plan.TotalLessons;

    public void Dispose()
    {
        _persistSub.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A goal card: the goal and its Bootstrap Icons glyph.</summary>
    private sealed record GoalChoice(FitnessGoal Goal, string Icon);

    /// <summary>The next workout of the programme the member follows.</summary>
    public sealed record TodayPlan(
        Guid CourseId,
        string CourseTitle,
        string? CoverImageUrl,
        Guid LessonId,
        string LessonTitle,
        int? DurationMinutes,
        int CompletedLessons,
        int TotalLessons);

    /// <summary>Everything the student dashboard shows, persisted as one piece across prerendering.</summary>
    public sealed record DashboardData(
        TrainingStatsDto? Stats,
        TodayPlan? Today,
        int EnrolledCount,
        List<CourseListItemDto> EnrolledCourses,
        List<CourseListItemDto> RecommendedCourses);
}
