using QuickBuilder.Pages.Contracts;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// The slug, title and name a copy gets (023 FR-020, research CE-11): the original's slug with
/// <c>-copy</c>, then <c>-copy-2</c> and so on; the member's localised title or name, made unique
/// with a number when another component has it.
/// </summary>
internal static class CopyNaming
{
    private const string SlugSuffix = "-copy";

    /// <summary>The first free copy slug among <paramref name="taken"/>, within the slug limit.</summary>
    public static string Slug(string original, IReadOnlyCollection<string> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);

        var used = taken.ToHashSet(StringComparer.Ordinal);
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? SlugSuffix : $"{SlugSuffix}-{n}";
            var stem = original.Length + suffix.Length > PageAddresses.MaxSlugLength
                ? original[..(PageAddresses.MaxSlugLength - suffix.Length)].TrimEnd('-')
                : original;
            var candidate = stem + suffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// <paramref name="original"/>, or with <c>-2</c>, <c>-3</c>… for the first one
    /// <paramref name="isFree"/> accepts, within the slug limit (021 FR-017: a restored page
    /// whose address is now taken).
    /// </summary>
    public static string NumberedSlug(string original, Func<string, bool> isFree)
    {
        ArgumentNullException.ThrowIfNull(isFree);

        if (isFree(original))
        {
            return original;
        }

        for (var n = 2; ; n++)
        {
            var suffix = $"-{n}";
            var stem = original.Length + suffix.Length > PageAddresses.MaxSlugLength
                ? original[..(PageAddresses.MaxSlugLength - suffix.Length)].TrimEnd('-')
                : original;
            var candidate = stem + suffix;
            if (isFree(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>A trimmed title or name, cut to <paramref name="maxLength"/>; the fallback when nothing is left.</summary>
    public static string Text(string? text, int maxLength, string fallback = "Copy")
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return fallback;
        }

        return trimmed.Length > maxLength ? trimmed[..maxLength].TrimEnd() : trimmed;
    }

    /// <summary>
    /// <paramref name="name"/>, or with " 2", " 3"… when another one has it, ignoring case (component
    /// names are unique per site that way, 010 FR-024).
    /// </summary>
    public static string Unique(string name, IReadOnlyCollection<string> taken, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(taken);

        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name))
        {
            return name;
        }

        for (var n = 2; ; n++)
        {
            var suffix = $" {n}";
            var stem = name.Length + suffix.Length > maxLength ? name[..(maxLength - suffix.Length)].TrimEnd() : name;
            var candidate = stem + suffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
