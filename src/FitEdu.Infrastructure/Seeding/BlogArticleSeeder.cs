using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Application.DTOs;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds example blog articles from BlogSeedData.
/// Idempotent: skips if any articles already exist.
/// </summary>
public static partial class BlogArticleSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.BlogArticles.AnyAsync())
        {
            LogAlreadySeeded(logger);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var articles = BlogSeedData.SeedArticles.Select(r => new BlogArticle
        {
            Id = Guid.NewGuid(),
            TitleEn = r.TitleEn,
            TitleEl = r.TitleEl,
            Slug = r.Slug,
            SummaryEn = r.SummaryEn,
            SummaryEl = r.SummaryEl,
            ContentEn = r.ContentEn,
            ContentEl = r.ContentEl,
            CoverImageUrl = r.CoverImageUrl,
            AuthorName = r.AuthorName,
            Tags = r.Tags is { Length: > 0 }
                                ? JsonSerializer.Serialize(r.Tags)
                                : null,
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now
        }).ToList();

        await db.BlogArticles.AddRangeAsync(articles);
        await db.SaveChangesAsync();

        LogSeeded(logger, articles.Count);
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "Blog articles already exist. Skipping.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} blog articles.")]
    private static partial void LogSeeded(ILogger logger, int count);
}
