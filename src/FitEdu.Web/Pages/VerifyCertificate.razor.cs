using Microsoft.AspNetCore.Components;
using FitEdu.Web.Consumers;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class VerifyCertificate
{
    [Parameter] public Guid VerificationId { get; set; }

    [Inject] private ICertificateConsumer CertificateConsumer { get; set; } = default!;
    [Inject] private ILogger<VerifyCertificate> _logger { get; set; } = default!;

    private CertificateVerificationDto? _result;
    private bool _loading = true;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    // The API message is English-only (it also serves third-party checks), so the page words its
    // own reason in the visitor's language: an unknown id has no status, a revoked one says so.
    private string InvalidLead =>
        _result?.Status == nameof(CertificateStatus.Revoked)
            ? CertificateRes.VerificationRevokedLead
            : CertificateRes.VerificationNotFoundLead;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _result = await CertificateConsumer.VerifyAsync(VerificationId, CurrentLang);
        }
        catch (Exception ex)
        {
            LogVerifyFailed(_logger, ex, VerificationId);
        }
        finally
        {
            _loading = false;
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to verify certificate {VerificationId}.")]
    private static partial void LogVerifyFailed(ILogger logger, Exception exception, Guid verificationId);
}
