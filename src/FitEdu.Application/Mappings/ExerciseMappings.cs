using System.Linq.Expressions;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;

namespace FitEdu.Application.Mappings;

/// <summary>
/// Exercise library mappers. The list item is projected inside the query (only the columns it
/// needs, never the long instruction texts); detail and lesson prescriptions are materialized.
/// The muscle and instruction columns are joined text, split here into arrays for the UI.
/// </summary>
public static class ExerciseMappings
{
    private const StringSplitOptions TrimAndDropEmpty =
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

    /// <summary>
    /// For IQueryable.Select. The Greek name uses the inline ternary (see <see cref="Localized"/>);
    /// the splitting and URL helpers run on the client because they sit in the final projection.
    /// </summary>
    public static Expression<Func<Exercise, ExerciseListItemDto>> ListItemProjection(bool isEl) =>
        e => new ExerciseListItemDto(
            e.Id,
            e.Slug,
            isEl ? (e.NameEl ?? e.NameEn) : e.NameEn,
            e.Category,
            e.Level,
            e.Equipment,
            SplitMuscles(e.PrimaryMuscles),
            ExerciseImagePaths.StartUrl(e.Slug),
            ExerciseImagePaths.EndUrl(e.Slug, e.ImageCount));

    /// <summary>Materialized detail; the first nine fields match <see cref="ListItemProjection"/>.</summary>
    public static ExerciseDetailDto ToDetailDto(this Exercise exercise, bool isEl) =>
        new(
            exercise.Id,
            exercise.Slug,
            Localized.Pick(isEl, exercise.NameEn, exercise.NameEl),
            exercise.Category,
            exercise.Level,
            exercise.Equipment,
            SplitMuscles(exercise.PrimaryMuscles),
            ExerciseImagePaths.StartUrl(exercise.Slug),
            ExerciseImagePaths.EndUrl(exercise.Slug, exercise.ImageCount),
            SplitMuscles(exercise.SecondaryMuscles),
            exercise.Force,
            exercise.Mechanic,
            PickInstructions(exercise, isEl));

    /// <summary>Materialized prescription; requires the Exercise navigation to be loaded.</summary>
    public static LessonExerciseDto ToLessonExerciseDto(this LessonExercise prescription, bool isEl)
    {
        var exercise = prescription.Exercise;
        return new LessonExerciseDto(
            exercise.Id,
            exercise.Slug,
            Localized.Pick(isEl, exercise.NameEn, exercise.NameEl),
            exercise.Equipment,
            SplitMuscles(exercise.PrimaryMuscles),
            prescription.Sets,
            prescription.Reps,
            prescription.RestSeconds,
            Localized.Pick(isEl, prescription.NotesEn, prescription.NotesEl),
            ExerciseImagePaths.StartUrl(exercise.Slug),
            ExerciseImagePaths.EndUrl(exercise.Slug, exercise.ImageCount),
            PickInstructions(exercise, isEl));
    }

    /// <summary>
    /// The steps in the caller's language. Greek steps that are missing or only blank fall back to
    /// the English ones, as every other bilingual field does (Localized.Pick only checks for null).
    /// </summary>
    private static string[] PickInstructions(Exercise exercise, bool isEl)
    {
        if (isEl && exercise.InstructionsEl is not null)
        {
            var greekSteps = SplitInstructions(exercise.InstructionsEl);
            if (greekSteps.Length > 0)
                return greekSteps;
        }

        return SplitInstructions(exercise.InstructionsEn);
    }

    /// <summary>Splits a muscle column ("chest, triceps") into its names.</summary>
    public static string[] SplitMuscles(string muscles) =>
        muscles.Split(Exercise.MuscleSeparator, TrimAndDropEmpty);

    /// <summary>Splits an instruction column into its steps.</summary>
    public static string[] SplitInstructions(string instructions) =>
        instructions.Split(Exercise.InstructionSeparator, TrimAndDropEmpty);
}
