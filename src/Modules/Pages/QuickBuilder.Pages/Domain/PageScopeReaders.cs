using System.Text.Json;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// The bindings, filter values and list sources of a document that read the page's item (016
/// research TB-02, US7 scenario 5): the ones a template change leaves broken.
/// </summary>
/// <remarks>
/// A reader reads the page's item when it names the <c>page</c> scope, or names none outside
/// every list card. Only the field it starts from is told: a reference step's own field lives in
/// another collection, which a template change does not touch.
/// </remarks>
internal static class PageScopeReaders
{
    /// <summary>The field ids read from the page's item, one entry per reader; empty for a built-in value.</summary>
    public static IReadOnlyList<string> FieldsRead(string document)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        using var parsed = JsonDocument.Parse(document);
        var found = new List<string>();
        if (parsed.RootElement.TryGetProperty("root", out var root))
        {
            Walk(root, inCard: 0, found);
        }

        return found;
    }

    /// <summary>
    /// How many readers break when the page's item comes from a collection with these fields
    /// instead; every reader when <paramref name="fieldIds"/> is null (the page stops being a
    /// template).
    /// </summary>
    public static int Breaking(string document, IReadOnlySet<string>? fieldIds) =>
        FieldsRead(document).Count(field => fieldIds is null || (field.Length > 0 && !fieldIds.Contains(field)));

    private static void Walk(JsonElement node, int inCard, List<string> found)
    {
        var isList = node.TryGetProperty("type", out var type) && type.GetString() == "collection-list";

        if (node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            if (props.TryGetProperty("bind", out var bind) && bind.ValueKind == JsonValueKind.Object)
            {
                foreach (var binding in bind.EnumerateObject())
                {
                    if (binding.Value.TryGetProperty("value", out var value))
                    {
                        if (value.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var part in parts.EnumerateArray())
                            {
                                Read(part, inCard, found);
                            }
                        }
                        else
                        {
                            Read(value, inCard, found);
                        }
                    }
                }
            }

            if (isList)
            {
                if (props.TryGetProperty("source", out var source) && source.TryGetProperty("field", out _))
                {
                    Read(source, inCard, found);
                }

                if (props.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array)
                {
                    foreach (var filter in filters.EnumerateArray())
                    {
                        if (filter.TryGetProperty("value", out var operand) && operand.ValueKind == JsonValueKind.Object && operand.TryGetProperty("field", out _))
                        {
                            Read(operand, inCard, found);
                        }
                    }
                }
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                var opensCard = isList && child.TryGetProperty("type", out var childType) && childType.GetString() == "list-card";
                Walk(child, inCard + (opensCard ? 1 : 0), found);
            }
        }
    }

    private static void Read(JsonElement reader, int inCard, List<string> found)
    {
        if (reader.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var scope = reader.TryGetProperty("scope", out var named) ? named.GetString() : null;
        var readsPage = scope == "page" || (scope is null && inCard == 0);
        if (!readsPage)
        {
            return;
        }

        // The field the reader starts from on the page's item: the reference step, else the field.
        var start = reader.TryGetProperty("via", out var via) ? via.GetString()
            : reader.TryGetProperty("field", out var field) ? field.GetString()
            : null;

        // A built-in value (slug, publish times, address) reads no field and never breaks on a change of collection.
        if (start is not null)
        {
            found.Add(start);
        }
        else if (reader.TryGetProperty("item", out _))
        {
            found.Add(string.Empty);
        }
    }
}
