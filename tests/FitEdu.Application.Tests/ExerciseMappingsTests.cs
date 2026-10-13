using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// A lesson prescription keeps its own Id apart from the exercise's: the workout page keys each row
/// on that Id, and one exercise can appear twice in the same lesson.
/// </summary>
public class ExerciseMappingsTests
{
    private static readonly Guid PrescriptionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ExerciseId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static LessonExercise Prescription()
    {
        var exercise = new Exercise
        {
            Id = ExerciseId,
            Slug = "goblet-squat",
            NameEn = "Goblet squat",
            NameEl = "Κάθισμα goblet",
            Category = "strength",
            Level = "beginner",
            PrimaryMuscles = "quadriceps, glutes",
            SecondaryMuscles = "",
            InstructionsEn = "Hold the weight at your chest.\nSit down between your heels."
        };
        return new LessonExercise
        {
            Id = PrescriptionId,
            ExerciseId = ExerciseId,
            Exercise = exercise,
            Sets = 3,
            Reps = "10",
            RestSeconds = 60
        };
    }

    [Fact]
    public void ToLessonExerciseDto_UsesThePrescriptionIdAsTheRowId()
    {
        var dto = Prescription().ToLessonExerciseDto(isEl: false);

        dto.Id.ShouldBe(PrescriptionId);
        dto.Id.ShouldNotBe(ExerciseId);
    }

    [Fact]
    public void ToLessonExerciseDto_KeepsTheExerciseIdAndSlugForTheLibraryLink()
    {
        var dto = Prescription().ToLessonExerciseDto(isEl: false);

        dto.ExerciseId.ShouldBe(ExerciseId);
        dto.Slug.ShouldBe("goblet-squat");
    }
}
