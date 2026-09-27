using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickBuilder.Pages.Infrastructure.Schema;

/// <summary>
/// Checks a JSON value against a JSON Schema (2020-12) written in a declared subset of the
/// vocabulary (research P-09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why in-house.</b> The page schema must be one file both the product and the editor
/// validate against, and that file is JSON Schema. The complete .NET implementations either
/// carry a licence that would pass a fee on to operators (JsonSchema.Net) or compile schemas
/// with Roslyn at run time (Corvus). The page schema needs a small part of the vocabulary, so
/// this interprets exactly that part, and refuses at load time any schema that uses more —
/// it can never silently ignore a keyword it does not know. <c>PageSchemaSubsetTests</c>
/// makes the same check against the shipped file in the build.
/// </para>
/// <para>
/// <b>Supported</b>: <c>type</c>, <c>properties</c>, <c>required</c>,
/// <c>additionalProperties</c> (boolean or schema), <c>const</c>, <c>enum</c>, <c>pattern</c>,
/// <c>minLength</c>, <c>maxLength</c>, <c>minimum</c>, <c>maximum</c>, <c>items</c>,
/// <c>maxItems</c>, <c>format: uuid</c>, <c>$ref</c> to <c>#/$defs/…</c>, and <c>oneOf</c> whose
/// branches are told apart by a <c>type</c> property's <c>const</c>. Annotations
/// (<c>$schema</c>, <c>$id</c>, <c>title</c>, <c>description</c>, <c>$defs</c>) are read or
/// ignored as the specification says.
/// </para>
/// <para>
/// <b>First failure only.</b> Keywords are checked in a fixed order, and object properties in
/// document order, so a given document always reports the same failure and location.
/// </para>
/// </remarks>
internal sealed class SchemaSubsetValidator
{
    /// <summary>The keywords this validator interprets or knowingly ignores.</summary>
    internal static readonly IReadOnlySet<string> SupportedKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "$schema", "$id", "$defs", "$ref", "title", "description",
        "type", "properties", "required", "additionalProperties", "const", "enum",
        "pattern", "minLength", "maxLength", "minimum", "maximum",
        "items", "maxItems", "format", "oneOf",
    };

    private readonly JsonElement _root;
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);

    private SchemaSubsetValidator(JsonElement root) => _root = root;

    /// <summary>Loads a schema, refusing one that uses anything outside the subset.</summary>
    /// <exception cref="InvalidOperationException">The schema uses an unsupported keyword or form.</exception>
    public static SchemaSubsetValidator Load(Stream schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        using var document = JsonDocument.Parse(schema);
        var root = document.RootElement.Clone();

        foreach (var unsupported in UnsupportedKeywords(root).Take(1))
        {
            throw new InvalidOperationException(
                $"The schema uses '{unsupported.Keyword}' at {unsupported.Location}, which the page schema validator does not support (research P-09).");
        }

        var validator = new SchemaSubsetValidator(root);
        validator.CompilePatterns(root);
        return validator;
    }

    /// <summary>Every keyword outside the subset, with where it is. Used by the load check and the build guard.</summary>
    internal static IEnumerable<(string Keyword, string Location)> UnsupportedKeywords(JsonElement schema, string location = "")
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in schema.EnumerateObject())
        {
            var here = $"{location}/{Escape(property.Name)}";

            if (!SupportedKeywords.Contains(property.Name))
            {
                yield return (property.Name, here);
                continue;
            }

            if (property.Name == "format" && property.Value.GetString() != "uuid")
            {
                yield return ($"format: {property.Value.GetString()}", here);
            }

            if (property.Name == "$ref" && property.Value.GetString()?.StartsWith("#/$defs/", StringComparison.Ordinal) != true)
            {
                yield return ($"$ref: {property.Value.GetString()}", here);
            }

            var subschemas = property.Name switch
            {
                "properties" or "$defs" => property.Value.EnumerateObject().Select(p => (p.Value, $"{here}/{Escape(p.Name)}")),
                "items" or "additionalProperties" => [(property.Value, here)],
                "oneOf" => property.Value.EnumerateArray().Select((v, i) => (v, $"{here}/{i}")),
                _ => [],
            };

            foreach (var (subschema, subLocation) in subschemas)
            {
                foreach (var nested in UnsupportedKeywords(subschema, subLocation))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>The first failure of <paramref name="instance"/> against the schema, or null.</summary>
    public SchemaFailure? Validate(JsonElement instance) => Check(_root, instance, string.Empty);

    private SchemaFailure? Check(JsonElement schema, JsonElement instance, string pointer)
    {
        if (schema.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (schema.ValueKind == JsonValueKind.False)
        {
            return new SchemaFailure(pointer, "false", "No value is allowed here.");
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var failure = Check(Resolve(reference.GetString()!), instance, pointer);
            if (failure is not null)
            {
                return failure;
            }
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(type, instance))
        {
            return new SchemaFailure(pointer, "type", $"Expected {DescribeType(type)}.");
        }

        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, instance))
        {
            return new SchemaFailure(pointer, "const", $"Expected {constant.GetRawText()}.");
        }

        if (schema.TryGetProperty("enum", out var options) && !options.EnumerateArray().Any(o => JsonElement.DeepEquals(o, instance)))
        {
            return new SchemaFailure(pointer, "enum", "The value is not one of the allowed values.");
        }

        if (schema.TryGetProperty("oneOf", out var branches))
        {
            var failure = CheckOneOf(branches, instance, pointer);
            if (failure is not null)
            {
                return failure;
            }
        }

        return instance.ValueKind switch
        {
            JsonValueKind.Object => CheckObject(schema, instance, pointer),
            JsonValueKind.Array => CheckArray(schema, instance, pointer),
            JsonValueKind.String => CheckString(schema, instance, pointer),
            JsonValueKind.Number => CheckNumber(schema, instance, pointer),
            _ => null,
        };
    }

    /// <summary>
    /// <c>oneOf</c>, restricted to branches told apart by a <c>type</c> property's
    /// <c>const</c>: exactly one branch can match, so it is found by that value and checked.
    /// </summary>
    private SchemaFailure? CheckOneOf(JsonElement branches, JsonElement instance, string pointer)
    {
        var discriminator = instance.ValueKind == JsonValueKind.Object && instance.TryGetProperty("type", out var value)
            ? value
            : default;

        foreach (var branch in branches.EnumerateArray())
        {
            var resolved = branch.TryGetProperty("$ref", out var reference) ? Resolve(reference.GetString()!) : branch;
            if (resolved.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("type", out var typeSchema)
                && typeSchema.TryGetProperty("const", out var expected)
                && discriminator.ValueKind != JsonValueKind.Undefined
                && JsonElement.DeepEquals(expected, discriminator))
            {
                return Check(branch, instance, pointer);
            }
        }

        return new SchemaFailure(pointer, "discriminator", "The value matches none of the allowed kinds.");
    }

    private SchemaFailure? CheckObject(JsonElement schema, JsonElement instance, string pointer)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
            {
                if (!instance.TryGetProperty(name, out _))
                {
                    return new SchemaFailure(pointer, "required", $"'{name}' is required.");
                }
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties);
        var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);

        foreach (var property in instance.EnumerateObject())
        {
            var childPointer = $"{pointer}/{Escape(property.Name)}";

            if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                if (Check(propertySchema, property.Value, childPointer) is { } failure)
                {
                    return failure;
                }
            }
            else if (hasAdditional)
            {
                if (additional.ValueKind == JsonValueKind.False)
                {
                    return new SchemaFailure(childPointer, "additionalProperties", $"'{property.Name}' is not allowed here.");
                }

                if (Check(additional, property.Value, childPointer) is { } failure)
                {
                    return failure;
                }
            }
        }

        return null;
    }

    private SchemaFailure? CheckArray(JsonElement schema, JsonElement instance, string pointer)
    {
        if (schema.TryGetProperty("maxItems", out var maxItems) && instance.GetArrayLength() > maxItems.GetInt32())
        {
            return new SchemaFailure(pointer, "maxItems", $"At most {maxItems.GetInt32()} item(s) are allowed.");
        }

        if (schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in instance.EnumerateArray())
            {
                if (Check(items, item, $"{pointer}/{index}") is { } failure)
                {
                    return failure;
                }

                index++;
            }
        }

        return null;
    }

    private SchemaFailure? CheckString(JsonElement schema, JsonElement instance, string pointer)
    {
        var text = instance.GetString()!;

        // Length is in code points, as the specification says, not UTF-16 units.
        var length = text.EnumerateRunes().Count();

        if (schema.TryGetProperty("minLength", out var minLength) && length < minLength.GetInt32())
        {
            return new SchemaFailure(pointer, "minLength", $"At least {minLength.GetInt32()} character(s) are required.");
        }

        if (schema.TryGetProperty("maxLength", out var maxLength) && length > maxLength.GetInt32())
        {
            return new SchemaFailure(pointer, "maxLength", $"At most {maxLength.GetInt32()} character(s) are allowed.");
        }

        if (schema.TryGetProperty("pattern", out var pattern) && !_patterns[pattern.GetString()!].IsMatch(text))
        {
            return new SchemaFailure(pointer, "pattern", "The value does not have the required form.");
        }

        if (schema.TryGetProperty("format", out _) && !IsUuid(text))
        {
            return new SchemaFailure(pointer, "format", "Expected a UUID.");
        }

        return null;
    }

    private static SchemaFailure? CheckNumber(JsonElement schema, JsonElement instance, string pointer)
    {
        var number = instance.GetDecimal();

        if (schema.TryGetProperty("minimum", out var minimum) && number < minimum.GetDecimal())
        {
            return new SchemaFailure(pointer, "minimum", $"The value must be at least {minimum.GetRawText()}.");
        }

        if (schema.TryGetProperty("maximum", out var maximum) && number > maximum.GetDecimal())
        {
            return new SchemaFailure(pointer, "maximum", $"The value must be at most {maximum.GetRawText()}.");
        }

        return null;
    }

    private JsonElement Resolve(string reference)
    {
        var name = reference["#/$defs/".Length..];
        return _root.GetProperty("$defs").GetProperty(Unescape(name));
    }

    private static bool MatchesType(JsonElement type, JsonElement instance) =>
        type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(t => MatchesType(t.GetString()!, instance))
            : MatchesType(type.GetString()!, instance);

    private static bool MatchesType(string type, JsonElement instance) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && decimal.TryParse(instance.GetRawText(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && decimal.Truncate(n) == n,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static string DescribeType(JsonElement type) =>
        type.ValueKind == JsonValueKind.Array
            ? string.Join(" or ", type.EnumerateArray().Select(t => t.GetString()))
            : type.GetString()!;

    // 8-4-4-4-12 hexadecimal digits, the form RFC 9562 writes. Guid.TryParse alone would
    // also accept braces, parentheses and 32 digits without hyphens.
    private static bool IsUuid(string text) =>
        text.Length == 36 && Guid.TryParseExact(text, "D", out _);

    private void CompilePatterns(JsonElement schema)
    {
        switch (schema.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in schema.EnumerateObject())
                {
                    if (property.Name == "pattern" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var pattern = property.Value.GetString()!;
                        _patterns.TryAdd(pattern, new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                    }
                    else if (property.Name is not ("const" or "enum"))
                    {
                        CompilePatterns(property.Value);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in schema.EnumerateArray())
                {
                    CompilePatterns(item);
                }

                break;
        }
    }

    /// <summary>A JSON Pointer reference token (RFC 6901): <c>~</c> and <c>/</c> escaped.</summary>
    internal static string Escape(string token) => token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string Unescape(string token) => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}

/// <summary>Where a value first failed its schema, and which keyword it failed.</summary>
internal sealed record SchemaFailure(string Location, string Keyword, string Message);
