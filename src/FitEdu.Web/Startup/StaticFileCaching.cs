using Microsoft.AspNetCore.StaticFiles;

namespace FitEdu.Web.Startup;

/// <summary>
/// PERF-12: the shipped images under /images change only with a deploy, so browsers may keep them
/// for a week instead of revalidating on every page load. Other static files keep the default.
/// </summary>
public static class StaticFileCaching
{
    /// <summary>Seven days, in seconds.</summary>
    public const string ImageCacheControl = "public,max-age=604800";

    private static readonly PathString ImagesPath = new("/images");

    public static StaticFileOptions CreateOptions() => new() { OnPrepareResponse = ApplyImageCaching };

    private static void ApplyImageCaching(StaticFileResponseContext context)
    {
        var isShippedImage = context.Context.Request.Path.StartsWithSegments(ImagesPath);
        if (!isShippedImage)
        {
            return;
        }

        context.Context.Response.Headers.CacheControl = ImageCacheControl;
    }
}
