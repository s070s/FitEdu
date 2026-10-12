using System.Globalization;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// UserStatus is repurposed as the user's training level (same stored ints), so the register and
/// profile selects must offer the fitness levels in both languages.
/// </summary>
public class UserStatusLabelsTests
{
    [Fact]
    public void Selectable_OffersTrainingLevelsAndOther()
    {
        UserStatusLabels.Selectable.ShouldBe(
        [
            UserStatus.Beginner,
            UserStatus.Intermediate,
            UserStatus.Advanced,
            UserStatus.Other
        ]);
    }

    [Theory]
    [InlineData(UserStatus.Beginner, "Beginner")]
    [InlineData(UserStatus.Intermediate, "Intermediate")]
    [InlineData(UserStatus.Advanced, "Advanced")]
    [InlineData(UserStatus.Other, "Prefer not to say")]
    public void Label_English_ReturnsTrainingLevelName(UserStatus status, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        UserStatusLabels.Label(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserStatus.Beginner, "Αρχάριος/α")]
    [InlineData(UserStatus.Intermediate, "Μεσαίο επίπεδο")]
    [InlineData(UserStatus.Advanced, "Προχωρημένος/η")]
    [InlineData(UserStatus.Other, "Προτιμώ να μην απαντήσω")]
    public void Label_Greek_ReturnsTrainingLevelName(UserStatus status, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");

        UserStatusLabels.Label(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", "Training level")]
    [InlineData("el", "Επίπεδο προπόνησης")]
    public void StatusFieldLabel_IsTrainingLevel(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        GlobalRes.Label_Info_Status.ShouldBe(expected);
    }
}
