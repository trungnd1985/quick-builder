using System.Globalization;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// The forms of field and option ids (research CS-02): permanent <c>f{n}</c> and <c>o{n}</c>,
/// and the temporary <c>~{n}</c> a proposal uses for what it adds.
/// </summary>
internal static class SchemaIds
{
    public const char TemporaryPrefix = '~';

    public static bool IsTemporary(string id) => id.Length > 1 && id[0] == TemporaryPrefix;

    public static string Field(int number) => "f" + number.ToString(CultureInfo.InvariantCulture);

    public static string Option(int number) => "o" + number.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Case-insensitive comparison of keys and labels, character by character with the invariant
/// simple case mapping, so the admin (<c>collectionSchemaRules.ts</c>) gets the same answer.
/// </summary>
internal static class Fold
{
    public static string Case(string text) => text.ToUpperInvariant();

    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;
}
