using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
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
    [InlineData(UserStatus.Unknown, "Not set")]
    public void Label_English_ReturnsTrainingLevelName(UserStatus status, string expected)
    {
        using var cultureScope = new CultureScope("en");

        UserStatusLabels.Label(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserStatus.Beginner, "Αρχάριο επίπεδο")]
    [InlineData(UserStatus.Intermediate, "Μεσαίο επίπεδο")]
    [InlineData(UserStatus.Advanced, "Προχωρημένο επίπεδο")]
    [InlineData(UserStatus.Other, "Προτιμώ να μην απαντήσω")]
    [InlineData(UserStatus.Unknown, "Δεν ορίστηκε")]
    public void Label_Greek_ReturnsTrainingLevelName(UserStatus status, string expected)
    {
        using var cultureScope = new CultureScope("el");

        UserStatusLabels.Label(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Other", "Prefer not to say")]
    [InlineData("Advanced", "Advanced")]
    [InlineData("NotAStatus", "NotAStatus")]
    public void Label_StoredName_ReturnsTheLocalizedLabelOrTheNameItself(string statusName, string expected)
    {
        // The admin user list receives the level as its enum name (AdminUserDto.Status).
        using var cultureScope = new CultureScope("en");

        UserStatusLabels.Label(statusName).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", "Training level")]
    [InlineData("el", "Επίπεδο προπόνησης")]
    public void StatusFieldLabel_IsTrainingLevel(string culture, string expected)
    {
        using var cultureScope = new CultureScope(culture);

        GlobalRes.Label_Info_Status.ShouldBe(expected);
    }
}
