namespace FitEdu.Web.Layout;

/// <summary>
/// Bootstrap Icons classes for the main destinations, kept in one place so the header, the bottom
/// tab bar and the search results show the same glyph for the same thing.
/// </summary>
public static class AppIcons
{
    public const string Home = "bi-house-door";
    public const string Course = "bi-collection-play";
    public const string Exercise = "bi-activity";
    public const string Path = "bi-signpost-split";
    public const string Session = "bi-camera-video";
    public const string Assessment = "bi-clipboard2-pulse";
    public const string Certificate = "bi-award";
    public const string Chat = "bi-chat-dots";
    public const string BlogArticle = "bi-newspaper";
    public const string Pricing = "bi-credit-card";
    public const string Billing = "bi-receipt";
    public const string Profile = "bi-person-circle";
    public const string Login = "bi-box-arrow-in-right";
    public const string Register = "bi-person-plus";
    public const string Users = "bi-people";
    public const string Analytics = "bi-bar-chart-line";
    public const string Categories = "bi-tags";
    public const string Testimonials = "bi-chat-quote";
    public const string Reviews = "bi-star";
    public const string Document = "bi-file-earmark";
    public const string Notification = "bi-bell";
    public const string Search = "bi-search";

    /// <summary>The icon for a site-search hit, by its <c>SourceType</c> ("Course", "Exercise", …).</summary>
    public static string ForSearchSource(string sourceType) => sourceType switch
    {
        "Course" => Course,
        "Assessment" => Assessment,
        "BlogArticle" => BlogArticle,
        "Exercise" => Exercise,
        _ => Document
    };
}
