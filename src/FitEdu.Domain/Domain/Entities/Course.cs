using FitEdu.Domain.Enums;

namespace FitEdu.Domain.Entities;

/// <summary>
/// Represents a full learning program (e.g. "FitEdu").
/// Independent of UI or pricing. Contains ordered Modules.
/// </summary>
public class Course : AuditableEntity
{
    /// <summary>
    /// Joins the codes stored in Equipment (e.g. "none,bands").
    /// </summary>
    public const string EquipmentSeparator = ",";

    /// <summary>
    /// The equipment codes a course may list; the catalogue filter and its labels know only these.
    /// </summary>
    public static readonly IReadOnlyList<string> EquipmentCodes =
        ["none", "dumbbells", "barbell", "kettlebell", "bands", "mat", "bike", "chair", "cables", "machine"];

    public Guid Id
    {
        get; set;
    }

    /// <summary>
    /// Display title of the course (English).
    /// </summary>
    public required string TitleEn
    {
        get; set;
    }

    /// <summary>
    /// Display title of the course (Greek). Falls back to English when null.
    /// </summary>
    public string? TitleEl
    {
        get; set;
    }

    /// <summary>
    /// Optional description for catalog/marketing (English).
    /// </summary>
    public string? DescriptionEn
    {
        get; set;
    }

    /// <summary>
    /// Optional description for catalog/marketing (Greek). Falls back to English when null.
    /// </summary>
    public string? DescriptionEl
    {
        get; set;
    }

    /// <summary>
    /// Minimum subscription tier required to access this course.
    /// Default is Free (all users can access).
    /// </summary>
    public SubscriptionTier RequiredTier { get; set; } = SubscriptionTier.Free;

    /// <summary>
    /// Optional category classification. Null means uncategorized.
    /// </summary>
    public Guid? CategoryId
    {
        get; set;
    }

    public Category? Category
    {
        get; set;
    }

    /// <summary>
    /// Difficulty of the programme. Stored as int.
    /// </summary>
    public CourseLevel Level { get; set; } = CourseLevel.Beginner;

    /// <summary>
    /// Main training goal of the programme. Stored as int.
    /// </summary>
    public FitnessGoal Goal { get; set; } = FitnessGoal.GeneralFitness;

    /// <summary>
    /// Comma-joined equipment codes (none, dumbbells, barbell, kettlebell, bands, mat, bike,
    /// chair, cables, machine). Null means not specified.
    /// </summary>
    public string? Equipment
    {
        get; set;
    }

    /// <summary>
    /// Suggested programme length in weeks.
    /// </summary>
    public int? EstimatedWeeks
    {
        get; set;
    }

    /// <summary>
    /// Suggested number of workouts per week.
    /// </summary>
    public int? SessionsPerWeek
    {
        get; set;
    }

    /// <summary>
    /// Slug of the exercise whose first photo is used as the course cover. Null = default cover.
    /// </summary>
    public string? CoverExerciseSlug
    {
        get; set;
    }

    // Navigation: one Course has many Modules
    public ICollection<Module> Modules { get; set; } = [];

    // Navigation: one Course has many Enrollments
    public ICollection<Enrollment> Enrollments { get; set; } = [];
}
