using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Seo;

namespace QuickBuilder.UnitTests.Rendering.Seo;

/// <summary>The one indexed address of a page (017 FR-011, FR-012; research SE-06).</summary>
public sealed class CanonicalAddressTests
{
    private const string Base = "https://client.example/";
    private static readonly Guid Target = Guid.Parse("0192f5a0-0000-7000-8000-000000000001");

    [Theory]
    [InlineData("https://client.example/", "/", "https://client.example/")]
    [InlineData("https://client.example", "/services", "https://client.example/services")]
    [InlineData("https://client.example/cms/", "/", "https://client.example/cms/")]
    [InlineData("https://client.example/cms", "/services", "https://client.example/cms/services")]
    [InlineData("https://client.example/", "/blog/hello-world", "https://client.example/blog/hello-world")]
    public void A_page_names_itself_on_the_base_url(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, CanonicalAddress.Of(baseUrl, path, null, null, null));
    }

    [Fact]
    public void The_address_is_lower_case()
    {
        Assert.Equal("https://client.example/about", CanonicalAddress.Of(Base, "/About", null, null, null));
    }

    [Fact]
    public void Page_parameters_past_the_first_page_follow_in_name_order()
    {
        var pages = new Dictionary<string, int> { ["people"] = 2, ["page"] = 3, ["first"] = 1 };

        Assert.Equal("https://client.example/blog?page=3&people=2", CanonicalAddress.Of(Base, "/blog", pages, null, null));
    }

    [Fact]
    public void A_page_override_names_the_target_s_current_address()
    {
        var targets = new Dictionary<Guid, PageLinkTarget> { [Target] = new("/original", IsLive: true) };

        Assert.Equal("https://client.example/original", CanonicalAddress.Of(Base, "/copy", null, new SeoCanonical(Target, null), targets));
    }

    [Fact]
    public void A_page_override_to_a_page_that_is_not_live_is_ignored()
    {
        var targets = new Dictionary<Guid, PageLinkTarget> { [Target] = new("/original", IsLive: false) };

        Assert.Equal("https://client.example/copy", CanonicalAddress.Of(Base, "/copy", null, new SeoCanonical(Target, null), targets));
        Assert.Equal("https://client.example/copy", CanonicalAddress.Of(Base, "/copy", null, new SeoCanonical(Guid.NewGuid(), null), targets));
    }

    [Fact]
    public void A_url_override_is_used_as_written()
    {
        Assert.Equal(
            "https://elsewhere.test/Original?x=1",
            CanonicalAddress.Of(Base, "/copy", new Dictionary<string, int> { ["page"] = 2 }, new SeoCanonical(null, "https://elsewhere.test/Original?x=1"), null));
    }
}
