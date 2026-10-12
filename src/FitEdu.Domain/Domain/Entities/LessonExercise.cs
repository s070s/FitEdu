namespace FitEdu.Domain.Entities;

/// <summary>
/// Prescription of one exercise inside a lesson (sets, reps, rest).
/// Deleted together with its lesson; an exercise in use cannot be deleted.
/// </summary>
public class LessonExercise
{
    public Guid Id { get; set; }

    public Guid LessonId { get; set; }

    public Lesson Lesson { get; set; } = null!;

    public Guid ExerciseId { get; set; }

    public Exercise Exercise { get; set; } = null!;

    /// <summary>
    /// Determines display order within the lesson.
    /// </summary>
    public int SortOrder { get; set; }

    public int? Sets { get; set; }

    /// <summary>
    /// Free text so it can hold ranges or durations (e.g. "8–12", "30 s", "AMRAP").
    /// </summary>
    public string? Reps { get; set; }

    public int? RestSeconds { get; set; }

    public string? NotesEn { get; set; }

    /// <summary>
    /// Greek coaching notes. Falls back to NotesEn when null.
    /// </summary>
    public string? NotesEl { get; set; }
}
