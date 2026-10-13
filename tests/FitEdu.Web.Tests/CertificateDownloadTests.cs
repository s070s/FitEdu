using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The certificate download speaks the member's language: the PDF's file name and the three
/// problem details (not found, revoked, PDF unavailable).
/// </summary>
public class CertificateDownloadTests : IClassFixture<CustomWebAppFactory>
{
    private const string GreekCulture = "el-GR";
    private const string StudentId = "student-1";

    private readonly CustomWebAppFactory _factory;

    public CertificateDownloadTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Download_InGreek_NamesTheFileInGreek()
    {
        using var cultures = new CultureScope();
        UseCulture(GreekCulture);
        await using var db = DbContextFactory.CreateInMemory();
        var certificate = NewCertificate();
        certificate.PdfPathEl = "certificates/el.pdf";
        db.Certificates.Add(certificate);
        await db.SaveChangesAsync();
        var controller = NewController(db, pdfExists: true);

        var result = await controller.DownloadCertificate(certificate.Id);

        var file = result.ShouldBeOfType<FileStreamResult>();
        file.FileDownloadName.ShouldBe("Πιστοποιητικό - Μαρία - Θεμέλια Δύναμης.pdf");
    }

    [Fact]
    public async Task Download_InEnglish_NamesTheFileInEnglish()
    {
        using var cultures = new CultureScope();
        UseCulture("en");
        await using var db = DbContextFactory.CreateInMemory();
        var certificate = NewCertificate();
        certificate.PdfPath = "certificates/en.pdf";
        db.Certificates.Add(certificate);
        await db.SaveChangesAsync();
        var controller = NewController(db, pdfExists: true);

        var result = await controller.DownloadCertificate(certificate.Id);

        result.ShouldBeOfType<FileStreamResult>().FileDownloadName.ShouldBe("Certificate - Μαρία - Strength Basics.pdf");
    }

    [Fact]
    public async Task Download_Unknown_ExplainsInGreek()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(GreekCulture);

        var response = await client.GetAsync($"/api/certificates/{Guid.NewGuid()}/download");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemDetailOf(response)).ShouldBe(GreekText(nameof(ErrorMessagesRes.Certificate_NotFound)));
    }

    [Fact]
    public async Task Download_Revoked_ExplainsInGreek()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(GreekCulture);
        var certificate = NewCertificate(userId);
        certificate.Status = CertificateStatus.Revoked;
        await _factory.SeedAsync(async seedDb =>
        {
            seedDb.Certificates.Add(certificate);
            await seedDb.SaveChangesAsync();
        });

        var response = await client.GetAsync($"/api/certificates/{certificate.Id}/download");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemDetailOf(response)).ShouldBe(GreekText(nameof(ErrorMessagesRes.Certificate_Revoked)));
    }

    [Fact]
    public async Task Download_PdfMissing_ExplainsInGreek()
    {
        using var cultures = new CultureScope();
        UseCulture(GreekCulture);
        await using var db = DbContextFactory.CreateInMemory();
        var certificate = NewCertificate();
        db.Certificates.Add(certificate);
        await db.SaveChangesAsync();
        var controller = NewController(db, pdfExists: false);

        var result = await controller.DownloadCertificate(certificate.Id);

        var problem = result.ShouldBeOfType<ObjectResult>().Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(StatusCodes.Status404NotFound);
        problem.Detail.ShouldBe(GreekText(nameof(ErrorMessagesRes.Certificate_PdfUnavailable)));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static Certificate NewCertificate(string userId = StudentId) => new()
    {
        Id = Guid.NewGuid(),
        RecipientName = "Μαρία",
        CourseTitleEn = "Strength Basics",
        CourseTitleEl = "Θεμέλια Δύναμης",
        UserId = userId,
        EnrollmentId = Guid.NewGuid()
    };

    /// <summary>A controller whose PDF step returns the stored certificate and whose storage holds an empty file.</summary>
    private static CertificatesController NewController(Infrastructure.Data.ApplicationDbContext db, bool pdfExists)
    {
        var certificates = Substitute.For<ICertificateService>();
        certificates.EnsurePdfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => db.Certificates.Single(c => c.Id == call.Arg<Guid>()));
        var storage = Substitute.For<IFileStorage>();
        storage.FileExists(Arg.Any<string>()).Returns(pdfExists);
        storage.GetFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ((Stream)new MemoryStream(), "application/pdf"));

        var controller = new CertificatesController(
            db, certificates, Substitute.For<ISubscriptionService>(), storage, NullLogger<CertificatesController>.Instance);
        var student = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, StudentId)], "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = student } };
        controller.ProblemDetailsFactory = new TestProblemDetailsFactory();
        return controller;
    }

    private static async Task<string?> ProblemDetailOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail;

    private static string? GreekText(string key) =>
        ErrorMessagesRes.ResourceManager.GetString(key, new CultureInfo(GreekCulture));

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    /// <summary>Builds plain problem details, as the app's factory does, without a service provider.</summary>
    private sealed class TestProblemDetailsFactory : Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext,
            Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelStateDictionary, int? statusCode = null,
            string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };
    }
}
