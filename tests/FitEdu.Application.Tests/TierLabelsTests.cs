using System.Globalization;
using FitEdu.Application.Common;
using FitEdu.Domain.Enums;
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
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");

        TierLabels.Name(tier).ShouldBe(expected);
    }

    [Theory]
    [InlineData(SubscriptionTier.Free, "Δωρεάν")]
    [InlineData(SubscriptionTier.Plus, "Plus")]
    [InlineData(SubscriptionTier.Pro, "Pro")]
    public void Greek_TranslatesFree_AndKeepsThePlanBrands(SubscriptionTier tier, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");

        TierLabels.Name(tier).ShouldBe(expected);
    }
}
