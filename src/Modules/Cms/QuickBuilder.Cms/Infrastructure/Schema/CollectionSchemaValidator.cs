using System.Text.Json;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.Cms.Infrastructure.Schema;

/// <summary>
/// Checks a schema document against the collection schema contract, in the order
/// docs/contracts/collection-schema.md §5 gives (research CS-04).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Size</b>, on the bytes as received, before anything is parsed.</item>
/// <item><b>Well-formed JSON without duplicate property names</b>: a parser keeps the last of
/// two equal names, so two readers could disagree about the document.</item>
/// <item><b>A supported <c>schemaVersion</c></b>. A missing or malformed one is left to the schema.</item>
/// <item><b>The schema file</b>, through <see cref="SchemaSubsetValidator"/> over the embedded file.</item>
/// <item><b>The rules</b> of <see cref="CollectionSchemaRules"/>, after a proposal is normalised.</item>
/// </list>
/// Stateless: the schema files are loaded once. Messages never quote document content.
/// </remarks>
internal sealed class CollectionSchemaValidator
{
    public const string InvalidSchema = "cms.invalid_schema";

    private readonly Dictionary<int, SchemaSubsetValidator> _schemas;

    public CollectionSchemaValidator()
    {
        _schemas = CollectionSchema.SupportedVersions.ToDictionary(
            v => v,
            v =>
            {
                using var stream = CollectionSchema.OpenSchema(v);
                return SchemaSubsetValidator.Load(stream);
            });
    }

    /// <summary>The loaded contract file for <paramref name="version"/>, for item value checks.</summary>
    public SchemaSubsetValidator SchemaFor(int version) => _schemas[version];

    public SchemaValidationResult Validate(ReadOnlySpan<byte> utf8Json, SchemaDocumentKind kind)
    {
        if (utf8Json.Length > CollectionSchemaLimits.MaxProposalBytes)
        {
            return SchemaValidationResult.Invalid(
                "cms.proposal_too_large", "too_large", string.Empty,
                $"A schema may be at most {CollectionSchemaLimits.MaxProposalBytes / 1024} KiB.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (JsonException)
        {
            return SchemaValidationResult.Invalid(InvalidSchema, "malformed_json", string.Empty, "The schema is not well-formed JSON.");
        }

        using (document)
        {
            return Validate(document.RootElement, kind);
        }
    }

    public SchemaValidationResult Validate(JsonElement root, SchemaDocumentKind kind)
    {
        if (FindDuplicateProperty(root, string.Empty) is { } duplicate)
        {
            return SchemaValidationResult.Invalid(InvalidSchema, "duplicate_property", duplicate, "A property name appears twice in one object.");
        }

        var version = CollectionSchema.CurrentVersion;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("schemaVersion", out var declared)
            && declared.ValueKind == JsonValueKind.Number
            && declared.TryGetInt32(out var declaredVersion))
        {
            if (!CollectionSchema.SupportedVersions.Contains(declaredVersion))
            {
                return SchemaValidationResult.Invalid(
                    "cms.unsupported_schema_version", "unsupported_schema_version", "/schemaVersion",
                    "This installation does not support that schema version.");
            }

            version = declaredVersion;
        }

        if (_schemas[version].Validate(root) is { } failure)
        {
            return SchemaValidationResult.Invalid(InvalidSchema, "schema_violation", failure.Location, failure.Message);
        }

        var schema = CollectionSchemaDocument.Read(root);
        if (kind == SchemaDocumentKind.Proposal)
        {
            schema = schema.Normalise();
        }

        if (CollectionSchemaRules.Check(schema, kind) is { } broken)
        {
            return SchemaValidationResult.Invalid(InvalidSchema, broken.Rule, broken.Pointer, broken.Message);
        }

        return SchemaValidationResult.Valid(schema);
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
}

/// <summary>The outcome of <see cref="CollectionSchemaValidator.Validate(ReadOnlySpan{byte}, SchemaDocumentKind)"/>.</summary>
internal sealed record SchemaValidationResult
{
    private SchemaValidationResult()
    {
    }

    public bool IsValid => Code is null;

    /// <summary>The document, normalised when it was a proposal. Null on failure.</summary>
    public CollectionSchemaDocument? Document { get; private init; }

    /// <summary><c>cms.invalid_schema</c>, <c>cms.unsupported_schema_version</c> or <c>cms.proposal_too_large</c>.</summary>
    public string? Code { get; private init; }

    /// <summary>The rule of contract §5 that failed.</summary>
    public string? Rule { get; private init; }

    /// <summary>A JSON Pointer into the document; empty for the whole document.</summary>
    public string? Pointer { get; private init; }

    public string? Message { get; private init; }

    public static SchemaValidationResult Valid(CollectionSchemaDocument document) => new() { Document = document };

    public static SchemaValidationResult Invalid(string code, string rule, string pointer, string message) =>
        new() { Code = code, Rule = rule, Pointer = pointer, Message = message };
}
