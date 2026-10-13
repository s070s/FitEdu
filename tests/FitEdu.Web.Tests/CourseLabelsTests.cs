using System.Globalization;
using FitEdu.Domain.Enums;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>Course levels, goals and equipment show CourseRes labels, so Greek pages never show an enum name or code.</summary>
public class CourseLabelsTests
{
    private const char FirstGreekLetter = 'Ά';
    private const char LastGreekLetter = 'ώ';

    [Fact]
    public void Greek_EveryLevelAndGoal_HasAGreekLabel()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");

        var levelLabels = Enum.GetValues<CourseLevel>().Select(CourseLabels.Level);
        var goalLabels = Enum.GetValues<FitnessGoal>().Select(CourseLabels.Goal);

        levelLabels.Concat(goalLabels).ShouldAllBe(label => label.Any(IsGreekLetter));
    }

    [Theory]
    [InlineData("en", CourseLevel.Advanced, "Advanced")]
    [InlineData("el", CourseLevel.Beginner, "Αρχάριο")]
    public void Level_ReturnsTheLabelOfTheCurrentLanguage(string culture, CourseLevel level, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        CourseLabels.Level(level).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", FitnessGoal.MuscleGain, "Muscle gain")]
    [InlineData("el", FitnessGoal.FatLoss, "Απώλεια λίπους")]
    public void Goal_ReturnsTheLabelOfTheCurrentLanguage(string culture, FitnessGoal goal, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        CourseLabels.Goal(goal).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", "none,bands", "No equipment|Bands")]
    [InlineData("el", "dumbbells, mat", "Αλτήρες|Στρωματάκι")]
    public void EquipmentList_LabelsEveryStoredCodeInOrder(string culture, string equipment, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        string.Join("|", CourseLabels.EquipmentList(equipment)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    public void EquipmentList_NothingStored_IsEmpty(string? equipment)
    {
        CourseLabels.EquipmentList(equipment).ShouldBeEmpty();
    }

    [Fact]
    public void Equipment_UnknownCode_IsShownAsItIs()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        CourseLabels.Equipment("rowing machine").ShouldBe("rowing machine");
    }

    private static bool IsGreekLetter(char character) =>
        character >= FirstGreekLetter && character <= LastGreekLetter;
}
