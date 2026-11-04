namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>
/// One module's share of a site archive (020 contracts/archive-parts.md §1).
/// </summary>
/// <remarks>
/// Each module that owns site content registers one. The Sites module runs them in
/// <see cref="Order"/> and never sees their rows: a part reads and writes only its own
/// module's tables, through its own <c>DbContext</c> joined to the shared
/// <see cref="ISiteTransferSession"/> (constitution Principle I, as amended in 2.6.0).
/// <para>
/// <see cref="CheckAsync"/> never writes. <see cref="ApplyAsync"/> takes every id from
/// <see cref="PartApplyContext.Ids"/>; the id map is complete before any part applies, which is
/// what lets pages and items refer to each other without a write order (research ST-08).
/// </para>
/// </remarks>
public interface ISiteArchivePart
{
    /// <summary>The part's name in the archive: <c>parts/&lt;Name&gt;/…</c>. Lower-case.</summary>
    string Name { get; }

    /// <summary>Run order: 10 sites, 20 media, 30 design, 40 cms, 50 pages (research ST-03).</summary>
    int Order { get; }

    /// <summary>The part's entry format version. 1 in archive format 1.</summary>
    int Version { get; }

    /// <summary>The content contracts the part's documents use, with this release's version of each.</summary>
    IReadOnlyDictionary<string, int> Contracts { get; }

    /// <summary>Writes the part's entries for <see cref="PartExportContext.SiteId"/>.</summary>
    Task ExportAsync(PartExportContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Validates the part's entries, declares and matches its ids, and reports problems and
    /// conflicts. Runs again inside the apply transaction to rebuild the id map (research ST-06).
    /// </summary>
    Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken);

    /// <summary>Writes the part's rows into the target site inside the shared transaction.</summary>
    Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken);
}

/// <summary>How an archive is imported (spec Context; FR-017).</summary>
public enum ImportMode
{
    /// <summary>A new, independent site is created from the archive.</summary>
    NewSite,

    /// <summary>The archive's content is added to an existing site; conflicts follow one policy.</summary>
    Merge,

    /// <summary>The archive's own source site is made to match it exactly (FR-027a).</summary>
    Restore,
}

/// <summary>What a merge does with a document that matches one in the target (FR-023).</summary>
public enum ConflictPolicy
{
    Replace,
    Skip,
    Rename,
}
