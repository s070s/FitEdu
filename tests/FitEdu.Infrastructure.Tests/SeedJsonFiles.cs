using System.Text.Json;
using System.Text.Json.Serialization;
using FitEdu.Application.DTOs;
using FitEdu.TestSupport;

namespace FitEdu.Infrastructure.Tests;

/// <summary>
/// Reads the real seed files under src/FitEdu.Shared/JSON with the seeders' own DTOs.
/// </summary>
internal static class SeedJsonFiles
{
    private const string JsonFilePattern = "*.json";

    public static readonly string CoursesFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Courses");
    public static readonly string AssessmentsFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Assessments");
    public static readonly string ExercisesFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Exercises");

    private static readonly string ExerciseLibraryPath = Path.Combine(ExercisesFolder, "exercises.json");
    private static readonly string GreekOverlayPath = Path.Combine(ExercisesFolder, "exercises.el.json");

    // Case-insensitive like the seeders, but an unknown field fails instead of being silently
    // ignored, so a misspelt field name in a seed file cannot slip through.
    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static List<SeedFile<CourseSeedDto>> LoadCourses() => LoadFolder<CourseSeedDto>(CoursesFolder);

    public static List<SeedFile<AssessmentSeedDto>> LoadAssessments() => LoadFolder<AssessmentSeedDto>(AssessmentsFolder);

    public static List<ExerciseSeedDto> LoadExerciseLibrary() => Deserialize<List<ExerciseSeedDto>>(ExerciseLibraryPath);

    public static List<ExerciseGreekSeedDto> LoadGreekOverlay() => Deserialize<List<ExerciseGreekSeedDto>>(GreekOverlayPath);

    private static List<SeedFile<T>> LoadFolder<T>(string folderPath) =>
        Directory.GetFiles(folderPath, JsonFilePattern)
            .Order(StringComparer.Ordinal)
            .Select(filePath => new SeedFile<T>(Path.GetFileNameWithoutExtension(filePath), Deserialize<T>(filePath)))
            .ToList();

    private static T Deserialize<T>(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, StrictJsonOptions)
                ?? throw new InvalidOperationException($"{filePath} holds no data");
        }
        catch (JsonException exception)
        {
            // Name the file: the serializer's message only gives the position inside it.
            throw new InvalidOperationException($"{filePath}: {exception.Message}", exception);
        }
    }
}

/// <summary>One seed file: its name without the .json extension, and its parsed content.</summary>
internal sealed record SeedFile<T>(string Name, T Content);
