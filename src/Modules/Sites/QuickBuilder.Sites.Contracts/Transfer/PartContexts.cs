namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>What a part gets to export one site (contracts/archive-parts.md §1).</summary>
public sealed class PartExportContext(
    Guid siteId,
    ISiteTransferSession session,
    IArchiveWriter archive,
    IAuthorCollector authors,
    ITransferProgress progress,
    Action<string, long> count,
    Action<ArchiveProblem> problem)
{
    public Guid SiteId { get; } = siteId;

    /// <summary>The read-only snapshot every part reads in.</summary>
    public ISiteTransferSession Session { get; } = session;

    public IArchiveWriter Archive { get; } = archive;

    /// <summary>Every user id the part's rows name.</summary>
    public IAuthorCollector Authors { get; } = authors;

    public ITransferProgress Progress { get; } = progress;

    /// <summary>Adds to the summary's count of <c>kind</c> (FR-009).</summary>
    public void Count(string kind, long n) => count(kind, n);

    /// <summary>Records a problem that does not stop the export, such as missing media bytes.</summary>
    public void Problem(string code, string kind, Guid id) => problem(new ArchiveProblem(code, kind, id));
}

/// <summary>What a part gets to check an archive against a target (research ST-06).</summary>
public sealed class PartCheckContext(
    IArchiveReader archive,
    ImportMode mode,
    Guid? targetSiteId,
    ISiteTransferSession? session,
    IIdMapBuilder ids,
    ICheckReport report,
    ITransferProgress progress,
    DateTimeOffset nowUtc)
{
    public IArchiveReader Archive { get; } = archive;

    public ArchiveManifest Manifest => Archive.Manifest;

    public ImportMode Mode { get; } = mode;

    /// <summary>The existing site for a merge or restore; null for a new site.</summary>
    public Guid? TargetSiteId { get; } = targetSiteId;

    /// <summary>Whether the archive was exported from the target site (FR-022a).</summary>
    public bool SameSiteAsTarget => TargetSiteId is { } target && Manifest.Source.SiteId == target;

    /// <summary>A read-only snapshot of the target, when there is one.</summary>
    public ISiteTransferSession? Session { get; } = session;

    public IIdMapBuilder Ids { get; } = ids;

    public ICheckReport Report { get; } = report;

    public ITransferProgress Progress { get; } = progress;

    /// <summary>The check's clock, for past-due schedules.</summary>
    public DateTimeOffset NowUtc { get; } = nowUtc;
}

/// <summary>What a part gets to apply an archive (research ST-05, ST-10 … ST-12).</summary>
public sealed class PartApplyContext(
    IArchiveReader archive,
    ImportMode mode,
    Guid targetSiteId,
    ISiteTransferSession session,
    IIdMap ids,
    IAuthorMap authors,
    IImportReport report,
    ITransferProgress progress,
    ConflictPolicy? policy,
    bool startFresh,
    Guid? actorUserId,
    DateTimeOffset nowUtc)
{
    public IArchiveReader Archive { get; } = archive;

    public ArchiveManifest Manifest => Archive.Manifest;

    public ImportMode Mode { get; } = mode;

    /// <summary>The site written to. For a new site, its id is allocated before any part applies.</summary>
    public Guid TargetSiteId { get; } = targetSiteId;

    /// <summary>The write transaction every part applies in; committed once, after the last part.</summary>
    public ISiteTransferSession Session { get; } = session;

    public IIdMap Ids { get; } = ids;

    public IAuthorMap Authors { get; } = authors;

    public IImportReport Report { get; } = report;

    public ITransferProgress Progress { get; } = progress;

    /// <summary>The merge policy; null for a new site or a restore.</summary>
    public ConflictPolicy? Policy { get; } = policy;

    /// <summary>New site only: drafts only, nothing published, no history (FR-021a, research ST-11).</summary>
    public bool StartFresh { get; } = startFresh;

    /// <summary>The Administrator who confirmed the import; null for the operator command.</summary>
    public Guid? ActorUserId { get; } = actorUserId;

    /// <summary>The import's clock, for past-due schedules and for created and changed times.</summary>
    public DateTimeOffset NowUtc { get; } = nowUtc;
}
