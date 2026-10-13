using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Extensions;
using FitEdu.Shared.Resources.Messages;
using System.Text.Json;

namespace FitEdu.Application.ApiServices;

/// <summary>
/// Student-facing assessment discovery and submission.
/// </summary>
public class AssessmentService(
    IApplicationDbContext db,
    ISubscriptionService subscriptionService,
    IMemoryCache cache,
    ILogger<AssessmentService> logger) : IAssessmentService
{
    public async Task<ServiceResult<PagedResult<AssessmentDefinitionDto>>> GetPublishedAssessmentsAsync(
        string userId, int page, int pageSize, string lang, Guid? categoryId = null, string? search = null,
        CancellationToken cancellationToken = default)
    {
        var userStatus = await subscriptionService.GetUserStatusAsync(userId, cancellationToken);
        if (userStatus.Features?.AssessmentAccess != true)
            return ServiceResult<PagedResult<AssessmentDefinitionDto>>.Forbidden(error: ErrorMessagesRes.AssessmentAccessRequiresPlus);

        var isEl = Localized.IsEl(lang);

        var query = db.AssessmentDefinitions
            .AsNoTracking()
            .Where(a => a.IsPublished);

        if (categoryId is { } catId)
            query = query.Where(a => a.CategoryId == catId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // EF.Functions.Like translates to a sargable LIKE predicate.
            // Explicit ToLower() is dropped — SQL Server's default CI_AS collation
            // makes LIKE case-insensitive without defeating the index.
            var term = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.Like(a.TitleEn, term) || (a.TitleEl != null && EF.Functions.Like(a.TitleEl, term)) ||
                (a.DescriptionEn != null && EF.Functions.Like(a.DescriptionEn, term)) ||
                (a.DescriptionEl != null && EF.Functions.Like(a.DescriptionEl, term)));
        }

        query = query.OrderBy(a => a.TitleEn);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(AssessmentMappings.StudentProjection(isEl))
            .ToListAsync(cancellationToken);

        // Resolve dual-language schema to single-language for student view.
        // Cached per (assessmentId, lang) — avoids re-allocating JsonDocument/MemoryStream on every page load.
        var resolved = items.Select(a => a with { SchemaJson = GetCachedResolvedSchema(a.Id, a.SchemaJson, isEl) }).ToList();

        return ServiceResult<PagedResult<AssessmentDefinitionDto>>.Ok(
            new PagedResult<AssessmentDefinitionDto>(resolved, totalCount, page, pageSize));
    }

    public async Task<ServiceResult<AssessmentDefinitionDto>> GetAssessmentAsync(
        string userId, Guid id, string lang, CancellationToken cancellationToken = default)
    {
        var userStatus = await subscriptionService.GetUserStatusAsync(userId, cancellationToken);
        if (userStatus.Features?.AssessmentAccess != true)
            return ServiceResult<AssessmentDefinitionDto>.Forbidden(error: ErrorMessagesRes.AssessmentAccessRequiresPlus);

        var isEl = Localized.IsEl(lang);

        var assessment = await db.AssessmentDefinitions
            .AsNoTracking()
            .Where(a => a.Id == id && a.IsPublished)
            .Select(AssessmentMappings.StudentProjection(isEl))
            .FirstOrDefaultAsync(cancellationToken);

        if (assessment == null)
            return ServiceResult<AssessmentDefinitionDto>.NotFound();

        // Resolve dual-language schema to single-language for student view (cache hit likely after list page)
        var resolved = assessment with { SchemaJson = GetCachedResolvedSchema(assessment.Id, assessment.SchemaJson, isEl) };

        return ServiceResult<AssessmentDefinitionDto>.Ok(resolved);
    }

    public async Task<ServiceResult<AssessmentSubmissionDto>> SubmitAssessmentAsync(
        string userId, Guid id, SubmitAssessmentRequest request, CancellationToken cancellationToken = default)
    {
        var assessment = await db.AssessmentDefinitions
            .Where(a => a.Id == id && a.IsPublished)
            .FirstOrDefaultAsync(cancellationToken);

        if (assessment == null)
            return ServiceResult<AssessmentSubmissionDto>.NotFound(error: ErrorMessagesRes.AssessmentNotFoundOrUnpublished);

        // Check subscription features and tier
        var userStatus = await subscriptionService.GetUserStatusAsync(userId, cancellationToken);
        if (userStatus.Features?.AssessmentAccess != true)
            return ServiceResult<AssessmentSubmissionDto>.Forbidden(error: ErrorMessagesRes.AssessmentAccessRequiresPlus);
        if (userStatus.Tier < assessment.RequiredTier)
            return ServiceResult<AssessmentSubmissionDto>.Forbidden(error: string.Format(ErrorMessagesRes.AssessmentRequiresTierFormat, TierLabels.Name(assessment.RequiredTier)));

        // DQ-2: reject answers that aren't well-formed JSON, reference a question id that
        // doesn't exist on this assessment, or leave a required question unanswered — instead of
        // persisting whatever was posted and only discovering the mismatch when rendering history.
        if (!AnswersMatchSchema(request.AnswersJson, assessment.SchemaJson))
            return ServiceResult<AssessmentSubmissionDto>.BadRequest(error: ErrorMessagesRes.AssessmentAnswersInvalid);

        var submission = new AssessmentSubmission
        {
            Id = Guid.NewGuid(),
            AssessmentDefinitionId = id,
            UserId = userId,
            AnswersJson = request.AnswersJson,
            SummaryJson = request.SummaryJson,
            SubmittedAt = DateTimeOffset.UtcNow
        };

        db.AssessmentSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);

        return ServiceResult<AssessmentSubmissionDto>.Ok(submission.ToDto(assessment.TitleEn));
    }

    public async Task<PagedResult<AssessmentSubmissionDto>> GetMySubmissionsAsync(
        string userId, int page, int pageSize, string sortBy, string sortDir, CancellationToken cancellationToken = default)
    {
        var query = db.AssessmentSubmissions
            .AsNoTracking()
            .Where(s => s.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .ApplySort(sortBy, sortDir)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(AssessmentMappings.SubmissionProjection)
            .ToListAsync(cancellationToken);

        return new PagedResult<AssessmentSubmissionDto>(items, totalCount, page, pageSize, sortBy, sortDir);
    }

    /// Returns cached resolved schema JSON, or computes and caches it on miss.
    /// Key: (assessmentId, "en"|"el"). TTL: 30 minutes — short enough to pick up admin edits.
    private string GetCachedResolvedSchema(Guid assessmentId, string schemaJson, bool isEl)
    {
        var langKey = isEl ? "el" : "en";
        var cacheKey = $"schema:{assessmentId}:{langKey}";

        return cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return ResolveSchemaJsonByLang(assessmentId, schemaJson, isEl);
        })!;
    }

    /// <summary>
    /// Resolves dual-language schema JSON to a single-language format for the student view.
    /// Handles both flat {"questions":[...]} and sectioned {"sections":[{"questions":[...]}]} schemas.
    /// The output always carries every question in one flat "questions" array (the submission
    /// check and the history page read it). A sectioned schema also gets a "sections" array of
    /// {"id","title","questionIds"} so the form can show one section per step with its title.
    /// Each Greek key (titleEl, labelEl, optionsEl, minLabelEl, maxLabelEl) replaces its English
    /// partner when the language is Greek and the Greek value is present.
    /// </summary>
    private string ResolveSchemaJsonByLang(Guid assessmentId, string schemaJson, bool isEl)
    {
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            var root = doc.RootElement;

            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
            writer.WriteStartObject();

            // Copy root properties (id, version, etc.); the title is localized, questions are rewritten below.
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name is "questions" or "sections" or "title" or "titleEl")
                    continue;
                prop.WriteTo(writer);
            }

            WriteLocalizedString(writer, root, "title", "title", isEl);
            WriteSectionOutline(writer, root, isEl);

            writer.WritePropertyName("questions");
            writer.WriteStartArray();
            foreach (var question in CollectQuestions(root))
                WriteResolvedQuestion(writer, question, isEl);
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            // DQ-4: include the assessment id — this is the only signal an operator gets that a
            // specific assessment's schema is corrupt (students still see the raw original JSON).
            logger.LogError(ex, "Failed to resolve schema JSON by language for assessment {AssessmentId}; returning original.", assessmentId);
            return schemaJson;
        }
    }

    /// Writes "sections": [{"id","title","questionIds"}] for a sectioned schema; a flat schema has none.
    private static void WriteSectionOutline(Utf8JsonWriter writer, JsonElement schemaRoot, bool isEl)
    {
        if (!schemaRoot.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
            return;

        writer.WritePropertyName("sections");
        writer.WriteStartArray();
        foreach (var section in sections.EnumerateArray())
        {
            writer.WriteStartObject();
            if (section.TryGetProperty("id", out var sectionId))
            {
                writer.WritePropertyName("id");
                sectionId.WriteTo(writer);
            }
            WriteLocalizedString(writer, section, "title", "title", isEl);
            WriteQuestionIds(writer, section);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteQuestionIds(Utf8JsonWriter writer, JsonElement section)
    {
        writer.WritePropertyName("questionIds");
        writer.WriteStartArray();
        if (section.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
        {
            foreach (var question in questions.EnumerateArray())
            {
                if (question.TryGetProperty("id", out var questionId) && questionId.ValueKind == JsonValueKind.String)
                    writer.WriteStringValue(questionId.GetString());
            }
        }
        writer.WriteEndArray();
    }

    /// Writes one question with its label, options and rating end labels in the page language.
    private static void WriteResolvedQuestion(Utf8JsonWriter writer, JsonElement question, bool isEl)
    {
        writer.WriteStartObject();
        foreach (var property in question.EnumerateObject())
        {
            // Every text that has a language variant is written once, after this loop.
            if (property.Name is "label" or "labelEn" or "labelEl"
                or "options" or "optionsEn" or "optionsEl"
                or "minLabel" or "minLabelEl" or "maxLabel" or "maxLabelEl" or "titleEl")
                continue;
            property.WriteTo(writer);
        }

        // "labelEn"/"optionsEn" come from older editor saves, "label"/"options" from seeds.
        var labelSource = question.TryGetProperty("labelEn", out _) ? "labelEn" : "label";
        WriteLocalizedString(writer, question, labelSource, "label", isEl);
        WriteLocalizedOptions(writer, question, isEl);
        WriteLocalizedString(writer, question, "minLabel", "minLabel", isEl);
        WriteLocalizedString(writer, question, "maxLabel", "maxLabel", isEl);
        writer.WriteEndObject();
    }

    /// Writes <paramref name="outputName"/> from the "…El" key when Greek and present, else from
    /// <paramref name="englishName"/>. Writes nothing when neither key holds a string.
    private static void WriteLocalizedString(
        Utf8JsonWriter writer, JsonElement source, string englishName, string outputName, bool isEl)
    {
        var greekName = (englishName.EndsWith("En", StringComparison.Ordinal) ? englishName[..^2] : englishName) + "El";
        var greek = isEl ? ReadString(source, greekName) : null;
        var value = string.IsNullOrWhiteSpace(greek) ? ReadString(source, englishName) : greek;
        if (value is null)
            return;

        writer.WriteString(outputName, value);
    }

    private static void WriteLocalizedOptions(Utf8JsonWriter writer, JsonElement question, bool isEl)
    {
        var englishName = question.TryGetProperty("optionsEn", out _) ? "optionsEn" : "options";
        var hasGreek = isEl
            && question.TryGetProperty("optionsEl", out var greekOptions)
            && greekOptions.ValueKind == JsonValueKind.Array
            && greekOptions.GetArrayLength() > 0;
        var sourceName = hasGreek ? "optionsEl" : englishName;
        if (!question.TryGetProperty(sourceName, out var options))
            return;

        writer.WritePropertyName("options");
        options.WriteTo(writer);
    }

    private static string? ReadString(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;


    /// Collects question elements from either a flat {"questions":[...]} or sectioned
    /// {"sections":[{"questions":[...]}]} schema.
    private static List<JsonElement> CollectQuestions(JsonElement schemaRoot)
    {
        var questions = new List<JsonElement>();

        if (schemaRoot.TryGetProperty("questions", out var flatQ) && flatQ.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in flatQ.EnumerateArray())
                questions.Add(q);
        }
        else if (schemaRoot.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray())
            {
                if (section.TryGetProperty("questions", out var sectionQ) && sectionQ.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in sectionQ.EnumerateArray())
                        questions.Add(q);
                }
            }
        }

        return questions;
    }

    /// <summary>
    /// DQ-2: structural validation only — confirms <paramref name="answersJson"/> is a JSON
    /// object whose keys are all real question ids on this assessment and that every
    /// <c>required</c> question has a non-blank answer. Does not check answer value/type against
    /// the question's declared type or options — those can legitimately differ between the En/El
    /// option sets a submission may have been rendered against, so per-value checks are left to
    /// client-side validation (AssessmentForm.razor.cs) to avoid false-rejecting a valid Greek
    /// (or English) submission.
    /// </summary>
    private static bool AnswersMatchSchema(string answersJson, string schemaJson)
    {
        JsonElement answersRoot;
        try
        {
            using var answersDoc = JsonDocument.Parse(answersJson);
            if (answersDoc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            answersRoot = answersDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        // Extracted as plain strings (not JsonElements) while schemaDoc is still alive — a
        // JsonElement from CollectQuestions would become invalid once schemaDoc is disposed.
        var questionIds = new HashSet<string>();
        var requiredIds = new HashSet<string>();
        try
        {
            using var schemaDoc = JsonDocument.Parse(schemaJson);
            foreach (var q in CollectQuestions(schemaDoc.RootElement))
            {
                if (!q.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
                    continue;
                var qid = idProp.GetString()!;
                questionIds.Add(qid);
                if (q.TryGetProperty("required", out var reqProp) && reqProp.ValueKind == JsonValueKind.True)
                    requiredIds.Add(qid);
            }
        }
        catch (JsonException)
        {
            // A corrupt stored schema (DQ-4) can't be validated against — don't compound the
            // failure by also rejecting every submission for it.
            return true;
        }

        foreach (var answer in answersRoot.EnumerateObject())
        {
            if (!questionIds.Contains(answer.Name))
                return false;
        }

        foreach (var requiredId in requiredIds)
        {
            // A multi-select answer is a JSON array of options; an empty one is no answer.
            if (!answersRoot.TryGetProperty(requiredId, out var value) ||
                value.ValueKind != JsonValueKind.String ||
                MultiSelectAnswer.IsBlank(value.GetString()))
                return false;
        }

        return true;
    }
}
