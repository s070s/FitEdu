using Microsoft.EntityFrameworkCore;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;

namespace FitEdu.Application.ApiServices;

/// <inheritdoc cref="IExerciseService"/>
public class ExerciseService(IApplicationDbContext db) : IExerciseService
{
    // A muscle filter wraps the column as ", chest, triceps," and looks for ", chest,". It ends on a
    // bare comma, not ", ", because SQL Server's LIKE ignores trailing blanks of the searched text.
    private const string MuscleListEnd = ",";

    public async Task<PagedResult<ExerciseListItemDto>> GetPublishedExercisesAsync(
        int page, int pageSize, string lang, ExerciseFilterDto? filter = null,
        CancellationToken cancellationToken = default)
    {
        var isEl = Localized.IsEl(lang);

        var query = ApplyFilter(PublishedExercises(), filter ?? new ExerciseFilterDto())
            .OrderBy(e => e.NameEn);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ExerciseMappings.ListItemProjection(isEl))
            .ToListAsync(cancellationToken);

        return new PagedResult<ExerciseListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<ExerciseDetailDto?> GetPublishedBySlugAsync(
        string slug, string lang, CancellationToken cancellationToken = default)
    {
        var exercise = await PublishedExercises()
            .FirstOrDefaultAsync(e => e.Slug == slug, cancellationToken);

        return exercise?.ToDetailDto(Localized.IsEl(lang));
    }

    public async Task<ExerciseFacetsDto> GetFacetsAsync(CancellationToken cancellationToken = default)
    {
        // One query for a few short columns of every exercise (under a thousand rows). The counting
        // runs in memory because the muscles are joined text that SQL cannot group by name.
        var rows = await PublishedExercises()
            .Select(e => new { e.PrimaryMuscles, e.Equipment, e.Level, e.Category })
            .ToListAsync(cancellationToken);

        return new ExerciseFacetsDto(
            CountValues(rows.SelectMany(row => ExerciseMappings.SplitMuscles(row.PrimaryMuscles))),
            CountValues(rows.Select(row => row.Equipment)),
            CountValues(rows.Select(row => row.Level)),
            CountValues(rows.Select(row => row.Category)));
    }

    public async Task<IReadOnlyList<LessonUsingExerciseDto>> GetLessonsUsingExerciseAsync(
        string slug, string lang, CancellationToken cancellationToken = default)
    {
        var isEl = Localized.IsEl(lang);

        // Lessons students can open: every lesson of a published course, as CourseService shows them
        // (seeded lessons keep Lesson.IsPublished false, so that flag is not used here either).
        var rows = await db.LessonExercises
            .AsNoTracking()
            .Where(x => x.Exercise.Slug == slug && x.Exercise.IsPublished && x.Lesson.Module.Course.IsPublished)
            .OrderBy(x => x.Lesson.Module.Course.TitleEn)
            .ThenBy(x => x.Lesson.Module.CourseId)
            .ThenBy(x => x.Lesson.Module.SortOrder)
            .ThenBy(x => x.Lesson.SortOrder)
            .Select(x => new
            {
                x.LessonId,
                LessonTitleEn = x.Lesson.TitleEn,
                LessonTitleEl = x.Lesson.TitleEl,
                x.Lesson.Module.CourseId,
                CourseTitleEn = x.Lesson.Module.Course.TitleEn,
                CourseTitleEl = x.Lesson.Module.Course.TitleEl
            })
            .ToListAsync(cancellationToken);

        // A lesson can prescribe the same exercise twice (warm-up and main set): list it once.
        return rows
            .DistinctBy(row => row.LessonId)
            .Select(row => new LessonUsingExerciseDto(
                row.LessonId,
                Localized.Pick(isEl, row.LessonTitleEn, row.LessonTitleEl),
                row.CourseId,
                Localized.Pick(isEl, row.CourseTitleEn, row.CourseTitleEl)))
            .ToList();
    }

    private IQueryable<Exercise> PublishedExercises() =>
        db.Exercises.AsNoTracking().Where(e => e.IsPublished);

    private static IQueryable<Exercise> ApplyFilter(IQueryable<Exercise> query, ExerciseFilterDto filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Same sargable LIKE as the course search; SQL Server's default collation ignores case.
            var term = $"%{filter.Search.Trim()}%";
            query = query.Where(e =>
                EF.Functions.Like(e.NameEn, term) || (e.NameEl != null && EF.Functions.Like(e.NameEl, term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Muscle))
        {
            // Whole names only: "back" must not match "lower back".
            var muscleToken = Exercise.MuscleSeparator + filter.Muscle.Trim() + MuscleListEnd;
            query = query.Where(e =>
                (Exercise.MuscleSeparator + e.PrimaryMuscles + MuscleListEnd).Contains(muscleToken));
        }

        if (!string.IsNullOrWhiteSpace(filter.Equipment))
        {
            var equipment = filter.Equipment.Trim();
            query = query.Where(e => e.Equipment == equipment);
        }

        if (!string.IsNullOrWhiteSpace(filter.Level))
        {
            var level = filter.Level.Trim();
            query = query.Where(e => e.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var category = filter.Category.Trim();
            query = query.Where(e => e.Category == category);
        }

        return query;
    }

    /// <summary>Counts each non-empty value; facets are sorted by value for stable chip order.</summary>
    private static List<ExerciseFacetDto> CountValues(IEnumerable<string?> values) =>
        values
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.Ordinal)
            .Select(group => new ExerciseFacetDto(group.Key, group.Count()))
            .OrderBy(facet => facet.Value, StringComparer.Ordinal)
            .ToList();
}
