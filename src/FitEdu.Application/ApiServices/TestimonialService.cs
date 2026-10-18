using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Extensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiServices;

/// <summary>
/// Implements ITestimonialService using ApplicationDbContext.
/// </summary>
public partial class TestimonialService : ITestimonialService
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<TestimonialService> _logger;

    public TestimonialService(IApplicationDbContext db, ILogger<TestimonialService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdminTestimonialDto>> GetActiveAsync(
        string lang = "en", CancellationToken cancellationToken = default)
    {
        var isEl = Localized.IsEl(lang);
        var items = await _db.Testimonials
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .ToListAsync(cancellationToken);

        return items.Select(t => ToLocalizedDto(t, isEl)).ToList();
    }

    public async Task<PagedResult<AdminTestimonialDto>> GetAllForAdminAsync(
        int page, int pageSize, string sortBy, string sortDir, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = PagingParams.Normalize(page, pageSize);

        var query = _db.Testimonials.AsNoTracking();

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .ApplySort(sortBy, sortDir)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminTestimonialDto>(
            Items: items.Select(ToAdminDto).ToList(),
            TotalCount: total,
            Page: page,
            PageSize: pageSize,
            SortBy: sortBy,
            SortDir: sortDir);
    }

    public async Task<AdminTestimonialDto?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _db.Testimonials
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return item is null ? null : ToAdminDto(item);
    }

    public async Task<AdminTestimonialDto> CreateAsync(
        SaveTestimonialRequest request, CancellationToken cancellationToken = default)
    {
        // Assign display order: place at end by default (max + 1)
        var maxOrder = await _db.Testimonials
            .AnyAsync(cancellationToken)
            ? await _db.Testimonials.MaxAsync(t => t.DisplayOrder, cancellationToken)
            : 0;

        var now = DateTimeOffset.UtcNow;

        var testimonial = new Testimonial
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName,
            RoleOrTitle = request.RoleOrTitle,
            RoleOrTitleEl = request.RoleOrTitleEl,
            CompanyOrContext = request.CompanyOrContext,
            QuoteText = request.QuoteText,
            QuoteTextEl = request.QuoteTextEl,
            DisplayOrder = request.DisplayOrder > 0 ? request.DisplayOrder : maxOrder + 1,
            IsActive = request.IsActive,
            CreatedAt = now
        };

        _db.Testimonials.Add(testimonial);
        await _db.SaveChangesAsync(cancellationToken);

        LogTestimonialCreated(_logger, testimonial.Id, testimonial.FullName);
        return ToAdminDto(testimonial);
    }

    public async Task<AdminTestimonialDto?> UpdateAsync(
        Guid id, SaveTestimonialRequest request, CancellationToken cancellationToken = default)
    {
        var testimonial = await _db.Testimonials
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (testimonial is null)
            return null;

        testimonial.FullName = request.FullName;
        testimonial.RoleOrTitle = request.RoleOrTitle;
        testimonial.RoleOrTitleEl = request.RoleOrTitleEl;
        testimonial.CompanyOrContext = request.CompanyOrContext;
        testimonial.QuoteText = request.QuoteText;
        testimonial.QuoteTextEl = request.QuoteTextEl;
        testimonial.DisplayOrder = request.DisplayOrder;
        testimonial.IsActive = request.IsActive;
        testimonial.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        LogTestimonialUpdated(_logger, testimonial.Id, testimonial.FullName);
        return ToAdminDto(testimonial);
    }

    public async Task<AdminTestimonialDto?> ToggleActiveAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var testimonial = await _db.Testimonials
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (testimonial is null)
            return null;

        testimonial.IsActive = !testimonial.IsActive;
        testimonial.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return ToAdminDto(testimonial);
    }

    public async Task<bool> MoveUpAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var current = await _db.Testimonials
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (current is null)
            return false;

        // Find the item with the next-lower DisplayOrder
        var previous = await _db.Testimonials
            .Where(t => t.DisplayOrder < current.DisplayOrder)
            .OrderByDescending(t => t.DisplayOrder)
            .FirstOrDefaultAsync(cancellationToken);

        if (previous is null)
            return false;

        (current.DisplayOrder, previous.DisplayOrder) = (previous.DisplayOrder, current.DisplayOrder);
        current.UpdatedAt = DateTimeOffset.UtcNow;
        previous.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MoveDownAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var current = await _db.Testimonials
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (current is null)
            return false;

        // Find the item with the next-higher DisplayOrder
        var next = await _db.Testimonials
            .Where(t => t.DisplayOrder > current.DisplayOrder)
            .OrderBy(t => t.DisplayOrder)
            .FirstOrDefaultAsync(cancellationToken);

        if (next is null)
            return false;

        (current.DisplayOrder, next.DisplayOrder) = (next.DisplayOrder, current.DisplayOrder);
        current.UpdatedAt = DateTimeOffset.UtcNow;
        next.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AdminTestimonialDto?> SetAvatarPathAsync(
        Guid id, string path, CancellationToken cancellationToken = default)
    {
        var testimonial = await _db.Testimonials
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (testimonial is null)
            return null;

        testimonial.AvatarPath = path;
        testimonial.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return ToAdminDto(testimonial);
    }

    public async Task<bool> RemoveAvatarAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var testimonial = await _db.Testimonials
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (testimonial is null)
            return false;

        testimonial.AvatarPath = null;
        testimonial.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var testimonial = await _db.Testimonials
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (testimonial is null)
            return false;

        _db.Testimonials.Remove(testimonial);
        await _db.SaveChangesAsync(cancellationToken);

        LogTestimonialDeleted(_logger, id);
        return true;
    }

    // --- Helpers ---

    private static AdminTestimonialDto ToAdminDto(Testimonial t) =>
        new(t.Id,
             t.FullName,
             t.RoleOrTitle,
             t.CompanyOrContext,
             t.QuoteText,
             t.AvatarPath,
             t.DisplayOrder,
             t.IsActive,
             t.CreatedAt,
             t.UpdatedAt,
             t.RoleOrTitleEl,
             t.QuoteTextEl);

    /// <summary>The landing-page view: role and quote in the requested language, Greek falling back to English.</summary>
    private static AdminTestimonialDto ToLocalizedDto(Testimonial t, bool isEl) =>
        ToAdminDto(t) with
        {
            RoleOrTitle = Localized.Pick(isEl, t.RoleOrTitle, t.RoleOrTitleEl),
            QuoteText = Localized.Pick(isEl, t.QuoteText, t.QuoteTextEl)
        };

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Information, Message = "Testimonial created: {Id} '{FullName}'.")]
    private static partial void LogTestimonialCreated(ILogger logger, Guid id, string fullName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Testimonial updated: {Id} '{FullName}'.")]
    private static partial void LogTestimonialUpdated(ILogger logger, Guid id, string fullName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Testimonial deleted: {Id}.")]
    private static partial void LogTestimonialDeleted(ILogger logger, Guid id);
}
