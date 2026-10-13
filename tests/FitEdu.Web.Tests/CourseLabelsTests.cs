using System.Globalization;
using FitEdu.Domain.Enums;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>Course levels and goals show CourseRes labels, so Greek pages never show an enum name.</summary>
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

    private static bool IsGreekLetter(char character) =>
        character >= FirstGreekLetter && character <= LastGreekLetter;
}
