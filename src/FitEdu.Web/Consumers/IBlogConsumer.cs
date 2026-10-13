using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for the public blog API.
/// </summary>
public interface IBlogConsumer
{
    Task<IReadOnlyList<BlogArticleSummaryDto>?> GetSummariesAsync(int count = 6, string lang = "en", CancellationToken ct = default);
    /// <summary>One article; the lookup also tells a 404 apart from a failed request.</summary>
    Task<ApiLookup<BlogArticleDto>> GetBySlugAsync(string slug, string lang = "en", CancellationToken ct = default);
}
