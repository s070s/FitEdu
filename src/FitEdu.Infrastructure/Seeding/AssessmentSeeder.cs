using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Application.DTOs;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds assessment definitions from JSON files located in the Shared project's JSON folder.
/// </summary>
public static partial class AssessmentSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Seeds assessment definitions from all JSON files in the specified folder.
    /// Only seeds if no assessment definitions exist in the database.
    /// </summary>
    public static async Task SeedFromJsonAsync(
        ApplicationDbContext db,
        string jsonFolderPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (await db.AssessmentDefinitions.AnyAsync(cancellationToken))
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

        var jsonFiles = Directory.GetFiles(resolvedPath, "*.json");

        if (jsonFiles.Length == 0)
        {
            LogNoFilesFound(logger, jsonFolderPath);
            return;
        }

        var categoryCache = await CategorySeedHelper.LoadCacheAsync(db, cancellationToken);

        foreach (var filePath in jsonFiles)
        {
            try
            {
                await SeedAssessmentFromFileAsync(db, filePath, categoryCache, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                LogSeedFileFailed(logger, ex, filePath);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, jsonFiles.Length);
    }

    private static async Task SeedAssessmentFromFileAsync(
        ApplicationDbContext db,
        string filePath,
        Dictionary<string, Category> categoryCache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var dto = JsonSerializer.Deserialize<AssessmentSeedDto>(json, JsonOptions);

        if (dto is null)
        {
            LogDeserializeFailed(logger, filePath);
            return;
        }

        var def = MapToAssessmentDefinition(dto);
        def.CategoryId = CategorySeedHelper.ResolveCategoryId(db, categoryCache, dto.Category, dto.CategoryEl);
        db.AssessmentDefinitions.Add(def);

        var fileName = Path.GetFileName(filePath);
        LogAssessmentLoaded(logger, def.TitleEn, def.Key, fileName);
    }

    private static AssessmentDefinition MapToAssessmentDefinition(AssessmentSeedDto dto)
    {
        var now = DateTimeOffset.UtcNow;

        return new AssessmentDefinition
        {
            Id = Guid.NewGuid(),
            Key = dto.Key,
            TitleEn = dto.Title,
            TitleEl = dto.TitleEl,
            DescriptionEn = dto.Description,
            DescriptionEl = dto.DescriptionEl,
            SchemaJson = dto.SchemaJson,
            CreatedAt = dto.CreatedAt ?? now,
            UpdatedAt = null,
            IsPublished = dto.IsPublished,
            PublishedAt = dto.IsPublished ? (dto.PublishedAt ?? now) : null
        };
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "Assessment definitions already exist; skipping seed.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "JSON seed folder not found: {Path}")]
    private static partial void LogFolderNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No JSON seed files found in: {Path}")]
    private static partial void LogNoFilesFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to seed assessment from file: {FilePath}")]
    private static partial void LogSeedFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} assessment(s) from JSON files.")]
    private static partial void LogSeeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize assessment from: {FilePath}")]
    private static partial void LogDeserializeFailed(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded assessment '{Title}' (Key: {Key}) from {FileName}")]
    private static partial void LogAssessmentLoaded(ILogger logger, string title, string key, string fileName);
}
