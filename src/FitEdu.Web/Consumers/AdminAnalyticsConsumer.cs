using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the admin analytics API.
/// </summary>
public class AdminAnalyticsConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), IAdminAnalyticsConsumer
{
    public Task<AnalyticsSummaryDto?> GetSummaryAsync()
        => GetAsync<AnalyticsSummaryDto>("api/admin/analytics/summary");
}
