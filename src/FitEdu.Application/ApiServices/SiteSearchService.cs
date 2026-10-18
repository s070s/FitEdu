using Microsoft.EntityFrameworkCore;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;

namespace FitEdu.Application.ApiServices;

/// <inheritdoc cref="ISiteSearchService"/>
public partial class SiteSearchService(
    IApplicationDbContext db,
    IAssistantRetrievalService retrieval,
    AssistantRuntimeState assistantState,
    ILogger<SiteSearchService> logger) : ISiteSearchService
{
    public async Task<SiteSearchResultDto> SearchAsync(
        string query, string language, int limit, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return new SiteSearchResultDto([], SemanticSearchUsed: false);

        // Only attempt semantic search once the assistant is actually Ready — before that the
        // chunk cache is empty (Disabled/OllamaUnreachable/DownloadingModels), so a title search
        // is both faster and the only one that can find anything.
        if (assistantState.Status == AssistantAvailability.Ready)
        {
            try
            {
                var hits = await retrieval.SearchGroupedAsync(query, language, limit, cancellationToken);
                if (hits.Count > 0)
                    return new SiteSearchResultDto(await WithExerciseHitsAsync(ToHitDtos(hits), query, language, limit, cancellationToken), SemanticSearchUsed: true);
            }
            catch (Exception ex)
            {
                LogSemanticSearchFailed(logger, ex);
            }
        }

        return new SiteSearchResultDto(await TitleSearchAsync(query, language, limit, cancellationToken), SemanticSearchUsed: false);
    }

    private static List<SiteSearchHitDto> ToHitDtos(IReadOnlyList<AssistantSearchHit> hits) =>
        hits.Select(h => new SiteSearchHitDto(h.SourceType.ToString(), h.Title, Truncate(WithoutHeader(h.Snippet)), h.Url)).ToList();

    /// <summary>
    /// The semantic index holds no exercises (AssistantSourceType has no Exercise), so their name
    /// matches are added to the semantic hits: shared out fairly, without a repeated URL, within the limit.
    /// </summary>
    private async Task<List<SiteSearchHitDto>> WithExerciseHitsAsync(
        List<SiteSearchHitDto> semanticHits, string query, string language, int limit, CancellationToken cancellationToken)
    {
        var semanticUrls = semanticHits.Select(hit => hit.Url).ToHashSet();
        var exerciseHits = (await ExerciseTitleSearchAsync(query, language, limit, cancellationToken))
            .Where(hit => !semanticUrls.Contains(hit.Url))
            .ToList();

        return TakeFairly([semanticHits, exerciseHits], limit);
    }

    /// <summary>
    /// A chunk starts with its metadata header line ("Course: … — Module: …", see AssistantChunker.Chunk);
    /// the hit already shows the title, so the snippet starts at the content.
    /// </summary>
    private static string WithoutHeader(string chunkText)
    {
        var headerEnd = chunkText.IndexOf(HeaderLineEnd);
        return headerEnd < 0 ? chunkText : chunkText[(headerEnd + 1)..];
    }

    private async Task<List<SiteSearchHitDto>> TitleSearchAsync(
        string query, string language, int limit, CancellationToken cancellationToken)
    {
        var isEl = Localized.IsEl(language);

        // Plain .Contains (not EF.Functions.Like): translates to a sargable LIKE '%term%' on SQL
        // Server under its default case-insensitive collation, and — unlike EF.Functions.Like —
        // also runs on the InMemory provider the Web.Tests factory uses.
        var courses = await db.Courses.AsNoTracking()
            .Where(c => c.IsPublished && (c.TitleEn.Contains(query) || (c.TitleEl != null && c.TitleEl.Contains(query))))
            .OrderBy(c => c.TitleEn)
            .Take(limit)
            .Select(c => new SiteSearchHitDto("Course", isEl ? (c.TitleEl ?? c.TitleEn) : c.TitleEn, null, $"courses/{c.Id}"))
            .ToListAsync(cancellationToken);

        var assessments = await db.AssessmentDefinitions.AsNoTracking()
            .Where(a => a.IsPublished && (a.TitleEn.Contains(query) || (a.TitleEl != null && a.TitleEl.Contains(query))))
            .OrderBy(a => a.TitleEn)
            .Take(limit)
            .Select(a => new SiteSearchHitDto("Assessment", isEl ? (a.TitleEl ?? a.TitleEn) : a.TitleEn, null, $"assessments/{a.Id}"))
            .ToListAsync(cancellationToken);

        var articles = await db.BlogArticles.AsNoTracking()
            .Where(a => a.IsPublished && (a.TitleEn.Contains(query) || (a.TitleEl != null && a.TitleEl.Contains(query))))
            .OrderBy(a => a.TitleEn)
            .Take(limit)
            .Select(a => new SiteSearchHitDto("BlogArticle", isEl ? (a.TitleEl ?? a.TitleEn) : a.TitleEn, null, $"blog/{a.Slug}"))
            .ToListAsync(cancellationToken);

        var exercises = await ExerciseTitleSearchAsync(query, language, limit, cancellationToken);

        // Each source may fill the whole limit on its own; TakeFairly then shares the places out.
        return TakeFairly([courses, assessments, articles, exercises], limit);
    }

    /// <summary>Published exercises whose English or Greek name contains the query, linking to their page.</summary>
    private async Task<List<SiteSearchHitDto>> ExerciseTitleSearchAsync(
        string query, string language, int limit, CancellationToken cancellationToken)
    {
        var isEl = Localized.IsEl(language);
        return await db.Exercises.AsNoTracking()
            .Where(e => e.IsPublished && (e.NameEn.Contains(query) || (e.NameEl != null && e.NameEl.Contains(query))))
            .OrderBy(e => e.NameEn)
            .Take(limit)
            .Select(e => new SiteSearchHitDto("Exercise", isEl ? (e.NameEl ?? e.NameEn) : e.NameEn, null, $"exercises/{e.Slug}"))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Picks up to <paramref name="limit"/> hits round-robin (every source's best hit before any
    /// source's second one), so no source is cut first, then lists them grouped by source again.
    /// </summary>
    private static List<SiteSearchHitDto> TakeFairly(List<List<SiteSearchHitDto>> sources, int limit) =>
        sources
            .SelectMany((hits, sourceIndex) => hits.Select((hit, rank) => (Hit: hit, SourceIndex: sourceIndex, Rank: rank)))
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.SourceIndex)
            .Take(limit)
            .OrderBy(entry => entry.SourceIndex)
            .ThenBy(entry => entry.Rank)
            .Select(entry => entry.Hit)
            .ToList();

    private const int MaxSnippetLength = 160;
    private const char HeaderLineEnd = '\n';

    private static string Truncate(string text) =>
        text.Length <= MaxSnippetLength ? text : text[..MaxSnippetLength] + "…";

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Warning, Message = "SiteSearchService: semantic search failed — falling back to title search.")]
    private static partial void LogSemanticSearchFailed(ILogger logger, Exception exception);
}
