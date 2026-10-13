using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>Uploads resolve to /api/media/…; shipped files and full URLs stay as they are.</summary>
public class MediaUrlsTests
{
    [Theory]
    [InlineData("blog/covers/a.jpg", "/api/media/blog/covers/a.jpg")]
    [InlineData(" testimonials/avatars/b.png ", "/api/media/testimonials/avatars/b.png")]
    public void Resolve_UploadedPath_PointsAtTheMediaEndpoint(string storedPath, string expected)
    {
        MediaUrls.Resolve(storedPath).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/images/blog/a.webp")]
    [InlineData("https://example.org/cover.jpg")]
    [InlineData("HTTP://example.org/cover.jpg")]
    public void Resolve_SiteRootPathOrFullUrl_IsUnchanged(string storedPath)
    {
        MediaUrls.Resolve(storedPath).ShouldBe(storedPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NoPath_ReturnsNull(string? storedPath)
    {
        MediaUrls.Resolve(storedPath).ShouldBeNull();
    }
}
