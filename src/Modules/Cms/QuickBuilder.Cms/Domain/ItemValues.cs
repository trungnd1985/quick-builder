using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Contracts;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// One item's draft or published values: <c>{ schemaVersion, values }</c>, keyed by field id
/// (contract §3, research CS-03, FR-014).
/// </summary>
/// <remarks>
/// No key means no value; <c>null</c> is never stored, so "empty" has one form. Mutable, for
/// the schema migration, which rewrites values in place and writes the document back.
/// </remarks>
internal sealed class ItemValues
{
    private readonly JsonObject _values;

    private ItemValues(int schemaVersion, JsonObject values)
    {
        SchemaVersion = schemaVersion;
        _values = values;
    }

    public int SchemaVersion { get; }

    public static ItemValues Empty() => new(CollectionSchema.CurrentVersion, []);

    public static ItemValues Read(JsonElement document) =>
        new(
            document.GetProperty("schemaVersion").GetInt32(),
            JsonNode.Parse(document.GetProperty("values").GetRawText())!.AsObject());

    public static ItemValues Read(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Read(document.RootElement);
    }

    public IEnumerable<string> FieldIds => _values.Select(p => p.Key);

    public bool Has(string fieldId) => _values.ContainsKey(fieldId);

    /// <summary>The value of <paramref name="fieldId"/>, or null when the item has none.</summary>
    public JsonNode? Get(string fieldId) => _values.TryGetPropertyValue(fieldId, out var value) ? value : null;

    /// <summary>Sets a value; null removes it, because no key is how "no value" is stored.</summary>
    public void Set(string fieldId, JsonNode? value)
    {
        if (value is null)
        {
            _values.Remove(fieldId);
        }
        else
        {
            _values[fieldId] = value.Parent is null ? value : value.DeepClone();
        }
    }

    public JsonObject Write() => new()
    {
        ["schemaVersion"] = SchemaVersion,
        ["values"] = _values.DeepClone(),
    };

    public JsonDocument ToDocument() => JsonDocument.Parse(Write().ToJsonString());
}
