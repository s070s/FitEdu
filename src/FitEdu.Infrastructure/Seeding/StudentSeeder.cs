using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Identity;
using FitEdu.Application.DTOs;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds student users from JSON files for development and testing purposes.
/// </summary>
public static partial class StudentSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Seeds student users from all JSON files in the specified folder.
    /// </summary>
    public static async Task SeedFromJsonAsync(
        UserManager<ApplicationUser> userManager,
        string jsonFolderPath,
        string password,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
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

        // Skip entirely if students are already seeded — avoids N×4 Identity queries on every startup
        if (await userManager.GetUsersInRoleAsync("Student") is { Count: > 0 })
        {
            LogAlreadySeeded(logger);
            return;
        }

        var seededCount = 0;

        foreach (var filePath in jsonFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                var students = JsonSerializer.Deserialize<List<StudentSeedDto>>(json, JsonOptions);

                if (students is null || students.Count == 0)
                {
                    LogNoStudentsInFile(logger, filePath);
                    continue;
                }

                foreach (var dto in students)
                {
                    var email = dto.Email
                                ?? $"{dto.FirstName.ToLowerInvariant()}.{dto.LastName.ToLowerInvariant()}@fitedu.local";

                    if (await userManager.FindByEmailAsync(email) is not null)
                        continue;

                    var student = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        FirstName = dto.FirstName,
                        LastName = dto.LastName,
                        EmailConfirmed = true,
                        IsEnabled = true,
                        GdprConsentGiven = true,
                        GdprConsentDate = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };

                    var result = await userManager.CreateAsync(student, password);
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(student, "Student");
                        seededCount++;
                    }
                    else
                    {
                        LogStudentFailed(logger, email, string.Join(", ", result.Errors.Select(e => e.Description)));
                    }
                }
            }
            catch (Exception ex)
            {
                LogSeedFileFailed(logger, ex, filePath);
            }
        }

        if (seededCount > 0)
        {
            LogSeeded(logger, seededCount);
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Warning, Message = "Student JSON seed folder not found: {Path}")]
    private static partial void LogFolderNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No student JSON seed files found in: {Path}")]
    private static partial void LogNoFilesFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Student users already seeded. Skipping.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No students found in: {FilePath}")]
    private static partial void LogNoStudentsInFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to seed student '{Email}': {Errors}")]
    private static partial void LogStudentFailed(ILogger logger, string email, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to seed students from file: {FilePath}")]
    private static partial void LogSeedFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} student user(s) from JSON files.")]
    private static partial void LogSeeded(ILogger logger, int count);}
