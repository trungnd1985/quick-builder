# Data Model: Soft Delete, Trash and Restore

**Feature**: [spec.md](./spec.md) · **Research**: [research.md](./research.md)

No new tables. Four content tables gain trash columns. Some unique indexes become partial,
and each table gets a partial index for the trash list. One configuration option is added.
Every change is additive and made by an automatic, forward-only migration per module.

## 1. Shared

### 1.1 `ITrashable` (Shared.Kernel)

A marker for entities that can be in the trash: `DateTimeOffset? TrashedAtUtc { get; }`.
`QueryFilterCoverageTests` asserts every `ITrashable` entity has the `Trash` named filter.

### 1.2 Query filter keys and helpers (Shared.Infrastructure)

| Name | Meaning |
|---|---|
| `QueryFilters.Site` | The site filter, renamed from anonymous on every entity (TR-03) |
| `QueryFilters.Trash` | `TrashedAtUtc == null`, on `ITrashable` entities only |
| `CrossSiteQuery.AcrossAllSites(reason)` | Now lifts `Site` only |
| `TrashQuery.IncludingTrash(reason)` | Lifts `Trash` only |
| `TrashQuery.OnlyTrash(reason)` | Lifts `Trash` and keeps `TrashedAtUtc != null` |

### 1.3 `TrashOptions` (configuration)

| Key | Type | Default | Range | Validation |
|---|---|---|---|---|
| `Trash:RetentionDays` | int | 30 | 1 to 3650 | Data annotations with `ValidateOnStart`. An invalid value stops startup and names the key. |

`TrashRetention.PurgeOn(trashedAtUtc) = trashedAtUtc + RetentionDays`. It is computed when
read and never stored.

## 2. Trash columns

Every trashable table gets:

| Column | Type | Notes |
|---|---|---|
| `trashed_at_utc` | `timestamptz NULL` | Set means the row is in the trash |
| `trashed_by` | `uuid NULL` | The member who deleted it |

Check constraint `ck_<table>_trash`: `(trashed_at_utc IS NULL) = (trashed_by IS NULL)`, and
every other `trashed_*` column below is null whenever `trashed_at_utc` is null.

Partial index `ix_<table>_trash`: `(site_id, trashed_at_utc DESC, id) WHERE trashed_at_utc
IS NOT NULL`.

### 2.1 `pages.pages`

Additional columns:

| Column | Type | Notes |
|---|---|---|
| `trashed_parent_id` | `uuid NULL` | No foreign key (TR-05). Null means it was top level. |
| `trashed_position` | `int NULL` | Its position among its siblings |
| `trashed_path` | `varchar NULL` | The address when deleted; shown and searched in the trash |
| `trashed_was_home` | `bool NULL` | |
| `trashed_template_collection_id` | `uuid NULL` | TR-12 |
| `trashed_had_schedule` | `bool NULL` | TR-07 |

On trash: `parent_id`, `is_home` (false) and `template_collection_id` are cleared, `slug`
and `path` are kept, and `depth` is kept.

Index changes:
- `ux_pages_site_path` → `UNIQUE (site_id, path) WHERE trashed_at_utc IS NULL`.
- Unchanged: `ux_pages_site_home` (filtered on `is_home`, which is false when trashed) and
  `ux_pages_site_template_collection`.
- `ck_pages_home_top_level` still holds, because `is_home` is false.

### 2.2 `pages.components`

Additional columns: `trashed_was_live bool NULL` and `trashed_had_schedule bool NULL`.

Index change: `ux_components_site_name` → `UNIQUE (site_id, name) WHERE trashed_at_utc IS
NULL`.

### 2.3 `cms.items`

Additional columns: `trashed_was_live bool NULL` and `trashed_had_schedule bool NULL`.

No index change. Slugs are reserved in `item_slugs`, whose rows are deleted on trash
(TR-04).

### 2.4 `media.media_files`

Additional column: `trashed_folder_id uuid NULL`. It has no foreign key; null means the file
was at the top level.

On trash, `folder_id` is cleared. The storage key and variants are kept, and the `version`
row token changes.

## 3. State and transitions

```text
            delete (guards of 010/012/013/015 + lock check TR-09)
   live ─────────────────────────────────────────────────────────▶ trashed
    ▲                                                                 │
    │ restore (plan → confirm, TR-10) [+ publish, TR-11]              │ permanent delete (Admin, TR-14)
    └─────────────────────────────────────────────────────────────────┤ purge (system, retention, TR-13)
                                                                      ▼
                                                                    gone
```

- **live → trashed**, in one module transaction:
  - set the trash columns, detach (TR-05) and delete the guard rows (TR-06);
  - shelve publishing (TR-07): cancel schedules, stop a live component or item being live,
    and write a log or history entry;
  - publish the existing `*Deleted` event (TR-08).
- **trashed → live**, in one module transaction:
  - re-check that the row is still trashed, compute the plan, and compare it with the
    confirmed address (`trash.plan_changed`);
  - clear the trash columns, place the document, and rebuild the guard rows;
  - write a log or history entry and publish `*Restored`;
  - with `publish`, run the rollback-to-last-live-version path in the same transaction.
- **trashed → gone**, in one module transaction per entry:
  - re-check that the row is still trashed (and due, for the purge);
  - `ForgetAsync` for pages and components, the cascade for items, the row for media
    (bytes after commit);
  - write a publish log `purged` entry for pages and components.
- **Concurrency**: the row's existing token (`xmin` for components and media, the page
  tree's `xmin` for pages, `revision` for items) makes a restore racing a purge, or two
  restores, end with exactly one winner. The loser gets `trash.not_in_trash` (FR-020).

## 4. Rules

| Rule | Where | Source |
|---|---|---|
| A page with live children cannot be deleted; trashed children do not count | `PageTree.Trash` (filters load live pages only) | 010 FR-012, clarification Q1 |
| A home page can be deleted only when it is the only page | `PageTree.Trash` | 010 FR-013 |
| A restored top-level page becomes home when the site has none | `PageTree.Restore` | Spec edge case |
| Restore is refused at the site's page limit | `PageTree.Restore` | TR-10 |
| A restored address that is taken gets the next free one | `PageAddresses`, `SlugRules`, `CopyNaming` | FR-017, TR-04 |
| Parents in the trash: restore them too or choose a place; a parent that is gone: choose a place | `PageTree.PlanRestore` and `PagesTrash.PlanPageAsync` (unit-tested) | FR-016 |
| A restored item is validated and marked `attention`, not refused | `ItemValidation` | FR-018 |
| An item restore is refused during a schema change | `CollectionWriteGate` | 014 FR-037 |
| A media restore needs delete-any, or delete-own for one's own upload, plus publish when live use | `RestoreFileHandler` | FR-013, 013 FR-016 |
| Deleting a collection deletes its trashed items | Database cascade; the confirmation counts `IncludingTrash` | FR-023 |
| Trashed uses never count as `InUse` or `InLiveUse` | `MediaUsage` | FR-027 |

## 5. Errors (new codes)

| Code | HTTP | When |
|---|---|---|
| `editing.lock_held` | 409 | A delete while another member holds the active lock. The body carries the holder and since (TR-09). |
| `trash.not_in_trash` | 404 | A restore or purge of an entry that is no longer in the trash |
| `trash.plan_changed` | 409 | The confirmed address or placement no longer matches. The body carries the new plan. |
| `trash.placement_required` | 422 | The original parent is trashed or gone and no placement was given |
| `trash.invalid_placement` | 422 | The chosen parent or folder is not live, or is under the page itself |
| `trash.count_changed` | 409 | Emptying the trash with an expected count that no longer matches. The body carries the current count. |
| `pages.too_many_pages` | 409 | Existing code, reused for restore at the site's page limit |
| (014 FR-037's existing refusal) | 409 | Existing, reused for an item restore while a schema change is applied |
