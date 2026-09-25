using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.UnitTests.Storage;

/// <summary>
/// The key grammar is the storage layer's security boundary (003 FR-021, research M-03).
/// </summary>
public sealed class StorageKeyTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("health-probe")]
    [InlineData("3f2c0a9b1d4e4f6a8b7c9d0e1f2a3b4c/media/7d41c2a90b6e4f0f9c3d8e21a5b7e0c2")]
    [InlineData("a/b/c/d/e/f/g/h")]
    public void Accepts_keys_that_follow_the_grammar(string value)
    {
        var key = new StorageKey(value);

        Assert.Equal(value, key.Value);
        Assert.Equal(value, key.ToString());
        Assert.True(StorageKey.IsValid(value));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("..", "lower-case")]
    [InlineData("a/../b", "lower-case")]
    [InlineData("/a", "empty segment")]
    [InlineData("a/", "empty segment")]
    [InlineData("a//b", "empty segment")]
    [InlineData("A/b", "lower-case")]
    [InlineData("a/%2e%2e", "lower-case")]
    [InlineData("a\\b", "lower-case")]
    [InlineData("c:/windows", "lower-case")]
    [InlineData("a b", "lower-case")]
    [InlineData("a.jpg", "lower-case")]
    [InlineData("a/b/c/d/e/f/g/h/i", "at most 8")]
    public void Rejects_keys_outside_the_grammar_naming_the_rule(string value, string rule)
    {
        var ex = Assert.Throws<ArgumentException>(() => new StorageKey(value));

        Assert.Contains(rule, ex.Message, StringComparison.Ordinal);
        Assert.False(StorageKey.IsValid(value));
    }

    [Fact]
    public void Rejects_a_segment_longer_than_64_characters()
    {
        Assert.True(StorageKey.IsValid(new string('a', 64)));
        Assert.Throws<ArgumentException>(() => new StorageKey(new string('a', 65)));
    }

    [Fact]
    public void Exposes_the_first_segment_which_is_the_site_for_module_keys()
    {
        var key = new StorageKey("siteabc/media/file123");

        Assert.Equal("siteabc", key.FirstSegment);
        Assert.Equal(3, key.SegmentCount);
        Assert.Equal("only", new StorageKey("only").FirstSegment);
    }

    [Fact]
    public void Starts_with_compares_ordinally()
    {
        var key = new StorageKey("site-a/media/x");

        Assert.True(key.StartsWith("site-a/"));
        Assert.True(key.StartsWith("site-a/media/"));
        Assert.True(key.StartsWith(string.Empty));
        Assert.False(key.StartsWith("site-b/"));
        Assert.False(key.StartsWith("SITE-A/"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("site-a/", true)]
    [InlineData("site-a/media/", true)]
    [InlineData("site-a/me", true)]
    [InlineData("../", false)]
    [InlineData("/", false)]
    [InlineData("Site/", false)]
    [InlineData("a//", false)]
    public void Validates_listing_prefixes(string prefix, bool valid) =>
        Assert.Equal(valid, StorageKey.IsValidPrefix(prefix));

    [Fact]
    public void Parse_is_the_constructor() =>
        Assert.Equal(new StorageKey("a/b"), StorageKey.Parse("a/b"));
}
