using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Web.Consumers;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class PathDetail
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private IPathConsumer PathConsumer { get; set; } = default!;

    private LearningPathDetailDto? _path;
    private bool _loading = true;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _path = await PathConsumer.GetPathAsync(Id, CurrentLang);
        _loading = false;
    }
}
