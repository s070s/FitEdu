using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Domain.Entities;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Site-wide settings management.
/// </summary>
[ApiController]
[Route("api/site")]
[Authorize(Policy = "AdminOnly")]
[Tags("Site Settings")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class SiteSettingsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<SiteSettingsController> _logger;

    public SiteSettingsController(IApplicationDbContext db, IFileStorage fileStorage, ILogger<SiteSettingsController> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("User ID not found");

    /// <summary>
    /// Get landing page background image (public endpoint).
    /// </summary>
    [HttpGet("background-image")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    public async Task<IActionResult> GetBackgroundImage()
    {
        var setting = await _db.SiteSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == SiteSetting.LandingBackgroundImageKey);

        if (setting == null || string.IsNullOrEmpty(setting.Value))
        {
            return NotFound();
        }

        if (!_fileStorage.FileExists(setting.Value))
        {
            return NotFound();
        }

        var (stream, contentType) = await _fileStorage.GetFileAsync(setting.Value);
        return File(stream, contentType);
    }

    /// <summary>
    /// Upload landing page background image (admin only).
    /// </summary>
    // API-8: lives under api/admin/* like every other admin-only surface, instead of
    // api/site/admin/* (the one outlier the SEC "everything admin lives under /api/admin"
    // review heuristic used to miss).
    [HttpPost("~/api/admin/site/background-image")]
    public async Task<IActionResult> UploadBackgroundImage(IFormFile file, CancellationToken cancellationToken = default)
    {
        var check = await UploadValidator.CheckAsync(
            file, UploadLimits.ImageContentTypes, UploadLimits.MaxCoverImageBytes, ErrorMessagesRes.OnlyImageFilesAllowed, cancellationToken);
        if (!check.IsValid)
        {
            return Problem(detail: check.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        var setting = await _db.SiteSettings
            .FirstOrDefaultAsync(s => s.Key == SiteSetting.LandingBackgroundImageKey, cancellationToken);

        // Save the new background, update the setting, then delete the old file (P05-6): a failure before the
        // update keeps the old file and the old value.
        var oldPath = setting?.Value;
        using var stream = file.OpenReadStream();
        var path = await _fileStorage.SaveFileAsync(stream, check.ContentType, "backgrounds", cancellationToken: cancellationToken);

        if (setting == null)
        {
            setting = new SiteSetting
            {
                Id = Guid.NewGuid(),
                Key = SiteSetting.LandingBackgroundImageKey,
                Value = path,
                UpdatedByUserId = UserId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.SiteSettings.Add(setting);
        }
        else
        {
            setting.Value = path;
            setting.UpdatedByUserId = UserId;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

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
        return Ok(new BackgroundImageUploadResultDto(path));
    }
}
