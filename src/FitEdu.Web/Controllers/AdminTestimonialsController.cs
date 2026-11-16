using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Admin-only CRUD endpoints for testimonial management.
/// </summary>
[ApiController]
[Route("api/admin/testimonials")]
[Authorize(Policy = "AdminOnly")]
[Tags("Admin · Testimonials")]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class AdminTestimonialsController : ControllerBase
{
    private readonly ITestimonialService _testimonials;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<AdminTestimonialsController> _logger;

    public AdminTestimonialsController(
        ITestimonialService testimonials,
        IFileStorage fileStorage,
        ILogger<AdminTestimonialsController> logger)
    {
        _testimonials = testimonials;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    /// <summary>Get a paged list of all testimonials (active and inactive).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminTestimonialDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string sortBy = "displayorder",
        [FromQuery] string sortDir = "asc",
        CancellationToken cancellationToken = default)
    {
        var result = await _testimonials.GetAllForAdminAsync(page, pageSize, sortBy, sortDir, cancellationToken);
        return Ok(result);
    }

    /// <summary>Get a single testimonial by id.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminTestimonialDto>> GetById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _testimonials.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>Create a new testimonial.</summary>
    [HttpPost]
    public async Task<ActionResult<AdminTestimonialDto>> Create(
        [FromBody] SaveTestimonialRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _testimonials.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Update an existing testimonial by id.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminTestimonialDto>> Update(
        Guid id,
        [FromBody] SaveTestimonialRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _testimonials.UpdateAsync(id, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Toggle a testimonial's active (published) state.</summary>
    [HttpPost("{id:guid}/toggle-active")]
    public async Task<ActionResult<AdminTestimonialDto>> ToggleActive(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _testimonials.ToggleActiveAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Move a testimonial one position up in display order.</summary>
    [HttpPost("{id:guid}/move-up")]
    public async Task<IActionResult> MoveUp(
        Guid id, CancellationToken cancellationToken = default)
    {
        var success = await _testimonials.MoveUpAsync(id, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    /// <summary>Move a testimonial one position down in display order.</summary>
    [HttpPost("{id:guid}/move-down")]
    public async Task<IActionResult> MoveDown(
        Guid id, CancellationToken cancellationToken = default)
    {
        var success = await _testimonials.MoveDownAsync(id, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    /// <summary>Upload (or replace) the avatar image for a testimonial (jpeg, png, gif, webp; max 5 MB).</summary>
    [HttpPost("{id:guid}/upload/avatar")]
    [RequestSizeLimit(UploadLimits.MaxImageBytes + UploadLimits.FormOverheadBytes)]
    public async Task<IActionResult> UploadAvatar(
        Guid id, IFormFile file, CancellationToken cancellationToken = default)
    {
        var check = await UploadValidator.CheckAsync(
            file, UploadLimits.ImageContentTypes, UploadLimits.MaxImageBytes, ErrorMessagesRes.OnlyImageFilesAllowed, cancellationToken);
        if (!check.IsValid)
            return Problem(detail: check.Error, statusCode: StatusCodes.Status400BadRequest);

        var existing = await _testimonials.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();

        // Save the new avatar, update the testimonial, then delete the old file (P05-6): a failure before the update
        // keeps the old file and the old value.
        using var stream = file.OpenReadStream();
        var path = await _fileStorage.SaveFileAsync(stream, check.ContentType, "testimonials/avatars", cancellationToken: cancellationToken);

        try
        {
            await _testimonials.SetAvatarPathAsync(id, path, cancellationToken);
        }
        catch
        {
            await _fileStorage.DeleteUnusedFileAsync(path, _logger);
            throw;
        }

        await _fileStorage.DeleteUnusedFileAsync(existing.AvatarPath, _logger);
        return Ok(new AvatarUploadResultDto(path));
    }

    /// <summary>Remove the avatar image from a testimonial.</summary>
    [HttpDelete("{id:guid}/avatar")]
    public async Task<IActionResult> RemoveAvatar(
        Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _testimonials.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();

        // The row is updated first, so a failed update keeps the file (P05-6).
        var success = await _testimonials.RemoveAvatarAsync(id, cancellationToken);
        if (success)
            await _fileStorage.DeleteUnusedFileAsync(existing.AvatarPath, _logger);

        return success ? NoContent() : NotFound();
    }

    /// <summary>Delete a testimonial and its associated avatar file.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _testimonials.GetByIdAsync(id, cancellationToken);

        // The row is deleted first, then its avatar file (P05-6).
        var success = await _testimonials.DeleteAsync(id, cancellationToken);
        if (success)
            await _fileStorage.DeleteUnusedFileAsync(existing?.AvatarPath, _logger);

        return success ? NoContent() : NotFound();
    }
}
