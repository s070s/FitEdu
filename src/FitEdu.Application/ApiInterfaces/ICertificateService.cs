using FitEdu.Domain.Entities;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Handles certificate issuance, revocation, and PDF regeneration.
/// </summary>
public interface ICertificateService
{
    /// <summary>
    /// Issues a certificate row for a completed course enrollment without rendering its PDF —
    /// the PDF is generated lazily on first download (PERF-4), keeping the CPU-bound QuestPDF
    /// render off the lesson-completion request path.
    /// Idempotent: returns the existing certificate if one has already been issued.
    /// Throws <see cref="InvalidOperationException"/> when the enrollment is not found or not completed.
    /// </summary>
    Task<Certificate> GetOrCreateAsync(string userId, Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the certificate's PDF in the current UI culture's language exists on disk, rendering
    /// and persisting it on the first download in that language. Returns the certificate with
    /// <c>PdfPath</c> (English) or <c>PdfPathEl</c> (Greek) populated.
    /// Throws <see cref="KeyNotFoundException"/> when the certificate does not exist.
    /// </summary>
    Task<Certificate> EnsurePdfAsync(Guid certificateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an active certificate.
    /// Throws <see cref="KeyNotFoundException"/> when the certificate does not exist.
    /// </summary>
    Task RevokeAsync(Guid certificateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes both language PDFs of an existing certificate and clears their paths, so each one
    /// renders again on its next download.
    /// Throws <see cref="KeyNotFoundException"/> when the certificate does not exist.
    /// </summary>
    Task RegenerateAsync(Guid certificateId, CancellationToken cancellationToken = default);
}
