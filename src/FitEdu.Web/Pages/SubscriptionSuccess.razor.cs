using Microsoft.AspNetCore.Components;
using FitEdu.Web.Consumers;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Pages;

public partial class SubscriptionSuccess
{
    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<SubscriptionSuccess> _logger { get; set; } = default!;

    private UserSubscriptionStatusDto? _status;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _status = await SubscriptionService.GetStatusAsync();
        }
        catch (Exception ex)
        {
            LogStatusLoadFailed(_logger, ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private void GoToCourses() => Navigation.NavigateTo("/courses");
    private void GoToPricing() => Navigation.NavigateTo("/pricing");

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load subscription status on success page.")]
    private static partial void LogStatusLoadFailed(ILogger logger, Exception exception);
}
