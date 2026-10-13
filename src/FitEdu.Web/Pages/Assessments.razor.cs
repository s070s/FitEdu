using Microsoft.AspNetCore.Components;
using FitEdu.Web.Consumers;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class Assessments : IDisposable
{
    [Inject] private IAssessmentConsumer AssessmentService { get; set; } = default!;
    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private ICategoryConsumer CategoryConsumer { get; set; } = default!;
    [Inject] private ILogger<Assessments> _logger { get; set; } = default!;

    private PagedResult<AssessmentDefinitionDto>? _pagedResult;
    private bool _assessmentAccess = false;
    private bool _loading = true;
    private string? _error;

    private int _page = 1;
    private int _pageSize = 10;

    private List<CategoryDto> _categories = [];
    private Guid? _selectedCategoryId;
    private string _search = string.Empty;
    private CancellationTokenSource? _searchCts;

    private static readonly int[] PageSizeOptions = [5, 10, 20, 50];
    private const int FullPercent = 100;

    /// <summary>Ids of the self-checks this member has sent at least once.</summary>
    private HashSet<Guid> _completedIds = [];

    /// <summary>Published self-checks with no filter applied, captured on the first load.</summary>
    private int _catalogueTotal;

    private int CompletedCount => Math.Min(_completedIds.Count, _catalogueTotal);

    private int CompletedPercent => _catalogueTotal == 0 ? 0 : FullPercent * CompletedCount / _catalogueTotal;

    private string CompletedRingText => $"{CompletedCount}/{_catalogueTotal}";


    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    private bool HasActiveFilter => _selectedCategoryId is not null || !string.IsNullOrWhiteSpace(_search);

    protected override async Task OnInitializedAsync()
    {
        var status = await SubscriptionService.GetStatusAsync();
        _assessmentAccess = status?.Features?.AssessmentAccess == true;

        if (_assessmentAccess)
        {
            await Task.WhenAll(LoadAssessmentsAsync(), LoadCategoriesAsync(), LoadCompletedIdsAsync());
            _catalogueTotal = _pagedResult?.TotalCount ?? 0;
        }
        else
        {
            _loading = false;
        }
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            _categories = await CategoryConsumer.GetCategoriesAsync("assessments", CurrentLang);
        }
        catch (Exception ex)
        {
            _categories = [];
            _logger.LogError(ex, "Failed to load assessment categories.");
        }
    }

    /// <summary>
    /// One page of the member's submissions is enough to mark the cards: the catalogue holds fewer
    /// self-checks than the largest page the API serves.
    /// </summary>
    private async Task LoadCompletedIdsAsync()
    {
        try
        {
            var submissions = await AssessmentService.GetMySubmissionsAsync(1, PagingParams.MaxPageSize);
            _completedIds = submissions?.Items.Select(submission => submission.AssessmentDefinitionId).ToHashSet() ?? [];
        }
        catch (Exception ex)
        {
            _completedIds = [];
            LogSubmissionsLoadFailed(_logger, ex);
        }
    }

    private async Task LoadAssessmentsAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            _pagedResult = await AssessmentService.GetAssessmentsAsync(_page, _pageSize, CurrentLang, _selectedCategoryId, _search);
        }
        catch (Exception ex)
        {
            _error = AssessmentRes.FailedToLoad;
            _logger.LogError(ex, "Failed to load assessments.");
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task GoToPage(int page)
    {
        if (page < 1 || (_pagedResult is not null && page > _pagedResult.TotalPages))
            return;
        _page = page;
        await LoadAssessmentsAsync();
    }

    private async Task ChangePageSize(int newSize)
    {
        _pageSize = newSize;
        _page = 1;
        await LoadAssessmentsAsync();
    }

    private async Task OnCategorySelected(Guid? categoryId)
    {
        _selectedCategoryId = categoryId;
        _page = 1;
        await LoadAssessmentsAsync();
    }

    private async Task OnSearchChanged(string value)
    {
        _search = value;
        _page = 1;

        var previous = _searchCts;
        _searchCts = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            await Task.Delay(300, _searchCts.Token);
            await LoadAssessmentsAsync();
        }
        catch (OperationCanceledException) { }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load the member's assessment submissions.")]
    private static partial void LogSubmissionsLoadFailed(ILogger logger, Exception exception);

    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
