using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Domain.Entities;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Web.Controllers;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>P05-19: the asset endpoint's two quiet-failure rules, checked on the controller itself.</summary>
public sealed class LessonAssetsControllerTests : IDisposable
{
    private readonly FitEdu.Infrastructure.Data.ApplicationDbContext _db = DbContextFactory.CreateInMemory();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly CapturingLogger _logger = new();

    public void Dispose() => _db.Dispose();

    private LessonAssetsController NewController(ClaimsPrincipal? user = null)
    {
        var controller = new LessonAssetsController(_db, _storage, new EphemeralDataProtectionProvider(), _logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) }
            },
            ProblemDetailsFactory = new PlainProblemDetailsFactory()
        };
        return controller;
    }

    [Fact]
    public async Task BadToken_IsLoggedAtDebugWithoutTheException()
    {
        var controller = NewController();

        var result = await controller.GetAsset(Guid.NewGuid(), "pdf", "garbage-token");

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task FileDeletedBetweenTheCheckAndTheOpen_Is404NotA500()
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C", IsPublished = true };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = module.Id, PdfPath = "lessons/pdf/gone.pdf" };
        module.Lessons.Add(lesson);
        course.Modules.Add(module);
        _db.Courses.Add(course);
        _db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = "member-1", CourseId = course.Id });
        await _db.SaveChangesAsync();
        _storage.GetFileInfo("lessons/pdf/gone.pdf").Returns(new StoredFileInfo(10, DateTimeOffset.UtcNow));
        _storage.GetFileAsync("lessons/pdf/gone.pdf", Arg.Any<CancellationToken>())
            .ThrowsAsync(new FileNotFoundException("deleted just now"));
        var member = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "member-1")], "Test"));

        var result = await NewController(member).GetAsset(lesson.Id, "pdf", null);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        problem.Value.ShouldBeOfType<ProblemDetails>().Detail.ShouldBe(ErrorMessagesRes.LessonFileNotFound);
    }

    private sealed class CapturingLogger : ILogger<LessonAssetsController>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }

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
