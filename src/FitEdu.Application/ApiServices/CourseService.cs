using Microsoft.EntityFrameworkCore;
using FitEdu.Application.DTOs;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.ApiServices;

/// <summary>
/// Handles course discovery, enrollment, and lesson consumption for students.
/// </summary>
public partial class CourseService(
    IApplicationDbContext db,
    ISubscriptionService subscriptionService,
    ICertificateService certificateService,
    ICourseReviewService reviewService,
    ILogger<CourseService> logger) : ICourseService
{
    public async Task<PagedResult<CourseListItemDto>> GetPublishedCoursesAsync(
        string userId, int page, int pageSize, string lang, Guid? categoryId = null, string? search = null,
        FitnessGoal? goal = null, CourseLevel? level = null, string? equipment = null,
        CancellationToken cancellationToken = default)
    {
        var isEl = Localized.IsEl(lang);

        var query = db.Courses
            .AsNoTracking()
            .Where(c => c.IsPublished);

        if (categoryId is { } catId)
            query = query.Where(c => c.CategoryId == catId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // EF.Functions.Like translates to a sargable LIKE predicate.
            // Explicit ToLower() is dropped — SQL Server's default CI_AS collation
            // makes LIKE case-insensitive without defeating the index.
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.TitleEn, term) || (c.TitleEl != null && EF.Functions.Like(c.TitleEl, term)) ||
                (c.DescriptionEn != null && EF.Functions.Like(c.DescriptionEn, term)) ||
                (c.DescriptionEl != null && EF.Functions.Like(c.DescriptionEl, term)));
        }

        query = ApplyFitnessFilters(query, goal, level, equipment)
            .OrderBy(c => c.TitleEn);

        var totalCount = await query.CountAsync(cancellationToken);

        var pageIds = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        // Two batched queries replace per-row correlated subqueries.
        var enrolmentStatusByCourseId = await db.Enrollments
            .AsNoTracking()
            .Where(e => e.UserId == userId && pageIds.Contains(e.CourseId))
            .ToDictionaryAsync(e => e.CourseId, e => e.Status, cancellationToken);

        var lessonCountById = await db.Lessons
            .AsNoTracking()
            .Where(l => pageIds.Contains(l.Module.CourseId))
            .GroupBy(l => l.Module.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count, cancellationToken);

        var ratingsById = await reviewService.GetRatingSummariesAsync(pageIds, cancellationToken);

        var courses = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.TitleEn,
                c.TitleEl,
                c.DescriptionEn,
                c.DescriptionEl,
                c.RequiredTier,
                c.CategoryId,
                CategoryNameEn = c.Category != null ? c.Category.NameEn : null,
                CategoryNameEl = c.Category != null ? c.Category.NameEl : null,
                c.Level,
                c.Goal,
                c.Equipment,
                c.EstimatedWeeks,
                c.SessionsPerWeek,
                c.CoverExerciseSlug
            })
            .ToListAsync(cancellationToken);

        var items = courses.Select(c =>
        {
            var rating = ratingsById.GetValueOrDefault(c.Id);
            var isEnrolled = enrolmentStatusByCourseId.TryGetValue(c.Id, out var enrolmentStatus);
            return new CourseListItemDto(
                c.Id,
                Localized.Pick(isEl, c.TitleEn, c.TitleEl),
                Localized.Pick(isEl, c.DescriptionEn, c.DescriptionEl),
                isEnrolled,
                lessonCountById.GetValueOrDefault(c.Id, 0),
                c.RequiredTier,
                c.CategoryId,
                c.CategoryId is null ? null : Localized.Pick(isEl, c.CategoryNameEn, c.CategoryNameEl),
                rating?.AverageRating,
                rating?.ReviewCount ?? 0,
                c.Level,
                c.Goal,
                c.Equipment,
                c.EstimatedWeeks,
                c.SessionsPerWeek,
                ExerciseImagePaths.CourseCoverUrl(c.CoverExerciseSlug),
                IsCompleted: isEnrolled && enrolmentStatus == EnrollmentStatus.Completed
            );
        }).ToList();

        return new PagedResult<CourseListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<Guid>> GetPublishedCourseIdsAsync(CancellationToken cancellationToken = default)
    {
        return await db.Courses
            .AsNoTracking()
            .Where(c => c.IsPublished)
            .OrderBy(c => c.TitleEn)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CourseDetailDto?> GetCourseDetailAsync(string userId, Guid courseId, string lang, CancellationToken cancellationToken = default)
    {
        var isEl = Localized.IsEl(lang);

        var course = await db.Courses
            .AsNoTracking()
            .Include(c => c.Category)
            .Include(c => c.Modules.OrderBy(m => m.SortOrder))
                .ThenInclude(m => m.Lessons.OrderBy(l => l.SortOrder))
            .Include(c => c.Enrollments.Where(e => e.UserId == userId))
            .FirstOrDefaultAsync(c => c.Id == courseId && c.IsPublished, cancellationToken);

        if (course is null)
            return null;

        var enrollment = course.Enrollments.FirstOrDefault();
        var allLessonIds = course.Modules.SelectMany(m => m.Lessons).Select(l => l.Id).ToList();
        var completedLessonIds = await db.LessonCompletions
            .AsNoTracking()
            .Where(lc => lc.UserId == userId && allLessonIds.Contains(lc.LessonId))
            .Select(lc => lc.LessonId)
            .ToHashSetAsync(cancellationToken);
        var exerciseCountByLessonId = await CountPublishedExercisesAsync(allLessonIds, cancellationToken);

        var totalLessons = allLessonIds.Count;
        var completedLessons = completedLessonIds.Count;
        var progressPercent = totalLessons > 0 ? Math.Round((double)completedLessons / totalLessons * 100, 1) : 0;

        return new CourseDetailDto(
            course.Id,
            Localized.Pick(isEl, course.TitleEn, course.TitleEl),
            Localized.Pick(isEl, course.DescriptionEn, course.DescriptionEl),
            enrollment is not null,
            enrollment?.Status == EnrollmentStatus.Completed,
            completedLessons,
            totalLessons,
            progressPercent,
            course.Modules.Select(m => new ModuleDto(
                m.Id,
                Localized.Pick(isEl, m.TitleEn, m.TitleEl),
                Localized.Pick(isEl, m.DescriptionEn, m.DescriptionEl),
                m.SortOrder,
                m.Lessons.Select(l => new LessonSummaryDto(
                    l.Id,
                    Localized.Pick(isEl, l.TitleEn, l.TitleEl),
                    (int)GetContentType(l),
                    l.DurationMinutes,
                    l.SortOrder,
                    completedLessonIds.Contains(l.Id),
                    exerciseCountByLessonId.GetValueOrDefault(l.Id, 0)
                )).ToList()
            )).ToList(),
            course.RequiredTier,
            course.Level,
            course.Goal,
            course.Equipment,
            course.EstimatedWeeks,
            course.SessionsPerWeek,
            ExerciseImagePaths.CourseCoverUrl(course.CoverExerciseSlug),
            course.Category is null ? null : Localized.Pick(isEl, course.Category.NameEn, course.Category.NameEl)
        );
    }

    /// <summary>
    /// How many published exercises each lesson prescribes, in one grouped query. Unpublished
    /// exercises are left out, as on the lesson page (<see cref="PublishedPrescriptions"/>).
    /// </summary>
    private async Task<Dictionary<Guid, int>> CountPublishedExercisesAsync(List<Guid> lessonIds, CancellationToken cancellationToken) =>
        await db.LessonExercises
            .AsNoTracking()
            .Where(prescription => lessonIds.Contains(prescription.LessonId) && prescription.Exercise.IsPublished)
            .GroupBy(prescription => prescription.LessonId)
            .Select(group => new { LessonId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.LessonId, row => row.Count, cancellationToken);

    public Task<int> CountEnrollmentsAsync(string userId, CancellationToken cancellationToken = default) =>
        db.Enrollments.CountAsync(enrollment => enrollment.UserId == userId, cancellationToken);

    public async Task<ServiceResult<EnrollmentResultDto>> EnrollAsync(string userId, Guid courseId, CancellationToken cancellationToken = default)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == courseId && c.IsPublished, cancellationToken);
        if (course is null)
            return ServiceResult<EnrollmentResultDto>.NotFound(new EnrollmentResultDto(false, ErrorMessagesRes.CourseNotFound, null));

        var userStatus = await subscriptionService.GetUserStatusAsync(userId, cancellationToken);
        if (userStatus.Tier < course.RequiredTier)
        {
            return ServiceResult<EnrollmentResultDto>.Forbidden(new EnrollmentResultDto(
                false,
                ResourceFormat.Fill(ErrorMessagesRes.CourseRequiresTierFormat, TierLabels.Name(course.RequiredTier)),
                null));
        }

        var maxCourses = userStatus.Features?.MaxCourses ?? 1;
        if (maxCourses != int.MaxValue)
        {
            // Note: count-then-insert is a TOCTOU — two concurrent requests for *different* courses
            // can both pass this gate and briefly exceed the limit. Fixing that requires serializable
            // isolation; the rare over-count is accepted here as a benign soft-limit edge case.
            var enrollmentCount = await CountEnrollmentsAsync(userId, cancellationToken);
            if (enrollmentCount >= maxCourses)
            {
                return ServiceResult<EnrollmentResultDto>.Forbidden(new EnrollmentResultDto(
                    false,
                    ResourceFormat.Fill(ErrorMessagesRes.CourseEnrollmentLimitReachedFormat, TierLabels.Name(userStatus.Tier), maxCourses),
                    null));
            }
        }

        var existing = await db.Enrollments
            .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId, cancellationToken);

        if (existing is not null)
            return ServiceResult<EnrollmentResultDto>.Ok(new EnrollmentResultDto(true, SuccessMessagesRes.AlreadyEnrolled, existing.Id));

        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CourseId = courseId,
            EnrolledAt = DateTime.UtcNow,
            Status = EnrollmentStatus.Active
        };

        db.Enrollments.Add(enrollment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request for the same course won the race and hit the unique
            // (UserId, CourseId) index first. Return the already-committed enrollment.
            var winner = await db.Enrollments
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId, cancellationToken);

            if (winner is not null)
                return ServiceResult<EnrollmentResultDto>.Ok(new EnrollmentResultDto(true, SuccessMessagesRes.AlreadyEnrolled, winner.Id));

            throw;
        }

        LogUserEnrolled(logger, userId, courseId);

        return ServiceResult<EnrollmentResultDto>.Ok(new EnrollmentResultDto(true, SuccessMessagesRes.EnrolledSuccessfully, enrollment.Id));
    }

    public async Task<ServiceResult<LessonDetailDto>> GetLessonDetailAsync(string userId, Guid lessonId, string lang, CancellationToken cancellationToken = default)
    {
        var isEl = Localized.IsEl(lang);

        var lesson = await db.Lessons
            .AsNoTracking()
            .Include(l => l.Module)
                .ThenInclude(m => m.Course)
            .Include(l => l.Exercises.OrderBy(x => x.SortOrder))
                .ThenInclude(x => x.Exercise)
            .FirstOrDefaultAsync(l => l.Id == lessonId, cancellationToken);

        if (lesson is null)
            return ServiceResult<LessonDetailDto>.NotFound(error: ErrorMessagesRes.LessonNotFound);

        var course = lesson.Module.Course;
        if (!course.IsPublished)
            return ServiceResult<LessonDetailDto>.NotFound(error: ErrorMessagesRes.CourseNotFound);

        var isEnrolled = await db.Enrollments
            .AnyAsync(e => e.UserId == userId && e.CourseId == course.Id, cancellationToken);

        if (!isEnrolled)
            return ServiceResult<LessonDetailDto>.BadRequest(error: ErrorMessagesRes.MustBeEnrolledToViewLessons);

        var isCompleted = await db.LessonCompletions
            .AnyAsync(lc => lc.UserId == userId && lc.LessonId == lessonId, cancellationToken);

        var allLessons = await db.Lessons
            .AsNoTracking()
            .Where(l => l.Module.CourseId == course.Id)
            .OrderBy(l => l.Module.SortOrder)
            .ThenBy(l => l.SortOrder)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        var currentIndex = allLessons.IndexOf(lessonId);
        var previousLessonId = currentIndex > 0 ? allLessons[currentIndex - 1] : (Guid?)null;
        var nextLessonId = currentIndex < allLessons.Count - 1 ? allLessons[currentIndex + 1] : (Guid?)null;

        var contentType = GetContentType(lesson);
        var displayContent = contentType == ContentType.Video
            ? lesson.VideoPath
            : Localized.Pick(isEl, lesson.ContentEn, lesson.ContentEl);

        var dto = new LessonDetailDto(
            lesson.Id,
            Localized.Pick(isEl, lesson.TitleEn, lesson.TitleEl),
            (int)contentType,
            displayContent,
            lesson.PdfPath,
            lesson.DurationMinutes,
            isCompleted,
            lesson.ModuleId,
            Localized.Pick(isEl, lesson.Module.TitleEn, lesson.Module.TitleEl),
            course.Id,
            Localized.Pick(isEl, course.TitleEn, course.TitleEl),
            previousLessonId,
            nextLessonId,
            PublishedPrescriptions(lesson, isEl)
        );

        return ServiceResult<LessonDetailDto>.Ok(dto);
    }

    /// <summary>The lesson's prescriptions in order; an unpublished exercise has no public page, so it is left out.</summary>
    private static List<LessonExerciseDto> PublishedPrescriptions(Lesson lesson, bool isEl) =>
        lesson.Exercises
            .Where(prescription => prescription.Exercise.IsPublished)
            .Select(prescription => prescription.ToLessonExerciseDto(isEl))
            .ToList();

    public async Task<ServiceResult<LessonCompletionResultDto>> CompleteLessonAsync(string userId, Guid lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons
            .Include(l => l.Module)
            .FirstOrDefaultAsync(l => l.Id == lessonId, cancellationToken);

        if (lesson is null)
            return ServiceResult<LessonCompletionResultDto>.NotFound(
                new LessonCompletionResultDto(false, ErrorMessagesRes.LessonNotFound, 0, 0, 0, false));

        var courseId = lesson.Module.CourseId;

        var enrollment = await db.Enrollments
            .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId, cancellationToken);

        if (enrollment is null)
            return ServiceResult<LessonCompletionResultDto>.BadRequest(
                new LessonCompletionResultDto(false, ErrorMessagesRes.NotEnrolledInCourse, 0, 0, 0, false));

        var existingCompletion = await db.LessonCompletions
            .FirstOrDefaultAsync(lc => lc.UserId == userId && lc.LessonId == lessonId, cancellationToken);

        if (existingCompletion is null)
        {
            var completion = new LessonCompletion
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LessonId = lessonId,
                CompletedAt = DateTime.UtcNow
            };
            db.LessonCompletions.Add(completion);
        }

        var allLessonIds = await db.Lessons
            .Where(l => l.Module.CourseId == courseId)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        var completedCount = await db.LessonCompletions
            .CountAsync(lc => lc.UserId == userId && allLessonIds.Contains(lc.LessonId), cancellationToken);

        if (existingCompletion is null)
            completedCount++;

        var totalLessons = allLessonIds.Count;
        var progressPercent = totalLessons > 0 ? Math.Round((double)completedCount / totalLessons * 100, 1) : 0;
        var courseCompleted = completedCount >= totalLessons;

        if (courseCompleted && enrollment.Status != EnrollmentStatus.Completed)
        {
            enrollment.Status = EnrollmentStatus.Completed;
            enrollment.CompletedAt = DateTime.UtcNow;
            LogCourseCompleted(logger, userId, courseId);
        }

        await db.SaveChangesAsync(cancellationToken);

        var certificatePending = false;
        var certificateIncluded = false;
        if (courseCompleted)
        {
            var subStatus = await subscriptionService.GetUserStatusAsync(userId, cancellationToken);
            certificateIncluded = subStatus.Features?.CertificateAccess == true;
            if (certificateIncluded)
            {
                try
                {
                    // Issue the certificate row only — the PDF renders lazily on first download
                    // (PERF-4), so the student's "complete lesson" click doesn't pay QuestPDF.
                    await certificateService.GetOrCreateAsync(userId, courseId, cancellationToken);
                }
                catch (Exception ex)
                {
                    LogCertificateIssuanceFailed(logger, ex, userId, courseId);
                    certificatePending = true;
                }
            }
        }

        return ServiceResult<LessonCompletionResultDto>.Ok(new LessonCompletionResultDto(
            true,
            existingCompletion is null ? SuccessMessagesRes.LessonCompletedMsg : SuccessMessagesRes.AlreadyCompleted,
            completedCount,
            totalLessons,
            progressPercent,
            courseCompleted,
            certificatePending,
            certificateIncluded
        ));
    }

    /// <summary>
    /// Optional catalog filters. Equipment matches one whole code: the column is wrapped as
    /// ",mat,kettlebell," and searched for ",kettlebell," so "kettlebells" never matches.
    /// </summary>
    private static IQueryable<Course> ApplyFitnessFilters(
        IQueryable<Course> query, FitnessGoal? goal, CourseLevel? level, string? equipment)
    {
        if (goal is { } wantedGoal)
            query = query.Where(c => c.Goal == wantedGoal);

        if (level is { } wantedLevel)
            query = query.Where(c => c.Level == wantedLevel);

        if (!string.IsNullOrWhiteSpace(equipment))
        {
            var equipmentToken = Course.EquipmentSeparator + equipment.Trim() + Course.EquipmentSeparator;
            query = query.Where(c => c.Equipment != null &&
                (Course.EquipmentSeparator + c.Equipment + Course.EquipmentSeparator).Contains(equipmentToken));
        }

        return query;
    }

    private static ContentType GetContentType(Lesson lesson)
    {
        if (!string.IsNullOrEmpty(lesson.VideoPath))
            return ContentType.Video;
        if (!string.IsNullOrEmpty(lesson.PdfPath))
            return ContentType.Pdf;
        return ContentType.Text;
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} enrolled in course {CourseId}")]
    private static partial void LogUserEnrolled(ILogger logger, string userId, Guid courseId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} completed course {CourseId}")]
    private static partial void LogCourseCompleted(ILogger logger, string userId, Guid courseId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Certificate issuance failed for user {UserId} on course {CourseId}.")]
    private static partial void LogCertificateIssuanceFailed(ILogger logger, Exception exception, string userId, Guid courseId);
}
