using System.Globalization;
using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;

namespace FitEdu.Web.Pages;

/// <summary>
/// Public exercise library: a grid of exercise cards with muscle chips, level / equipment / type
/// selects, a debounced search and pages. The prerendered first page is handed to the interactive
/// render, so the grid does not flash or load twice.
/// </summary>
public partial class Exercises : IDisposable
{
    private const int SearchDebounceMilliseconds = 300;
    private const int FirstPage = 1;
    private const string PersistKey = "exercise-library";

    // The facets endpoint sorts values alphabetically; the level select lists them by difficulty.
    private static readonly string[] LevelOrder = ["beginner", "intermediate", "expert"];

    [Inject] private IExerciseConsumer ExerciseConsumer { get; set; } = default!;
    [Inject] private PersistentComponentState ApplicationState { get; set; } = default!;

    private LibraryFilter _filter = LibraryFilter.None;
    private int _page = FirstPage;
    private string _searchText = string.Empty;

    private ExerciseFacetsDto _facets = ExerciseFacetsDto.Empty;
    private PagedResult<ExerciseListItemDto>? _result;
    private List<FacetOption> _muscleOptions = [];
    private List<FacetOption> _levelOptions = [];
    private List<FacetOption> _equipmentOptions = [];
    private List<FacetOption> _categoryOptions = [];

    private bool _loading = true;
    private bool _refreshing;
    private int _loadVersion;
    private bool _focusResults;
    private ElementReference _resultsHeading;
    private CancellationTokenSource? _searchDebounce;
    private PersistingComponentStateSubscription _persistSubscription;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    private int LibraryTotal => _facets.Levels.Sum(level => level.Count);

    private string LibraryTotalText => ExerciseCountText(LibraryTotal);

    private string ResultCountText => ExerciseCountText(_result?.TotalCount ?? 0);

    private static string ExerciseCountText(int count) =>
        ResourceFormat.Count(count, ExerciseRes.ResultCountOne, ExerciseRes.ResultsFormat);

    private bool HasResults => _result is { Items.Count: > 0 };

    protected override async Task OnInitializedAsync()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistSnapshot);

        if (ApplicationState.TryTakeFromJson<LibrarySnapshot>(PersistKey, out var snapshot) && snapshot is not null)
        {
            ShowFacets(snapshot.Facets);
            _result = snapshot.Result;
            _loading = false;
            return;
        }

        await ReloadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusResults)
            return;

        // After a page change the results heading takes focus, which also scrolls the grid back into view.
        _focusResults = false;
        await _resultsHeading.FocusAsync();
    }

    private async Task LoadExercisesAsync()
    {
        // A slower, older request must not overwrite the results of a newer one.
        var version = ++_loadVersion;
        _refreshing = _result is not null;

        var result = await ExerciseConsumer.ListAsync(_page, ExerciseLibraryPaging.DefaultPageSize, CurrentLang, _filter.ToDto());
        if (version != _loadVersion)
            return;

        _result = result;
        _loading = false;
        _refreshing = false;
    }

    private void ShowFacets(ExerciseFacetsDto facets)
    {
        _facets = facets;
        _muscleOptions = SortByLabel(facets.Muscles, ExerciseLabels.Muscle);
        _levelOptions = SortByDifficulty(facets.Levels);
        _equipmentOptions = SortByLabel(facets.Equipment, ExerciseLabels.Equipment);
        _categoryOptions = SortByLabel(facets.Categories, ExerciseLabels.Category);
    }

    private static List<FacetOption> SortByLabel(IEnumerable<ExerciseFacetDto> facets, Func<string, string> labelFor)
    {
        // Sorted by the label the reader sees, so the Greek chips are in Greek alphabetical order.
        var comparer = StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true);
        return facets
            .Select(facet => new FacetOption(facet.Value, labelFor(facet.Value), facet.Count))
            .OrderBy(option => option.Label, comparer)
            .ToList();
    }

    private static List<FacetOption> SortByDifficulty(IEnumerable<ExerciseFacetDto> facets) =>
        facets
            .Select(facet => new FacetOption(facet.Value, ExerciseLabels.Level(facet.Value), facet.Count))
            .OrderBy(option => DifficultyRank(option.Value))
            .ToList();

    // An unknown level goes after the three known ones.
    private static int DifficultyRank(string level)
    {
        var index = Array.IndexOf(LevelOrder, level);
        return index < 0 ? LevelOrder.Length : index;
    }

    // ---- Filter events ----------------------------------------------------------------------

    private Task SelectMuscleAsync(string? muscle)
    {
        // Tapping the selected chip again clears it.
        var next = muscle == _filter.Muscle ? null : muscle;
        return ApplyFilterAsync(_filter with { Muscle = next });
    }

    private Task OnLevelChangedAsync(ChangeEventArgs args) =>
        ApplyFilterAsync(_filter with { Level = SelectedValue(args) });

    private Task OnEquipmentChangedAsync(ChangeEventArgs args) =>
        ApplyFilterAsync(_filter with { Equipment = SelectedValue(args) });

    private Task OnCategoryChangedAsync(ChangeEventArgs args) =>
        ApplyFilterAsync(_filter with { Category = SelectedValue(args) });

    private Task ClearFiltersAsync()
    {
        CancelPendingSearch();
        _searchText = string.Empty;
        return ApplyFilterAsync(LibraryFilter.None);
    }

    private async Task OnSearchInputAsync(ChangeEventArgs args)
    {
        _searchText = args.Value?.ToString() ?? string.Empty;

        CancelPendingSearch();
        var debounce = new CancellationTokenSource();
        _searchDebounce = debounce;

        try
        {
            await Task.Delay(SearchDebounceMilliseconds, debounce.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ApplySearchAsync();
    }

    // Enter in the search box searches at once instead of waiting for the debounce.
    private Task SubmitSearchAsync()
    {
        CancelPendingSearch();
        return ApplySearchAsync();
    }

    private Task ApplySearchAsync()
    {
        var search = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText.Trim();
        if (search == _filter.Search)
            return Task.CompletedTask;

        return ApplyFilterAsync(_filter with { Search = search });
    }

    private async Task ReloadAsync()
    {
        var facetsTask = ExerciseConsumer.GetFacetsAsync();
        await LoadExercisesAsync();
        ShowFacets(await facetsTask);
    }

    private async Task ApplyFilterAsync(LibraryFilter filter)
    {
        _filter = filter;
        _page = FirstPage;
        await LoadExercisesAsync();
    }

    private async Task GoToPageAsync(int page)
    {
        _page = page;
        await LoadExercisesAsync();
        _focusResults = true;
    }

    private void CancelPendingSearch()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = null;
    }

    private static string? SelectedValue(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private Task PersistSnapshot()
    {
        if (_result is not null)
            ApplicationState.PersistAsJson(PersistKey, new LibrarySnapshot(_facets, _result));

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CancelPendingSearch();
        _persistSubscription.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>One chip or select option: the stored value, its label in the page language and its count.</summary>
    private sealed record FacetOption(string Value, string Label, int Count);

    /// <summary>The prerendered facets and first page, handed to the interactive render.</summary>
    private sealed record LibrarySnapshot(ExerciseFacetsDto Facets, PagedResult<ExerciseListItemDto> Result);

    /// <summary>The filters the reader has chosen; null means "all".</summary>
    private sealed record LibraryFilter(string? Search, string? Muscle, string? Level, string? Equipment, string? Category)
    {
        public static LibraryFilter None { get; } = new(null, null, null, null, null);

        public bool IsActive => this != None;

        public ExerciseFilterDto ToDto() => new(Search, Muscle, Equipment, Level, Category);
    }
}
