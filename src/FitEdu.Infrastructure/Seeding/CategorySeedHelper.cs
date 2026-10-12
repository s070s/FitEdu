using Microsoft.EntityFrameworkCore;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Get-or-create category resolution shared by CourseSeeder and AssessmentSeeder. The returned
/// cache must be reused across every file in a single seeding run so repeated category names
/// (e.g. "Strength") don't insert duplicates before the batched SaveChangesAsync commits.
/// </summary>
internal static class CategorySeedHelper
{
    public static async Task<Dictionary<string, Category>> LoadCacheAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.Categories.ToListAsync(cancellationToken);
        return existing.ToDictionary(c => c.NameEn.ToLowerInvariant(), c => c);
    }

    /// <summary>
    /// Returns the id of the category named <paramref name="categoryName"/>, creating it when new.
    /// The Greek name is set on a new category and back-filled on one that has no Greek name yet.
    /// </summary>
    public static Guid? ResolveCategoryId(
        ApplicationDbContext db,
        Dictionary<string, Category> cache,
        string? categoryName,
        string? categoryNameEl)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return null;

        var key = categoryName.Trim().ToLowerInvariant();
        var greekName = string.IsNullOrWhiteSpace(categoryNameEl) ? null : categoryNameEl.Trim();
        if (cache.TryGetValue(key, out var existing))
        {
            BackfillGreekName(existing, greekName);
            return existing.Id;
        }

        var category = new Category { Id = Guid.NewGuid(), NameEn = categoryName.Trim(), NameEl = greekName };
        db.Categories.Add(category);
        cache[key] = category;
        return category.Id;
    }

    /// <summary>Only fills a missing Greek name, so a name an admin already set is never overwritten.</summary>
    private static void BackfillGreekName(Category category, string? greekName)
    {
        if (greekName is null || !string.IsNullOrWhiteSpace(category.NameEl))
            return;

        category.NameEl = greekName;
    }
}
