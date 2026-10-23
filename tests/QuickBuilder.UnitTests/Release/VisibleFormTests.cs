using QuickBuilder.SmokeTests.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// What visitors read and where links lead, without markup: what the upgrade gate compares
/// between releases (019 contracts/snapshot.md §3, research RL-17).
/// </summary>
public sealed class VisibleFormTests
{
    private const string Page = """
        <!doctype html>
        <html lang="en">
        <head>
          <title>About us</title>
          <link rel="stylesheet" href="/_assets/blocks.3f2a9c1d7e.css">
          <script src="/_assets/form.0123abcd89.js"></script>
          <style>.qb-heading { color: red }</style>
        </head>
        <body class="qb-page">
          <section class="qb-section"><h1 class="qb-heading">Hello   &amp; welcome</h1>
          <p>We   build
             websites.</p>
          <img src="/_media/2f/photo.jpg?w=800" srcset="/_media/2f/photo.jpg?w=400 400w" alt="Our team">
          <a href="/contact">Contact</a></section>
          <script>window.qb = { secret: "not visible" };</script>
          <!-- a comment -->
          <form action="/_forms/abc"><button>Send</button></form>
        </body>
        </html>
        """;

    [Fact]
    public void Takes_the_title_and_the_body_text_with_whitespace_collapsed()
    {
        var form = VisibleForm.Of(Page);

        Assert.Contains("title: About us", form, StringComparison.Ordinal);
        Assert.Contains("text: Hello & welcome We build websites. Contact Send", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_link_targets_sources_and_alternative_text_in_document_order()
    {
        var lines = VisibleForm.Of(Page).Split('\n');

        Assert.Equal(
            [
                "href: /_assets/blocks.css",
                "src: /_assets/form.js",
                "src: /_media/2f/photo.jpg?w=800",
                "srcset: /_media/2f/photo.jpg?w=400 400w",
                "alt: Our team",
                "href: /contact",
                "action: /_forms/abc",
            ],
            lines.Where(l => !l.StartsWith("title:", StringComparison.Ordinal) && !l.StartsWith("text:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Script_style_and_comment_content_is_not_visible()
    {
        var form = VisibleForm.Of(Page);

        Assert.DoesNotContain("secret", form, StringComparison.Ordinal);
        Assert.DoesNotContain("color", form, StringComparison.Ordinal);
        Assert.DoesNotContain("comment", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Markup_classes_and_asset_hashes_do_not_change_the_fingerprint()
    {
        var restyled = Page
            .Replace("<section class=\"qb-section\">", "<div class=\"qb-block qb-section--wide\"><section>", StringComparison.Ordinal)
            .Replace("</section>", "</section></div>", StringComparison.Ordinal)
            .Replace("blocks.3f2a9c1d7e.css", "blocks.9999aaaa0000.css", StringComparison.Ordinal)
            .Replace("class=\"qb-heading\"", "class=\"qb-heading qb-heading--xl\" data-x='1'", StringComparison.Ordinal);

        Assert.Equal(VisibleForm.Fingerprint(Page), VisibleForm.Fingerprint(restyled));
    }

    [Theory]
    [InlineData("websites.", "web sites.")]
    [InlineData("href=\"/contact\"", "href=\"/contact-us\"")]
    [InlineData("alt=\"Our team\"", "alt=\"Our people\"")]
    [InlineData("photo.jpg?w=800", "photo.jpg?w=1200")]
    [InlineData("<title>About us</title>", "<title>About</title>")]
    public void A_change_visitors_can_see_changes_the_fingerprint(string before, string after)
    {
        var changed = Page.Replace(before, after, StringComparison.Ordinal);

        Assert.NotEqual(VisibleForm.Fingerprint(Page), VisibleForm.Fingerprint(changed));
    }

    [Fact]
    public void Attribute_values_in_any_quoting_and_entities_are_read_alike()
    {
        Assert.Equal(
            VisibleForm.Of("<body><a href=\"/a?x=1&amp;y=2\">A &lt;b&gt;</a></body>"),
            VisibleForm.Of("<body><a href='/a?x=1&y=2'>A &lt;b></a></body>"));
        Assert.Contains("href: /x", VisibleForm.Of("<body><a href=/x>X</a></body>"), StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprints_are_labelled_sha256()
    {
        Assert.StartsWith("sha256:", VisibleForm.Fingerprint(Page), StringComparison.Ordinal);
        Assert.Equal(71, VisibleForm.Fingerprint(Page).Length);
    }
}
