using Microsoft.AspNetCore.Components;
using FitEdu.Web.Consumers;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Web.Extensions;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class Pricing
{
    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<Pricing> _logger { get; set; } = default!;

    private List<SubscriptionPlanDto> _plans = [];
    private UserSubscriptionStatusDto? _currentStatus;
    private bool _loading = true;
    private bool _processing;
    private bool _showCancelConfirm;
    private string? _error;
    private string? _cancelMessage;
    private bool _cancelSuccess;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    /// <summary>The second line of every plan card: how many courses can run at once.</summary>
    private static string CourseLimitLabel(int maxCourses) =>
        CourseLimitText.Describe(maxCourses, PricingRes.UnlimitedCourses, PricingRes.OneCourseAtATime, PricingRes.UpToCoursesFormat);

    /// <summary>The member's current plan name in their language; empty while nothing is loaded.</summary>
    private string CurrentPlanName =>
        _currentStatus is null ? string.Empty : TierLabels.Name(_currentStatus.Tier);

    /// <summary>A tick for an included feature, a cross for one the plan lacks.</summary>
    private static string FeatureIcon(bool included) => included ? "bi-check-lg" : "bi-x-lg";

    /// <summary>Pro is the highlighted plan: lime border, badge, and first in the list on phones.</summary>
    private static bool IsFeatured(SubscriptionPlanDto plan) => plan.Tier == SubscriptionTier.Pro;

    /// <summary>Which courses the tier opens, the first line of every plan card.</summary>
    private static string CourseAccessLabel(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Plus => PricingRes.FeatureCoursesPlus,
        SubscriptionTier.Pro => PricingRes.FeatureCoursesPro,
        _ => PricingRes.FeatureCoursesFree
    };

    /// <summary>The "/ month" suffix after a price; the API sends the BillingPeriod enum name.</summary>
    private static string PeriodLabel(string billingPeriod)
    {
        if (!Enum.TryParse<BillingPeriod>(billingPeriod, ignoreCase: true, out var period))
            return string.Empty;

        return period switch
        {
            BillingPeriod.Quarterly => PricingRes.PeriodQuarterly,
            BillingPeriod.Yearly => PricingRes.PeriodYearly,
            BillingPeriod.Lifetime => PricingRes.PeriodLifetime,
            _ => PricingRes.PeriodMonthly
        };
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _plans = await SubscriptionService.GetPlansAsync(CurrentLang);
            _currentStatus = await SubscriptionService.GetStatusAsync();
        }
        catch (Exception ex)
        {
            _error = PricingRes.FailedToLoadPlans;
            LogPlansLoadFailed(_logger, ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task Checkout(Guid planId)
    {
        _processing = true;
        _cancelMessage = null;
        try
        {
            var session = await SubscriptionService.CheckoutAsync(planId);
            if (session is not null && !string.IsNullOrEmpty(session.CheckoutUrl))
            {
                Navigation.NavigateTo(session.CheckoutUrl, forceLoad: false);
            }
            else
            {
                // Reload status after successful mock checkout
                _currentStatus = await SubscriptionService.GetStatusAsync();
                _error = null;
            }
        }
        catch (Exception ex)
        {
            _error = PricingRes.CheckoutFailed;
            LogCheckoutFailed(_logger, ex, planId);
        }
        finally
        {
            _processing = false;
        }
    }

    private async Task CancelSubscription()
    {
        _processing = true;
        _cancelMessage = null;
        try
        {
            var result = await SubscriptionService.CancelAsync();
            if (result is not null)
            {
                _cancelSuccess = result.Success;
                _cancelMessage = result.Message;

                if (result.Success)
                {
                    _currentStatus = await SubscriptionService.GetStatusAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _cancelSuccess = false;
            _cancelMessage = PricingRes.FailedToCancel;
            LogCancelSubscriptionFailed(_logger, ex);
        }
        finally
        {
            _processing = false;
            _showCancelConfirm = false;
        }
    }

    private void RequestCancel() => _showCancelConfirm = true;

    private void CloseCancelDialog() => _showCancelConfirm = false;

    private void NavigateToRegister()
    {
        Navigation.NavigateTo("/register");
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load pricing plans.")]
    private static partial void LogPlansLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Checkout failed for plan {PlanId}.")]
    private static partial void LogCheckoutFailed(ILogger logger, Exception exception, Guid planId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to cancel subscription.")]
    private static partial void LogCancelSubscriptionFailed(ILogger logger, Exception exception);
}
