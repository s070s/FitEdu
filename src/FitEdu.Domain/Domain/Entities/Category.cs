namespace FitEdu.Domain.Entities;

/// <summary>
/// A classification tag shared by Courses and AssessmentDefinitions.
/// Always visible to admins/students, so it has no publish flag.
/// </summary>
public class Category : AuditableEntity, ISoftDeletable
{
    public Guid Id
    {
        get; set;
    }

    /// <summary>
    /// Display name (English).
    /// </summary>
    public required string NameEn
    {
        get; set;
    }

    /// <summary>
    /// Display name (Greek). Falls back to English when null.
    /// </summary>
    public string? NameEl
    {
        get; set;
    }

    // Navigation: one Category has many Courses
    public ICollection<Course> Courses { get; set; } = [];

    // Navigation: one Category has many AssessmentDefinitions
    public ICollection<AssessmentDefinition> AssessmentDefinitions { get; set; } = [];

    /// <summary>
    /// Whether the item is soft-deleted; a global query filter hides soft-deleted rows.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// When the item was soft-deleted. Null while it is live.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
