using FitEdu.Application.Common;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Localized review labels shared by the course card and the course page.
/// </summary>
public static class ReviewLabels
{
    /// <summary>"4.5 out of 5 (1 review)" or "4.5 out of 5 (12 reviews)".</summary>
    public static string RatingSummary(double averageRating, int reviewCount)
    {
        var reviewCountText = ResourceFormat.Count(reviewCount, ReviewRes.ReviewCountOne, ReviewRes.ReviewsCountFormat);
        return ResourceFormat.Fill(ReviewRes.RatingSummaryFormat, averageRating, reviewCountText);
    }
}
