using QuickBuilder.Pages.Domain.Events;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// The publication state of one page or component: which version visitors get, and the
/// page's render generation (012 research PR-02, PR-05, data-model §1.1, §3.1).
/// </summary>
/// <remarks>
/// Created on the first publish and kept when the subject is unpublished, so numbers and
/// generations only ever grow. Its <c>xmin</c> serialises concurrent publishes of one subject
/// (PR-03).
/// </remarks>
internal sealed partial class Publication : AggregateRoot, ISiteOwned
{
    private Publication(Guid siteId, string subjectKind, Guid subjectId)
    {
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        NextNumber = 1;
        Generation = 1;
    }

    // EF Core materialisation.
    private Publication()
    {
        SubjectKind = string.Empty;
    }

    public Guid SiteId { get; private set; }

    /// <summary><see cref="SubjectKinds.Page"/> or <see cref="SubjectKinds.Component"/>.</summary>
    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    /// <summary>The version visitors get; null while unpublished.</summary>
    public Guid? LiveVersionId { get; private set; }

    public int? LiveNumber { get; private set; }

    /// <summary>The number the next publish or rollback takes.</summary>
    public int NextNumber { get; private set; }

    /// <summary>
    /// The draft version (<c>xmin</c>) the live version was copied from; null after a rollback,
    /// so the draft reads as changed until the next publish (PR-02).
    /// </summary>
    public long? PublishedFromDraftVersion { get; private set; }

    public DateTimeOffset? LiveSinceUtc { get; private set; }

    public Guid? LiveBy { get; private set; }

    /// <summary>
    /// The page's render generation (PR-05). Rendering keys its cache by it, so incrementing
    /// it is how every change reaches visitors on every instance.
    /// </summary>
    public long Generation { get; private set; }

    /// <summary>The concurrency token, mapped to <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public bool IsLive => LiveVersionId is not null;

    /// <summary>A publication for a subject that has never been published.</summary>
    public static Publication Start(Guid siteId, string subjectKind, Guid subjectId) =>
        new(siteId, subjectKind, subjectId);

    /// <summary>Versions kept per subject, never counting out the live one (research PR-15).</summary>
    public const int RetainedVersions = 50;

    /// <summary>
    /// Makes a new version live (data-model §3.1): a publish, a scheduled publish, or a rollback
    /// (<paramref name="restoredFromNumber"/> set, and no draft it was copied from).
    /// </summary>
    /// <param name="document">The document, already validated.</param>
    /// <param name="schemaVersion">Its page schema version.</param>
    /// <param name="title">For pages, the title visitors will see; null for components.</param>
    /// <param name="fromDraftVersion">The draft version copied, or null for a rollback.</param>
    /// <param name="kind">One of <see cref="VersionKinds"/>.</param>
    /// <param name="restoredFromNumber">For a rollback, the version restored.</param>
    /// <param name="actor">Who published; for a schedule, who set it.</param>
    /// <param name="path">For pages, the address, carried by the integration event.</param>
    /// <param name="now">When it takes effect.</param>
    public PublishedVersion Publish(
        string document,
        int schemaVersion,
        string? title,
        long? fromDraftVersion,
        string kind,
        int? restoredFromNumber,
        Guid actor,
        string? path,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        var version = new PublishedVersion(
            SiteId, SubjectKind, SubjectId, NextNumber, schemaVersion, document, title, kind, restoredFromNumber, actor, now);

        LiveVersionId = version.Id;
        LiveNumber = version.Number;
        NextNumber = version.Number + 1;
        PublishedFromDraftVersion = fromDraftVersion;
        LiveSinceUtc = now;
        LiveBy = actor;
        Generation++;

        Raise(new SubjectPublishedEvent(SiteId, SubjectKind, SubjectId, version.Number, kind, path, now));
        return version;
    }

    /// <summary>Takes the subject off the public site. Its versions and numbering stay.</summary>
    public void Unpublish(bool scheduled, string? path, DateTimeOffset now)
    {
        if (!IsLive)
        {
            throw new InvalidOperationException("Only a live subject can be unpublished; the caller checks first.");
        }

        LiveVersionId = null;
        LiveNumber = null;
        PublishedFromDraftVersion = null;
        LiveSinceUtc = null;
        LiveBy = null;
        Generation++;

        Raise(new SubjectUnpublishedEvent(SiteId, SubjectKind, SubjectId, scheduled, path, now));
    }

    /// <summary>
    /// Whether the draft differs from what visitors get (FR-007, research PR-02). A page's
    /// title is part of its version, so a renamed page has changes too.
    /// </summary>
    public bool HasUnpublishedChanges(long draftVersion, string? currentTitle, string? liveTitle) =>
        IsLive
        && (PublishedFromDraftVersion != draftVersion
            || (SubjectKind == SubjectKinds.Page && !string.Equals(currentTitle, liveTitle, StringComparison.Ordinal)));
}
