using FitEdu.Web.Services;
using Microsoft.Extensions.FileProviders;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The site's own stylesheets get a content version in their address, so a returning visitor
/// fetches a changed file instead of keeping the copy in their browser cache.
/// </summary>
public sealed class StaticAssetVersionsTests : IDisposable
{
    private const string StylesheetPath = "css/app.css";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "fitedu-asset-versions-" + Guid.NewGuid().ToString("N"));

    private readonly PhysicalFileProvider _files;

    public StaticAssetVersionsTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "css"));
        _files = new PhysicalFileProvider(_webRoot);
    }

    public void Dispose()
    {
        _files.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Fact]
    public void WithVersion_AddsAVersionThatStaysTheSameForTheSameContent()
    {
        WriteStylesheet("body { color: red; }");
        var versions = NewVersions();

        var first = versions.WithVersion(StylesheetPath);
        var second = versions.WithVersion(StylesheetPath);

        first.ShouldStartWith(StylesheetPath + "?v=");
        first.Length.ShouldBeGreaterThan((StylesheetPath + "?v=").Length);
        second.ShouldBe(first);
    }

    [Fact]
    public void WithVersion_ChangesWhenTheFileChanges()
    {
        WriteStylesheet("body { color: red; }");
        var before = NewVersions().WithVersion(StylesheetPath);

        WriteStylesheet("body { color: blue; }");
        var after = NewVersions().WithVersion(StylesheetPath);

        after.ShouldNotBe(before);
    }

    [Fact]
    public void WithVersion_MissingFile_ReturnsThePathUnchanged()
    {
        NewVersions().WithVersion("css/missing.css").ShouldBe("css/missing.css");
    }

    private StaticAssetVersions NewVersions() => new(_files);

    private void WriteStylesheet(string content) =>
        File.WriteAllText(Path.Combine(_webRoot, "css", "app.css"), content);
}
