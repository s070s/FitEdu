using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for student-facing and public certificate API operations.
/// </summary>
public interface ICertificateConsumer
{
    Task<List<CertificateDto>?> GetMyCertificatesAsync(string lang = "en");
    Task<CertificateDto?> IssueCertificateAsync(Guid courseId, string lang = "en");
    Task<CertificateVerificationDto?> VerifyAsync(Guid verificationId, string lang = "en");
}
