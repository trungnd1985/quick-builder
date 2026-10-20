# Contract: Cross-Module Readers and Integration Events

**Feature**: 012 (issue #12) | **Research**: PR-01, PR-06, PR-08, PR-16, PR-19

These C# shapes are added to Contracts projects. Rendering depends only on them (Principle
I). Every reader:
- is implemented in its owning module over a **read-only connection** (research PR-06);
- applies the site filter;
- returns only published or public data.

---

## 1. Readers

### 1.1 `QuickBuilder.Pages.Contracts.Publishing.IPublishedContentReader`

```csharp
public interface IPublishedContentReader
{
    /// The live page at a normalised, lower-case address, with its render generation;
    /// null when no page has that address or it is not live. One indexed lookup.
    Task<LivePageRef?> FindLiveByPathAsync(Guid siteId, string path, CancellationToken ct);

    /// The live version of a page: document, schema version and title.
    Task<LivePageVersion?> GetLivePageAsync(Guid siteId, Guid pageId, CancellationToken ct);

    /// Live versions of the given components; absent ids have none.
    Task<IReadOnlyDictionary<Guid, LiveDocument>> GetLiveComponentsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken ct);

    /// For link targets: current address and whether each page is live. Absent ids do
    /// not exist.
    Task<IReadOnlyDictionary<Guid, PageLinkTarget>> GetLinkTargetsAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken ct);

    /// Whether the site's home page is live, for the not-found page.
    Task<bool> IsHomePageLiveAsync(Guid siteId, CancellationToken ct);
}

public sealed record LivePageRef(Guid PageId, long Generation);
public sealed record LiveDocument(int SchemaVersion, JsonElement Document);
public sealed record LivePageVersion(int SchemaVersion, JsonElement Document, string Title);
public sealed record PageLinkTarget(string Path, bool IsLive);
```

**016 additions** (collection templates and data binding,
[module-surface.md](../../016-collection-templates-binding/contracts/module-surface.md)):

- `FindLiveByPathAsync` never returns a template page: `/blog/{slug}` typed by a visitor is
  not a page.
- `FindLiveTemplateAsync(siteId, parentPath)` returns the live template under a parent, as
  `LiveTemplateRef(PageId, Generation, CollectionId, PageParamNames)`, for item addresses.
- `GetLiveTemplatePatternsAsync(siteId, collectionIds)` returns the address patterns of the
  live templates of those collections, for links to item pages.
- `LivePageRef` and `LiveTemplateRef` carry `PageParamNames`: the query parameters of the
  page's paginated lists, read before the content so they can be part of the cache key.
- `GetLinkTargetsAsync` reports template pages as not live, so a page link to a template
  renders as plain text.
- Published collection items are read through Cms's
  `QuickBuilder.Cms.Contracts.Publishing.IPublishedItemReader`, on its own read-only
  connection.

### 1.2 `QuickBuilder.Editor.Contracts.IPublishedDesignReader`

```csharp
public interface IPublishedDesignReader
{
    /// The live design number; 0 when the site has never published (defaults).
    /// One primary-key lookup; read on every public request (PR-05).
    Task<int> GetLiveNumberAsync(Guid siteId, CancellationToken ct);

    /// The live design document, or DefaultDesignTokens with number 0.
    Task<PublishedDesign> GetLiveAsync(Guid siteId, CancellationToken ct);
}

public sealed record PublishedDesign(int Number, int SchemaVersion, JsonElement Document);
```

011's `IDesignTokenReader` (the draft set) is unchanged and is not used by Rendering.

### 1.3 `QuickBuilder.Media.Contracts.IPublicMediaReader`

```csharp
public interface IPublicMediaReader
{
    /// Public paths of the given files that exist and are available; absent ids are
    /// missing. Paths include the base path, as MediaFileDto.PublicPath does.
    Task<IReadOnlyDictionary<Guid, PublicMediaFile>> GetAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken ct);
}

public sealed record PublicMediaFile(string PublicPath, string ContentType);
```

### 1.4 `QuickBuilder.Rendering.Contracts.IPublicNotFoundPage`

```csharp
/// The one not-found answer for a resolved site (public-rendering.md §4). The host writes
/// it for unmatched public paths and for admin paths on client domains.
public interface IPublicNotFoundPage
{
    Task<PublicResponse> RenderAsync(SiteDto site, CancellationToken ct);
}

public sealed record PublicResponse(
    int StatusCode, string ContentType,
    IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body);
```

`Rendering.Contracts` also holds `RenderingRoutes.AssetsPrefix = "/_assets"`. The host adds
it to `RoutePrefixes.PublicReservedPrefixes`.

### 1.5 Existing, unchanged

- `ICurrentSite` (Sites): the resolved `SiteDto`. `Version`, `DefaultLanguage` and
  `BaseUrl` are used.
- `IPageDocumentValidator` (Pages.Contracts): not used on the public path. Documents were
  validated at publish.

---

### Added by 024

- `IPublishedContentReader.GetLiveMenusAsync(siteId, menuIds)` returns the live version of each
  menu that has one, as `LiveMenu(SchemaVersion, Document, Name)`; `Name` is the menu's name
  when that version was published. Absent ids are not live, or not of the site.
- `PageLinkTarget` gains a trailing `LiveTitle`: the title visitors saw last (the live
  version's, or the last published one's once unpublished), for menu entries without their own
  label. Null for a page never published and for a template.

See [024 module-surface.md](../../024-navigation/contracts/module-surface.md) §2.

### Added by 017

- `IPublishedContentReader.GetLiveAncestorsAsync(siteId, pageId)`: the live home page, then
  each live page above the given one, with its live title, for breadcrumbs. One query.
- `ISitemapReader`, `IRedirectReader` and `IRedirectHits` (Pages.Contracts): what the
  sitemap lists, the redirect matching an address, and the in-memory use counter.
- `IPublishedItemReader.ListAddressesAsync` (Cms.Contracts): published item slugs of a
  collection, keyset-paged, with the per-item hide switch.

See 017 [module-surface.md](../../017-seo/contracts/module-surface.md) §2–§5.

## 2. Shared infrastructure additions

- **`ReadOnlyConnection.From(string connectionString)`**: returns the same connection
  string with `Options=-c default_transaction_read_only=on` appended. Used by the three
  readers' contexts.
- **`ICacheProvider`** is reshaped (research PR-07):

  ```csharp
  Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
      CacheEntrySettings settings, CancellationToken ct = default);
  void Remove(string key);

  public sealed record CacheEntrySettings(long Size, TimeSpan? Sliding = null,
      TimeSpan? Absolute = null);
  ```

  Concurrent misses for one key share one factory call.
- **`OutboxMessage`** gains a way to set `AvailableAtUtc` when adding a message, for
  scheduled due messages (research PR-14).

---

## 3. Integration events

All events carry `Id`, `OccurredAtUtc`, `SiteId` and `ActorUserId?`, as the existing events
do. **No titles, names or content** (FR-044). Contract names are permanent: renaming needs a
V2.

| Event (`EventType`) | Extra fields | Published when |
|---|---|---|
| `Pages.PagePublished` | `PageId`, `VersionNumber`, `Kind` (`publish`, `scheduled`, `rollback`), `Path` | A page version becomes live |
| `Pages.PageUnpublished` | `PageId`, `Path`, `Scheduled` (bool) | A page stops being live |
| `Pages.ComponentPublished` | `ComponentId`, `VersionNumber`, `Kind` | A component version becomes live |
| `Pages.ComponentUnpublished` | `ComponentId` | A component stops being live |
| `Editor.DesignPublished` | `VersionNumber`, `Kind` (`publish`, `rollback`), `RestoredFromNumber?` | A design version becomes live |
| `Media.FileDeleted` | `FileId` | A media file was deleted (003's delete) |
| `Pages.PublishScheduleDue` | `ScheduleId`, `Revision` | **Module-internal**: written with `AvailableAtUtc = due time`, handled only by Pages (PR-14) |

**Handlers added by this feature**:

| Handler (module) | Handles | Effect | Idempotency |
|---|---|---|---|
| `RecordDesignPublishHandler` (Pages) | `Editor.DesignPublished` | Appends a `design` entry to `publish_log` | The entry id is the event id (primary key) |
| `RefreshPagesShowingMediaHandler` (Pages) | `Media.FileDeleted` | Bumps generations of dependent pages | Its own `processed_events` row in the Pages schema, as 009's Identity handler does |
| `RunDueScheduleHandler` (Pages) | `Pages.PublishScheduleDue` | Runs the schedule (data-model §3.1) | The schedule state and revision |

`specs/001-foundation-skeleton/contracts/integration-events.md` lists the new events.
