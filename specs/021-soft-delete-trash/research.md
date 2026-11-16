# Research: Soft Delete, Trash and Restore

**Feature**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #21

Decisions are numbered TR-01 … TR-22 and referenced from the plan, the data model and the
contracts. Each one states the decision, why, and what else was considered.

---

## TR-01 Where the trash lives: in each owning module

**Decision**: No new module and no shared trash table. Each module that owns a trashable
document implements its trash in a vertical slice `Features/Trash/`:

| Content | Module | Table |
|---|---|---|
| Pages | Pages | `pages.pages` |
| Shared components | Pages | `pages.components` |
| Collection items | Cms | `cms.items` |
| Media files | Media | `media.media_files` |

The admin's trash view has one tab per content type (FR-009), and each tab calls only the
owning module's endpoints, so nothing has to aggregate across modules.

**Rationale**: Principle I. The row that is "in the trash" is the content row itself, so its
draft, versions and history stay where they are and restore is a flag change plus
rebuilding derived rows in the same module transaction.

**Alternatives considered**:
- *A `Trash` module storing snapshots of deleted content*: it would read and write other
  modules' tables or duplicate every document format, and a restore would have to replay a
  snapshot through another module. Rejected.
- *One `platform.trash_entries` table written by every module*: a cross-module write per
  delete, and a second source of truth next to the content row. Rejected.

---

## TR-02 Representation: trash columns on the content row

**Decision**: Each trashable table gains nullable columns, all set or all null (check
constraint `ck_<table>_trash`):

- `trashed_at_utc timestamptz`, `trashed_by uuid`;
- `trashed_was_live boolean` and `trashed_had_schedule boolean` (pages, components, items;
  clarification Q4 of this spec);
- the original place (TR-05).

The domain term is **trashed**, to keep it apart from the permanent deletes that still exist
(collections, menus, folders, purge). Entities implement a new `ITrashable` marker in
`QuickBuilder.Shared.Kernel` (`DateTimeOffset? TrashedAtUtc`).

**Rationale**: The content keeps its identity (FR-014) with no copying. Nullable columns are
an additive, forward-only migration (Principle II, III).

**Alternatives considered**: a boolean `is_deleted` (no "when", needed for the purge date
and sort); moving rows to `*_trash` shadow tables (every foreign key and index duplicated,
and restore becomes a cross-table copy).

---

## TR-03 Hiding trashed rows: EF Core 10 named query filters

**Decision**:
1. Every query filter in every module becomes a **named** filter. Site filters use the key
   `QueryFilters.Site`; the four trashable entities add a second filter
   `QueryFilters.Trash` (`e => e.TrashedAtUtc == null`). EF Core 10 (10.0.12, already
   referenced) supports several named filters per entity and lifting them selectively; it
   does not allow anonymous and named filters on the same entity, which is why the site
   filters are renamed everywhere (mechanical, no migration).
2. `CrossSiteQuery.AcrossAllSites(reason)` lifts **only** `QueryFilters.Site`. Every
   existing admin read (`PagesDbContext.ForSite`, the Cms and Media equivalents) therefore
   keeps hiding trashed rows with no change to the handler (FR-006: "without each feature
   having to remember").
3. A new `TrashQuery.IncludingTrash(reason)` in `Shared.Infrastructure` is the only way to
   see trashed rows, and `TrashQuery.OnlyTrash(reason)` adds `TrashedAtUtc != null`.
4. `BannedSymbols.txt` also bans the keyed overload
   `IgnoreQueryFilters<T>(IQueryable<T>, IReadOnlyCollection<string>)`; the two helper files
   hold the only suppressions.
5. Architecture tests:
   - `QueryFilterCoverageTests` (renamed from `SiteFilterCoverageTests`): every
     `ISiteOwned` entity has the `Site` filter and every `ITrashable` entity has the `Trash`
     filter;
   - `TrashQueryUsageTests`: `IncludingTrash` and `OnlyTrash` are called only from
     `Features/Trash/`, the purge services (TR-13), the media orphan sweep (TR-18) and the
     transfer archive parts (TR-17), each with a reason.

**Dependents** (drafts, versions, usage rows) carry no trash filter. Every endpoint that
names a document by id already loads the document first; `TrashedIdProbe` (TR-21) calls
every endpoint in `EndpointCatalog` with the id of a trashed document and expects the
not-found answer, the same way `IsolationProbe` does for other sites' ids.

**Raw SQL**: one raw SQL read touches trashable content: `GetTokenUsage` (Pages), which
counts design-token uses in page and component drafts. It joins the owning row and adds
`trashed_at_utc IS NULL`, with a test (constitution: raw SQL applies filters explicitly).
The other raw SQL is Cms's advisory collection locks, and Forms and Identity, none of which
read trashable data.

**Navigations**: filters apply to `Include`, so `PageTree` loads only live pages and the
tree's rules (children count, home page, page count limit, renumbering) ignore trashed pages
without change.

**Alternatives considered**: hand-written `Where(!trashed)` in every handler (exactly what
FR-006 forbids); a database view per table (EF mapping duplicated, writes through views);
row-level security (a second filtering mechanism beside EF, and the app connects as one
role).

---

## TR-04 Unique addresses and names are freed while trashed

**Decision**: Uniqueness ignores trashed rows (FR-004):
- `ux_pages_site_path` becomes partial `WHERE trashed_at_utc IS NULL`. `ux_pages_site_home`
  and `ux_pages_site_template_collection` are kept and need no change, because trashing
  clears `is_home` and `template_collection_id` (TR-05).
- `ux_components_site_name` becomes partial too. Component names are freed like page
  addresses, and a clash on restore is resolved with the copy-naming rule from 023
  (`CopyNaming`, "Header (2)"). This extends FR-004 and FR-017 to component names; see the
  plan's Spec alignment.
- Item slugs are reserved through `cms.item_slugs` rows (015 research IT-03). Trashing an
  item deletes its `item_slugs` rows; restore re-adds them through `ItemIndexer` (TR-06).
- Media addresses are the file id and are never reused, so nothing changes (FR-004).

On restore, a taken address gets the next free one from the owning module's existing rule
(`PageAddresses` for slugs with `-2`, `-3`…; `SlugRules` for items), and the restore plan
shows the old and new address before confirming (FR-017, TR-10).

---

## TR-05 Original place, detached from foreign keys

**Decision**: A trashed row is detached from the structures whose foreign keys or checks it
would otherwise block, and its original place is kept in plain columns without foreign keys:

| Table | Cleared on trash | Kept as |
|---|---|---|
| `pages` | `parent_id` → null, `is_home` → false, `template_collection_id` → null | `trashed_parent_id`, `trashed_position`, `trashed_path`, `trashed_was_home`, `trashed_template_collection_id` |
| `media_files` | `folder_id` → null | `trashed_folder_id` |
| `items` | nothing (the collection foreign key cascades, which is FR-023) | — |
| `components` | nothing (no place) | — |

**Rationale**:
- `pages.parent_id` is `ON DELETE RESTRICT`. A trashed child that kept its `parent_id` would
  block purging or permanently deleting the parent.
- `ck_pages_home_top_level` and `ux_pages_site_home` would otherwise keep a trashed home
  page as the home page.
- `media_files.folder_id` is `RESTRICT`. 013 FR-002 lets a folder be deleted only when it is
  empty, and the spec says a folder that holds only trashed files counts as empty.
- Keeping the original values lets restore decide "original place still exists" (FR-016)
  and lets the trash view show where the entry was (FR-005).

**Alternatives considered**: keeping `parent_id` and switching the foreign key to
`SET NULL`, which silently loses the parent and changes 010's backstop for live pages.

---

## TR-06 Derived rows: guard rows go, usage rows stay

**Decision**: Rows derived from a document fall into two groups.

**Guard rows** are rows that make the document block or claim something for others. They
are deleted on trash and rebuilt from the stored content on restore with the module's
existing extractors:
- Pages: `component_usages` (blocks deleting a component, 010 FR-028), `collection_usages`
  (blocks deleting a collection, 016 FR-010), and `live_dependencies` (none for an
  unpublished page; cleared for a component).
- Cms: `item_slugs` (TR-04) and `item_references` where the item is the source (blocks
  deleting the target, 015 FR-008).

**Usage rows** are kept, so media usage can say "used by content in the trash" (FR-027):
`draft_media_references`, `version_media_references` (Pages) and `item_media` (Cms). The
readers behind `IMediaUsageReader` and `IItemMediaUsageReader` join the owner with
`IncludingTrash` and return each use with `InTrash`. Media's `MediaUsage` reports trashed
uses separately, and they never count toward `InUse` or `InLiveUse` (013 FR-015, FR-016).

**Purge** deletes the row; the remaining dependents cascade as they do today.

**Rationale**:
- Deleting the guard rows is what makes "a trashed page does not block deleting a component
  or collection" and "a trashed item does not block deleting what it refers to" true, with
  no change to the guard queries.
- Rebuilding them on restore uses code that already runs on every save (010, 015, 016).
- A reference to something that has disappeared meanwhile is then handled as 015 and 016
  already handle it (needs attention, bound to a deleted collection).

**Alternatives considered**: keeping every row and filtering the guard queries by joining the
owner (each guard would need the join, and the foreign keys from `item_references` and
`component_usages` would still block purges).

---

## TR-07 What happens to publishing state on trash

**Decision**:
- **Page**: unchanged guard, a live page is refused (012 FR-006). Pending schedules are
  cancelled with reason `trashed`, and `trashed_had_schedule` records it. Versions and the
  publication row are kept.
- **Component**: the guard is unchanged (refused while any live page uses it, 012 FR-004).
  If the component itself has a live version, that version stops being live and
  `trashed_was_live` is set. Versions are kept and schedules are cancelled.
- **Item**: if it is published, the item is unpublished as part of the trash
  (`ItemPublishing`, the same effect as 015's unpublish) and `trashed_was_live` is set.
  Schedules are cancelled; versions and history are kept.
- **Records**:
  - pages and components: the publish log (012 FR-017) gains the actions `trashed`,
    `restored` and `purged` (the permanent delete keeps today's `deleted`);
  - items: `item_history` gains `trashed` and `restored`.

`PublishingWrites.ForgetAsync` (used by today's delete) becomes the purge step. A new
`PublishingWrites.ShelveAsync` does the trash step.

---

## TR-08 Integration events

**Decision**:
- **Trashing publishes the existing events.** `PageDeleted`, `ComponentDeleted`,
  `ItemDeleted` (with `WasPublished`) and `FileDeleted` are published at trash time,
  because their consumers must react to trashing:
  - Editor releases sessions;
  - Pages refreshes item lists, menus and pages showing the media;
  - Cms re-indexes items for media.

  Their shape is unchanged. Their documentation changes from "permanently deleted" to
  "deleted (moved to the trash or deleted permanently)".
- **New events, one per kind, published on restore**: `PageRestored`, `ComponentRestored`,
  `ItemRestored` and `FileRestored`. These are the consumers:
  - `FileRestored`: Pages `RefreshPagesShowingMediaHandler` (live pages show the image
    again, FR-026) and Cms `ReindexItemsForMediaHandler` (clears "needs attention" for
    items whose media came back);
  - `ItemRestored`: no public effect, because the item comes back unpublished. It is
    published for the registry and for future consumers.
- **No purge events.** Every consumer already acted at trash time, so a purge is recorded in
  logs (ids only) and in the publish log for pages and components.

**Alternatives considered**:
- *New `...Trashed` events*: every consumer would have to subscribe twice, and a missed
  subscription would leak trashed content.
- *A `Permanent` flag on the deleted events*: no consumer needs it.

---

## TR-09 Delete refused while another member holds the edit lock

**Decision**: `IEditingLocks` (Editor.Contracts) gains
`FindActiveHolderAsync(siteId, kind, documentId, userId)`. It returns the holder when a
live, not-stale lock is held by **another user**; a member's own lock in another tab does
not block. The page, component and item delete handlers call it before their transaction
and refuse with `editing.lock_held` (409, holder and since), reusing 023's refusal shape
(clarification Q2 of this spec, FR-008).

The read happens before the transaction, as 023 CE-07 does. A lock claimed between the check
and the commit is the accepted race: the claimant's next save fails and the editor shows
"moved to the trash" (TR-16).

---

## TR-10 Restore: plan first, then confirm

**Decision**: Restore is two steps, so that the member is told before confirming (FR-016,
FR-017, SC-008).
- `GET …/trash/{kind}/{id}/restore-plan` computes what would happen:
  - the target place;
  - whether the parents must be chosen (parent trashed: `with_parents` or `chosen`; parent
    gone: `chosen`, top level preselected);
  - the resulting address and whether it changed;
  - the template slot (TR-12);
  - the schema issues for items;
  - `wasLive`, `hadSchedule`, and whether "Restore and publish" is available.
- `POST …/restore` carries the member's placement choice and the **confirmed address**. If
  the plan computed at commit differs (another page took the address meanwhile), it is
  refused with `trash.plan_changed` and the new plan, and nothing changes.

**Placement rules**:
- **Original**: the original parent exists and is live. The page goes back at
  `min(trashed_position, sibling count)`.
- **With parents**: the trashed ancestors are restored top-down in one tree change. Each
  goes back to its own original place, and each address is resolved.
- **Chosen**: the member picks a live parent or the top level, and the page goes at the end.
- **Home**: a restored top-level page becomes the home page when the site has none (spec
  edge case).
- **Limits**: restore is refused when the site is at its page limit
  (`PageAddresses.MaxPagesPerSite`, `pages.too_many_pages`), since trashed pages do not count
  toward it.
- **Redirects**: if the target address is a redirect source (017), the rule that creating or
  moving a page at that address uses applies.
- **Items**:
  - the slug is re-added (TR-04);
  - the item is validated against the current schema and marked with `attention` instead of
    being refused (015, FR-018);
  - restore is refused while a schema change runs (`CollectionWriteGate`, 014 FR-037).
- **Media**: the original folder if it still exists, otherwise the top level or a folder
  the member chooses.

**Authorisation** (FR-013):

| Content | Capability to restore | Also needed |
|---|---|---|
| Page | `pages.structure.manage` | — |
| Component | `pages.components.manage` | — |
| Item | `cms.items.edit` | — |
| Media file | `media.files.delete`, or `media.own_files.delete` for one's own upload | `pages.publishing.manage` when a live page uses the file (013 FR-016) |

**Alternatives considered**: a single restore call that silently moves content to the top
level, which fails SC-008.

---

## TR-11 "Restore and publish"

**Decision**: `restore` takes `publish: true`. In the same module transaction, after the
restore, it runs the module's existing rollback-to-version path:
- 012 Rollback for pages and components;
- 015 RollbackItem for items.

That path publishes the **last version that was live**, and the action is recorded as a
restore and a publish (FR-015). It requires the publish capability as well:
`pages.publishing.manage` for pages and components, `cms.items.publish` for items.

It is offered only when such a version exists. Content that was only scheduled and never
published comes back unpublished, and the restore says it had been scheduled. This narrows
clarification Q4's "publishes its last published version" to "when there is one"; see the
plan's Spec alignment.

---

## TR-12 Template pages

**Decision**: Trashing a collection template page (016) clears `template_collection_id`
into `trashed_template_collection_id`, so the collection has no template while the page is
in the trash, as if it had been deleted. On restore:
- if the collection still exists and has no template, the page becomes its template again;
- otherwise the page is restored as a normal page, and the restore plan says so.

---

## TR-13 Purge job: one service per module, hourly, a conditional delete

**Decision**: `PagesTrashPurgeService`, `CmsTrashPurgeService` and `MediaTrashPurgeService`
are `BackgroundService`s on a `PeriodicTimer` of 1 hour, with the same shape as
`TransferRetentionService`. Each tick:
1. Selects up to 100 due rows (`trashed_at_utc <= now − retention`) across all sites with
   `AcrossAllSites` and `OnlyTrash`.
2. Purges each in its own transaction, exactly as a permanent delete (TR-14) with actor
   "system". The delete is conditional on the row still being trashed and due, so a restore
   that won the race leaves it alone.
3. For media, deletes the bytes and variants after commit. A failure leaves them to the
   orphan sweep (003 FR-012).

Several instances may select the same row; only one conditional delete finds it, so each row
is purged once, and each row commits or rolls back on its own (FR-024). The purge runs at least daily, and an entry goes
within about an hour of its purge date (SC-006: within 24 hours).

**Alternatives considered**:
- *One shared purge service calling each module through Contracts*: Shared.Infrastructure
  would have to know modules.
- *Daily at a fixed time*: it bunches the load, and an instance that is down at that time
  skips a day.
- *Advisory-lock leader election*: not needed when the work is row-claimed.

---

## TR-14 Permanent delete and emptying the trash

**Decision**:
- **Single entry**: `DELETE …/trash/{kind}/{id}` purges one entry. It requires
  `<module>.trash.purge`, a new site-scoped capability granted to Administrators only:
  `pages.trash.purge`, `cms.trash.purge` and `media.trash.purge` (FR-021). The capability is
  declared per module because capabilities are `<module>.<noun>.<verb>`.
- **Several entries**: `POST …/trash/purge` with a list of up to 100 ids.
- **Empty**: `POST …/trash/empty` with `trashedUpTo` (the time the admin loaded the counts)
  and the expected count per kind. Only entries trashed at or before `trashedUpTo` are
  purged (FR-021). If the count no longer matches, the request is refused with
  `trash.count_changed` and the current count, so the Administrator confirms again. The
  admin's "Empty trash" calls Pages, Cms and Media in turn, each with its own counts, and
  shows per-module results. A partial failure leaves the remaining entries restorable.
- **What a purge removes**: what today's delete removes (003 FR-004, 010 FR-014, 012
  FR-006, 015 FR-008):
  - pages and components: `ForgetAsync`, which keeps the publish log entries labelled with
    the last title and path;
  - items: the cascade, including `item_history`, as 015 FR-008 does (see Spec alignment);
  - media: the row, then the bytes and variants.

---

## TR-15 Retention setting

**Decision**:
- **Key**: `Trash:RetentionDays`, an integer, default 30, `[Range(1, 3650)]`, bound to
  `TrashOptions` in `Shared.Infrastructure/Trash` and validated with `ValidateOnStart` in
  `Program.cs` (FR-025, Principle II: an invalid value stops startup and names the key).
- **Purge date**: computed when it is read, as `trashed_at_utc + retention`
  (`TrashRetention.PurgeOn`), so a changed setting applies to every entry at once. Nothing
  stores the purge date.
- **Docs**: `docs/configuration.md`.

**Why days rather than a TimeSpan** like `Outbox:RetentionPeriod`: the spec and operators
think in days, and a TimeSpan accepts meaningless values such as 90 minutes.

**Why in Shared.Infrastructure**: three modules need the same value. It is a platform
setting like the outbox retention, and it has no module concept in it.

---

## TR-16 Members who have the document open

**Decision**: 023 already releases sessions on `PageDeleted`, `ComponentDeleted` and
`ItemDeleted` (`ReleaseDeletedDocumentsHandler`), and its heartbeat and saves answer
not-found for a document that is gone. The admin's `useEditingSession` reacts to that answer
by calling `GET …/trash/{kind}/{id}`, which needs the view capability. When the document is
in the trash, the editor shows "Moved to the trash by {name} at {time}". It keeps 011's
read-only view with copyable blocks, and it links to the trash when the member may restore
(FR-008).

---

## TR-17 Site export and import (020)

**Decision**:
- **Export**: the archive parts read through the normal filtered sets, so trashed rows are
  left out without any change (FR-030). `TransferTrashTests` proves it.
- **Restore import** (replace): each part's "clear the site" step uses `IncludingTrash`, so
  the trash is emptied with the rest. Without that, trashed rows would collide with imported
  ids.
- **Merge import**: the identity lookup matches live rows only. A document whose identity is
  in the trash is added under a new id, and the trashed copy stays in the trash. Replacing it
  and taking it out of the trash is deferred (`docs/backlog.md`): it would add a report line
  and a second way out of the trash, for a case with no collision and no loss.
- **Archive format** is unchanged, so `docs/contracts/site-archive` does not change. The
  part classes do change, and `specs/020-site-export-import/contracts/archive-parts.md` is
  updated in this PR.

---

## TR-18 Background readers that must see trashed rows

**Decision**: These readers must count trashed rows as existing:
- the media orphan sweep, so it never deletes the bytes of a trashed file;
- the variant worker;
- the image-metadata and search-text backfills;
- the item index backfill.

Each switches to `IncludingTrash` and joins `TrashQueryUsageTests`' allowlist.
`MediaOrphanSweepTrashTests` proves that a trashed file's bytes survive a sweep.

---

## TR-19 Trash listing and search

**Decision**:
- **Index**: each table gets a partial index
  `(site_id, trashed_at_utc DESC, id) WHERE trashed_at_utc IS NOT NULL`. Lists are
  keyset-paged, 50 per page, newest first (FR-010).
- **Search**: `ILIKE` over the fields below, inside the partial index's rows. At the 10,000
  entries of SC-003 that is a few milliseconds.

  | Content | Searched fields |
  |---|---|
  | Pages | title, `trashed_path` |
  | Components | name |
  | Items | name, slug |
  | Media | `search_text` |

- **Filters**: `trashedBy`, a `from`/`to` date range, `collectionId` for items and
  `trashedFolderId` for media.
- **Media total**: the media list response carries `totalBytes` of the site's trashed files
  (FR-012).
- **Names**: the names of the members who deleted entries come from `IUserDirectory`
  (Identity.Contracts, already used for history).

**Alternative considered**: trigram indexes, which are not needed at this volume.

---

## TR-20 Admin

**Decision**:
- **Screen**: a new `src/admin/src/features/trash/` with:
  - `TrashView.vue`: tabs Pages, Components, Items and Media, shown per view capability;
    search and filters; multi-select;
  - `TrashPreview.vue`: read-only. It shows who deleted the entry, the text of its draft
    (the name, for a file) and a Restore button. The editor canvas, the read-only item form
    and an image of a trashed file are deferred (`docs/backlog.md`);
  - `RestoreDialog.vue`: shows the plan, the placement choice, the address change and
    "Restore and publish";
  - permanent delete of the selected entries asks for confirmation, and
    `EmptyTrashDialog.vue` requires typing the count. Both are Administrator only.
- **Route and navigation**: route `/sites/:siteId/trash` and an entry in the site's
  navigation.
- **Delete dialogs**: the dialogs for pages, components, items and media now say "Moved to
  the trash. It can be restored until {date}" (FR-002). Each refusal names the lock holder
  (TR-09).
- **Usage view**: the media usage view shows "used by content in the trash" separately
  (FR-027).
- **Accessibility**: keyboard paths and axe checks (FR-031).

No new admin libraries.

---

## TR-21 Tests

- **Unit**:
  - `PageTreeTrashTests` (pages: original, with parents, chosen, home, template, page
    limit, slug clashes);
  - `TrashRetentionTests`.
- **Integration** (Testcontainers):
  - `TrashRoundTripTests`, one per kind, for SC-001: trash, then restore, then compare
    identity, draft, versions and address, and check that references work;
  - `TrashedIdProbe` (TR-03), joining the isolation suite for SC-004;
  - `TrashVisibilityTests`: public render, sitemap, menus, media address, pickers, counts
    and search;
  - `TrashCacheTests`: media and item trash and restore refresh exactly the dependent
    pages, on two instances, for SC-005;
  - `TrashRestoreRulesTests`: parent trashed or gone, folder gone, schema changed,
    collection deleted, referenced item gone, address taken, `plan_changed`;
  - `TrashAuthorizationTests`: restore mirrors delete, purge and empty are
    Administrator-only, and other sites answer not-found (SC-007);
  - `TrashLockTests` (TR-09);
  - `TrashPurgeTests`: retention, two instances, a restore racing a purge, and emptying
    (`trashedUpTo`, `count_changed`) (SC-006);
  - `TrashBulkEndpointTests`: the bulk and per-kind endpoints of each module;
  - `TransferTrashTests` (TR-17);
  - `MediaOrphanSweepTrashTests` (TR-18);
  - `TrashConfigurationTests`: invalid `Trash:RetentionDays` fails startup;
  - `UpgradeFrom020Tests`: a 020 snapshot upgrades, the trash is empty, addresses stay
    unique, and trash and restore work on upgraded rows;
  - `TrashListPerformanceTests` (SC-003), opt-in like `TransferPerformanceTests`.
- **Architecture**: `QueryFilterCoverageTests`, `TrashQueryUsageTests`,
  `AuthorizationDeclarationTests` (new endpoints) and `ModuleBoundaryTests` (no new edges).
- **Admin** (vitest + axe): `TrashView.spec.ts`, `RestoreDialog.spec.ts`,
  `EmptyTrashDialog.spec.ts`, and the delete dialogs' text.
- **Coverage**: 80% line coverage on the restore and purge handlers and on the page tree's
  restore, which sits next to publish on the critical path.

---

## TR-22 Things looked at and left out

- **"Undo" in the toast after a delete**: useful, but the spec does not ask for it. It is
  recorded as deferred.
- **Trash for collections, menus and folders**: out of scope (spec).
- **Per-site retention**: clarification Q3 of this spec chose an installation-wide setting.
- **Restoring old field values**: out of scope; versions cover it.
