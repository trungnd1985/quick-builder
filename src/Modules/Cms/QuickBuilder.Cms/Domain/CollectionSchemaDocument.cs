using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Contracts;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// A collection's schema document (contract §2): its fields in order, and which field names
/// the collection's items.
/// </summary>
/// <remarks>
/// The same type holds a stored schema and a proposal; only a proposal may hold temporary ids
/// (contract §6), which <see cref="IdAssignment"/> replaces before anything is stored.
/// </remarks>
internal sealed record CollectionSchemaDocument
{
    public static CollectionSchemaDocument Empty { get; } = new() { Fields = [] };

    public int SchemaVersion { get; init; } = CollectionSchema.CurrentVersion;

    public string? TitleFieldId { get; init; }

    public required IReadOnlyList<Field> Fields { get; init; }

    public Field? Find(string id) => Fields.FirstOrDefault(f => f.Id == id);

    public Field? TitleField => TitleFieldId is null ? null : Find(TitleFieldId);

    /// <summary>Reads a document the contract's schema has accepted.</summary>
    public static CollectionSchemaDocument Read(JsonElement element) => new()
    {
        SchemaVersion = element.GetProperty("schemaVersion").GetInt32(),
        TitleFieldId = element.TryGetProperty("titleFieldId", out var title) ? title.GetString() : null,
        Fields = [.. element.GetProperty("fields").EnumerateArray().Select(Field.Read)],
    };

    public static CollectionSchemaDocument Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    /// <summary>Writes the document in the contract's canonical property order.</summary>
    public JsonObject Write()
    {
        var json = new JsonObject { ["schemaVersion"] = SchemaVersion };
        if (TitleFieldId is not null)
        {
            json["titleFieldId"] = TitleFieldId;
        }

        json["fields"] = new JsonArray([.. Fields.Select(f => (JsonNode)f.Write())]);
        return json;
    }

    public string ToJson() => Write().ToJsonString();

    /// <summary>
    /// What the server does to every proposal before the rules run (contract §5): with no
    /// <c>titleFieldId</c>, the first single-line text field names the items; the title field
    /// is always required (FR-011).
    /// </summary>
    public CollectionSchemaDocument Normalise()
    {
        var titleId = TitleFieldId ?? Fields.FirstOrDefault(f => f.IsSingleLineText)?.Id;
        if (titleId is null)
        {
            return this;
        }

        return this with
        {
            TitleFieldId = titleId,
            Fields = [.. Fields.Select(f => f.Id == titleId && f.IsSingleLineText && !f.Required ? f with { Required = true } : f)],
        };
    }
}
