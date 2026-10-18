using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Application.DTOs;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds courses from JSON files located in the Shared project's JSON folder.
/// </summary>
public static partial class CourseSeeder
{
    private const string EmptyLibraryHint = " The exercise library is empty: check SeedData:JsonPaths:Exercises.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Seeds courses from all JSON files in the specified folder.
    /// Only seeds if no courses exist in the database.
    /// </summary>
    public static async Task SeedFromJsonAsync(
        ApplicationDbContext db,
        string jsonFolderPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (await db.Courses.AnyAsync(cancellationToken))
        {
            LogAlreadySeeded(logger);
            return;
        }

        // Resolve relative paths from the current directory
        var resolvedPath = Path.GetFullPath(jsonFolderPath);

        if (!Directory.Exists(resolvedPath))
        {
            LogFolderNotFound(logger, resolvedPath);
            return;
        }

        var jsonFiles = Directory.GetFiles(resolvedPath, "*.json");

        if (jsonFiles.Length == 0)
        {
            LogNoFilesFound(logger, jsonFolderPath);
            return;
        }

        var categoryCache = await CategorySeedHelper.LoadCacheAsync(db, cancellationToken);
        var exerciseIdsBySlug = await LoadExerciseIdsBySlugAsync(db, cancellationToken);

        foreach (var filePath in jsonFiles)
        {
            try
            {
                await SeedCourseFromFileAsync(db, filePath, categoryCache, exerciseIdsBySlug, logger, cancellationToken);
            }
            // A content error (an unknown or missing exercise slug) stops the whole seed instead of
            // silently dropping the course. Other failures, such as malformed JSON, skip the file.
            catch (Exception ex) when (ex is not SeedContentException)
            {
                LogSeedFileFailed(logger, ex, filePath);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, jsonFiles.Length);
    }

    /// <summary>
    /// Loads the slug-to-id map lessons link exercises with. Only two columns are read, so the
    /// long instruction texts of the library are never loaded.
    /// </summary>
    private static Task<Dictionary<string, Guid>> LoadExerciseIdsBySlugAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        return db.Exercises
            .Select(exercise => new { exercise.Slug, exercise.Id })
            .ToDictionaryAsync(exercise => exercise.Slug, exercise => exercise.Id, StringComparer.Ordinal, cancellationToken);
    }

    private static async Task SeedCourseFromFileAsync(
        ApplicationDbContext db,
        string filePath,
        Dictionary<string, Category> categoryCache,
        Dictionary<string, Guid> exerciseIdsBySlug,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var dto = JsonSerializer.Deserialize<CourseSeedDto>(json, JsonOptions);

        if (dto is null)
        {
            LogDeserializeFailed(logger, filePath);
            return;
        }

        var course = MapToCourse(dto, exerciseIdsBySlug);
        course.CategoryId = CategorySeedHelper.ResolveCategoryId(db, categoryCache, dto.Category, dto.CategoryEl);
        db.Courses.Add(course);

        var fileName = Path.GetFileName(filePath);
        LogCourseLoaded(logger, course.TitleEn, fileName);
    }

    private static Course MapToCourse(CourseSeedDto dto, Dictionary<string, Guid> exerciseIdsBySlug)
    {
        return new Course
        {
            Id = Guid.NewGuid(),
            TitleEn = dto.Title,
            TitleEl = dto.TitleEl,
            DescriptionEn = dto.Description,
            DescriptionEl = dto.DescriptionEl,
            RequiredTier = ParseEnumOrDefault(dto.RequiredTier, SubscriptionTier.Free),
            IsPublished = dto.IsPublished,
            Level = ParseEnumOrDefault(dto.Level, CourseLevel.Beginner),
            Goal = ParseEnumOrDefault(dto.Goal, FitnessGoal.GeneralFitness),
            Equipment = dto.Equipment,
            EstimatedWeeks = dto.EstimatedWeeks,
            SessionsPerWeek = dto.SessionsPerWeek,
            CoverExerciseSlug = ResolveCoverExerciseSlug(dto, exerciseIdsBySlug),
            Modules = dto.Modules.Select(module => MapToModule(module, exerciseIdsBySlug)).ToList()
        };
    }

    /// <summary>
    /// Parses an enum name case-insensitively. Unknown text, and numbers that match no member,
    /// give the fallback (Enum.TryParse on its own accepts any number).
    /// </summary>
    private static TEnum ParseEnumOrDefault<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
            return fallback;

        return Enum.IsDefined(parsed) ? parsed : fallback;
    }

    /// <summary>Returns the cover slug (null when none is given); an unknown slug stops the seed.</summary>
    private static string? ResolveCoverExerciseSlug(CourseSeedDto dto, Dictionary<string, Guid> exerciseIdsBySlug)
    {
        if (string.IsNullOrWhiteSpace(dto.CoverExerciseSlug))
            return null;

        if (!exerciseIdsBySlug.ContainsKey(dto.CoverExerciseSlug))
            throw UnknownSlugError($"Course '{dto.Title}' references unknown cover exercise slug '{dto.CoverExerciseSlug}'.", exerciseIdsBySlug);

        return dto.CoverExerciseSlug;
    }

    /// <summary>
    /// The error for a slug the library does not have. When the library is empty the cause is the
    /// Exercises seed folder, not the course file, so the message says where to look.
    /// </summary>
    private static SeedContentException UnknownSlugError(string message, Dictionary<string, Guid> exerciseIdsBySlug)
    {
        if (exerciseIdsBySlug.Count > 0)
            return new SeedContentException(message);

        return new SeedContentException(message + EmptyLibraryHint);
    }

    private static Module MapToModule(ModuleSeedDto dto, Dictionary<string, Guid> exerciseIdsBySlug)
    {
        return new Module
        {
            Id = Guid.NewGuid(),
            TitleEn = dto.Title,
            TitleEl = dto.TitleEl,
            DescriptionEn = dto.Description,
            DescriptionEl = dto.DescriptionEl,
            SortOrder = dto.SortOrder,
            Lessons = dto.Lessons.Select(lesson => MapToLesson(lesson, exerciseIdsBySlug)).ToList()
        };
    }

    private static Lesson MapToLesson(LessonSeedDto dto, Dictionary<string, Guid> exerciseIdsBySlug)
    {
        return new Lesson
        {
            Id = Guid.NewGuid(),
            TitleEn = dto.Title,
            TitleEl = dto.TitleEl,
            ContentEn = dto.Content,
            ContentEl = dto.ContentEl,
            PdfPath = dto.PdfPath,
            VideoPath = dto.VideoPath,
            DurationMinutes = dto.DurationMinutes,
            SortOrder = dto.SortOrder,
            Exercises = MapToLessonExercises(dto, exerciseIdsBySlug)
        };
    }

    /// <summary>Builds the lesson's prescriptions; SortOrder follows the JSON list (1, 2, 3...).</summary>
    private static List<LessonExercise> MapToLessonExercises(LessonSeedDto dto, Dictionary<string, Guid> exerciseIdsBySlug)
    {
        if (dto.Exercises is null)
            return [];

        return dto.Exercises
            .Select((exercise, index) => MapToLessonExercise(dto.Title, exercise, index + 1, exerciseIdsBySlug))
            .ToList();
    }

    private static LessonExercise MapToLessonExercise(
        string lessonTitle,
        LessonExerciseSeedDto? dto,
        int sortOrder,
        Dictionary<string, Guid> exerciseIdsBySlug)
    {
        var slug = dto?.Slug ?? string.Empty;
        if (dto is null || !exerciseIdsBySlug.TryGetValue(slug, out var exerciseId))
            throw UnknownSlugError($"Lesson '{lessonTitle}' references unknown exercise slug '{slug}'.", exerciseIdsBySlug);

        return new LessonExercise
        {
            Id = Guid.NewGuid(),
            ExerciseId = exerciseId,
            SortOrder = sortOrder,
            Sets = dto.Sets,
            Reps = dto.Reps,
            RestSeconds = dto.RestSeconds,
            NotesEn = dto.Notes,
            NotesEl = dto.NotesEl
        };
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "Courses already exist; skipping seed.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "JSON seed folder not found: {Path}")]
    private static partial void LogFolderNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No JSON seed files found in: {Path}")]
    private static partial void LogNoFilesFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to seed course from file: {FilePath}")]
    private static partial void LogSeedFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} course(s) from JSON files.")]
    private static partial void LogSeeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize course from: {FilePath}")]
    private static partial void LogDeserializeFailed(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded course '{Title}' from {FileName}")]
    private static partial void LogCourseLoaded(ILogger logger, string title, string fileName);}
