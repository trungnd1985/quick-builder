using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure.Schema;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>What one stored value is, measured against its field (contract §3).</summary>
internal enum ValueState
{
    /// <summary>Well-formed and meets the field's settings.</summary>
    Valid,

    /// <summary>No value, and the field does not require one.</summary>
    Empty,

    /// <summary>No value, and the field requires one (FR-029).</summary>
    MissingRequired,

    /// <summary>Well-formed, but breaks the field's settings; kept and reported, never rewritten (FR-027).</summary>
    NeedsAttention,

    /// <summary>Not the shape its field type's definition gives. Never stored by this product.</summary>
    Malformed,
}

/// <summary>
/// Checks item values against their fields: the shape through the contract's value
/// definitions, rich text through the page schema's <c>rtDoc</c>, then the field's settings
/// (research CS-03, CS-04).
/// </summary>
/// <remarks>
/// Whether a media file or a referenced item still exists needs the database, so it is not
/// decided here; such a value is <see cref="ValueState.Valid"/> in shape. Stateless.
/// </remarks>
internal sealed class ItemValueValidator(CollectionSchemaValidator schemas)
{
    private readonly SchemaSubsetValidator _pageSchema = LoadPageSchema();

    public ValueState Check(Field field, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (value is null)
        {
            // A boolean with no value is false (FR-010), so it is never missing.
            return field.Required && field.Type != FieldType.Boolean ? ValueState.MissingRequired : ValueState.Empty;
        }

        if (!IsWellFormed(field, value))
        {
            return ValueState.Malformed;
        }

        return MeetsSettings(field, value) ? ValueState.Valid : ValueState.NeedsAttention;
    }

    public bool IsWellFormed(Field field, JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var element = JsonSerializer.SerializeToElement(value);

        if (field.Type == FieldType.RichText)
        {
            return _pageSchema.ValidateDefinition("rtDoc", element) is null
                && RichText.CharacterCount(element) <= PageSchema.MaxRichTextChars;
        }

        if (schemas.SchemaFor(CollectionSchema.CurrentVersion).ValidateDefinition(field.Type.ValueDefinition(field.WithTime), element) is not null)
        {
            return false;
        }

        return field.Type switch
        {
            FieldType.Date when field.WithTime => DateTimeOffset.TryParseExact(element.GetString(), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _),
            FieldType.Date => DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => true,
        };
    }

    private static bool MeetsSettings(Field field, JsonNode value)
    {
        switch (field.Type)
        {
            case FieldType.Text:
                var text = value.GetValue<string>();
                return text.EnumerateRunes().Count() <= field.MaxLength
                    && (field.Multiline || text.IndexOfAny(['\n', '\r']) < 0);
            case FieldType.Number:
                var number = value.GetValue<decimal>();
                return decimal.Round(number, field.Decimals) == number
                    && (field.Min is not { } min || number >= min)
                    && (field.Max is not { } max || number <= max);
            case FieldType.Option:
                return field.FindOption(value.GetValue<string>()) is not null;
            case FieldType.MultiReference:
                var ids = value.AsArray().Select(n => n!.GetValue<string>()).ToList();
                return ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count;
            default:
                return true;
        }
    }

    private static SchemaSubsetValidator LoadPageSchema()
    {
        using var stream = PageSchema.OpenSchema(PageSchema.CurrentVersion);
        return SchemaSubsetValidator.Load(stream);
    }
}

/// <summary>Reading rich text values (the page schema's <c>rtDoc</c>).</summary>
internal static class RichText
{
    /// <summary>Characters, in code points, across every text node (page-schema.md §3 rule 9).</summary>
    public static int CharacterCount(JsonElement node)
    {
        var count = 0;
        Walk(node, ref count);
        return count;

        static void Walk(JsonElement element, ref int count)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (element.TryGetProperty("type", out var type) && type.ValueEquals("text")
                && element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                count += text.GetString()!.EnumerateRunes().Count();
            }

            if (element.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in content.EnumerateArray())
                {
                    Walk(child, ref count);
                }
            }
        }
    }
}
