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

    public static IServiceCollection AddFitEduHsts(this IServiceCollection services) =>
        services.AddHsts(options => options.MaxAge = HstsMaxAge);

    public static WebApplication UseFitEduSecurityHeaders(this WebApplication app)
    {
        // script-src no longer needs 'unsafe-inline' (SEC-5): the app has zero literal
        // inline <script> blocks, and Blazor's own @onclick-style handlers are wired via
        // its SignalR runtime, not inline HTML attributes — the only things that DID need
        // it were the two <link onload="..."> lazy-CSS attributes in App.razor, now handled
        // by the external js/lazy-css.js instead. style-src still needs 'unsafe-inline':
        // dozens of components bind style="@expr" (a legitimate, common Razor pattern),
        // and CSS injection is a materially smaller blast radius than script injection —
        // tightening that is a separate, much larger pass across every component.
        // In Development, allow VS BrowserLink (http://localhost:*) and hot-reload
        // (ws://localhost:*) in connect-src. These are dev-only tools; the condition
        // ensures the looser directive never reaches production.
        var devConnectSrc = app.Environment.IsDevelopment()
            ? " http://localhost:* ws://localhost:*"
            : string.Empty;
        var contentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self' https://cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "font-src 'self' data:; " +
            "img-src 'self' data: blob:; " +
            "media-src 'self' blob:; " +
            "frame-src 'self' https://www.youtube.com https://www.youtube-nocookie.com; " +
            $"connect-src 'self' https://cdn.jsdelivr.net{devConnectSrc}; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self';";

        app.Use(async (ctx, next) =>
        {
            // The headers go on as the response starts, not before the pipeline runs: the exception handler
            // clears every header when it answers an unhandled exception, which used to leave the 500 bare
            // (P03-8). A header the endpoint already set for itself (the media endpoint's stricter
            // Content-Security-Policy) is kept, as it was when this ran first.
            ctx.Response.OnStarting(() =>
            {
                var headers = ctx.Response.Headers;
                headers.TryAdd("X-Content-Type-Options", "nosniff");
                headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                headers.TryAdd("X-Frame-Options", "DENY");
                headers.TryAdd("Permissions-Policy", "camera=(self), microphone=(self), display-capture=(self), geolocation=()");
                headers.TryAdd("Content-Security-Policy", contentSecurityPolicy);
                return Task.CompletedTask;
            });
            await next();
        });

        return app;
    }
}
