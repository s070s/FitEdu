using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;
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
    private readonly ILogger<AdminLessonsController> _logger;

    // Constructor receives dependencies (DbContext, file storage, the admin course service and a logger) via DI.
    public AdminLessonsController(
        IApplicationDbContext db,
        IFileStorage fileStorage,
        IAdminCourseService adminCourseService,
        ILogger<AdminLessonsController> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _adminCourseService = adminCourseService;
        _logger = logger;
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

        // Remove the lesson entity and persist the deletion first; its files go afterwards (P05-6), so a failed
        // save never leaves a lesson whose files are gone.
        _db.Lessons.Remove(lesson);
        await _db.SaveChangesAsync();

        // Delete the associated PDF and video files (an http(s) video URL is skipped: only uploaded files are deleted).
        await _fileStorage.DeleteUnusedFileAsync(lesson.PdfPath, _logger);
        await _fileStorage.DeleteUnusedFileAsync(lesson.VideoPath, _logger);

        // Return 204 No Content to indicate successful deletion.
        return NoContent();
    }

    /// <summary>Upload (or replace) the PDF asset for a lesson. Accepts a single PDF file.</summary>
    [HttpPost("{id:guid}/upload/pdf")]
    [RequestSizeLimit(UploadLimits.MaxPdfBytes + UploadLimits.FormOverheadBytes)]
    public async Task<IActionResult> UploadPdf(Guid id, IFormFile file, CancellationToken cancellationToken = default)
    {
        // Find the lesson or return 404.
        var lesson = await _db.Lessons.FindAsync([id], cancellationToken);
        if (lesson == null)
            return NotFound();

        // Validate the file: present, a PDF by type and by its first bytes, within the size limit.
        var check = await UploadValidator.CheckAsync(
            file, UploadLimits.PdfContentTypes, UploadLimits.MaxPdfBytes, ErrorMessagesRes.OnlyPdfFilesAllowed, cancellationToken);
        if (!check.IsValid)
            return Problem(detail: check.Error, statusCode: StatusCodes.Status400BadRequest);

        // Save the new PDF, update the lesson, then delete the previous file (P05-6): a failure before the update
        // keeps the old file and the old value.
        var oldPath = lesson.PdfPath;
        using var stream = file.OpenReadStream();
        var path = await _fileStorage.SaveFileAsync(stream, check.ContentType, "lessons/pdf", cancellationToken: cancellationToken);

        // Update the lesson entity with the new path and audit metadata, then persist.
        lesson.PdfPath = path;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        lesson.UpdatedByUserId = UserId;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _fileStorage.DeleteUnusedFileAsync(path, _logger);
            throw;
        }

        await _fileStorage.DeleteUnusedFileAsync(oldPath, _logger);

        // Return the stored PDF path to the caller.
        return Ok(new LessonPdfUploadResultDto(path));
    }

    /// <summary>Upload (or replace) the video asset for a lesson (mp4, webm, ogg; up to 500 MB).</summary>
    [HttpPost("{id:guid}/upload/video")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.MaxVideoBytes)]
    public async Task<IActionResult> UploadVideo(Guid id, IFormFile file, CancellationToken cancellationToken = default)
    {
        // Find the lesson or return 404.
        var lesson = await _db.Lessons.FindAsync([id], cancellationToken);
        if (lesson == null)
            return NotFound();

        // Validate the file: present, a video by type and by its first bytes, within the size limit.
        var check = await UploadValidator.CheckAsync(
            file, UploadLimits.VideoContentTypes, UploadLimits.MaxVideoBytes, ErrorMessagesRes.OnlyVideoFilesAllowed, cancellationToken);
        if (!check.IsValid)
            return Problem(detail: check.Error, statusCode: StatusCodes.Status400BadRequest);

        // Save the new video, update the lesson, then delete the previous file (P05-6): a failure before the update
        // keeps the old file and the old value. The old value may be an http(s) URL, which is never deleted.
        var oldPath = lesson.VideoPath;
        using var stream = file.OpenReadStream();
        var path = await _fileStorage.SaveFileAsync(stream, check.ContentType, "lessons/video", cancellationToken: cancellationToken);

        // Update entity with new video path and audit metadata, then persist.
        lesson.VideoPath = path;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        lesson.UpdatedByUserId = UserId;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _fileStorage.DeleteUnusedFileAsync(path, _logger);
            throw;
        }

        await _fileStorage.DeleteUnusedFileAsync(oldPath, _logger);

        // Return the new video path.
        return Ok(new LessonVideoUploadResultDto(path));
    }
}
