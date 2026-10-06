using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// "This page's or component's draft shows that media file" (013 data-model.md §2.1,
/// research MV-05).
/// </summary>
/// <remarks>
/// Rewritten with every draft save, in the same transaction, so a file's usage is never
/// stale. <see cref="MediaId"/> has no foreign key: the file is another module's row, and a
/// reference to a deleted file is exactly what the editor's "image missing" placeholder
/// shows (011). The subject's delete removes its rows explicitly, since a subject is either a
/// page or a component and so has no single foreign key.
/// </remarks>
internal sealed class DraftMediaReference : ISiteOwned
{
    public DraftMediaReference(Guid siteId, string subjectKind, Guid subjectId, Guid mediaId)
    {
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        MediaId = mediaId;
    }

    public Guid SiteId { get; private set; }

    /// <summary>One of <see cref="SubjectKinds"/>.</summary>
    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    public Guid MediaId { get; private set; }
}

/// <summary>
/// "This published version shows that media file" (013 data-model.md §2.2, research MV-05).
/// </summary>
/// <remarks>
/// Written in the publish transaction for the new version, from the dependencies already
/// extracted there, and removed with the version by cascade (retention and deletes alike).
/// Whether a reference is live or history is read from the publication's live pointer, so
/// nothing has to be rewritten when the pointer moves.
/// </remarks>
internal sealed class VersionMediaReference : ISiteOwned
{
    public VersionMediaReference(Guid siteId, Guid versionId, Guid mediaId)
    {
        SiteId = siteId;
        VersionId = versionId;
        MediaId = mediaId;
    }

    public Guid SiteId { get; private set; }

    public Guid VersionId { get; private set; }

    public Guid MediaId { get; private set; }
}
