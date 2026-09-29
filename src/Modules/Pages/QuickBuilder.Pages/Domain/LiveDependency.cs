using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// "This live page or component depends on that component, page or media file"
/// (012 research PR-04, data-model §1.3).
/// </summary>
/// <remarks>
/// Written in the publish transaction from the version's document, so the graph is complete
/// before any visitor can request the version; recording it while rendering would race a
/// component publish. It describes live versions only and answers which pages to refresh
/// when something changes.
/// </remarks>
internal sealed class LiveDependency : ISiteOwned
{
    public LiveDependency(Guid siteId, string subjectKind, Guid subjectId, string targetKind, Guid targetId)
    {
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        TargetKind = targetKind;
        TargetId = targetId;
    }

    public Guid SiteId { get; private set; }

    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    /// <summary>One of <see cref="DependencyKinds"/>.</summary>
    public string TargetKind { get; private set; }

    public Guid TargetId { get; private set; }
}
