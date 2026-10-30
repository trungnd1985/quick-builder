using System.Text.Json;
using QuickBuilder.Cms.Domain.Events;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// One item of a collection: its draft and published values kept apart, its slugs, status and
/// publishing state (015 data-model §1.2 and §3.1, research IT-01; 014 FR-015, Principle VII).
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root of an item. Every write increments <see cref="Revision"/>, the concurrency
/// token clients send back (FR-005). Its operations change the item and return or record the
/// rows that go with the change (a version, history entries), which <c>ItemWrites</c> adds in
/// the same transaction. It trusts the validation report it is given: <c>ItemValidation</c>
/// needs the database.
/// </para>
/// <para>
/// Name, search text, attention and the derived tables are kept in step by <c>ItemIndexer</c>,
/// never here (research IT-11). Only publish, unpublish, rollback and scheduled runs write the
/// published values (SC-004).
/// </para>
/// </remarks>
internal sealed class Item : AggregateRoot, ISiteOwned, IDisposable
{
    /// <summary>Published versions kept per item, never deleting the live one (FR-023).</summary>
    public const int KeptVersions = 50;

    private readonly List<ItemHistoryEntry> _newHistory = [];

    private Item(Guid siteId, Guid collectionId, string slug, JsonDocument draft, Guid actor, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        SiteId = siteId;
        CollectionId = collectionId;
        Slug = slug;
        Draft = draft;
        Status = ItemStatuses.Draft;
        Revision = 1;
        DraftRevision = 1;
        DraftChangedBy = actor == Guid.Empty ? null : actor;
        DraftChangedAtUtc = now;
        DraftChangeKind = ItemDraftChangeKinds.Member;
        NextVersionNumber = 1;
        SearchText = string.Empty;
        CreatedAtUtc = now;
        CreatedBy = actor;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    // EF Core materialisation.
    private Item()
    {
        Draft = null!;
        Slug = string.Empty;
        Status = string.Empty;
        SearchText = string.Empty;
        DraftChangeKind = ItemDraftChangeKinds.Member;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid CollectionId { get; private set; }

    /// <summary>The draft values document (contract §3).</summary>
    public JsonDocument Draft { get; private set; }

    /// <summary>The published values document; null when nothing is published.</summary>
    public JsonDocument? Published { get; private set; }

    public string Slug { get; private set; }

    /// <summary>The published slug; null exactly when <see cref="Published"/> is.</summary>
    public string? PublishedSlug { get; private set; }

    /// <summary>One of <see cref="ItemStatuses"/>.</summary>
    public string Status { get; private set; }

    /// <summary>+1 on every write, and on every schema migration that rewrites the item.</summary>
    public int Revision { get; private set; }

    /// <summary>
    /// +1 on every change to the draft only: saves, copies into the draft, and schema changes
    /// that rewrite it (023 research CE-08). Draft saves compare it, so a publish by someone else
    /// does not refuse the next save.
    /// </summary>
    public int DraftRevision { get; private set; }

    /// <summary>Who last changed the draft; null for a schema change (023 research CE-09).</summary>
    public Guid? DraftChangedBy { get; private set; }

    public DateTimeOffset DraftChangedAtUtc { get; private set; }

    /// <summary>One of <see cref="ItemDraftChangeKinds"/>.</summary>
    public string DraftChangeKind { get; private set; }

    /// <summary>023 FR-021: the item this one is a copy of; null unless made by "save my version as a copy".</summary>
    public Guid? CopiedFromId { get; private set; }

    /// <summary>When the source version the member had loaded was saved.</summary>
    public DateTimeOffset? CopiedFromSavedAtUtc { get; private set; }

    public Guid? CopiedBy { get; private set; }

    public DateTimeOffset? CopiedAtUtc { get; private set; }

    /// <summary>The title field's draft value, or null (research IT-11).</summary>
    public string? Name { get; private set; }

    /// <summary>Name and slug, normalised for search (research IT-13).</summary>
    public string SearchText { get; private set; }

    /// <summary>
    /// The published text field values, lower-cased and accent-folded, keyed by field id, for
    /// Collection lists' <c>contains</c> (016 research TB-08); null when nothing is published.
    /// </summary>
    public JsonDocument? PublishedText { get; private set; }

    /// <summary>The draft's report when it has any problem, otherwise null (research IT-05).</summary>
    public JsonDocument? Attention { get; private set; }

    /// <summary>The <c>ItemIndexer</c> version that last refreshed the derived data; 0 when never.</summary>
    public int IndexVersion { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedBy { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public Guid? PublishedBy { get; private set; }

    /// <summary>
    /// When the item was first published, kept across unpublishing; the built-in value
    /// <c>firstPublishedAt</c> of bindings and lists (016 binding-expression.md §2).
    /// </summary>
    public DateTimeOffset? FirstPublishedAtUtc { get; private set; }

    /// <summary>
    /// Moves on with every change to what the item's page shows (016 research TB-10, data-model
    /// §1.4): its own publishes and unpublishes here, and changes to items it refers to and to
    /// media it shows through <c>RenderStampBump</c>. The page's cache key carries it.
    /// </summary>
    public long RenderStamp { get; private set; } = 1;

    /// <summary>The number of the live version; null when unpublished.</summary>
    public int? LiveVersion { get; private set; }

    public int NextVersionNumber { get; private set; }

    /// <summary>History entries recorded by this write, for <c>ItemWrites</c> to add.</summary>
    public IReadOnlyList<ItemHistoryEntry> NewHistory => _newHistory;

    public bool IsPublished => Published is not null;

    /// <summary>
    /// An item as stored data, before #15's rules applied: used by tests and 014's seeding. Its
    /// slug is a placeholder the index backfill replaces from the name (research IT-22).
    /// </summary>
    public static Item Create(Guid siteId, Guid collectionId, ItemValues draft, ItemValues? published, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();
        var item = new Item(siteId, collectionId, string.Empty, draft.ToDocument(), Guid.Empty, now);
        item.Slug = PlaceholderSlug(item.Id);
        if (published is not null)
        {
            item.Published = published.ToDocument();
            item.PublishedSlug = item.Slug;
        }

        item.Status = ItemStatuses.Compute(draft, item.Slug, published, item.PublishedSlug);
        return item;
    }

    /// <summary>
    /// The slug given to items stored before they had one: <c>item-</c> and the last 12 hex digits
    /// of the id, which are random in a UUID v7 (the first ones are its time, shared by items
    /// created in the same millisecond).
    /// </summary>
    public static string PlaceholderSlug(Guid id) => "item-" + id.ToString("N")[^12..];

    /// <summary>A new draft item (US1). The caller has refused malformed values.</summary>
    public static Item New(Guid siteId, Guid collectionId, string slug, ItemValues draft, Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        EnsureSlug(slug);

        var item = new Item(siteId, collectionId, slug, draft.ToDocument(), actor, now);
        item._newHistory.Add(ItemHistoryEntry.For(item, ItemHistoryKinds.Created, actor, now));
        item.Raise(new ItemCreatedEvent(siteId, collectionId, item.Id, now));
        return item;
    }

    public ItemValues DraftValues() => ItemValues.Read(Draft);

    public ItemValues? PublishedValues() => Published is null ? null : ItemValues.Read(Published);

    /// <summary>Replaces the draft (FR-013, FR-014). Never touches the published values (SC-004).</summary>
    public void SaveDraft(string slug, ItemValues draft, Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        EnsureSlug(slug);

        ReplaceDraft(slug, draft);
        Touch(actor, now);
        DraftChanged(actor, ItemDraftChangeKinds.Member, now);
        Raise(new ItemDraftSavedEvent(SiteId, CollectionId, Id, now));
    }

    /// <summary>
    /// 023 FR-020, FR-021: a new draft item holding the member's version of this one, recording
    /// where it came from. No published values, versions or schedules.
    /// </summary>
    public Item CopyAsNew(string slug, ItemValues draft, DateTimeOffset sourceSavedAtUtc, Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        EnsureSlug(slug);

        var copy = new Item(SiteId, CollectionId, slug, draft.ToDocument(), actor, now)
        {
            CopiedFromId = Id,
            CopiedFromSavedAtUtc = sourceSavedAtUtc,
            CopiedBy = actor,
            CopiedAtUtc = now,
        };
        copy._newHistory.Add(ItemHistoryEntry.For(copy, ItemHistoryKinds.CreatedAsCopy, actor, now));
        copy.Raise(new ItemCreatedEvent(SiteId, CollectionId, copy.Id, now));
        return copy;
    }

    /// <summary>
    /// A new draft item holding a copy of this one's draft (US5 scenario 5): no published
    /// values, versions or schedules. The caller has chosen the copy's slug and name.
    /// </summary>
    public Item Duplicate(string slug, ItemValues draft, Guid actor, DateTimeOffset now) =>
        New(SiteId, CollectionId, slug, draft, actor, now);

    /// <summary>
    /// Publishes <paramref name="values"/> and <paramref name="slug"/> as a new version (FR-019):
    /// the draft for a publish, the captured content for a scheduled one, a kept version for a
    /// rollback (FR-022). The caller has checked them against today's rules.
    /// </summary>
    public ItemVersion Publish(ItemValues values, string slug, string kind, int? restoredFrom, string? scheduleAction, Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureSlug(slug);

        var slugChanged = PublishedSlug is not null && !string.Equals(PublishedSlug, slug, StringComparison.Ordinal);
        var version = new ItemVersion(SiteId, Id, NextVersionNumber, values, slug, kind, restoredFrom, actor, now);
        NextVersionNumber++;

        Published?.Dispose();
        Published = values.ToDocument();
        PublishedSlug = slug;
        LiveVersion = version.Number;
        PublishedAtUtc = now;
        PublishedBy = actor;
        FirstPublishedAtUtc ??= now;
        RenderStamp++;
        Status = ItemStatuses.Compute(DraftValues(), Slug, values, slug);
        Touch(actor, now);

        _newHistory.Add(ItemHistoryEntry.For(
            this,
            kind == ItemVersionKinds.Rollback ? ItemHistoryKinds.RolledBack : ItemHistoryKinds.Published,
            actor,
            now,
            versionNumber: version.Number,
            scheduleAction: scheduleAction));
        Raise(new ItemPublishedEvent(SiteId, CollectionId, Id, version.Number, kind, slugChanged, now));
        return version;
    }

    /// <summary>
    /// A media file the published values show was deleted or changed (016 data-model §1.4 case 3):
    /// the item's page shows something new, so its cache key moves on.
    /// </summary>
    public void ShownMediaChanged() => RenderStamp++;

    /// <summary>Removes the published values, keeping the draft and every version (FR-021).</summary>
    public void Unpublish(string? scheduleAction, Guid actor, DateTimeOffset now)
    {
        if (!IsPublished)
        {
            throw new InvalidOperationException("The item is not published; the caller checks first.");
        }

        Published!.Dispose();
        Published = null;
        PublishedSlug = null;
        LiveVersion = null;
        PublishedAtUtc = null;
        PublishedBy = null;
        RenderStamp++;
        Status = ItemStatuses.Draft;
        Touch(actor, now);

        _newHistory.Add(ItemHistoryEntry.For(this, ItemHistoryKinds.Unpublished, actor, now, scheduleAction: scheduleAction));
        Raise(new ItemUnpublishedEvent(SiteId, CollectionId, Id, scheduleAction is not null, now));
    }

    /// <summary>Replaces the draft with a kept version's content; publishes nothing (FR-022).</summary>
    public void CopyToDraft(ItemVersion version, Guid actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(version);

        ReplaceDraft(version.Slug, version.ReadValues());
        Touch(actor, now);
        DraftChanged(actor, ItemDraftChangeKinds.Member, now);
        _newHistory.Add(ItemHistoryEntry.For(this, ItemHistoryKinds.CopiedToDraft, actor, now, versionNumber: version.Number));
        Raise(new ItemDraftSavedEvent(SiteId, CollectionId, Id, now));
    }

    /// <summary>Records a change to the item's schedules (FR-024, FR-027); the schedule rows change separately.</summary>
    public void RecordSchedule(string kind, string action, DateTimeOffset? dueAtUtc, string? failureCode, Guid actor, DateTimeOffset now)
    {
        Touch(actor, now);
        _newHistory.Add(ItemHistoryEntry.For(this, kind, actor, now, scheduleAction: action, dueAtUtc: dueAtUtc, failureCode: failureCode));
    }

    /// <summary>
    /// Asks Pages to redirect the item's old address (017 FR-029, FR-032): to the item when its
    /// published slug changed, or to a page when it stopped being live.
    /// </summary>
    public void RequestRedirect(string oldSlug, string reason, Guid? targetPageId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldSlug);
        Raise(new ItemRedirectRequestedEvent(SiteId, CollectionId, Id, oldSlug, reason, targetPageId, now));
    }

    /// <summary>Records that the item is being deleted, with its versions, schedules and history (FR-008).</summary>
    public void MarkDeleted(DateTimeOffset now) =>
        Raise(new ItemDeletedEvent(SiteId, CollectionId, Id, IsPublished, now));

    /// <summary>
    /// Stores values converted by a schema change. Draft and published copies are rewritten
    /// together (014 clarification Q1); the revision moves so an open form cannot overwrite them.
    /// </summary>
    public void Migrate(ItemValues draft, ItemValues? published, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(clock);

        var migrated = draft.ToDocument();
        var draftChanged = !JsonElement.DeepEquals(Draft.RootElement, migrated.RootElement);

        Draft.Dispose();
        Draft = migrated;
        if (Published is not null && published is not null)
        {
            Published.Dispose();
            Published = published.ToDocument();
        }

        Revision++;
        UpdatedAtUtc = clock.GetUtcNow();

        // 023 research CE-09: an open form learns the draft changed, and that no member did it.
        if (draftChanged)
        {
            DraftChanged(null, ItemDraftChangeKinds.SchemaChange, UpdatedAtUtc);
        }
    }

    /// <summary>What <c>ItemIndexer</c> derived from the item (research IT-11).</summary>
    public void ApplyIndex(string? name, string searchText, JsonDocument? publishedText, ItemReport draftReport, int indexVersion)
    {
        ArgumentNullException.ThrowIfNull(draftReport);

        Name = name;
        SearchText = searchText;
        PublishedText?.Dispose();
        PublishedText = publishedText;
        Attention?.Dispose();
        Attention = draftReport.ToAttention();
        Status = ItemStatuses.Compute(DraftValues(), Slug, PublishedValues(), PublishedSlug);
        IndexVersion = indexVersion;
    }

    /// <summary>Replaces a placeholder slug from before #15 with one built from the name (research IT-22).</summary>
    public void ReplacePlaceholderSlug(string slug)
    {
        EnsureSlug(slug);

        var wasSame = string.Equals(PublishedSlug, Slug, StringComparison.Ordinal);
        Slug = slug;
        if (wasSame && PublishedSlug is not null)
        {
            PublishedSlug = slug;
        }
    }

    public void ClearNewHistory() => _newHistory.Clear();

    public void Dispose()
    {
        Draft?.Dispose();
        Published?.Dispose();
        PublishedText?.Dispose();
        Attention?.Dispose();
    }

    private void ReplaceDraft(string slug, ItemValues draft)
    {
        Draft.Dispose();
        Draft = draft.ToDocument();
        Slug = slug;
        Status = ItemStatuses.Compute(draft, slug, PublishedValues(), PublishedSlug);
    }

    private void Touch(Guid actor, DateTimeOffset now)
    {
        Revision++;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    private void DraftChanged(Guid? actor, string kind, DateTimeOffset now)
    {
        DraftRevision++;
        DraftChangedBy = actor;
        DraftChangedAtUtc = now;
        DraftChangeKind = kind;
    }

    private static void EnsureSlug(string slug)
    {
        if (!SlugRules.IsValid(slug))
        {
            throw new ArgumentException("The slug breaks the slug grammar; the caller validates it first.", nameof(slug));
        }
    }
}
