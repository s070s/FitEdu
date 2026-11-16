namespace FitEdu.Domain.Entities;

/// <summary>
/// Represents a blog article published on the public landing page.
/// Bilingual content: En fields are required; El fields fall back to En when null.
/// Inherits audit tracking (CreatedAt, UpdatedAt) from AuditableEntity and the publish workflow from IPublishable. It is hard-deleted, never soft-deleted.
/// </summary>
public class BlogArticle : AuditableEntity, IPublishable
{
    public Guid Id { get; set; }

    public required string TitleEn { get; set; }
    public string? TitleEl { get; set; }

    public required string Slug { get; set; }

    public required string SummaryEn { get; set; }
    public string? SummaryEl { get; set; }

    public required string ContentEn { get; set; }
    public string? ContentEl { get; set; }

    public string? CoverImageUrl { get; set; }

    public required string AuthorName { get; set; }

    /// <summary>JSON-serialized string[].</summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Whether the item is visible to the public.
    /// </summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// When the item was first published. Null while it is a draft.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }
}
