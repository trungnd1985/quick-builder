using static QuickBuilder.UnitTests.Rendering.RenderHarness;

namespace QuickBuilder.UnitTests.Rendering.Blocks;

/// <summary>Image, Button and Embed as published (012 FR-025 to FR-027, research PR-11).</summary>
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

    /// <summary>013 contracts/public-media.md §2: attribute order, variants, never the huge original.</summary>
    [Fact]
    public void A_measured_photo_offers_its_variants_states_its_size_and_never_the_original()
    {
        var (html, _) = _harness.Block($$$"""
            {"id":"s","type":"section","props":{"width":"contained"},"children":[
              {"id":"i","type":"image","props":{"media":{"mediaId":"{{{LargePhoto}}}"},"alt":"Harbour"},"children":[]}]}
            """);

        var id = LargePhoto.ToString("N");
        var srcset = string.Join(", ", new[] { 320, 640, 960, 1280, 1920, 2560 }.Select(w => $"/_media/{id}-v1-w{w}.jpg {w}w"));
        Assert.Contains(
            $"<img src=\"/_media/{id}-v1-w1280.jpg\" srcset=\"{srcset}\" sizes=\"(max-width: 1200px) 100vw, 1200px\" width=\"6000\" height=\"4000\" alt=\"Harbour\" loading=\"lazy\" decoding=\"async\">",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"/_media/{id}.jpg", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("narrow", "full", "(max-width: 720px) 100vw, 720px")]
    [InlineData("wide", "full", "(max-width: 1440px) 100vw, 1440px")]
    [InlineData("full", "full", "100vw")]
    [InlineData("wide", "natural", "(max-width: 800px) 100vw, 800px")]
    [InlineData("narrow", "natural", "(max-width: 720px) 100vw, 720px")]
    public void Sizes_follow_the_section_width_and_a_natural_images_own_width(string section, string width, string expected)
    {
        var (html, _) = _harness.Block($$$"""
            {"id":"s","type":"section","props":{"width":"{{{section}}}"},"children":[
              {"id":"i","type":"image","props":{"media":{"mediaId":"{{{SmallLogo}}}"},"width":"{{{width}}}"},"children":[]}]}
            """);

        Assert.Contains($"sizes=\"{expected}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_small_image_offers_itself_last_and_is_its_own_source_when_nothing_is_narrower()
    {
        var (html, _) = _harness.Block($$$"""{"id":"i","type":"image","props":{"media":{"mediaId":"{{{SmallLogo}}}"}},"children":[]}""");

        var id = SmallLogo.ToString("N");
        Assert.Contains($"srcset=\"/_media/{id}-v2-w320.png 320w, /_media/{id}-v2-w640.png 640w, /_media/{id}-v2.png 800w\"", html, StringComparison.Ordinal);
        Assert.Contains($"src=\"/_media/{id}-v2-w640.png\"", html, StringComparison.Ordinal);
        Assert.Contains("sizes=\"100vw\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_without_variants_or_dimensions_writes_neither()
    {
        var (html, _) = _harness.Block($$$"""{"id":"i","type":"image","props":{"media":{"mediaId":"{{{Photo}}}"}},"children":[]}""");

        Assert.DoesNotContain("srcset", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sizes", html, StringComparison.Ordinal);
        Assert.DoesNotContain("width=", html, StringComparison.Ordinal);
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
}
