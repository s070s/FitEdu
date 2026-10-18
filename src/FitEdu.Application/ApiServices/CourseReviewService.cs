using Microsoft.EntityFrameworkCore;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Extensions;

namespace FitEdu.Application.ApiServices;

/// <inheritdoc cref="ICourseReviewService"/>
public partial class CourseReviewService(IApplicationDbContext db, INotificationDispatcher notifications, ILogger<CourseReviewService> logger)
    : ICourseReviewService
{
    public async Task<IReadOnlyList<CourseReviewDto>> GetApprovedForCourseAsync(
        Guid courseId, CancellationToken cancellationToken = default)
    {
        // Author name computed inline (not via a helper method) so EF can translate the whole
        // projection to SQL instead of falling back to client evaluation. Published courses only:
        // the read is open to guests, and a hidden programme must not show its reviews.
        return await db.CourseReviews
            .AsNoTracking()
            .Where(r => r.CourseId == courseId && r.Status == ReviewStatus.Approved && r.Course!.IsPublished)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new CourseReviewDto(
                r.Id,
                !string.IsNullOrWhiteSpace(r.User!.DisplayName) ? r.User.DisplayName! : (r.User.FirstName + " " + r.User.LastName).Trim(),
                r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<CourseRatingSummaryDto?> GetRatingSummaryAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        var summaries = await GetRatingSummariesAsync([courseId], cancellationToken);
        return summaries.GetValueOrDefault(courseId);
    }

    public async Task<Dictionary<Guid, CourseRatingSummaryDto>> GetRatingSummariesAsync(
        IEnumerable<Guid> courseIds, CancellationToken cancellationToken = default)
    {
        var ids = courseIds.ToList();
        if (ids.Count == 0)
            return [];

        var rows = await db.CourseReviews
            .AsNoTracking()
            .Where(r => r.CourseId != null && ids.Contains(r.CourseId.Value) && r.Status == ReviewStatus.Approved
                && r.Course!.IsPublished)
            .GroupBy(r => r.CourseId!.Value)
            .Select(g => new { CourseId = g.Key, Average = g.Average(r => r.Rating), Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.CourseId, r => new CourseRatingSummaryDto(Math.Round(r.Average, 1), r.Count));
    }

    public async Task<MyCourseReviewDto?> GetMyReviewAsync(string userId, Guid courseId, CancellationToken cancellationToken = default)
    {
        return await db.CourseReviews
            .AsNoTracking()
            .Where(r => r.CourseId == courseId && r.UserId == userId)
            .Select(ReviewMappings.MyReviewProjection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ServiceResult<MyCourseReviewDto>> SaveMyReviewAsync(
        string userId, Guid courseId, SaveCourseReviewRequest request, CancellationToken cancellationToken = default)
    {
        var isEnrolled = await db.Enrollments.AsNoTracking()
            .AnyAsync(e => e.UserId == userId && e.CourseId == courseId, cancellationToken);
        if (!isEnrolled)
            return ServiceResult<MyCourseReviewDto>.Forbidden(error: "Only enrolled students can review this course.");

        var review = await db.CourseReviews.FirstOrDefaultAsync(r => r.CourseId == courseId && r.UserId == userId, cancellationToken);

        if (review is null)
        {
            review = new CourseReview
            {
                CourseId = courseId,
                UserId = userId,
                Rating = request.Rating,
                Body = request.Body,
                Status = ReviewStatus.Pending
            };
            db.CourseReviews.Add(review);
        }
        else
        {
            // Editing changes the content, so it goes back through moderation.
            review.Rating = request.Rating;
            review.Body = request.Body;
            review.Status = ReviewStatus.Pending;
            review.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return ServiceResult<MyCourseReviewDto>.Ok(review.ToMyReviewDto());
    }

    public async Task<PagedResult<AdminCourseReviewDto>> GetPagedAsync(
        int page, int pageSize, string sortBy, string sortDir, string? statusFilter, CancellationToken cancellationToken = default)
    {
        var query = db.CourseReviews.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(statusFilter) && Enum.TryParse<ReviewStatus>(statusFilter, ignoreCase: true, out var status))
            query = query.Where(r => r.Status == status);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .ApplySort(sortBy, sortDir)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminCourseReviewDto(
                r.Id, r.CourseId!.Value, r.Course!.TitleEn,
                !string.IsNullOrWhiteSpace(r.User!.DisplayName) ? r.User.DisplayName! : (r.User.FirstName + " " + r.User.LastName).Trim(),
                r.Rating, r.Body, r.Status.ToString(), r.CreatedAt, r.Course.TitleEl))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminCourseReviewDto>(items, totalCount, page, pageSize, sortBy, sortDir);
    }

    public async Task<ServiceResult<string>> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await db.CourseReviews.Include(r => r.Course).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (review is null)
            return ServiceResult<string>.NotFound(error: "Review not found.");

        review.Status = ReviewStatus.Approved;
        review.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var titleEn = review.Course?.TitleEn ?? string.Empty;
        var titleEl = review.Course?.TitleEl ?? titleEn;
        try
        {
            await notifications.DispatchAsync(
                review.UserId, NotificationType.ReviewApproved, "ReviewApproved",
                [titleEn, titleEl], $"courses/{review.CourseId}", cancellationToken); // {0} English, {1} Greek title
        }
        catch (Exception ex)
        {
            // Notification delivery is best-effort — the moderation action itself already succeeded.
            LogReviewApprovedNotificationFailed(logger, ex, id);
        }

        return ServiceResult<string>.Ok("Review approved.");
    }

    public async Task<ServiceResult<string>> RejectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await db.CourseReviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (review is null)
            return ServiceResult<string>.NotFound(error: "Review not found.");

        review.Status = ReviewStatus.Rejected;
        review.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ServiceResult<string>.Ok("Review rejected.");
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to dispatch review-approved notification for review {ReviewId}.")]
    private static partial void LogReviewApprovedNotificationFailed(ILogger logger, Exception exception, Guid reviewId);
}
