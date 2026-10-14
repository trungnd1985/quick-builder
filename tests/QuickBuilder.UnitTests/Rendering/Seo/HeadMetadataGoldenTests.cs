using System.Text.Json;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Rendering.Seo;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.UnitTests.Rendering.Seo;

/// <summary>
/// The head of a published page: tags, fallbacks and encoding (017 FR-003, FR-005, SC-007;
/// research SE-05; public-seo.md §3).
/// </summary>
/// <remarks>
/// Each case writes its head tags to <c>tests/fixtures/rendering/golden/head/</c>. Set
/// <c>QB_UPDATE_GOLDEN=1</c> to rewrite them, then review every changed file.
/// </remarks>
public sealed class HeadMetadataGoldenTests
{
    private static readonly Guid ImageId = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f");
    private static readonly Guid DefaultImageId = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10");

    public static TheoryData<string> Cases() => [.. GoldenCases.Keys.Order(StringComparer.Ordinal)];

    private static readonly Dictionary<string, Func<HeadInputs>> GoldenCases = new(StringComparer.Ordinal)
    {
        ["every-field"] = () => Inputs(
            seo: """{ "title": "Web design in Hanoi", "description": "Fast sites.", "ogTitle": "Design", "ogDescription": "Fast.", "ogImage": { "mediaId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" } }"""),
        ["fallbacks"] = () => Inputs(seo: null, site: Site(suffix: " | Acme", defaultImage: DefaultImageId)),
        ["item-page"] = () => Inputs(seo: null, title: "Hello world", path: "/blog/hello-world", isItemPage: true),
        ["hidden"] = () => Inputs(seo: """{ "noindex": true }"""),
        ["discouraged"] = () => Inputs(seo: null, site: Site(discouraged: true)),
        ["base-path"] = () => Inputs(seo: """{ "ogImage": { "mediaId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" } }""", site: Site(baseUrl: "https://client.example/cms")),
        ["home"] = () => Inputs(seo: null, path: "/"),
        ["hostile"] = () => Inputs(
            seo: """{ "title": "\"><script>x</script>", "description": "a & b < c \" d ' e" }""",
            site: Site(name: "Acme \"<b>\"", suffix: " | <i>")),
        ["locale-pt-br"] = () => Inputs(seo: null, site: Site(language: "pt-BR")),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_head_equals_its_golden_file(string name)
    {
        var writer = new HtmlWriter();
        HeadWriter.Write(writer, HeadBuilder.Build(GoldenCases[name]()));
        var html = writer.ToString();
        var golden = RepositoryFiles.Fixtures("golden", "head", $"{name}.html");

        if (RepositoryFiles.UpdateGolden)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, html);
            return;
        }

        Assert.True(File.Exists(golden), $"No golden file for {name}. Run the tests with QB_UPDATE_GOLDEN=1, then review it.");
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), html);
    }

    [Fact]
    public void Empty_fields_fall_back_in_the_order_of_FR_005()
    {
        var head = HeadBuilder.Build(Inputs(seo: null, site: Site(suffix: " | Acme", defaultImage: DefaultImageId)));

        Assert.Equal("Services | Acme", head.Title);
        Assert.Null(head.Description);
        Assert.Equal("Services | Acme", head.OgTitle);
        Assert.Null(head.OgDescription);
        Assert.Equal("https://client.example/_media/default.png", head.Image!.Url);
    }

    [Fact]
    public void A_meta_title_is_used_as_written_without_the_suffix()
    {
        var head = HeadBuilder.Build(Inputs(seo: """{ "title": "Web design in Hanoi" }""", site: Site(suffix: " | Acme")));

        Assert.Equal("Web design in Hanoi", head.Title);
    }

    [Fact]
    public void The_sharing_description_falls_back_to_the_meta_description()
    {
        var head = HeadBuilder.Build(Inputs(seo: """{ "description": "Fast sites." }"""));

        Assert.Equal("Fast sites.", head.OgDescription);
    }

    [Fact]
    public void A_deleted_sharing_image_writes_no_image()
    {
        var head = HeadBuilder.Build(Inputs(seo: """{ "ogImage": { "mediaId": "0192f5a0-0000-7000-8000-00000000dead" } }"""));

        Assert.Null(head.Image);
    }

    [Fact]
    public void The_image_is_the_largest_variant_up_to_1200_pixels_wide_with_its_height_scaled()
    {
        var head = HeadBuilder.Build(Inputs(seo: """{ "ogImage": { "mediaId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" } }"""));

        Assert.Equal("https://client.example/_media/photo-1200.jpg", head.Image!.Url);
        Assert.Equal((1200, 800), (head.Image.Width, head.Image.Height));
        Assert.Equal("A team at work", head.Image.Alt);
    }

    [Fact]
    public void An_image_without_variants_is_its_original()
    {
        var media = new Dictionary<Guid, PublicMediaFile>
        {
            [ImageId] = new("/_media/logo.svg", "image/svg+xml", null, null, []),
        };

        var head = HeadBuilder.Build(Inputs(seo: """{ "ogImage": { "mediaId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" } }""", media: media));

        Assert.Equal("https://client.example/_media/logo.svg", head.Image!.Url);
        Assert.Null(head.Image.Width);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("vi", "vi")]
    [InlineData("pt-BR", "pt_BR")]
    [InlineData("zh-Hant", "zh")]
    [InlineData("sr-Latn-RS", "sr_RS")]
    public void The_locale_follows_the_default_language(string language, string locale)
    {
        Assert.Equal(locale, OgLocale.From(language));
    }

    [Fact]
    public void Hiding_a_page_asks_not_to_index_and_discouraging_also_not_to_follow()
    {
        Assert.Equal("noindex", HeadBuilder.Build(Inputs(seo: """{ "noindex": true }""")).Robots);
        Assert.Equal("noindex, nofollow", HeadBuilder.Build(Inputs(seo: """{ "noindex": true }""", site: Site(discouraged: true))).Robots);
        Assert.Null(HeadBuilder.Build(Inputs(seo: """{ "noSitemap": true }""")).Robots);
    }

    [Fact]
    public void Item_pages_are_articles_and_other_pages_websites()
    {
        Assert.Equal("article", HeadBuilder.Build(Inputs(seo: null, isItemPage: true)).OgType);
        Assert.Equal("website", HeadBuilder.Build(Inputs(seo: null)).OgType);
    }

    [Fact]
    public void The_canonical_address_is_absolute_on_the_primary_host_name()
    {
        Assert.Equal("https://client.example/services", HeadBuilder.Build(Inputs(seo: null)).Canonical);
        Assert.Equal("https://client.example/", HeadBuilder.Build(Inputs(seo: null, path: "/")).Canonical);
        Assert.Equal("https://client.example/cms/services", HeadBuilder.Build(Inputs(seo: null, site: Site(baseUrl: "https://client.example/cms"))).Canonical);
        Assert.Equal("https://client.example/cms/", HeadBuilder.Build(Inputs(seo: null, path: "/", site: Site(baseUrl: "https://client.example/cms"))).Canonical);
    }

    [Fact]
    public void A_bound_field_takes_its_value_then_its_fallback_text_then_the_default()
    {
        const string seo = """{ "title": "Own", "bind": { "title": { "value": { "field": "f1" } }, "description": { "value": { "field": "f2" } }, "ogTitle": { "value": { "field": "f3" } } }, "fallback": { "description": "  A   post  " } }""";
        var bound = new Dictionary<string, BoundValue>(StringComparer.Ordinal)
        {
            ["title"] = new(BoundState.Value, Text: "Hello world"),
            ["description"] = new(BoundState.Fallback, Broken: "field_missing"),
            ["ogTitle"] = new(BoundState.Fallback),
        };

        var head = HeadBuilder.Build(Inputs(seo, site: Site(suffix: " | Acme"), title: "Hello world", isItemPage: true, bound: bound));

        Assert.Equal("Hello world", head.Title);
        Assert.Equal("A post", head.Description);
        Assert.Equal("Hello world", head.OgTitle);
    }

    [Fact]
    public void An_empty_bound_title_without_fallback_text_is_the_item_name_with_the_suffix()
    {
        const string seo = """{ "bind": { "title": { "value": { "field": "f1" } } } }""";
        var bound = new Dictionary<string, BoundValue>(StringComparer.Ordinal) { ["title"] = new(BoundState.Fallback) };

        var head = HeadBuilder.Build(Inputs(seo, site: Site(suffix: " | Acme"), title: "Hello world", isItemPage: true, bound: bound));

        Assert.Equal("Hello world | Acme", head.Title);
    }

    [Fact]
    public void A_bound_image_and_an_on_hide_switch_apply_to_the_item_page()
    {
        const string seo = """{ "bind": { "ogImage": { "value": { "field": "f10" } }, "noindex": { "value": { "field": "f4" } } } }""";
        var bound = new Dictionary<string, BoundValue>(StringComparer.Ordinal)
        {
            ["ogImage"] = new(BoundState.Value, MediaId: ImageId),
            ["noindex"] = new(BoundState.Value, Flag: true),
        };

        var head = HeadBuilder.Build(Inputs(seo, site: Site(defaultImage: DefaultImageId), isItemPage: true, bound: bound));

        Assert.Equal("https://client.example/_media/photo-1200.jpg", head.Image!.Url);
        Assert.Equal("noindex", head.Robots);
    }

    [Fact]
    public void Without_resolved_bindings_a_page_keeps_its_own_metadata()
    {
        const string seo = """{ "title": "Own", "bind": { "title": { "value": { "field": "f1" } }, "noindex": { "value": { "field": "f4" } } } }""";

        var head = HeadBuilder.Build(Inputs(seo));

        Assert.Equal("Own", head.Title);
        Assert.Null(head.Robots);
    }

    private static HeadInputs Inputs(
        string? seo,
        SiteDto? site = null,
        string title = "Services",
        string path = "/services",
        bool isItemPage = false,
        IReadOnlyDictionary<Guid, PublicMediaFile>? media = null,
        IReadOnlyDictionary<string, BoundValue>? bound = null) =>
        new(
            title,
            path,
            isItemPage,
            seo is null ? null : SeoDocument.From(Document(seo)),
            site ?? Site(),
            media ?? Media(),
            bound);

    private static Dictionary<Guid, PublicMediaFile> Media() => new()
    {
        [ImageId] = new(
            "/_media/photo.jpg",
            "image/jpeg",
            2400,
            1600,
            [new(640, "/_media/photo-640.jpg"), new(1200, "/_media/photo-1200.jpg"), new(1600, "/_media/photo-1600.jpg")],
            AltText: "A team at work"),
        [DefaultImageId] = new("/_media/default.png", "image/png", 1200, 630, []),
    };

    private static SiteDto Site(
        string name = "Acme",
        string? suffix = null,
        Guid? defaultImage = null,
        bool discouraged = false,
        string baseUrl = "https://client.example/",
        string language = "en") =>
        new(Guid.Parse("0192f5a0-0000-7000-8000-0000000000aa"), name, ["client.example"], DateTimeOffset.UnixEpoch)
        {
            PrimaryHostName = "client.example",
            Scheme = "https",
            BaseUrl = baseUrl,
            DefaultLanguage = language,
            Status = "enabled",
            Version = 1,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            Search = new SiteSearchSettings(suffix, defaultImage, null, null, discouraged, null),
        };

    private static JsonElement Document(string seo)
    {
        using var parsed = JsonDocument.Parse(
            $$"""{ "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": { "seo": {{seo}} }, "children": [] } }""");
        return parsed.RootElement.Clone();
    }
}
