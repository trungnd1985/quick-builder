using QuickBuilder.Media.Contracts;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.UnitTests.Rendering.Preview;

/// <summary>
/// Where a preview's addresses point (022 research PV-06): links to the public site, media and
/// the stylesheet through the preview, and nothing to another draft (spec FR-012, FR-027).
/// </summary>
public sealed class PreviewAddressesTests
{
    private const string LinkMedia = "/_preview/l/secret/media";

    private static PreviewContext Preview(string? siteUrl = "https://example.com") => new(
        PreviewMode.Link,
        new PreviewBarModel(PreviewState.Draft, DateTimeOffset.UnixEpoch, "Lan"),
        new PreviewAddresses(siteUrl, LinkMedia, "/_preview/assets", "/_preview/l/secret", "/_preview/form"));

    private static string Button(Guid pageId) =>
        $$$"""{"id":"b","type":"button","props":{"label":"Go","link":{"type":"page","pageId":"{{{pageId}}}"}},"children":[]}""";

    [Fact]
    public void A_link_to_a_live_page_goes_to_its_public_address()
    {
        var harness = new RenderHarness { Preview = Preview() };

        var (html, _) = harness.Block(Button(RenderHarness.LivePage));

        Assert.Contains("href=\"https://example.com/contact\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_to_a_page_that_is_not_live_or_on_a_site_without_a_host_name_is_not_written()
    {
        var toDraft = new RenderHarness { Preview = Preview() }.Block(Button(RenderHarness.DraftOnlyPage)).Html;
        var noHost = new RenderHarness { Preview = Preview(siteUrl: null) }.Block(Button(RenderHarness.LivePage)).Html;

        Assert.DoesNotContain("href=", toDraft, StringComparison.Ordinal);
        Assert.DoesNotContain("href=", noHost, StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_link_is_unchanged()
    {
        var harness = new RenderHarness { Preview = Preview() };

        var (html, _) = harness.Block("""{"id":"b","type":"button","props":{"label":"Go","link":{"type":"url","href":"https://other.example/x"}},"children":[]}""");

        Assert.Contains("href=\"https://other.example/x\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_published_page_keeps_its_root_relative_links()
    {
        var (html, _) = new RenderHarness { BasePath = "/site" }.Block(Button(RenderHarness.LivePage));

        Assert.Contains("href=\"/site/contact\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Media_and_every_variant_go_through_the_preview()
    {
        var file = new PublicMediaFile("/_media/abc.jpg", "image/jpeg", 800, 400, [new PublicMediaVariant(320, "/_media/abc-w320.jpg")]);

        var rewritten = Preview().Addresses.Media(file);

        Assert.Equal(LinkMedia + "/abc.jpg", rewritten.PublicPath);
        Assert.Equal(LinkMedia + "/abc-w320.jpg", rewritten.Variants.Single().PublicPath);
        Assert.Equal("/elsewhere.jpg", Preview().Addresses.Media("/elsewhere.jpg"));
    }

    [Fact]
    public void The_stylesheet_is_served_through_the_preview()
    {
        var harness = new RenderHarness { BasePath = "/site", Preview = Preview() };

        var html = harness.Page(RenderHarness.Document("[]"));

        Assert.Contains("href=\"/site/_preview/assets/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/_assets/", html, StringComparison.Ordinal);
    }
}
