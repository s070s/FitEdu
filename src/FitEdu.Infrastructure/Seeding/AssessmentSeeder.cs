using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Application.Common;
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

        // Sorted, so which of two files with the same Key is kept does not depend on the file system.
        var jsonFiles = Directory.GetFiles(resolvedPath, "*.json").Order(StringComparer.Ordinal).ToArray();

        if (jsonFiles.Length == 0)
        {
            LogNoFilesFound(logger, jsonFolderPath);
            return;
        }

        var categoryCache = await CategorySeedHelper.LoadCacheAsync(db, cancellationToken);

        var seededKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seededCount = 0;
        foreach (var filePath in jsonFiles)
        {
            try
            {
                if (await SeedAssessmentFromFileAsync(db, filePath, categoryCache, seededKeys, logger, cancellationToken))
                    seededCount++;
            }
            catch (Exception ex)
            {
                LogSeedFileFailed(logger, ex, filePath);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, seededCount, jsonFiles.Length - seededCount);
    }

    /// <summary>Adds the assessment of one file; false (with a warning or error logged) when the file is skipped.</summary>
    private static async Task<bool> SeedAssessmentFromFileAsync(
        ApplicationDbContext db,
        string filePath,
        Dictionary<string, Category> categoryCache,
        HashSet<string> seededKeys,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var dto = JsonSerializer.Deserialize<AssessmentSeedDto>(json, JsonOptions);

        if (dto is null)
        {
            LogDeserializeFailed(logger, filePath);
            return false;
        }

        // The Key column is unique: a repeated Key would fail the single save below and drop every assessment.
        if (!seededKeys.Add(dto.Key))
        {
            LogDuplicateKey(logger, dto.Key, filePath);
            return false;
        }

        var schemaProblems = AssessmentSchemaValidator.Validate(dto.SchemaJson);
        if (schemaProblems.Count > 0)
        {
            LogInvalidSchema(logger, dto.Key, filePath, string.Join("; ", schemaProblems.Select(problem => $"{problem.Kind} {problem.QuestionId}".Trim())));
            return false;
        }

        var def = MapToAssessmentDefinition(dto);
        def.CategoryId = CategorySeedHelper.ResolveCategoryId(db, categoryCache, dto.Category, dto.CategoryEl);
        db.AssessmentDefinitions.Add(def);

        var fileName = Path.GetFileName(filePath);
        LogAssessmentLoaded(logger, def.TitleEn, def.Key, fileName);
        return true;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Seeded} assessment(s) from JSON files; skipped {Skipped}.")]
    private static partial void LogSeeded(ILogger logger, int seeded, int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped assessment '{Key}' from {FilePath}: another file already seeded that key.")]
    private static partial void LogDuplicateKey(ILogger logger, string key, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped assessment '{Key}' from {FilePath}: its schema is not valid ({Problems}).")]
    private static partial void LogInvalidSchema(ILogger logger, string key, string filePath, string problems);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize assessment from: {FilePath}")]
    private static partial void LogDeserializeFailed(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded assessment '{Title}' (Key: {Key}) from {FileName}")]
    private static partial void LogAssessmentLoaded(ILogger logger, string title, string key, string fileName);
}
