namespace FitEdu.Application.DTOs;

/// <summary>
/// Full lesson detail for the lesson viewer. Exercises lists the prescribed exercises in their
/// lesson order (empty when the lesson has none).
/// </summary>
public record LessonDetailDto(
    Guid Id,
    string Title,
    int ContentType,
    string? Content,
    string? PdfPath,
    int? DurationMinutes,
    bool IsCompleted,
    Guid ModuleId,
    string ModuleTitle,
    Guid CourseId,
    string CourseTitle,
    Guid? PreviousLessonId,
    Guid? NextLessonId,
    IReadOnlyList<LessonExerciseDto>? Exercises = null
);
