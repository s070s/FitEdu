using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Course levels, goals and equipment codes show CourseRes labels, so Greek pages never show an
/// enum name or a raw code.
/// </summary>
public class CourseLabelsTests
{
    private const char FirstGreekLetter = 'Ά';
    private const char LastGreekLetter = 'ώ';

    [Fact]
    public void Greek_EveryLevelAndGoal_HasAGreekLabel()
    {
        using var cultureScope = new CultureScope("el");

        var levelLabels = Enum.GetValues<CourseLevel>().Select(CourseLabels.Level);
        var goalLabels = Enum.GetValues<FitnessGoal>().Select(CourseLabels.Goal);

        levelLabels.Concat(goalLabels).ShouldAllBe(label => label.Any(IsGreekLetter));
    }

    [Theory]
    [InlineData("en", CourseLevel.Advanced, "Advanced")]
    [InlineData("el", CourseLevel.Beginner, "Αρχάριο")]
    public void Level_ReturnsTheLabelOfTheCurrentLanguage(string culture, CourseLevel level, string expected)
    {
        using var cultureScope = new CultureScope(culture);

        CourseLabels.Level(level).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", FitnessGoal.MuscleGain, "Muscle gain")]
    [InlineData("el", FitnessGoal.FatLoss, "Απώλεια λίπους")]
    public void Goal_ReturnsTheLabelOfTheCurrentLanguage(string culture, FitnessGoal goal, string expected)
    {
        using var cultureScope = new CultureScope(culture);

        CourseLabels.Goal(goal).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", "none,bands", "No equipment|Bands")]
    [InlineData("el", "dumbbells, mat", "Αλτήρες|Στρωματάκι")]
    public void EquipmentList_LabelsEveryStoredCodeInOrder(string culture, string equipment, string expected)
    {
        using var cultureScope = new CultureScope(culture);

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
        using var cultureScope = new CultureScope("en");

        CourseLabels.Equipment("rowing machine").ShouldBe("rowing machine");
    }

    [Fact]
    public void Greek_EveryEquipmentCode_HasAGreekLabel()
    {
        using var cultureScope = new CultureScope("el");

        // "kettlebell" is the one code Greek also writes in Latin letters.
        var labels = Course.EquipmentCodes.Where(code => code != "kettlebell").Select(CourseLabels.Equipment);

        labels.ShouldAllBe(label => label.Any(IsGreekLetter));
    }

    [Theory]
    [InlineData("en", "none", "No equipment")]
    [InlineData("el", "dumbbells", "Αλτήρες")]
    [InlineData("en", "unknown-code", "unknown-code")]
    [InlineData("en", " ", "")]
    public void Equipment_ReturnsTheLabelOrTheCodeItself(string culture, string code, string expected)
    {
        using var cultureScope = new CultureScope(culture);

        CourseLabels.Equipment(code).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", 8, 3, "8 weeks · 3× per week")]
    [InlineData("en", 1, null, "1 week")]
    [InlineData("en", null, 4, "4× per week")]
    [InlineData("el", 8, 3, "8 εβδομάδες · 3× την εβδομάδα")]
    [InlineData("el", 1, 2, "1 εβδομάδα · 2× την εβδομάδα")]
    public void Schedule_JoinsTheWeeksAndSessionsInThePageLanguage(string culture, int? weeks, int? sessions, string expected)
    {
        using var cultureScope = new CultureScope(culture);

        CourseLabels.Schedule(weeks, sessions).ShouldBe(expected);
    }

    [Fact]
    public void Schedule_WithoutWeeksOrSessions_IsNull()
    {
        CourseLabels.Schedule(null, null).ShouldBeNull();
    }

    private static bool IsGreekLetter(char character) =>
        character >= FirstGreekLetter && character <= LastGreekLetter;
}
