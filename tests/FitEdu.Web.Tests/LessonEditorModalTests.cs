using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Web.Consumers;
using FitEdu.Web.Shared.Components.Forms;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the lesson editor's exercise picker: existing rows load in order, the search
/// adds library exercises, rows can be reordered and removed, and Save sends the whole list.
/// </summary>
public class LessonEditorModalTests : BunitContext
{
    private readonly IAdminLessonConsumer _lessonConsumer = Substitute.For<IAdminLessonConsumer>();
    private readonly IExerciseConsumer _exerciseConsumer = Substitute.For<IExerciseConsumer>();
    private readonly Guid _moduleId = Guid.NewGuid();
    private SaveLessonRequest? _savedRequest;

    public LessonEditorModalTests()
    {
        Services.AddSingleton(_lessonConsumer);
        Services.AddSingleton(_exerciseConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // QuillEditor and FormModal call JS on first render; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _lessonConsumer
            .UpdateLessonAsync(Arg.Any<Guid>(), Arg.Do<SaveLessonRequest>(request => _savedRequest = request))
            .Returns(call => Lesson([]));
        _lessonConsumer
            .CreateLessonAsync(Arg.Do<SaveLessonRequest>(request => _savedRequest = request))
            .Returns(call => Lesson([]));
    }

    private AdminLessonDto Lesson(List<LessonExerciseEditDto> exercises) => new(
        Guid.NewGuid(), "Leg day", null, null, null, null, null, 30, 1, _moduleId, false, exercises);

    private static ExerciseListItemDto ListItem(string slug, string name) => new(
        Guid.NewGuid(), slug, name, "strength", "beginner", "body only", ["chest"],
        $"/images/exercises/{slug}/0.webp", $"/images/exercises/{slug}/1.webp");

    private IRenderedComponent<LessonEditorModal> RenderModal(AdminLessonDto? editingLesson) =>
        Render<LessonEditorModal>(parameters => parameters
            .Add(modal => modal.IsVisible, true)
            .Add(modal => modal.EditingLesson, editingLesson)
            .Add(modal => modal.ModuleId, _moduleId));

    private static void ClickSave(IRenderedComponent<LessonEditorModal> modal) =>
        modal.Find(".modal-footer .btn-primary").Click();

    [Fact]
    public void EditingLesson_ShowsItsExerciseRowsInOrder()
    {
        var modal = RenderModal(Lesson(
        [
            new LessonExerciseEditDto("barbell-squat", Sets: 5, Name: "Barbell Squat"),
            new LessonExerciseEditDto("plank", Reps: "30 s", Name: "Plank")
        ]));

        var rows = modal.FindAll(".lesson-exercise-row");
        rows.Count.ShouldBe(2);
        rows[0].TextContent.ShouldContain("Barbell Squat");
        rows[1].TextContent.ShouldContain("Plank");
        modal.Find("#exercise-0-sets").GetAttribute("value").ShouldBe("5");
        modal.Find("#exercise-1-reps").GetAttribute("value").ShouldBe("30 s");
    }

    [Fact]
    public void LessonWithoutExercises_ShowsEmptyHint()
    {
        var modal = RenderModal(Lesson([]));

        modal.FindAll(".lesson-exercise-row").ShouldBeEmpty();
        modal.Markup.ShouldContain(FitEdu.Shared.Resources.AdminRes.NoLessonExercises);
    }

    [Fact]
    public void SearchAndPick_AddsExercise_AndSaveSendsEditedRowsInOrder()
    {
        _exerciseConsumer
            .ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Is<ExerciseFilterDto?>(filter => filter!.Search == "push"))
            .Returns(new PagedResult<ExerciseListItemDto>([ListItem("push-ups", "Push-Ups")], 1, 1, 10));
        var modal = RenderModal(Lesson([new LessonExerciseEditDto("barbell-squat", Sets: 5, Name: "Barbell Squat")]));

        modal.Find("#exercise-search").Input("push");
        modal.Find(".exercise-search-button").Click();
        modal.Find(".exercise-search-result").Click();
        modal.Find("#exercise-1-sets").Change("3");
        modal.Find("#exercise-1-reps").Change("10");
        modal.Find("#exercise-1-rest").Change("60");
        modal.Find("#exercise-1-notes-en").Change("Elbows at 45°");
        modal.Find("#exercise-1-notes-el").Change("Αγκώνες στις 45°");
        ClickSave(modal);

        _savedRequest.ShouldNotBeNull();
        var rows = _savedRequest.Exercises!;
        rows.Select(row => row.Slug).ShouldBe(["barbell-squat", "push-ups"]);
        rows[0].Sets.ShouldBe(5);
        rows[1].Sets.ShouldBe(3);
        rows[1].Reps.ShouldBe("10");
        rows[1].RestSeconds.ShouldBe(60);
        rows[1].NotesEn.ShouldBe("Elbows at 45°");
        rows[1].NotesEl.ShouldBe("Αγκώνες στις 45°");
    }

    [Fact]
    public void Search_WithNoMatches_ShowsNoResultsMessage()
    {
        _exerciseConsumer
            .ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ExerciseFilterDto?>())
            .Returns(new PagedResult<ExerciseListItemDto>([], 0, 1, 10));
        var modal = RenderModal(Lesson([]));

        modal.Find("#exercise-search").Input("zzz");
        modal.Find(".exercise-search-button").Click();

        modal.FindAll(".exercise-search-result").ShouldBeEmpty();
        modal.Markup.ShouldContain(FitEdu.Shared.Resources.AdminRes.NoExercisesFound);
    }

    [Fact]
    public void MoveAndRemove_ChangeTheOrderThatSaveSends()
    {
        var modal = RenderModal(Lesson(
        [
            new LessonExerciseEditDto("a-move", Name: "A"),
            new LessonExerciseEditDto("b-move", Name: "B"),
            new LessonExerciseEditDto("c-move", Name: "C")
        ]));

        modal.FindAll(".exercise-move-up")[2].Click();   // A, C, B
        modal.FindAll(".exercise-move-down")[0].Click(); // C, A, B
        modal.FindAll(".exercise-remove")[1].Click();    // C, B
        ClickSave(modal);

        _savedRequest!.Exercises!.Select(row => row.Slug).ShouldBe(["c-move", "b-move"]);
    }

    [Fact]
    public void FirstRowCannotMoveUp_AndLastRowCannotMoveDown()
    {
        var modal = RenderModal(Lesson(
        [
            new LessonExerciseEditDto("a-move", Name: "A"),
            new LessonExerciseEditDto("b-move", Name: "B")
        ]));

        modal.FindAll(".exercise-move-up")[0].HasAttribute("disabled").ShouldBeTrue();
        modal.FindAll(".exercise-move-down")[1].HasAttribute("disabled").ShouldBeTrue();
        modal.FindAll(".exercise-move-up")[1].HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void NewLesson_SavesWithPickedExercises()
    {
        _exerciseConsumer
            .ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ExerciseFilterDto?>())
            .Returns(new PagedResult<ExerciseListItemDto>([ListItem("plank", "Plank")], 1, 1, 10));
        var modal = RenderModal(editingLesson: null);

        modal.Find("#lesson-title-en").Change("Core basics");
        modal.Find("#exercise-search").Input("plank");
        modal.Find(".exercise-search-button").Click();
        modal.Find(".exercise-search-result").Click();
        ClickSave(modal);

        _savedRequest!.TitleEn.ShouldBe("Core basics");
        _savedRequest.Exercises!.Single().Slug.ShouldBe("plank");
    }
}
