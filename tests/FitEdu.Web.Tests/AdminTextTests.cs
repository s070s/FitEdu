using System.Globalization;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The admin lists show a title or name in the admin's UI language: the Greek value under the Greek
/// UI when one is set, otherwise the English value.
/// </summary>
public class AdminTextTests
{
    [Fact]
    public void Pick_GreekUiWithGreekValue_ReturnsGreek()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");

        AdminText.Pick("Strength 101", "Δύναμη 101").ShouldBe("Δύναμη 101");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pick_GreekUiWithoutGreekValue_ReturnsEnglish(string? greek)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");

        AdminText.Pick("Strength 101", greek).ShouldBe("Strength 101");
    }

    [Fact]
    public void Pick_EnglishUi_ReturnsEnglishEvenWhenGreekIsSet()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");

        AdminText.Pick("Strength 101", "Δύναμη 101").ShouldBe("Strength 101");
    }

    [Fact]
    public void Pick_NoValueInEitherLanguage_ReturnsNull()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");

        AdminText.Pick(null, null).ShouldBeNull();
    }
}
