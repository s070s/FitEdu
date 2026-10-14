using FitEdu.TestSupport;
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
        using var cultureScope = new CultureScope("el-GR");

        AdminText.Pick("Strength 101", "Δύναμη 101").ShouldBe("Δύναμη 101");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pick_GreekUiWithoutGreekValue_ReturnsEnglish(string? greek)
    {
        using var cultureScope = new CultureScope("el");

        AdminText.Pick("Strength 101", greek).ShouldBe("Strength 101");
    }

    [Fact]
    public void Pick_EnglishUi_ReturnsEnglishEvenWhenGreekIsSet()
    {
        using var cultureScope = new CultureScope("en-GB");

        AdminText.Pick("Strength 101", "Δύναμη 101").ShouldBe("Strength 101");
    }

    [Fact]
    public void Pick_NoValueInEitherLanguage_ReturnsNull()
    {
        using var cultureScope = new CultureScope("el");

        AdminText.Pick(null, null).ShouldBeNull();
    }
}
