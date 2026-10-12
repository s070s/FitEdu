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
    private const string GreekFragmentFilePattern = "*.el.json";

    public static readonly string CoursesFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Courses");
    public static readonly string AssessmentsFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Assessments");
    public static readonly string ExercisesFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Exercises");
    public static readonly string PathsFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Paths");
    public static readonly string ShowcaseFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Showcase");

    private static readonly string ExerciseLibraryPath = Path.Combine(ExercisesFolder, "exercises.json");
    private static readonly string GreekOverlayPath = Path.Combine(ExercisesFolder, "exercises.el.json");
    private static readonly string GreekFragmentsFolder = Path.Combine(ExercisesFolder, "el");
    private static readonly string TestimonialsPath = Path.Combine(ShowcaseFolder, "testimonials.json");
    private static readonly string LiveClassesPath = Path.Combine(ShowcaseFolder, "live-classes.json");

    // Case-insensitive like the seeders, but an unknown field fails instead of being silently
    // ignored, so a misspelt field name in a seed file cannot slip through.
    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static List<SeedFile<CourseSeedDto>> LoadCourses() => LoadFolder<CourseSeedDto>(CoursesFolder);

    public static List<SeedFile<AssessmentSeedDto>> LoadAssessments() => LoadFolder<AssessmentSeedDto>(AssessmentsFolder);

    /// <summary>Every learning path in the Paths folder, in file-name order.</summary>
    public static List<LearningPathSeedDto> LoadPaths() =>
        LoadFolder<List<LearningPathSeedDto>>(PathsFolder).SelectMany(file => file.Content).ToList();

    public static List<TestimonialSeedDto> LoadTestimonials() => Deserialize<List<TestimonialSeedDto>>(TestimonialsPath);

    public static List<LiveClassSeedDto> LoadLiveClasses() => Deserialize<List<LiveClassSeedDto>>(LiveClassesPath);

    public static List<ExerciseSeedDto> LoadExerciseLibrary() => Deserialize<List<ExerciseSeedDto>>(ExerciseLibraryPath);

    /// <summary>
    /// The Greek row the seeder applies to each slug: exercises.el.json first, then el/*.el.json
    /// in ordinal file-name order, and the first row for a slug wins (as in ExerciseSeeder).
    /// </summary>
    public static List<ExerciseGreekSeedDto> LoadGreekOverlay()
    {
        var fragmentPaths = Directory.GetFiles(GreekFragmentsFolder, GreekFragmentFilePattern).Order(StringComparer.Ordinal);
        string[] greekFilePaths = [GreekOverlayPath, .. fragmentPaths];

        return greekFilePaths
            .SelectMany(Deserialize<List<ExerciseGreekSeedDto>>)
            .DistinctBy(row => row.Slug, StringComparer.Ordinal)
            .ToList();
    }

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
