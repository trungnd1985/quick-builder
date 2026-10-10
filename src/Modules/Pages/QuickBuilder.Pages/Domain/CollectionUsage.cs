using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// "This page's or component's draft, live version or pending scheduled content uses that
/// collection, and reads these of its fields" (016 data-model §1.3, research TB-13).
/// </summary>
/// <remarks>
/// Rewritten for one subject and state in the transaction that changes that state, like
/// <see cref="DraftMediaReference"/>, so Cms can refuse to delete a collection that is in use
/// (FR-010) and count the uses of a field before a schema change (FR-011).
/// <see cref="CollectionId"/> has no foreign key: the collection is another module's row.
/// </remarks>
internal sealed class CollectionUsage : ISiteOwned
{
    public CollectionUsage(Guid siteId, string subjectKind, Guid subjectId, string state, Guid collectionId, IReadOnlyCollection<string> fieldIds)
    {
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        State = state;
        CollectionId = collectionId;
        FieldIds = [.. fieldIds.Order(StringComparer.Ordinal)];
    }

    // EF Core materialisation.
    private CollectionUsage()
    {
        SubjectKind = string.Empty;
        State = string.Empty;
        FieldIds = [];
    }

    public Guid SiteId { get; private set; }

    /// <summary>One of <see cref="SubjectKinds"/>.</summary>
    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    /// <summary>One of <see cref="CollectionUsageStates"/>.</summary>
    public string State { get; private set; }

    public Guid CollectionId { get; private set; }

    /// <summary>The field ids that bindings, filters and sort keys read, sorted.</summary>
    public string[] FieldIds { get; private set; }
}

/// <summary>Where a collection is used (016 data-model §1.3).</summary>
internal static class CollectionUsageStates
{
    public const string Draft = "draft";

    public const string Live = "live";

    public const string Scheduled = "scheduled";

    /// <summary>In the order uses are listed.</summary>
    public static IReadOnlyList<string> All { get; } = [Draft, Live, Scheduled];
}
