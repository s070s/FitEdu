using System.Globalization;
using Microsoft.AspNetCore.Components;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Web.Extensions;

namespace FitEdu.Web.Pages;

public partial class Billing
{
    private const string GreekLanguageCode = "el";

    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<Billing> _logger { get; set; } = default!;

    private BillingOverviewDto? _overview;
    private bool _loading = true;
    private bool _cancelling;
    private bool _showCancelConfirm;
    private string? _error;
    private string? _cancelMessage;
    private bool _cancelSuccess;
    private int _page = 1;
    private int _pageSize = 10;
    private static readonly int[] PageSizeOptions = [10, 25, 50];
    private string _sortBy = "createdat";
    private string _sortDir = "desc";

    protected override async Task OnInitializedAsync()
    {
        await LoadBillingOverviewAsync();
    }

    private async Task LoadBillingOverviewAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            _overview = await SubscriptionService.GetBillingOverviewAsync(_page, _pageSize, _sortBy, _sortDir);
        }
        catch (Exception ex)
        {
            _error = ErrorMessagesRes.FailedToLoadBilling;
            _logger.LogError(ex, "Failed to load billing overview.");
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnSort(string columnKey)
    {
        if (_sortBy == columnKey)
            _sortDir = _sortDir == "asc" ? "desc" : "asc";
        else
        {
            _sortBy = columnKey;
            _sortDir = "asc";
        }
        _page = 1;
        await LoadBillingOverviewAsync();
    }

    private async Task PreviousPage()
    {
        if (_overview is { Transactions.HasPreviousPage: true })
        {
            _page--;
            await LoadBillingOverviewAsync();
        }
    }

    private async Task NextPage()
    {
        if (_overview is { Transactions.HasNextPage: true })
        {
            _page++;
            await LoadBillingOverviewAsync();
        }
    }

    private async Task OnPageSizeChanged(int size)
    {
        _pageSize = size;
        _page = 1;
        await LoadBillingOverviewAsync();
    }

    private async Task CancelSubscription()
    {
        _cancelling = true;
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
                    _page = 1;
                    await LoadBillingOverviewAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _cancelSuccess = false;
            _cancelMessage = ErrorMessagesRes.FailedToCancel;
            _logger.LogError(ex, "Failed to cancel subscription.");
        }
        finally
        {
            _cancelling = false;
            _showCancelConfirm = false;
        }
    }

    private void RequestCancel() => _showCancelConfirm = true;

    private void CloseCancelDialog() => _showCancelConfirm = false;

    private void GoToPricing() => Navigation.NavigateTo("/pricing");

    /// <summary>How many courses the current plan lets the member run at once.</summary>
    private static string CourseLimitLabel(int maxCourses) =>
        CourseLimitText.Describe(maxCourses, BillingRes.UnlimitedCourses, BillingRes.OneCourse, BillingRes.CoursesCountFormat);

    /// <summary>The current plan's name in the member's language; empty while nothing is loaded.</summary>
    private string CurrentPlanName =>
        _overview?.CurrentSubscription is { } subscription ? TierLabels.Name(subscription.Tier) : string.Empty;

    /// <summary>Lime for Pro, teal for Plus, the neutral chip for Free.</summary>
    private static string TierChipClass(SubscriptionTier? tier) => tier switch
    {
        SubscriptionTier.Pro => "chip--lime",
        SubscriptionTier.Plus => "chip--teal",
        _ => string.Empty
    };

    /// <summary>
    /// The server writes each transaction description in English ("Upgraded from Plus to Pro").
    /// The type chip and the plan column already say the same, so the Greek page leaves it out.
    /// </summary>
    private static bool ShowTransactionDescription =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != GreekLanguageCode;

    /// <summary>The localized name of a transaction type; the API sends the enum name.</summary>
    private static string TransactionTypeLabel(string type)
    {
        if (!Enum.TryParse<BillingTransactionType>(type, ignoreCase: true, out var transactionType))
            return type;

        return transactionType switch
        {
            BillingTransactionType.Purchase => BillingRes.TxType_Purchase,
            BillingTransactionType.Upgrade => BillingRes.TxType_Upgrade,
            BillingTransactionType.Downgrade => BillingRes.TxType_Downgrade,
            BillingTransactionType.Renewal => BillingRes.TxType_Renewal,
            BillingTransactionType.Refund => BillingRes.TxType_Refund,
            BillingTransactionType.FreePlanAssignment => BillingRes.TxType_FreePlanAssignment,
            BillingTransactionType.PlanSwitch => BillingRes.TxType_PlanSwitch,
            BillingTransactionType.Expired => BillingRes.TxType_Expired,
            _ => type
        };
    }
}
