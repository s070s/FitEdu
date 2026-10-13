using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;

namespace FitEdu.Domain.Entities;

/// <summary>
/// Verifiable certificate issued after a user completes a course.
/// One certificate per user-course pair, enforced via unique index.
/// </summary>
public class Certificate
{
    public Guid Id { get; set; }

    /// <summary>
    /// Public-facing verification identifier embedded in URLs and the PDF.
    /// Kept separate from the primary key so the internal DB ID is never exposed.
    /// </summary>
    public Guid VerificationId { get; set; } = Guid.NewGuid();

    // --- Snapshot fields (denormalised for PDF stability) ---

    /// <summary>
    /// Recipient display name captured at issuance.
    /// Remains unchanged even if the user later updates their profile.
    /// </summary>
    public required string RecipientName { get; set; }

    /// <summary>
    /// Course title (English) captured at issuance.
    /// </summary>
    public required string CourseTitleEn { get; set; }

    /// <summary>
    /// Course title (Greek) captured at issuance. Falls back to CourseTitleEn when null.
    /// </summary>
    public string? CourseTitleEl { get; set; }

    /// <summary>
    /// Total estimated duration in minutes, summed from lesson DurationMinutes at issuance.
    /// Null when no duration data exists on the course lessons.
    /// </summary>
    public int? TotalDurationMinutes { get; set; }

    // --- Lifecycle ---

    /// <summary>
    /// UTC timestamp when the certificate was issued.
    /// </summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the certificate was revoked. Null if still active.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Current validity state. Stored as int.
    /// </summary>
    public CertificateStatus Status { get; set; } = CertificateStatus.Active;

    /// <summary>
    /// Relative file path to the English PDF via IFileStorage.
    /// Null until the English PDF has been generated.
    /// </summary>
    public string? PdfPath { get; set; }

    /// <summary>
    /// Relative file path to the Greek PDF via IFileStorage.
    /// Null until the Greek PDF has been generated.
    /// </summary>
    public string? PdfPathEl { get; set; }

    // --- Foreign keys ---

    /// <summary>Foreign key to ApplicationUser.</summary>
    public required string UserId { get; set; }

    /// <summary>Navigation: the certificate recipient.</summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary>Foreign key to Enrollment.</summary>
    public Guid EnrollmentId { get; set; }

    /// <summary>Navigation: the completed enrollment that triggered this certificate.</summary>
    public Enrollment Enrollment { get; set; } = null!;

    /// <summary>
    /// Foreign key to Course. Nullable so the relationship can be optional at the EF model
    /// level — a soft-deleted course must not silently drop this certificate out of Include
    /// queries (see CertificateConfiguration).
    /// </summary>
    public Guid? CourseId { get; set; }

    /// <summary>Navigation: the course this certificate was issued for. May be null when the course has been soft-deleted.</summary>
    public Course? Course { get; set; }
}
