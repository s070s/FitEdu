using Ganss.Xss;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using FitEdu.Web.Controllers;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// DB-10: deleting a lesson, a module or an assessment definition removes the rows that depend on it. The lesson and
/// module controllers leave the completion records to the database cascade; the assessment controller deletes the
/// submissions itself because that foreign key is Restrict. SQLite enforces foreign keys and cascades like SQL Server,
/// so these tests fail if a delete leaves a dependent row behind or is refused.
/// </summary>
public sealed class AdminDeleteCascadeTests : IDisposable
{
    private readonly ApplicationDbContext _db = DbContextFactory.CreateSqlite();

    public void Dispose() => _db.Dispose();

    private async Task<(Module Module, Lesson Lesson)> SeedCompletedLessonAsync()
    {
        var user = new ApplicationUser { Id = "u1", UserName = "u@x.com", Email = "u@x.com", FirstName = "A", LastName = "B" };
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = module.Id };
        _db.Users.Add(user);
        _db.Courses.Add(course);
        _db.Modules.Add(module);
        _db.Lessons.Add(lesson);
        _db.LessonCompletions.Add(new LessonCompletion { Id = Guid.NewGuid(), UserId = user.Id, LessonId = lesson.Id });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (module, lesson);
    }

    [Fact]
    public async Task DeleteLesson_RemovesItsCompletionRecords()
    {
        var (_, lesson) = await SeedCompletedLessonAsync();
        var controller = new AdminLessonsController(
            _db, Substitute.For<IFileStorage>(), Substitute.For<IAdminCourseService>(), NullLogger<AdminLessonsController>.Instance);

        (await controller.DeleteLesson(lesson.Id)).ShouldBeOfType<NoContentResult>();

        (await _db.Lessons.CountAsync()).ShouldBe(0);
        (await _db.LessonCompletions.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteModule_RemovesItsLessonsAndTheirCompletionRecords()
    {
        var (module, _) = await SeedCompletedLessonAsync();
        var controller = new AdminModulesController(_db, new HtmlSanitizer());

        (await controller.DeleteModule(module.Id)).ShouldBeOfType<NoContentResult>();

        (await _db.Modules.CountAsync()).ShouldBe(0);
        (await _db.Lessons.CountAsync()).ShouldBe(0);
        (await _db.LessonCompletions.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteAssessment_RemovesItsSubmissions()
    {
        var user = new ApplicationUser { Id = "u1", UserName = "u@x.com", Email = "u@x.com", FirstName = "A", LastName = "B" };
        var assessment = new AssessmentDefinition { Id = Guid.NewGuid(), Key = "k", TitleEn = "T", SchemaJson = "{}" };
        _db.Users.Add(user);
        _db.AssessmentDefinitions.Add(assessment);
        _db.AssessmentSubmissions.Add(new AssessmentSubmission
        {
            Id = Guid.NewGuid(), AssessmentDefinitionId = assessment.Id, UserId = user.Id, AnswersJson = "{}"
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var controller = new AdminAssessmentsController(
            _db, NullLogger<AdminAssessmentsController>.Instance, new HtmlSanitizer());

        (await controller.DeleteAssessment(assessment.Id)).ShouldBeOfType<NoContentResult>();

        (await _db.AssessmentDefinitions.CountAsync()).ShouldBe(0);
        (await _db.AssessmentSubmissions.CountAsync()).ShouldBe(0);
    }
}
