using QuickBuilder.Rendering.Features.RenderPage;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>A request path as a page address (012 public-rendering.md §2).</summary>
public sealed class PathNormaliserTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/about", "/about")]
    [InlineData("/About", "/about")]
    [InlineData("/about/", "/about")]
    [InlineData("/Services/Web-Design/", "/services/web-design")]
    public void Addresses_are_matched_ignoring_case_and_one_trailing_slash(string? path, string expected)
    {
        Assert.Equal(expected, PathNormaliser.Normalise(path));
    }

    [Theory]
    [InlineData("/a/../admin")]
    [InlineData("/a/./b")]
    [InlineData("/a//b")]
    [InlineData("/a\\b")]
    [InlineData("/a%2Fb")]
    [InlineData("/a\u0000b")]
    [InlineData("about")]
    public void Anything_suspicious_is_no_page(string path)
    {
        Assert.Null(PathNormaliser.Normalise(path));
    }
}
