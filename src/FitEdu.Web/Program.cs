// Bring App and Routes razor components into scope
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using FitEdu.Web;
using FitEdu.Web.Hubs;
using FitEdu.Web.Logging;
using FitEdu.Web.Startup;

EnvFileLoader.LoadIfPresent(Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(args);

builder.ValidateRequiredConfig();

// --- Logging ---
// LOG-6: anchor the log directory to the content root, not the (launch-dependent) working
// directory, so logs always land in the same place and the daily digest (LOG-1) can find them.
// CLOUD-1: allow relocating logs outside the deploy folder via Logging:File:Directory.
var configuredLogDir = builder.Configuration["Logging:File:Directory"];
var logsDirectory = string.IsNullOrWhiteSpace(configuredLogDir)
    ? Path.Combine(builder.Environment.ContentRootPath, "Logs")
    : configuredLogDir;
builder.Logging.AddFileLogger(logsDirectory);

builder.AddFitEduAuthentication();
builder.AddFitEduServices();
builder.Services.AddFitEduHsts();

var app = builder.Build();

await app.PrewarmAndSeedDatabaseWithRetryAsync();

// --- Pipeline ---
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();

    // OpenAPI JSON document at /openapi/v1.json (Development only).
    app.MapOpenApi();

    // Swagger UI (Swashbuckle) served at /swagger, pointed at the built-in OpenAPI document.
    // Development only — no AddSwaggerGen needed; the UI consumes /openapi/v1.json directly.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "FitEdu API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "FitEdu API — Swagger UI";
        options.EnablePersistAuthorization();
        options.EnableTryItOutByDefault();
    });
}
else
{
    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        // This small pipeline replaces the main one for the 500 response, so it needs HSTS itself
        // (the main pipeline sends it for every non-Development host too).
        exceptionHandlerApp.UseHsts();
        exceptionHandlerApp.Run(async context =>
        {
            // OBS-3: log the unhandled exception with the same traceId ProblemDetails returns to the
            // client, so a user-reported traceId can be located in the logs — and so the exception
            // reaches the file sink, not only the framework's console provider.
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var exLogger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            StartupLog.UnhandledException(exLogger, error, context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problemDetailsService.WriteAsync(new()
            {
                HttpContext = context,
                ProblemDetails = new()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = "Please try again later."
                }
            });
        });
    });
}

// Converts any response that reaches a terminal error status code (400-599) with no body yet
// written into a fitting representation. Re-executes against the dispatcher below, which keeps
// /api/* on the JSON application/problem+json envelope (bare NotFound()/Forbid()/Unauthorized()
// results, 429 rate-limit rejections — the contract API consumers expect) and renders the Blazor
// NotFound page for everything else — most importantly a genuinely unmatched page route, which
// the routing layer 404s on *before* the Router ever runs (the <NotFound> render fragment was
// removed in .NET 10 in favor of Router.NotFoundPage) — so a mistyped/dead URL gets the app shell
// instead of a bare 404 with no navigation (UX-3).
// CLOUD-2: apply X-Forwarded-* before anything reads the scheme (HTTPS redirect, HSTS, Secure
// cookies) so the app works behind a TLS-terminating reverse proxy. No-op when no proxy sends them.
app.UseForwardedHeaders();

// Right after the forwarded headers so every response the pipeline answers carries the security
// headers, the HTTP to HTTPS redirect below included.
app.UseFitEduSecurityHeaders();

app.UseStatusCodePagesWithReExecute("/__status-code-dispatch");

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    // Enforce HTTPS: max-age one year (AddFitEduHsts; the framework default would be 30 days), this host only.
    app.UseHsts();
}

// MapStaticAssets (below) serves the compressed, fingerprinted CSS/JS/fonts. /images is excluded from its
// manifest in the csproj, so only that path goes through the static file middleware (which would
// otherwise answer every wwwroot file first, uncompressed).
app.UseWhen(
    context => context.Request.Path.StartsWithSegments(StaticFileCaching.ImagesPath),
    images => images.UseStaticFiles(StaticFileCaching.CreateOptions()));

// Explicit routing so MapStaticAssets().ShortCircuit() (below) answers fingerprinted assets right here,
// before localization, authentication (a per-request user lookup, and a possible Set-Cookie that would
// sit next to "immutable"), the rate limiter and antiforgery. Auth, rate limiting and antiforgery
// must come after UseRouting.
app.UseRouting();
app.UseRequestLocalization();

// Disabled-user enforcement is handled by two mechanisms:
// - Cookie auth: CookieAuthenticationHandler.OnValidatePrincipal re-validates on every Blazor request.
// - JWT auth: OnTokenValidated looks the user up and fails the token (401) when the user is gone,
//             disabled, or the token's security stamp no longer matches.

app.UseAuthentication();
app.UseAuthorization();

// Must run after UseAuthentication/UseAuthorization: the "sensitive"/"assistant" policies
// partition by httpContext.User's NameIdentifier claim, which is only populated once auth has
// run. Registered earlier, every request — regardless of which user — fell back to a single
// shared "anonymous" partition, silently making the per-user limiter a global one (one user's
// burst could 429 everyone else, exactly what the per-user design was meant to prevent).
app.UseRateLimiter();

app.UseAntiforgery();

app.MapInfrastructureEndpoints();

// Target of UseStatusCodePagesWithReExecute above — branches on the ORIGINAL request path
// (available via IStatusCodeReExecuteFeature, since this endpoint itself is always reached at
// /__status-code-dispatch) so /api/* keeps its JSON contract while page routes get the whole App:
// no route matches this path, so the Router shows its NotFoundPage inside the shell (head, styles,
// tab title, navigation). Rendering NotFound on its own would send a bare fragment. Not meant for direct navigation. Re-execution preserves the ORIGINAL HTTP
// method (e.g. a POST that 403'd), so this must accept any verb — Map(), not MapGet().
app.Map("/__status-code-dispatch", (HttpContext ctx) =>
{
    var originalPath = ctx.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;
    return originalPath is not null && originalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        ? Results.Problem(statusCode: ctx.Response.StatusCode)
        : (IResult)new RazorComponentResult<App>();
}).AllowAnonymous().ExcludeFromDescription();

app.MapControllers();
// CloseOnAuthenticationExpiration closes a connection when its access token expires (60 minutes for the
// bearer token the Blazor circuits mint); the client reconnects with a freshly minted token, which the
// bearer validation rejects for a disabled account or a rotated security stamp.
app.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true);
app.MapHub<CallHub>("/hubs/call", options => options.CloseOnAuthenticationExpiration = true);
app.MapHub<NotificationHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true);
// appsettings.json sets DisableStaticAssetNotFoundRuntimeFallback: when MapStaticAssets runs from the build-time
// manifest (dotnet run, test hosts) it would otherwise map a GET/HEAD-only {**path:file} fallback that turns a
// POST/PUT/DELETE to an unknown URL into 405 instead of 404.
app.MapStaticAssets().ShortCircuit();
app.MapRazorComponents<App>()
   // The security headers middleware already sends frame-ancestors 'none' on every response; Blazor's own
   // frame-ancestors 'self' on interactive pages would be a second Content-Security-Policy header.
   .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null)
   .WithStaticAssets();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
StartupLog.Started(logger, logsDirectory);

app.Run();

// Exposes the implicit top-level Program class as public so integration tests can use
// WebApplicationFactory<Program>. Compile-time only — no runtime effect.
public partial class Program;
