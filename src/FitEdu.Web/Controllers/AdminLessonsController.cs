using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Data;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Web.Extensions;
using System.Security.Claims;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Admin endpoints for managing lessons.
/// </summary>
[ApiController]
[Route("api/admin/lessons")]
[Authorize(Policy = "AdminOnly")]
[Tags("Admin · Lessons")]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class AdminLessonsController : ControllerBase
{
    // EF Core DB context used to query and persist lessons and related data.
    private readonly IApplicationDbContext _db;
    // File storage abstraction used to save and delete lesson PDFs and videos.
    private readonly IFileStorage _fileStorage;
    // Lesson create/update (sanitising, exercise rows, concurrency) lives in the admin course service.
    private readonly IAdminCourseService _adminCourseService;

    // Constructor receives dependencies (DbContext, file storage and the admin course service) via DI.
    public AdminLessonsController(IApplicationDbContext db, IFileStorage fileStorage, IAdminCourseService adminCourseService)
    {
        _db = db;
        _fileStorage = fileStorage;
        _adminCourseService = adminCourseService;
    }

    // Helper property to get the current authenticated user's ID (used for audit fields).
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Get all lessons for a specific module, ordered by sort order.</summary>
    [HttpGet("module/{moduleId:guid}")]
    public async Task<ActionResult<List<AdminLessonDto>>> GetLessonsByModule(Guid moduleId)
    {
        // Query lessons for the module, order and project to DTOs to minimize payload.
        var lessons = await _db.Lessons
            .AsNoTracking()
            .Where(l => l.ModuleId == moduleId)
            .OrderBy(l => l.SortOrder)
            .Select(CourseContentMappings.LessonAdminProjection)
            .ToListAsync();

        // Return the list of lesson DTOs.
        return Ok(lessons);
    }

    /// <summary>Get a single lesson by id. API-4: added so lesson create/update can point their Location/reload at a real resource instead of the module-scoped list.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminLessonDto>> GetLessonById(Guid id)
    {
        var lesson = await _db.Lessons
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(CourseContentMappings.LessonAdminProjection)
            .FirstOrDefaultAsync();

        if (lesson is null)
            return NotFound();

        return Ok(lesson);
    }

    /// <summary>Create a new lesson under a module, with its exercises when given.</summary>
    [HttpPost]
    [ProducesResponseType<AdminLessonDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminLessonDto>> CreateLesson([FromBody] SaveLessonRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _adminCourseService.CreateLessonAsync(request, UserId, cancellationToken);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        // API-4: point Location at the new by-id GET for this lesson, not the module-scoped
        // list of all its siblings.
        var dto = result.Value!;
        return CreatedAtAction(nameof(GetLessonById), new
        {
            id = dto.Id
        }, dto);
    }

    /// <summary>Update an existing lesson by id. A non-null exercise list replaces the lesson's exercises wholesale.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminLessonDto>> UpdateLesson(Guid id, [FromBody] SaveLessonRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _adminCourseService.UpdateLessonAsync(id, request, UserId, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>Delete a lesson, its completion records, and any associated files.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteLesson(Guid id)
    {
        // Find the lesson or return 404.
        var lesson = await _db.Lessons.FindAsync(id);
        if (lesson == null)
            return NotFound();

        // Remove lesson completion records first (FK constraint).
        var completions = await _db.LessonCompletions
            .Where(lc => lc.LessonId == id)
            .ToListAsync();
        _db.LessonCompletions.RemoveRange(completions);

        // Delete associated PDF file if present.
        if (!string.IsNullOrEmpty(lesson.PdfPath))
        {
            await _fileStorage.DeleteFileAsync(lesson.PdfPath);
        }
        // Delete associated video file if present (skip URLs — only delete uploaded files).
        if (!string.IsNullOrEmpty(lesson.VideoPath) && !lesson.VideoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            await _fileStorage.DeleteFileAsync(lesson.VideoPath);
        }

        // Remove the lesson entity and persist deletion.
        _db.Lessons.Remove(lesson);
        await _db.SaveChangesAsync();

        // Return 204 No Content to indicate successful deletion.
        return NoContent();
    }

    /// <summary>Upload (or replace) the PDF asset for a lesson. Accepts a single PDF file.</summary>
    [HttpPost("{id:guid}/upload/pdf")]
    public async Task<IActionResult> UploadPdf(Guid id, IFormFile file)
    {
        // Find the lesson or return 404.
        var lesson = await _db.Lessons.FindAsync(id);
        if (lesson == null)
            return NotFound();

        // Validate that a file was provided.
        if (file == null || file.Length == 0)
            return Problem(detail: "No file provided", statusCode: StatusCodes.Status400BadRequest);

        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return Problem(detail: "Only PDF files are allowed.", statusCode: StatusCodes.Status400BadRequest);

        // Delete previous PDF if it exists to avoid orphaned files.
        if (!string.IsNullOrEmpty(lesson.PdfPath))
        {
            await _fileStorage.DeleteFileAsync(lesson.PdfPath);
        }

        // Save the new PDF using the file storage abstraction.
        using var stream = file.OpenReadStream();
        var path = await _fileStorage.SaveFileAsync(stream, file.FileName, "lessons/pdf");

        // Update the lesson entity with the new path and audit metadata, then persist.
        lesson.PdfPath = path;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        lesson.UpdatedByUserId = UserId;
        await _db.SaveChangesAsync();

        // Return the stored PDF path to the caller.
        return Ok(new LessonPdfUploadResultDto(path));
    }

    /// <summary>Upload (or replace) the video asset for a lesson (mp4, webm, ogg; up to 500 MB).</summary>
    [HttpPost("{id:guid}/upload/video")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)] // 500 MB
    public async Task<IActionResult> UploadVideo(Guid id, IFormFile file)
    {
        // Find the lesson or return 404.
        var lesson = await _db.Lessons.FindAsync(id);
        if (lesson == null)
            return NotFound();

        // Validate file input.
        if (file == null || file.Length == 0)
            return Problem(detail: "No file provided", statusCode: StatusCodes.Status400BadRequest);

        var allowedVideoTypes = new[] { "video/mp4", "video/webm", "video/ogg" };
        if (!allowedVideoTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return Problem(detail: "Only video files are allowed (mp4, webm, ogg).", statusCode: StatusCodes.Status400BadRequest);

        // Delete previous video if present to prevent orphaned files.
        if (!string.IsNullOrEmpty(lesson.VideoPath))
        {
            await _fileStorage.DeleteFileAsync(lesson.VideoPath);
        }

        // Save the new video and get its storage path.
        using var stream = file.OpenReadStream();
        var path = await _fileStorage.SaveFileAsync(stream, file.FileName, "lessons/video");

        // Update entity with new video path and audit metadata, then persist.
        lesson.VideoPath = path;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        lesson.UpdatedByUserId = UserId;
        await _db.SaveChangesAsync();

        // Return the new video path.
        return Ok(new LessonVideoUploadResultDto(path));
    }
}
