using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// <see cref="ReviewLabels.RatingSummary"/> names one review in the singular ("1 review", «1 κριτική»).
/// </summary>
public class ReviewLabelsTests
{
    [Theory]
    [InlineData("en", 1, "4.5 out of 5 (1 review)")]
    [InlineData("en", 12, "4.5 out of 5 (12 reviews)")]
    [InlineData("el", 1, "4,5 από 5 (1 κριτική)")]
    [InlineData("el", 12, "4,5 από 5 (12 κριτικές)")]
    public void RatingSummary_UsesTheSingularForOneReview(string culture, int reviewCount, string expected)
    {
        using var cultureScope = new CultureScope(culture);

        ReviewLabels.RatingSummary(4.5, reviewCount).ShouldBe(expected);
    }
}
