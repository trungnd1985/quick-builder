namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>
/// What a merge does with a document that matches one in the target, per kind and policy, and
/// how a renamed document is named (020 FR-023 … FR-026, research ST-10).
/// </summary>
/// <remarks>
/// Each part applies the names through its own module's creation rules, so a renamed document
/// is valid by construction; these helpers only produce the candidates, in order.
/// </remarks>
public static class ConflictRules
{
    /// <summary>The longest run of candidates tried before a rename gives up.</summary>
    public const int MaxAttempts = 1000;

    /// <summary>
    /// The outcome of a matched document. Redirect sources are unique by rule, and site settings
    /// and design tokens exist once per site, so neither can be renamed: rename treats them as
    /// skip, and the report says so.
    /// </summary>
    public static IdOutcome Outcome(string kind, ConflictPolicy policy) => policy switch
    {
        ConflictPolicy.Replace => IdOutcome.Replaced,
        ConflictPolicy.Skip => IdOutcome.Skipped,
        ConflictPolicy.Rename when !CanRename(kind) => IdOutcome.Skipped,
        ConflictPolicy.Rename => IdOutcome.Renamed,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    /// <summary>Whether a document of <paramref name="kind"/> can be added beside its match under a new name.</summary>
    public static bool CanRename(string kind) =>
        kind is not (ArchiveKinds.Redirect or ArchiveKinds.Design or ArchiveKinds.Site or ArchiveKinds.HostName);

    /// <summary>
    /// <c>slug-2</c>, <c>slug-3</c>… for pages, items and collection address names (010, 014,
    /// 015 rules): the first that <paramref name="taken"/> does not hold.
    /// </summary>
    public static string NextSlug(string slug, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(taken);
        for (var n = 2; n < MaxAttempts + 2; n++)
        {
            var candidate = $"{slug}-{n}";
            if (!taken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free name was found for '{slug}'.");
    }

    /// <summary>
    /// <c>Name (imported)</c>, then <c>Name (imported) 2</c>… for components, menus and collection
    /// display names: the first that <paramref name="taken"/> does not hold.
    /// </summary>
    public static string ImportedName(string name, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);
        var first = $"{name} (imported)";
        if (!taken(first))
        {
            return first;
        }

        for (var n = 2; n < MaxAttempts + 2; n++)
        {
            var candidate = $"{first} {n}";
            if (!taken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free name was found for '{name}'.");
    }
}
