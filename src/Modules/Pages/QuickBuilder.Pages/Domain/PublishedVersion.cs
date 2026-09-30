using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// One published version of a page or component: an immutable copy of its content
/// (012 research PR-02, data-model §1.2).
/// </summary>
/// <remarks>
/// Never changed once stored; a rollback copies an older version into a new one. Its
/// content may be removed by retention (PR-15) or with its subject, while the log entry
/// that records it stays.
/// </remarks>
internal sealed class PublishedVersion : ISiteOwned
{
    internal PublishedVersion(
        Guid siteId,
        string subjectKind,
        Guid subjectId,
        int number,
        int schemaVersion,
        string document,
        string? title,
        string kind,
        int? restoredFromNumber,
        Guid publishedBy,
        DateTimeOffset publishedAtUtc)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        Number = number;
        SchemaVersion = schemaVersion;
        Document = document;
        Title = title;
        Kind = kind;
        RestoredFromNumber = restoredFromNumber;
        PublishedBy = publishedBy;
        PublishedAtUtc = publishedAtUtc;
    }

    // EF Core materialisation.
    private PublishedVersion()
    {
        SubjectKind = string.Empty;
        Document = string.Empty;
        Kind = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    /// <summary>1, 2, 3 … per subject, never repeated (FR-012).</summary>
    public int Number { get; private set; }

    public int SchemaVersion { get; private set; }

    /// <summary>The page schema document as JSON text (<c>jsonb</c>).</summary>
    public string Document { get; private set; }

    /// <summary>The page title at publish, the document title visitors see (FR-024). Null for components.</summary>
    public string? Title { get; private set; }

    /// <summary>One of <see cref="VersionKinds"/>.</summary>
    public string Kind { get; private set; }

    public int? RestoredFromNumber { get; private set; }

    /// <summary>Who published it; for a scheduled publish, who set the schedule.</summary>
    public Guid PublishedBy { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }
}
