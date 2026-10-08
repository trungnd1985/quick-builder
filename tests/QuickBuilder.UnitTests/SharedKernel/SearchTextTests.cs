using QuickBuilder.Shared.Kernel.Text;

namespace QuickBuilder.UnitTests.SharedKernel;

/// <summary>Search ignores case and accents and treats the term literally (013 FR-006, research MV-03).</summary>
/// <remarks>
/// These run under the product's invariant globalization (Directory.Build.props), where
/// <c>string.Normalize</c> does nothing. That is the case the fold table exists for.
/// </remarks>
public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Été", "ete")]
    [InlineData("BROCHURE-été.pdf", "brochure-ete.pdf")]
    [InlineData("Ångström Œuvre Straße", "angstrom oeuvre strasse")]
    [InlineData("Nguyễn Đức Trung", "nguyen duc trung")]
    [InlineData("Łódź Ørsted Þór", "lodz orsted thor")]
    [InlineData("  many   spaces\there ", "many spaces here")]
    [InlineData("Ελληνικά", "ελληνικά")]
    public void Case_and_accents_fold(string input, string expected) =>
        Assert.Equal(expected, SearchText.Normalise(input));

    [Fact]
    public void Decomposed_input_folds_like_precomposed_input() =>
        // "e" followed by COMBINING ACUTE ACCENT (U+0301): how macOS names files.
        Assert.Equal(SearchText.Normalise("été"), SearchText.Normalise("été"));

    [Fact]
    public void The_search_text_holds_name_and_alternative_text()
    {
        Assert.Equal("desk.jpg anna nguyen at her desk", SearchText.Combine("Desk.jpg", "Anna Nguyễn at her desk"));
        Assert.Equal("desk.jpg", SearchText.Combine("Desk.jpg", null));
        Assert.Equal("desk.jpg", SearchText.Combine("Desk.jpg", "   "));
    }

    [Theory]
    [InlineData("ETE", "%ete%")]
    [InlineData("broch", "%broch%")]
    [InlineData("%", "%\\%%")]
    [InlineData("_", "%\\_%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("50%_off", "%50\\%\\_off%")]
    public void Like_patterns_are_literal(string term, string expected) =>
        Assert.Equal(expected, SearchText.LikePattern(term));

    [Fact]
    public void A_term_of_the_maximum_length_is_handled()
    {
        var term = new string('é', SearchText.MaxTermLength);

        Assert.Equal("%" + new string('e', SearchText.MaxTermLength) + "%", SearchText.LikePattern(term));
    }

    [Fact]
    public void Searching_a_folded_name_with_a_folded_term_matches()
    {
        var stored = SearchText.Combine("BROCHURE-été.pdf", null);

        Assert.Contains(SearchText.Normalise("ÉTÉ"), stored, StringComparison.Ordinal);
    }
}
