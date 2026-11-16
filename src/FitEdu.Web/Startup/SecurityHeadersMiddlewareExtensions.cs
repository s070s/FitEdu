namespace FitEdu.Web.Startup;

/// <summary>
/// Security response headers (CSP, nosniff, frame-ancestors, etc.), added to every response.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// How long a browser keeps a site on HTTPS only once it has seen the HSTS header: one year, for this host only
    /// (no includeSubDomains, no preload: owner decision, P03-7). ASP.NET Core's own default is 30 days.
    /// </summary>
    public static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);

    /// <summary>The one CDN path the policy allows (path-restricted: the version is part of it); quill-interop.js loads from it.</summary>
    private const string QuillCdnSource = "https://cdn.jsdelivr.net/npm/quill@2.0.3/";

    public static IServiceCollection AddFitEduHsts(this IServiceCollection services) =>
        services.AddHsts(options => options.MaxAge = HstsMaxAge);

    public static WebApplication UseFitEduSecurityHeaders(this WebApplication app)
    {
        // script-src needs no 'unsafe-inline' (SEC-5): the app has zero literal inline <script>
        // blocks and no inline on* handler attributes; Blazor's @onclick-style handlers are wired
        // through its SignalR runtime.
        // style-src keeps 'unsafe-inline' (SEC-5, accepted): the Blazor script's built-in reconnect
        // popup (blazor.web.js 10.0.15, the app has no custom components-reconnect-modal) creates a
        // <style> element with innerHTML, which only 'unsafe-inline' lets through, so a style-src /
        // style-src-attr split would leave that popup unstyled; and dozens of components bind
        // style="@expr". CSS injection is a materially smaller blast radius than script injection.
        // Quill (the admin rich-text editor) comes from jsDelivr until it is vendored (owner download
        // question, DEP-5): only its version's path is allowed, in script-src and style-src, and
        // quill-interop.js loads it with integrity hashes on the first editor (P03-10, P03-11).
        // frame-ancestors 'none' goes with X-Frame-Options: DENY (no page of this site is framed).
        // In Development, allow VS BrowserLink (http://localhost:*) and hot-reload
        // (ws://localhost:*) in connect-src. These are dev-only tools; the condition
        // ensures the looser directive never reaches production.
        var devConnectSrc = app.Environment.IsDevelopment()
            ? " http://localhost:* ws://localhost:*"
            : string.Empty;
        var contentSecurityPolicy =
            "default-src 'self'; " +
            $"script-src 'self' {QuillCdnSource}; " +
            $"style-src 'self' 'unsafe-inline' {QuillCdnSource}; " +
            "font-src 'self' data:; " +
            "img-src 'self' data: blob:; " +
            "media-src 'self' blob:; " +
            "frame-src 'self' https://www.youtube.com https://www.youtube-nocookie.com; " +
            $"connect-src 'self'{devConnectSrc}; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none';";

        app.Use(async (ctx, next) =>
        {
            // The headers go on as the response starts, not before the pipeline runs: the exception handler
            // clears every header when it answers an unhandled exception, which used to leave the 500 bare
            // (P03-8). The four plain headers are assigned, so DENY also beats the SAMEORIGIN the antiforgery
            // middleware adds to a page that sets its cookie. A Content-Security-Policy an endpoint already set
            // for itself (the media endpoint's stricter one for files) is kept.
            ctx.Response.OnStarting(() =>
            {
                var headers = ctx.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["X-Frame-Options"] = "DENY";
                headers["Permissions-Policy"] = "camera=(self), microphone=(self), display-capture=(self), geolocation=()";
                headers.TryAdd("Content-Security-Policy", contentSecurityPolicy);
                return Task.CompletedTask;
            });
            await next();
        });

        return app;
    }
}
