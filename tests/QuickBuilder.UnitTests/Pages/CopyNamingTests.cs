using QuickBuilder.Pages.Domain;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>Slugs, titles and names of copies (023 FR-020, research CE-11).</summary>
public sealed class CopyNamingTests
{
    [Fact]
    public void A_copy_slug_ends_in_copy_and_counts_up_past_taken_ones()
    {
        Assert.Equal("pricing-copy", CopyNaming.Slug("pricing", []));
        Assert.Equal("pricing-copy-2", CopyNaming.Slug("pricing", ["pricing-copy"]));
        Assert.Equal("pricing-copy-4", CopyNaming.Slug("pricing", ["pricing-copy", "pricing-copy-2", "pricing-copy-3"]));
    }

    [Fact]
    public void A_copy_slug_fits_the_slug_limit_without_a_trailing_hyphen()
    {
        var longSlug = new string('a', 95) + "-bcde";
        var copy = CopyNaming.Slug(longSlug, []);

        Assert.Equal(100, copy.Length);
        Assert.EndsWith("-copy", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("--", copy, StringComparison.Ordinal);

        var cut = CopyNaming.Slug(new string('a', 94) + "-b", []);
        Assert.Equal(new string('a', 94) + "-copy", cut);
    }

    [Fact]
    public void A_copy_title_or_name_is_trimmed_and_cut_to_its_limit()
    {
        Assert.Equal("Pricing (copy)", CopyNaming.Text("  Pricing (copy) ", 200));
        Assert.Equal(200, CopyNaming.Text(new string('x', 250), 200).Length);
        Assert.Equal("Copy", CopyNaming.Text("   ", 200, fallback: "Copy"));
    }

    [Fact]
    public void A_taken_component_name_gets_a_number_ignoring_case()
    {
        Assert.Equal("Header (copy)", CopyNaming.Unique("Header (copy)", ["Header"], 100));
        Assert.Equal("Header (copy) 2", CopyNaming.Unique("Header (copy)", ["header (COPY)"], 100));
        Assert.Equal("Header (copy) 3", CopyNaming.Unique("Header (copy)", ["Header (copy)", "Header (copy) 2"], 100));

        var near = new string('n', 99);
        var numbered = CopyNaming.Unique(near, [near], 100);
        Assert.Equal(100, numbered.Length);
        Assert.EndsWith(" 2", numbered, StringComparison.Ordinal);
    }
}
