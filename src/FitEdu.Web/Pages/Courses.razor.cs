using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Shared.Components.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Application.DTOs;
using System.Globalization;

namespace FitEdu.Web.Pages;

/// <summary>
/// The course catalogue. Category and search live on the page; goal, level and equipment live in
/// the address (?goal=FatLoss&amp;level=Beginner&amp;equipment=none), so other pages can link
/// straight to a filtered list. Guests can browse it too: they have no plan, so they see no plan
/// banner and the paid programmes show as locked, linking to the plans.
/// </summary>
public partial class Courses : IDisposable
{
    private const string GoalQueryName = "goal";
    private const string LevelQueryName = "level";
    private const string EquipmentQueryName = "equipment";
    private const string CategoryScope = "courses";
    private const int DefaultPageSize = 12;
    private const int SearchDebounceMilliseconds = 300;
    private const int SkeletonCardCount = 6;
    private const int DefaultMaxCourses = 1;

    // Multiples of 6 fill whole rows of the one-, two- and three-column grid.
    private static readonly int[] PageSizeOptions = [6, 12, 24, 48];

    [Inject] private ICourseConsumer CourseService { get; set; } = default!;
    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private ICategoryConsumer CategoryConsumer { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<Courses> _logger { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    [SupplyParameterFromQuery(Name = GoalQueryName)]
    public string? GoalQuery { get; set; }

    [SupplyParameterFromQuery(Name = LevelQueryName)]
    public string? LevelQuery { get; set; }

    [SupplyParameterFromQuery(Name = EquipmentQueryName)]
    public string? EquipmentQuery { get; set; }

    private PagedResult<CourseListItemDto>? _pagedResult;
    private UserSubscriptionStatusDto? _userStatus;
    private SubscriptionTier _userTier = SubscriptionTier.Free;
    private int _maxCourses = DefaultMaxCourses;
    // Null until the API says how many programmes the member joined (or when it could not say).
    private int? _enrolledCount;
    private bool _statusLoaded;
    private bool _loading = true;
    private string? _error;
    private int _latestRequestNumber;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    private List<CategoryDto> _categories = [];
    private Guid? _selectedCategoryId;
    private string _search = string.Empty;
    private CancellationTokenSource? _searchCts;

    private FitnessGoal? _goal;
    private CourseLevel? _level;
    private string? _equipment;
    private IReadOnlyList<FilterChipGroup.Option> _goalOptions = [];
    private IReadOnlyList<FilterChipGroup.Option> _levelOptions = [];
    private IReadOnlyList<FilterChipGroup.Option> _equipmentOptions = [];

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    private bool HasQueryFilter => _goal is not null || _level is not null || _equipment is not null;

    private bool HasActiveFilter =>
        _selectedCategoryId is not null || !string.IsNullOrWhiteSpace(_search) || HasQueryFilter;

    private int ExtraFilterCount =>
        (_goal is null ? 0 : 1) + (_level is null ? 0 : 1) + (_equipment is null ? 0 : 1);

    // The cards wait for the plan too, so a course is never shown locked and then unlocked.
    private bool ShowSkeleton => _loading || !_statusLoaded;

    private bool IsCatalogueEmpty =>
        !ShowSkeleton && _error is null && _pagedResult is { TotalCount: 0 } && !HasActiveFilter;

    private bool ShowPlanBanner => _userStatus is not null && _maxCourses != int.MaxValue;

    private bool IsPlanFull => _enrolledCount >= _maxCourses;

    private bool HasEnrolledCount => _enrolledCount is not null;

    protected override async Task OnInitializedAsync()
    {
        BuildFilterOptions();
        ApplyQueryFilters();

        await Task.WhenAll(LoadCoursesAsync(), LoadStatusAsync(), LoadCategoriesAsync());
    }

    protected override async Task OnParametersSetAsync()
    {
        // Also runs right after OnInitializedAsync, which already loaded these filters.
        if (!ApplyQueryFilters())
            return;

        _page = 1;
        await LoadCoursesAsync();
    }

    /// <summary>Chip labels follow the page language, so they are built per page, not once.</summary>
    private void BuildFilterOptions()
    {
        _goalOptions = Enum.GetValues<FitnessGoal>()
            .Select(goal => new FilterChipGroup.Option(goal.ToString(), CourseLabels.Goal(goal)))
            .ToList();
        _levelOptions = Enum.GetValues<CourseLevel>()
            .Select(level => new FilterChipGroup.Option(level.ToString(), CourseLabels.Level(level)))
            .ToList();
        _equipmentOptions = Course.EquipmentCodes
            .Select(code => new FilterChipGroup.Option(code, CourseLabels.Equipment(code)))
            .ToList();
    }

    /// <summary>Reads goal, level and equipment from the address. True when any of them changed.</summary>
    private bool ApplyQueryFilters()
    {
        var goal = ParseEnumName<FitnessGoal>(GoalQuery);
        var level = ParseEnumName<CourseLevel>(LevelQuery);
        var equipment = ParseEquipmentCode(EquipmentQuery);

        var changed = goal != _goal || level != _level || equipment != _equipment;
        _goal = goal;
        _level = level;
        _equipment = equipment;
        return changed;
    }

    /// <summary>An enum member by name (any case); null for a missing or unknown value.</summary>
    private static TEnum? ParseEnumName<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // IsDefined drops numbers such as "42", which TryParse accepts.
        var isKnown = Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed);
        return isKnown ? parsed : null;
    }

    /// <summary>A known course equipment code in lower case; null for a missing or unknown value.</summary>
    private static string? ParseEquipmentCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var code = value.Trim().ToLowerInvariant();
        return Course.EquipmentCodes.Contains(code) ? code : null;
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            // A guest has no subscription to ask about.
            if (!await AuthenticationStateTask.IsSignedInAsync())
                return;

            _userStatus = await SubscriptionService.GetStatusAsync();
            if (_userStatus is null)
                return;

            _userTier = _userStatus.Tier;
            _maxCourses = _userStatus.Features?.MaxCourses ?? DefaultMaxCourses;

            // The API counts every enrolment, as the join check does; a page shows only some of them.
            if (_maxCourses != int.MaxValue)
                _enrolledCount = await CourseService.CountEnrollmentsAsync();
        }
        finally
        {
            _statusLoaded = true;
        }
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            _categories = await CategoryConsumer.GetCategoriesAsync(CategoryScope, CurrentLang);
        }
        catch (Exception ex)
        {
            _categories = [];
            _logger.LogError(ex, "Failed to load course categories.");
        }
    }

    private async Task LoadCoursesAsync()
    {
        // Only the newest request may update the page: a slower, older one must not overwrite it.
        var requestNumber = ++_latestRequestNumber;
        _loading = true;
        _error = null;
        try
        {
            var result = await CourseService.GetCoursesAsync(
                _page, _pageSize, CurrentLang, _selectedCategoryId, _search, _goal, _level, _equipment);
            if (requestNumber != _latestRequestNumber)
                return;

            _pagedResult = result;
        }
        catch (Exception ex)
        {
            if (requestNumber == _latestRequestNumber)
                _error = ErrorMessagesRes.FailedToLoadCourses;
            _logger.LogError(ex, "Failed to load courses.");
        }
        finally
        {
            if (requestNumber == _latestRequestNumber)
                _loading = false;
        }
    }

    private bool IsLocked(CourseListItemDto course) => _userTier < course.RequiredTier && !course.IsEnrolled;

    private async Task GoToPage(int page)
    {
        if (page < 1 || (_pagedResult is not null && page > _pagedResult.TotalPages))
            return;

        _page = page;
        await LoadCoursesAsync();
    }

    private async Task OnPageSizeChanged(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var newSize) || !PageSizeOptions.Contains(newSize))
            return;

        _pageSize = newSize;
        _page = 1;
        await LoadCoursesAsync();
    }

    private async Task OnCategorySelected(Guid? categoryId)
    {
        _selectedCategoryId = categoryId;
        _page = 1;
        await LoadCoursesAsync();
    }

    private void OnGoalSelected(string? goal) => SetQueryFilter(GoalQueryName, goal);

    private void OnLevelSelected(string? level) => SetQueryFilter(LevelQueryName, level);

    private void OnEquipmentSelected(string? equipment) => SetQueryFilter(EquipmentQueryName, equipment);

    // The address holds these filters: OnParametersSetAsync reloads once the router applies it.
    private void SetQueryFilter(string name, string? value)
    {
        var uri = Navigation.GetUriWithQueryParameter(name, value);
        Navigation.NavigateTo(uri, replace: true);
    }

    private async Task ClearFiltersAsync()
    {
        CancelPendingSearch();
        _selectedCategoryId = null;
        _search = string.Empty;
        _page = 1;

        if (!HasQueryFilter)
        {
            await LoadCoursesAsync();
            return;
        }

        // Dropping the address filters reloads the list through OnParametersSetAsync.
        var uri = Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            [GoalQueryName] = null,
            [LevelQueryName] = null,
            [EquipmentQueryName] = null
        });
        Navigation.NavigateTo(uri, replace: true);
    }

    private async Task OnSearchChanged(string value)
    {
        _search = value;
        _page = 1;

        CancelPendingSearch();
        var searchCts = new CancellationTokenSource();
        _searchCts = searchCts;

        try
        {
            await Task.Delay(SearchDebounceMilliseconds, searchCts.Token);
            await LoadCoursesAsync();
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke (or "Clear filters") replaced this search.
        }
    }

    private void CancelPendingSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    public void Dispose() => CancelPendingSearch();
}
