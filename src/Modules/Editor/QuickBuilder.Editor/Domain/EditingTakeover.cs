using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Editor.Domain;

/// <summary>
/// A record that one member took over another's stale lock (023 data-model §1.2, research
/// CE-12, FR-018). Ids and a duration only: no titles or content. Kept 90 days.
/// </summary>
internal sealed class EditingTakeover : ISiteOwned
{
    private EditingTakeover(Guid siteId, string kind, Guid documentId, Guid fromUserId, Guid toUserId, DateTimeOffset takenAt, int staleForSeconds)
    {
        Id = Guid.CreateVersion7(takenAt);
        SiteId = siteId;
        DocumentKind = kind;
        DocumentId = documentId;
        FromUserId = fromUserId;
        ToUserId = toUserId;
        TakenAt = takenAt;
        StaleForSeconds = staleForSeconds;
    }

    // EF Core materialisation.
    private EditingTakeover()
    {
        DocumentKind = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string DocumentKind { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid FromUserId { get; private set; }

    public Guid ToUserId { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    /// <summary>How long the lock had been inactive when it was taken over.</summary>
    public int StaleForSeconds { get; private set; }

    public static EditingTakeover Record(EditingSession from, Guid toUserId, DateTimeOffset lastActivityAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(from);
        var staleFor = (int)Math.Max(0, (now - lastActivityAt).TotalSeconds);
        return new EditingTakeover(from.SiteId, from.DocumentKind, from.DocumentId, from.UserId, toUserId, now, staleFor);
    }
}
