using System.Text.RegularExpressions;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.Preview;

namespace QuickBuilder.UnitTests.Rendering.Preview;

/// <summary>
/// The preview bar (022 research PV-13, spec FR-020, FR-021): what it says, where it sits, and
/// that it hides without a script and changes nothing in the page below it.
/// </summary>
public sealed partial class PreviewBarTests
{
    private static readonly DateTimeOffset Saved = new(2026, 11, 19, 10, 42, 0, TimeSpan.Zero);

    private static PreviewContext Context(PreviewBarModel bar, PreviewMode mode = PreviewMode.Member) =>
        new(mode, bar, new PreviewAddresses("https://example.com", "/_preview/sites/s/media", "/_preview/assets", "/_preview/sites/s/pages/p", "/_preview/form"));

    [Fact]
    public void A_member_bar_names_the_draft_its_saver_and_the_way_back()
    {
        var html = PreviewBar.Html(new PreviewBarModel(
            PreviewState.Draft, Saved, "Lan", EditAddress: "/admin/sites/s/pages/p/edit", ShareAddress: "/admin/sites/s/pages/p/edit?share=1"));

        Assert.Contains("Preview — not live", html, StringComparison.Ordinal);
        Assert.Contains("Draft saved 2026-11-19 10:42 UTC by Lan", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/sites/s/pages/p/edit\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/sites/s/pages/p/edit?share=1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("expires", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_bar_shows_the_expiry_and_no_admin_links()
    {
        var html = PreviewBar.Html(new PreviewBarModel(PreviewState.Draft, Saved, "Lan", ExpiresAt: Saved.AddDays(7)));

        Assert.Contains("This link expires 2026-11-26 10:42 UTC", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/admin", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parts_from_drafts_a_schedule_a_version_and_a_sample_item_are_named()
    {
        var html = PreviewBar.Html(new PreviewBarModel(
            PreviewState.Version, Saved, "Dee", VersionNumber: 7, SampleItemName: "Hello",
            FromDrafts: [new PreviewDraftPart(PreviewPartKind.Component, "Header"), new PreviewDraftPart(PreviewPartKind.Design, string.Empty)],
            ScheduledAt: Saved.AddDays(2)));

        Assert.Contains("Version 7, published 2026-11-19 10:42 UTC by Dee", html, StringComparison.Ordinal);
        Assert.Contains("Showing the item “Hello”", html, StringComparison.Ordinal);
        Assert.Contains("Shared component “Header” shown from its draft (never published)", html, StringComparison.Ordinal);
        Assert.Contains("The site design shown from its draft (never published)", html, StringComparison.Ordinal);
        Assert.Contains("Scheduled to publish 2026-11-21 10:42 UTC", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_that_needs_attention_is_flagged()
    {
        var html = PreviewBar.Html(new PreviewBarModel(PreviewState.Draft, Saved, null, NeedsAttention: true));

        Assert.Contains("This item needs attention: some values may not show", html, StringComparison.Ordinal);
        Assert.Contains("Draft saved 2026-11-19 10:42 UTC", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_are_encoded()
    {
        var html = PreviewBar.Html(new PreviewBarModel(
            PreviewState.Draft, Saved, "<b>Lan</b>", FromDrafts: [new PreviewDraftPart(PreviewPartKind.Menu, "Main & <script>")]));

        Assert.Contains("&lt;b&gt;Lan&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Main &amp; &lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_opens_the_body_outside_the_page_root_with_its_rules_in_the_one_style_element()
    {
        var harness = new RenderHarness { Preview = Context(new PreviewBarModel(PreviewState.Draft, Saved, "Lan")) };

        var html = harness.Page(RenderHarness.Document("""[{"id":"t","type":"text","props":{"text":"Hello"},"children":[]}]"""));
        const string BodyTag = "<body class=\"qb-published\">\n";
        var body = html[(html.IndexOf(BodyTag, StringComparison.Ordinal) + BodyTag.Length)..];

        Assert.StartsWith("<div class=\"qb-preview\"", body, StringComparison.Ordinal);
        Assert.True(body.IndexOf("qb-preview", StringComparison.Ordinal) < body.IndexOf("qb-page", StringComparison.Ordinal));
        Assert.Single(StyleElement().Matches(html));
        Assert.Contains(".qb-preview", StyleElement().Match(html).Value, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html.Replace("<script type=\"application/ld+json\">", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain(" style=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_hides_with_a_labelled_checkbox_and_no_script()
    {
        var html = PreviewBar.Html(new PreviewBarModel(PreviewState.Draft, Saved, "Lan"));

        Assert.Contains("<input type=\"checkbox\" id=\"qb-preview-hide\"", html, StringComparison.Ordinal);
        Assert.Contains("<label for=\"qb-preview-hide\"", html, StringComparison.Ordinal);
        Assert.Contains("Hide bar", html, StringComparison.Ordinal);
        Assert.Contains("#qb-preview-hide:checked", PreviewBar.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_below_the_bar_is_unchanged()
    {
        const string Document = """[{"id":"t","type":"text","props":{"text":"Hello"},"children":[]}]""";
        var plain = new RenderHarness().Page(RenderHarness.Document(Document));
        var previewed = new RenderHarness { Preview = Context(new PreviewBarModel(PreviewState.Draft, Saved, "Lan")) }
            .Page(RenderHarness.Document(Document));

        Assert.Equal(PageRoot(plain), PageRoot(previewed));
        Assert.Equal(StyleElement().Match(plain).Value.Replace("<style>", string.Empty, StringComparison.Ordinal),
            StyleElement().Match(previewed).Value.Replace(PreviewBar.Css, string.Empty, StringComparison.Ordinal).Replace("<style>", string.Empty, StringComparison.Ordinal));
    }

    private static string PageRoot(string html)
    {
        var start = html.IndexOf("<div class=\"qb-canvas qb-page\">", StringComparison.Ordinal);
        return html[start..html.LastIndexOf("</body>", StringComparison.Ordinal)];
    }

    [GeneratedRegex("<style>[^<]*</style>")]
    private static partial Regex StyleElement();
}
