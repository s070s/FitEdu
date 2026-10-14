using System.Globalization;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Domain.Tests;

/// <summary>
/// The member's stored language is "en" or "el"; every culture that is not Greek reads as English,
/// the site's default language.
/// </summary>
public class UserLanguageTests
{
    [Fact]
    public void NewUser_PrefersEnglish()
    {
        var user = new ApplicationUser { FirstName = "Test", LastName = "User" };

        user.PreferredLanguage.ShouldBe(UserLanguage.English);
    }

    [Theory]
    [InlineData("el-GR", "el")]
    [InlineData("el", "el")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "en")]
    public void FromCulture_GreekCultureIsGreekAndAnyOtherIsEnglish(string cultureName, string expectedLanguage)
    {
        UserLanguage.FromCulture(new CultureInfo(cultureName)).ShouldBe(expectedLanguage);
    }

    [Theory]
    [InlineData("el-GR", "el")]
    [InlineData("EL", "el")]
    [InlineData("en-GB", "en")]
    [InlineData("not-a-culture", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void FromCultureName_ReadsTheLanguagePartOfTheName(string? cultureName, string expectedLanguage)
    {
        UserLanguage.FromCultureName(cultureName).ShouldBe(expectedLanguage);
    }

    [Theory]
    [InlineData("el", "el-GR")]
    [InlineData("en", "en-GB")]
    public void ToCultureName_IsTheSiteCultureForTheLanguage(string language, string expectedCultureName)
    {
        UserLanguage.ToCultureName(language).ShouldBe(expectedCultureName);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("el", true)]
    [InlineData("EL", false)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void IsSupported_AcceptsOnlyTheTwoStoredCodes(string? language, bool expected)
    {
        UserLanguage.IsSupported(language).ShouldBe(expected);
    }
}
