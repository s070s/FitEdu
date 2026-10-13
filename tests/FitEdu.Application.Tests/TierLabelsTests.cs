using FitEdu.Application.Common;
using FitEdu.Domain.Enums;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class TierLabelsTests
{
    [Theory]
    [InlineData(SubscriptionTier.Free, "Free")]
    [InlineData(SubscriptionTier.Plus, "Plus")]
    [InlineData(SubscriptionTier.Pro, "Pro")]
    public void English_NamesEveryTier(SubscriptionTier tier, string expected)
    {
        using var cultureScope = new CultureScope("en-GB");

        TierLabels.Name(tier).ShouldBe(expected);
    }

    [Theory]
    [InlineData(SubscriptionTier.Free, "Δωρεάν")]
    [InlineData(SubscriptionTier.Plus, "Plus")]
    [InlineData(SubscriptionTier.Pro, "Pro")]
    public void Greek_TranslatesFree_AndKeepsThePlanBrands(SubscriptionTier tier, string expected)
    {
        using var cultureScope = new CultureScope("el-GR");

        TierLabels.Name(tier).ShouldBe(expected);
    }
}
