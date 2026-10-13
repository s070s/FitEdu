namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// A seed file is wrong in a way that must be fixed before the app starts, such as a course that
/// names an exercise slug the library does not have. Seeders let it stop the whole seed, while a
/// file they simply cannot read is logged and skipped.
/// </summary>
public sealed class SeedContentException(string message) : InvalidOperationException(message);
