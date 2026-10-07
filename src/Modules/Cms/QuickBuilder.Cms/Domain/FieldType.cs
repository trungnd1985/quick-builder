namespace QuickBuilder.Cms.Domain;

/// <summary>The kinds of field a collection schema may hold (FR-009, contract §2).</summary>
internal enum FieldType
{
    Text,
    RichText,
    Number,
    Boolean,
    Date,
    Image,
    File,
    Option,
    Reference,
    MultiReference,
}

/// <summary>The contract's names for <see cref="FieldType"/>, the <c>type</c> of each field.</summary>
internal static class FieldTypes
{
    private static readonly Dictionary<string, FieldType> ByName = new(StringComparer.Ordinal)
    {
        ["text"] = FieldType.Text,
        ["richText"] = FieldType.RichText,
        ["number"] = FieldType.Number,
        ["boolean"] = FieldType.Boolean,
        ["date"] = FieldType.Date,
        ["image"] = FieldType.Image,
        ["file"] = FieldType.File,
        ["option"] = FieldType.Option,
        ["reference"] = FieldType.Reference,
        ["multiReference"] = FieldType.MultiReference,
    };

    private static readonly Dictionary<FieldType, string> NameOf = ByName.ToDictionary(p => p.Value, p => p.Key);

    public static FieldType Parse(string name) =>
        ByName.TryGetValue(name, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(name), name, "Not a field type of the collection schema.");

    public static string ToName(this FieldType type) => NameOf[type];

    /// <summary>Which fill-in values a type takes when a field is added or made required (FR-031).</summary>
    public static bool TakesFillIn(this FieldType type) =>
        type is FieldType.Text or FieldType.Number or FieldType.Boolean or FieldType.Date or FieldType.Option;

    /// <summary>The value definition in the contract file for this type (contract §3).</summary>
    public static string ValueDefinition(this FieldType type, bool withTime) => type switch
    {
        FieldType.Text => "textValue",
        FieldType.Number => "numberValue",
        FieldType.Boolean => "booleanValue",
        FieldType.Date => withTime ? "dateTimeValue" : "dateValue",
        FieldType.Image or FieldType.File => "mediaValue",
        FieldType.Option => "optionValue",
        FieldType.Reference => "referenceValue",
        FieldType.MultiReference => "multiReferenceValue",
        FieldType.RichText => throw new InvalidOperationException("Rich text values are checked against the page schema's rtDoc."),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>What a file field accepts (contract §2).</summary>
internal static class FileAccept
{
    public const string Any = "any";
    public const string Documents = "documents";
    public const string Images = "images";
}
