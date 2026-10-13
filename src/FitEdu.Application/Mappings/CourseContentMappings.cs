using System.Linq.Expressions;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;

namespace FitEdu.Application.Mappings;

/// <summary>
/// Shared module/lesson admin mappers (MAINT-1). AdminModuleDto was hand-built at four sites
/// and AdminLessonDto at three inside their controllers. Keep each expression/extension pair's
/// field order in sync — they are the same projection for query and materialized use.
/// </summary>
public static class CourseContentMappings
{
    /// <summary>For IQueryable.Select (requires the Lessons navigation for the count).</summary>
    public static readonly Expression<Func<Module, AdminModuleDto>> ModuleAdminProjection =
        m => new AdminModuleDto(m.Id, m.TitleEn, m.TitleEl, m.DescriptionEn, m.DescriptionEl,
            m.SortOrder, m.CourseId, m.Lessons.Count);

    /// <summary>Materialized variant. A freshly created module's empty Lessons collection
    /// yields the same 0 the create endpoint used to pass explicitly.</summary>
    public static AdminModuleDto ToAdminDto(this Module m) =>
        new(m.Id, m.TitleEn, m.TitleEl, m.DescriptionEn, m.DescriptionEl,
            m.SortOrder, m.CourseId, m.Lessons.Count);

    /// <summary>For IQueryable.Select. The exercise rows keep their lesson order.</summary>
    public static readonly Expression<Func<Lesson, AdminLessonDto>> LessonAdminProjection =
        l => new AdminLessonDto(l.Id, l.TitleEn, l.TitleEl, l.ContentEn, l.ContentEl,
            l.PdfPath, l.VideoPath, l.DurationMinutes, l.SortOrder, l.ModuleId, l.IsPublished,
            l.Exercises
                .OrderBy(x => x.SortOrder)
                .Select(x => new LessonExerciseEditDto(x.Exercise.Slug, x.Sets, x.Reps, x.RestSeconds,
                    x.NotesEn, x.NotesEl, x.Exercise.NameEn))
                .ToList());

    /// <summary>Materialized variant; requires Exercises and each row's Exercise to be loaded.</summary>
    public static AdminLessonDto ToAdminDto(this Lesson l) =>
        new(l.Id, l.TitleEn, l.TitleEl, l.ContentEn, l.ContentEl,
            l.PdfPath, l.VideoPath, l.DurationMinutes, l.SortOrder, l.ModuleId, l.IsPublished,
            l.Exercises
                .OrderBy(x => x.SortOrder)
                .Select(x => x.ToEditDto())
                .ToList());

    /// <summary>Same fields, in the same order, as the row built inside <see cref="LessonAdminProjection"/>.</summary>
    public static LessonExerciseEditDto ToEditDto(this LessonExercise x) =>
        new(x.Exercise.Slug, x.Sets, x.Reps, x.RestSeconds,
            x.NotesEn, x.NotesEl, x.Exercise.NameEn);
}
