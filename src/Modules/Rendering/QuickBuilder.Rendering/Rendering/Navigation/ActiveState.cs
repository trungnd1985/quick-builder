namespace QuickBuilder.Rendering.Rendering.Navigation;

/// <summary>The page being rendered, as active state compares it (024 research NV-11).</summary>
/// <param name="PageId">The page, or for an item page its template; never matches a template entry, which is never live.</param>
/// <param name="ItemId">The item of an item page; null otherwise.</param>
/// <param name="Path">The address being rendered, within the site, without query or fragment.</param>
internal sealed record CurrentPage(Guid? PageId, Guid? ItemId, string Path);

/// <summary>
/// Marks which entries are the current page, which contain it, and which is the current section
/// (024 FR-027 to FR-030, research NV-11).
/// </summary>
/// <remarks>
/// A pure function of the resolved entries and the current page, run on the whole tree before
/// the block's levels are applied, so an entry below the shown levels still marks its ancestors.
/// <list type="number">
/// <item><b>Current</b>: every linked entry whose page or item is the one rendered (the same
/// target twice marks both). An anchor on the page does not matter.</item>
/// <item><b>Containing</b>: every ancestor of a current entry.</item>
/// <item><b>Section</b>, only when nothing is current: the linked page entry, other than the
/// home page, whose address is the longest prefix of the current address, segment by segment;
/// its ancestors are containing.</item>
/// <item>Addresses, anchors and grouping entries are never current. Query strings never count:
/// the current address has none.</item>
/// </list>
/// </remarks>
internal static class ActiveState
{
    public static void Mark(IReadOnlyList<ResolvedEntry> entries, CurrentPage current)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(current);

        if (MarkCurrent(entries, current))
        {
            return;
        }

        var best = default(ResolvedEntry);
        var bestLength = -1;
        FindSection(entries, current.Path, ref best, ref bestLength);
        if (best is not null)
        {
            MarkPath(entries, best);
        }
    }

    private static bool MarkCurrent(IReadOnlyList<ResolvedEntry> entries, CurrentPage current)
    {
        var any = false;
        foreach (var entry in entries)
        {
            var childCurrent = MarkCurrent(entry.Children, current);
            if (IsCurrent(entry, current))
            {
                entry.State = NavState.Current;
                any = true;
            }
            else if (childCurrent)
            {
                entry.State = NavState.Containing;
            }

            any |= childCurrent;
        }

        return any;
    }

    private static bool IsCurrent(ResolvedEntry entry, CurrentPage current) =>
        entry.Href is not null && !entry.External
        && ((current.ItemId is { } itemId && entry.ItemId == itemId)
            || (current.ItemId is null && current.PageId is { } pageId && entry.PageId == pageId));

    private static void FindSection(IReadOnlyList<ResolvedEntry> entries, string currentPath, ref ResolvedEntry? best, ref int bestLength)
    {
        foreach (var entry in entries)
        {
            // FR-028: the home page is a section of nothing; it is current on "/" only.
            if (entry.Href is not null && entry.Path is { } path && path != "/" && IsUnder(currentPath, path) && path.Length > bestLength)
            {
                best = entry;
                bestLength = path.Length;
            }

            FindSection(entry.Children, currentPath, ref best, ref bestLength);
        }
    }

    /// <summary>Whether <paramref name="path"/> lies below <paramref name="ancestor"/>, segment by segment.</summary>
    private static bool IsUnder(string path, string ancestor) =>
        path.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Marks <paramref name="section"/> and its ancestors; true when it lies under <paramref name="entries"/>.</summary>
    private static bool MarkPath(IReadOnlyList<ResolvedEntry> entries, ResolvedEntry section)
    {
        foreach (var entry in entries)
        {
            if (ReferenceEquals(entry, section))
            {
                entry.State = NavState.Section;
                return true;
            }

            if (MarkPath(entry.Children, section))
            {
                entry.State = NavState.Containing;
                return true;
            }
        }

        return false;
    }
}
