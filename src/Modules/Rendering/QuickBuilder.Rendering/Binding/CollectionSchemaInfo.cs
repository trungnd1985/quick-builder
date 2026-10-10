using System.Collections.Frozen;
using System.Text.Json;

namespace QuickBuilder.Rendering.Binding;

/// <summary>
/// What the renderer needs to know about a collection's fields: their types and the settings
/// that change how a value reads (016 research TB-09; collection-schema.md §2).
/// </summary>
/// <remarks>
/// Read from the current schema document Cms hands out. The document has passed the
/// collection schema contract, so anything unexpected is ignored rather than trusted: a field
/// it cannot read is simply absent, and a binding to it is broken.
/// </remarks>
internal sealed class CollectionSchemaInfo
{
    private CollectionSchemaInfo(Guid collectionId, FrozenDictionary<string, FieldInfo> fields, string? titleFieldId)
    {
        CollectionId = collectionId;
        Fields = fields;
        TitleFieldId = titleFieldId;
    }

    public Guid CollectionId { get; }

    public FrozenDictionary<string, FieldInfo> Fields { get; }

    /// <summary>The field that names items (014 FR-011), if the collection has one.</summary>
    public string? TitleFieldId { get; }

    public FieldInfo? Field(string? id) => id is not null && Fields.TryGetValue(id, out var field) ? field : null;

    public static CollectionSchemaInfo Parse(Guid collectionId, JsonElement schema)
    {
        var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("fields", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in list.EnumerateArray())
            {
                if (FieldInfo.Read(field) is { } info)
                {
                    fields[info.Id] = info;
                }
            }
        }

        var title = schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("titleFieldId", out var titleId)
            && titleId.ValueKind == JsonValueKind.String
                ? titleId.GetString()
                : null;

        return new CollectionSchemaInfo(collectionId, fields.ToFrozenDictionary(StringComparer.Ordinal), title);
    }
}

/// <summary>One field: its id, type, and the settings that affect how its value reads.</summary>
/// <param name="Type">The collection schema type name: <c>text</c>, <c>richText</c>, <c>number</c>, …</param>
/// <param name="Options">An option field's labels by option id; empty for other types.</param>
/// <param name="Target">A reference field's target collection.</param>
internal sealed record FieldInfo(
    string Id,
    string Type,
    bool Multiline,
    bool WithTime,
    int Decimals,
    FrozenDictionary<string, string> Options,
    Guid? Target)
{
    public const string Text = "text";
    public const string RichText = "richText";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string Image = "image";
    public const string File = "file";
    public const string Option = "option";
    public const string Reference = "reference";
    public const string MultiReference = "multiReference";

    public static FieldInfo? Read(JsonElement field)
    {
        if (field.ValueKind != JsonValueKind.Object
            || !field.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || !field.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (field.TryGetProperty("options", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in list.EnumerateArray())
            {
                if (option.TryGetProperty("id", out var optionId) && optionId.ValueKind == JsonValueKind.String
                    && option.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
                {
                    options[optionId.GetString()!] = label.GetString()!;
                }
            }
        }

        return new FieldInfo(
            id.GetString()!,
            type.GetString()!,
            Multiline: field.TryGetProperty("multiline", out var multiline) && multiline.ValueKind == JsonValueKind.True,
            WithTime: field.TryGetProperty("withTime", out var withTime) && withTime.ValueKind == JsonValueKind.True,
            Decimals: field.TryGetProperty("decimals", out var decimals) && decimals.TryGetInt32(out var places) ? places : 0,
            Options: options.ToFrozenDictionary(StringComparer.Ordinal),
            Target: field.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String && Guid.TryParse(target.GetString(), out var targetId)
                ? targetId
                : null);
    }
}
