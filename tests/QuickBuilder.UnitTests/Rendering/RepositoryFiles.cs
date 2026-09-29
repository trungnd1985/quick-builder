namespace QuickBuilder.UnitTests.Rendering;

/// <summary>Files shared with the admin's tests, found from the repository root.</summary>
internal static class RepositoryFiles
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("QuickBuilder.slnx not found above the test output.");
    });

    public static string Root => RootPath.Value;

    /// <summary>A path below the repository root.</summary>
    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary><c>tests/fixtures/rendering</c>.</summary>
    public static string Fixtures(params string[] parts) => Path(["tests", "fixtures", "rendering", .. parts]);

    /// <summary><c>docs/contracts/page-schema/examples/valid</c>.</summary>
    public static string ValidExamples => Path("docs", "contracts", "page-schema", "examples", "valid");

    /// <summary>Whether to rewrite golden files instead of comparing with them.</summary>
    public static bool UpdateGolden => Environment.GetEnvironmentVariable("QB_UPDATE_GOLDEN") == "1";
}
