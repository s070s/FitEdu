namespace FitEdu.TestSupport;

/// <summary>
/// Absolute paths into the repository checkout, for tests that read real repo files
/// (seed JSON, web assets, resources) instead of copies in the test bin folder.
/// </summary>
public static class RepoPaths
{
    private const string SolutionFileName = "FitEdu.sln";

    /// <summary>The folder holding FitEdu.sln, found by walking up from the test bin folder.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>The web app's static files: src/FitEdu.Web/wwwroot.</summary>
    public static string WebRoot { get; } = Path.Combine(RepoRoot, "src", "FitEdu.Web", "wwwroot");

    /// <summary>The seed data folders (Courses, Assessments, Exercises, Students): src/FitEdu.Shared/JSON.</summary>
    public static string SeedJsonRoot { get; } = Path.Combine(RepoRoot, "src", "FitEdu.Shared", "JSON");

    /// <summary>The localisation resources (*.resx, *.el.resx, *.Designer.cs): src/FitEdu.Shared/Resources.</summary>
    public static string ResourcesRoot { get; } = Path.Combine(RepoRoot, "src", "FitEdu.Shared", "Resources");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"{SolutionFileName} not found above the test directory");
    }
}
