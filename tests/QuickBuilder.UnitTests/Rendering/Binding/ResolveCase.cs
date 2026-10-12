using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.UnitTests.Rendering.Binding;

/// <summary>
/// One of the binding contract's resolve cases (docs/contracts/binding-expression/examples/resolve/,
/// described in <c>_world.md</c>), read into the render inputs it describes.
/// </summary>
internal sealed class ResolveCase
{
    private ResolveCase(string name, JsonElement root) => (Name, Root) = (name, root);

    public string Name { get; }

    public JsonElement Root { get; }

    public JsonElement Document => Root.GetProperty("document");

    public static string Directory => RepositoryFiles.Path("docs", "contracts", "binding-expression", "examples", "resolve");

    public static IEnumerable<string> Names() =>
        System.IO.Directory.GetFiles(Directory, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal);

    public static ResolveCase Load(string name)
    {
        using var parsed = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, $"{name}.json")));
        return new ResolveCase(name, parsed.RootElement.Clone());
    }

    /// <summary>The render inputs the case describes, with the page's item, items, schemas, templates and media.</summary>
    public RenderInputs Inputs(IReadOnlyDictionary<string, PublishedItemPage>? lists = null)
    {
        var media = Root.GetProperty("media").EnumerateObject().ToDictionary(
            m => Guid.Parse(m.Name),
            m => new PublicMediaFile(m.Value.GetString()!, "application/octet-stream", null, null, []));

        var data = new ResolvedData
        {
            PageItem = Root.GetProperty("pageItem") is { ValueKind: JsonValueKind.Object } page ? Item(page) : null,
            Items = Root.GetProperty("items").EnumerateArray().Select(Item).ToDictionary(i => i.Id),
            Schemas = Root.GetProperty("schemas").EnumerateObject().ToDictionary(
                s => Guid.Parse(s.Name), s => CollectionSchemaInfo.Parse(Guid.Parse(s.Name), s.Value)),
            TemplatePatterns = Root.GetProperty("templates").EnumerateObject().ToDictionary(t => Guid.Parse(t.Name), t => t.Value.GetString()!),
            Lists = lists ?? new Dictionary<string, PublishedItemPage>(StringComparer.Ordinal),
        };

        return new RenderInputs(
            "Case", Document, DefaultDesignTokens.Document, new Dictionary<Guid, LiveDocument>(), new Dictionary<Guid, PageLinkTarget>(),
            media, Root.GetProperty("language").GetString()!, Root.GetProperty("basePath").GetString()!, data);
    }

    /// <summary>A render context with the case's list scopes open, as inside the cards it names.</summary>
    public RenderContext Context()
    {
        var context = new RenderContext(Inputs(), BlockDefaults.Current);
        var items = context.Data.Items;
        foreach (var scope in Root.GetProperty("scopes").EnumerateArray())
        {
            context.Scopes.Push(scope.GetProperty("name").GetString()!, items[Guid.Parse(scope.GetProperty("item").GetString()!)]);
        }

        return context;
    }

    /// <summary>The expected outcomes, by <c>nodeId.prop</c>.</summary>
    public IEnumerable<(string NodeId, string Prop, JsonElement Outcome)> Expected() =>
        Root.GetProperty("expected").EnumerateObject().Select(e =>
        {
            var dot = e.Name.LastIndexOf('.');
            return (e.Name[..dot], e.Name[(dot + 1)..], e.Value);
        });

    /// <summary>The node with <paramref name="id"/> in the case's document.</summary>
    public JsonElement Node(string id) => Find(Document.GetProperty("root"), id)
        ?? throw new InvalidOperationException($"{Name}: no node '{id}'.");

    public static PublishedItem Item(JsonElement item) => new(
        Guid.Parse(item.GetProperty("id").GetString()!),
        Guid.Parse(item.GetProperty("collectionId").GetString()!),
        item.GetProperty("slug").GetString()!,
        item.GetProperty("name").GetString(),
        DateTimeOffset.Parse(item.GetProperty("firstPublishedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(item.GetProperty("lastPublishedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
        item.GetProperty("values").Clone());

    private static JsonElement? Find(JsonElement node, string id)
    {
        if (node.GetProperty("id").GetString() == id)
        {
            return node;
        }

        foreach (var child in node.GetProperty("children").EnumerateArray())
        {
            if (Find(child, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
