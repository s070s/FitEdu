using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for subscription-related API operations.
/// </summary>
public interface ISubscriptionConsumer
{
    /// <summary>Active plans with the description in <paramref name="lang"/> ("en" or "el").</summary>
    Task<List<SubscriptionPlanDto>> GetPlansAsync(string lang = "en");
    Task<UserSubscriptionStatusDto?> GetStatusAsync();
    Task<CheckoutSessionDto?> CheckoutAsync(Guid planId);
    Task<CancelSubscriptionResultDto?> CancelAsync();
    Task<BillingOverviewDto?> GetBillingOverviewAsync(int page = 1, int pageSize = 10, string sortBy = "createdat", string sortDir = "desc");
}
