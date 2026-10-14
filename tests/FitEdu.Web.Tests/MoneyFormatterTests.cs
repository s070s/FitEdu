using System.Globalization;
using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Prices are euros in both languages: the English site must not print the en-GB pound sign,
/// and the Greek site writes the amount the Greek way.
/// </summary>
public class MoneyFormatterTests
{
    [Fact]
    public void Format_English_PutsTheEuroSignFirst()
    {
        MoneyFormatter.Format(9.99m, "EUR", new CultureInfo("en-GB")).ShouldBe("€9.99");
    }

    [Fact]
    public void Format_Greek_UsesADecimalCommaAndTheSignAfter()
    {
        MoneyFormatter.Format(9.99m, "EUR", new CultureInfo("el-GR")).ShouldBe("9,99 €");
    }

    [Fact]
    public void Format_NoCulture_FollowsTheCurrentCulture()
    {
        using var cultureScope = new CultureScope("el-GR");

        MoneyFormatter.Format(19.99m).ShouldBe("19,99 €");
    }

    [Theory]
    [InlineData("eur", "€")]
    [InlineData(null, "€")]
    [InlineData("CHF", "CHF")]
    public void Symbol_KnownCodeGivesItsSignAndUnknownCodeStaysACode(string? currencyCode, string expected)
    {
        MoneyFormatter.Symbol(currencyCode).ShouldBe(expected);
    }
}
