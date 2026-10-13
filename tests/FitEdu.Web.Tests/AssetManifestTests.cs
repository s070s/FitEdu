using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Asset policy guard: every shipped image must be covered by a row in
/// <c>docs/assets/asset-manifest.json</c> whose licence is on the allowed list (public domain,
/// CC0 or our own work). A new image without a row, or a row with a placeholder, fails the test run.
/// </summary>
[Trait("Category", "Assets")]
public class AssetManifestTests
{
    private const string PlaceholderPrefix = "<";

    // The licence policy itself (ASSET-MANIFEST.md lists the same four). It lives here so that
    // widening the list in asset-manifest.json alone fails a test instead of passing every row.
    private static readonly string[] AllowedLicences = ["Unlicense", "CC0-1.0", "Public domain", "MIT (own work)"];

    // The shipped image files the policy covers, relative to wwwroot.
    private static readonly string[] ShippedImagePatterns = ["images/**/*", "favicon.*", "icon-*.png"];

    private static readonly string WebRoot = RepoPaths.WebRoot;
    private static readonly string ManifestPath = Path.Combine(RepoPaths.RepoRoot, "docs", "assets", "asset-manifest.json");

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ManifestRow(
        string Glob,
        string Name,
        string SourceUrl,
        string Licence,
        string LicenceStatedAt,
        string CheckedOn,
        string Type);

    private sealed record Manifest(string[] AllowedLicences, ManifestRow[] Assets);

    [Fact]
    public void EveryImageUnderWwwroot_HasAManifestRow()
    {
        var manifest = LoadManifest();
        var shippedFiles = FindFiles(ShippedImagePatterns);
        var coveredFiles = FindFiles(manifest.Assets.Select(row => row.Glob)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uncoveredFiles = shippedFiles.Where(file => !coveredFiles.Contains(file)).ToList();

        uncoveredFiles.ShouldBeEmpty("files without a manifest row");
    }

    [Fact]
    public void ManifestAllowedLicences_AreExactlyThePolicyList()
    {
        LoadManifest().AllowedLicences.ShouldBe(AllowedLicences, ignoreOrder: true);
    }

    [Fact]
    public void EveryManifestRow_HasAnAllowedLicenceAndNoPlaceholders()
    {
        var manifest = LoadManifest();
        manifest.Assets.ShouldNotBeEmpty();

        foreach (var row in manifest.Assets)
        {
            AllowedLicences.ShouldContain(row.Licence, $"{row.Glob} licence");
            string[] requiredValues = [row.Glob, row.Name, row.SourceUrl, row.LicenceStatedAt, row.CheckedOn, row.Type];
            requiredValues.ShouldAllBe(
                value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal),
                $"{row.Glob} has an empty or placeholder value");
        }
    }

    [Fact]
    public void EveryManifestRow_MatchesAtLeastOneShippedFile()
    {
        var manifest = LoadManifest();

        // A row that matches nothing is stale (the file was removed or renamed) and would hide a gap.
        var staleGlobs = manifest.Assets
            .Select(row => row.Glob)
            .Where(glob => FindFiles([glob]).Count == 0)
            .ToList();

        staleGlobs.ShouldBeEmpty("manifest rows that match no file");
    }

    private static Manifest LoadManifest()
    {
        File.Exists(ManifestPath).ShouldBeTrue($"asset manifest missing at {ManifestPath}");
        var manifestJson = File.ReadAllText(ManifestPath);
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestJson, ManifestJsonOptions);
        return manifest ?? throw new InvalidOperationException("asset manifest is empty");
    }

    /// <summary>Files under wwwroot matching any of the globs, as forward-slash paths relative to wwwroot.</summary>
    private static List<string> FindFiles(IEnumerable<string> globs)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var glob in globs)
        {
            matcher.AddInclude(glob);
        }

        return matcher.GetResultsInFullPath(WebRoot)
            .Select(fullPath => Path.GetRelativePath(WebRoot, fullPath).Replace('\\', '/'))
            .ToList();
    }
}
