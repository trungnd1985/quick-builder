# Contract: What Items Add to the Cms Module's Surface

**Feature**: [../spec.md](../spec.md) | **Research**: IT-13, IT-15, IT-16, IT-17

Additions to the shared registries and to other modules, on top of
[014 module-surface.md](../../014-cms-collections-schema/contracts/module-surface.md).

## 1. Project references

| Project | Change |
|---|---|
| `QuickBuilder.Shared.Kernel` | + `Text/SearchText.cs`, moved unchanged from `QuickBuilder.Media/Domain` (IT-13). |
| `QuickBuilder.Media` | Uses `SearchText` from the kernel. + reference to `QuickBuilder.Cms.Contracts` (IT-15). |
| `QuickBuilder.Cms` | No new references. |

No cycle: Cms → `Media.Contracts` and Media → `Cms.Contracts` are both contract-only
references. `ModuleBoundaryTests` gains the Media → `Cms.Contracts` edge in its allowed list.

## 2. `Cms.Contracts`: item media usage reader

```csharp
namespace QuickBuilder.Cms.Contracts.Media;

/// Where a site's media files are used by collection items (015 research IT-15).
public interface IItemMediaUsageReader
{
    Task<IReadOnlyDictionary<Guid, ItemMediaUsageSummary>> SummariseAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ItemMediaUsagePlace>> ListAsync(
        Guid siteId, Guid mediaId, CancellationToken cancellationToken = default);
}

public sealed record ItemMediaUsageSummary(int Items, int DraftItems, int LiveItems, int HistoryOnlyItems);

/// Name and CollectionName are content: render as text, never log.
public sealed record ItemMediaUsagePlace(
    Guid ItemId, string? Name, string Slug, Guid CollectionId, string CollectionName,
    bool InDraft, bool Live, bool InScheduled, bool InHistory);
```

Implemented by Cms over `item_media`, reading the named site (`ForSite<T>`). Media adds the
counts to `MediaUsageSummary` (live use and in-use rules unchanged in shape, 013 MV-06), and
lists item places after pages and components, only to callers holding
`cms.collections.view`.

## 3. Capabilities (added to 002 contracts/authorization.md §2)

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `cms.items.edit` | Site | ✓ | ✓ | ✓ |
| `cms.items.publish` | Site | ✓ | ✓ | ✓ |

## 4. Integration events (added to 001 contracts/integration-events.md)

| Event | Published by | Payload | When |
|---|---|---|---|
| `Cms.ItemCreated` | Cms | `siteId`, `collectionId`, `itemId`, `actorUserId` | Created, including by duplicate |
| `Cms.ItemDraftSaved` | Cms | `siteId`, `collectionId`, `itemId`, `actorUserId` | Draft saved or copied from a version |
| `Cms.ItemPublished` | Cms | `siteId`, `collectionId`, `itemId`, `versionNumber`, `kind`, `slugChanged`, `actorUserId` | Publish, scheduled publish or rollback |
| `Cms.ItemUnpublished` | Cms | `siteId`, `collectionId`, `itemId`, `scheduled`, `actorUserId` | Manual or scheduled unpublish |
| `Cms.ItemDeleted` | Cms | `siteId`, `collectionId`, `itemId`, `wasPublished`, `actorUserId` | Deleted |

Ids and flags only. No consumer in this feature; #16 subscribes to the three that change
published data (`ItemPublished`, `ItemUnpublished`, `ItemDeleted` with `wasPublished`).

Cms now **consumes** `Media.FileDeleted` and `Media.FileChanged` (re-index the items using
the file, IT-11). Both handlers are idempotent: re-indexing is a pure recomputation.

## 5. Obligations

- **#16 renders only `items.published`** through a read path with no write access (Principle
  VII), and treats references to items without published values as absent (clarification Q4).
- **#16 closes E-6** (014 plan) before rendering items: schema changes rewrite published
  values *and* kept versions (IT-12).
- **#20 (export/import)** exports items with draft, published, versions and schedules;
  `item_slugs`, `item_references` and `item_media` are derived and rebuilt by the indexer.
- **Every item write** goes through `ItemWrites` (IT-06); the architecture test
  `ItemWritesOnlyTests` fails if another type modifies `Item`.
