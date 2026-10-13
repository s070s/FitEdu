using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using QuestPDF.Fluent;
using FitEdu.TestSupport;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Infrastructure.ApiServices;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public partial class CertificateServiceTests
{
    private static CertificateService NewService(ApplicationDbContext db, IFileStorage storage) =>
        new(db, storage, Substitute.For<INotificationDispatcher>(), NullLogger<CertificateService>.Instance);

    private static async Task<(Course course, string userId)> SeedCompletedAsync(
        ApplicationDbContext db, string? displayName = null, int[]? durations = null)
    {
        var user = new ApplicationUser
        {
            Id = "u1",
            UserName = "u@x.com",
            Email = "u@x.com",
            FirstName = "John",
            LastName = "Doe",
            DisplayName = displayName
        };
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C# Basics", TitleEl = null };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        foreach (var d in durations ?? [])
            module.Lessons.Add(new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = module.Id, DurationMinutes = d });
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CourseId = course.Id,
            Status = EnrollmentStatus.Completed
        };
        db.Users.Add(user);
        db.Courses.Add(course);
        db.Modules.Add(module);
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();
        return (course, user.Id);
    }

    private static Certificate ExistingCert(string userId, Guid courseId, string? pdfPath = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CourseId = courseId,
        EnrollmentId = Guid.NewGuid(),
        RecipientName = "John Doe",
        CourseTitleEn = "C# Basics",
        Status = CertificateStatus.Active,
        PdfPath = pdfPath
    };

    [Fact]
    public async Task DeleteUnreferencedPdfs_DeletesOnlyFilesNoCertificateNames()
    {
        // The per-language migration cleared the old paths; those PDFs still hold a member's name.
        await using var db = DbContextFactory.CreateInMemory();
        var english = ExistingCert("u1", Guid.NewGuid(), pdfPath: "certificates/kept-en.pdf");
        var greek = ExistingCert("u2", Guid.NewGuid());
        greek.PdfPathEl = "certificates/kept-el.pdf";
        db.Certificates.AddRange(english, greek);
        await db.SaveChangesAsync();
        var storage = Substitute.For<IFileStorage>();
        storage.ListFiles(CertificateService.PdfFolder).Returns(
            ["certificates/kept-en.pdf", "certificates/kept-el.pdf", "certificates/old.pdf"]);

        var deleted = await NewService(db, storage).DeleteUnreferencedPdfsAsync();

        deleted.ShouldBe(1);
        await storage.Received(1).DeleteFileAsync("certificates/old.pdf", Arg.Any<CancellationToken>());
        await storage.Received(1).DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreate_New_BuildsAndPersistsRowWithoutRenderingPdf()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (course, userId) = await SeedCompletedAsync(db, durations: new[] { 30, 30 });

        var storage = Substitute.For<IFileStorage>();

        var cert = await NewService(db, storage).GetOrCreateAsync(userId, course.Id);

        cert.RecipientName.ShouldBe("John Doe");
        cert.CourseTitleEn.ShouldBe("C# Basics");
        cert.TotalDurationMinutes.ShouldBe(60);
        cert.Status.ShouldBe(CertificateStatus.Active);
        // PERF-4: the PDF is deferred to first download — issuance must not render or store it.
        cert.PdfPath.ShouldBeNull();
        (await db.Certificates.CountAsync()).ShouldBe(1);
        await storage.DidNotReceive().SaveFileAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Βασικά C#", "Βασικά C#")]
    [InlineData(null, "C# Basics")] // no Greek title: the Greek notification shows the English one
    public async Task GetOrCreate_New_NotifiesWithTheEnglishAndGreekTitles(string? greekTitle, string expectedGreekArgument)
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (course, userId) = await SeedCompletedAsync(db);
        course.TitleEl = greekTitle;
        await db.SaveChangesAsync();
        var notifications = Substitute.For<INotificationDispatcher>();
        var service = new CertificateService(db, Substitute.For<IFileStorage>(), notifications, NullLogger<CertificateService>.Instance);

        await service.GetOrCreateAsync(userId, course.Id);

        await notifications.Received(1).DispatchAsync(
            userId,
            NotificationType.CertificateIssued,
            "CertificateIssued",
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "C# Basics", expectedGreekArgument })),
            "/my-certificates",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsurePdf_FirstCall_RendersAndStoresPdf()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (course, userId) = await SeedCompletedAsync(db, durations: new[] { 30, 30 });

        byte[]? pdf = null;
        var storage = Substitute.For<IFileStorage>();
        storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                using var ms = new MemoryStream();
                ci.Arg<Stream>().CopyTo(ms);
                pdf = ms.ToArray();
                return "certificates/cert.pdf";
            });
        var svc = NewService(db, storage);
        var cert = await svc.GetOrCreateAsync(userId, course.Id);
        // The PDF is printed in the request's language; pin English so the title check below holds on any machine.
        using var cultureScope = new CultureScope("en");

        var withPdf = await svc.EnsurePdfAsync(cert.Id);

        withPdf.PdfPath.ShouldBe("certificates/cert.pdf");
        withPdf.PdfPathEl.ShouldBeNull();
        await storage.Received(1).SaveFileAsync(
            Arg.Any<Stream>(), $"{cert.VerificationId}.en.pdf", "certificates", Arg.Any<long?>(), Arg.Any<CancellationToken>());
        pdf.ShouldNotBeNull();
        Encoding.ASCII.GetString(pdf!, 0, 4).ShouldBe("%PDF");
        // The document title readers show in their title bar and file properties.
        Encoding.ASCII.GetString(pdf!).ShouldContain("/Title (Programme Completion Certificate)");
    }

    [Fact]
    public async Task EnsurePdf_ExistingFile_DoesNotRegenerate()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var cert = ExistingCert("u1", Guid.NewGuid(), "certificates/old.pdf");
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();
        var storage = Substitute.For<IFileStorage>();
        storage.FileExists("certificates/old.pdf").Returns(true);
        using var cultureScope = new CultureScope("en");

        var result = await NewService(db, storage).EnsurePdfAsync(cert.Id);

        result.PdfPath.ShouldBe("certificates/old.pdf");
        await storage.DidNotReceive().SaveFileAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsurePdf_EnglishThenGreek_StoresOnePdfPerLanguage()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var cert = ExistingCert("u1", Guid.NewGuid());
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();
        var storage = StorageThatSavesUnderTheRequestedName();
        var service = NewService(db, storage);

        using var cultureScope = new CultureScope("en");
        await service.EnsurePdfAsync(cert.Id);
        using var greekCultureScope = new CultureScope("el");
        var result = await service.EnsurePdfAsync(cert.Id);

        result.PdfPath.ShouldBe($"certificates/{cert.VerificationId}.en.pdf");
        result.PdfPathEl.ShouldBe($"certificates/{cert.VerificationId}.el.pdf");
        CertificateService.PdfPathFor(result, CultureInfo.GetCultureInfo("el")).ShouldBe(result.PdfPathEl);
        CertificateService.PdfPathFor(result, CultureInfo.GetCultureInfo("en-GB")).ShouldBe(result.PdfPath);
    }

    [Fact]
    public async Task EnsurePdf_SecondEnglishDownload_ReusesTheEnglishPdf()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var cert = ExistingCert("u1", Guid.NewGuid());
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();
        var storage = StorageThatSavesUnderTheRequestedName();
        var service = NewService(db, storage);
        using var cultureScope = new CultureScope("en");

        var first = await service.EnsurePdfAsync(cert.Id);
        storage.FileExists(first.PdfPath!).Returns(true);
        var second = await service.EnsurePdfAsync(cert.Id);

        second.PdfPath.ShouldBe(first.PdfPath);
        await storage.Received(1).SaveFileAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsurePdf_StoredFileMissing_RendersAgain()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var cert = ExistingCert("u1", Guid.NewGuid(), "certificates/gone.pdf");
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();
        var storage = StorageThatSavesUnderTheRequestedName();
        using var cultureScope = new CultureScope("en");

        var result = await NewService(db, storage).EnsurePdfAsync(cert.Id);

        result.PdfPath.ShouldBe($"certificates/{cert.VerificationId}.en.pdf");
    }

    /// <summary>A storage mock whose saved path is the folder plus the file name it was asked for.</summary>
    private static IFileStorage StorageThatSavesUnderTheRequestedName()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(call => $"{call.ArgAt<string>(2)}/{call.ArgAt<string>(1)}");
        return storage;
    }

    [Fact]
    public async Task EnsurePdf_Missing_Throws()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => NewService(db, Substitute.For<IFileStorage>()).EnsurePdfAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetOrCreate_PrefersDisplayName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (course, userId) = await SeedCompletedAsync(db, displayName: "Johnny D");
        var storage = Substitute.For<IFileStorage>();
        storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns("certificates/cert.pdf");

        var cert = await NewService(db, storage).GetOrCreateAsync(userId, course.Id);

        cert.RecipientName.ShouldBe("Johnny D");
    }

    [Fact]
    public async Task GetOrCreate_NoDuration_LeavesTotalNull()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (course, userId) = await SeedCompletedAsync(db, durations: []);
        var storage = Substitute.For<IFileStorage>();
        storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns("certificates/cert.pdf");

        var cert = await NewService(db, storage).GetOrCreateAsync(userId, course.Id);

        cert.TotalDurationMinutes.ShouldBeNull();
    }

    [Fact]
    public async Task GetOrCreate_Existing_ReturnsItWithoutRegenerating()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var courseId = Guid.NewGuid();
        var existing = ExistingCert("u1", courseId, "certificates/old.pdf");
        db.Certificates.Add(existing);
        await db.SaveChangesAsync();
        var storage = Substitute.For<IFileStorage>();

        var cert = await NewService(db, storage).GetOrCreateAsync("u1", courseId);

        cert.Id.ShouldBe(existing.Id);
        await storage.DidNotReceive().SaveFileAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("en-GB", "C# Basics", "Βασικά C#", "C# Basics")]   // English culture → English title
    [InlineData("el-GR", "C# Basics", "Βασικά C#", "Βασικά C#")]   // Greek culture → Greek title
    [InlineData("el-GR", "C# Basics", null, "C# Basics")]           // Greek culture, no Greek title → English
    [InlineData("en-GB", "C# Basics", null, "C# Basics")]           // English culture, no Greek title → English
    [InlineData("fr-FR", "C# Basics", "Βασικά C#", "C# Basics")]    // Other culture → English (not Greek)
    public void ResolveCourseTitle_PicksTitleByCulture(string culture, string titleEn, string? titleEl, string expected)
    {
        var result = CertificateService.ResolveCourseTitle(
            titleEn, titleEl, System.Globalization.CultureInfo.GetCultureInfo(culture));

        result.ShouldBe(expected);
    }

    [Fact]
    public async Task GetOrCreate_NoCompletedEnrollment_Throws()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var storage = Substitute.For<IFileStorage>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => NewService(db, storage).GetOrCreateAsync("u1", Guid.NewGuid()));
    }

    [Fact]
    public async Task Revoke_SetsRevokedStatusAndTimestamp()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var cert = ExistingCert("u1", Guid.NewGuid());
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        await NewService(db, Substitute.For<IFileStorage>()).RevokeAsync(cert.Id);

        var reloaded = await db.Certificates.FindAsync(cert.Id);
        reloaded!.Status.ShouldBe(CertificateStatus.Revoked);
        reloaded.RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Revoke_Missing_Throws()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => NewService(db, Substitute.For<IFileStorage>()).RevokeAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Regenerate_DeletesBothLanguageFilesAndClearsBothPaths()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var cert = ExistingCert("u1", Guid.NewGuid(), "certificates/old.en.pdf");
        cert.PdfPathEl = "certificates/old.el.pdf";
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();
        var storage = Substitute.For<IFileStorage>();
        storage.FileExists(Arg.Any<string>()).Returns(true);

        await NewService(db, storage).RegenerateAsync(cert.Id);

        await storage.Received().DeleteFileAsync("certificates/old.en.pdf", Arg.Any<CancellationToken>());
        await storage.Received().DeleteFileAsync("certificates/old.el.pdf", Arg.Any<CancellationToken>());
        var reloaded = (await db.Certificates.FindAsync(cert.Id))!;
        // Both languages render again on their next download.
        reloaded.PdfPath.ShouldBeNull();
        reloaded.PdfPathEl.ShouldBeNull();
        await storage.DidNotReceive().SaveFileAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Regenerate_Missing_Throws()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => NewService(db, Substitute.For<IFileStorage>()).RegenerateAsync(Guid.NewGuid()));
    }

    // ---- Printed certificate -----------------------------------------------------------------

    private const string Lime = "#C6F432";
    private const string Teal = "#2EC4B6";
    private const string Charcoal = "#0E1116";

    private static Certificate PrintableCertificate() => new()
    {
        Id = Guid.NewGuid(),
        VerificationId = Guid.Parse("8d0f6a52-3c1e-4b7a-9f2d-5e6c7b8a9d01"),
        UserId = "u1",
        CourseId = Guid.NewGuid(),
        EnrollmentId = Guid.NewGuid(),
        RecipientName = "Eleni Papadopoulou",
        CourseTitleEn = "Strength Foundations",
        CourseTitleEl = "Θεμέλια Δύναμης",
        TotalDurationMinutes = 90,
        IssuedAt = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc),
        Status = CertificateStatus.Active
    };

    /// <summary>QuestPDF's SVG output of page 1: the same layout as the PDF, with the text readable.</summary>
    private static string RenderFirstPage(Certificate certificate, string cultureName) =>
        CertificateService.CreateDocument(certificate, CultureInfo.GetCultureInfo(cultureName)).GenerateSvg().First();

    /// <summary>Every text run on the page, in drawing order, joined by single spaces.</summary>
    private static string VisibleText(string pageSvg) =>
        string.Join(" ", TextRunRegex().Matches(pageSvg)
            .Select(match => WebUtility.HtmlDecode(match.Groups["text"].Value).Trim())
            .Where(text => text.Length > 0));

    private static string FillOfTextContaining(string pageSvg, string word) =>
        TextRunRegex().Matches(pageSvg)
            .First(match => match.Groups["text"].Value.Contains(word, StringComparison.Ordinal))
            .Groups["fill"].Value;

    [Fact]
    public void RenderedPage_IsTheProgrammeCertificateInTheBrandPalette()
    {
        var page = RenderFirstPage(PrintableCertificate(), "en-GB");
        var text = VisibleText(page);

        text.ShouldContain("Programme Completion Certificate", Case.Sensitive);
        text.ShouldContain("Eleni Papadopoulou", Case.Sensitive);
        text.ShouldContain("has completed the programme", Case.Sensitive);
        text.ShouldContain("Strength Foundations", Case.Sensitive);
        text.ShouldContain("Total training time: 1h 30m", Case.Sensitive);
        text.ShouldContain("Issued on 12 October 2026", Case.Sensitive);
        text.ShouldContain("Verification ID: 8d0f6a52-3c1e-4b7a-9f2d-5e6c7b8a9d01", Case.Sensitive);
        text.ShouldNotContain("Certificate of Completion");

        page.ShouldContain($"<rect fill=\"{Charcoal}\"", Case.Sensitive); // the page background
        FillOfTextContaining(page, "Programme").ShouldBe(Lime);
        FillOfTextContaining(page, "Foundations").ShouldBe(Teal);
    }

    [Fact]
    public void RenderedPage_GreekCulture_PrintsGreekLabelsTitleAndDate()
    {
        var text = VisibleText(RenderFirstPage(PrintableCertificate(), "el-GR"));

        text.ShouldContain("Πιστοποιητικό ολοκλήρωσης προγράμματος", Case.Sensitive);
        text.ShouldContain("Βεβαιώνεται ότι", Case.Sensitive);
        text.ShouldContain("ολοκλήρωσε το πρόγραμμα", Case.Sensitive);
        text.ShouldContain("Θεμέλια Δύναμης", Case.Sensitive);
        text.ShouldContain("Συνολικός χρόνος προπόνησης: 1 ώρ. 30 λεπ.", Case.Sensitive);
        text.ShouldContain("Εκδόθηκε στις 12 Οκτωβρίου 2026", Case.Sensitive);
        // The SVG draws the id's first digit as its own run after Greek text, so check the label and the id apart.
        text.ShouldContain("Κωδικός επαλήθευσης:", Case.Sensitive);
        text.ShouldContain("3c1e-4b7a-9f2d-5e6c7b8a9d01", Case.Sensitive);
        text.ShouldNotContain("Programme Completion Certificate");
        text.ShouldNotContain("Strength Foundations");
    }

    [Fact]
    public void RenderedPage_GreekCultureWithoutGreekTitle_FallsBackToTheEnglishCourseTitle()
    {
        var certificate = PrintableCertificate();
        certificate.CourseTitleEl = null;
        certificate.TotalDurationMinutes = 45;

        var text = VisibleText(RenderFirstPage(certificate, "el"));

        text.ShouldContain("Strength Foundations", Case.Sensitive);
        text.ShouldContain("Συνολικός χρόνος προπόνησης: 45 λεπ.", Case.Sensitive);
    }

    [Fact]
    public void RenderedPage_DrawsTheDumbbellBrandMark()
    {
        var page = RenderFirstPage(PrintableCertificate(), "en-GB");

        // Text runs are <text> elements; the mark's four plates (lime) and its bar (teal) are shapes.
        var shapeFills = ShapeFillRegex().Matches(page).Select(match => match.Groups["fill"].Value).ToList();
        shapeFills.Count(fill => fill == Lime).ShouldBeGreaterThanOrEqualTo(4);
        shapeFills.ShouldContain(Teal);
    }

    [Fact]
    public void BrandMark_IsTheFaviconDumbbell()
    {
        // The PDF cannot read the web root, so the mark is a copy: keep it in step with favicon.svg.
        var favicon = File.ReadAllText(Path.Combine(RepoPaths.WebRoot, "favicon.svg"));
        var faviconShapes = FaviconRectRegex().Matches(favicon).Select(match => match.Value).ToList();

        faviconShapes.Count.ShouldBe(6); // the tile, the bar and four plates
        foreach (var shape in faviconShapes)
            CertificateService.BrandMarkSvg.ShouldContain(shape, Case.Sensitive);
    }

    [GeneratedRegex("<text fill=\"(?<fill>[^\"]+)\"[^>]*>(?<text>[^<]*)</text>")]
    private static partial Regex TextRunRegex();

    [GeneratedRegex("<(?:path|rect)[^>]*?fill=\"(?<fill>[^\"]+)\"")]
    private static partial Regex ShapeFillRegex();

    [GeneratedRegex("<rect[^>]*/>")]
    private static partial Regex FaviconRectRegex();
}
