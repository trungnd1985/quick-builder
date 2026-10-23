extern alias releasetool;

using releasetool::QuickBuilder.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// Release-note fragments: one per pull request that changes something operators must hear
/// about (019 contracts/release-notes.md §2, research RL-16).
/// </summary>
public sealed class FragmentTests
{
    private const string Valid = """
        ---
        issue: 19
        kind: added
        breaking: false
        config:
          added: [Releases:Foo, "Releases:Bar"]
          changed: []
          removed: []
        background: []
        caches: ["Every cached page renders once more: the block stylesheet changes."]
        manual: []
        ---
        Operators can see the running release in Settings. It is also the first log line.
        """;

    private static Fragment Parse(string text, string name = "19-release-pipeline.md") => Fragment.Parse(name, text);

    [Fact]
    public void A_valid_fragment_is_read()
    {
        var fragment = Parse(Valid);

        Assert.Equal(19, fragment.Issue);
        Assert.Equal("added", fragment.Kind);
        Assert.False(fragment.Breaking);
        Assert.Equal(["Releases:Foo", "Releases:Bar"], fragment.ConfigAdded);
        Assert.Empty(fragment.ConfigRemoved);
        Assert.Equal(["Every cached page renders once more: the block stylesheet changes."], fragment.Caches);
        Assert.StartsWith("Operators can see", fragment.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_keys_may_be_left_out()
    {
        var fragment = Parse("---\nissue: 7\nkind: fixed\nbreaking: false\n---\nA fix.\n", "7-fix.md");

        Assert.Empty(fragment.ConfigAdded);
        Assert.Empty(fragment.Manual);
    }

    [Theory]
    [InlineData("---\nissue: 19\nkind: added\nbreaking: false\nsurprise: yes\n---\nBody.\n", "surprise")]
    [InlineData("---\nissue: 19\nkind: added\n---\nBody.\n", "breaking")]
    [InlineData("---\nissue: 19\nkind: renamed\nbreaking: false\n---\nBody.\n", "kind")]
    [InlineData("---\nissue: 19\nkind: added\nbreaking: maybe\n---\nBody.\n", "breaking")]
    [InlineData("---\nissue: 19\nkind: added\nbreaking: false\nmanual:\n  - do this\n---\nBody.\n", "flow list")]
    [InlineData("---\nissue: 19\nkind: added\nbreaking: false\n---\n# Heading\nBody.\n", "heading")]
    [InlineData("---\nissue: 19\nkind: added\nbreaking: false\n---\n", "body")]
    [InlineData("---\nissue: 19\nkind: added\nbreaking: false\n---\nOne. Two. Three. Four. Five. Six.\n", "sentences")]
    [InlineData("issue: 19\n---\nBody.\n", "front matter")]
    public void A_malformed_fragment_is_refused_with_its_file_and_reason(string text, string reason)
    {
        var error = Assert.Throws<ReleaseRuleException>(() => Parse(text));

        Assert.Contains("19-release-pipeline.md", error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_file_name_must_start_with_the_issue()
    {
        var error = Assert.Throws<ReleaseRuleException>(() => Parse(Valid, "18-other.md"));

        Assert.Contains("18-other.md", error.Message, StringComparison.Ordinal);
    }
}
