using QuickBuilder.Sites.Domain;
using Xunit;

namespace QuickBuilder.UnitTests.Sites;

/// <summary>The fixed language list (009 research S-09).</summary>
public sealed class SupportedLanguagesTests
{
    [Fact]
    public void The_list_holds_forty_distinct_tags_and_includes_the_default()
    {
        Assert.Equal(40, SupportedLanguages.All.Count);
        Assert.Equal(40, SupportedLanguages.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(SupportedLanguages.Default, SupportedLanguages.All);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData(" de ", "de")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("ZH-HANS", "zh-Hans")]
    public void Tags_are_found_regardless_of_case_and_returned_canonical(string input, string expected)
    {
        Assert.True(SupportedLanguages.TryCanonicalise(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx")]
    [InlineData("en-US")]
    [InlineData("english")]
    public void Anything_else_is_unsupported(string? input)
    {
        Assert.False(SupportedLanguages.TryCanonicalise(input, out _));
    }
}
