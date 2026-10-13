using System.Globalization;
using System.Security;
using System.Text;

namespace FitEdu.Web.Services;

/// <summary>
/// Writes the XML sitemap: one &lt;url&gt; per public page, with the site address in front of
/// each path and every value XML-escaped.
/// </summary>
public sealed class SitemapBuilder
{
    /// <summary>
    /// The public site address setting. Canonical links (SeoHead) read the same key, so they
    /// match the sitemap URLs.
    /// </summary>
    public const string BaseUrlSetting = "Sitemap:BaseUrl";

    private const string DateFormat = "yyyy-MM-dd";

    private readonly string _escapedBaseUrl;
    private readonly StringBuilder _urls = new();

    public SitemapBuilder(string baseUrl)
    {
        _escapedBaseUrl = SecurityElement.Escape(baseUrl.TrimEnd('/'));
    }

    /// <param name="path">Site path starting with "/", e.g. "/paths".</param>
    /// <param name="priority">Relative importance, "0.0" to "1.0".</param>
    /// <param name="changeFrequency">"weekly", "monthly", "yearly" and so on.</param>
    /// <param name="lastModified">Optional last change date.</param>
    public void AddPage(string path, string priority, string changeFrequency, DateTimeOffset? lastModified = null)
    {
        var location = _escapedBaseUrl + SecurityElement.Escape(path);
        var lastModifiedElement = lastModified is null
            ? string.Empty
            : $"<lastmod>{lastModified.Value.ToString(DateFormat, CultureInfo.InvariantCulture)}</lastmod>";

        _urls.AppendLine(CultureInfo.InvariantCulture,
            $"  <url><loc>{location}</loc>{lastModifiedElement}<priority>{priority}</priority><changefreq>{changeFrequency}</changefreq></url>");
    }

    public string ToXml()
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        xml.Append(_urls);
        xml.AppendLine("</urlset>");
        return xml.ToString();
    }
}
