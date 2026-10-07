using QuickBuilder.Cms.Domain;

namespace QuickBuilder.UnitTests.Cms.Items;

/// <summary>An item's slug: its grammar and the suggestions offered (015 FR-003, research IT-03).</summary>
public sealed class SlugRulesTests
{
    [Theory]
    [InlineData("hello-world", true)]
    [InlineData("a", true)]
    [InlineData("2026-launch", true)]
    [InlineData("Hello", false)]
    [InlineData("-hello", false)]
    [InlineData("hello-", false)]
    [InlineData("hello--world", false)]
    [InlineData("hello world", false)]
    [InlineData("", false)]
    public void The_grammar_is_the_page_slug_grammar(string slug, bool valid) =>
        Assert.Equal(valid, SlugRules.IsValid(slug));

    [Fact]
    public void A_slug_is_at_most_100_characters()
    {
        Assert.True(SlugRules.IsValid(new string('a', 100)));
        Assert.False(SlugRules.IsValid(new string('a', 101)));
    }

    [Theory]
    [InlineData("Hello world", "hello-world")]
    [InlineData("  Hello,   World!  ", "hello-world")]
    [InlineData("Nguyễn Đức Trung", "nguyen-duc-trung")]
    [InlineData("Ångström Œuvre Straße", "angstrom-oeuvre-strasse")]
    [InlineData("Crème brûlée: 3 ways", "creme-brulee-3-ways")]
    [InlineData("!!!", "")]
    [InlineData("", "")]
    public void A_slug_is_built_from_the_name_as_the_admin_does(string name, string slug) =>
        Assert.Equal(slug, SlugRules.FromName(name));

    [Fact]
    public void A_long_name_is_cut_at_a_word_boundary()
    {
        var slug = SlugRules.FromName(string.Join(' ', Enumerable.Repeat("word", 30)));

        Assert.True(slug.Length <= SlugRules.MaxLength);
        Assert.True(SlugRules.IsValid(slug));
        Assert.EndsWith("word", slug, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_with_nothing_to_build_from_gets_the_creation_time()
    {
        var now = new DateTimeOffset(2026, 10, 8, 9, 30, 15, TimeSpan.Zero);

        Assert.Equal("item-20261008093015", SlugRules.Suggest("日本語", now));
        Assert.Equal("item-20261008093015", SlugRules.Suggest(null, now));
        Assert.Equal("hello", SlugRules.Suggest("Hello", now));
    }

    [Fact]
    public void Numbered_suggestions_follow_the_slug()
    {
        Assert.Equal(["hello", "hello-2", "hello-3"], SlugRules.Candidates("hello").Take(3));
    }

    [Fact]
    public void Copies_are_suffixed_and_still_fit()
    {
        Assert.Equal(["hello-copy", "hello-copy-2"], SlugRules.CopyCandidates("hello").Take(2));

        var longest = string.Join('-', Enumerable.Repeat("abcdefghi", 10));
        foreach (var candidate in SlugRules.CopyCandidates(longest).Take(12))
        {
            Assert.True(SlugRules.IsValid(candidate), candidate);
        }
    }
}
