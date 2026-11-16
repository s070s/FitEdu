namespace FitEdu.Domain.Entities;

/// <summary>
/// An entity that is soft-deleted (flagged, not removed). A global query filter in ApplicationDbContext hides the
/// soft-deleted rows of every type that implements it. Entities that are hard-deleted do not implement it (DB-4).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }
}
