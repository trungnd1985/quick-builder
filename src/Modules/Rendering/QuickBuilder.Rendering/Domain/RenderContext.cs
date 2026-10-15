using System.Text;
using System.Text.Json;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Rendering;

namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// One render in progress: the inputs, the design, and the per-node style rules collected so
/// far (012 research PR-09).
/// </summary>
/// <remarks>
/// Token-driven values are custom properties on the block, as on the canvas. The canvas puts
/// them in a <c>style</c> attribute; a published page collects them here into one
/// <c>&lt;style&gt;</c> element, <c>[data-qb="…"]{…}</c> per node, so the page's content
/// security policy can allow exactly that element by its hash and no inline style at all.
/// </remarks>
internal sealed class RenderContext(RenderInputs inputs, BlockDefaults defaults)
{
    private readonly StringBuilder _rules = new();

    // A card's blocks repeat once per item with the same rules: each key is written once.
    private readonly HashSet<string> _styled = new(StringComparer.Ordinal);

    private readonly HashSet<(string NodeId, string Reason)> _broken = [];

    public RenderInputs Inputs { get; } = inputs;

    /// <summary>
    /// The structured data nodes the blocks contribute as they render (017 research SE-18): a
    /// video embed's <c>VideoObject</c>, a collection list's <c>ItemList</c>.
    /// </summary>
    public List<System.Text.Json.Nodes.JsonObject> StructuredData { get; } = [];

    public BlockDefaults Defaults { get; } = defaults;

    public TokenCss Tokens { get; } = TokenCss.For(inputs.Design);

    /// <summary>What the page's bindings and lists read (016 research TB-09).</summary>
    public ResolvedData Data { get; } = inputs.Data ?? ResolvedData.Empty;

    /// <summary>The binding scopes around the node being rendered (016 FR-028).</summary>
    public ScopeStack Scopes { get; } = new((inputs.Data ?? ResolvedData.Empty).PageItem);

    /// <summary>
    /// Bindings found broken in this render, by node id and reason, for one warning per render
    /// (FR-038). Never values.
    /// </summary>
    public IReadOnlyCollection<(string NodeId, string Reason)> BrokenBindings => _broken;

    /// <summary>Renders a node's children; set by the page renderer.</summary>
    public Action<JsonElement, string, HtmlWriter> RenderChildren { get; set; } = (_, _, _) => { };

    /// <summary>Renders one node with its type's renderer; set by the page renderer (016: a list renders its card per item).</summary>
    public Action<JsonElement, string, HtmlWriter> RenderNode { get; set; } = (_, _, _) => { };

    /// <summary>
    /// What property <paramref name="prop"/> of <paramref name="node"/> shows when bound (016
    /// research TB-09); <see cref="BoundValue.Unbound"/> when it is not. A broken binding is
    /// recorded and shown as empty.
    /// </summary>
    public BoundValue Bound(JsonElement node, string prop)
    {
        var bound = BindingResolver.Resolve(node, prop, this);
        if (bound.Broken is { } reason)
        {
            _broken.Add((IdOf(node), reason));
        }

        return bound;
    }

    /// <summary>
    /// Whether any binding of <paramref name="node"/> leaves it out (FR-019): a bound property
    /// with nothing to show, and "hide" chosen for it.
    /// </summary>
    public bool IsHidden(JsonElement node)
    {
        if (!node.TryGetProperty("props", out var props) || !props.TryGetProperty("bind", out var bind) || bind.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var binding in bind.EnumerateObject())
        {
            if (Bound(node, binding.Name).State == BoundState.Hide)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A text property: its bound value, or else its own value or default.</summary>
    public string BoundText(JsonElement node, string prop) =>
        Bound(node, prop) is { State: BoundState.Value, Text: { } text } ? text : String(node, prop);

    /// <summary>
    /// The content width, in pixels, of the Section being rendered; null inside a full-width
    /// Section or outside any Section with its default width. The image renderer's
    /// <c>sizes</c> hint reads it (013 research MV-15).
    /// </summary>
    public double? SectionWidth { get; set; }

    private int _navigationBlocks;

    /// <summary>
    /// The next Navigation block's id within the page (<c>n1</c>, <c>n2</c>, …), so the submenu
    /// groups (<c>name</c>) of two blocks never join (024 public-navigation.md §4).
    /// </summary>
    public string NextNavigationId() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"n{++_navigationBlocks}");

    /// <summary>
    /// Whether the navigation script has been written into this page (024 research NV-14). The
    /// page's content security policy admits it only when it has.
    /// </summary>
    public bool NavigationScriptWritten { get; set; }

    /// <summary>The per-node rules collected so far, for the page's style element.</summary>
    public string NodeRules => _rules.ToString();

    /// <summary>A property of <paramref name="node"/>, or its schema default.</summary>
    public JsonElement? Prop(JsonElement node, string name)
    {
        if (node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object && props.TryGetProperty(name, out var value))
        {
            return value;
        }

        return Defaults.Of(TypeOf(node), name);
    }

    /// <summary>A string property or its default, or <paramref name="fallback"/>.</summary>
    public string String(JsonElement node, string name, string fallback = "") =>
        Prop(node, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : fallback;

    /// <summary>
    /// A property that is one of <paramref name="allowed"/>, or else its default, or else the first
    /// allowed value. Enum values become class names, so nothing else may through.
    /// </summary>
    public string Choice(JsonElement node, string name, params string[] allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        var value = String(node, name);
        if (Array.IndexOf(allowed, value) >= 0)
        {
            return value;
        }

        var fallback = Defaults.Of(TypeOf(node), name) is { ValueKind: JsonValueKind.String } d ? d.GetString()! : null;
        return fallback is not null && Array.IndexOf(allowed, fallback) >= 0 ? fallback : allowed[0];
    }

    /// <summary>A boolean property or its default.</summary>
    public bool Bool(JsonElement node, string name) =>
        Prop(node, name) is { ValueKind: JsonValueKind.True };

    /// <summary>A number property or its default, or <paramref name="fallback"/>.</summary>
    public int Int(JsonElement node, string name, int fallback) =>
        Prop(node, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : fallback;

    /// <summary>A token property as CSS, falling back to the property default's token (useBlock.token).</summary>
    public string Token(JsonElement node, string name, TokenKind kind, string fallback = "initial")
    {
        var own = node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object && props.TryGetProperty(name, out var value)
            ? value
            : (JsonElement?)null;

        return Tokens.Value(own, kind, Defaults.Of(TypeOf(node), name), fallback);
    }

    /// <summary>
    /// Records per-node declarations and writes the <c>data-qb</c> attribute that selects them.
    /// Values come from token variables, enums or constants; never from free text.
    /// </summary>
    public void Style(HtmlWriter writer, string scope, JsonElement node, params (string Property, string Value)[] declarations)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (declarations.Length == 0)
        {
            return;
        }

        var key = $"{scope}{IdOf(node)}";

        if (_styled.Add(key))
        {
            _rules.Append("[data-qb=\"").Append(key).Append("\"]{");
            foreach (var (property, value) in declarations)
            {
                _rules.Append(property).Append(':').Append(value).Append(';');
            }

            _rules.Append('}');
        }

        writer.Attribute("data-qb", key);
    }

    public static string TypeOf(JsonElement node) =>
        node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString()! : string.Empty;

    /// <summary>The node's id; the contract limits it to <c>[A-Za-z0-9_-]{1,64}</c>, safe in a selector.</summary>
    public static string IdOf(JsonElement node) =>
        node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : string.Empty;
}
