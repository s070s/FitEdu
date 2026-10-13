using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Application.DTOs;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds the landing-page testimonials and the upcoming live classes from the Shared project's
/// JSON/Showcase folder. Runs after the course seeder: a live class names its course by English title.
/// </summary>
public static partial class ShowcaseSeeder
{
    private const string TestimonialsFileName = "testimonials.json";
    private const string LiveClassesFileName = "live-classes.json";
    private const int LastHourOfDay = 23;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Seeds testimonials when the Testimonials table is empty and live classes when the
    /// ScheduledSessions table is empty; each table is checked on its own.
    /// </summary>
    /// <param name="hostUserId">The user who hosts every live class (the admin account).</param>
    /// <param name="timeProvider">The clock that gives the seed date the class dates count from.</param>
    public static async Task SeedFromJsonAsync(
        ApplicationDbContext db,
        string jsonFolderPath,
        string hostUserId,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = Path.GetFullPath(jsonFolderPath);
        if (!Directory.Exists(resolvedPath))
        {
            LogFolderNotFound(logger, resolvedPath);
            return;
        }

        var now = timeProvider.GetUtcNow();
        await SeedTestimonialsAsync(db, Path.Combine(resolvedPath, TestimonialsFileName), now, logger, cancellationToken);
        await SeedLiveClassesAsync(db, Path.Combine(resolvedPath, LiveClassesFileName), hostUserId, now, logger, cancellationToken);
    }

    private static async Task SeedTestimonialsAsync(
        ApplicationDbContext db,
        string filePath,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await db.Testimonials.AnyAsync(cancellationToken))
        {
            LogTestimonialsAlreadySeeded(logger);
            return;
        }

        var testimonials = await ReadListAsync<TestimonialSeedDto>(filePath, logger, cancellationToken);
        if (testimonials is null)
            return;

        db.Testimonials.AddRange(testimonials.Select(dto => MapToTestimonial(dto, now)));
        await db.SaveChangesAsync(cancellationToken);
        LogTestimonialsSeeded(logger, testimonials.Count);
    }

    private static async Task SeedLiveClassesAsync(
        ApplicationDbContext db,
        string filePath,
        string hostUserId,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await db.ScheduledSessions.AnyAsync(cancellationToken))
        {
            LogLiveClassesAlreadySeeded(logger);
            return;
        }

        var liveClasses = await ReadListAsync<LiveClassSeedDto>(filePath, logger, cancellationToken);
        if (liveClasses is null)
            return;

        var courseIdsByTitle = await CourseTitleLookup.LoadCourseIdsByTitleAsync(db, cancellationToken);
        var seedDate = DateOnly.FromDateTime(now.UtcDateTime);

        db.ScheduledSessions.AddRange(liveClasses.Select(dto => MapToSession(dto, hostUserId, seedDate, now, courseIdsByTitle)));
        await db.SaveChangesAsync(cancellationToken);
        LogLiveClassesSeeded(logger, liveClasses.Count);
    }

    /// <summary>Reads a JSON list; a missing file gives null and a warning.</summary>
    private static async Task<List<T>?> ReadListAsync<T>(string filePath, ILogger logger, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            LogFileNotFound(logger, filePath);
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
    }

    private static Testimonial MapToTestimonial(TestimonialSeedDto dto, DateTimeOffset now)
    {
        return new Testimonial
        {
            Id = Guid.NewGuid(),
            FullName = dto.FullName,
            RoleOrTitle = dto.RoleOrTitle,
            RoleOrTitleEl = dto.RoleOrTitleEl,
            QuoteText = dto.QuoteText,
            QuoteTextEl = dto.QuoteTextEl,
            DisplayOrder = dto.DisplayOrder,
            IsActive = dto.IsActive,
            CreatedAt = now
        };
    }

    private static ScheduledSession MapToSession(
        LiveClassSeedDto dto,
        string hostUserId,
        DateOnly seedDate,
        DateTimeOffset now,
        Dictionary<string, Guid> courseIdsByTitle)
    {
        EnsureValidSchedule(dto);

        var startDate = NthWeekdayAfter(seedDate, dto.WeekdayOffset);
        return new ScheduledSession
        {
            Id = Guid.NewGuid(),
            HostUserId = hostUserId,
            TitleEn = dto.Title,
            TitleEl = dto.TitleEl,
            CourseId = ResolveCourseId(dto, courseIdsByTitle),
            StartsAtUtc = new DateTimeOffset(startDate.ToDateTime(new TimeOnly(dto.HourUtc, 0)), TimeSpan.Zero),
            DurationMinutes = dto.DurationMinutes,
            MaxParticipants = dto.MaxParticipants,
            CreatedAt = now
        };
    }

    /// <summary>Stops the seed on values the admin session editor would never save.</summary>
    private static void EnsureValidSchedule(LiveClassSeedDto dto)
    {
        if (dto.WeekdayOffset < 1)
            throw new SeedContentException($"Live class '{dto.Title}' needs a weekdayOffset of 1 or more.");
        if (dto.HourUtc is < 0 or > LastHourOfDay)
            throw new SeedContentException($"Live class '{dto.Title}' has hourUtc {dto.HourUtc}; use 0 to {LastHourOfDay}.");
        if (dto.DurationMinutes < 1)
            throw new SeedContentException($"Live class '{dto.Title}' needs a positive durationMinutes.");
        if (dto.MaxParticipants is < 1 or > ScheduledSession.MaxParticipantsLimit)
            throw new SeedContentException(
                $"Live class '{dto.Title}' has maxParticipants {dto.MaxParticipants}; use 1 to {ScheduledSession.MaxParticipantsLimit}.");
    }

    /// <summary>Null when the class names no course; an unknown title is a content error that stops the seed.</summary>
    private static Guid? ResolveCourseId(LiveClassSeedDto dto, Dictionary<string, Guid> courseIdsByTitle)
    {
        if (string.IsNullOrWhiteSpace(dto.CourseTitle))
            return null;

        if (!courseIdsByTitle.TryGetValue(dto.CourseTitle, out var courseId))
            throw new SeedContentException(
                $"Live class '{dto.Title}' references unknown course title '{dto.CourseTitle}'.");

        return courseId;
    }

    /// <summary>The N-th Monday-to-Friday day after the seed date; the seed date itself does not count.</summary>
    private static DateOnly NthWeekdayAfter(DateOnly seedDate, int weekdayOffset)
    {
        var day = seedDate;
        var weekdaysCounted = 0;

        while (weekdaysCounted < weekdayOffset)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                weekdaysCounted++;
        }

        return day;
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Warning, Message = "Showcase JSON seed folder not found: {Path}")]
    private static partial void LogFolderNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Showcase JSON seed file not found: {Path}")]
    private static partial void LogFileNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Testimonials already exist; skipping seed.")]
    private static partial void LogTestimonialsAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} testimonial(s) from JSON.")]
    private static partial void LogTestimonialsSeeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled sessions already exist; skipping live class seed.")]
    private static partial void LogLiveClassesAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} live class(es) from JSON.")]
    private static partial void LogLiveClassesSeeded(ILogger logger, int count);
}
