using System.Text.Json;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// Which collections a document uses, and which of their fields it reads (016 data-model §1.3,
/// research TB-13): what Cms needs to refuse deleting a collection in use (FR-010) and to count
/// a field's uses before a schema change (FR-011).
/// </summary>
/// <remarks>
/// <para>
/// The walk carries the binding scopes as it goes: the page's item on a template, then the item
/// of each list whose card it enters. A Collection list uses its collection, with the fields its
/// filters and sort keys read; a binding reads a field of its scope's collection, and through
/// <c>via</c> also a field of the referenced collection.
/// </para>
/// <para>
/// A list over a multi-reference field, and a <c>via</c> through a reference, reach the field's
/// target collection, which only a schema knows: <paramref name="referenceTarget"/> answers it,
/// or null when it cannot be told, in which case the field still counts for its own collection
/// and the target is left out. The
/// document has passed the page schema, so anything else unexpected is skipped.
/// </para>
/// </remarks>
internal static class CollectionUseExtractor
{
    /// <summary>The collections used, each with the field ids read.</summary>
    /// <param name="document">A page or component document; null for none (only the template counts).</param>
    /// <param name="templateCollectionId">The page's template collection, for a template page.</param>
    /// <param name="referenceTarget">A (multi-)reference field's target collection, by collection and field.</param>
    public static IReadOnlyDictionary<Guid, SortedSet<string>> Extract(
        string? document,
        Guid? templateCollectionId,
        Func<Guid, string, Guid?> referenceTarget)
    {
        ArgumentNullException.ThrowIfNull(referenceTarget);

        var uses = new Dictionary<Guid, SortedSet<string>>();
        if (templateCollectionId is { } template)
        {
            Use(uses, template);
        }

        if (string.IsNullOrEmpty(document))
        {
            return uses;
        }

        using var parsed = JsonDocument.Parse(document);
        if (!parsed.RootElement.TryGetProperty("root", out var root))
        {
            return uses;
        }

        var scopes = new List<(string Name, Guid? Collection)>();
        if (templateCollectionId is not null)
        {
            scopes.Add(("page", templateCollectionId));
        }

        Visit(root);
        return uses;

        void Visit(JsonElement node)
        {
            var props = node.TryGetProperty("props", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty("bind", out var bind) && bind.ValueKind == JsonValueKind.Object)
            {
                foreach (var binding in bind.EnumerateObject())
                {
                    if (binding.Value.ValueKind == JsonValueKind.Object && binding.Value.TryGetProperty("value", out var value))
                    {
                        ReadBinding(value);
                    }
                }
            }

            var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
            Guid? listCollection = null;
            if (type == "collection-list" && props.ValueKind == JsonValueKind.Object)
            {
                listCollection = ReadList(props);
            }

            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var child in children.EnumerateArray())
            {
                // Only a list's card opens its scope; its empty state has no item.
                var opens = type == "collection-list" && child.TryGetProperty("type", out var ct) && ct.GetString() == "list-card";
                if (opens)
                {
                    scopes.Add((node.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty, listCollection));
                }

                Visit(child);
                if (opens)
                {
                    scopes.RemoveAt(scopes.Count - 1);
                }
            }
        }

        void ReadBinding(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            IEnumerable<JsonElement> readers = value.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array
                ? parts.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object)
                : [value];

            foreach (var reader in readers)
            {
                var collection = ScopeCollection(reader);
                if (collection is not { } scopeCollection)
                {
                    continue;
                }

                var field = reader.TryGetProperty("field", out var f) ? f.GetString() : null;
                if (reader.TryGetProperty("via", out var via) && via.GetString() is { } viaField)
                {
                    Use(uses, scopeCollection).Add(viaField);
                    if (field is not null && referenceTarget(scopeCollection, viaField) is { } target)
                    {
                        Use(uses, target).Add(field);
                    }
                }
                else if (field is not null)
                {
                    Use(uses, scopeCollection).Add(field);
                }
            }
        }

        Guid? ReadList(JsonElement props)
        {
            Guid? collection = null;
            if (props.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                if (source.TryGetProperty("collectionId", out var id) && Guid.TryParse(id.GetString(), out var direct))
                {
                    collection = direct;
                }
                else if (source.TryGetProperty("field", out var field) && field.GetString() is { } fieldId && ScopeCollection(source) is { } scopeCollection)
                {
                    Use(uses, scopeCollection).Add(fieldId);
                    collection = referenceTarget(scopeCollection, fieldId);
                }
            }

            if (collection is not { } listCollection)
            {
                return null;
            }

            var read = Use(uses, listCollection);
            foreach (var property in new[] { "filters", "sort" })
            {
                if (!props.TryGetProperty(property, out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (entry.TryGetProperty("field", out var target) && target.GetString() is { } targetField)
                    {
                        read.Add(targetField);
                    }

                    // A filter's value read from a scope's item is a use of that scope's field.
                    if (entry.TryGetProperty("value", out var operand) && operand.ValueKind == JsonValueKind.Object
                        && operand.TryGetProperty("field", out var operandField) && operandField.GetString() is { } fieldId
                        && ScopeCollection(operand) is { } operandCollection)
                    {
                        Use(uses, operandCollection).Add(fieldId);
                    }
                }
            }

            return listCollection;
        }

        Guid? ScopeCollection(JsonElement reader)
        {
            var name = reader.TryGetProperty("scope", out var scope) ? scope.GetString() : null;
            for (var i = scopes.Count - 1; i >= 0; i--)
            {
                if (name is null || scopes[i].Name == name)
                {
                    return scopes[i].Collection;
                }
            }

            return null;
        }
    }

    private static SortedSet<string> Use(Dictionary<Guid, SortedSet<string>> uses, Guid collection)
    {
        if (!uses.TryGetValue(collection, out var fields))
        {
            fields = new SortedSet<string>(StringComparer.Ordinal);
            uses[collection] = fields;
        }

        return fields;
    }
}
