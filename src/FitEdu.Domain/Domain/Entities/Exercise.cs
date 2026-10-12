namespace FitEdu.Domain.Entities;

/// <summary>
/// One exercise from the public-domain Free Exercise DB, with optional Greek text.
/// Images live at wwwroot/images/exercises/{Slug}/0.webp … {ImageCount - 1}.webp.
/// </summary>
public class Exercise : AuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// URL-safe unique key (e.g. "pushups"), used in /exercises/{slug} and the image folder.
    /// </summary>
    public required string Slug { get; set; }

    public required string NameEn { get; set; }

    /// <summary>
    /// Greek name. Falls back to NameEn when null.
    /// </summary>
    public string? NameEl { get; set; }

    /// <summary>
    /// strength | stretching | cardio | plyometrics | powerlifting | strongman | olympic weightlifting.
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// beginner | intermediate | expert.
    /// </summary>
    public required string Level { get; set; }

    /// <summary>
    /// push | pull | static, when known.
    /// </summary>
    public string? Force { get; set; }

    /// <summary>
    /// compound | isolation, when known.
    /// </summary>
    public string? Mechanic { get; set; }

    /// <summary>
    /// Equipment needed (e.g. "dumbbell", "body only"), when known.
    /// </summary>
    public string? Equipment { get; set; }

    /// <summary>
    /// Lowercase muscle names joined with ", " (e.g. "chest, triceps").
    /// </summary>
    public required string PrimaryMuscles { get; set; }

    /// <summary>
    /// Lowercase muscle names joined with ", ". Empty string when there are none.
    /// </summary>
    public required string SecondaryMuscles { get; set; }

    /// <summary>
    /// Step-by-step instructions (English), one step per line.
    /// </summary>
    public required string InstructionsEn { get; set; }

    /// <summary>
    /// Step-by-step instructions (Greek), one step per line. Falls back to InstructionsEn when null.
    /// </summary>
    public string? InstructionsEl { get; set; }

    /// <summary>
    /// Number of position photos (start and end position by default).
    /// </summary>
    public int ImageCount { get; set; } = 2;

    // Navigation: lessons that prescribe this exercise
    public ICollection<LessonExercise> LessonExercises { get; set; } = [];
}
