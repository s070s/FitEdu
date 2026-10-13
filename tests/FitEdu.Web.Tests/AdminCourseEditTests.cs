using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the course editor's fitness fields: they load from the course, are sent on save,
/// and a cover exercise slug that is not in the library shows a validation message instead of saving.
/// </summary>
public class AdminCourseEditTests : BunitContext
{
    private readonly IAdminCourseConsumer _courseConsumer = Substitute.For<IAdminCourseConsumer>();
    private readonly IAdminModuleConsumer _moduleConsumer = Substitute.For<IAdminModuleConsumer>();
    private readonly IAdminLessonConsumer _lessonConsumer = Substitute.For<IAdminLessonConsumer>();
    private readonly IAdminCategoryConsumer _categoryConsumer = Substitute.For<IAdminCategoryConsumer>();
    private readonly IExerciseConsumer _exerciseConsumer = Substitute.For<IExerciseConsumer>();
    private SaveCourseRequest? _savedRequest;

    public AdminCourseEditTests()
    {
        Services.AddSingleton(_courseConsumer);
        Services.AddSingleton(_moduleConsumer);
        Services.AddSingleton(_lessonConsumer);
        Services.AddSingleton(_categoryConsumer);
        Services.AddSingleton(_exerciseConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // QuillEditor and the unsaved-changes guard call JS; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _categoryConsumer.GetAllCategoriesAsync().Returns([]);
        _moduleConsumer.GetModulesByCourseAsync(Arg.Any<Guid>()).Returns([]);
        _courseConsumer
            .CreateCourseAsync(Arg.Do<SaveCourseRequest>(request => _savedRequest = request))
            .Returns(call => Course(Guid.NewGuid()));
        _courseConsumer
            .UpdateCourseAsync(Arg.Any<Guid>(), Arg.Do<SaveCourseRequest>(request => _savedRequest = request))
            .Returns(call => Course(call.ArgAt<Guid>(0)));
    }

    private static AdminCourseDto Course(Guid id, string? coverExerciseSlug = null) => new(
        id, "Strength 101", null, null, null, false, DateTimeOffset.UtcNow, null, 0, 0, 0, SubscriptionTier.Free,
        Level: CourseLevel.Intermediate, Goal: FitnessGoal.MuscleGain, Equipment: "dumbbells,mat",
        EstimatedWeeks: 6, SessionsPerWeek: 4, CoverExerciseSlug: coverExerciseSlug);

    private static ApiLookup<ExerciseDetailDto> Detail(string slug) => ApiLookup.Found<ExerciseDetailDto>(new(
        Guid.NewGuid(), slug, "Barbell Squat", "strength", "beginner", "barbell", ["quadriceps"],
        $"/images/exercises/{slug}/0.webp", $"/images/exercises/{slug}/1.webp", [], null, null, []));

    private IRenderedComponent<AdminCourseEdit> RenderNewCourse() =>
        Render<AdminCourseEdit>(parameters => parameters.Add(page => page.CourseId, Guid.Empty));

    private static void ClickSave(IRenderedComponent<AdminCourseEdit> page) =>
        page.FindAll("button").First(button => button.TextContent.Trim() == AdminRes.SaveCourse).Click();

    [Fact]
    public void ExistingCourse_LoadsFitnessFieldsIntoInputs()
    {
        var courseId = Guid.NewGuid();
        _courseConsumer.GetCourseAsync(courseId).Returns(Course(courseId, coverExerciseSlug: "barbell-squat"));

        var page = Render<AdminCourseEdit>(parameters => parameters.Add(p => p.CourseId, courseId));

        page.Find("#course-level").GetAttribute("value").ShouldBe(nameof(CourseLevel.Intermediate));
        page.Find("#course-goal").GetAttribute("value").ShouldBe(nameof(FitnessGoal.MuscleGain));
        page.Find("#course-equipment").GetAttribute("value").ShouldBe("dumbbells,mat");
        page.Find("#course-weeks").GetAttribute("value").ShouldBe("6");
        page.Find("#course-sessions").GetAttribute("value").ShouldBe("4");
        page.Find("#course-cover-exercise").GetAttribute("value").ShouldBe("barbell-squat");
    }

    [Fact]
    public void Save_UnknownCoverExerciseSlug_ShowsValidationMessage_AndDoesNotSave()
    {
        _exerciseConsumer.GetBySlugAsync("no-such-move", Arg.Any<string>()).Returns(ApiLookup.NotFound<ExerciseDetailDto>());
        var page = RenderNewCourse();

        page.Find("#course-title-en").Change("Strength 101");
        page.Find("#course-cover-exercise").Change("no-such-move");
        ClickSave(page);

        page.Find("#course-cover-exercise").ClassList.ShouldContain("is-invalid");
        page.Find("#course-cover-exercise-error").TextContent.ShouldContain(AdminRes.UnknownExerciseSlug);
        _courseConsumer.DidNotReceive().CreateCourseAsync(Arg.Any<SaveCourseRequest>());
    }

    [Fact]
    public void UnknownCoverExerciseSlugError_ClearsWhenTheSlugChanges()
    {
        _exerciseConsumer.GetBySlugAsync("no-such-move", Arg.Any<string>()).Returns(ApiLookup.NotFound<ExerciseDetailDto>());
        var page = RenderNewCourse();
        page.Find("#course-title-en").Change("Strength 101");
        page.Find("#course-cover-exercise").Change("no-such-move");
        ClickSave(page);

        page.Find("#course-cover-exercise").Change("barbell-squat");

        page.Find("#course-cover-exercise").ClassList.ShouldNotContain("is-invalid");
        page.FindAll("#course-cover-exercise-error").ShouldBeEmpty();
    }

    [Fact]
    public void Save_CoverExerciseLookupFails_ShowsTheGenericError_NotUnknownExercise()
    {
        // A failed request says nothing about the slug, so the field must not be marked as wrong.
        _exerciseConsumer.GetBySlugAsync("barbell-squat", Arg.Any<string>()).Returns(ApiLookup.Failure<ExerciseDetailDto>());
        var page = RenderNewCourse();

        page.Find("#course-title-en").Change("Strength 101");
        page.Find("#course-cover-exercise").Change("barbell-squat");
        ClickSave(page);

        page.Markup.ShouldContain(ErrorMessagesRes.UnexpectedErrorTryAgain);
        page.Find("#course-cover-exercise").ClassList.ShouldNotContain("is-invalid");
        page.FindAll("#course-cover-exercise-error").ShouldBeEmpty();
        _courseConsumer.DidNotReceive().CreateCourseAsync(Arg.Any<SaveCourseRequest>());
    }

    [Fact]
    public void Save_UnknownEquipmentCode_NamesTheCode_AndDoesNotSave()
    {
        var page = RenderNewCourse();

        page.Find("#course-title-en").Change("Strength 101");
        page.Find("#course-equipment").Change("dumbbells, Yoga-Mat");
        ClickSave(page);

        page.Find("#course-equipment").ClassList.ShouldContain("is-invalid");
        var error = page.Find("#course-equipment-error").TextContent;
        error.ShouldContain("yoga-mat");
        error.ShouldNotContain("{0}");
        _courseConsumer.DidNotReceive().CreateCourseAsync(Arg.Any<SaveCourseRequest>());
    }

    [Fact]
    public void UnknownEquipmentCodeError_ClearsWhenTheEquipmentChanges()
    {
        var page = RenderNewCourse();
        page.Find("#course-title-en").Change("Strength 101");
        page.Find("#course-equipment").Change("dumbbells, Yoga-Mat");
        ClickSave(page);

        page.Find("#course-equipment").Change("dumbbells, mat");

        page.Find("#course-equipment").ClassList.ShouldNotContain("is-invalid");
        page.FindAll("#course-equipment-error").ShouldBeEmpty();
    }

    [Fact]
    public void EquipmentHint_ListsEveryKnownCode()
    {
        var page = RenderNewCourse();

        var hint = page.Find("#course-equipment-hint").TextContent;

        foreach (var code in FitEdu.Domain.Entities.Course.EquipmentCodes)
            hint.ShouldContain(code);
    }

    [Fact]
    public void Save_SendsFitnessFields()
    {
        _exerciseConsumer.GetBySlugAsync("barbell-squat", Arg.Any<string>()).Returns(Detail("barbell-squat"));
        var page = RenderNewCourse();

        page.Find("#course-title-en").Change("Strength 101");
        page.Find("#course-level").Change(nameof(CourseLevel.Advanced));
        page.Find("#course-goal").Change(nameof(FitnessGoal.Strength));
        page.Find("#course-equipment").Change("barbell,bands");
        page.Find("#course-weeks").Change("8");
        page.Find("#course-sessions").Change("3");
        page.Find("#course-cover-exercise").Change(" barbell-squat ");
        ClickSave(page);

        _savedRequest.ShouldNotBeNull();
        _savedRequest.Level.ShouldBe(CourseLevel.Advanced);
        _savedRequest.Goal.ShouldBe(FitnessGoal.Strength);
        _savedRequest.Equipment.ShouldBe("barbell,bands");
        _savedRequest.EstimatedWeeks.ShouldBe(8);
        _savedRequest.SessionsPerWeek.ShouldBe(3);
        _savedRequest.CoverExerciseSlug.ShouldBe("barbell-squat");
        page.FindAll("#course-cover-exercise-error").ShouldBeEmpty();
        page.FindAll("#course-equipment-error").ShouldBeEmpty();
    }

    [Fact]
    public void Save_CoverSlugWithCapitals_IsCheckedAndSentInTheServersLowercaseForm()
    {
        // The server lower-cases the slug, so the page checks and sends the same form; otherwise it
        // would block "Barbell-Squat" (case-sensitive on SQLite) that the API accepts.
        _exerciseConsumer.GetBySlugAsync("barbell-squat", Arg.Any<string>()).Returns(Detail("barbell-squat"));
        var page = RenderNewCourse();

        page.Find("#course-title-en").Change("Strength 101");
        page.Find("#course-cover-exercise").Change(" Barbell-Squat ");
        ClickSave(page);

        _savedRequest.ShouldNotBeNull();
        _savedRequest.CoverExerciseSlug.ShouldBe("barbell-squat");
        page.FindAll("#course-cover-exercise-error").ShouldBeEmpty();
    }

    [Fact]
    public void Save_BlankCoverExerciseSlug_SkipsTheLibraryCheck()
    {
        var page = RenderNewCourse();

        page.Find("#course-title-en").Change("Strength 101");
        ClickSave(page);

        _savedRequest.ShouldNotBeNull();
        _savedRequest.CoverExerciseSlug.ShouldBeNull();
        _exerciseConsumer.DidNotReceive().GetBySlugAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
