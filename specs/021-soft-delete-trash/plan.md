# Implementation Plan: Soft Delete, Trash and Restore

**Branch**: `021-soft-delete-trash` | **Date**: 2026-11-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-soft-delete-trash/spec.md`

**Issue**: #21.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none.

## Summary

Deleting a page, shared component, collection item or media file now moves it to its site's
trash instead of removing it. In the trash it behaves as deleted everywhere. Members who may
delete it can restore it with its identity, draft, versions and references for 30 days (set
by the operator). A restore whose original place is gone says where the content will go
before it happens. Administrators can delete permanently or empty the trash, and an hourly
job purges what is past the retention period.

The approach, detailed in [research.md](./research.md):
- **Owner** (TR-01, TR-02): each owning module keeps the trashed row where it is and marks
  it with trash columns:
  - Pages: pages and components;
  - Cms: items;
  - Media: files.

  There is no new module and no shared trash table.
- **Hiding** (TR-03):
  - every query filter becomes an EF Core 10 **named** filter, and the four trashable
    entities gain a `Trash` filter;
  - `AcrossAllSites` now lifts only the site filter, so every existing admin and public read
    hides trashed rows unchanged;
  - `TrashQuery.IncludingTrash/OnlyTrash` is the only way to see them, guarded by banned
    symbols and architecture tests;
  - `TrashedIdProbe` checks every endpoint.
- **Freeing and detaching** (TR-04, TR-05, TR-06):
  - unique indexes on page paths and component names become partial, and item slug rows are
    removed;
  - a trashed page or file is detached from its parent or folder, and the original place is
    kept in plain columns;
  - guard rows (component, collection and item references) are removed and rebuilt on
    restore;
  - media-use rows are kept, so usage can say "used by content in the trash".
- **Publishing** (TR-07, TR-11):
  - trash cancels schedules and stops a live component or item being live, and keeps
    versions;
  - restore brings content back unpublished;
  - "Restore and publish" runs the existing rollback to the last live version.
- **Events** (TR-08): the existing `*Deleted` events fire at trash time; new `*Restored`
  events (`FileRestored` refreshes live pages); no purge events.
- **Locks** (TR-09): delete is refused while another member holds an active 023 lock
  (`IEditingLocks.FindActiveHolderAsync`).
- **Restore** (TR-10): a plan endpoint, then a restore that carries the confirmed address
  and is refused with `trash.plan_changed` if it no longer holds.
- **Removal** (TR-13, TR-14, TR-15):
  - per-module purge services, hourly, a conditional delete, one transaction per
    entry;
  - Administrator-only `<module>.trash.purge` capabilities;
  - Empty trash bounded by `trashedUpTo`;
  - `Trash:RetentionDays` (1 to 3650, default 30), validated at startup.
- **Admin** (TR-20): a new `features/trash/` view with tabs, search, preview, a restore
  dialog and an empty-trash dialog. The delete dialogs state the purge date.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10.0.12 + Npgsql (its named query filters are what
  TR-03 relies on), the in-house dispatcher and outbox, hosted services.
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n.
- **Nothing new**.

**Storage**: PostgreSQL 16+. Three migrations, all additive:

| Module | Migration | Change |
|---|---|---|
| Pages | `AddTrash` | **~** `pages` (+ 8 columns, `ux_pages_site_path` becomes partial, `ix_pages_trash`); **~** `components` (+ 4 columns, `ux_components_site_name` becomes partial, `ix_components_trash`); the publish log `action` check gains `trashed`, `restored` and `purged` |
| Cms | `AddItemTrash` | **~** `items` (+ 4 columns, `ix_items_trash`); the `item_history` kind check gains `trashed` and `restored` |
| Media | `AddFileTrash` | **~** `media_files` (+ 3 columns, `ix_media_files_trash`) |

No data rewrite and no document schema change. Converting the site filters to named filters
changes the model only, not the database.

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. The suites are listed in research TR-21. The main ones:
- `TrashRoundTripTests` (SC-001);
- `TrashedIdProbe` and `TrashVisibilityTests` (SC-004);
- `TrashCacheTests`, two-instance (SC-005);
- `TrashRestoreRulesTests` (SC-008);
- `TrashPurgeTests`, two-instance (SC-006);
- `TrashAuthorizationTests` (SC-007);
- `TrashLockTests`, `TransferTrashTests`, `MediaOrphanSweepTrashTests` and
  `UpgradeFrom020Tests`;
- architecture: `QueryFilterCoverageTests` and `TrashQueryUsageTests`;
- admin: `TrashView.spec.ts`, `RestoreDialog.spec.ts` and `EmptyTrashDialog.spec.ts`.

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend and the Vue admin SPA. On
the public site, only "trashed means not found" is visible.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Trash search and filter**: under 1 s at p95 with 10,000 entries (SC-003). Partial index,
  keyset paging (TR-19).
- **Restore**: one module transaction. Most of the time goes into rebuilding guard rows,
  which costs the same as one draft save.
- **Rendering hot path**: one extra `trashed_at_utc IS NULL` predicate on media serving and
  on published item reads, with no new round trip. The 200 ms / 20 ms budgets are
  unaffected.
- **Purge**: batches of 100 per module per hour. Each entry costs about as much as today's
  delete.

**Constraints**:
- **No leak**: trashed content must never reach a public or admin read outside the trash
  (FR-006). This is enforced structurally by named filters and the probe, not by care in
  handlers.
- **Restore never publishes** unless "Restore and publish" is chosen (Principle VII).
- **Logs**: ids and counts only. Trash events and log lines carry no titles, names or
  content.
- **Offline**: no outbound calls.

**Scale/Scope**:
- **Code**: 0 new projects and 0 new modules.
- **Migrations**: 3. Tables changed: 4. Tables added: 0.
- **Endpoints**: per module, list, entry, restore plan, restore, bulk restore, purge, bulk
  purge and empty. That is 6 for Cms and Media and 9 for Pages (pages and components), about
  21 in all. Four delete endpoints change (200 with a receipt, a new lock refusal), and the
  site response gains one field.
- **Contracts**:
  - 1 interface method added (`IEditingLocks.FindActiveHolderAsync`);
  - 2 usage records gain `InTrash`;
  - 4 new events;
  - 4 existing events change documentation only.
- **Capabilities**: 3 new (Administrator only). **Configuration keys**: 1.
- **Admin**: about 6 components and 2 TypeScript modules.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module** (TR-01). Each owning module has a `Features/Trash/` slice: list, get entry, restore plan, restore, bulk restore, purge, bulk purge, empty. Purge services are under `Infrastructure/`. Controllers are thin and use in-house handlers. <br>• **Domain model**: `PageTree.Trash` and `PageTree.Restore` keep the tree's invariants (children, home, page limit, renumbering) inside the aggregate. A pure `TrashPlacement` holds the placement rules and is unit-tested. `Item.Trash` and `Item.Restore` cover Cms's rich model. <br>• **Cross-module**: no new edges (module-surface.md §1). Trash state reaches other modules only through the existing `*Deleted` events and the new `*Restored` events via the outbox. The lock read happens before the owning transaction, as 023 does. <br>• **Shared**: `ITrashable` (Kernel), `QueryFilters`, `TrashQuery` and `TrashOptions` (Infrastructure). These are platform concepts used by three modules, small, and free of module types (TR-15). |
| II | Easy to Install, Operate and Upgrade | **Complies.** One key, `Trash:RetentionDays`, with a safe default (30) and a range checked by `ValidateOnStart`, documented in `docs/configuration.md`. Three automatic, forward-only, re-runnable migrations. Health checks unchanged. |
| III | Upgrades Never Lose Data | **Complies.** <br>• `UpgradeFrom020Tests` (snapshot from the previous release). <br>• The feature *adds* recoverability. Nothing deleted before the upgrade is affected. <br>• **Export and import**: archives exclude the trash, and a restore import empties it (TR-17). The archive format is unchanged. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. The purge runs on built-in hosted services, and a conditional delete makes it multi-instance safe without a leader or queue. Cache invalidation on restore goes through the existing render-stamp path, so it works with Redis too. Media bytes stay in the configured provider until purge. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Filters**: the site filter is still applied by EF and is now named. The bypass still goes through `AcrossAllSites` with a reason, and the trash bypass is separate and allow-listed (TR-03). <br>• **Raw SQL**: the one existing raw SQL read over trashable content (`GetTokenUsage`) adds the trash predicate explicitly, with a test. <br>• **Background work**: purge services read across sites by design and say so in the reason. Each purged row is acted on in its own site. <br>• **Authorisation**: the trash endpoints name the site in the route, and other sites' ids answer 404 (FR-029). The probes join the isolation suite. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** No change to the page schema, collection schema, binding format or archive format. Restored items are validated against the current schema (015's validator). |
| VII | Draft and Published Are Separate Worlds | **Complies.** <br>• Trash never publishes. It only takes content down, as delete already did. <br>• Restore returns content unpublished. "Restore and publish" is an explicit, audited publish through the existing rollback path (TR-11). <br>• The public path still reads only published data, and gains no write access. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** Trash uses the existing `*Deleted` events, which already invalidate exactly the dependent pages. Restore of media publishes `FileRestored`, handled by the same refresh handler. Items and pages return unpublished, so nothing public changes except through a publish. `TrashCacheTests` checks two instances. |
| IX | Secure by Default | **Complies.** <br>• **Server-side checks**: restore mirrors each kind's delete capability, including 013's row rules. Permanent delete and empty need new Administrator-only capabilities. <br>• **Declarations**: `AuthorizationDeclarationTests` cover every new endpoint. <br>• **Personal data**: the trash shows display names only. Logs carry ids only. A permanent delete gives operators immediate removal, for privacy or legal reasons. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests for every new and changed endpoint. <br>• **Domain**: unit tests for `TrashPlacement`, `TrashRetention` and the address rules. <br>• **Multi-instance**: two-instance tests for the purge and the cache. <br>• **Upgrade**: `UpgradeFrom020Tests`. <br>• **Coverage**: 80% line coverage on the restore and purge handlers and on `PageTree.Restore`. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies.** <br>• **Not built**: no trash module, no snapshots, no purge events, no per-site retention, no undo toast (TR-22). <br>• **Reuse**: rollback for "Restore and publish", 023's copy naming and lock store, the existing extractors and indexer for rebuilding guard rows, and the existing `*Deleted` events. <br>• **Delivery**: one PR. Pages, Cms and Media each ship a complete slice; the admin view ships with them. No feature flag is needed, because delete stays one action and only becomes reversible. |

**Technology Constraints**:
- MVC controllers only.
- EF Core only. The one existing raw SQL read over trashable content (`GetTokenUsage`)
  gains the trash predicate and stays parameterised.
- No Node process at run time.
- No new admin libraries.

**Development Workflow**:
- Issue #21 → `specs/021-soft-delete-trash` → branch `021-soft-delete-trash` → one PR that
  closes #21.
- Clarify has run: 1 question at specify and 3 in clarify, all answered. Analyze follows
  tasks.
- **Spec alignment**: recorded in spec.md in this change, to be confirmed in
  `/speckit-analyze`.
  1. **Component names** are freed while trashed, and a clash on restore gets a copy name
     (TR-04). FR-004 and FR-017 now name components.
  2. **"Restore and publish"** is offered only when a previously live version exists. Content
     that was only scheduled comes back unpublished, with the note "was scheduled" (TR-11).
     FR-015 is narrowed.
  3. **Item history** is removed with the item on permanent delete or purge, as 015 FR-008
     already does. Pages and components keep their publish log entries (TR-14). The edge
     case "History after purge" is narrowed.
  4. **Template pages** come back as normal pages when the collection has another template
     by then (TR-12). Added as an edge case.
- **Contract and registry changes in the same PR**:
  - `specs/001-foundation-skeleton/contracts/integration-events.md`: 4 new events, and 4
    events whose meaning changes;
  - `specs/002-identity-setup/contracts/authorization.md`: 3 capabilities and the new
    endpoints;
  - `specs/010-page-tree-components/contracts/`, `specs/013-media-library-variants/contracts/`
    and `specs/015-cms-items/contracts/`: the deletes return a receipt, and the lock refusal;
  - `specs/020-site-export-import/contracts/archive-parts.md`: trash rules for the import
    parts;
  - `specs/023-concurrency/contracts/module-surface.md`: `FindActiveHolderAsync`;
  - `docs/configuration.md` and `docs/backlog.md`;
  - `docs/contracts/` is not touched: no schema changes.

**Operator impact**:
- **New configuration keys**: `Trash:RetentionDays` (env `Trash__RetentionDays`), integer
  days, default 30, allowed 1 to 3650. Invalid values stop startup.
- **Migrations**: 3, additive and automatic: Pages `AddTrash`, Cms `AddItemTrash` and Media
  `AddFileTrash`.
- **New dependencies**: none.
- **Upgrade notes** (`docs/releases/unreleased/`):
  1. Deleting a page, shared component, collection item or media file now moves it to the
     site's trash. Members can restore it for 30 days, or as set by `Trash:RetentionDays`.
     After that it is deleted permanently by a background job.
  2. Media files in the trash keep using storage until they are purged. To free space at
     once, an Administrator can delete them permanently or empty the trash.
  3. Only Administrators can delete permanently. Designers and Editors can delete (to the
     trash) and restore what they are allowed to delete.
  4. Content deleted before this upgrade is not in the trash and cannot be restored.

**Post-Phase-1 re-check**:
- `trash-api.yaml` declares a capability on every operation, and a row check where 013
  requires one;
- `module-surface.md` adds no edges;
- every trashable entity carries the `Trash` named filter, and the only bypasses are
  allow-listed;
- events carry ids only;
- raw SQL only where the plan's race needs it: `FOR SHARE` on the live components a draft save references (T028), and the trash join added to the token-usage count;
- the public path gains only the trash predicate;
- the archive format is unchanged.

**Gate: PASS** (no new exception).

## Project Structure

### Documentation (this feature)

```text
specs/021-soft-delete-trash/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 1 + 3 questions)
├── research.md                # Phase 0: decisions TR-01 … TR-22
├── data-model.md              # Phase 1: columns, indexes, transitions, rules, errors
├── quickstart.md              # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── trash-api.yaml         # HTTP: trash lists, entries, restore, purge, empty; changed deletes
│   ├── admin-trash.md         # Admin behaviour: delete dialogs, trash view, restore, empty
│   └── module-surface.md      # ITrashable, filters, IEditingLocks, usage readers, events, capabilities, transfer
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
BannedSymbols.txt                                          # ~ ban the keyed IgnoreQueryFilters overload
src/Shared/
├── QuickBuilder.Shared.Kernel/MultiSite/ + ITrashable.cs
└── QuickBuilder.Shared.Infrastructure/
    ~ MultiSite/CrossSiteQuery.cs                          # lifts the Site filter only
    + Data/QueryFilters.cs
    + Trash/TrashQuery.cs  TrashOptions.cs  TrashRetention.cs
src/QuickBuilder.Host/
    ~ Program.cs  appsettings.json                         # TrashOptions, ValidateOnStart
src/Modules/
├── Pages/QuickBuilder.Pages/
│   ~ Domain/Page.cs  Component.cs  PageTree.cs            # Trash/Restore, trash columns
│   + Domain/PageTree.Trash.cs
│   ~ Domain/PublishLogEntry.cs                            # trashed/restored/purged
│   + Features/Trash/ListTrash.cs  GetTrashEntry.cs  RestorePlan.cs  RestorePage.cs
│   +                RestoreComponent.cs  BulkRestore.cs  Purge.cs  EmptyTrash.cs  TrashResponses.cs
│   ~ Features/DeletePage/  DeleteComponent/               # trash instead of remove; lock check; receipt
│   ~ Infrastructure/PublishingWrites.cs                   # ShelveAsync; ForgetAsync used by purge
│   ~ Infrastructure/MediaUsageReader (IMediaUsageReader)  # InTrash
│   + Infrastructure/PagesTrashPurgeService.cs
│   ~ Features/Transfer/PagesArchivePart.Import.cs         # IncludingTrash on clear and match
│   ~ Infrastructure/PagesDbContext.cs  PublishedReadDbContext.cs  # named filters, partial indexes
│   + Infrastructure/Migrations/<ts>_AddTrash.cs
│   ~ PagesCapabilities.cs  PagesErrors.cs  PagesModule.cs
├── Pages/QuickBuilder.Pages.Contracts/Events/ + PageRestored.cs  ComponentRestored.cs  ~ PageDeleted.cs  ComponentDeleted.cs (docs)
├── Cms/QuickBuilder.Cms/
│   ~ Domain/Item.cs  ItemHistoryEntry.cs
│   + Features/Items/Trash/ListTrash.cs  GetTrashEntry.cs  RestorePlan.cs  RestoreItem.cs  BulkRestore.cs  Purge.cs  EmptyTrash.cs
│   ~ Features/Items/DeleteItem/                           # trash; lock check; receipt
│   ~ Features/DeleteCollection/                           # count includes trashed items
│   ~ Infrastructure/ItemWrites.cs  ItemIndexer.cs  ItemMediaUsageReader.cs  ItemIndexBackfillService.cs
│   + Infrastructure/CmsTrashPurgeService.cs
│   ~ Features/Transfer/  Infrastructure/CmsDbContext.cs  CmsPublishedReadDbContext.cs
│   + Infrastructure/Migrations/<ts>_AddItemTrash.cs
│   ~ CmsCapabilities.cs  CmsErrors.cs  CmsModule.cs
├── Cms/QuickBuilder.Cms.Contracts/ ~ Events/ItemEvents.cs (+ ItemRestored)  ~ Media/IItemMediaUsageReader.cs
├── Media/QuickBuilder.Media/
│   ~ Domain/MediaFile.cs
│   + Features/Trash/ListTrash.cs  GetTrashEntry.cs  RestorePlan.cs  RestoreFile.cs  BulkRestore.cs  Purge.cs  EmptyTrash.cs
│   ~ Features/DeleteFile/DeleteFile.cs  Features/Folders/ (delete when only trashed files)
│   ~ Infrastructure/MediaUsage.cs  MediaOrphanSweepService.cs  ImageMetadataBackfillService.cs  SearchTextBackfillService.cs  PublicMediaReader.cs
│   ~ Variants/ThumbnailWorker.cs
│   + Infrastructure/MediaTrashPurgeService.cs
│   ~ Features/Transfer/  Infrastructure/MediaDbContext.cs
│   + Infrastructure/Migrations/<ts>_AddFileTrash.cs
│   ~ MediaCapabilities.cs  MediaErrors.cs  MediaModule.cs
├── Media/QuickBuilder.Media.Contracts/Events/ + FileRestored.cs  ~ FileDeleted.cs (docs)
├── Editor/
│   ~ QuickBuilder.Editor.Contracts/IEditingLocks.cs        # FindActiveHolderAsync
│   ~ QuickBuilder.Editor/Infrastructure/EditingLocks.cs
├── Sites/QuickBuilder.Sites/ ~ Features/ (site response: trashRetentionDays)
└── (Editor, Forms, Identity, Rendering, Sites DbContexts)  # ~ site filters renamed to QueryFilters.Site

src/admin/src/
+ api/trash.ts
+ features/trash/TrashView.vue  TrashList.vue  TrashPreview.vue  RestoreDialog.vue  PurgeDialog.vue  EmptyTrashDialog.vue  useTrash.ts
~ app/router.ts  app/SiteNav.vue                           # route and nav entry
~ api/pages.ts  api/items.ts  api/media.ts  api/sites.ts   # delete receipt, lock refusal, trashRetentionDays
~ features/pages/DeletePageDialog  features/pages/ComponentsView.vue
~ features/cms/items/DeleteItemDialog.vue  features/cms/DeleteCollectionDialog
~ features/media/ (delete dialog, usage panel)
~ editing/useEditingSession.ts                             # "moved to the trash" state
~ i18n/en.json
src/admin/tests/
+ features/trash/TrashView.spec.ts  RestoreDialog.spec.ts  EmptyTrashDialog.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/ (SiteFilterCoverageTests → QueryFilterCoverageTests, + TrashQueryUsageTests, AuthorizationDeclarationTests)
+ QuickBuilder.UnitTests/Pages/PageTreeTrashTests.cs  Trash/TrashRetentionTests.cs
~ QuickBuilder.IntegrationTests/
│   + Trash/PagesTrashRoundTripTests.cs  ItemAndFileTrashRoundTripTests.cs  TrashVisibilityTests.cs  TrashedIdProbeTests.cs  TrashRestoreRulesTests.cs
│   +       TrashAuthorizationTests.cs  TrashLockTests.cs  TrashPurgeTests.cs  TrashBulkEndpointTests.cs
│   +       UpgradeFrom020Tests.cs  TrashListPerformanceTests.cs
│   + Transfer/TransferTrashTests.cs  Media/MediaOrphanSweepTrashTests.cs  Pages/UpgradeFrom020Tests.cs
│   ~ Isolation/Probes/ (+ TrashedIdProbe)  Infrastructure/EndpointCatalog.cs
specs/001-foundation-skeleton/contracts/integration-events.md     # ~
specs/002-identity-setup/contracts/authorization.md               # ~
specs/010-…/contracts/  specs/013-…/contracts/  specs/015-…/contracts/  specs/020-…/contracts/archive-parts.md  specs/023-…/contracts/module-surface.md   # ~
docs/configuration.md  docs/backlog.md  docs/releases/unreleased/  # ~ / +
```

**Structure decision**:
- **Pages, Cms and Media**: each trash is a slice of the module that owns the content, so
  restore can reuse that module's aggregate, extractors and publishing code in one
  transaction.
- **Shared**: holds only the filter keys, the bypass helpers and the retention setting.
- **Admin**: one `features/trash/` folder. Changes elsewhere are limited to dialog text, the
  lock refusal and the editor's "moved to the trash" state.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6**, **E-7** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. The keyboard and screen-reader passes over the trash view and dialogs are manual in quickstart §8, until #25. |

**New exceptions**: none.

## Risks

- **The named-filter migration touches every DbContext.** A filter left anonymous, or a
  bypass that lifts both filters, would leak trashed rows or break cross-site reads.
  - *Mitigation*: `QueryFilterCoverageTests` asserts both filters by name on every entity.
    The banned-symbol rule covers both `IgnoreQueryFilters` overloads. The isolation suite
    and `TrashedIdProbe` run across every endpoint. This work lands as the first task, with
    no behaviour change, so its tests pass before trash code exists.
- **Dependent rows have no trash filter** (drafts, versions, usages).
  - *Mitigation*: the probe calls every endpoint with a trashed id. Readers that join usage
    rows go through the owner. Guard rows are removed at trash time (TR-06).
- **Background readers that must see trashed rows** (orphan sweep, variant worker,
  backfills) could destroy trashed bytes if missed.
  - *Mitigation*: TR-18 lists them. `MediaOrphanSweepTrashTests` covers the sweep, and
    `TrashQueryUsageTests` lists the allowed callers.
- **The page aggregate removes a trashed page from `_pages`**, and EF could treat that as
  deleting an orphan.
  - *Mitigation*: `PageTree.Trash` detaches the page explicitly and the handler marks the
    row modified. `TrashRoundTripTests` asserts the row survives.
- **A restore racing a purge or a second restore.**
  - *Mitigation*: both re-check the trash state under the row's concurrency token, so one
    wins and the other gets `trash.not_in_trash` (data-model §3).
    `TrashPurgeTests` covers the race.
- **Storage growth**: trashed media keep their bytes for the retention period.
  - *Mitigation*: the media trash total (FR-012), permanent delete and empty, and upgrade
    note 2.
- **Behaviour change for API scripts**: `DELETE` now returns 200 with a receipt instead of
  204, and the content can still be found through the trash.
  - *Mitigation*: the admin is updated in the same PR, and the release notes mention it.
    There is no public API commitment for admin endpoints beyond the admin.

## Deferred (recorded, not built)

- An "Undo" link in the toast after a delete (TR-22).
- Per-site or per-kind retention periods.
- Trash for collections, menus, folders, design tokens, settings, sites and users (spec Out
  of Scope).
- Purge events for consumers outside the owning module, until one needs them.
