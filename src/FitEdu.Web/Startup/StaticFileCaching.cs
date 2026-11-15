using Microsoft.AspNetCore.StaticFiles;

namespace FitEdu.Web.Startup;

/// <summary>
/// PERF-12: the shipped images under /images change only with a deploy, so browsers may keep them
/// for a week instead of revalidating on every page load. Only /images is served by the static file middleware (Program.cs); everything else is MapStaticAssets.
/// </summary>
public static class StaticFileCaching
{
    /// <summary>Seven days, in seconds.</summary>
    public const string ImageCacheControl = "public,max-age=604800";

    public static readonly PathString ImagesPath = new("/images");

    public static StaticFileOptions CreateOptions() => new() { OnPrepareResponse = ApplyImageCaching };

    private static void ApplyImageCaching(StaticFileResponseContext context)
    {
        // Safety guard: the middleware is branched to /images in Program.cs, but a path change there must not
        // put a week-long cache on other files.
        var isShippedImage = context.Context.Request.Path.StartsWithSegments(ImagesPath);
        if (!isShippedImage)
        {
            return;
        }

        context.Context.Response.Headers.CacheControl = ImageCacheControl;
    }
}
