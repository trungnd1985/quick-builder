# Module Surface: Collection Templates and Data Binding

What each module exposes to the others in this feature. Public interfaces live in the
module's `Contracts` project (Principle I). Decisions: [research.md](../research.md).

## 1. New reference edges

| From (implementation) | To (Contracts) | Why |
|---|---|---|
| **+** `QuickBuilder.Pages` | `Cms.Contracts` | collection checks (rule 14, TB-02); item counts (TB-12); `CollectionsReferencingAsync` and Cms events (TB-10) |
| **+** `QuickBuilder.Rendering` | `Cms.Contracts` | `IPublishedItemReader` (TB-08) |
| `QuickBuilder.Cms` | `Pages.Contracts` | existing edge; now also `ITemplateReader`, `ICollectionUsageReader` |

No Contracts project references another module's Contracts, so there is no cycle.
`ModuleBoundaryTests` adds the two edges.

## 2. Cms.Contracts

```csharp
// Public path, read-only connection (TB-08). Only items with published data are visible.
public interface IPublishedItemReader
{
    Task<PublishedItemRef?> FindBySlugAsync(Guid siteId, Guid collectionId, string slug, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, PublishedItem>> GetAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
    Task<PublishedItemPage> QueryAsync(Guid siteId, PublishedListQuery query, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, CollectionSchemaDocument>> GetSchemasAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken ct = default);
    Task<int> CountPublishedAsync(Guid siteId, Guid collectionId, CancellationToken ct = default);
}

public sealed record PublishedItemRef(Guid ItemId, long RenderStamp);
public sealed record PublishedItem(Guid Id, Guid CollectionId, string Slug, string? Name,
    DateTimeOffset FirstPublishedAtUtc, DateTimeOffset LastPublishedAtUtc, JsonElement Values);
public sealed record PublishedItemPage(IReadOnlyList<PublishedItem> Items, int Total);
public sealed record PublishedListQuery(Guid CollectionId, IReadOnlyList<Guid>? ItemIds,
    IReadOnlyList<ListFilter> Filters, IReadOnlyList<ListSort> Sorts, int Offset, int Count);
// ListFilter / ListSort: field id or built-in, operator, typed operand (binding-expression.md §5).

// The one reading of the list format, for the renderer and the editor's preview alike: every
// scope value resolved to a literal, a value that cannot be read becoming a broken condition.
public sealed record ListScope(string Name, string Slug, Func<string, JsonElement?> Read, Func<string, Guid?> MultiReferenceTarget);
public static class ListQueryBuilder
{
    public static PublishedListQuery? Build(JsonElement listProps, IReadOnlyList<ListScope> scopes, int pageNumber = 1);
}

// Admin side, normal connection.
public interface ICollectionSchemaReader   // new; Cms.Contracts has only the static CollectionSchema today
{
    Task<IReadOnlySet<Guid>> ExistingAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, CollectionSchemaDocument>> GetSchemasAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> CollectionsReferencingAsync(Guid siteId, Guid collectionId, CancellationToken ct = default);
    // Address warnings on the admin surface (TB-12), which resolves no site.
    Task<int> CountPublishedItemsAsync(Guid siteId, Guid collectionId, CancellationToken ct = default);
    Task<Guid?> PublishedItemWithSlugAsync(Guid siteId, Guid collectionId, string slug, CancellationToken ct = default);
}
```

`IItemMediaUsageReader` (015) gains
`CollectionsShowingPublishedAsync(siteId, mediaId)`, for TB-10's media row.

## 3. Pages.Contracts

```csharp
public interface ITemplateReader
{
    Task<TemplateInfo?> GetTemplateAsync(Guid siteId, Guid collectionId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, TemplateInfo>> GetTemplatesAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken ct = default);
    // Whether a live ordinary page has the address, for an item's "hidden_by_page" reason (TB-12):
    // the admin surface resolves no site, so the public reader cannot answer there.
    Task<bool> LivePageAtAsync(Guid siteId, string path, CancellationToken ct = default);
}
public sealed record TemplateInfo(Guid PageId, string Pattern, bool IsLive);   // Pattern like "/blog/{slug}"

public interface ICollectionUsageReader
{
    Task<CollectionUses> ListUsesAsync(Guid siteId, Guid collectionId, int limit, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, int>> CountFieldUsesAsync(Guid siteId, Guid collectionId, IReadOnlyCollection<string> fieldIds, CancellationToken ct = default);
}
public sealed record CollectionUse(string Kind, Guid Id, string Title, IReadOnlyList<string> States);
public sealed record CollectionUses(IReadOnlyList<CollectionUse> Uses, int Total);
```

`IPublishedContentReader` (012) gains:

```csharp
// The live template whose parent has this normalised path; null when none (TB-03).
Task<LiveTemplateRef?> FindLiveTemplateAsync(Guid siteId, string parentPath, CancellationToken ct = default);
public sealed record LiveTemplateRef(Guid PageId, long Generation, Guid CollectionId, IReadOnlyList<string> PageParamNames);
// LivePageRef (012) gains the same: LivePageRef(Guid PageId, long Generation, IReadOnlyList<string> PageParamNames).
// PageParamNames: the page's own publications.page_param_names plus those of the live components
// it shows (live_dependencies page → component), merged by the reader (data-model §1.2a, TB-11, FR-027).
// GetLinkTargetsAsync reports template pages with IsLive = false (TB-02).
// For item links (TB-09): the patterns of live templates by collection.
Task<IReadOnlyDictionary<Guid, string>> GetLiveTemplatePatternsAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken ct = default);
```

`FindLiveByPathAsync` ignores template pages from now on.

## 4. Events

| Event | Status |
|---|---|
| `Pages.PageTemplateChanged (Id, OccurredAtUtc, SiteId, PageId, CollectionId?, PreviousCollectionId?, ActorUserId?)` | **new**, published contract name |
| `Pages.PageAddressesChanged` | now also emitted when a template pattern changes |

Pages handlers, idempotent through `pages.processed_events` (TB-10, TB-16):
- `RefreshPagesListingItemsHandler`: `Cms.ItemPublished`, `ItemUnpublished`, and
  `ItemDeleted` with `WasPublished`;
- `RefreshPagesUsingCollectionHandler`: `Cms.CollectionChanged` with `SchemaChanged` or
  `ItemsRewritten`, and `Cms.CollectionDeleted`;
- `RefreshPagesShowingMediaHandler` (changed): adds list pages.

`specs/001-foundation-skeleton/contracts/integration-events.md` gains the new event and the
new subscriptions.

## 5. Capabilities

No new capability. `specs/002-identity-setup/contracts/authorization.md` §2 gains notes on
which existing capabilities guard the new endpoints (TB-17).

## 6. Obligations closed and opened

- **Closes E-6** (014, 015): schema changes reach live pages by refreshing every page that
  binds to or lists the collection (TB-10).
- **015 module-surface §5**: "#16 subscribes to ItemPublished, ItemUnpublished,
  ItemDeleted": met.
- **Opens for #17**: `Pages.PageTemplateChanged`, item addresses through `ITemplateReader`
  and `IPublishedItemReader`, for sitemaps and redirects.
