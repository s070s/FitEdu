using System.Globalization;
using Microsoft.AspNetCore.Components;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Pages;

public partial class AdminAnalytics
{
    [Inject] private IAdminAnalyticsConsumer Analytics { get; set; } = default!;
    [Inject] private ILogger<AdminAnalytics> _logger { get; set; } = default!;

    private AnalyticsSummaryDto? stats;
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            stats = await Analytics.GetSummaryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analytics.");
            _error = ErrorMessagesRes.FailedToLoadAnalytics;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>A count with the reader's digit grouping, e.g. 1,240 or 1.240.</summary>
    private static string FormatCount(int count) => count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Whole-number completion percentage, or null when nobody has enrolled yet.</summary>
    private static int? CompletionPercent(CourseStatDto courseStat)
    {
        if (courseStat.EnrollmentCount <= 0)
        {
            return null;
        }

        var percent = 100.0 * courseStat.CompletionCount / courseStat.EnrollmentCount;
        return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
    }
}
