using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain.Events;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// A site's collection: its details, its schema and the counters that issue field and option
/// ids (data-model §1.1 and §3.1, research CS-02).
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root of the Cms module. Every saved change increments <see cref="Revision"/>,
/// which is the concurrency token clients send back (FR-022). The schema is stored as the
/// canonical JSON of <see cref="CollectionSchemaDocument"/>; this type trusts what it is
/// given, which the handler has already validated against the contract.
/// </para>
/// <para>
/// The counters only grow, so an id is never issued twice for one collection, even after its
/// field is removed (FR-006).
/// </para>
/// </remarks>
internal sealed partial class Collection : AggregateRoot, ISiteOwned
{
    private CollectionSchemaDocument? _schema;

    private Collection(Guid siteId, CollectionDetails details, Guid actor, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        SiteId = siteId;
        Name = details.Name;
        Slug = details.Slug;
        Description = details.Description;
        Schema = CollectionSchemaDocument.Empty.ToJson();
        SchemaVersion = CollectionSchema.CurrentVersion;
        Revision = 1;
        SchemaRevision = 1;
        NextFieldNumber = 1;
        NextOptionNumber = 1;
        CreatedAtUtc = now;
        CreatedBy = actor;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    // EF Core materialisation.
    private Collection()
    {
        Name = string.Empty;
        Slug = string.Empty;
        Schema = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public string? Description { get; private set; }

    /// <summary>The schema document as canonical JSON (contract §2).</summary>
    public string Schema { get; private set; }

    /// <summary>Always equal to the schema document's own <c>schemaVersion</c>.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Incremented by every saved change; the concurrency token.</summary>
    public int Revision { get; private set; }

    /// <summary>
    /// The <see cref="Revision"/> at which the schema document last changed (015 research IT-07).
    /// An item form built from an older one is out of date; a details-only save leaves it alone.
    /// </summary>
    public int SchemaRevision { get; private set; }

    public int NextFieldNumber { get; private set; }

    public int NextOptionNumber { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedBy { get; private set; }

    public CollectionDetails Details => new(Name, Slug, Description);

    public CollectionSchemaDocument SchemaDocument => _schema ??= CollectionSchemaDocument.Read(Schema);

    /// <summary>A new, empty collection at revision 1 (US1).</summary>
    public static Collection Create(Guid siteId, CollectionDetails details, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();
        var collection = new Collection(siteId, details, actor, now);
        collection.Raise(new CollectionCreatedEvent(siteId, collection.Id, collection.Revision, now));
        return collection;
    }

    /// <summary>
    /// Ids for a proposal's temporary fields and options, from this collection's counters. Pure:
    /// the counters advance only when <see cref="Apply"/> stores the result.
    /// </summary>
    public IdAssignment AssignIds(CollectionSchemaDocument proposal) =>
        IdAssignment.Assign(proposal, NextFieldNumber, NextOptionNumber);

    /// <summary>
    /// Stores a save as the next revision: the details, the schema with its permanent ids, and
    /// the advanced counters (FR-021, FR-032). Returns the history entry for it.
    /// </summary>
    /// <remarks>
    /// The caller has checked the revision, validated the proposal, computed its impact and
    /// confirmed it, and rewrites the items in the same transaction.
    /// </remarks>
    public SchemaChangeEntry Apply(CollectionDetails details, IdAssignment assignment, ImpactSummary impact, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(clock);

        if (impact.Changes.Count == 0)
        {
            throw new InvalidOperationException("A save with no change is not a new revision.");
        }

        var detailsBefore = Details;
        var now = clock.GetUtcNow();

        Name = details.Name;
        Slug = details.Slug;
        Description = details.Description;
        _schema = assignment.Document;
        Schema = assignment.Document.ToJson();
        SchemaVersion = assignment.Document.SchemaVersion;
        NextFieldNumber = assignment.NextFieldNumber;
        NextOptionNumber = assignment.NextOptionNumber;
        Revision++;
        UpdatedAtUtc = now;
        UpdatedBy = actor;

        var schemaChanged = impact.Changes.Any(c => c.Change.Kind != ChangeKind.DetailsChanged);
        if (schemaChanged)
        {
            SchemaRevision = Revision;
        }

        Raise(new CollectionChangedEvent(SiteId, Id, Revision, schemaChanged, impact.RewritesItems, now));

        return SchemaChangeEntry.For(this, detailsBefore, impact, actor, now);
    }

    /// <summary>Records that the collection and all its items are being deleted (FR-004).</summary>
    public void MarkDeleted(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Raise(new CollectionDeletedEvent(SiteId, Id, clock.GetUtcNow()));
    }
}
