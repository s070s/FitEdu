using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;

namespace FitEdu.Application.ApiServices;

/// <inheritdoc cref="ILandingService"/>
public class LandingService(
    IApplicationDbContext db, ICourseService courseService, IFileStorage fileStorage, IMemoryCache cache) : ILandingService
{
    public const int FeaturedCourseCount = 6;

    // Every guest's home page asks for the same numbers; a minute-old count is fine for them.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private const string MemberRoleName = "Student";

    // Keyed by the two page languages only, so odd ?lang= values cannot fill the cache.
    public static string CacheKey(string lang) => Localized.IsEl(lang) ? "landing:el" : "landing:en";

    public async Task<LandingOverviewDto> GetOverviewAsync(string lang, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey(lang), out LandingOverviewDto? cached) && cached is not null)
            return cached;

        var overview = await LoadOverviewAsync(lang, cancellationToken);
        cache.Set(CacheKey(lang), overview, CacheDuration);
        return overview;
    }

    private async Task<LandingOverviewDto> LoadOverviewAsync(string lang, CancellationToken cancellationToken)
    {
        var courseCount = await db.Courses.CountAsync(c => c.IsPublished, cancellationToken);
        var lessonCount = await db.Lessons.CountAsync(l => l.Module.Course.IsPublished, cancellationToken);
        var exerciseCount = await db.Exercises.CountAsync(e => e.IsPublished, cancellationToken);
        var memberCount = await CountMembersAsync(cancellationToken);
        var assessmentCount = await db.AssessmentDefinitions.CountAsync(a => a.IsPublished, cancellationToken);
        var featuredCourses = await GetFeaturedCoursesAsync(lang, cancellationToken);
        var hasCustomHeroImage = await HasCustomHeroImageAsync(cancellationToken);

        return new LandingOverviewDto(
            courseCount, lessonCount, exerciseCount, memberCount, assessmentCount, featuredCourses, hasCustomHeroImage);
    }

    private Task<int> CountMembersAsync(CancellationToken cancellationToken)
    {
        var memberRoleIds = db.Roles.Where(r => r.Name == MemberRoleName).Select(r => r.Id);
        return db.UserRoles.CountAsync(ur => memberRoleIds.Contains(ur.RoleId), cancellationToken);
    }

    /// <summary>
    /// The most-enrolled published courses, then the best rated, then by title. The catalog's first page
    /// (up to the paging maximum) is the pool, which covers the whole seeded catalog many times over.
    /// </summary>
    private async Task<IReadOnlyList<CourseListItemDto>> GetFeaturedCoursesAsync(string lang, CancellationToken cancellationToken)
    {
        // Nobody is signed in on the public page, so no course reads as enrolled.
        var catalog = await courseService.GetPublishedCoursesAsync(
            AnonymousUser.Id, page: 1, PagingParams.MaxPageSize, lang, cancellationToken: cancellationToken);

        var enrolmentCountByCourseId = await db.Enrollments
            .AsNoTracking()
            .GroupBy(e => e.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count, cancellationToken);

        return catalog.Items
            .OrderByDescending(c => enrolmentCountByCourseId.GetValueOrDefault(c.Id))
            .ThenByDescending(c => c.AverageRating ?? 0)
            .ThenBy(c => c.Title, StringComparer.CurrentCulture)
            .Take(FeaturedCourseCount)
            .ToList();
    }

    private async Task<bool> HasCustomHeroImageAsync(CancellationToken cancellationToken)
    {
        var heroImagePath = await db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Key == SiteSetting.LandingBackgroundImageKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return !string.IsNullOrEmpty(heroImagePath) && fileStorage.FileExists(heroImagePath);
    }
}
