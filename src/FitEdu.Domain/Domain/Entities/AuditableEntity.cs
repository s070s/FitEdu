namespace FitEdu.Domain.Entities;

/// <summary>
/// Base class for entities that require audit tracking. The publish and soft-delete columns live in
/// <see cref="IPublishable"/> and <see cref="ISoftDeletable"/>, which only the entities that use them implement.
/// </summary>
public abstract class AuditableEntity
{
    /// <summary>
    /// Left at its default, the save stamps it when the row is added; a value set before then (a seed row's
    /// own date) is kept. An edit never changes it.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? UpdatedByUserId { get; set; }

    /// <summary>
    /// DB-7: DB-generated concurrency token (SQL Server <c>rowversion</c>). EF Core includes it
    /// in every UPDATE's WHERE clause automatically for an entity loaded and saved within the
    /// same DbContext scope — no DTO or client round-trip needed. A second concurrent
    /// read-modify-write against the same row (from a different request/DbContext) then throws
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> instead of
    /// silently overwriting the first writer's change; the admin update paths map
    /// that to 409 Conflict.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}
