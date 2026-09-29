using System.Collections.Frozen;
using System.Text.Json;
using QuickBuilder.Pages.Contracts;

namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// Every block property's default, read from the page schema (012 research PR-09; 011 ED-07).
/// </summary>
/// <remarks>
/// The schema is the one definition of a block type (constitution Principle VI): the editor
/// reads these same defaults from the same file, so a missing property renders exactly as the
/// canvas shows it. A default may sit on the property or on the definition it references
/// (<c>align</c> is <c>start</c> through <c>#/$defs/align</c>).
/// </remarks>
internal sealed class BlockDefaults
{
    private readonly FrozenDictionary<string, FrozenDictionary<string, JsonElement>> _defaults;

    private BlockDefaults(FrozenDictionary<string, FrozenDictionary<string, JsonElement>> defaults) => _defaults = defaults;

    /// <summary>The defaults of the current schema version, loaded once.</summary>
    public static BlockDefaults Current { get; } = Load(PageSchema.CurrentVersion);

    /// <summary>The node types the schema defines, with their defaults (possibly none).</summary>
    public IEnumerable<string> Types => _defaults.Keys;

    /// <summary>The default of <paramref name="property"/> on <paramref name="type"/>, if it has one.</summary>
    public JsonElement? Of(string type, string property) =>
        _defaults.TryGetValue(type, out var properties) && properties.TryGetValue(property, out var value) ? value : null;

    public static BlockDefaults Load(int schemaVersion)
    {
        using var stream = PageSchema.OpenSchema(schemaVersion);
        using var schema = JsonDocument.Parse(stream);
        var definitions = schema.RootElement.GetProperty("$defs");

        var result = new Dictionary<string, FrozenDictionary<string, JsonElement>>(StringComparer.Ordinal);

        foreach (var definition in definitions.EnumerateObject())
        {
            // A node type is a definition whose "type" property is a constant naming it.
            if (!definition.Value.TryGetProperty("properties", out var fields)
                || !fields.TryGetProperty("type", out var typeField)
                || !typeField.TryGetProperty("const", out var typeName)
                || typeName.ValueKind != JsonValueKind.String
                || !fields.TryGetProperty("props", out var props))
            {
                continue;
            }

            var defaults = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            if (props.TryGetProperty("properties", out var propertyList))
            {
                foreach (var property in propertyList.EnumerateObject())
                {
                    if (DefaultOf(property.Value, definitions) is { } value)
                    {
                        defaults[property.Name] = value.Clone();
                    }
                }
            }

            result[typeName.GetString()!] = defaults.ToFrozenDictionary(StringComparer.Ordinal);
        }

        return new BlockDefaults(result.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static JsonElement? DefaultOf(JsonElement property, JsonElement definitions)
    {
        if (property.TryGetProperty("default", out var value))
        {
            return value;
        }

        // One level of $ref is all the schema uses for defaults (align, colorRef, …).
        if (property.TryGetProperty("$ref", out var reference)
            && reference.GetString() is { } pointer
            && pointer.StartsWith("#/$defs/", StringComparison.Ordinal)
            && definitions.TryGetProperty(pointer["#/$defs/".Length..], out var target)
            && target.TryGetProperty("default", out var referenced))
        {
            return referenced;
        }

        return null;
    }
}
