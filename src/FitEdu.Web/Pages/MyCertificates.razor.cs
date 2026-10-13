using Microsoft.AspNetCore.Components;
using FitEdu.Web.Consumers;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class MyCertificates
{
    /// <summary>The plan that includes certificates (SubscriptionPlanSeeder).</summary>
    private const string CertificatePlanName = "Pro";

    [Inject] private ICertificateConsumer CertificateConsumer { get; set; } = default!;
    [Inject] private ISubscriptionConsumer SubscriptionConsumer { get; set; } = default!;
    [Inject] private ILogger<MyCertificates> _logger { get; set; } = default!;

    private List<CertificateDto>? _certificates;
    private bool _loading = true;
    private string _error = string.Empty;

    // Until the plan is known the page keeps the usual copy, so a failed status call never shows an upsell.
    private bool _certificateAccess = true;

    private bool HasNoCertificates => _certificates is null || _certificates.Count == 0;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    protected override async Task OnInitializedAsync()
    {
        var accessTask = LoadCertificateAccessAsync();
        try
        {
            _certificates = await CertificateConsumer.GetMyCertificatesAsync(CurrentLang);
        }
        catch (Exception ex)
        {
            _error = CertificateRes.FailedToLoad;
            _logger.LogError(ex, "Failed to load certificates.");
        }
        finally
        {
            _certificateAccess = await accessTask;
            _loading = false;
        }
    }

    /// <summary>Whether the member's plan issues certificates; true when the plan cannot be read.</summary>
    private async Task<bool> LoadCertificateAccessAsync()
    {
        try
        {
            var status = await SubscriptionConsumer.GetStatusAsync();
            if (status?.Features is null)
                return true;

            return status.Features.CertificateAccess;
        }
        catch (Exception ex)
        {
            LogStatusLoadFailed(_logger, ex);
            return true;
        }
    }

    // Source-generated log method (LoggerMessage) avoids the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load the subscription status for certificates.")]
    private static partial void LogStatusLoadFailed(ILogger logger, Exception exception);
}
