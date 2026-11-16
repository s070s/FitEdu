using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Admin-only CRUD endpoints for blog article management.
/// </summary>
[ApiController]
[Route("api/admin/blog")]
[Authorize(Policy = "AdminOnly")]
[Tags("Admin · Blog")]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class AdminBlogController : ControllerBase
{
    // The folder a cover upload stores into; only a file in it is deleted when a cover is replaced.
    private const string CoverFolder = "blog/covers";

    private readonly IBlogArticleService _blogService;
    private readonly IFileStorage _fileStorage;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<AdminBlogController> _logger;

    public AdminBlogController(
        IBlogArticleService blogService,
        IFileStorage fileStorage,
        IApplicationDbContext context,
        ILogger<AdminBlogController> logger)
    {
        _blogService = blogService;
        _fileStorage = fileStorage;
        _context = context;
        _logger = logger;
    }

    /// <summary>Get a paged list of all blog articles (published and draft) with optional search.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminBlogArticleDto>>> GetBlogArticles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "createdat",
        [FromQuery] string sortDir = "desc",
        CancellationToken cancellationToken = default)
    {
        var result = await _blogService.GetAllForAdminAsync(page, pageSize, search, sortBy, sortDir, cancellationToken);
        return Ok(result);
    }

    /// <summary>Get a single blog article by id (admin view, includes drafts).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminBlogArticleDto>> GetBlogArticle(
        Guid id, CancellationToken cancellationToken = default)
    {
        var article = await _blogService.GetByIdForAdminAsync(id, cancellationToken);
        return article is null ? NotFound() : Ok(article);
    }

    /// <summary>Create a new blog article. Returns 409 if the slug already exists.</summary>
    [HttpPost]
    [ProducesResponseType<AdminBlogArticleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminBlogArticleDto>> CreateBlogArticle(
        [FromBody] SaveBlogArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _blogService.CreateAsync(request, cancellationToken);
        if (result is null)
            return Problem(detail: ErrorMessagesRes.BlogSlugTaken, statusCode: StatusCodes.Status409Conflict);

        return CreatedAtAction(nameof(GetBlogArticle), new { id = result.Id }, result);
    }

    /// <summary>Update an existing blog article. Returns 409 if the new slug clashes with another article.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminBlogArticleDto>> UpdateBlogArticle(
        Guid id,
        [FromBody] SaveBlogArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        AdminBlogArticleDto? result;
        try
        {
            result = await _blogService.UpdateAsync(id, request, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // DB-7: an article changed by another admin in between matches zero rows (RowVersion).
            return Problem(detail: ErrorMessagesRes.ModifiedConcurrently, statusCode: StatusCodes.Status409Conflict);
        }

        if (result is null)
        {
            var exists = await _blogService.GetByIdForAdminAsync(id, cancellationToken);
            return exists is null
                ? NotFound()
                : Problem(detail: ErrorMessagesRes.BlogSlugTaken, statusCode: StatusCodes.Status409Conflict);
        }
        return Ok(result);
    }

    /// <summary>Publish a blog article (make it publicly visible).</summary>
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> PublishBlogArticle(
        Guid id, CancellationToken cancellationToken = default)
    {
        var success = await _blogService.PublishAsync(id, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    /// <summary>Unpublish a blog article (hide it from the public site).</summary>
    [HttpPost("{id:guid}/unpublish")]
    public async Task<IActionResult> UnpublishBlogArticle(
        Guid id, CancellationToken cancellationToken = default)
    {
        var success = await _blogService.UnpublishAsync(id, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    /// <summary>Delete a blog article.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBlogArticle(
        Guid id, CancellationToken cancellationToken = default)
    {
        var success = await _blogService.DeleteAsync(id, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    /// <summary>Upload (or replace) the cover image for a blog article.</summary>
    [HttpPost("{id:guid}/upload/cover")]
    [RequestSizeLimit(UploadLimits.MaxCoverImageBytes + UploadLimits.FormOverheadBytes)]
    public async Task<IActionResult> UploadBlogCoverImage(
        Guid id, IFormFile file, CancellationToken cancellationToken = default)
    {
        var check = await UploadValidator.CheckAsync(
            file, UploadLimits.ImageContentTypes, UploadLimits.MaxCoverImageBytes, ErrorMessagesRes.OnlyImageFilesAllowed, cancellationToken);
        if (!check.IsValid)
            return Problem(detail: check.Error, statusCode: StatusCodes.Status400BadRequest);

        var article = await _context.BlogArticles.FindAsync(new object[] { id }, cancellationToken);
        if (article is null)
            return NotFound();

        // Save the new cover, update the article, then delete the old one (P05-6): a failure before the update keeps
        // the old file and the old value. The old value may be a site file, a URL or a path in another upload folder;
        // only a file in the cover folder is deleted.
        var oldCover = article.CoverImageUrl;
        using var stream = file.OpenReadStream();
        var path = await _fileStorage.SaveFileAsync(stream, check.ContentType, CoverFolder, cancellationToken: cancellationToken);

        article.CoverImageUrl = path;
        article.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _fileStorage.DeleteUnusedFileAsync(path, _logger);
            throw;
        }

        if (oldCover?.StartsWith(CoverFolder + "/", StringComparison.Ordinal) == true)
            await _fileStorage.DeleteUnusedFileAsync(oldCover, _logger);
        return Ok(new BlogCoverUploadResultDto(path));
    }
}
