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
}
