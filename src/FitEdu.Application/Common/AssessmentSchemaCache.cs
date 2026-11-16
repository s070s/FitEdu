using Microsoft.Extensions.Caching.Memory;

namespace FitEdu.Application.Common;

/// <summary>
/// The in-process cache of an assessment's schema resolved to one language. The student service fills it;
/// every admin change to a definition (edit, publish, unpublish, delete) evicts both languages, so a
/// student never sees questions the submit check no longer accepts.
/// </summary>
public static class AssessmentSchemaCache
{
    /// <summary>How long a resolved schema stays cached when no admin change evicts it earlier.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public static string Key(Guid assessmentId, bool isEl) =>
        $"schema:{assessmentId}:{(isEl ? "el" : "en")}";

    public static void Evict(IMemoryCache cache, Guid assessmentId)
    {
        cache.Remove(Key(assessmentId, isEl: false));
        cache.Remove(Key(assessmentId, isEl: true));
    }
}
