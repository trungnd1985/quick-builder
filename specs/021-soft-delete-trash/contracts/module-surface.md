# Module Surface: Soft Delete, Trash and Restore

What crosses module boundaries or the platform layer in feature 021. Research: TR-03, TR-06,
TR-08, TR-09, TR-15, TR-17.

## 1. Edges

**No new edges.** Every call below uses a Contracts project or a Shared project that the
caller already references:
- Pages → Editor.Contracts and Cms → Editor.Contracts were added by 023;
- Pages → Cms.Contracts and Cms → Media.Contracts already exist.

`ModuleBoundaryTests` stays unchanged.

## 2. Shared.Kernel

```csharp
namespace QuickBuilder.Shared.Kernel.MultiSite;   // beside ISiteOwned

/// An entity that can be in its site's trash (021 TR-02).
public interface ITrashable
{
    DateTimeOffset? TrashedAtUtc { get; }
}
```

## 3. Shared.Infrastructure

| Type | Purpose |
|---|---|
| `Data/QueryFilters` | `const string Site = "site"`, `const string Trash = "trash"` (TR-03) |
| `MultiSite/CrossSiteQuery.AcrossAllSites` | **Changed**: `IgnoreQueryFilters([QueryFilters.Site])` |
| `Trash/TrashQuery.IncludingTrash(reason)`, `OnlyTrash(reason)` | The only ways to see trashed rows; `BannedSymbols.txt` bans both `IgnoreQueryFilters` overloads elsewhere |
| `Trash/TrashOptions` | `Trash:RetentionDays`, 1 to 3650, default 30 (TR-15) |
| `Trash/TrashRetention` | `PurgeOn(trashedAtUtc)`, `DueBefore(now)` |

## 4. Editor.Contracts: `IEditingLocks` (changed, additive)

```csharp
/// The member who holds an active (not stale) lock on the document, when that member is not
/// userId; null otherwise. One indexed read, made before the caller's transaction (021 TR-09).
Task<EditingHolder?> FindActiveHolderAsync(
    Guid siteId, string documentKind, Guid documentId, Guid userId,
    CancellationToken cancellationToken = default);

public sealed record EditingHolder(Guid UserId, DateTimeOffset Since);
```

## 5. Media usage readers (changed, additive)

- `Pages.Contracts.Media.IMediaUsageReader` and `Cms.Contracts.Media.IItemMediaUsageReader`:
  every usage returned gains `bool InTrash`. The readers use `IncludingTrash` on the owner
  (TR-06).
- Media's `MediaUsageSummary` gains `TrashedUses`. Uses in the trash never set `InUse` or
  `InLiveUse` (FR-027).

## 6. Integration events

**Changed meaning** (shape unchanged). Each is now published when the content is moved to
the trash; their docs say "deleted (moved to the trash)":

| Event | Consumers (unchanged) |
|---|---|
| `Pages.PageDeleted` | Editor `ReleaseDeletedDocuments` |
| `Pages.ComponentDeleted` | Editor `ReleaseDeletedDocuments` |
| `Cms.ItemDeleted` (`WasPublished`) | Editor; Pages `RefreshPagesListingItems`, `RefreshPagesShowingMenuItems` |
| `Media.FileDeleted` | Pages `RefreshPagesShowingMedia`; Cms `ReindexItemsForMedia` |

**New**:

```csharp
public sealed record PageRestored(Guid Id, DateTimeOffset OccurredAtUtc, Guid SiteId, Guid PageId, Guid? ActorUserId) : IIntegrationEvent
{ public static string EventType => "Pages.PageRestored"; }

public sealed record ComponentRestored(Guid Id, DateTimeOffset OccurredAtUtc, Guid SiteId, Guid ComponentId, Guid? ActorUserId) : IIntegrationEvent
{ public static string EventType => "Pages.ComponentRestored"; }

public sealed record ItemRestored(Guid Id, DateTimeOffset OccurredAtUtc, Guid SiteId, Guid CollectionId, Guid ItemId, Guid? ActorUserId) : IIntegrationEvent
{ public static string EventType => "Cms.ItemRestored"; }

public sealed record FileRestored(Guid Id, DateTimeOffset OccurredAtUtc, Guid SiteId, Guid FileId, Guid? ActorUserId) : IIntegrationEvent
{ public static string EventType => "Media.FileRestored"; }
```

| New event | Consumers |
|---|---|
| `Media.FileRestored` | Pages `RefreshPagesShowingMediaHandler`: live pages show the image again (FR-026). Cms `ReindexItemsForMediaHandler`: clears "needs attention" for missing media. |
| `Pages.PageRestored`, `Pages.ComponentRestored`, `Cms.ItemRestored` | None in this feature. They are recorded in the registry for consumers outside the owning module; restored content is unpublished, so nothing public changes. |

"Restore and publish" also publishes the existing publish events (`PagePublished`,
`ItemPublished`, …) through the rollback path.

Purge publishes **no** event (TR-08). Events carry ids only, with no titles, names or
content.

Registry to update: `specs/001-foundation-skeleton/contracts/integration-events.md`.

## 7. Capabilities

| Capability | Scope | Administrator | Designer | Editor | Used by |
|---|---|---|---|---|---|
| `pages.trash.purge` | Site | ✓ | — | — | Permanent delete and empty, for pages and components |
| `cms.trash.purge` | Site | ✓ | — | — | Permanent delete and empty, for items |
| `media.trash.purge` | Site | ✓ | — | — | Permanent delete and empty, for files |

Viewing and restoring reuse the existing capabilities (trash-api.yaml).

Registry to update: `specs/002-identity-setup/contracts/authorization.md`.

## 8. Site transfer parts (020)

| Part | Change |
|---|---|
| Export | None. The filtered reads leave trashed rows out. |
| Restore import (replace) | The step that clears the site uses `IncludingTrash`, which empties the trash. |
| Merge import | Identity lookup of live rows only. A document whose identity is in the trash is added under a new id; the trashed copy stays in the trash. |

The archive format is unchanged, so `docs/contracts/site-archive` is untouched.
`specs/020-site-export-import/contracts/archive-parts.md` gains the two rules above.
