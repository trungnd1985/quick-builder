using System.Text.Json;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Contracts.Schema;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Checks a design token set against its contract, in the order docs/contracts/design-tokens.md
/// §2 gives (011 research ED-14, data-model §2).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Size</b>, on the bytes as received, before anything is parsed.</item>
/// <item><b>Well-formed JSON</b> without duplicate property names, which parsers disagree about.</item>
/// <item><b>A supported <c>schemaVersion</c></b>. A missing or malformed one is left to the schema.</item>
/// <item><b>No empty list</b>, before the schema, so deleting the last colour, text size or spacing
/// step or radius gets its own reason rather than a generic one.</item>
/// <item><b>The schema</b>, through the shared <see cref="SchemaSubsetValidator"/> over the embedded file.</item>
/// <item><b>Ids unique across all lists</b>, then <b>names unique within a list</b> ignoring case —
/// rules the schema cannot say.</item>
/// <item><b>Content widths in order</b>, Narrow ≤ Normal ≤ Wide, when the set has them.</item>
/// </list>
/// Stops at the first failure. Messages never quote the token set (011 FR-045).
/// </remarks>
internal sealed class TokenSetValidator
{
    private static readonly (string Pointer, string[] Path)[] Lists =
    [
        ("/colors", ["colors"]),
        ("/typography/sizes", ["typography", "sizes"]),
        ("/spacing", ["spacing"]),

        // Optional (066): a set without radii uses the default set's, so an absent list is skipped.
        ("/radii", ["radii"]),
    ];

    private readonly Dictionary<int, SchemaSubsetValidator> _schemas;

    public TokenSetValidator()
    {
        _schemas = DesignTokens.SupportedVersions.ToDictionary(
            v => v,
            v =>
            {
                using var stream = DesignTokens.OpenSchema(v);
                return SchemaSubsetValidator.Load(stream);
            });
    }

    /// <summary>The pointer a refusal carries, or null.</summary>
    public static string? PointerOf(Error error) =>
        EditorErrors.ExtensionsOf(error)?.GetValueOrDefault("pointer") as string;

    /// <summary>Validates a token set. On success, the version it was written in.</summary>
    public Result<int> Validate(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > DesignTokens.MaxDocumentBytes)
        {
            return Result.Failure<int>(EditorErrors.TooLarge);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (JsonException)
        {
            return Result.Failure<int>(EditorErrors.InvalidTokens(string.Empty, "It is not well-formed JSON."));
        }

        using (document)
        {
            return Validate(document.RootElement);
        }
    }

    private Result<int> Validate(JsonElement root)
    {
        if (FindDuplicateProperty(root, string.Empty) is { } duplicate)
        {
            return Result.Failure<int>(EditorErrors.InvalidTokens(duplicate, "A property name appears twice in one object."));
        }

        var version = DesignTokens.CurrentVersion;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("schemaVersion", out var declared)
            && declared.ValueKind == JsonValueKind.Number
            && declared.TryGetInt32(out var declaredVersion))
        {
            if (!DesignTokens.SupportedVersions.Contains(declaredVersion))
            {
                return Result.Failure<int>(EditorErrors.UnsupportedSchemaVersion);
            }

            version = declaredVersion;
        }

        foreach (var (pointer, path) in Lists)
        {
            if (At(root, path) is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() == 0)
            {
                return Result.Failure<int>(EditorErrors.TokenRequired(pointer));
            }
        }

        if (_schemas[version].Validate(root) is { } failure)
        {
            return Result.Failure<int>(EditorErrors.InvalidTokens(failure.Location, failure.Message));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pointer, path) in Lists)
        {
            if (At(root, path) is not { ValueKind: JsonValueKind.Array } entries)
            {
                continue;
            }

            var index = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!ids.Add(entry.GetProperty("id").GetString()!))
                {
                    return Result.Failure<int>(EditorErrors.DuplicateTokenId($"{pointer}/{index}/id"));
                }

                index++;
            }
        }

        foreach (var (pointer, path) in Lists)
        {
            if (At(root, path) is not { ValueKind: JsonValueKind.Array } entries)
            {
                continue;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!names.Add(entry.GetProperty("name").GetString()!))
                {
                    return Result.Failure<int>(EditorErrors.DuplicateTokenName($"{pointer}/{index}/name"));
                }

                index++;
            }
        }

        if (At(root, ["contentWidths"]) is { } widths
            && !(widths.GetProperty("narrow").GetInt32() <= widths.GetProperty("normal").GetInt32()
                && widths.GetProperty("normal").GetInt32() <= widths.GetProperty("wide").GetInt32()))
        {
            return Result.Failure<int>(EditorErrors.ContentWidthsOrder);
        }

        return Result.Success(version);
    }

    private static JsonElement? At(JsonElement root, string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current;
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
