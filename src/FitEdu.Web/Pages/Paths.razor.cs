using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Web.Consumers;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class Paths
{
    /// <summary>Placeholder cards while loading: the seeded catalogue has four paths.</summary>
    private const int SkeletonCardCount = 4;

    [Inject] private IPathConsumer PathConsumer { get; set; } = default!;

    private List<LearningPathListItemDto> _paths = [];
    private bool _loading = true;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    protected override async Task OnInitializedAsync()
    {
        _paths = [.. await PathConsumer.GetPathsAsync(CurrentLang)];
        _loading = false;
    }
}
