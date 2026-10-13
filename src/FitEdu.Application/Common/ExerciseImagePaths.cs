namespace FitEdu.Application.Common;

/// <summary>
/// The one rule for exercise photo URLs: wwwroot/images/exercises/{slug}/{index}.webp, where
/// index 0 is the start position and index 1 the end position. Course covers reuse it.
/// </summary>
public static class ExerciseImagePaths
{
    private const int StartImageIndex = 0;
    private const int EndImageIndex = 1;

    public static string Url(string slug, int index) => $"/images/exercises/{slug}/{index}.webp";

    public static string StartUrl(string slug) => Url(slug, StartImageIndex);

    /// <summary>
    /// The end-position photo, or the start photo when the exercise has only one image.
    /// </summary>
    public static string EndUrl(string slug, int imageCount) =>
        imageCount > EndImageIndex ? Url(slug, EndImageIndex) : StartUrl(slug);

    /// <summary>
    /// A course cover is the start photo of its cover exercise; null when the course has none.
    /// </summary>
    public static string? CourseCoverUrl(string? coverExerciseSlug) =>
        string.IsNullOrWhiteSpace(coverExerciseSlug) ? null : StartUrl(coverExerciseSlug);
}
