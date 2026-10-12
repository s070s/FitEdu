using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds the exercise library from the public-domain Free Exercise DB copy in the Shared
/// project's JSON/Exercises folder, plus optional Greek translations.
/// </summary>
public static partial class ExerciseSeeder
{
    private const string ExerciseFileName = "exercises.json";
    private const string GreekOverlayFileName = "exercises.el.json";
    private const string GreekFragmentFolderName = "el";
    private const string GreekFragmentFilePattern = "*.el.json";
    // The entity owns the stored format, so the read side splits on the same separators.
    private const string MuscleSeparator = Exercise.MuscleSeparator;
    private const string InstructionSeparator = Exercise.InstructionSeparator;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Seeds every exercise in exercises.json, then applies the Greek rows from
    /// exercises.el.json and el/*.el.json. Only seeds if no exercises exist in the database.
    /// </summary>
    public static async Task SeedFromJsonAsync(
        ApplicationDbContext db,
        string jsonFolderPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (await db.Exercises.AnyAsync(cancellationToken))
        {
            LogAlreadySeeded(logger);
            return;
        }

        var resolvedPath = Path.GetFullPath(jsonFolderPath);
        var exerciseFilePath = Path.Combine(resolvedPath, ExerciseFileName);
        if (!File.Exists(exerciseFilePath))
        {
            LogFileNotFound(logger, exerciseFilePath);
            return;
        }

        var exerciseRows = await ReadJsonArrayAsync<ExerciseSeedDto>(exerciseFilePath, logger, cancellationToken);
        if (exerciseRows is null)
            return;

        var exercisesBySlug = MapExercises(exerciseRows, logger);
        await ApplyGreekTranslationsAsync(resolvedPath, exercisesBySlug, logger, cancellationToken);

        db.Exercises.AddRange(exercisesBySlug.Values);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, exercisesBySlug.Count);
    }

    /// <summary>
    /// Maps the dataset rows to entities keyed by slug. Incomplete rows, and later rows that
    /// repeat a slug, are skipped: the slug is unique (it is the URL and the image folder name).
    /// </summary>
    private static Dictionary<string, Exercise> MapExercises(List<ExerciseSeedDto> rows, ILogger logger)
    {
        var exercisesBySlug = new Dictionary<string, Exercise>(StringComparer.Ordinal);
        var publishedAt = DateTimeOffset.UtcNow;

        foreach (var row in rows)
        {
            var slug = SlugHelper.FromExerciseId(row?.Id);
            if (slug.Length == 0 || !HasRequiredFields(row))
            {
                LogIncompleteRow(logger, row?.Id);
                continue;
            }

            if (!exercisesBySlug.TryAdd(slug, MapToExercise(row, slug, publishedAt)))
                LogDuplicateSlug(logger, slug);
        }

        return exercisesBySlug;
    }

    private static bool HasRequiredFields([NotNullWhen(true)] ExerciseSeedDto? row) =>
        row is not null
        && !string.IsNullOrWhiteSpace(row.Name)
        && !string.IsNullOrWhiteSpace(row.Level)
        && !string.IsNullOrWhiteSpace(row.Category);

    private static Exercise MapToExercise(ExerciseSeedDto row, string slug, DateTimeOffset publishedAt)
    {
        return new Exercise
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameEn = row.Name,
            Category = row.Category,
            Level = row.Level,
            Force = row.Force,
            Mechanic = row.Mechanic,
            Equipment = row.Equipment,
            PrimaryMuscles = JoinValues(row.PrimaryMuscles, MuscleSeparator),
            SecondaryMuscles = JoinValues(row.SecondaryMuscles, MuscleSeparator),
            InstructionsEn = JoinValues(row.Instructions, InstructionSeparator),
            ImageCount = row.Images?.Count ?? 0,
            // The library is public reference content, so seeded exercises are visible at once.
            IsPublished = true,
            PublishedAt = publishedAt
        };
    }

    /// <summary>
    /// Applies Greek rows from exercises.el.json first, then from el/*.el.json in ordinal
    /// file-name order. The first row for a slug wins, so a later file cannot overwrite it.
    /// </summary>
    private static async Task ApplyGreekTranslationsAsync(
        string folderPath,
        Dictionary<string, Exercise> exercisesBySlug,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var translatedSlugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filePath in GetGreekFilePaths(folderPath))
        {
            var greekRows = await ReadJsonArrayAsync<ExerciseGreekSeedDto>(filePath, logger, cancellationToken);
            if (greekRows is null)
                continue;

            var fileName = Path.GetFileName(filePath);
            foreach (var greekRow in greekRows)
                ApplyGreekRow(greekRow, fileName, exercisesBySlug, translatedSlugs, logger);
        }
    }

    private static List<string> GetGreekFilePaths(string folderPath)
    {
        var filePaths = new List<string>();

        var overlayPath = Path.Combine(folderPath, GreekOverlayFileName);
        if (File.Exists(overlayPath))
            filePaths.Add(overlayPath);

        var fragmentFolder = Path.Combine(folderPath, GreekFragmentFolderName);
        if (!Directory.Exists(fragmentFolder))
            return filePaths;

        var fragmentPaths = Directory.GetFiles(fragmentFolder, GreekFragmentFilePattern)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal);
        filePaths.AddRange(fragmentPaths);
        return filePaths;
    }

    private static void ApplyGreekRow(
        ExerciseGreekSeedDto? greekRow,
        string fileName,
        Dictionary<string, Exercise> exercisesBySlug,
        HashSet<string> translatedSlugs,
        ILogger logger)
    {
        var slug = greekRow?.Slug;
        if (greekRow is null || string.IsNullOrWhiteSpace(slug) || !exercisesBySlug.TryGetValue(slug, out var exercise))
        {
            LogUnknownGreekSlug(logger, slug, fileName);
            return;
        }

        if (!translatedSlugs.Add(slug))
        {
            LogDuplicateGreekSlug(logger, slug, fileName);
            return;
        }

        exercise.NameEl = string.IsNullOrWhiteSpace(greekRow.NameEl) ? null : greekRow.NameEl;
        exercise.InstructionsEl = JoinValuesOrNull(greekRow.InstructionsEl, InstructionSeparator);
    }

    /// <summary>Reads a JSON array file; returns null (and logs the error) when it is not valid JSON.</summary>
    private static async Task<List<T>?> ReadJsonArrayAsync<T>(
        string filePath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            LogUnreadableFile(logger, exception, filePath);
            return null;
        }
    }

    /// <summary>Joins a JSON array into one column value; a missing array becomes an empty string.</summary>
    private static string JoinValues(List<string>? values, string separator) =>
        values is null ? string.Empty : string.Join(separator, values);

    /// <summary>Like <see cref="JoinValues"/>, but null when empty so the UI falls back to English.</summary>
    private static string? JoinValuesOrNull(List<string>? values, string separator) =>
        values is null || values.Count == 0 ? null : string.Join(separator, values);

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "Exercises already exist; skipping seed.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exercise seed file not found: {FilePath}")]
    private static partial void LogFileNotFound(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read exercise seed file: {FilePath}")]
    private static partial void LogUnreadableFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Skipped exercise row '{ExerciseId}': it needs an id, name, level and category.")]
    private static partial void LogIncompleteRow(ILogger logger, string? exerciseId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped exercise row with duplicate slug '{Slug}'.")]
    private static partial void LogDuplicateSlug(ILogger logger, string slug);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Skipped Greek row for unknown exercise slug '{Slug}' in {FileName}.")]
    private static partial void LogUnknownGreekSlug(ILogger logger, string? slug, string fileName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Skipped Greek row for slug '{Slug}' in {FileName}: an earlier row already translates it.")]
    private static partial void LogDuplicateGreekSlug(ILogger logger, string slug, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} exercise(s) from JSON.")]
    private static partial void LogSeeded(ILogger logger, int count);
}
