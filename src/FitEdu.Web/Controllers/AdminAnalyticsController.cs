using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Data;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Admin analytics and statistics endpoints.
/// </summary>
[ApiController]
[Route("api/admin/analytics")]
[Authorize(Policy = "AdminOnly")]
[Tags("Admin · Analytics")]
[Produces("application/json", "application/problem+json")]
public class AdminAnalyticsController : ControllerBase
{
    // EF Core DB context used to query application data (courses, enrollments, etc.)
    private readonly IApplicationDbContext _db;
    // Identity user manager used to query and manage application users and their roles
    private readonly UserManager<ApplicationUser> _userManager;

    // Constructor receives dependencies via dependency injection
    public AdminAnalyticsController(IApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    /// <summary>
    /// Get analytics summary for admin dashboard.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<AnalyticsSummaryDto>> GetSummary()
    {
        var totalUsers = await _userManager.Users.CountAsync();

        var activeUsers = await _db.Enrollments
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync();

        var totalEnrollments = await _db.Enrollments.CountAsync();

        var completedCourses = await _db.Enrollments
            .CountAsync(e => e.Status == EnrollmentStatus.Completed);

        // GroupBy pushed into SQL — avoids loading every row into memory.
        var courseStats = await _db.Enrollments
            .GroupBy(e => new { e.CourseId, CourseTitle = e.Course.TitleEn, CourseTitleEl = e.Course.TitleEl })
            .Select(g => new CourseStatDto(
                g.Key.CourseTitle,
                g.Count(),
                g.Count(e => e.Status == EnrollmentStatus.Completed),
                g.Key.CourseTitleEl
            ))
            .ToListAsync();

        var assessmentStats = await _db.AssessmentSubmissions
            .GroupBy(s => new { s.AssessmentDefinitionId, AssessmentTitle = s.AssessmentDefinition.TitleEn, AssessmentTitleEl = s.AssessmentDefinition.TitleEl })
            .Select(g => new AssessmentStatDto(g.Key.AssessmentTitle, g.Count(), g.Key.AssessmentTitleEl))
            .ToListAsync();

        var dto = new AnalyticsSummaryDto(
            totalUsers,
            activeUsers,
            totalEnrollments,
            completedCourses,
            courseStats,
            assessmentStats
        );

        return Ok(dto);
    }
}
