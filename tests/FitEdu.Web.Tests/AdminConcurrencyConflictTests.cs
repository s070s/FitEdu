using System.Security.Claims;
using Ganss.Xss;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Web.Controllers;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// DB-7: an admin edit of a module, assessment, blog article or testimonial that another admin changed in the
/// meantime answers 409 with the conflict text and keeps the other admin's version. A stale original RowVersion is
/// what a concurrent SQL Server write leaves behind, so the UPDATE's WHERE clause matches no row.
/// (The course and category edits are covered in the Application tests, where their services return the 409.)
/// </summary>
public sealed class AdminConcurrencyConflictTests : IDisposable
{
    private readonly ApplicationDbContext _db = DbContextFactory.CreateInMemory();

    public void Dispose() => _db.Dispose();

    private static T WithContext<T>(T controller) where T : ControllerBase
    {
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = admin } };
        controller.ProblemDetailsFactory = new PlainProblemDetailsFactory();
        return controller;
    }

    private async Task MakeStaleAsync(object entity)
    {
        _db.Add(entity);
        await _db.SaveChangesAsync();
        _db.Entry(entity).OriginalValues["RowVersion"] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
    }

    private static void ShouldBeConflict(IActionResult result)
    {
        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        objectResult.Value.ShouldBeOfType<ProblemDetails>().Detail.ShouldBe(ErrorMessagesRes.ModifiedConcurrently);
    }

    [Fact]
    public async Task UpdateModule_ChangedByAnotherAdminMeanwhile_Returns409_AndKeepsTheirVersion()
    {
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "Old", CourseId = Guid.NewGuid() };
        await MakeStaleAsync(module);
        var controller = WithContext(new AdminModulesController(_db, new HtmlSanitizer()));

        var result = await controller.UpdateModule(module.Id, new SaveModuleRequest("Renamed", null, null, null, 1, module.CourseId));

        ShouldBeConflict(result.Result!);
        (await _db.Modules.AsNoTracking().SingleAsync(m => m.Id == module.Id)).TitleEn.ShouldBe("Old");
    }

    [Fact]
    public async Task UpdateAssessment_ChangedByAnotherAdminMeanwhile_Returns409_AndKeepsTheirVersion()
    {
        var assessment = new AssessmentDefinition { Id = Guid.NewGuid(), Key = "old-key", TitleEn = "Old", SchemaJson = "{}" };
        await MakeStaleAsync(assessment);
        var controller = WithContext(new AdminAssessmentsController(_db, new HtmlSanitizer()));
        var request = new SaveAssessmentDefinitionRequest("old-key", "Renamed", null, null, null, """{"questions":[]}""");

        var result = await controller.UpdateAssessment(assessment.Id, request);

        ShouldBeConflict(result.Result!);
        (await _db.AssessmentDefinitions.AsNoTracking().SingleAsync(a => a.Id == assessment.Id)).TitleEn.ShouldBe("Old");
    }

    [Fact]
    public async Task UpdateBlogArticle_ChangedByAnotherAdminMeanwhile_Returns409_AndKeepsTheirVersion()
    {
        var article = new BlogArticle
        {
            Id = Guid.NewGuid(), TitleEn = "Old", Slug = "old", SummaryEn = "S", ContentEn = "C", AuthorName = "A"
        };
        await MakeStaleAsync(article);
        var service = new BlogArticleService(_db, NullLogger<BlogArticleService>.Instance, new HtmlSanitizer());
        var controller = WithContext(new AdminBlogController(
            service, Substitute.For<IFileStorage>(), _db, NullLogger<AdminBlogController>.Instance));
        var request = new SaveBlogArticleRequest("Renamed", null, "old", "S", null, "C", null, null, "A", null, false);

        var result = await controller.UpdateBlogArticle(article.Id, request);

        ShouldBeConflict(result.Result!);
        (await _db.BlogArticles.AsNoTracking().SingleAsync(a => a.Id == article.Id)).TitleEn.ShouldBe("Old");
    }

    [Fact]
    public async Task UpdateTestimonial_ChangedByAnotherAdminMeanwhile_Returns409_AndKeepsTheirVersion()
    {
        var testimonial = new Testimonial { Id = Guid.NewGuid(), FullName = "Old", QuoteText = "Q" };
        await MakeStaleAsync(testimonial);
        var service = new TestimonialService(_db, NullLogger<TestimonialService>.Instance);
        var controller = WithContext(new AdminTestimonialsController(
            service, Substitute.For<IFileStorage>(), NullLogger<AdminTestimonialsController>.Instance));

        var result = await controller.Update(testimonial.Id, new SaveTestimonialRequest("Renamed", null, null, "Q", 1, true));

        ShouldBeConflict(result.Result!);
        (await _db.Testimonials.AsNoTracking().SingleAsync(t => t.Id == testimonial.Id)).FullName.ShouldBe("Old");
    }

    /// <summary>Builds plain problem details, as the app's factory does, without a service provider.</summary>
    private sealed class PlainProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext,
            ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null, string? type = null,
            string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };
    }
}
