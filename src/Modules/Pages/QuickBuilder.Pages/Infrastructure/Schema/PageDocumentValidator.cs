using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.Pages.Infrastructure.Schema;

/// <summary>
/// Checks a content document against the page schema contract, in the order
/// docs/contracts/page-schema.md §3 gives (research P-09).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Size</b>, on the bytes as received, before anything is parsed.</item>
/// <item><b>Well-formed JSON, without duplicate property names.</b> A parser keeps the last of
/// two equal names, so two readers of one document could disagree about it; it is refused.</item>
/// <item><b>A supported <c>schemaVersion</c>.</b> A missing or malformed one is left to the
/// schema, which reports it like any other missing field.</item>
/// <item><b>A structural walk</b>: depth, node count, node ids unique. Before the schema on
/// purpose: it bounds the schema check's work, and an oversized document gets its own reason
/// rather than a generic one.</item>
/// <item><b>The schema</b>, through <see cref="SchemaSubsetValidator"/> over the embedded file.</item>
/// <item><b>The component rules</b> the schema cannot say: a component document references no
/// component. Whether a referenced component exists is the caller's to check, against its
/// site; this collects the ids.</item>
/// <item><b>Rich text length</b> (§3 rule 9, added by 011): the sum over a Rich Text block's
/// text nodes, which the schema can only bound one node at a time.</item>
/// </list>
/// Messages never quote document content (FR-037).
/// </remarks>
internal sealed class PageDocumentValidator : IPageDocumentValidator
{
    private const string InvalidDocument = "pages.invalid_document";
    private const string ComponentRefType = "component-ref";

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        // Well past any document within MaxNodeDepth (a node level is an object inside an
        // array, so about two JSON levels), so the structural walk — which can say where —
        // reports ordinary over-deep documents. Only a pathological one hits this cap.
        MaxDepth = 256,
    };

    private const string RichTextType = "rich-text";

    private readonly Dictionary<int, SchemaSubsetValidator> _schemas;

    /// <summary>
    /// Per version, the node types: every type the placement lists admit. A known node type in
    /// the wrong list is <c>nesting_not_allowed</c>; anything else is <c>unknown_type</c>, even
    /// when the schema uses that name elsewhere, as rich text does with <c>paragraph</c>
    /// (011 research ED-05).
    /// </summary>
    private readonly Dictionary<int, IReadOnlySet<string>> _nodeTypes;

    public PageDocumentValidator()
    {
        _schemas = PageSchema.SupportedVersions.ToDictionary(
            v => v,
            v =>
            {
                using var stream = PageSchema.OpenSchema(v);
                return SchemaSubsetValidator.Load(stream);
            });

        _nodeTypes = _schemas.ToDictionary(
            s => s.Key,
            s => (IReadOnlySet<string>)s.Value.BranchTypes("rootChild").Union(s.Value.BranchTypes("contentChild")).ToHashSet(StringComparer.Ordinal));
    }

    public DocumentValidationResult Validate(ReadOnlySpan<byte> utf8Json, DocumentKind kind)
    {
        if (utf8Json.Length > PageSchema.MaxDocumentBytes)
        {
            return DocumentValidationResult.Invalid(
                "pages.document_too_large", "too_large", string.Empty,
                $"A document may be at most {PageSchema.MaxDocumentBytes} bytes.");
        }

        JsonDocument document;
        try
        {
            // A whole-input parse, so anything after the document is refused rather than ignored.
            document = JsonDocument.Parse(utf8Json.ToArray(), ParseOptions);
        }
        catch (JsonException ex) when (ex.Message.Contains("maximum configured depth", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("too_deep", string.Empty, $"Nodes may be nested at most {PageSchema.MaxNodeDepth} deep.");
        }
        catch (JsonException)
        {
            return Invalid("malformed_json", string.Empty, "The document is not well-formed JSON.");
        }

        using (document)
        {
            return Validate(document.RootElement, kind);
        }
    }

    private DocumentValidationResult Validate(JsonElement root, DocumentKind kind)
    {
        if (FindDuplicateProperty(root, string.Empty) is { } duplicate)
        {
            return Invalid("duplicate_property", duplicate, "A property name appears twice in one object.");
        }

        var version = PageSchema.CurrentVersion;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("schemaVersion", out var declared)
            && declared.ValueKind == JsonValueKind.Number
            && declared.TryGetInt32(out var declaredVersion))
        {
            if (!PageSchema.SupportedVersions.Contains(declaredVersion))
            {
                return DocumentValidationResult.Invalid(
                    "pages.unsupported_schema_version", "unsupported_schema_version", "/schemaVersion",
                    "This installation does not support that schema version.");
            }

            version = declaredVersion;
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("root", out var rootNode))
        {
            var walk = new StructuralWalk();
            if (walk.Visit(rootNode, "/root", depth: 1) is { } structural)
            {
                return structural;
            }
        }

        if (_schemas[version].Validate(root) is { } failure)
        {
            return FromSchema(failure, root, _nodeTypes[version]);
        }

        var referenced = new HashSet<Guid>();
        if (CheckComponentRules(root.GetProperty("root"), "/root", kind, referenced) is { } componentFailure)
        {
            return componentFailure;
        }

        if (CheckRichTextLength(root.GetProperty("root"), "/root") is { } richTextFailure)
        {
            return richTextFailure;
        }

        return DocumentValidationResult.Valid(version, referenced);
    }

    /// <summary>Turns a schema failure into the contract's rule names (pages-api.yaml, DocumentProblem).</summary>
    private static DocumentValidationResult FromSchema(SchemaFailure failure, JsonElement root, IReadOnlySet<string> nodeTypes)
    {
        // Anything under a node's props is a props failure, whatever the keyword — a rich-text
        // mark of an unknown kind included. Checked before the discriminator, which would
        // otherwise call it an unknown node type (011 research ED-05, data-model §1.4).
        var segments = failure.Location.Split('/');
        if (segments.Contains("props", StringComparer.Ordinal))
        {
            var location = failure.Keyword == "discriminator" ? $"{failure.Location}/type" : failure.Location;
            return Invalid("props_invalid", location, $"The node's properties are not valid: {failure.Message}");
        }

        if (failure.Keyword == "discriminator")
        {
            var type = At(root, failure.Location) is { ValueKind: JsonValueKind.Object } node
                && node.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

            if (type == "root")
            {
                return Invalid("root_misplaced", failure.Location, "A root node may only be the document's root.");
            }

            return type is not null && nodeTypes.Contains(type)
                ? Invalid("nesting_not_allowed", $"{failure.Location}/type", "This kind of node is not allowed here.")
                : Invalid("unknown_type", $"{failure.Location}/type", "This is not a known node type.");
        }

        if (failure.Keyword == "maxItems" && failure.Location.EndsWith("/children", StringComparison.Ordinal))
        {
            return Invalid("children_not_allowed", failure.Location, "This kind of node cannot have children.");
        }

        return Invalid("schema_violation", failure.Location, failure.Message);
    }

    private static DocumentValidationResult? CheckComponentRules(
        JsonElement node,
        string pointer,
        DocumentKind kind,
        HashSet<Guid> referenced)
    {
        if (node.GetProperty("type").GetString() == ComponentRefType)
        {
            if (kind == DocumentKind.Component)
            {
                return Invalid("component_ref_not_allowed", pointer, "A component cannot contain other components.");
            }

            referenced.Add(node.GetProperty("props").GetProperty("componentId").GetGuid());
        }

        var index = 0;
        foreach (var child in node.GetProperty("children").EnumerateArray())
        {
            if (CheckComponentRules(child, $"{pointer}/children/{index}", kind, referenced) is { } failure)
            {
                return failure;
            }

            index++;
        }

        return null;
    }

    /// <summary>
    /// Contract §3 rule 9: a Rich Text block holds at most <see cref="PageSchema.MaxRichTextChars"/>
    /// characters of text across all its text nodes, counted in code points as <c>maxLength</c>
    /// is. The schema bounds each text node; only code can bound their sum.
    /// </summary>
    private static DocumentValidationResult? CheckRichTextLength(JsonElement node, string pointer)
    {
        if (node.GetProperty("type").GetString() == RichTextType)
        {
            var length = 0L;
            CountText(node.GetProperty("props").GetProperty("content"), ref length);
            if (length > PageSchema.MaxRichTextChars)
            {
                return Invalid(
                    "rich_text_too_long",
                    $"{pointer}/props/content",
                    $"A Rich Text block may hold at most {PageSchema.MaxRichTextChars} characters of text.");
            }
        }

        var index = 0;
        foreach (var child in node.GetProperty("children").EnumerateArray())
        {
            if (CheckRichTextLength(child, $"{pointer}/children/{index}") is { } failure)
            {
                return failure;
            }

            index++;
        }

        return null;
    }

    /// <summary>Adds the code points of every <c>text</c> node under a validated rich-text value.</summary>
    private static void CountText(JsonElement richText, ref long length)
    {
        if (richText.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            foreach (var _ in text.GetString()!.EnumerateRunes())
            {
                length++;
            }
        }

        if (richText.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                CountText(child, ref length);
            }
        }
    }

    private static string? FindDuplicateProperty(JsonElement element, string pointer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var here = $"{pointer}/{SchemaSubsetValidator.Escape(property.Name)}";
                    if (!seen.Add(property.Name))
                    {
                        return here;
                    }

                    if (FindDuplicateProperty(property.Value, here) is { } nested)
                    {
                        return nested;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (FindDuplicateProperty(item, $"{pointer}/{index}") is { } nested)
                    {
                        return nested;
                    }

                    index++;
                }

                return null;
            default:
                return null;
        }
    }

    private static JsonElement? At(JsonElement root, string pointer)
    {
        var current = root;
        foreach (var token in pointer.Split('/').Skip(1))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal), out var next))
            {
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(token, out var i) && i < current.GetArrayLength())
            {
                current = current[i];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static DocumentValidationResult Invalid(string rule, string pointer, string message) =>
        DocumentValidationResult.Invalid(InvalidDocument, rule, pointer, message);

    /// <summary>
    /// Depth, count and id uniqueness, over whatever node-shaped values exist. It follows
    /// <c>children</c> arrays of objects only, and leaves every other shape question to the
    /// schema.
    /// </summary>
    private sealed class StructuralWalk
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private int _count;

        public DocumentValidationResult? Visit(JsonElement node, string pointer, int depth)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (depth > PageSchema.MaxNodeDepth)
            {
                return Invalid("too_deep", pointer, $"Nodes may be nested at most {PageSchema.MaxNodeDepth} deep.");
            }

            if (++_count > PageSchema.MaxNodes)
            {
                return Invalid("too_many_nodes", pointer, $"A document may hold at most {PageSchema.MaxNodes} nodes.");
            }

            if (node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !_ids.Add(id.GetString()!))
            {
                return Invalid("duplicate_node_id", $"{pointer}/id", "Another node in this document has the same id.");
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var child in children.EnumerateArray())
                {
                    if (Visit(child, $"{pointer}/children/{index}", depth + 1) is { } failure)
                    {
                        return failure;
                    }

                    index++;
                }
            }

            return null;
        }
    }
}
