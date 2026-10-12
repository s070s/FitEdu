using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Application.DTOs;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds learning paths from the JSON files in the Shared project's JSON/Paths folder.
/// Runs after the course seeder: each step names its course by English title.
/// </summary>
public static partial class LearningPathSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Seeds the paths of every JSON file in the folder (each file holds a list of paths).
    /// Only seeds if no learning paths exist in the database.
    /// </summary>
    public static async Task SeedFromJsonAsync(
        ApplicationDbContext db,
        string jsonFolderPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (await db.LearningPaths.AnyAsync(cancellationToken))
        {
            LogAlreadySeeded(logger);
            return;
        }

        var resolvedPath = Path.GetFullPath(jsonFolderPath);
        if (!Directory.Exists(resolvedPath))
        {
            LogFolderNotFound(logger, resolvedPath);
            return;
        }

        var jsonFiles = Directory.GetFiles(resolvedPath, "*.json").Order(StringComparer.Ordinal).ToList();
        if (jsonFiles.Count == 0)
        {
            LogNoFiles(logger, resolvedPath);
            return;
        }

        var courseIdsByTitle = await CourseTitleLookup.LoadCourseIdsByTitleAsync(db, cancellationToken);
        var categoryCache = await CategorySeedHelper.LoadCacheAsync(db, cancellationToken);
        var seededCount = 0;

        foreach (var filePath in jsonFiles)
        {
            var paths = await ReadPathsAsync(filePath, cancellationToken);
            foreach (var dto in paths)
                AddPath(db, dto, courseIdsByTitle, categoryCache);

            seededCount += paths.Count;
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, seededCount);
    }

    private static void AddPath(
        ApplicationDbContext db,
        LearningPathSeedDto dto,
        Dictionary<string, Guid> courseIdsByTitle,
        Dictionary<string, Category> categoryCache)
    {
        var path = MapToPath(dto, courseIdsByTitle);
        path.CategoryId = CategorySeedHelper.ResolveCategoryId(db, categoryCache, dto.Category, dto.CategoryEl);
        db.LearningPaths.Add(path);
    }

    private static async Task<List<LearningPathSeedDto>> ReadPathsAsync(string filePath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<List<LearningPathSeedDto>>(json, JsonOptions) ?? [];
    }

    private static LearningPath MapToPath(LearningPathSeedDto dto, Dictionary<string, Guid> courseIdsByTitle)
    {
        return new LearningPath
        {
            Id = Guid.NewGuid(),
            TitleEn = dto.Title,
            TitleEl = dto.TitleEl,
            DescriptionEn = dto.Description,
            DescriptionEl = dto.DescriptionEl,
            DisplayOrder = dto.DisplayOrder,
            IsPublished = dto.IsPublished,
            PublishedAt = dto.IsPublished ? DateTimeOffset.UtcNow : null,
            Steps = dto.CourseTitles
                .Select((courseTitle, index) => MapToStep(dto.Title, courseTitle, index + 1, courseIdsByTitle))
                .ToList()
        };
    }

    /// <summary>Builds one step; an unknown course title is a content error that stops the whole seed.</summary>
    private static LearningPathStep MapToStep(
        string pathTitle,
        string courseTitle,
        int stepOrder,
        Dictionary<string, Guid> courseIdsByTitle)
    {
        if (!courseIdsByTitle.TryGetValue(courseTitle, out var courseId))
            throw new InvalidOperationException(
                $"Learning path '{pathTitle}' references unknown course title '{courseTitle}'.");

        return new LearningPathStep
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            StepOrder = stepOrder
        };
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "Learning paths already exist; skipping seed.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Learning path JSON seed folder not found: {Path}")]
    private static partial void LogFolderNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No learning path JSON seed files found in: {Path}")]
    private static partial void LogNoFiles(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} learning path(s) from JSON files.")]
    private static partial void LogSeeded(ILogger logger, int count);
}
