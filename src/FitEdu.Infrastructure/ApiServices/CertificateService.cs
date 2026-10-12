using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using FitEdu.Application.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Application.ApiInterfaces;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// Issues, revokes, and regenerates PDF certificates for completed course enrollments.
/// </summary>
public class CertificateService : ICertificateService
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly INotificationDispatcher _notifications;
    private readonly ILogger<CertificateService> _logger;

    private const string Heading = "Programme Completion Certificate";
    private const string PlatformName = "FitEdu";
    private const float BrandMarkSize = 44;

    // Brand palette (the --bg-primary, --text-* tokens and the teal accent in wwwroot/css/app.css).
    private const string PageColor = "#0E1116";
    private const string LimeColor = "#C6F432";
    private const string TealColor = "#2EC4B6";
    private const string StrongTextColor = "#F2F5F7";
    private const string LightTextColor = "#B8C2CC";
    private const string MutedTextColor = "#8A96A3";
    private const string DividerColor = "#2A323C";

    /// <summary>
    /// The dumbbell mark of src/FitEdu.Web/wwwroot/favicon.svg. Infrastructure has no access to the
    /// web root, so the markup is copied here; CertificateServiceTests keeps the two in step.
    /// </summary>
    public const string BrandMarkSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">
          <rect width="64" height="64" rx="14" fill="#0E1116"/>
          <rect x="20" y="28" width="24" height="8" fill="#2EC4B6"/>
          <rect x="12" y="12" width="8" height="40" rx="2" fill="#C6F432"/>
          <rect x="44" y="12" width="8" height="40" rx="2" fill="#C6F432"/>
          <rect x="8" y="20" width="4" height="24" rx="1" fill="#C6F432"/>
          <rect x="52" y="20" width="4" height="24" rx="1" fill="#C6F432"/>
        </svg>
        """;

    static CertificateService()
    {
        // Set once when the class is first loaded. Community license is free for
        // non-commercial and open-source use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public CertificateService(
        IApplicationDbContext db,
        IFileStorage storage,
        INotificationDispatcher notifications,
        ILogger<CertificateService> logger)
    {
        _db = db;
        _storage = storage;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Certificate> GetOrCreateAsync(
        string userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        // Idempotency: return the existing certificate without recreating anything
        var existing = await _db.Certificates
            .FirstOrDefaultAsync(c => c.UserId == userId && c.CourseId == courseId, cancellationToken);

        if (existing is not null)
            return existing;

        // Validate that the enrollment exists and is fully completed
        var enrollment = await _db.Enrollments
            .Include(e => e.Course)
            .Include(e => e.User)
            .FirstOrDefaultAsync(
                e => e.UserId == userId
                  && e.CourseId == courseId
                  && e.Status == EnrollmentStatus.Completed,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"No completed enrollment found for user {userId} on course {courseId}.");

        var course = enrollment.Course;
        var user = enrollment.User;

        // Sum lesson durations; null DurationMinutes are treated as zero
        var totalDuration = await _db.Lessons
            .Where(l => l.Module.CourseId == courseId)
            .SumAsync(l => l.DurationMinutes ?? 0, cancellationToken);

        // Prefer DisplayName, fall back to first + last name
        var recipientName = !string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.DisplayName
            : $"{user.FirstName} {user.LastName}".Trim();

        var certificate = new Certificate
        {
            Id = Guid.NewGuid(),
            VerificationId = Guid.NewGuid(),
            UserId = userId,
            CourseId = courseId,
            EnrollmentId = enrollment.Id,
            RecipientName = recipientName,
            CourseTitleEn = course.TitleEn,
            CourseTitleEl = course.TitleEl,
            TotalDurationMinutes = totalDuration > 0 ? totalDuration : null,
            IssuedAt = DateTime.UtcNow,
            Status = CertificateStatus.Active
            // PdfPath left null — the PDF is rendered lazily on first download (PERF-4), keeping
            // the CPU-bound QuestPDF render off the lesson-completion request path.
        };

        _db.Certificates.Add(certificate);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request won the race and inserted first (unique index on UserId+CourseId).
            // No PDF was written yet, so just return the already-committed certificate.
            var winner = await _db.Certificates
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId && c.CourseId == courseId, cancellationToken);

            if (winner is not null)
                return winner;

            throw; // Unexpected: unique violation but no row found — re-throw the original exception.
        }

        _logger.LogInformation(
            "Certificate {CertificateId} issued for user {UserId} on course {CourseId}.",
            certificate.Id, userId, courseId);

        await _notifications.DispatchAsync(
            userId,
            NotificationType.CertificateIssued,
            "CertificateIssued",
            [course.TitleEn],
            "/my-certificates",
            cancellationToken);

        return certificate;
    }

    public async Task<Certificate> EnsurePdfAsync(
        Guid certificateId,
        CancellationToken cancellationToken = default)
    {
        var certificate = await _db.Certificates
            .FirstOrDefaultAsync(c => c.Id == certificateId, cancellationToken)
            ?? throw new KeyNotFoundException($"Certificate {certificateId} not found.");

        // Already rendered — nothing to do.
        if (!string.IsNullOrEmpty(certificate.PdfPath) && _storage.FileExists(certificate.PdfPath))
            return certificate;

        certificate.PdfPath = await GeneratePdfAsync(certificate, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Certificate {CertificateId} PDF rendered on first download.", certificate.Id);

        return certificate;
    }

    public async Task RevokeAsync(Guid certificateId, CancellationToken cancellationToken = default)
    {
        var certificate = await _db.Certificates.FindAsync([certificateId], cancellationToken)
            ?? throw new KeyNotFoundException($"Certificate {certificateId} not found.");

        certificate.Status = CertificateStatus.Revoked;
        certificate.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Certificate {CertificateId} revoked.", certificateId);
    }

    public async Task RegenerateAsync(Guid certificateId, CancellationToken cancellationToken = default)
    {
        var certificate = await _db.Certificates.FindAsync([certificateId], cancellationToken)
            ?? throw new KeyNotFoundException($"Certificate {certificateId} not found.");

        if (!string.IsNullOrEmpty(certificate.PdfPath) && _storage.FileExists(certificate.PdfPath))
            await _storage.DeleteFileAsync(certificate.PdfPath, cancellationToken);

        certificate.PdfPath = await GeneratePdfAsync(certificate, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Certificate {CertificateId} PDF regenerated.", certificateId);
    }

    // ---------------------------------------------------------------------------
    // PDF generation
    // ---------------------------------------------------------------------------

    private async Task<string> GeneratePdfAsync(Certificate cert, CancellationToken cancellationToken)
    {
        // Render the certificate in the culture active for the current request
        // (set by UseRequestLocalization). Defaults to English outside a localized request.
        var pdfBytes = CreateDocument(cert, CultureInfo.CurrentUICulture).GeneratePdf();

        using var stream = new MemoryStream(pdfBytes);
        return await _storage.SaveFileAsync(
            stream,
            $"{cert.VerificationId}.pdf",
            "certificates",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Selects the course title to print for the given culture.
    /// Greek cultures (el) get the Greek title when one exists; every other culture
    /// gets the English title. Fixes the prior behaviour that always preferred Greek.
    /// </summary>
    public static string ResolveCourseTitle(string titleEn, string? titleEl, CultureInfo culture)
    {
        var isGreek = culture.TwoLetterISOLanguageName.Equals("el", StringComparison.OrdinalIgnoreCase);
        return isGreek && !string.IsNullOrWhiteSpace(titleEl) ? titleEl : titleEn;
    }

    /// <summary>
    /// Lays out the one-page landscape certificate in the brand palette: the dumbbell mark and
    /// name at the top, the heading, recipient and programme in the middle, the issue date and
    /// verification id at the bottom.
    /// </summary>
    public static IDocument CreateDocument(Certificate cert, CultureInfo culture)
    {
        var courseTitle = ResolveCourseTitle(cert.CourseTitleEn, cert.CourseTitleEl, culture);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.PageColor(PageColor);
                page.MarginHorizontal(60);
                page.MarginVertical(44);
                page.DefaultTextStyle(style => style.FontColor(LightTextColor));

                page.Header().Element(ComposeBrandRow);
                page.Content().AlignMiddle().Element(content => ComposeBody(content, cert, courseTitle));
                page.Footer().Element(footer => ComposeFooter(footer, cert));
            });
        })
        .WithMetadata(new DocumentMetadata { Title = Heading, Author = PlatformName, Subject = courseTitle });
    }

    private static void ComposeBrandRow(IContainer container)
    {
        container.Row(row =>
        {
            row.Spacing(12);
            row.ConstantItem(BrandMarkSize).Height(BrandMarkSize).Svg(BrandMarkSvg);
            row.RelativeItem().AlignMiddle()
                .Text(PlatformName)
                .FontSize(20)
                .Bold()
                .FontColor(StrongTextColor);
        });
    }

    private static void ComposeBody(IContainer container, Certificate cert, string courseTitle)
    {
        container.Column(column =>
        {
            column.Spacing(12);

            // A short lime rule above the heading, like the accent bars in the app.
            column.Item().AlignCenter().Width(80).LineHorizontal(3).LineColor(LimeColor);

            column.Item().AlignCenter().Text(Heading).FontSize(32).Bold().FontColor(LimeColor);
            column.Item().AlignCenter().Text("This is to certify that").FontSize(13);
            column.Item().AlignCenter().Text(cert.RecipientName).FontSize(28).Bold().FontColor(StrongTextColor);
            column.Item().AlignCenter().Text("has completed the programme").FontSize(13);

            // Course title, localised for the issuing culture (English by default)
            column.Item().AlignCenter().Text(courseTitle).FontSize(22).Bold().FontColor(TealColor);

            var duration = FormatDuration(cert.TotalDurationMinutes);
            if (duration is not null)
                column.Item().AlignCenter().Text($"Total training time: {duration}").FontSize(11);
        });
    }

    private static void ComposeFooter(IContainer container, Certificate cert)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(DividerColor);
            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Text(FormatIssuedOn(cert.IssuedAt)).FontSize(11);
                row.RelativeItem().AlignRight()
                    .Text($"Verification ID: {cert.VerificationId}")
                    .FontSize(9)
                    .FontColor(MutedTextColor);
            });
        });
    }

    /// <summary>"1h 30m", or "45m" under an hour; null when no lesson has a duration.</summary>
    private static string? FormatDuration(int? totalMinutes)
    {
        if (totalMinutes is not > 0)
            return null;

        var hours = totalMinutes.Value / 60;
        var minutes = totalMinutes.Value % 60;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    // The labels are English, so the date is too (a Greek month name would read oddly beside them).
    private static string FormatIssuedOn(DateTime issuedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"Issued on {issuedAt:MMMM d, yyyy}");
}
