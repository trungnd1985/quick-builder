extern alias releasetool;

using releasetool::QuickBuilder.Release;
using SemanticVersion = releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// The release tool reads the configuration reference at its old path for old releases and at
/// its new path for new ones, and requires deprecations to be announced (026 T039, T096;
/// research OD-06; FR-017, FR-028).
/// </summary>
public sealed class ConfigurationKeysTests : IDisposable
{
    private const string Old = "docs/configuration.md";
    private const string New = "docs/operators/reference/configuration.md";

    private readonly TempRepository _repo = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void An_old_release_is_read_at_the_old_path_and_a_new_one_at_the_new_path()
    {
        _repo.Write(Old, "| `A:One` | x |\n").Commit().Tag("v0.1.0");
        _repo.Delete(Old).Write(New, "| `A:One` | `A__One` | x |\n| `A:Two` | `A__Two` | x |\n").Commit().Tag("v0.2.0");

        Assert.Equal(["A:One"], ConfigurationKeys.At(_repo.Root, "v0.1.0"));
        Assert.Equal(["A:One", "A:Two"], ConfigurationKeys.At(_repo.Root, "v0.2.0").Order(StringComparer.Ordinal));
        Assert.Equal(["A:One", "A:Two"], ConfigurationKeys.At(_repo.Root, null).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_missing_reference_has_no_keys()
    {
        _repo.Write("README.md", "x").Commit().Tag("v0.1.0");

        Assert.Empty(ConfigurationKeys.At(_repo.Root, "v0.1.0"));
        Assert.Empty(ConfigurationKeys.At(_repo.Root, null));
    }

    [Fact]
    public void Deprecated_rows_are_recognised()
    {
        _repo.Write(New, "| `A:One` | x | Old. **Deprecated** — use `A:Two` instead; removed in 2.0.0. |\n| `A:Two` | x | New. |\n");

        Assert.Equal(["A:One"], ConfigurationKeys.DeprecatedAt(_repo.Root, null));
        Assert.True(ConfigurationKeys.IsGuide(Old));
        Assert.True(ConfigurationKeys.IsGuide(New));
    }

    [Fact]
    public void A_newly_deprecated_key_must_be_announced_by_a_fragment()
    {
        var template = File.ReadAllText(Path.Combine(Root(), "docs", "releases", "TEMPLATE.md"));
        _repo.Props("0.2.0")
            .Write("docs/releases/TEMPLATE.md", template)
            .Write(New, "| `A:One` | x | Old. |\n")
            .Write("docs/releases/unreleased/.gitkeep", string.Empty)
            .Commit().Tag("v0.1.0");
        _repo.Write(New, "| `A:One` | x | Old. **Deprecated**; removed in 1.0.0. |\n")
            .Write("docs/releases/unreleased/7-deprecate.md", Fragment(deprecated: string.Empty))
            .Commit();

        var refused = Assert.Throws<ReleaseRuleException>(() => Assemble());
        Assert.Contains("A:One was deprecated", refused.Message, StringComparison.Ordinal);

        _repo.Write("docs/releases/unreleased/7-deprecate.md", Fragment(deprecated: "A:One")).Commit();
        Assert.Contains("[`A:One`](https://trungnd1985.github.io/quick-builder/0.2/reference/configuration#a-one) (deprecated)", Assemble(), StringComparison.Ordinal);
    }

    private string Assemble() =>
        NotesAssembler.Assemble(_repo.Root, SemanticVersion.Parse("0.2.0"), "v0.1.0", ["0.1.0"], patch: false, new DateOnly(2026, 12, 1));

    private static string Fragment(string deprecated) => $"""
        ---
        issue: 7
        kind: changed
        breaking: false
        config:
          added: []
          changed: []
          deprecated: [{deprecated}]
          removed: []
        background: []
        caches: []
        manual: []
        ---
        A:One is deprecated.
        """;

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
