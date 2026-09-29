using System.Text.Json;
using QuickBuilder.Pages.Contracts.Publishing;
using static QuickBuilder.UnitTests.Rendering.RenderHarness;

namespace QuickBuilder.UnitTests.Rendering.Blocks;

/// <summary>Image, Button, Embed and component references as published (012 FR-025 to FR-027, research PR-11).</summary>
public sealed class MediaRendererTests
{
    private readonly RenderHarness _harness = new();

    [Fact]
    public void An_available_image_is_served_from_the_media_address_under_the_base_path()
    {
        _harness.BasePath = "/site";

        var (html, _) = _harness.Block($$$"""{"id":"i","type":"image","props":{"media":{"mediaId":"{{{Photo}}}"},"alt":"A \"photo\""},"children":[]}""");

        Assert.Equal(
            $"<figure class=\"qb-image qb-image--full\"><img src=\"/site/_media/{Photo:N}.jpg\" alt=\"A &quot;photo&quot;\" loading=\"lazy\" decoding=\"async\"></figure>",
            html);
    }

    [Fact]
    public void A_decorative_image_has_empty_alternative_text()
    {
        var (html, _) = _harness.Block($$$"""{"id":"i","type":"image","props":{"media":{"mediaId":"{{{Photo}}}"},"alt":"ignored","decorative":true,"width":"natural"},"children":[]}""");

        Assert.Contains("qb-image--natural", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_image_is_left_out_entirely()
    {
        Assert.Equal(string.Empty, _harness.Block($$$"""{"id":"i","type":"image","props":{"media":{"mediaId":"{{{Guid.NewGuid()}}}"}},"children":[]}""").Html);
        Assert.Equal(string.Empty, _harness.Block("""{"id":"i","type":"image","props":{},"children":[]}""").Html);
    }

    [Fact]
    public void A_linked_image_is_wrapped_in_its_link()
    {
        var (html, _) = _harness.Block($$$"""{"id":"i","type":"image","props":{"media":{"mediaId":"{{{Photo}}}"},"link":{"type":"page","pageId":"{{{LivePage}}}"}},"children":[]}""");

        Assert.Contains("<a href=\"/contact\"><img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_button_to_an_external_address_opens_safely_in_a_new_tab()
    {
        var (html, rules) = _harness.Block("""
            {"id":"b","type":"button","props":{"label":"Go","style":"outline","align":"end","newTab":true,
             "link":{"type":"url","href":"https://example.com"}},"children":[]}
            """);

        Assert.Equal(
            "<div class=\"qb-button-row qb-align--end\"><a class=\"qb-button qb-button--outline\" data-qb=\"p.b\" href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">Go</a></div>",
            html);
        Assert.Contains("--qb-button-brand:var(--qb-color-color-brand)", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void A_button_to_a_page_that_is_not_live_is_plain_text_with_the_same_classes()
    {
        var (html, _) = _harness.Block($$$"""{"id":"b","type":"button","props":{"label":"Soon","link":{"type":"page","pageId":"{{{DraftOnlyPage}}}"}},"children":[]}""");

        Assert.Equal("<div class=\"qb-button-row qb-align--start\"><span class=\"qb-button qb-button--primary\" data-qb=\"p.b\">Soon</span></div>", html);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=10", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/123456", "https://player.vimeo.com/video/123456?dnt=1")]
    public void An_embed_is_the_providers_lazy_privacy_enhanced_player(string url, string player)
    {
        var (html, rules) = _harness.Block($$$"""{"id":"e","type":"embed","props":{"url":"{{{url}}}","title":"Launch video","aspectRatio":"4:3"},"children":[]}""");

        Assert.Contains($"src=\"{player.Replace("&", "&amp;", StringComparison.Ordinal)}\"", html, StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", html, StringComparison.Ordinal);
        Assert.Contains("title=\"Launch video\"", html, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio:4 / 3", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void An_embed_from_another_provider_is_not_written()
    {
        Assert.Equal(string.Empty, _harness.Block("""{"id":"e","type":"embed","props":{"url":"https://evil.example/player"},"children":[]}""").Html);
    }

    [Fact]
    public void A_component_is_drawn_in_place_with_its_own_style_scope()
    {
        var component = Guid.NewGuid();
        using var document = JsonDocument.Parse("""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[]}]}}
            """);
        _harness.Components[component] = new LiveDocument(1, document.RootElement.Clone());

        var page = _harness.Page(Document($$$"""
            [{"id":"ref","type":"component-ref","props":{"componentId":"{{{component}}}"},"children":[]},
             {"id":"s","type":"section","props":{},"children":[]}]
            """));

        Assert.Contains($"data-qb=\"c.{component:N}.s\"", page, StringComparison.Ordinal);
        Assert.Contains("data-qb=\"p.s\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_without_a_live_version_draws_nothing()
    {
        var page = _harness.Page(Document($$$"""[{"id":"ref","type":"component-ref","props":{"componentId":"{{{Guid.NewGuid()}}}"},"children":[]}]"""));

        Assert.Contains("<div class=\"qb-canvas qb-page\"></div>", page, StringComparison.Ordinal);
    }
}
