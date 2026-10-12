using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.Pages.Infrastructure.Schema;

/// <summary>
/// The rules of the page schema contract §3 that bindings and Collection list blocks add
/// (016, rules 13 and 15–20; research TB-06), for a document that has passed the schema.
/// </summary>
/// <remarks>
/// <para>
/// Only what makes a document meaningless or unsafe is refused here: a missing format
/// version, a binding or list that names no value, a scope that names no enclosing list, a
/// page item in a component, too many or too deeply nested lists, two lists paging on one
/// parameter. A binding to a field that does not exist, or whose type does not fit, is
/// <em>broken</em>, never refused: schemas change in another module after the page is saved
/// (binding-expression.md §4). So is the <c>"page"</c> scope in an ordinary page, so that
/// turning a template back into a page never makes its draft unsaveable.
/// </para>
/// <para>
/// Rule 14 (a list's collection belongs to the site) needs the caller's site, as rule 6 does
/// for components: this collects the collections and the caller checks them, last.
/// </para>
/// <para>
/// Rule 20 (a list holds its card, then its empty state) runs first, with the structural
/// checks; rule 13 runs next; then one walk in document order checks each list and each
/// binding as it meets them. The order is fixed so a document always fails the same way.
/// </para>
/// </remarks>
internal static class BindingDocumentRules
{
    public const string ListType = "collection-list";
    public const string CardType = "list-card";
    public const string EmptyType = "list-empty";
    public const string PageScope = "page";
    public const string DefaultPageParam = "page";

    /// <summary>Rule 20: every Collection list holds exactly its card and then its empty state.</summary>
    public static (string Rule, string Pointer, string Message)? CheckListChildren(JsonElement node, string pointer)
    {
        if (node.GetProperty("type").GetString() == ListType)
        {
            var children = node.GetProperty("children");
            if (children.GetArrayLength() != 2
                || children[0].GetProperty("type").GetString() != CardType
                || children[1].GetProperty("type").GetString() != EmptyType)
            {
                return ("list_children_invalid", $"{pointer}/children", "A Collection list holds exactly its card and then its empty state.");
            }
        }

        var index = 0;
        foreach (var child in node.GetProperty("children").EnumerateArray())
        {
            if (CheckListChildren(child, $"{pointer}/children/{index}") is { } failure)
            {
                return failure;
            }

            index++;
        }

        return null;
    }

    /// <summary>
    /// Rules 13, 15–19 and the "exactly one of" shapes the schema cannot say, over a document
    /// that passed the schema and rule 20. Collects every list source collection into
    /// <paramref name="collections"/>.
    /// </summary>
    public static (string Rule, string Pointer, string Message)? Check(
        JsonElement document, DocumentKind kind, Dictionary<Guid, string> collections)
    {
        var root = document.GetProperty("root");

        if (UsesBindings(root) && !document.TryGetProperty("bindingVersion", out _))
        {
            return ("binding_version_required", string.Empty, "A document with bindings or collection lists must state its binding format version.");
        }

        var walk = new Walk(kind, collections);
        return walk.Visit(root, "/root");
    }

    /// <summary>Whether a document holds a binding or a Collection list anywhere.</summary>
    public static bool UsesBindings(JsonElement node)
    {
        if (node.GetProperty("type").GetString() == ListType
            || (node.GetProperty("props").TryGetProperty("bind", out var bind) && bind.EnumerateObject().Any()))
        {
            return true;
        }

        return node.GetProperty("children").EnumerateArray().Any(UsesBindings);
    }

    private sealed class Walk(DocumentKind kind, Dictionary<Guid, string> collections)
    {
        /// <summary>The enclosing lists whose card the walk is inside, innermost last.</summary>
        private readonly List<string> _scopes = [];
        private readonly HashSet<string> _pageParams = new(StringComparer.Ordinal);
        private int _lists;

        /// <summary>How many lists the walk is inside, through their card or their empty state.</summary>
        private int _listDepth;

        public (string, string, string)? Visit(JsonElement node, string pointer)
        {
            var type = node.GetProperty("type").GetString();
            var props = node.GetProperty("props");

            if (type == ListType && CheckList(node, props, pointer) is { } listFailure)
            {
                return listFailure;
            }

            if (props.TryGetProperty("bind", out var bind))
            {
                foreach (var binding in bind.EnumerateObject())
                {
                    var here = $"{pointer}/props/bind/{SchemaSubsetValidator.Escape(binding.Name)}/value";
                    if (CheckBindingValue(binding.Value.GetProperty("value"), here) is { } bindFailure)
                    {
                        return bindFailure;
                    }
                }
            }

            if (type == ListType)
            {
                _listDepth++;
            }

            var index = 0;
            foreach (var child in node.GetProperty("children").EnumerateArray())
            {
                // A card's blocks bind to the card's item, so the card opens the list's scope;
                // the empty state has no item and opens none.
                var opensScope = type == ListType && child.GetProperty("type").GetString() == CardType;
                if (opensScope)
                {
                    _scopes.Add(node.GetProperty("id").GetString()!);
                }

                var failure = Visit(child, $"{pointer}/children/{index}");

                if (opensScope)
                {
                    _scopes.RemoveAt(_scopes.Count - 1);
                }

                // The walk stops at the first failure, so nothing needs undoing on the way out.
                if (failure is not null)
                {
                    return failure;
                }

                index++;
            }

            if (type == ListType)
            {
                _listDepth--;
            }

            return null;
        }

        private (string, string, string)? CheckList(JsonElement node, JsonElement props, string pointer)
        {
            if (++_lists > PageSchema.MaxListsPerDocument)
            {
                return ("too_many_lists", $"{pointer}/type", $"A document may hold at most {PageSchema.MaxListsPerDocument} collection lists.");
            }

            if (_listDepth + 1 > PageSchema.MaxListDepth)
            {
                return ("list_too_deep", $"{pointer}/type", $"Collection lists may be nested at most {PageSchema.MaxListDepth} deep.");
            }

            var source = props.GetProperty("source");
            var sourcePointer = $"{pointer}/props/source";
            var hasCollection = source.TryGetProperty("collectionId", out var collectionId);
            var hasField = source.TryGetProperty("field", out _);
            if (hasCollection == hasField)
            {
                return PropsInvalid(sourcePointer, "A list's source names either a collection or a multi-reference field.");
            }

            if (hasCollection)
            {
                collections.TryAdd(collectionId.GetGuid(), sourcePointer);
            }
            else if (CheckScope(source, sourcePointer) is { } scopeFailure)
            {
                return scopeFailure;
            }

            if (props.TryGetProperty("filters", out var filters))
            {
                var index = 0;
                foreach (var filter in filters.EnumerateArray())
                {
                    var here = $"{pointer}/props/filters/{index}";
                    if (filter.TryGetProperty("field", out _) == filter.TryGetProperty("item", out _))
                    {
                        return PropsInvalid(here, "A filter names either a field or an item value.");
                    }

                    if (filter.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
                    {
                        var valuePointer = $"{here}/value";
                        if (value.TryGetProperty("field", out _) == value.TryGetProperty("current", out _))
                        {
                            return PropsInvalid(valuePointer, "A filter value from a scope names either a field or the current item.");
                        }

                        if (CheckScope(value, valuePointer) is { } failure)
                        {
                            return failure;
                        }
                    }

                    index++;
                }
            }

            if (props.TryGetProperty("sort", out var sorts))
            {
                var index = 0;
                foreach (var sort in sorts.EnumerateArray())
                {
                    if (sort.TryGetProperty("field", out _) == sort.TryGetProperty("item", out _))
                    {
                        return PropsInvalid($"{pointer}/props/sort/{index}", "A sort key names either a field or an item value.");
                    }

                    index++;
                }
            }

            if (props.TryGetProperty("paging", out var paging))
            {
                var hasParam = paging.TryGetProperty("param", out var param);
                var name = hasParam ? param.GetString()! : DefaultPageParam;
                if (!_pageParams.Add(name))
                {
                    var here = hasParam ? $"{pointer}/props/paging/param" : $"{pointer}/props/paging";
                    return ("duplicate_page_param", here, "Two paginated lists in one document use the same page parameter.");
                }
            }

            return null;
        }

        private (string, string, string)? CheckBindingValue(JsonElement value, string pointer)
        {
            var named = (value.TryGetProperty("field", out _) ? 1 : 0)
                + (value.TryGetProperty("item", out _) ? 1 : 0)
                + (value.TryGetProperty("parts", out _) ? 1 : 0);
            if (named != 1)
            {
                return PropsInvalid(pointer, "A binding names exactly one of a field, an item value or text parts.");
            }

            if (value.TryGetProperty("parts", out var parts))
            {
                if (value.EnumerateObject().Any(p => p.Name != "parts"))
                {
                    return PropsInvalid(pointer, "A text template holds only its parts.");
                }

                var index = 0;
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object)
                    {
                        var here = $"{pointer}/parts/{index}";
                        if (part.TryGetProperty("field", out _) == part.TryGetProperty("item", out _))
                        {
                            return PropsInvalid(here, "A value in a text template names either a field or an item value.");
                        }

                        if (CheckScope(part, here) is { } failure)
                        {
                            return failure;
                        }
                    }

                    index++;
                }

                return null;
            }

            return CheckScope(value, pointer);
        }

        /// <summary>
        /// Rules 15 and 16 for anything that reads a scope: the scope it names, or the nearest one
        /// when it names none, must exist here, and a component has no page item.
        /// </summary>
        private (string, string, string)? CheckScope(JsonElement reader, string pointer)
        {
            string scope;
            string where;
            if (reader.TryGetProperty("scope", out var named))
            {
                scope = named.GetString()!;
                where = $"{pointer}/scope";
                if (scope != PageScope && !_scopes.Contains(scope, StringComparer.Ordinal))
                {
                    return ("unknown_scope", where, "The scope names no enclosing collection list.");
                }
            }
            else
            {
                scope = _scopes.Count > 0 ? _scopes[^1] : PageScope;
                where = pointer;
            }

            if (scope == PageScope && kind == DocumentKind.Component)
            {
                return ("page_scope_not_allowed", where, "A component cannot read the item of the page it is shown on.");
            }

            return null;
        }

        private static (string, string, string) PropsInvalid(string pointer, string message) =>
            ("props_invalid", pointer, $"The node's properties are not valid: {message}");
    }
}
