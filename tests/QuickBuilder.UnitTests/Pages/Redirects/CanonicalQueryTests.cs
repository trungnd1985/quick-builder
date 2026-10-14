using QuickBuilder.Pages.Contracts.Redirects;

namespace QuickBuilder.UnitTests.Pages.Redirects;

/// <summary>One spelling for every equivalent query (017 FR-017a, research SE-09).</summary>
public sealed class CanonicalQueryTests
{
    [Theory]
    [InlineData("id=8", "id=8")]
    [InlineData("b=2&a=1", "a=1&b=2")]
    [InlineData("a=2&a=1", "a=1&a=2")]
    [InlineData("q=caf%C3%A9", "q=caf%C3%A9")]
    [InlineData("q=café", "q=caf%C3%A9")]
    [InlineData("q=a+b", "q=a%20b")]
    [InlineData("q=a%20b", "q=a%20b")]
    [InlineData("Page=1", "Page=1")]
    [InlineData("flag", "flag=")]
    [InlineData("&&a=1&", "a=1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_raw_query_has_one_canonical_form(string? raw, string canonical)
    {
        Assert.Equal(canonical, CanonicalQuery.FromRaw(raw));
    }

    [Fact]
    public void Names_keep_their_letter_case_and_order_by_ordinal_comparison()
    {
        Assert.Equal("B=1&a=2", CanonicalQuery.FromRaw("a=2&B=1"));
    }

    [Fact]
    public void A_request_query_given_as_pairs_matches_the_same_raw_query()
    {
        var pairs = new[] { new KeyValuePair<string, string?>("ref", "mail"), new KeyValuePair<string, string?>("id", "8") };

        Assert.Equal(CanonicalQuery.FromRaw("id=8&ref=mail"), CanonicalQuery.FromPairs(pairs));
    }

    [Fact]
    public void A_leading_question_mark_is_ignored()
    {
        Assert.Equal("id=7", CanonicalQuery.FromRaw("?id=7"));
    }
}
