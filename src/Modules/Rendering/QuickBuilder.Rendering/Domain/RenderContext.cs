using System.Text;
using System.Text.Json;
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

    public RenderInputs Inputs { get; } = inputs;

    public BlockDefaults Defaults { get; } = defaults;

    public TokenCss Tokens { get; } = TokenCss.For(inputs.Design);

    /// <summary>Renders a node's children; set by the page renderer.</summary>
    public Action<JsonElement, string, HtmlWriter> RenderChildren { get; set; } = (_, _, _) => { };

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

        _rules.Append("[data-qb=\"").Append(key).Append("\"]{");
        foreach (var (property, value) in declarations)
        {
            _rules.Append(property).Append(':').Append(value).Append(';');
        }

        _rules.Append('}');

        writer.Attribute("data-qb", key);
    }

    public static string TypeOf(JsonElement node) =>
        node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString()! : string.Empty;

    /// <summary>The node's id; the contract limits it to <c>[A-Za-z0-9_-]{1,64}</c>, safe in a selector.</summary>
    public static string IdOf(JsonElement node) =>
        node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : string.Empty;
}
