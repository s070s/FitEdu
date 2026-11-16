namespace FitEdu.Domain.Entities;

/// <summary>
/// An ordered sequence of courses ("Bodyweight Basics → Mobility &amp; Flexibility → Strength Foundations").
/// Publish workflow through <see cref="IPublishable"/>, same as Course.
/// </summary>
public class LearningPath : AuditableEntity, IPublishable, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string TitleEn { get; set; }

    public string? TitleEl { get; set; }

    public string? DescriptionEn { get; set; }

    public string? DescriptionEl { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>Controls display order on the /paths catalog page. Lower = first.</summary>
    public int DisplayOrder { get; set; }

    public ICollection<LearningPathStep> Steps { get; set; } = [];

    /// <summary>
    /// Whether the item is visible to the public.
    /// </summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// When the item was first published. Null while it is a draft.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Whether the item is soft-deleted; a global query filter hides soft-deleted rows.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// When the item was soft-deleted. Null while it is live.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
