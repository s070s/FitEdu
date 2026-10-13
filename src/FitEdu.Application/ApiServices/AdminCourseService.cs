using Ganss.Xss;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.DTOs;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Extensions;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.ApiServices;

/// <summary>
/// Admin CRUD operations for courses, plus lesson create/update (which carry the lesson's exercises).
/// </summary>
public class AdminCourseService(
    IApplicationDbContext db,
    ILogger<AdminCourseService> logger,
    IHtmlSanitizer sanitizer) : IAdminCourseService
{
    public async Task<AdminCourseDto?> GetCourseByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await db.Courses
            .AsNoTracking()
            .Include(c => c.Modules)
            .ThenInclude(m => m.Lessons)
            .Include(c => c.Enrollments)
            .Include(c => c.Category)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return course is null ? null : MapToDto(course);
    }

    public async Task<PagedResult<AdminCourseDto>> GetCoursesAsync(
        int page, int pageSize, string sortBy, string sortDir, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = db.Courses.AsQueryable();

        // UX-13: same EF.Functions.Like approach as BlogArticleService — SQL Server's
        // default case-insensitive collation makes it a case-insensitive match without
        // defeating an index the way .ToLower() would.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.TitleEn, term) ||
                (c.TitleEl != null && EF.Functions.Like(c.TitleEl, term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .AsNoTracking()
            .Include(c => c.Modules)
            .ThenInclude(m => m.Lessons)
            .Include(c => c.Enrollments)
            .ApplySort(sortBy, sortDir)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new AdminCourseDto(
                c.Id,
                c.TitleEn,
                c.TitleEl,
                c.DescriptionEn,
                c.DescriptionEl,
                c.IsPublished,
                c.CreatedAt,
                c.UpdatedAt,
                c.Modules.Count,
                c.Modules.SelectMany(m => m.Lessons).Count(),
                c.Enrollments.Count,
                c.RequiredTier,
                c.CategoryId,
                c.Category != null ? c.Category.NameEn : null,
                c.Level,
                c.Goal,
                c.Equipment,
                c.EstimatedWeeks,
                c.SessionsPerWeek,
                c.CoverExerciseSlug,
                c.Category != null ? c.Category.NameEl : null
            ))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminCourseDto>(items, totalCount, page, pageSize, sortBy, sortDir);
    }

    public async Task<ServiceResult<AdminCourseDto>> CreateCourseAsync(SaveCourseRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var coverExerciseSlug = SlugHelper.NormalizeTypedSlug(request.CoverExerciseSlug);
        var fitnessFieldError = await FindFitnessFieldErrorAsync(coverExerciseSlug, request.Equipment, cancellationToken);
        if (fitnessFieldError is not null)
            return ServiceResult<AdminCourseDto>.BadRequest(error: fitnessFieldError);

        var categoryId = await AdminCategoryService.ResolveCategoryAsync(db, request.CategoryId, request.NewCategoryName, cancellationToken);

        var course = new Course
        {
            Id = Guid.NewGuid(),
            TitleEn = request.TitleEn,
            TitleEl = request.TitleEl,
            DescriptionEn = request.DescriptionEn is not null ? sanitizer.Sanitize(request.DescriptionEn) : null,
            DescriptionEl = request.DescriptionEl is not null ? sanitizer.Sanitize(request.DescriptionEl) : null,
            RequiredTier = request.RequiredTier,
            CategoryId = categoryId,
            IsPublished = false,
            UpdatedByUserId = userId
        };
        ApplyFitnessFields(course, request, coverExerciseSlug);

        db.Courses.Add(course);
        await db.SaveChangesAsync(cancellationToken);

        var created = await GetCourseByIdAsync(course.Id, cancellationToken) ?? MapToDto(course);
        return ServiceResult<AdminCourseDto>.Created(created);
    }

    public async Task<ServiceResult<AdminCourseDto>> UpdateCourseAsync(Guid id, SaveCourseRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var course = await db.Courses
            .Include(c => c.Modules)
            .ThenInclude(m => m.Lessons)
            .Include(c => c.Enrollments)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (course is null)
            return ServiceResult<AdminCourseDto>.NotFound(error: ErrorMessagesRes.CourseNotFound);

        var coverExerciseSlug = SlugHelper.NormalizeTypedSlug(request.CoverExerciseSlug);
        var fitnessFieldError = await FindFitnessFieldErrorAsync(coverExerciseSlug, request.Equipment, cancellationToken);
        if (fitnessFieldError is not null)
            return ServiceResult<AdminCourseDto>.BadRequest(error: fitnessFieldError);

        var categoryId = await AdminCategoryService.ResolveCategoryAsync(db, request.CategoryId, request.NewCategoryName, cancellationToken);

        course.TitleEn = request.TitleEn;
        course.TitleEl = request.TitleEl;
        course.DescriptionEn = request.DescriptionEn is not null ? sanitizer.Sanitize(request.DescriptionEn) : null;
        course.DescriptionEl = request.DescriptionEl is not null ? sanitizer.Sanitize(request.DescriptionEl) : null;
        course.RequiredTier = request.RequiredTier;
        course.CategoryId = categoryId;
        ApplyFitnessFields(course, request, coverExerciseSlug);
        course.UpdatedAt = DateTimeOffset.UtcNow;
        course.UpdatedByUserId = userId;

        await db.SaveChangesAsync(cancellationToken);

        var updated = await GetCourseByIdAsync(course.Id, cancellationToken) ?? MapToDto(course);
        return ServiceResult<AdminCourseDto>.Ok(updated);
    }

    public async Task<bool> DeleteCourseAsync(Guid id, string userId, CancellationToken cancellationToken = default)
    {
        var course = await db.Courses
            .Include(c => c.Enrollments)
            .Include(c => c.Modules)
            .ThenInclude(m => m.Lessons)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (course is null)
            return false;

        // Soft-delete the course so students retain access to their earned certificates.
        // The global IsDeleted query filter will hide the course and its enrollments
        // from all normal queries while leaving certificate records intact.
        var now = DateTimeOffset.UtcNow;
        course.IsDeleted = true;
        course.DeletedAt = now;
        course.UpdatedByUserId = userId;

        // DB-4: soft-deleting a course previously left its Modules/Lessons with IsDeleted=false —
        // still live rows, still readable/editable via the admin module/lesson endpoints directly
        // by id, even though the parent course was gone from every course-scoped view. Cascade
        // the same stamp to every module and lesson so "deleted" means the same thing everywhere.
        var moduleCount = 0;
        var lessonCount = 0;
        foreach (var module in course.Modules)
        {
            module.IsDeleted = true;
            module.DeletedAt = now;
            module.UpdatedByUserId = userId;
            moduleCount++;

            foreach (var lesson in module.Lessons)
            {
                lesson.IsDeleted = true;
                lesson.DeletedAt = now;
                lesson.UpdatedByUserId = userId;
                lessonCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Admin {UserId} soft-deleted course {CourseId} with {Enrollments} enrollment(s), {Modules} module(s), {Lessons} lesson(s)",
            userId, id, course.Enrollments.Count, moduleCount, lessonCount);

        return true;
    }

    public async Task<bool> PublishCourseAsync(Guid id, string userId, CancellationToken cancellationToken = default)
    {
        var course = await db.Courses.FindAsync([id], cancellationToken);
        if (course is null)
            return false;

        if (!course.IsPublished)
        {
            course.IsPublished = true;
            course.PublishedAt = DateTimeOffset.UtcNow;
            course.UpdatedByUserId = userId;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> UnpublishCourseAsync(Guid id, string userId, CancellationToken cancellationToken = default)
    {
        var course = await db.Courses.FindAsync([id], cancellationToken);
        if (course is null)
            return false;

        if (course.IsPublished)
        {
            course.IsPublished = false;
            course.UpdatedAt = DateTimeOffset.UtcNow;
            course.UpdatedByUserId = userId;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static AdminCourseDto MapToDto(Course course) => new(
        course.Id,
        course.TitleEn,
        course.TitleEl,
        course.DescriptionEn,
        course.DescriptionEl,
        course.IsPublished,
        course.CreatedAt,
        course.UpdatedAt,
        course.Modules.Count,
        course.Modules.SelectMany(m => m.Lessons).Count(),
        course.Enrollments.Count,
        course.RequiredTier,
        course.CategoryId,
        course.Category?.NameEn,
        course.Level,
        course.Goal,
        course.Equipment,
        course.EstimatedWeeks,
        course.SessionsPerWeek,
        course.CoverExerciseSlug,
        course.Category?.NameEl
    );

    // ── Lessons ──

    public async Task<ServiceResult<AdminLessonDto>> CreateLessonAsync(SaveLessonRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var exercisesBySlug = await LoadRequestedExercisesAsync(request.Exercises, cancellationToken);
        var unknownSlug = FindUnknownSlug(request.Exercises, exercisesBySlug);
        if (unknownSlug is not null)
            return ServiceResult<AdminLessonDto>.BadRequest(error: await SlugErrorAsync(unknownSlug, cancellationToken));

        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            TitleEn = request.TitleEn,
            TitleEl = request.TitleEl,
            ContentEn = SanitizeOptional(request.ContentEn),
            ContentEl = SanitizeOptional(request.ContentEl),
            VideoPath = request.VideoUrl,
            DurationMinutes = request.DurationMinutes,
            SortOrder = request.SortOrder,
            ModuleId = request.ModuleId,
            IsPublished = false,
            UpdatedByUserId = userId
        };
        await ReplaceExerciseRowsAsync(lesson, request.Exercises, exercisesBySlug, cancellationToken);

        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(cancellationToken);

        return ServiceResult<AdminLessonDto>.Created(lesson.ToAdminDto());
    }

    public async Task<ServiceResult<AdminLessonDto>> UpdateLessonAsync(Guid id, SaveLessonRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons
            .Include(l => l.Exercises)
            .ThenInclude(x => x.Exercise)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (lesson is null)
            return ServiceResult<AdminLessonDto>.NotFound(error: ErrorMessagesRes.LessonNotFound);

        var exercisesBySlug = await LoadRequestedExercisesAsync(request.Exercises, cancellationToken);
        var unknownSlug = FindUnknownSlug(request.Exercises, exercisesBySlug);
        if (unknownSlug is not null)
            return ServiceResult<AdminLessonDto>.BadRequest(error: await SlugErrorAsync(unknownSlug, cancellationToken));

        lesson.TitleEn = request.TitleEn;
        lesson.TitleEl = request.TitleEl;
        lesson.ContentEn = SanitizeOptional(request.ContentEn);
        lesson.ContentEl = SanitizeOptional(request.ContentEl);
        lesson.VideoPath = request.VideoUrl ?? lesson.VideoPath;
        lesson.DurationMinutes = request.DurationMinutes;
        lesson.SortOrder = request.SortOrder;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        lesson.UpdatedByUserId = userId;
        await ReplaceExerciseRowsAsync(lesson, request.Exercises, exercisesBySlug, cancellationToken);

        // DB-7: RowVersion (AuditableEntity) is part of this UPDATE's WHERE clause, so a lesson changed
        // by another request in between matches zero rows and EF throws instead of overwriting it.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<AdminLessonDto>.Conflict(error: ErrorMessagesRes.Lesson_ModifiedConcurrently);
        }

        return ServiceResult<AdminLessonDto>.Ok(lesson.ToAdminDto());
    }

    private string? SanitizeOptional(string? html) => html is null ? null : sanitizer.Sanitize(html);

    // ── Exercise references ──

    /// <summary>
    /// Loads (tracked) every published library exercise the rows name, keyed by slug. Empty when there
    /// are no rows. Published only, because students never see an unpublished exercise in a lesson.
    /// </summary>
    private async Task<Dictionary<string, Exercise>> LoadRequestedExercisesAsync(
        List<LessonExerciseEditDto>? rows, CancellationToken cancellationToken)
    {
        if (rows is null || rows.Count == 0)
            return [];

        var slugs = rows.Select(row => NormalizeSlug(row.Slug)).Distinct().ToList();
        return await db.Exercises
            .Where(e => slugs.Contains(e.Slug) && e.IsPublished)
            .ToDictionaryAsync(e => e.Slug, cancellationToken);
    }

    /// <summary>The first row slug that is not a published library exercise, or null when every row is known.</summary>
    private static string? FindUnknownSlug(List<LessonExerciseEditDto>? rows, Dictionary<string, Exercise> exercisesBySlug)
    {
        if (rows is null)
            return null;

        return rows
            .Select(row => NormalizeSlug(row.Slug))
            .FirstOrDefault(slug => !exercisesBySlug.ContainsKey(slug));
    }

    /// <summary>
    /// Replaces the lesson's exercise rows with <paramref name="rows"/> in list order.
    /// Null rows leave the lesson's current exercises untouched.
    /// </summary>
    private async Task ReplaceExerciseRowsAsync(
        Lesson lesson, List<LessonExerciseEditDto>? rows, Dictionary<string, Exercise> exercisesBySlug,
        CancellationToken cancellationToken)
    {
        if (rows is null)
            return;

        // lesson.Exercises was loaded through the query filter, which hides the rows of a soft-deleted
        // exercise; load every row so none of those survives beside the new list.
        var storedRows = await db.LessonExercises
            .IgnoreQueryFilters()
            .Where(row => row.LessonId == lesson.Id)
            .ToListAsync(cancellationToken);
        db.LessonExercises.RemoveRange(storedRows);
        lesson.Exercises.Clear();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var exercise = exercisesBySlug[NormalizeSlug(row.Slug)];
            var prescription = new LessonExercise
            {
                Id = Guid.NewGuid(),
                LessonId = lesson.Id,
                ExerciseId = exercise.Id,
                Exercise = exercise,
                SortOrder = index + 1,
                Sets = row.Sets,
                Reps = BlankToNull(row.Reps),
                RestSeconds = row.RestSeconds,
                NotesEn = BlankToNull(row.NotesEn),
                NotesEl = BlankToNull(row.NotesEl)
            };

            // Also added to the DbSet: a row with a preset Id that EF only finds through the collection
            // of a tracked lesson is taken for an existing row (Modified), and the save then fails.
            // Collection first: EF's fix-up then sees the row is already there and does not add it twice.
            lesson.Exercises.Add(prescription);
            db.LessonExercises.Add(prescription);
        }
    }

    /// <summary>The first problem with the cover slug or the equipment codes, or null when both are fine.</summary>
    private async Task<string?> FindFitnessFieldErrorAsync(string? coverExerciseSlug, string? equipment, CancellationToken cancellationToken)
    {
        if (!await CoverExerciseExistsAsync(coverExerciseSlug, cancellationToken))
            return await SlugErrorAsync(coverExerciseSlug!, cancellationToken);

        var unknownCode = EquipmentCodeList.FindUnknownCode(equipment);
        return unknownCode is null ? null : $"{ErrorMessagesRes.Course_UnknownEquipmentCode}: '{unknownCode}'";
    }

    /// <summary>
    /// True when no cover is set, or when the slug names a published library exercise. Published,
    /// because the course editor checks the slug through the public library, which finds only those.
    /// </summary>
    private async Task<bool> CoverExerciseExistsAsync(string? slug, CancellationToken cancellationToken)
    {
        if (slug is null)
            return true;

        return await db.Exercises.AnyAsync(e => e.Slug == slug && e.IsPublished, cancellationToken);
    }

    private static void ApplyFitnessFields(Course course, SaveCourseRequest request, string? coverExerciseSlug)
    {
        course.Level = request.Level;
        course.Goal = request.Goal;
        course.Equipment = EquipmentCodeList.Normalize(request.Equipment);
        course.EstimatedWeeks = request.EstimatedWeeks;
        course.SessionsPerWeek = request.SessionsPerWeek;
        course.CoverExerciseSlug = coverExerciseSlug;
    }

    // A blank row slug stays "" so it is reported as an unknown slug.
    private static string NormalizeSlug(string slug) => SlugHelper.NormalizeTypedSlug(slug) ?? string.Empty;

    private static string? BlankToNull(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Why a slug was refused: the exercise exists but is not published, or there is no such exercise.
    /// An admin who only renamed a lesson whose row names a hidden exercise then sees the real reason.
    /// </summary>
    private async Task<string> SlugErrorAsync(string slug, CancellationToken cancellationToken)
    {
        var isUnpublished = await db.Exercises.AnyAsync(e => e.Slug == slug && !e.IsPublished, cancellationToken);
        var reason = isUnpublished ? ErrorMessagesRes.Exercise_NotPublished : ErrorMessagesRes.Exercise_UnknownSlug;
        return $"{reason}: '{slug}'";
    }
}
