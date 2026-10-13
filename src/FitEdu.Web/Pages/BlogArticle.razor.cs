using Microsoft.AspNetCore.Components;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Application.DTOs;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FitEdu.Web.Pages;

public partial class BlogArticle : IDisposable
{
    // Shipped covers are 1200 x 675; the pair reserves a 16:9 box before the photo loads.
    private const int CoverWidth = 1200;
    private const int CoverHeight = 675;
    // An average adult reads about 200 words a minute; every article shows at least one minute.
    private const int WordsPerMinute = 200;
    private const int MinimumReadingMinutes = 1;

    [Parameter] public string Slug { get; set; } = string.Empty;

    [Inject] private IBlogConsumer BlogConsumer { get; set; } = default!;
    [Inject] private PersistentComponentState ApplicationState { get; set; } = default!;

    // Present only while prerendering, so a missing item can answer 404.
    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    private BlogArticleDto? _article;
    private bool _loading = true;
    private bool _notFound;

    private int _readingMinutes;

    private PersistingComponentStateSubscription _persistSub;

    private string? CoverUrl => MediaUrls.Resolve(_article?.CoverImageUrl);

    private string AuthorInitial =>
        string.IsNullOrWhiteSpace(_article?.AuthorName) ? string.Empty : _article.AuthorName.Trim()[..1].ToUpperInvariant();

    private string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    protected override Task OnInitializedAsync()
    {
        _persistSub = ApplicationState.RegisterOnPersisting(PersistArticle);
        return Task.CompletedTask;
    }

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _notFound = false;
        _article = null;

        // Restore from prerender state if available (avoids duplicate API call on circuit connect)
        var key = $"blog-article-{Slug}";
        if (ApplicationState.TryTakeFromJson<BlogArticleDto>(key, out var cached))
        {
            _article = cached;
        }
        else
        {
            _article = await BlogConsumer.GetBySlugAsync(Slug, lang: CurrentLang);
        }

        if (_article is null)
        {
            _notFound = true;
            PrerenderStatus.SetNotFound(HttpContext);
        }
        else
            _readingMinutes = EstimateReadingMinutes(_article.Content);

        _loading = false;
    }

    /// <summary>Reading time from the word count of the article HTML with its tags removed.</summary>
    private static int EstimateReadingMinutes(string html)
    {
        var text = HtmlTag().Replace(html, " ");
        var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var minutes = (int)Math.Round(wordCount / (double)WordsPerMinute);
        return Math.Max(MinimumReadingMinutes, minutes);
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    private Task PersistArticle()
    {
        ApplicationState.PersistAsJson($"blog-article-{Slug}", _article);
        return Task.CompletedTask;
    }

    public void Dispose() => _persistSub.Dispose();
}
