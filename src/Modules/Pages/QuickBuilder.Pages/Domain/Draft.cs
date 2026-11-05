using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// The editable content of one page or one component: a content document in the page
/// schema format (FR-015 to FR-021, research P-08).
/// </summary>
/// <remarks>
/// <para>
/// A draft is its own aggregate, not part of the tree or the component. Content and
/// structure are different people's work (clarification Q3), so saving one never
/// contends with changing the other, and a tree loads without its documents.
/// </para>
/// <para>
/// A save replaces the whole document, and only when the caller saw the current version
/// (<c>xmin</c>). The document is stored as <c>jsonb</c>, which keeps its value but not its
/// key order: "unchanged" means equal as JSON (FR-020). It is always validated before it
/// gets here — this type trusts its input, and the handler is where the contract is
/// enforced.
/// </para>
/// <para>
/// The two kinds are separate tables with separate foreign keys, so the base class is
/// deliberately not in the EF model: each derived type maps on its own.
/// </para>
/// </remarks>
internal abstract partial class Draft : AggregateRoot, ISiteOwned
{
    /// <summary>A new document: a root with no children, in the current version (P-10).</summary>
    public const string EmptyDocument =
        """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[]}}""";

    protected Draft(Guid siteId, Guid actor, DateTimeOffset now)
    {
        SiteId = siteId;
        SchemaVersion = PageSchema.CurrentVersion;
        Document = EmptyDocument;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    // EF Core materialisation.
    protected Draft()
    {
        Document = EmptyDocument;
    }

    public Guid SiteId { get; private set; }

    /// <summary>Always equal to the document's own <c>schemaVersion</c>.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>The validated document, as JSON text.</summary>
    public string Document { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The concurrency token, mapped to <c>xmin</c>, sent to clients as <c>version</c>.</summary>
    public uint Version { get; private set; }

    /// <summary>Replaces the document with one that has already passed validation.</summary>
    public void Replace(string document, int schemaVersion, Guid actor, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);
        ArgumentNullException.ThrowIfNull(clock);

        Document = document;
        SchemaVersion = schemaVersion;
        UpdatedAtUtc = clock.GetUtcNow();
        UpdatedBy = actor;
    }
}

/// <summary>A page's draft. One per page, created with it.</summary>
internal sealed class PageDraft : Draft
{
    private PageDraft(Guid siteId, Guid pageId, Guid actor, DateTimeOffset now)
        : base(siteId, actor, now) => PageId = pageId;

    // EF Core materialisation.
    private PageDraft()
    {
    }

    public Guid PageId { get; private set; }

    public static PageDraft CreateEmpty(Guid siteId, Guid pageId, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new PageDraft(siteId, pageId, actor, clock.GetUtcNow());
    }
}

/// <summary>A shared component's draft. One per component, created with it.</summary>
internal sealed class ComponentDraft : Draft
{
    private ComponentDraft(Guid siteId, Guid componentId, Guid actor, DateTimeOffset now)
        : base(siteId, actor, now) => ComponentId = componentId;

    // EF Core materialisation.
    private ComponentDraft()
    {
    }

    public Guid ComponentId { get; private set; }

    public static ComponentDraft CreateEmpty(Guid siteId, Guid componentId, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new ComponentDraft(siteId, componentId, actor, clock.GetUtcNow());
    }
}
