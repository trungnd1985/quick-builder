using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Seo.StructuredData;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.UnitTests.Rendering.Binding;

namespace QuickBuilder.UnitTests.Rendering.Seo;

/// <summary>
/// The structured data an item page and its blocks describe themselves with, against the
/// contract's golden examples (017 FR-034 to FR-036; research SE-18, SE-19; structured-data.md §6).
/// </summary>
public sealed class StructuredDataGoldenTests
{
    private static string Examples(string kind) => RepositoryFiles.Path("docs", "contracts", "structured-data", "examples", kind);

    public static TheoryData<string> RenderCases() => [.. Directory.GetFiles(Examples("render"), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal)];

    public static TheoryData<string> MapCases() => [.. Directory.GetFiles(Examples("map"), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal)];

    private static JsonElement Load(string kind, string name)
    {
        using var parsed = JsonDocument.Parse(File.ReadAllText(Path.Combine(Examples(kind), $"{name}.json")));
        return parsed.RootElement.Clone();
    }

    private static Dictionary<Guid, CollectionSchemaInfo> Schemas(JsonElement root) =>
        root.GetProperty("schemas").EnumerateObject().ToDictionary(s => Guid.Parse(s.Name), s => CollectionSchemaInfo.Parse(Guid.Parse(s.Name), s.Value));

    private static Dictionary<string, JsonElement> Bindings(JsonElement mapping) =>
        mapping.TryGetProperty("bind", out var bind)
            ? bind.EnumerateObject().ToDictionary(b => b.Name, b => b.Value.Clone(), StringComparer.Ordinal)
            : [];

    [Theory]
    [MemberData(nameof(RenderCases))]
    public void An_item_page_describes_itself_as_its_golden_node(string name)
    {
        var root = Load("render", name);
        var item = ResolveCase.Item(root.GetProperty("item"));
        var data = new ResolvedData
        {
            PageItem = item,
            Items = root.GetProperty("items").EnumerateArray().Select(ResolveCase.Item).ToDictionary(i => i.Id),
            Schemas = Schemas(root),
        };
        var media = root.GetProperty("media").EnumerateObject().ToDictionary(
            m => Guid.Parse(m.Name), m => new PublicMediaFile(m.Value.GetString()!, "image/jpeg", null, null, []));
        var mapping = root.GetProperty("structuredData");

        var node = ItemNode.Build(
            mapping.GetProperty("type").GetString(),
            Bindings(mapping),
            new ItemNodeInputs(item, data, media, root.GetProperty("baseUrl").GetString()!, root.GetProperty("language").GetString()!, root.GetProperty("url").GetString()!));

        var expected = root.GetProperty("expected");
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(node);
            return;
        }

        Assert.NotNull(node);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), node), $"{name}: got {node!.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(MapCases))]
    public void A_mapping_marks_exactly_its_golden_broken_bindings(string name)
    {
        var root = Load("map", name);
        var schemas = Schemas(root);
        var mapping = root.GetProperty("structuredData");

        var broken = ItemNode.Broken(
            mapping.GetProperty("type").GetString(),
            Bindings(mapping),
            schemas[Guid.Parse(root.GetProperty("collectionId").GetString()!)],
            new ResolvedData { Schemas = schemas });

        var expected = root.GetProperty("broken").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        Assert.Equal(expected.OrderBy(e => e.Key, StringComparer.Ordinal), broken.OrderBy(e => e.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Breadcrumbs_lead_from_the_home_page_to_the_page_whose_crumb_has_no_link()
    {
        var crumbs = PageNodes.Breadcrumbs(
            Site(),
            [new LiveAncestor(Guid.NewGuid(), "/", "Home", IsHome: true), new LiveAncestor(Guid.NewGuid(), "/blog", "Blog", IsHome: false)],
            "Hello world");

        Assert.Equal(
            """{"@type":"BreadcrumbList","itemListElement":[{"@type":"ListItem","position":1,"name":"Home","item":"https://client.example/"},{"@type":"ListItem","position":2,"name":"Blog","item":"https://client.example/blog"},{"@type":"ListItem","position":3,"name":"Hello world"}]}""",
            crumbs.ToJsonString());
    }

    [Fact]
    public void Without_a_live_home_page_the_trail_starts_with_the_site_name_unlinked()
    {
        var crumbs = PageNodes.Breadcrumbs(Site(), [], "About");

        Assert.Equal(
            """{"@type":"BreadcrumbList","itemListElement":[{"@type":"ListItem","position":1,"name":"Acme"},{"@type":"ListItem","position":2,"name":"About"}]}""",
            crumbs.ToJsonString());
    }

    [Fact]
    public void The_home_page_describes_the_website_and_the_organisation_when_named()
    {
        Assert.Equal(
            ["""{"@type":"WebSite","name":"Acme","url":"https://client.example/"}"""],
            PageNodes.Home(Site(), new Dictionary<Guid, PublicMediaFile>()).Select(n => n.ToJsonString()));

        var logo = Guid.NewGuid();
        var nodes = PageNodes.Home(Site("Acme Ltd", logo), new Dictionary<Guid, PublicMediaFile> { [logo] = new("/_media/logo.png", "image/png", 200, 200, []) }).ToList();
        Assert.Equal("""{"@type":"Organization","name":"Acme Ltd","url":"https://client.example/","logo":"https://client.example/_media/logo.png"}""", nodes[1].ToJsonString());
    }

    private static SiteDto Site(string? organisation = null, Guid? logo = null) =>
        new(Guid.Parse("0192f5a0-0000-7000-8000-0000000000aa"), "Acme", ["client.example"], DateTimeOffset.UnixEpoch)
        {
            PrimaryHostName = "client.example",
            Scheme = "https",
            BaseUrl = "https://client.example/",
            DefaultLanguage = "en",
            Status = "enabled",
            Version = 1,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            Search = new SiteSearchSettings(null, null, organisation, logo, false, null),
        };
}
