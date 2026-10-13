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
        var greekRowApplier = new GreekRowApplier(exercisesBySlug, ReadGreekTextLimits(db), logger);
        await ApplyGreekTranslationsAsync(resolvedPath, greekRowApplier, logger, cancellationToken);

        db.Exercises.AddRange(exercisesBySlug.Values);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, exercisesBySlug.Count);
    }

    /// <summary>
    /// Maps the dataset rows to entities keyed by slug. Incomplete rows, and later rows that
    /// repeat a slug, are skipped: the slug is unique (it is the URL and the image folder name).
    /// </summary>
    private static Dictionary<string, Exercise> MapExercises(List<ExerciseSeedDto?> rows, ILogger logger)
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
            PrimaryMuscles = JoinValues(row.PrimaryMuscles, Exercise.MuscleSeparator),
            SecondaryMuscles = JoinValues(row.SecondaryMuscles, Exercise.MuscleSeparator),
            InstructionsEn = JoinValues(row.Instructions, Exercise.InstructionSeparator),
            ImageCount = row.Images?.Count ?? 0,
            // The library is public reference content, so seeded exercises are visible at once.
            IsPublished = true,
            PublishedAt = publishedAt
        };
    }

    /// <summary>The Greek column limits from the EF model, so the seeder and the schema share one source.</summary>
    private static GreekTextLimits ReadGreekTextLimits(ApplicationDbContext db)
    {
        var exerciseType = db.Model.FindEntityType(typeof(Exercise))!;
        var nameMaxLength = exerciseType.FindProperty(nameof(Exercise.NameEl))!.GetMaxLength();
        var instructionsMaxLength = exerciseType.FindProperty(nameof(Exercise.InstructionsEl))!.GetMaxLength();
        return new GreekTextLimits(nameMaxLength ?? int.MaxValue, instructionsMaxLength ?? int.MaxValue);
    }

    /// <summary>
    /// Applies Greek rows from exercises.el.json first, then from el/*.el.json in ordinal
    /// file-name order. The first row for a slug wins, so a later file cannot overwrite it.
    /// </summary>
    private static async Task ApplyGreekTranslationsAsync(
        string folderPath,
        GreekRowApplier greekRowApplier,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in GetGreekFilePaths(folderPath))
        {
            var greekRows = await ReadJsonArrayAsync<ExerciseGreekSeedDto>(filePath, logger, cancellationToken);
            if (greekRows is null)
                continue;

            var fileName = Path.GetFileName(filePath);
            foreach (var greekRow in greekRows)
                greekRowApplier.Apply(greekRow, fileName);
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

    /// <summary>
    /// Reads a JSON array file. Returns null, and logs why, when the file cannot be read, is not
    /// valid JSON or holds no array (e.g. the literal null).
    /// </summary>
    private static async Task<List<T?>?> ReadJsonArrayAsync<T>(
        string filePath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var rows = await JsonSerializer.DeserializeAsync<List<T?>>(stream, JsonOptions, cancellationToken);
            if (rows is null)
                LogNoArray(logger, filePath);

            return rows;
        }
        // A locked or unreadable file is reported like a malformed one instead of stopping start-up.
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
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

    /// <summary>The longest Greek name and joined instruction text the Exercises columns hold.</summary>
    private sealed record GreekTextLimits(int NameMaxLength, int InstructionsMaxLength);

    /// <summary>
    /// Copies Greek rows onto the exercises. The first usable row for a slug wins; rows for an
    /// unknown slug, repeats and rows whose text does not fit its column are skipped with a warning.
    /// </summary>
    private sealed class GreekRowApplier(
        Dictionary<string, Exercise> exercisesBySlug,
        GreekTextLimits limits,
        ILogger logger)
    {
        private readonly HashSet<string> _translatedSlugs = new(StringComparer.Ordinal);

        public void Apply(ExerciseGreekSeedDto? greekRow, string fileName)
        {
            var slug = greekRow?.Slug;
            if (greekRow is null || string.IsNullOrWhiteSpace(slug) || !exercisesBySlug.TryGetValue(slug, out var exercise))
            {
                LogUnknownGreekSlug(logger, slug, fileName);
                return;
            }

            if (_translatedSlugs.Contains(slug))
            {
                LogDuplicateGreekSlug(logger, slug, fileName);
                return;
            }

            var nameEl = string.IsNullOrWhiteSpace(greekRow.NameEl) ? null : greekRow.NameEl;
            var instructionsEl = JoinValuesOrNull(greekRow.InstructionsEl, Exercise.InstructionSeparator);
            // SQL Server would refuse the whole save for one over-long value, so the row stays English.
            if (nameEl?.Length > limits.NameMaxLength || instructionsEl?.Length > limits.InstructionsMaxLength)
            {
                LogGreekTextTooLong(logger, slug, fileName);
                return;
            }

            _translatedSlugs.Add(slug);
            exercise.NameEl = nameEl;
            exercise.InstructionsEl = instructionsEl;
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "Exercises already exist; skipping seed.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exercise seed file not found: {FilePath}")]
    private static partial void LogFileNotFound(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read exercise seed file: {FilePath}")]
    private static partial void LogUnreadableFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped exercise seed file {FilePath}: it holds no JSON array.")]
    private static partial void LogNoArray(ILogger logger, string filePath);

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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Skipped Greek row for slug '{Slug}' in {FileName}: its name or instructions are longer than the column allows.")]
    private static partial void LogGreekTextTooLong(ILogger logger, string slug, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} exercise(s) from JSON.")]
    private static partial void LogSeeded(ILogger logger, int count);
}
