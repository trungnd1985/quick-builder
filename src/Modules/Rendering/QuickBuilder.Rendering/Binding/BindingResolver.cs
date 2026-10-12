using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering;

namespace QuickBuilder.Rendering.Binding;

/// <summary>What a bound property shows (data-model §3.3).</summary>
internal enum BoundState
{
    /// <summary>The property is not bound: the block shows its own value.</summary>
    Unbound,

    /// <summary>The binding gave a value.</summary>
    Value,

    /// <summary>The binding gave nothing, and the block shows its own value instead.</summary>
    Fallback,

    /// <summary>The binding gave nothing, and the whole block is left out.</summary>
    Hide,
}

/// <summary>A bound property's outcome, with the value in the form its kind takes.</summary>
/// <param name="Broken">Why the binding could not be read, when it was broken rather than empty.</param>
internal sealed record BoundValue(
    BoundState State,
    string? Text = null,
    JsonElement? RichText = null,
    Guid? MediaId = null,
    ResolvedLink? Link = null,
    string? Broken = null)
{
    public static BoundValue Unbound { get; } = new(BoundState.Unbound);
}

/// <summary>
/// Resolves a block property's binding against the scopes around it (016 research TB-09,
/// binding-expression.md §2–§4).
/// </summary>
/// <remarks>
/// Pure: it reads only the render's <see cref="ResolvedData"/>, media and scopes, so it is the
/// same code in every render and in the golden tests. A binding that cannot be read — its
/// scope, field or reference gone, or its type no longer fitting — is <em>broken</em>, and is
/// shown exactly as an empty value is: never an error (FR-018, FR-038).
/// </remarks>
internal static partial class BindingResolver
{
    /// <summary>The outcome of property <paramref name="prop"/> of <paramref name="node"/>.</summary>
    public static BoundValue Resolve(JsonElement node, string prop, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!node.TryGetProperty("props", out var props)
            || !props.TryGetProperty("bind", out var bind)
            || !bind.TryGetProperty(prop, out var binding)
            || !binding.TryGetProperty("value", out var value)
            || BindingRules.KindOf(RenderContext.TypeOf(node), prop) is not { } kind)
        {
            return BoundValue.Unbound;
        }

        var resolved = value.TryGetProperty("parts", out var parts)
            ? Template(parts, node, context)
            : One(value, kind, node, context);

        if (resolved.State == BoundState.Value)
        {
            return resolved;
        }

        var hide = binding.TryGetProperty("empty", out var empty) && empty.GetString() == "hide";
        return new BoundValue(hide ? BoundState.Hide : BoundState.Fallback, Broken: resolved.Broken);
    }

    /// <summary>A text template: fixed text and values, where an empty value adds nothing (FR-015).</summary>
    private static BoundValue Template(JsonElement parts, JsonElement node, RenderContext context)
    {
        var text = new System.Text.StringBuilder();
        var anyValue = false;
        string? broken = null;

        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                text.Append(part.GetString());
                continue;
            }

            var resolved = One(part, PropKind.Text, node, context);
            if (resolved.State == BoundState.Value)
            {
                text.Append(resolved.Text);
                anyValue = true;
            }
            else
            {
                broken ??= resolved.Broken;
            }
        }

        return anyValue
            ? new BoundValue(BoundState.Value, Text: text.ToString())
            : new BoundValue(BoundState.Fallback, Broken: broken);
    }

    /// <summary>One value: a field or built-in value of a scope's item, through one reference at most.</summary>
    private static BoundValue One(JsonElement value, PropKind kind, JsonElement node, RenderContext context)
    {
        var scope = value.TryGetProperty("scope", out var named) ? named.GetString() : null;
        if (context.Scopes.Find(scope) is not { } item)
        {
            return Broken(BindingRules.ScopeMissing);
        }

        var data = context.Data;

        if (value.TryGetProperty("via", out var via))
        {
            if (!data.Schemas.TryGetValue(item.CollectionId, out var schema) || schema.Field(via.GetString()) is not { } reference)
            {
                return Broken(BindingRules.FieldMissing);
            }

            if (reference.Type != FieldInfo.Reference)
            {
                return Broken(BindingRules.ViaNotReference);
            }

            // A referenced item without published data reads as empty, not broken (FR-019).
            if (ReadValue(item, reference.Id) is not { ValueKind: JsonValueKind.String } target
                || !Guid.TryParse(target.GetString(), out var targetId)
                || !data.Items.TryGetValue(targetId, out var referenced))
            {
                return Empty;
            }

            item = referenced;
        }

        var format = value.TryGetProperty("format", out var f) ? f : (JsonElement?)null;

        if (value.TryGetProperty("item", out var builtIn))
        {
            return BuiltIn(builtIn.GetString()!, kind, item, format, context);
        }

        if (!data.Schemas.TryGetValue(item.CollectionId, out var itemSchema)
            || itemSchema.Field(value.TryGetProperty("field", out var fieldId) ? fieldId.GetString() : null) is not { } field)
        {
            return Broken(BindingRules.FieldMissing);
        }

        if (!BindingRules.Fits(kind, field))
        {
            return Broken(BindingRules.TypeMismatch);
        }

        if (ReadValue(item, field.Id) is not { } stored)
        {
            // No key means no value (collection-schema.md §3); a boolean never set reads as false.
            return field.Type == FieldInfo.Boolean && kind == PropKind.Text
                ? Text(BooleanText(false, format))
                : Empty;
        }

        return kind switch
        {
            PropKind.Text => Text(FieldText(field, stored, format, RenderContext.TypeOf(node), context)),
            PropKind.RichText => RichTextOf(field, stored),
            PropKind.Image => MediaOf(stored, context),
            PropKind.Link => LinkOf(field, stored, context),
            _ => Empty,
        };
    }

    private static BoundValue BuiltIn(string name, PropKind kind, PublishedItem item, JsonElement? format, RenderContext context)
    {
        if (!BindingRules.Fits(kind, name))
        {
            return Broken(BindingRules.TypeMismatch);
        }

        var language = context.Inputs.Language;
        var style = DateStyle(format);

        return name switch
        {
            BuiltIns.Slug => Text(item.Slug),
            BuiltIns.FirstPublishedAt => Text(ValueFormatter.Date(item.FirstPublishedAtUtc, style, language)),
            BuiltIns.LastPublishedAt => Text(ValueFormatter.Date(item.LastPublishedAtUtc, style, language)),
            BuiltIns.Url => ItemLink(item, context),
            _ => Broken(BindingRules.TypeMismatch),
        };
    }

    /// <summary>The address of an item's page, when its collection has a live template (FR-030).</summary>
    public static BoundValue ItemLink(PublishedItem item, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Data.TemplatePatterns.TryGetValue(item.CollectionId, out var pattern))
        {
            return Empty;
        }

        var href = PageShell.TrimBase(context.Inputs.BasePath)
            + pattern.Replace(Pages.Contracts.TemplateInfo.SlugPlaceholder, item.Slug, StringComparison.Ordinal);
        return HtmlWriter.IsSitePath(href)
            ? new BoundValue(BoundState.Value, Link: new ResolvedLink(href, External: false))
            : Empty;
    }

    /// <summary>A field's value as text (binding-expression.md §4).</summary>
    private static string? FieldText(FieldInfo field, JsonElement stored, JsonElement? format, string blockType, RenderContext context)
    {
        var language = context.Inputs.Language;

        switch (field.Type)
        {
            case FieldInfo.Text when stored.ValueKind == JsonValueKind.String:
                var text = stored.GetString()!;
                // A heading is one line: a multi-line value reads with its breaks as spaces.
                return blockType == "heading" ? LineBreaks().Replace(text, " ") : text;

            case FieldInfo.Number when stored.ValueKind == JsonValueKind.Number && stored.TryGetDecimal(out var number):
                var decimals = format is { } n && n.TryGetProperty("number", out var numberFormat)
                    && numberFormat.TryGetProperty("decimals", out var places) && places.TryGetInt32(out var p)
                    ? p
                    : field.Decimals;
                return ValueFormatter.Number(number, decimals, language);

            case FieldInfo.Date when stored.ValueKind == JsonValueKind.String:
                return ValueFormatter.Date(stored.GetString()!, DateStyle(format), language);

            case FieldInfo.Option when stored.ValueKind == JsonValueKind.String:
                return field.Options.TryGetValue(stored.GetString()!, out var label) ? label : null;

            case FieldInfo.Boolean:
                return BooleanText(stored.ValueKind == JsonValueKind.True, format);

            case FieldInfo.Reference when stored.ValueKind == JsonValueKind.String:
                return Guid.TryParse(stored.GetString(), out var id) && context.Data.Items.TryGetValue(id, out var referenced)
                    ? referenced.Name
                    : null;

            default:
                return null;
        }
    }

    /// <summary>A boolean reads as the yes or no text the binding sets; without them it reads as nothing.</summary>
    private static string? BooleanText(bool value, JsonElement? format) =>
        format is { } f && f.TryGetProperty(value ? "yes" : "no", out var text) ? text.GetString() : null;

    private static BoundValue RichTextOf(FieldInfo field, JsonElement stored)
    {
        if (field.Type == FieldInfo.RichText)
        {
            return HasContent(stored) ? new BoundValue(BoundState.Value, RichText: stored) : Empty;
        }

        // Multi-line text: one paragraph per line, an empty line an empty paragraph.
        if (stored.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(stored.GetString()))
        {
            return Empty;
        }

        var paragraphs = new JsonArray();
        foreach (var line in stored.GetString()!.ReplaceLineEndings("\n").Split('\n'))
        {
            var paragraph = new JsonObject { ["type"] = "paragraph" };
            if (line.Length > 0)
            {
                paragraph["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = line });
            }

            paragraphs.Add(paragraph);
        }

        var document = new JsonObject { ["type"] = "doc", ["content"] = paragraphs };
        return new BoundValue(BoundState.Value, RichText: JsonSerializer.SerializeToElement(document));
    }

    private static BoundValue MediaOf(JsonElement stored, RenderContext context) =>
        MediaId(stored) is { } id && context.Inputs.Media.ContainsKey(id)
            ? new BoundValue(BoundState.Value, MediaId: id)
            : Empty;

    private static BoundValue LinkOf(FieldInfo field, JsonElement stored, RenderContext context)
    {
        if (field.Type == FieldInfo.File)
        {
            if (MediaId(stored) is not { } id || !context.Inputs.Media.TryGetValue(id, out var file))
            {
                return Empty;
            }

            var href = PageShell.TrimBase(context.Inputs.BasePath) + file.PublicPath;
            return HtmlWriter.IsSitePath(href) ? new BoundValue(BoundState.Value, Link: new ResolvedLink(href, External: false)) : Empty;
        }

        // A text field holding an address: only the schemes links may use (011), else nothing.
        return stored.ValueKind == JsonValueKind.String
               && stored.GetString() is { Length: > 0 and <= 2048 } address
               && AllowedAddress().IsMatch(address)
            ? new BoundValue(BoundState.Value, Link: new ResolvedLink(address, External: true))
            : Empty;
    }

    private static Guid? MediaId(JsonElement stored) =>
        stored.ValueKind == JsonValueKind.Object
        && stored.TryGetProperty("mediaId", out var id)
        && Guid.TryParse(id.GetString(), out var mediaId)
            ? mediaId
            : null;

    /// <summary>The value stored under <paramref name="fieldId"/> in an item's published values, if any.</summary>
    public static JsonElement? ReadValue(PublishedItem item, string fieldId)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Values.ValueKind == JsonValueKind.Object
               && item.Values.TryGetProperty("values", out var values)
               && values.ValueKind == JsonValueKind.Object
               && values.TryGetProperty(fieldId, out var value)
            ? value
            : null;
    }

    /// <summary>Whether a rich text holds any text or image (035: one with neither is no value).</summary>
    private static bool HasContent(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object
        && ((node.TryGetProperty("type", out var type) && type.GetString() is "text" or "image")
            || (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(HasContent)));

    private static string? DateStyle(JsonElement? format) =>
        format is { } f && f.TryGetProperty("date", out var style) ? style.GetString() : null;

    private static BoundValue Text(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Empty : new BoundValue(BoundState.Value, Text: text);

    private static BoundValue Empty { get; } = new(BoundState.Fallback);

    private static BoundValue Broken(string reason) => new(BoundState.Fallback, Broken: reason);

    // The page schema's link pattern for an address (page-schema.md §2a).
    [GeneratedRegex("^(https?://[^\\u0000-\\u0020\\u007f]+|mailto:[^\\u0000-\\u0020\\u007f]+|tel:[+0-9 ().-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedAddress();

    [GeneratedRegex("\\r\\n|\\r|\\n")]
    private static partial Regex LineBreaks();
}
