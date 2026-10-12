using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for admin analytics API operations.
/// </summary>
public interface IAdminAnalyticsConsumer
{
    Task<AnalyticsSummaryDto?> GetSummaryAsync();
}
