using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// One field of a collection schema: the common properties and its type's settings
/// (contract §2, research CS-02).
/// </summary>
/// <remarks>
/// Settings that do not belong to <see cref="Type"/> keep their defaults and are never
/// written. Instances are read only from documents that have passed the contract's schema,
/// so the reader trusts shapes and only maps them.
/// </remarks>
internal sealed record Field
{
    public required string Id { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public string Help { get; init; } = string.Empty;

    public bool Required { get; init; }

    public required FieldType Type { get; init; }

    /// <summary>Text: several lines allowed.</summary>
    public bool Multiline { get; init; }

    /// <summary>Text: the most characters, in code points.</summary>
    public int MaxLength { get; init; }

    /// <summary>Number: decimal places; 0 is a whole number.</summary>
    public int Decimals { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    /// <summary>Date: a moment (UTC) rather than a calendar date.</summary>
    public bool WithTime { get; init; }

    /// <summary>File: <see cref="FileAccept"/>.</summary>
    public string Accept { get; init; } = FileAccept.Any;

    /// <summary>Option: the choices, in order.</summary>
    public IReadOnlyList<FieldOption> Options { get; init; } = [];

    /// <summary>Reference and multi-reference: the target collection.</summary>
    public Guid? Target { get; init; }

    /// <summary>Rich text: the format groups it allows (035 FR-022); all by default.</summary>
    public RichTextFormats Formats { get; init; } = RichTextFormats.All;

    /// <summary>Whether this field can name a collection's items (FR-011).</summary>
    public bool IsSingleLineText => Type == FieldType.Text && !Multiline;

    public FieldOption? FindOption(string id) => Options.FirstOrDefault(o => o.Id == id);

    /// <summary>Reads a field the contract's schema has accepted.</summary>
    public static Field Read(JsonElement element)
    {
        var type = FieldTypes.Parse(element.GetProperty("type").GetString()!);

        return new Field
        {
            Id = element.GetProperty("id").GetString()!,
            Key = element.GetProperty("key").GetString()!,
            Name = element.GetProperty("name").GetString()!,
            Help = element.TryGetProperty("help", out var help) ? help.GetString()! : string.Empty,
            Required = element.GetProperty("required").GetBoolean(),
            Type = type,
            Multiline = type == FieldType.Text && element.GetProperty("multiline").GetBoolean(),
            MaxLength = type == FieldType.Text ? element.GetProperty("maxLength").GetInt32() : 0,
            Decimals = type == FieldType.Number ? element.GetProperty("decimals").GetInt32() : 0,
            Min = type == FieldType.Number && element.TryGetProperty("min", out var min) ? min.GetDecimal() : null,
            Max = type == FieldType.Number && element.TryGetProperty("max", out var max) ? max.GetDecimal() : null,
            WithTime = type == FieldType.Date && element.GetProperty("withTime").GetBoolean(),
            Accept = type == FieldType.File ? element.GetProperty("accept").GetString()! : FileAccept.Any,
            Options = type == FieldType.Option
                ? [.. element.GetProperty("options").EnumerateArray().Select(o => new FieldOption(o.GetProperty("id").GetString()!, o.GetProperty("label").GetString()!))]
                : [],
            Target = type is FieldType.Reference or FieldType.MultiReference ? element.GetProperty("target").GetGuid() : null,
            Formats = type == FieldType.RichText && element.TryGetProperty("formats", out var formats) ? RichTextFormats.Read(formats) : RichTextFormats.All,
        };
    }

    /// <summary>Writes the field in the contract's canonical property order.</summary>
    public JsonObject Write()
    {
        var json = new JsonObject
        {
            ["id"] = Id,
            ["key"] = Key,
            ["name"] = Name,
            ["help"] = Help,
            ["required"] = Required,
            ["type"] = Type.ToName(),
        };

        switch (Type)
        {
            case FieldType.Text:
                json["multiline"] = Multiline;
                json["maxLength"] = MaxLength;
                break;
            case FieldType.Number:
                json["decimals"] = Decimals;
                if (Min is { } min)
                {
                    json["min"] = min;
                }

                if (Max is { } max)
                {
                    json["max"] = max;
                }

                break;
            case FieldType.Date:
                json["withTime"] = WithTime;
                break;
            case FieldType.File:
                json["accept"] = Accept;
                break;
            case FieldType.Option:
                json["options"] = new JsonArray([.. Options.Select(o => (JsonNode)new JsonObject { ["id"] = o.Id, ["label"] = o.Label })]);
                break;
            case FieldType.Reference or FieldType.MultiReference:
                json["target"] = Target!.Value.ToString();
                break;
            case FieldType.RichText when Formats != RichTextFormats.All:
                json["formats"] = new JsonObject { ["headings"] = Formats.Headings, ["images"] = Formats.Images, ["tables"] = Formats.Tables };
                break;
        }

        return json;
    }

    /// <summary>Two fields are the same definition, options and their order included.</summary>
    public bool SameDefinitionAs(Field other) =>
        this with { Options = [] } == other with { Options = [] } && Options.SequenceEqual(other.Options);
}

/// <summary>
/// The format groups a rich text field allows (035 FR-022, collection-schema.md §2): headings
/// and quotes, images, tables. A missing group, like a missing setting, is allowed.
/// </summary>
internal sealed record RichTextFormats(bool Headings = true, bool Images = true, bool Tables = true)
{
    public static RichTextFormats All { get; } = new();

    public static RichTextFormats Read(JsonElement formats) => new(
        !formats.TryGetProperty("headings", out var headings) || headings.GetBoolean(),
        !formats.TryGetProperty("images", out var images) || images.GetBoolean(),
        !formats.TryGetProperty("tables", out var tables) || tables.GetBoolean());

    /// <summary>Whether <paramref name="after"/> disallows a group this allows.</summary>
    public bool NarrowedBy(RichTextFormats after)
    {
        ArgumentNullException.ThrowIfNull(after);
        return (Headings && !after.Headings) || (Images && !after.Images) || (Tables && !after.Tables);
    }

    /// <summary>The allowed groups, by name, for a preview and history: "headings images tables".</summary>
    public override string ToString() =>
        string.Join(" ", new[] { Headings ? "headings" : null, Images ? "images" : null, Tables ? "tables" : null }.OfType<string>());
}
