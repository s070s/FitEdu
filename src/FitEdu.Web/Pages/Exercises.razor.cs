using System.Globalization;
using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;

namespace FitEdu.Web.Pages;

/// <summary>
/// Public exercise library: a grid of exercise cards with muscle chips, level / equipment / type
/// selects, a debounced search and pages. The filters and the page live in the address
/// (?search=squat&amp;muscle=chest&amp;level=beginner&amp;page=2), so a link, a reload or Back shows the
/// same list. The prerendered first page is handed to the interactive render, so the grid does not
/// flash or load twice.
/// </summary>
public partial class Exercises : IDisposable
{
    private const int SearchDebounceMilliseconds = 300;
    private const string PersistKey = "exercise-library";
    private const string SearchQueryName = "search";
    private const string MuscleQueryName = "muscle";
    private const string EquipmentQueryName = "equipment";
    private const string LevelQueryName = "level";
    private const string CategoryQueryName = "category";
    private const string PageQueryName = "page";

    // The facets endpoint sorts values alphabetically; the level select lists them by difficulty.
    private static readonly string[] LevelOrder = ["beginner", "intermediate", "expert"];

    [Inject] private IExerciseConsumer ExerciseConsumer { get; set; } = default!;
    [Inject] private PersistentComponentState ApplicationState { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = SearchQueryName)]
    public string? SearchQuery { get; set; }

    [SupplyParameterFromQuery(Name = MuscleQueryName)]
    public string? MuscleQuery { get; set; }

    [SupplyParameterFromQuery(Name = EquipmentQueryName)]
    public string? EquipmentQuery { get; set; }

    [SupplyParameterFromQuery(Name = LevelQueryName)]
    public string? LevelQuery { get; set; }

    [SupplyParameterFromQuery(Name = CategoryQueryName)]
    public string? CategoryQuery { get; set; }

    // Text, not int: a malformed page number is ignored instead of failing the page.
    [SupplyParameterFromQuery(Name = PageQueryName)]
    public string? PageQuery { get; set; }

    private LibraryFilter _filter = LibraryFilter.None;
    private int _page = QueryValues.FirstPage;
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

    private string ResultCountText =>
        ResourceFormat.Count(_result?.TotalCount ?? 0, ExerciseRes.ResultCountOne, ExerciseRes.ResultsFormat);

    private bool HasResults => _result is { Items.Count: > 0 };

    protected override async Task OnInitializedAsync()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistSnapshot);
        ApplyQuery();

        if (ApplicationState.TryTakeFromJson<LibrarySnapshot>(PersistKey, out var snapshot) && snapshot is not null)
        {
            ShowFacets(snapshot.Facets);
            _result = snapshot.Result;
            _loading = false;
            return;
        }

        await ReloadAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        // Also runs right after OnInitializedAsync, which already read the address.
        var previousPage = _page;
        if (!ApplyQuery())
            return;

        await LoadExercisesAsync();

        // After a page change the results heading takes focus, which also scrolls the grid back into view.
        _focusResults = _page != previousPage;
    }

    /// <summary>Reads the filters and the page from the address. True when any of them changed.</summary>
    private bool ApplyQuery()
    {
        var filter = new LibraryFilter(
            QueryValues.Text(SearchQuery),
            QueryValues.Text(MuscleQuery),
            QueryValues.Text(LevelQuery),
            QueryValues.Text(EquipmentQuery),
            QueryValues.Text(CategoryQuery));
        var page = QueryValues.PageNumber(PageQuery);

        var changed = filter != _filter || page != _page;
        _filter = filter;
        _page = page;

        // While a newer search is still waiting, the box keeps what the reader is typing.
        if (_searchDebounce is null)
            _searchText = filter.Search ?? string.Empty;

        return changed;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusResults)
            return;

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

    private void SelectMuscle(string? muscle)
    {
        // Tapping the selected chip again clears it.
        var next = muscle == _filter.Muscle ? null : muscle;
        ApplyFilter(_filter with { Muscle = next });
    }

    private void OnLevelChanged(ChangeEventArgs args) =>
        ApplyFilter(_filter with { Level = SelectedValue(args) });

    private void OnEquipmentChanged(ChangeEventArgs args) =>
        ApplyFilter(_filter with { Equipment = SelectedValue(args) });

    private void OnCategoryChanged(ChangeEventArgs args) =>
        ApplyFilter(_filter with { Category = SelectedValue(args) });

    private void ClearFilters()
    {
        CancelPendingSearch();
        _searchText = string.Empty;
        ApplyFilter(LibraryFilter.None);
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

        // The wait is over, so the address may fill the search box again.
        CancelPendingSearch();
        ApplySearch();
    }

    // Enter in the search box searches at once instead of waiting for the debounce.
    private void SubmitSearch()
    {
        CancelPendingSearch();
        ApplySearch();
    }

    private void ApplySearch()
    {
        var search = QueryValues.Text(_searchText);
        if (search == _filter.Search)
            return;

        ApplyFilter(_filter with { Search = search });
    }

    private async Task ReloadAsync()
    {
        var facetsTask = ExerciseConsumer.GetFacetsAsync();
        await LoadExercisesAsync();
        ShowFacets(await facetsTask);
    }

    // The address holds the filters: OnParametersSetAsync reloads once the router applies it.
    private void ApplyFilter(LibraryFilter filter) => SetAddress(filter, QueryValues.FirstPage);

    private void GoToPage(int page) => SetAddress(_filter, page);

    private void SetAddress(LibraryFilter filter, int page)
    {
        var uri = Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            [SearchQueryName] = filter.Search,
            [MuscleQueryName] = filter.Muscle,
            [EquipmentQueryName] = filter.Equipment,
            [LevelQueryName] = filter.Level,
            [CategoryQueryName] = filter.Category,
            [PageQueryName] = QueryValues.PageParameter(page)
        });
        Navigation.NavigateTo(uri, replace: true);
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
