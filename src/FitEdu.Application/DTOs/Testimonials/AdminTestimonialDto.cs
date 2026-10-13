namespace FitEdu.Application.DTOs;

/// <summary>
/// Admin read DTO — full testimonial with raw AvatarPath and audit fields.
/// The public list (GET api/testimonials?lang=) returns it with RoleOrTitle and QuoteText
/// already in the requested language.
/// </summary>
public record AdminTestimonialDto(
    Guid Id,
    string FullName,
    string? RoleOrTitle,
    string? CompanyOrContext,
    string QuoteText,
    string? AvatarPath,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? RoleOrTitleEl = null,
    string? QuoteTextEl = null
);
