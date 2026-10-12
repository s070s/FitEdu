using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Course lookup by English title, shared by the seeders whose JSON names courses by title
/// (learning path steps, live classes).
/// </summary>
internal static class CourseTitleLookup
{
    /// <summary>
    /// Maps each English course title to a course id. Two courses may share a title, so the
    /// first one read wins instead of the dictionary build throwing on the duplicate key.
    /// </summary>
    public static async Task<Dictionary<string, Guid>> LoadCourseIdsByTitleAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var courses = await db.Courses
            .Select(course => new { course.TitleEn, course.Id })
            .ToListAsync(cancellationToken);

        return courses
            .GroupBy(course => course.TitleEn, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);
    }
}
