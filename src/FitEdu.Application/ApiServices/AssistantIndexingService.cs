using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;

namespace FitEdu.Application.ApiServices;

using SourceKey = (AssistantSourceType SourceType, Guid SourceId, string Language);

/// <inheritdoc cref="IAssistantIndexingService"/>
public class AssistantIndexingService(
    IApplicationDbContext db,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<AssistantIndexingService> logger) : IAssistantIndexingService
{
    private static readonly string[] Languages = ["en", "el"];
    private const string ExerciseListSeparator = "; ";
    private const string HeaderPartSeparator = " — ";
    private const string HeaderTextSeparator = "|";

    // Chunk labels in each chunk's own language, so a Greek chunk (and the Greek search snippet
    // built from it) reads Greek throughout.
    private const string CourseLabelEn = "Course: ";
    private const string CourseLabelEl = "Πρόγραμμα: ";
    private const string ModuleLabelEn = "Module: ";
    private const string ModuleLabelEl = "Ενότητα: ";
    private const string LessonLabelEn = "Lesson: ";
    private const string LessonLabelEl = "Μάθημα: ";
    private const string AssessmentLabelEn = "Assessment: ";
    private const string AssessmentLabelEl = "Αξιολόγηση: ";
    private const string BlogLabelEn = "Blog: ";
    private const string BlogLabelEl = "Άρθρο: ";
    private const string ExercisesLabelEn = "Exercises: ";
    private const string ExercisesLabelEl = "Ασκήσεις: ";

    public async Task<AssistantIndexSummary> RunIndexPassAsync(CancellationToken cancellationToken = default)
    {
        var existingByKey = (await db.AssistantContentChunks.ToListAsync(cancellationToken))
            .GroupBy(c => (c.SourceType, c.SourceId, c.Language))
            .ToDictionary(g => g.Key, g => g.ToList());

        var touched = new HashSet<SourceKey>();
        var added = 0;
        var updated = 0;
        var unchanged = 0;

        var courses = await db.Courses.AsNoTracking().Where(c => c.IsPublished).ToListAsync(cancellationToken);
        foreach (var course in courses)
            await ProcessSourceAsync(AssistantSourceType.Course, course.Id,
                lang => BuildCourseSource(course, lang), existingByKey, touched,
                r => { if (r) added++; else updated++; }, () => unchanged++, cancellationToken);

        // Every lesson of a published course, the lessons students can open (CourseService rule):
        // seeded lessons keep Lesson.IsPublished false, so that flag would hide all of them.
        var lessons = await db.Lessons
            .AsNoTracking()
            .Include(l => l.Module).ThenInclude(m => m.Course)
            .Include(l => l.Exercises).ThenInclude(x => x.Exercise)
            .Where(l => l.Module.Course.IsPublished)
            .ToListAsync(cancellationToken);
        foreach (var lesson in lessons)
            await ProcessSourceAsync(AssistantSourceType.Lesson, lesson.Id,
                lang => BuildLessonSource(lesson, lang), existingByKey, touched,
                r => { if (r) added++; else updated++; }, () => unchanged++, cancellationToken);

        var assessments = await db.AssessmentDefinitions.AsNoTracking().Where(a => a.IsPublished).ToListAsync(cancellationToken);
        foreach (var assessment in assessments)
            await ProcessSourceAsync(AssistantSourceType.Assessment, assessment.Id,
                lang => BuildAssessmentSource(assessment, lang), existingByKey, touched,
                r => { if (r) added++; else updated++; }, () => unchanged++, cancellationToken);

        var articles = await db.BlogArticles.AsNoTracking().Where(a => a.IsPublished).ToListAsync(cancellationToken);
        foreach (var article in articles)
            await ProcessSourceAsync(AssistantSourceType.BlogArticle, article.Id,
                lang => BuildBlogSource(article, lang), existingByKey, touched,
                r => { if (r) added++; else updated++; }, () => unchanged++, cancellationToken);

        var removed = 0;
        foreach (var (key, rows) in existingByKey)
        {
            if (touched.Contains(key))
                continue;

            db.AssistantContentChunks.RemoveRange(rows);
            removed++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var summary = new AssistantIndexSummary(added, updated, removed, unchanged);
        logger.LogInformation(
            "Assistant index pass complete: {Added} added, {Updated} updated, {Removed} removed, {Unchanged} unchanged.",
            summary.Added, summary.Updated, summary.Removed, summary.Unchanged);
        return summary;
    }

    /// <summary>
    /// Diffs one source×language against its stored content hash and, if changed, re-chunks,
    /// re-embeds, and replaces its rows. <paramref name="onWrite"/> is invoked with
    /// <c>isNew: true/false</c> when a write happens; <paramref name="onUnchanged"/> otherwise.
    /// </summary>
    private async Task ProcessSourceAsync(
        AssistantSourceType sourceType,
        Guid sourceId,
        Func<string, (string Header, string Text)?> buildSource,
        Dictionary<SourceKey, List<AssistantContentChunk>> existingByKey,
        HashSet<SourceKey> touched,
        Action<bool> onWrite,
        Action onUnchanged,
        CancellationToken cancellationToken)
    {
        foreach (var lang in Languages)
        {
            var source = buildSource(lang);
            if (source is null)
                continue;

            var key = (sourceType, sourceId, lang);
            touched.Add(key);

            // The header is part of every chunk, so a changed header (e.g. a renamed module) re-indexes too.
            var hash = Sha256Hex(source.Value.Header + HeaderTextSeparator + source.Value.Text);
            var isNew = !existingByKey.TryGetValue(key, out var existingRows);
            if (!isNew && existingRows![0].ContentHash == hash)
            {
                onUnchanged();
                continue;
            }

            if (!isNew)
                db.AssistantContentChunks.RemoveRange(existingRows!);

            var stripped = AssistantChunker.StripHtml(source.Value.Text);
            var chunkTexts = AssistantChunker.Chunk(stripped, source.Value.Header);
            if (chunkTexts.Count == 0)
                continue;

            var embeddings = await embeddingGenerator.GenerateAsync(chunkTexts, cancellationToken: cancellationToken);
            var now = DateTime.UtcNow;
            for (var i = 0; i < chunkTexts.Count; i++)
            {
                db.AssistantContentChunks.Add(new AssistantContentChunk
                {
                    SourceType = sourceType,
                    SourceId = sourceId,
                    Language = lang,
                    ChunkIndex = i,
                    Text = chunkTexts[i],
                    Embedding = EmbeddingCodec.ToBytes(embeddings[i].Vector),
                    ContentHash = hash,
                    UpdatedAt = now
                });
            }

            onWrite(isNew);
        }
    }

    private static (string Header, string Text)? BuildCourseSource(Course course, string lang)
    {
        if (lang == "en")
            return (CourseLabelEn + course.TitleEn, Join(course.TitleEn, course.DescriptionEn));

        if (string.IsNullOrWhiteSpace(course.TitleEl))
            return null;

        return (CourseLabelEl + course.TitleEl, Join(course.TitleEl, course.DescriptionEl));
    }

    private static (string Header, string Text)? BuildLessonSource(Lesson lesson, string lang)
    {
        if (lang == "en")
        {
            var header = LessonHeader(isEl: false, lesson.Module.Course.TitleEn, lesson.Module.TitleEn, lesson.TitleEn);
            return (header, Join(lesson.TitleEn, lesson.ContentEn, DescribeExercises(lesson, isEl: false)));
        }

        if (string.IsNullOrWhiteSpace(lesson.TitleEl))
            return null;

        var courseTitleEl = lesson.Module.Course.TitleEl ?? lesson.Module.Course.TitleEn;
        var moduleTitleEl = lesson.Module.TitleEl ?? lesson.Module.TitleEn;
        var headerEl = LessonHeader(isEl: true, courseTitleEl, moduleTitleEl, lesson.TitleEl);
        return (headerEl, Join(lesson.TitleEl, lesson.ContentEl, DescribeExercises(lesson, isEl: true)));
    }

    /// <summary>"Course: … — Module: … — Lesson: …", with the labels in the chunk's language.</summary>
    private static string LessonHeader(bool isEl, string courseTitle, string moduleTitle, string lessonTitle)
    {
        var courseLabel = isEl ? CourseLabelEl : CourseLabelEn;
        var moduleLabel = isEl ? ModuleLabelEl : ModuleLabelEn;
        var lessonLabel = isEl ? LessonLabelEl : LessonLabelEn;
        return courseLabel + courseTitle
            + HeaderPartSeparator + moduleLabel + moduleTitle
            + HeaderPartSeparator + lessonLabel + lessonTitle;
    }

    /// <summary>
    /// "Exercises: Pushups (chest, triceps); Plank (abdominals)" in list order, so a search for an
    /// exercise or a muscle finds the workouts that train it. Null for a lesson without published exercises.
    /// </summary>
    /// <remarks>
    /// Deliberate choices: only published exercises are listed, because the lesson page leaves the
    /// others out (CourseService.PublishedPrescriptions). The Greek chunk has a Greek label but keeps
    /// the English muscle values, which are the fixed library vocabulary the coach's search_exercises
    /// filter takes; the embedding model is multilingual.
    /// </remarks>
    private static string? DescribeExercises(Lesson lesson, bool isEl)
    {
        var descriptions = lesson.Exercises
            .OrderBy(prescription => prescription.SortOrder)
            .Select(prescription => prescription.Exercise)
            .Where(exercise => exercise.IsPublished)
            .Select(exercise => $"{Localized.Pick(isEl, exercise.NameEn, exercise.NameEl)} ({exercise.PrimaryMuscles})")
            .ToList();
        if (descriptions.Count == 0)
            return null;

        var label = isEl ? ExercisesLabelEl : ExercisesLabelEn;
        return label + string.Join(ExerciseListSeparator, descriptions);
    }

    private static (string Header, string Text)? BuildAssessmentSource(AssessmentDefinition assessment, string lang)
    {
        if (lang == "en")
            return (AssessmentLabelEn + assessment.TitleEn, Join(assessment.TitleEn, assessment.DescriptionEn));

        if (string.IsNullOrWhiteSpace(assessment.TitleEl))
            return null;

        return (AssessmentLabelEl + assessment.TitleEl, Join(assessment.TitleEl, assessment.DescriptionEl));
    }

    private static (string Header, string Text)? BuildBlogSource(BlogArticle article, string lang)
    {
        if (lang == "en")
            return (BlogLabelEn + article.TitleEn, Join(article.TitleEn, article.SummaryEn, article.ContentEn));

        if (string.IsNullOrWhiteSpace(article.TitleEl))
            return null;

        return (BlogLabelEl + article.TitleEl, Join(article.TitleEl, article.SummaryEl, article.ContentEl));
    }

    private static string Join(params string?[] parts) =>
        string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
