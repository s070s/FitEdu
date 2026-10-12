namespace FitEdu.Domain.Entities;

/// <summary>
/// Represents a testimonial displayed on the public landing page.
/// Managed entirely by admins — no publish workflow, just active/inactive toggle and display order.
/// </summary>
public class Testimonial
{
    public Guid Id { get; set; }

    public required string FullName { get; set; }

    public string? RoleOrTitle { get; set; }

    /// <summary>Greek role or title. Falls back to RoleOrTitle when null.</summary>
    public string? RoleOrTitleEl { get; set; }

    public string? CompanyOrContext { get; set; }

    public required string QuoteText { get; set; }

    /// <summary>Greek quote. Falls back to QuoteText when null.</summary>
    public string? QuoteTextEl { get; set; }

    /// <summary>Relative file path within App_Data/Uploads (e.g. "testimonials/avatars/file.jpg"). Null = no avatar.</summary>
    public string? AvatarPath { get; set; }

    /// <summary>Controls display sequence on the landing page. Lower number = displayed first.</summary>
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
