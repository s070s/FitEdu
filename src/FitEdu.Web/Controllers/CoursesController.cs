using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Web.Extensions;
using FitEdu.Web.Startup;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources.Messages;
using System.Security.Claims;

namespace FitEdu.Web.Controllers;

/// <summary>
/// API endpoints for course discovery, enrollment, and consumption.
/// The catalogue reads (list, detail, reviews) are public, so guests can browse programmes;
/// joining, lessons, completion and writing a review require authentication.
/// </summary>
[ApiController]
[Route("api/courses")]
[Authorize]
[Tags("Courses")]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class CoursesController(ICourseService courseService, ICourseReviewService reviewService) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("User ID not found in claims");

    // Null for a guest on the public catalogue reads.
    private string? OptionalUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool IsSignedIn => OptionalUserId is not null;

    /// <summary>
    /// Get a paged list of all published courses with enrollment status. Optional filters:
    /// category, search text, goal and level (enum names) and one equipment code.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(PublicRateLimit.PolicyName)]
    public async Task<ActionResult<PagedResult<CourseListItemDto>>> GetCourses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string lang = "en",
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? search = null,
        [FromQuery] FitnessGoal? goal = null,
        [FromQuery] CourseLevel? level = null,
        [FromQuery] string? equipment = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = PagingParams.Normalize(page, pageSize);

        var result = await courseService.GetPublishedCoursesAsync(
            OptionalUserId ?? AnonymousUser.Id, page, pageSize, lang, categoryId, search, goal, level, equipment, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get full course detail including modules, lessons, and progress. A guest gets the outline
    /// (lesson titles, kinds, minutes and exercise counts) without progress; lesson content stays
    /// behind the lesson endpoint.
    /// </summary>
    [HttpGet("{courseId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting(PublicRateLimit.PolicyName)]
    public async Task<ActionResult<CourseDetailDto>> GetCourse(Guid courseId, [FromQuery] string lang = "en", CancellationToken cancellationToken = default)
    {
        var dto = await courseService.GetCourseDetailAsync(OptionalUserId ?? AnonymousUser.Id, courseId, lang, cancellationToken);
        return dto is not null
            ? Ok(dto)
            : Problem(detail: ErrorMessagesRes.CourseNotFound, statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Enroll the current user in a course.
    /// Admins cannot enroll - this is for students only.
    /// </summary>
    [HttpPost("{courseId:guid}/enroll")]
    public async Task<ActionResult<EnrollmentResultDto>> Enroll(Guid courseId, CancellationToken cancellationToken = default)
    {
        if (User.IsInRole("Admin"))
            return StatusCode(403, new EnrollmentResultDto(false, ErrorMessagesRes.AdminCannotEnroll, null));

        var result = await courseService.EnrollAsync(UserId, courseId, cancellationToken);
        return result.ToEmbeddedActionResult();
    }

    /// <summary>
    /// Get full lesson detail for the lesson viewer.
    /// </summary>
    [HttpGet("lessons/{lessonId:guid}")]
    public async Task<ActionResult<LessonDetailDto>> GetLesson(Guid lessonId, [FromQuery] string lang = "en", CancellationToken cancellationToken = default)
    {
        var result = await courseService.GetLessonDetailAsync(UserId, lessonId, lang, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Mark a lesson as completed.
    /// </summary>
    [HttpPost("lessons/{lessonId:guid}/complete")]
    public async Task<ActionResult<LessonCompletionResultDto>> CompleteLesson(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var result = await courseService.CompleteLessonAsync(UserId, lessonId, cancellationToken);
        return result.ToEmbeddedActionResult();
    }

    /// <summary>
    /// Get approved reviews, the aggregate rating, and (for the signed-in user) their own
    /// review at any moderation status — everything the course page needs in one call. Guests get
    /// the approved reviews and the rating with no review of their own.
    /// </summary>
    [HttpGet("{courseId:guid}/reviews")]
    [AllowAnonymous]
    [EnableRateLimiting(PublicRateLimit.PolicyName)]
    public async Task<ActionResult<CourseReviewsResponseDto>> GetReviews(Guid courseId, CancellationToken cancellationToken = default)
    {
        var reviews = await reviewService.GetApprovedForCourseAsync(courseId, cancellationToken);
        var summary = await reviewService.GetRatingSummaryAsync(courseId, cancellationToken);
        var canHaveOwnReview = IsSignedIn && !User.IsInRole("Admin");
        var mine = canHaveOwnReview ? await reviewService.GetMyReviewAsync(UserId, courseId, cancellationToken) : null;

        return Ok(new CourseReviewsResponseDto([.. reviews], summary, mine));
    }

    /// <summary>
    /// Create or update the current user's review for this course (one per course). Requires
    /// an active enrollment; editing resets the review to Pending for re-moderation.
    /// </summary>
    [HttpPost("{courseId:guid}/reviews")]
    public async Task<ActionResult<MyCourseReviewDto>> SaveReview(
        Guid courseId, [FromBody] SaveCourseReviewRequest request, CancellationToken cancellationToken = default)
    {
        var result = await reviewService.SaveMyReviewAsync(UserId, courseId, request, cancellationToken);
        return result.ToActionResult(this);
    }
}
