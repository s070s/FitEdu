using System.ComponentModel.DataAnnotations;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.DTOs;

/// <summary>
/// Request record for both create and update of a blog article.
/// TitleEn and SummaryEn are required; El variants are optional and fall back to En when null.
/// </summary>
public record SaveBlogArticleRequest(
    // DQ-3: TitleEn/TitleEl/AuthorName capped to match BlogArticleConfiguration's column
    // lengths — a longer value would pass this check and then throw a SQL truncation error
    // on SaveChanges.
    [Required, MaxLength(200)] string TitleEn,
    [MaxLength(200)] string? TitleEl,
    [Required, MaxLength(200)] string Slug,
    [Required, MaxLength(500)] string SummaryEn,
    [MaxLength(500)] string? SummaryEl,
    [Required] string ContentEn,
    string? ContentEl,
    // The site's content policy only loads images from this site (img-src 'self'), so a cover must be a file the site
    // has: an uploaded one ("blog/covers/x.png") or one shipped in wwwroot ("/images/blog/x.webp"). A full URL, or a
    // protocol-relative one ("//host/x.png", also with leading spaces or backslashes, which browsers read as slashes),
    // would be blocked by the browser and never show (P05-10).
    [MaxLength(500)]
    [RegularExpression(@"^(?!\s*[/\\][/\\])(?![\s\S]*://)[\s\S]*$",
        ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.CoverImageMustBeSiteFile))]
    string? CoverImageUrl,
    [Required, MaxLength(100)] string AuthorName,
    string[]? Tags,
    bool IsPublished
);
