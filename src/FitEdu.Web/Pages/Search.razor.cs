using System.Globalization;
using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;

namespace FitEdu.Web.Pages;

public partial class Search
{
    private const int PageLimit = 20;

    [Inject] private ISearchConsumer Consumer { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "q")]
    private string? Q { get; set; }

    private string _input = string.Empty;
    private bool _loading;
    private SiteSearchResultDto? _result;
    private string? _loadedFor;

    private static string Lang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    protected override async Task OnParametersSetAsync()
    {
        _input = Q ?? string.Empty;

        if (string.IsNullOrWhiteSpace(Q) || Q == _loadedFor)
            return;

        _loadedFor = Q;
        _loading = true;
        _result = null;
        StateHasChanged();

        _result = await Consumer.SearchAsync(Q, PageLimit, Lang);
        _loading = false;
    }

    private void Submit()
    {
        var query = _input.Trim();
        if (query.Length == 0)
            return;

        Navigation.NavigateTo($"/search?q={Uri.EscapeDataString(query)}");
    }

    private static string TypeLabel(string sourceType) => sourceType switch
    {
        "Course" => SearchRes.TypeCourse,
        "Assessment" => SearchRes.TypeAssessment,
        "BlogArticle" => SearchRes.TypeBlogArticle,
        "Exercise" => SearchRes.TypeExercise,
        _ => sourceType
    };
}
