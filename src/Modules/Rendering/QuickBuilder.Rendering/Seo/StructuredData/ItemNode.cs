using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Rendering.Binding;

namespace QuickBuilder.Rendering.Seo.StructuredData;

/// <summary>Everything mapping an item page's content type reads.</summary>
/// <param name="Item">The page's item.</param>
/// <param name="Url">The item page's canonical address.</param>
internal sealed record ItemNodeInputs(
    PublishedItem Item,
    ResolvedData Data,
    IReadOnlyDictionary<Guid, PublicMediaFile> Media,
    string BaseUrl,
    string Language,
    string Url);

/// <summary>
/// An item page's <c>Article</c>, <c>Product</c> or <c>Event</c> node, from its template's
/// <c>seo.structuredData</c> (017 FR-035; research SE-19; structured-data.md §2, §3).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each property reads its binding from the page's item, through one reference at most,
/// and only from what <c>types.json</c> says it accepts. Anything else is broken and left out,
/// never an error (binding-expression.md §4).</item>
/// <item>An empty or broken property takes its default, else it is left out. A required property
/// left empty leaves out the whole node.</item>
/// <item>Text is one line, rich text as plain text; dates are ISO 8601; images are absolute
/// addresses of the largest variant up to 1200 pixels.</item>
/// </list>
/// Pure, so the golden cases pin it down.
/// </remarks>
internal static partial class ItemNode
{
    /// <summary>Properties written as ISO dates, a number, a switch or an image; every other one is text.</summary>
    private static readonly HashSet<string> DateProperties = new(StringComparer.Ordinal) { "datePublished", "dateModified", "startDate", "endDate" };

    public static JsonObject? Build(string? typeName, IReadOnlyDictionary<string, JsonElement> bindings, ItemNodeInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(inputs);

        if (typeName is null || !StructuredDataTypes.All.TryGetValue(typeName, out var type))
        {
            return null;
        }

        var values = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var property in type.Properties)
        {
            var value = (bindings.TryGetValue(property.Name, out var binding) ? Read(binding, property, inputs) : null)
                ?? Default(property, inputs);
            if (value is not null)
            {
                values[property.Name] = value;
            }
            else if (property.Required)
            {
                return null;
            }
        }

        var node = new JsonObject { ["@type"] = type.Name };
        switch (type.Name)
        {
            case "Article":
                Copy(values, node, "headline", "description", "image");
                if (values.TryGetValue("authorName", out var author))
                {
                    node["author"] = new JsonObject { ["@type"] = "Person", ["name"] = author.DeepClone() };
                }

                Copy(values, node, "datePublished", "dateModified");
                break;

            case "Product":
                Copy(values, node, "name", "description", "image");
                if (values.TryGetValue("brand", out var brand))
                {
                    node["brand"] = new JsonObject { ["@type"] = "Brand", ["name"] = brand.DeepClone() };
                }

                Copy(values, node, "sku");
                if (values.TryGetValue("price", out var price)
                    && values.TryGetValue("priceCurrency", out var currency) && Currency().IsMatch(currency.GetValue<string>()))
                {
                    var offer = new JsonObject { ["@type"] = "Offer", ["price"] = price.DeepClone(), ["priceCurrency"] = currency.DeepClone() };
                    Copy(values, offer, "availability");
                    node["offers"] = offer;
                }

                break;

            case "Event":
                Copy(values, node, "name", "description", "image", "startDate", "endDate");
                if (values.ContainsKey("locationName") || values.ContainsKey("locationAddress"))
                {
                    var place = new JsonObject { ["@type"] = "Place" };
                    if (values.TryGetValue("locationName", out var placeName))
                    {
                        place["name"] = placeName.DeepClone();
                    }

                    if (values.TryGetValue("locationAddress", out var address))
                    {
                        place["address"] = address.DeepClone();
                    }

                    node["location"] = place;
                }

                break;

            default:
                // A type added to the contract without its shape here writes its properties as named.
                foreach (var (name, value) in values)
                {
                    node[name] = value.DeepClone();
                }

                break;
        }

        node["url"] = inputs.Url;
        return node;
    }

    /// <summary>
    /// Why each bound property of a mapping is broken, against the page's collection schema:
    /// <c>unknown_property</c>, or one of the binding reasons (binding-expression.md §4).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Broken(string? typeName, IReadOnlyDictionary<string, JsonElement> bindings, CollectionSchemaInfo? schema, ResolvedData data)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(data);

        var broken = new Dictionary<string, string>(StringComparer.Ordinal);
        StructuredDataTypes.All.TryGetValue(typeName ?? string.Empty, out var type);
        foreach (var (name, binding) in bindings)
        {
            if (type?.Property(name) is not { } property)
            {
                broken[name] = "unknown_property";
                continue;
            }

            if (!binding.TryGetProperty("value", out var value))
            {
                continue;
            }

            var readers = value.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array
                ? property.Accepts.Contains("template") ? parts.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object).ToList() : null
                : [value];
            if (readers is null)
            {
                broken[name] = BindingRules.TypeMismatch;
                continue;
            }

            foreach (var reader in readers)
            {
                if (ReaderBroken(reader, readers.Count > 1 || value.TryGetProperty("parts", out _) ? TextAccepts : property.Accepts, schema, data) is { } reason)
                {
                    broken[name] = reason;
                    break;
                }
            }
        }

        return broken;
    }

    private static readonly System.Collections.Frozen.FrozenSet<string> TextAccepts =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(["text", "option", "number", "date", "reference", "slug"], StringComparer.Ordinal);

    private static string? ReaderBroken(JsonElement reader, System.Collections.Frozen.FrozenSet<string> accepts, CollectionSchemaInfo? schema, ResolvedData data)
    {
        if (reader.TryGetProperty("scope", out var scope) && scope.GetString() != "page")
        {
            return BindingRules.ScopeMissing;
        }

        if (schema is null)
        {
            return BindingRules.ScopeMissing;
        }

        var target = schema;
        if (reader.TryGetProperty("via", out var via))
        {
            if (schema.Field(via.GetString()) is not { } reference)
            {
                return BindingRules.FieldMissing;
            }

            if (reference.Type != FieldInfo.Reference)
            {
                return BindingRules.ViaNotReference;
            }

            if (reference.Target is not { } targetId || !data.Schemas.TryGetValue(targetId, out target!))
            {
                return null;
            }
        }

        if (reader.TryGetProperty("item", out var builtIn))
        {
            return accepts.Contains(builtIn.GetString() ?? string.Empty) ? null : BindingRules.TypeMismatch;
        }

        if (target.Field(reader.TryGetProperty("field", out var field) ? field.GetString() : null) is not { } info)
        {
            return BindingRules.FieldMissing;
        }

        return StructuredDataTypes.AcceptOf(info) is { } accept && accepts.Contains(accept) ? null : BindingRules.TypeMismatch;
    }

    private static void Copy(Dictionary<string, JsonNode> values, JsonObject node, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value))
            {
                node[name] = value.DeepClone();
            }
        }
    }

    private static JsonNode? Default(StructuredDataProperty property, ItemNodeInputs inputs) => property.Default switch
    {
        "name" when !string.IsNullOrWhiteSpace(inputs.Item.Name) => PlainText.Collapse(inputs.Item.Name),
        "firstPublishedAt" => Iso(inputs.Item.FirstPublishedAtUtc),
        "lastPublishedAt" => Iso(inputs.Item.LastPublishedAtUtc),
        _ => null,
    };

    private static JsonNode? Read(JsonElement binding, StructuredDataProperty property, ItemNodeInputs inputs)
    {
        if (!binding.TryGetProperty("value", out var value))
        {
            return null;
        }

        if (value.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            if (!property.Accepts.Contains("template"))
            {
                return null;
            }

            var text = new StringBuilder();
            var any = false;
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    text.Append(part.GetString());
                }
                else if (One(part, TextAccepts, "text", inputs) is JsonValue piece && piece.TryGetValue<string>(out var s))
                {
                    text.Append(s);
                    any = true;
                }
            }

            return any && PlainText.Collapse(text.ToString()) is { Length: > 0 } joined ? joined : null;
        }

        return One(value, property.Accepts, Output(property.Name), inputs);
    }

    private static string Output(string property) => property switch
    {
        "image" => "image",
        "price" => "number",
        "availability" => "availability",
        _ when DateProperties.Contains(property) => "date",
        _ => "text",
    };

    private static JsonNode? One(JsonElement reader, System.Collections.Frozen.FrozenSet<string> accepts, string output, ItemNodeInputs inputs)
    {
        if (reader.TryGetProperty("scope", out var scope) && scope.GetString() != "page")
        {
            return null;
        }

        var item = inputs.Item;
        var data = inputs.Data;
        if (!data.Schemas.TryGetValue(item.CollectionId, out var schema))
        {
            return null;
        }

        if (reader.TryGetProperty("via", out var via))
        {
            if (schema.Field(via.GetString()) is not { Type: FieldInfo.Reference } reference
                || BindingResolver.ReadValue(item, reference.Id) is not { ValueKind: JsonValueKind.String } target
                || !Guid.TryParse(target.GetString(), out var targetId)
                || !data.Items.TryGetValue(targetId, out var referenced)
                || !data.Schemas.TryGetValue(referenced.CollectionId, out schema))
            {
                return null;
            }

            item = referenced;
        }

        if (reader.TryGetProperty("item", out var builtInElement))
        {
            var builtIn = builtInElement.GetString() ?? string.Empty;
            if (!accepts.Contains(builtIn))
            {
                return null;
            }

            return builtIn switch
            {
                "slug" => item.Slug,
                "firstPublishedAt" => Iso(item.FirstPublishedAtUtc),
                "lastPublishedAt" => Iso(item.LastPublishedAtUtc),
                _ => null,
            };
        }

        if (schema.Field(reader.TryGetProperty("field", out var fieldId) ? fieldId.GetString() : null) is not { } field
            || StructuredDataTypes.AcceptOf(field) is not { } accept
            || !accepts.Contains(accept)
            || BindingResolver.ReadValue(item, field.Id) is not { } stored)
        {
            return null;
        }

        return (accept, output) switch
        {
            ("text", _) when stored.ValueKind == JsonValueKind.String => NonEmpty(PlainText.Collapse(stored.GetString()!)),
            ("richText", _) => NonEmpty(PlainText.Of(stored)),
            ("option", _) when stored.ValueKind == JsonValueKind.String => field.Options.TryGetValue(stored.GetString()!, out var label) ? NonEmpty(label) : null,
            ("number", "number") when stored.TryGetDecimal(out var number) => JsonValue.Create(number),
            ("number", _) when stored.TryGetDecimal(out var number) => number.ToString(CultureInfo.InvariantCulture),
            ("date", "date") when stored.ValueKind == JsonValueKind.String => IsoDate(stored.GetString()!),
            ("date", _) when stored.ValueKind == JsonValueKind.String => ValueFormatter.Date(stored.GetString()!, null, inputs.Language),
            ("boolean", "availability") => stored.ValueKind switch
            {
                JsonValueKind.True => "https://schema.org/InStock",
                JsonValueKind.False => "https://schema.org/OutOfStock",
                _ => null,
            },
            ("image", "image") => stored.ValueKind == JsonValueKind.Object && stored.TryGetProperty("mediaId", out var id)
                && Guid.TryParse(id.GetString(), out var mediaId) && inputs.Media.TryGetValue(mediaId, out var file)
                    ? HeadBuilder.SharingImageOf(file, inputs.BaseUrl).Url
                    : null,
            ("reference", _) when stored.ValueKind == JsonValueKind.String => Guid.TryParse(stored.GetString(), out var refId)
                && data.Items.TryGetValue(refId, out var named) && !string.IsNullOrWhiteSpace(named.Name)
                    ? PlainText.Collapse(named.Name)
                    : null,
            _ => null,
        };
    }

    private static JsonNode? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private static string Iso(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A stored date value, already ISO 8601 (collection-schema.md): a date, or a UTC moment.</summary>
    private static string? IsoDate(string stored) =>
        IsoDatePattern().IsMatch(stored) ? stored : null;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex Currency();

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}(T\\d{2}:\\d{2}:\\d{2}Z)?$")]
    private static partial Regex IsoDatePattern();
}
