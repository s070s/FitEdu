namespace FitEdu.Application.DTOs;

/// <summary>
/// Response for enrollment and completion operations.
/// </summary>
public record EnrollmentResultDto(
    bool Success,
    string? Message,
    Guid? EnrollmentId
);

/// <summary>
/// Response for lesson completion. <c>CertificatePending</c> is true when the course just
/// completed and certificate auto-generation (REL-5) failed — the completion itself still
/// succeeded, but the UI should tell the user their certificate is pending rather than staying
/// silent; <c>IssueCertificate</c> retries generation on demand. <c>CertificateIncluded</c> is
/// true when the course just completed on a plan with certificates, so the UI can offer the
/// certificate, or an upgrade when it is false.
/// </summary>
public record LessonCompletionResultDto(
    bool Success,
    string? Message,
    int CompletedLessons,
    int TotalLessons,
    double ProgressPercent,
    bool CourseCompleted,
    bool CertificatePending = false,
    bool CertificateIncluded = false
);
