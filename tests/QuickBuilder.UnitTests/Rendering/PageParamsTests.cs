using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering.Blocks;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// The page numbers a request asks of a page's lists, as the cache key holds them (016 research
/// TB-11, FR-027; tasks T071), and the numbers a pager shows.
/// </summary>
public sealed class PageParamsTests
{
    private static readonly string[] Declared = ["page", "people"];

    private static string Key(params (string Name, string? Value)[] query) =>
        PageParams.From(Declared, query.ToDictionary(q => q.Name, q => q.Value, StringComparer.Ordinal)).Key;

    [Fact]
    public void Only_the_lists_own_parameters_are_kept_sorted()
    {
        Assert.Equal("page=3&people=2", Key(("people", "2"), ("utm_source", "mail"), ("page", "3")));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("02")]
    [InlineData("2.5")]
    [InlineData("3x")]
    [InlineData("10001")]
    [InlineData("99999999999")]
    [InlineData("")]
    [InlineData(null)]
    public void The_first_page_and_malformed_values_are_dropped(string? value)
    {
        Assert.Equal(string.Empty, Key(("page", value)));
    }

    [Fact]
    public void Pages_two_to_ten_thousand_are_kept()
    {
        Assert.Equal("page=2", Key(("page", "2")));
        Assert.Equal("page=10000", Key(("page", "10000")));
    }

    [Fact]
    public void The_plain_address_the_first_page_and_unknown_parameters_share_one_key()
    {
        Assert.Equal(Key(), Key(("page", "1")));
        Assert.Equal(Key(), Key(("utm", "x")));
        Assert.Same(PageParams.None, PageParams.From([], new Dictionary<string, string?> { ["page"] = "4" }));
    }

    [Fact]
    public void A_list_reads_its_own_page_and_the_first_when_absent()
    {
        var pages = PageParams.From(Declared, new Dictionary<string, string?> { ["people"] = "3" });

        Assert.Equal(3, pages.PageOf("people"));
        Assert.Equal(1, pages.PageOf("page"));
    }

    [Theory]
    [InlineData(1, 1, new[] { 1 })]
    [InlineData(1, 3, new[] { 1, 2, 3 })]
    [InlineData(1, 20, new[] { 1, 2, 3, 0, 20 })]
    [InlineData(10, 20, new[] { 1, 0, 8, 9, 10, 11, 12, 0, 20 })]
    [InlineData(20, 20, new[] { 1, 0, 18, 19, 20 })]
    [InlineData(4, 7, new[] { 1, 2, 3, 4, 5, 6, 7 })]
    public void A_pager_shows_the_first_the_last_and_two_neighbours_with_gaps(int current, int last, int[] expected)
    {
        Assert.Equal(expected, Pager.Numbers(current, last));
        Assert.True(Pager.Numbers(current, last).Count(n => n > 0) <= 7);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(40, 10, 4)]
    public void The_last_page_counts_partial_pages(int total, int pageSize, int last)
    {
        Assert.Equal(last, Pager.LastPage(total, pageSize));
    }
}
