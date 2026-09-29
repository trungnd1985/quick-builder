using QuickBuilder.Rendering.Rendering;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>Content never reaches the page unencoded (012 FR-025, research PR-09).</summary>
public sealed class HtmlWriterTests
{
    [Fact]
    public void Text_is_encoded()
    {
        var html = new HtmlWriter().Text("<script>alert('x')</script> & \"more\"").ToString();

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Letters_outside_ascii_stay_readable()
    {
        // Letters stay as they are; characters beyond the basic plane become numeric
        // references, which browsers show the same.
        Assert.Equal("Dịch vụ – 日本語 &#x1F600;", new HtmlWriter().Text("Dịch vụ – 日本語 😀").ToString());
    }

    [Fact]
    public void Attribute_values_cannot_close_the_attribute()
    {
        var html = new HtmlWriter().Open("img").Attribute("alt", "\" onerror=\"alert(1)").EndTag().ToString();

        Assert.Equal(1, html.Split("onerror").Length - 1);
        Assert.DoesNotContain("\" onerror", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/a?b=c", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:hello@example.com", true)]
    [InlineData("tel:+84 (28) 1234-5678", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JAVASCRIPT:alert(1)", false)]
    [InlineData("data:text/html,<b>x</b>", false)]
    [InlineData("//evil.example/x", false)]
    [InlineData("https://exa mple.com", false)]
    public void Only_allowed_schemes_become_links(string url, bool allowed)
    {
        var writer = new HtmlWriter();

        Assert.Equal(allowed, writer.Url("href", url));
        Assert.Equal(allowed, writer.ToString().Contains("href=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/about", true)]
    [InlineData("/base/_media/x.jpg", true)]
    [InlineData("//evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("about", false)]
    [InlineData("/a\\b", false)]
    public void Site_paths_stay_on_the_site(string path, bool allowed)
    {
        Assert.Equal(allowed, new HtmlWriter().SitePath("href", path));
    }
}
