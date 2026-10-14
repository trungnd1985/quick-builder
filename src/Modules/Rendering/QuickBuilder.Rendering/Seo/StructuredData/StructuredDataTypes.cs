using System.Collections.Frozen;
using System.Text.Json;
using QuickBuilder.Rendering.Binding;

namespace QuickBuilder.Rendering.Seo.StructuredData;

/// <summary>One property of a content type (structured-data.md §3).</summary>
/// <param name="Accepts">What may fill it: field type names, built-in values and <c>template</c>.</param>
/// <param name="Default">What it takes when unbound or empty: <c>name</c>, <c>firstPublishedAt</c>, <c>lastPublishedAt</c>, or null.</param>
internal sealed record StructuredDataProperty(string Name, bool Required, FrozenSet<string> Accepts, string? Default);

/// <summary>A content type an item page can describe itself as.</summary>
internal sealed record StructuredDataType(string Name, IReadOnlyList<StructuredDataProperty> Properties)
{
    public StructuredDataProperty? Property(string name) => Properties.FirstOrDefault(p => p.Name == name);
}

/// <summary>
/// The content types of <c>docs/contracts/structured-data/types.json</c>, embedded from the
/// contract's own file (017 research SE-19). The editor loads the same file.
/// </summary>
internal static class StructuredDataTypes
{
    private static readonly Lazy<FrozenDictionary<string, StructuredDataType>> Loaded = new(Load);

    /// <summary>Every type, by name.</summary>
    public static FrozenDictionary<string, StructuredDataType> All => Loaded.Value;

    /// <summary>What a field of this type offers, in the contract's accept names; null when nothing accepts it.</summary>
    public static string? AcceptOf(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Type switch
        {
            FieldInfo.Text => "text",
            FieldInfo.RichText => "richText",
            FieldInfo.Option => "option",
            FieldInfo.Number => "number",
            FieldInfo.Date => "date",
            FieldInfo.Boolean => "boolean",
            FieldInfo.Image => "image",
            FieldInfo.Reference => "reference",
            _ => null,
        };
    }

    private static FrozenDictionary<string, StructuredDataType> Load()
    {
        using var stream = typeof(StructuredDataTypes).Assembly.GetManifestResourceStream("QuickBuilder.Rendering.StructuredData.types.json")
            ?? throw new InvalidOperationException("The structured data types are not embedded in the Rendering assembly.");
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("types").EnumerateObject()
            .Select(type => new StructuredDataType(
                type.Name,
                [.. type.Value.GetProperty("properties").EnumerateArray().Select(p => new StructuredDataProperty(
                    p.GetProperty("name").GetString()!,
                    p.TryGetProperty("required", out var required) && required.GetBoolean(),
                    p.GetProperty("accepts").EnumerateArray().Select(a => a.GetString()!).ToFrozenSet(StringComparer.Ordinal),
                    p.TryGetProperty("default", out var fallback) ? fallback.GetString() : null))]))
            .ToFrozenDictionary(t => t.Name, StringComparer.Ordinal);
    }
}
