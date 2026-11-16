namespace FitEdu.Domain.Entities;

/// <summary>
/// An entity with a publish workflow: a draft until an admin publishes it. Only the entities whose code reads
/// or writes these columns implement it, so no other table carries them (DB-4).
/// </summary>
public interface IPublishable
{
    bool IsPublished { get; set; }

    DateTimeOffset? PublishedAt { get; set; }
}
