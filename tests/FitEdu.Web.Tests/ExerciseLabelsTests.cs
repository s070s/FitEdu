using System.Globalization;
using System.Text.Json;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The exercise library stores its metadata as lowercase English values ("lower back",
/// "e-z curl bar"); <see cref="ExerciseLabels"/> turns each into an ExerciseRes label. The facts
/// read the real exercises.json so a value the import adds without a label is caught here.
/// </summary>
public class ExerciseLabelsTests
{
    private static readonly string ExerciseLibraryPath =
        Path.Combine(RepoPaths.SeedJsonRoot, "Exercises", "exercises.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void EveryMetadataValueInTheExerciseLibrary_HasAResourceKey()
    {
        var exercises = LoadExerciseLibrary();
        var valuesByPrefix = new Dictionary<string, IEnumerable<string?>>
        {
            [ExerciseLabels.MusclePrefix] = exercises.SelectMany(exercise => exercise.PrimaryMuscles.Concat(exercise.SecondaryMuscles)),
            [ExerciseLabels.EquipmentPrefix] = exercises.Select(exercise => exercise.Equipment),
            [ExerciseLabels.ForcePrefix] = exercises.Select(exercise => exercise.Force),
            [ExerciseLabels.MechanicPrefix] = exercises.Select(exercise => exercise.Mechanic),
            [ExerciseLabels.LevelPrefix] = exercises.Select(exercise => exercise.Level),
            [ExerciseLabels.CategoryPrefix] = exercises.Select(exercise => exercise.Category)
        };

        var missingKeys = valuesByPrefix
            .SelectMany(pair => pair.Value
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => ExerciseLabels.ResourceKey(pair.Key, value!)))
            .Distinct(StringComparer.Ordinal)
            .Where(key => ExerciseRes.ResourceManager.GetString(key, CultureInfo.InvariantCulture) is null)
            .ToList();

        exercises.ShouldNotBeEmpty();
        missingKeys.ShouldBeEmpty("ExerciseRes needs these keys (in .resx, .el.resx and the Designer class)");
    }

    [Theory]
    [InlineData("en", "Lower back")]
    [InlineData("el", "Κάτω πλάτη")]
    public void Muscle_MultiWordValue_ReturnsTheLabelOfTheCurrentLanguage(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        ExerciseLabels.Muscle("lower back").ShouldBe(expected);
    }

    [Theory]
    [InlineData("en", "E-Z curl bar")]
    [InlineData("el", "Μπάρα E-Z")]
    public void Equipment_HyphenatedValue_ReturnsTheLabelOfTheCurrentLanguage(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        ExerciseLabels.Equipment("e-z curl bar").ShouldBe(expected);
    }

    [Fact]
    public void Category_GreekCulture_ReturnsGreekLabel()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");

        ExerciseLabels.Category("olympic weightlifting").ShouldBe("Ολυμπιακή άρση βαρών");
    }

    [Fact]
    public void Label_UnknownValue_FallsBackToTheRawValue()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");

        ExerciseLabels.Equipment("suspension trainer").ShouldBe("suspension trainer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Label_MissingValue_ReturnsEmptyText(string? value)
    {
        ExerciseLabels.Force(value).ShouldBe(string.Empty);
    }

    private static List<ExerciseSeedDto> LoadExerciseLibrary()
    {
        var json = File.ReadAllText(ExerciseLibraryPath);
        return JsonSerializer.Deserialize<List<ExerciseSeedDto>>(json, JsonOptions) ?? [];
    }
}
