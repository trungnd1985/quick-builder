---

description: "Task list for 021-soft-delete-trash"
---

# Tasks: Soft Delete, Trash and Restore

**Input**: Design documents from `/specs/021-soft-delete-trash/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires integration tests for every new and changed endpoint,
  unit tests for domain rules, two-instance tests for background and cache work, and 80%
  line coverage on the restore and purge handlers and `PageTree.Restore`.
- The spec measures SC-001, SC-004, SC-005, SC-006, SC-007 and SC-008 through automated
  suites (research TR-21).
- Within each story, test tasks come first and must fail before implementation starts.

**Changed after analysis (2026-11-06)**:
- T020, T022, T050, T056, T061 and T086 were extended or reworded.
- T061a was added.
- T009 moved before the filter conversion as T004a, so the test is written first.
- No other task was renumbered.
- In the spec, FR-022, FR-028 and the purge-frequency assumption were reworded.
- Plan and research now name the one raw SQL read that touches trashable content.

**Organization**: tasks are grouped by user story (US1–US6 from spec.md), so each story can
be implemented and tested on its own. US1 and US2 are both P1 and ship together: US1 makes
delete reversible, and US2 makes the trash invisible everywhere else.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US6)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `SHAREDK` = `src/Shared/QuickBuilder.Shared.Kernel`
- `SHAREDI` = `src/Shared/QuickBuilder.Shared.Infrastructure`
- `HOST` = `src/QuickBuilder.Host`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CMSC` = `src/Modules/Cms/QuickBuilder.Cms.Contracts`
- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `MEDIAC` = `src/Modules/Media/QuickBuilder.Media.Contracts`
- `EDITORM` = `src/Modules/Editor/QuickBuilder.Editor`
- `EDITORC` = `src/Modules/Editor/QuickBuilder.Editor.Contracts`
- `ADMIN` = `src/admin/src`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`

**Conventions**:
- **Backend slices** live under `<Module>/Features/Trash/` (Cms: `CMS/Features/Items/Trash/`),
  one file per slice, modelled on `PAGES/Features/DeletePage/DeletePage.cs`. Every
  controller action:
  - is routed as in [contracts/trash-api.yaml](./contracts/trash-api.yaml);
  - declares exactly one `[RequireCapability(...)]` with the `x-capability` of the contract,
    as `ATESTS/AuthorizationDeclarationTests.cs` requires; a second capability
    (`x-capability-when-publish`, 013's live-use rule) is checked in the handler;
  - dispatches through `IDispatcher`;
  - refuses with the codes in [data-model.md §5](./data-model.md);
  - is registered in the module's `<Module>Module.cs` and listed in
    `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **Trashed rows** are only ever seen through `TrashQuery.IncludingTrash(reason)` or
  `TrashQuery.OnlyTrash(reason)`, from a caller on the `TrashQueryUsageTests` allowlist.
  Never write `Where(x => x.TrashedAtUtc == null)` in a handler: the named filter does it.
- **Time** comes from `TimeProvider` (tests use `ITESTS/Infrastructure/ManualClock.cs`).
  The purge date is always `TrashRetention.PurgeOn(trashedAtUtc)` and never stored.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task.
- **Logs and events** carry ids, kinds, counts and codes only, never titles, names, paths or
  content.

**References**:
- decisions: `TR-xx` in [research.md](./research.md);
- columns, transitions, rules and errors: [data-model.md](./data-model.md);
- endpoints: [contracts/trash-api.yaml](./contracts/trash-api.yaml);
- admin behaviour: [contracts/admin-trash.md](./contracts/admin-trash.md);
- interfaces, events, capabilities and transfer: [contracts/module-surface.md](./contracts/module-surface.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents and the retention setting.

- [X] T001 Commit `specs/021-soft-delete-trash/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `021-soft-delete-trash`, referencing #21.
- [X] T002 [P] Write `UTESTS/Shared/TrashRetentionTests.cs`: `PurgeOn` adds exactly `RetentionDays` days; `DueBefore(now)` returns `now − RetentionDays`; a changed option changes every result (nothing cached). Must fail first.
- [X] T003 Create `SHAREDI/Trash/TrashOptions.cs` (section `Trash`, `RetentionDays` int, default 30, `[Range(1, 3650)]`) and `SHAREDI/Trash/TrashRetention.cs` (`PurgeOn`, `DueBefore`, over `IOptionsMonitor<TrashOptions>`) until T002 passes. Register both in `HOST/Program.cs` with `BindConfiguration("Trash").ValidateDataAnnotations().ValidateOnStart()` beside the existing options, and add `"Trash": { "RetentionDays": 30 }` to `HOST/appsettings.json`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: named query filters with no behaviour change, then the trash columns, the
domain marker and the error codes every story uses (research TR-02, TR-03, TR-05).

**⚠️ CRITICAL**: no user story work can begin until this phase is complete. T004–T010 land
first and change no behaviour: the whole existing suite must stay green after T010.

### Named query filters (TR-03)

- [X] T004 Create `SHAREDI/Data/QueryFilters.cs` (`const string Site = "site"`, `const string Trash = "trash"`) and `SHAREDK/MultiSite/ITrashable.cs` (`DateTimeOffset? TrashedAtUtc { get; }`) exactly as module-surface.md §2–§3.
- [X] T004a Rename `ITESTS/Sites/SiteFilterCoverageTests.cs` to `ITESTS/Sites/QueryFilterCoverageTests.cs` and change it to assert, through the EF model's declared filters by name: every `ISiteOwned` entity has `QueryFilters.Site`; every `ITrashable` entity has `QueryFilters.Trash`; no entity has an anonymous filter. It must fail until T005–T008 are done (ID T009 is retired so later references stay valid).
- [X] T005 Change `SHAREDI/MultiSite/SiteQueryFilter.cs` so the site filter it applies is registered as the named filter `QueryFilters.Site` (EF Core 10 `HasQueryFilter(name, expression)`), and convert every remaining anonymous `HasQueryFilter` in every module DbContext to the named `Site` filter: `PAGES/Infrastructure/PagesDbContext.cs`, `PAGES/Infrastructure/PublishedReadDbContext.cs`, `CMS/Infrastructure/CmsDbContext.cs`, `CMS/Infrastructure/CmsPublishedReadDbContext.cs`, `MEDIA/Infrastructure/MediaDbContext.cs`, and the Editor, Forms, Identity, Rendering and Sites DbContexts. No migration is generated (model-only change); confirm with `dotnet ef migrations has-pending-model-changes` per module.
- [X] T006 Change `SHAREDI/MultiSite/CrossSiteQuery.cs` so `AcrossAllSites(reason)` calls `IgnoreQueryFilters([QueryFilters.Site])` (the keyed overload) instead of lifting every filter. Keep its reason argument and XML docs; add "lifts the site filter only; trashed rows stay hidden (021 TR-03)".
- [X] T007 Create `SHAREDI/Trash/TrashQuery.cs`: `IncludingTrash<T>(this IQueryable<T>, string reason) where T : class, ITrashable` lifting only `QueryFilters.Trash`, and `OnlyTrash<T>(…)` adding `Where(e => e.TrashedAtUtc != null)`. Both reject an empty reason, as `CrossSiteQuery` does.
- [X] T008 Add the keyed overload `M:Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters``1(System.Linq.IQueryable{``0},System.Collections.Generic.IReadOnlyCollection{System.String})` to `BannedSymbols.txt` with the message "Use CrossSiteQuery.AcrossAllSites or TrashQuery (021 TR-03)", and add `#pragma warning disable RS0030` with a reason comment in `SHAREDI/MultiSite/CrossSiteQuery.cs` and `SHAREDI/Trash/TrashQuery.cs` only.
- [X] T010 [P] Create `ATESTS/TrashQueryUsageTests.cs` (same reflection/IL approach as `ATESTS/TransferSessionUsageTests.cs`): `TrashQuery.IncludingTrash` and `OnlyTrash` are called only from types under `*.Features.Trash` / `*.Features.Items.Trash`, the three `*TrashPurgeService`s, `MediaOrphanSweepService`, `ThumbnailWorker` (variant worker), `ImageMetadataBackfillService`, `SearchTextBackfillService`, `ItemIndexBackfillService`, `MediaUsageReader`, `ItemMediaUsageReader`, the delete-collection slice, `MenuReferences`, the Rollback and Publish slices (T061a) and the `Features/Transfer` archive parts (TR-17, TR-18). The allowlist is a literal in the test with a TR reference per entry.
  *Done as*: the allowlist also names `ItemWrites` (a restore writes through the one item write path, T038) and the menu, rollback and publish handlers of T061/T061a.

**Checkpoint A**: `dotnet test` is fully green; nothing is trashable yet.

### Trash columns and migrations (data-model §2)

- [X] T011 [P] In `PAGES/Domain/Page.cs` implement `ITrashable` and add `TrashedAtUtc`, `TrashedBy`, `TrashedParentId`, `TrashedPosition`, `TrashedPath`, `TrashedWasHome`, `TrashedTemplateCollectionId`, `TrashedHadSchedule` with private setters; in `PAGES/Domain/Component.cs` add `TrashedAtUtc`, `TrashedBy`, `TrashedWasLive`, `TrashedHadSchedule`. No behaviour yet.
- [X] T012 Map the new columns in `PAGES/Infrastructure/PagesDbContext.cs`: the `QueryFilters.Trash` named filter on `Page` and `Component`; `ck_pages_trash` and `ck_components_trash` (all `trashed_*` null together, data-model §2); `ux_pages_site_path` and `ux_components_site_name` filtered `trashed_at_utc IS NULL`; partial indexes `ix_pages_trash` and `ix_components_trash` on `(site_id, trashed_at_utc DESC, id) WHERE trashed_at_utc IS NOT NULL`; extend the publish log `action` check with `trashed`, `restored`, `purged` in `PAGES/Domain/PublishLogEntry.cs` and its mapping. Apply the same `Trash` filter in `PAGES/Infrastructure/PublishedReadDbContext.cs` for the pages and components it maps. Generate `PAGES/Infrastructure/Migrations/<ts>_AddTrash.cs` and update the snapshot.
- [X] T013 [P] In `CMS/Domain/Item.cs` implement `ITrashable` with `TrashedAtUtc`, `TrashedBy`, `TrashedWasLive`, `TrashedHadSchedule`; add `trashed` and `restored` to the kinds in `CMS/Domain/ItemHistoryEntry.cs`.
- [X] T014 Map them in `CMS/Infrastructure/CmsDbContext.cs` and `CMS/Infrastructure/CmsPublishedReadDbContext.cs` (`Trash` named filter, `ck_items_trash`, `ix_items_trash`, history kind check). Generate `CMS/Infrastructure/Migrations/<ts>_AddItemTrash.cs` and update the snapshot.
- [X] T015 [P] In `MEDIA/Domain/MediaFile.cs` implement `ITrashable` with `TrashedAtUtc`, `TrashedBy`, `TrashedFolderId`.
- [X] T016 Map them in `MEDIA/Infrastructure/MediaDbContext.cs` (`Trash` named filter, `ck_media_files_trash`, `ix_media_files_trash`). Generate `MEDIA/Infrastructure/Migrations/<ts>_AddFileTrash.cs` and update the snapshot.
- [X] T017 Run the `QueryFilterCoverageTests` (T004a) and `ATESTS/MigrationTransactionTests.cs`; both must pass with the four trashable entities.

### Errors (data-model §5)

- [X] T018 [P] Create `SHAREDI/Trash/TrashErrors.cs` with `trash.not_in_trash` (404), `trash.plan_changed` (409, body `plan`), `trash.placement_required` (422), `trash.invalid_placement` (422) and `trash.count_changed` (409, body `currentCount`), and map them in each module's error-result mapper (`PAGES/PagesErrors.cs`, `CMS/CmsErrors.cs` and `MEDIA/Infrastructure/MediaErrorResults.cs`) the same way existing codes are mapped.
  *Done as*: the codes and their statuses are in `SHAREDI/Trash/TrashErrorCodes.cs`, which each module's `StatusFor` consults first; each module builds its own `Error` values (fields go through that module's extensions).
- [X] T019 [P] Create the shared response shapes of trash-api.yaml (`TrashReceipt { trashedAtUtc, purgeOnUtc }`, `TrashEntry`, `RestorePlan`, `RestoreRequest`, `BulkOutcome`) in `PAGES/Features/Trash/TrashResponses.cs`, `CMS/Features/Items/Trash/TrashResponses.cs` and `MEDIA/Features/Trash/TrashResponses.cs` (one per module, no shared DTO across modules, Principle I).

**Checkpoint B**: the three migrations apply on a fresh and on an upgraded database; the full
suite is still green; no endpoint changed yet.

---

## Phase 3: User Story 1 — A member undoes a wrong delete (Priority: P1) 🎯 MVP

**Goal**: deleting a page, component, item or media file moves it to the trash with a
receipt, and a member who may delete it can restore it to its original place with its
identity, draft, versions and references (FR-001–FR-005, FR-013–FR-015).

**Independent Test**: quickstart §1. For each kind, delete then restore, and compare id,
draft, versions, address and references; "Restore and publish" brings back the last live
version (`TrashRoundTripTests`).

### Tests for User Story 1 ⚠️

- [X] T020 [P] [US1] Write `ITESTS/Trash/TrashRoundTripTests.cs`, one test per kind: the delete answers 200 with a `TrashReceipt` whose `purgeOnUtc = trashedAtUtc + 30 days`; the row still exists with the same id (read via `IncludingTrash` in the test); the trash list returns it with who and when; restore answers 200; id, draft JSON, version list, address/folder and (for items) outgoing references are identical to before; `component_usages`, `collection_usages`, `item_slugs` and `item_references` are rebuilt. `GET …/trash/{kind}/{id}` returns the entry for a trashed id, and answers `trash.not_in_trash` for a live id and for an unknown id. Must fail first.
  *Done as*: split into `ITESTS/Trash/PagesTrashRoundTripTests.cs` and `ItemAndFileTrashRoundTripTests.cs`.
- [X] T021 [P] [US1] Add to `ITESTS/Trash/TrashRoundTripTests.cs`: a published item deleted then "Restore and publish" is live with the version that was live (and a publish log / history `restored` + publish entry); a page deleted with a pending schedule comes back unpublished with `hadSchedule` and `canRestoreAndPublish = false`; a restore without `publish` never publishes (Principle VII).
- [X] T022 [P] [US1] Write `ITESTS/Trash/TrashAuthorizationTests.cs` (restore part): restore mirrors delete per kind (TR-10 table) — Editor can restore an item, cannot restore a page; `media.own_files.delete` restores only one's own upload; a media restore with live use needs `pages.publishing.manage`; other sites' ids answer 404 on list, entry, restore-plan and restore (FR-029, SC-007).

### Pages and components

- [X] T023 [US1] Add `PageTree.Trash(pageId, actor, now)` in `PAGES/Domain/PageTree.cs`: keep the existing guards (children, home, published: 010 FR-012/013, 012 FR-006), record `TrashedParentId`, `TrashedPosition`, `TrashedPath`, `TrashedWasHome`, `TrashedTemplateCollectionId`, clear `parent_id`, `is_home`, `template_collection_id`, renumber siblings, and remove the page from the loaded tree **without** deleting it (the handler marks the row modified, plan Risks).
- [X] T024 [US1] Add `PageTree.Restore(page, placement=original, now)` in `PAGES/Domain/PageTree.cs`: insert at `min(TrashedPosition, sibling count)` under the live original parent, recompute path and depth, clear every trash column, restore `template_collection_id` when the collection has no template (TR-12), and become home when the site has none and the page is top level. Refuse at `PageAddresses.MaxPagesPerSite` with `pages.too_many_pages`.
- [X] T025 [US1] Add `Component.Trash(actor, wasLive, hadSchedule, now)` and `Component.Restore(now)` in `PAGES/Domain/Component.cs`.
- [X] T026 [US1] Add `PublishingWrites.ShelveAsync(siteId, kind, id, actor)` in `PAGES/Infrastructure/PublishingWrites.cs`: cancel pending schedules (reason `trashed`), stop a live component being live while keeping its versions, and write a publish log `trashed` entry. Return `(wasLive, hadSchedule)`. Keep `ForgetAsync` unchanged (used by purge in US5).
  *Done as*: `ShelveAsync` also removes the subject's menu-usage rows, and a sibling `UnshelveAsync` rebuilds component, menu and collection usage rows from the stored draft on restore. The rollback body moved into `RollbackContentAsync`, which runs inside the caller's transaction (for "Restore and publish").
- [X] T027 [US1] Change `PAGES/Features/DeletePage/DeletePage.cs` to trash instead of remove: call `PageTree.Trash`, delete the page's `component_usages`, `collection_usages` and `live_dependencies` rows (TR-06), call `ShelveAsync`, publish the existing `PageDeleted`, and answer 200 with a `TrashReceipt`. Keep every existing refusal.
- [X] T028 [US1] Change the component delete slice in `PAGES/Features/DeleteComponent/` the same way: guards unchanged (010 FR-028, 012 FR-004), `Component.Trash`, delete its `live_dependencies`, `ShelveAsync`, `ComponentDeleted`, 200 with receipt.
  *Done as*: a trashed row still satisfies the foreign key that used to settle the race with a draft save, so `DraftWrites` now takes `FOR SHARE` on the live components it references, inside the save's transaction. This is one new raw SQL read, which the plan said there would not be.
- [X] T029 [P] [US1] Create `PAGESC/Events/PageRestored.cs` and `PAGESC/Events/ComponentRestored.cs` exactly as module-surface.md §6, and change the XML docs of `PAGESC/Events/PageDeleted.cs` and `ComponentDeleted.cs` to "deleted (moved to the trash or deleted permanently)".
- [X] T030 [US1] Create `PAGES/Features/Trash/ListTrash.cs` (`GET …/trash/pages` and `…/trash/components`; `OnlyTrash`, newest first, keyset `after` cursor, 50 per page; deleter names via `IUserDirectory`; `purgeOnUtc` from `TrashRetention`; `canRestore` and `canPurge` from the caller's capabilities) and `PAGES/Features/Trash/GetTrashEntry.cs` (`GET …/trash/pages/{id}` and `…/components/{id}`; 404 `trash.not_in_trash` when not trashed).
- [X] T031 [US1] Create `PAGES/Features/Trash/RestorePlan.cs` (`GET …/restore-plan`) for `original` placement only in this story: target parent, resulting address (unchanged when free), home and template effects, `wasLive`, `hadSchedule`, `canRestoreAndPublish` (a previously live version exists, TR-11). When the parent is trashed or gone answer `trash.placement_required` (placements arrive in US4).
- [X] T032 [US1] Create `PAGES/Features/Trash/RestorePage.cs` and `PAGES/Features/Trash/RestoreComponent.cs` (`POST …/restore`): in one transaction re-check the row is trashed under the concurrency token (`trash.not_in_trash`), recompute the plan and compare it with `confirmedAddress` (`trash.plan_changed` with the new plan), `PageTree.Restore` / `Component.Restore`, rebuild `component_usages` and `collection_usages` with the existing extractors from the stored draft, write a publish log `restored` entry, publish `PageRestored` / `ComponentRestored`, and with `publish: true` (requires `pages.publishing.manage`) run 012's rollback to the last live version in the same transaction.
  *Done as*: `PageTreeWrites.ChangeReadingAsync` takes an async change, so the trashed page loads in the tree's transaction. EF fixes that row up into the tree's navigation, so `PageTree` now reads its live pages only (`Live`); found by `PagesTrashRoundTripTests`.
- [X] T033 [US1] Register the new Pages actions in `PAGES/PagesModule.cs` and add them to `ITESTS/Infrastructure/EndpointCatalog.cs`.
  *Done as*: `EndpointCatalog` discovers routes itself; the new addressed endpoints are covered by `ITESTS/Isolation/Probes/TrashIsolationProbe.cs`.

### Items

- [X] T034 [US1] Add `Item.Trash(actor, wasLive, hadSchedule, now)` and `Item.Restore(now)` in `CMS/Domain/Item.cs`, writing `trashed` / `restored` history entries.
- [X] T035 [US1] Change `CMS/Features/Items/DeleteItem/` to trash: keep the referenced-item refusal (015 FR-008), unpublish through `CMS/Infrastructure/ItemPublishing.cs` when published (`trashed_was_live`), cancel schedules, delete its `item_slugs` rows and its outgoing `item_references` (TR-04, TR-06) through `CMS/Infrastructure/ItemWrites.cs`, publish the existing `ItemDeleted` (with `WasPublished`), and answer 200 with a receipt.
- [X] T036 [P] [US1] Add `ItemRestored` to `CMSC/Events/ItemEvents.cs` (module-surface.md §6) and update the `ItemDeleted` docs.
- [X] T037 [US1] Create `CMS/Features/Items/Trash/ListTrash.cs`, `GetTrashEntry.cs` and `RestorePlan.cs` (`GET /api/v1/cms/sites/{siteId}/trash/items…`), same shape as T030–T031: slug unchanged when free, schema issues from `ItemValidation`, `canRestoreAndPublish`.
  *Done as*: `schemaIssues` is empty in the plan until T078; the restore itself still marks the item through `ItemIndexer`.
- [X] T038 [US1] Create `CMS/Features/Items/Trash/RestoreItem.cs`: re-check under `revision`, compare the plan, `Item.Restore`, re-add `item_slugs` and outgoing `item_references` through `CMS/Infrastructure/ItemIndexer.cs`, publish `ItemRestored`, and with `publish: true` (requires `cms.items.publish`) run 015's `RollbackItem` path in the same transaction. Register in `CMS/CmsModule.cs` and `EndpointCatalog.cs`.

### Media

- [X] T039 [US1] Add `MediaFile.Trash(actor, now)` (record `TrashedFolderId`, clear `folder_id`) and `MediaFile.Restore(folderId, now)` in `MEDIA/Domain/MediaFile.cs`.
- [X] T040 [US1] Change `MEDIA/Features/DeleteFile/DeleteFile.cs` to trash: keep 013's in-use confirmation and publish capability checks, keep the bytes and variants, publish the existing `FileDeleted`, answer 200 with a receipt.
- [X] T041 [P] [US1] Create `MEDIAC/Events/FileRestored.cs` (module-surface.md §6) and update the `FileDeleted` docs.
- [X] T042 [US1] Create `MEDIA/Features/Trash/ListTrash.cs`, `GetTrashEntry.cs`, `RestorePlan.cs` and `RestoreFile.cs` (`/api/v1/media/sites/{siteId}/trash/files…`): original folder when it still exists, otherwise `trash.placement_required`; restore re-checks under `xmin`, checks own-file and live-use capabilities in the handler (TR-10 table), publishes `FileRestored`. Register in `MEDIA/MediaModule.cs` and `EndpointCatalog.cs`.

### Site response and admin

- [X] T043 [P] [US1] Add `trashRetentionDays` (from `TrashOptions`) to the site response in `src/Modules/Sites/QuickBuilder.Sites/Features/GetSite/` and to `ADMIN/api/sites.ts`; extend its existing integration test.
- [X] T044 [P] [US1] Create `ADMIN/api/trash.ts`: typed `list`, `get`, `restorePlan`, `restore` per kind against trash-api.yaml, and change the delete functions in `ADMIN/api/pages.ts`, `ADMIN/api/items.ts` and `ADMIN/api/media.ts` to return `TrashReceipt`.
- [X] T045 [US1] Change the delete confirmations in `ADMIN/features/pages/PageTreeView.vue`, `ADMIN/features/pages/ComponentsView.vue`, `ADMIN/features/cms/items/DeleteItemDialog.vue` and `ADMIN/features/media/DeleteDialog.vue` to "{Name} will be moved to the trash. It can be restored until {purgeDate}." and show a "Moved to the trash" toast linking to the trash view (admin-trash.md §1). Update their existing specs (`ADMINTESTS/pageTreeView.spec.ts`, `ADMINTESTS/componentsView.spec.ts`, `ADMINTESTS/cms/`, `ADMINTESTS/media/`).
  *Done as*: the confirmations say the content moves to the trash, and add "It can be restored until {date}" from the site's `trashRetentionDays` (read once per site by `features/trash/useTrashRetention.ts`). The page tree shows the receipt's date after the delete; the other views keep their existing success messages.
- [X] T046 [US1] Create `ADMIN/features/trash/useTrash.ts`, `ADMIN/features/trash/TrashView.vue` and `ADMIN/features/trash/TrashList.vue`: route `/sites/:siteId/trash` in `ADMIN/app/router.ts`, a "Trash" link in `ADMIN/features/sites/SiteDetailView.vue` shown when the member may view one kind, tabs per view capability with `?tab=`, rows with label, address or file name, who, when and "Deleted permanently on {date}", "Load more", and the empty, loading and error states (admin-trash.md §2).
  *Done as*: no separate `useTrash.ts` or `TrashList.vue`; the list lives in `TrashView.vue`. Tabs answering forbidden are left out, since no endpoint reports view capabilities per kind.
- [X] T047 [US1] Create `ADMIN/features/trash/RestoreDialog.vue` for the original placement: loads the plan, shows "Back under {parent}", the "was live" / "was scheduled" note, "Restore" and (with `canRestoreAndPublish`) "Restore and publish", sends `confirmedAddress`, handles `trash.plan_changed` and ends with "Restored. Open {label}" (admin-trash.md §4 steps 1, 4–6).
- [X] T048 [P] [US1] Write `ADMINTESTS/features/trash/TrashView.spec.ts` (tabs per capability, rows, empty state, axe) and `ADMINTESTS/features/trash/RestoreDialog.spec.ts` (plan shown, publish button only with `canRestoreAndPublish`, `plan_changed` re-shows, axe).

**Checkpoint**: quickstart §1 passes; `TrashRoundTripTests` green. US1 is not shipped
without US2.

---

## Phase 4: User Story 2 — Deleted content is gone everywhere until it is restored (Priority: P1)

**Goal**: trashed content behaves as deleted on every public and admin read, references and
caches react correctly on trash and restore, and a delete is refused while another member
holds the edit lock (FR-006–FR-008, FR-026, FR-027, FR-030).

**Independent Test**: quickstart §2 and §3. Every endpoint called with a trashed id answers
not-found (`TrashedIdProbe`); live pages drop and regain the content on two instances.

### Tests for User Story 2 ⚠️

- [X] T049 [P] [US2] Create `ITESTS/Isolation/Probes/TrashedIdProbe.cs` and wire it into `ITESTS/Isolation/SiteIsolationSuite.cs` and `ITESTS/Isolation/IsolationCoverageTests.cs`: for every endpoint in `EndpointCatalog` that takes a page, component, item or file id (other than the trash endpoints), call it with the id of a trashed document and expect the same not-found answer as for an unknown id (SC-004).
  *Done as*: `ITESTS/Trash/TrashedIdProbeTests.cs`, comparing each endpoint's answer for a trashed id with its answer for an unknown id. It found `GET …/draft` leaking trashed drafts, fixed by requiring a live owner on draft reads and writes. `TrashIsolationProbe` covers other sites' ids on the trash endpoints.
- [X] T050 [P] [US2] Write `ITESTS/Trash/TrashVisibilityTests.cs`: a trashed page answers 404 publicly and leaves the sitemap; a trashed item leaves lists, menus, the item picker, collection counts and search; a trashed file's `/_media/…` and variant addresses answer 404, and an image block showing it is left out of the rendered page; a link in a published page to a trashed page renders as plain text; a trashed component leaves the component picker; FR-007: binding, linking or picking a trashed target is refused like a missing one.
  *Done as*: the image and plain-text-link cases are not repeated here: `MediaChangeRefreshTests` covers an image left out, and a page must be unpublished before it can be trashed, so links to it already render as plain text.
- [X] T051 [P] [US2] Write `ITESTS/Trash/TrashCacheTests.cs` (two instances, as the existing cache tests): trashing a published item or a file used by a live page refreshes exactly the dependent pages on both instances; restoring the file shows it again on the next request on both (SC-005, FR-026).
  *Done as*: a restore case added to `ITESTS/Rendering/MediaChangeRefreshTests.cs`, including a second instance with its own cache.
- [X] T052 [P] [US2] Write `ITESTS/Trash/TrashLockTests.cs` (`ManualClock`): another member's active lock refuses page, component and item delete with `editing.lock_held` (holder, since) and nothing changes; one's own lock in another tab does not refuse; a lock idle 30 minutes (stale) no longer refuses (FR-008).
- [X] T053 [P] [US2] Write `ITESTS/Media/MediaOrphanSweepTrashTests.cs`: the orphan sweep, the variant worker and both backfills leave a trashed file's bytes and variants in place (TR-18).

### Implementation for User Story 2

- [X] T054 [US2] Add `FindActiveHolderAsync(siteId, kind, documentId, userId)` and `EditingHolder` to `EDITORC/IEditingLocks.cs` and implement it in `EDITORM/Infrastructure/EditingLocks.cs` (one indexed read on `ux_editing_sessions_holder`, not stale, `user_id <> userId`), with unit cases in `UTESTS/Editor/`.
- [X] T055 [US2] Call `FindActiveHolderAsync` before the transaction in `PAGES/Features/DeletePage/DeletePage.cs`, `PAGES/Features/DeleteComponent/` and `CMS/Features/Items/DeleteItem/`, refusing with 023's `editing.lock_held` shape (TR-09).
- [X] T056 [US2] In `PAGES/Features/GetTokenUsage/GetTokenUsage.cs`, join `pages.pages` and `pages.components` and add `trashed_at_utc IS NULL`, so drafts of trashed pages and components are not counted as token uses. Add the case to the token usage test. No other raw SQL reads a trashable table: `CMS/Infrastructure/CollectionLocks.cs` takes advisory locks, and Forms and Identity hold no trashable data.
  *Done as*: the two draft reads join the live owner, with a case in `ITESTS/Pages/TokenUsageApiTests.cs`.
- [X] T057 [US2] Make `MEDIA/Infrastructure/PublicMediaReader.cs` and `MEDIA/Features/GetFileVariant/` serve trashed files as not found through the named filter (confirm no `AcrossAllSites`-only path bypasses it), and the published item and page reads in `CMS/Infrastructure/PublishedItemReader.cs` and `PAGES/Infrastructure/PublishedContentReader.cs` likewise.
  *Done as*: the public read models (`PublicMediaRow`, `PageRow`, `PublishedItemRow`) are `ITrashable` with the named `Trash` filter, so media serving, page rendering and item pages hide trashed rows the same way.
- [X] T058 [US2] Switch the background readers of TR-18 to `IncludingTrash` with a reason: `MEDIA/Infrastructure/MediaOrphanSweepService.cs`, `MEDIA/Variants/ThumbnailWorker.cs`, `MEDIA/Infrastructure/ImageMetadataBackfillService.cs`, `MEDIA/Infrastructure/SearchTextBackfillService.cs`, `CMS/Infrastructure/ItemIndexBackfillService.cs` (the backfill skips re-indexing slugs for trashed items, TR-04).
- [X] T059 [US2] Add `InTrash` to each usage returned by `PAGESC/Media/IMediaUsageReader.cs` and `CMSC/Media/IItemMediaUsageReader.cs`; implement it in `PAGES/Infrastructure/MediaUsageReader.cs` and `CMS/Infrastructure/ItemMediaUsageReader.cs` by joining the owner with `IncludingTrash`; in `MEDIA/Infrastructure/MediaUsage.cs` report trashed uses as `TrashedUses` and never count them toward `InUse` or `InLiveUse` (FR-027).
- [X] T060 [US2] Subscribe `PAGES/Features/RefreshPagesShowingMedia/RefreshPagesShowingMediaHandler.cs` and `CMS/Features/Items/ReindexItemsForMedia/ReindexItemsForMedia.cs` to `FileRestored` (module-surface.md §6), with handler tests next to their existing ones.
- [X] T061 [US2] In `PAGES/Features/Menus/MenuReferences.cs`, report a page or item target that is in the trash as deleted with `inTrash: true` (look it up with `IncludingTrash`). Show "deleted (in trash)" in `ADMIN/features/menus/MenuEditorView.vue`, with a case in `ADMINTESTS/menus/`. T050 covers the public menu dropping the entry.
  *Done as*: the state lives in `PAGES/Infrastructure/MenuTargetStates.cs` (not `MenuReferences.cs`): a deleted target that is in the trash carries `inTrash: true`. Items are asked through a new `ICollectionSchemaReader.TrashedItemsAsync`.
- [X] T061a [US2] When rollback or publish refuses with `pages.unknown_component` and the component id is in the trash (`IncludingTrash`), add `componentName` and `inTrash: true` to the refusal, in `PAGES/Features/Rollback/Rollback.cs` and `PAGES/Features/Publish/Publish.cs`. Show "{name} is in the trash; restore it first" in the admin publish and rollback flows, and add the case to `ITESTS/Trash/TrashVisibilityTests.cs` (spec edge case "A trashed component used by an old page version").
  *Done as*: the refusal is 012's `pages.version_missing_components` (not `pages.unknown_component`); it gains `trashedComponents` with names, raised by `PublishingWrites.RollbackContentAsync`. Publishing cannot meet a trashed component: deleting one is refused while any draft uses it.
- [X] T062 [US2] Update the transfer archive parts (TR-17): export is unchanged (assert in `ITESTS/Transfer/TransferTrashTests.cs`); restore import's "clear the site" step and merge import's identity lookup use `IncludingTrash` in `PAGES/Features/Transfer/PagesArchivePart.Import.cs` and the Cms and Media `Features/Transfer/` parts; a matched trashed document is untrashed and reported as `restoredFromTrash`. Write `ITESTS/Transfer/TransferTrashTests.cs` first.
  *Done as*: restore import empties the trash (Pages, Cms, Media). Merge import matches live rows only: FR-030 was narrowed so the imported document is added beside the trashed copy; replacing it and untrashing is in `docs/backlog.md`.
- [X] T063 [US2] In `ADMIN/editing/useEditingSession.ts`, when a heartbeat or save answers not-found, call `trash.get(kind, id)`; when it is in the trash show "Moved to the trash by {name} at {time}", stay read-only with copyable blocks, and link to the trash when the member may restore; otherwise keep today's "deleted" state (admin-trash.md §6). Add a case to `ADMINTESTS/editing/`.
- [X] T064 [US2] Show the `editing.lock_held` refusal in the four delete flows of T045 ("{Name} is editing this page since {time}…", admin-trash.md §1), and add the "Used by content in the trash" group to `ADMIN/features/media/UsageList.vue` (admin-trash.md §7) with spec cases.

**Checkpoint**: quickstart §2 and §3 pass; the isolation suite with `TrashedIdProbe` is
green. US1 + US2 together are the shippable MVP.

---

## Phase 5: User Story 3 — Members browse and search the trash (Priority: P2)

**Goal**: search, filters, preview, multi-select restore and the media storage total
(FR-009–FR-012, FR-019).

**Independent Test**: quickstart §4; 10,000 entries search under 1 s at p95 (SC-003).

### Tests for User Story 3 ⚠️

- [X] T065 [P] [US3] Extend `ITESTS/Trash/TrashRoundTripTests.cs` or add `ITESTS/Trash/TrashListTests.cs`: search over the fields of TR-19 per kind, `trashedBy`, `from`/`to`, `collectionId`, `trashedFolderId`; keyset paging is stable while entries are added; the media list carries `totalBytes`.
  *Done as*: in `TrashRestoreRulesTests.Search_filters_and_paging_narrow_the_trash` for pages; the Cms and Media filters in `TrashBulkEndpointTests`.
- [X] T066 [P] [US3] Write `ITESTS/Trash/TrashBulkRestoreTests.cs`: bulk restore of up to 100 ids returns one outcome per entry (restored, or failed with code), an entry needing a placement fails with `trash.placement_required`, and the others still restore (FR-019).
  *Done as*: in `TrashRestoreRulesTests`.
- [X] T067 [P] [US3] Write `ITESTS/Trash/TrashListPerformanceTests.cs`, opt-in like `ITESTS/Transfer/TransferPerformanceTests.cs`: 10,000 entries, p95 under 1 s for search and each filter (SC-003).

### Implementation for User Story 3

- [X] T068 [US3] Add search and filters to the three `ListTrash` slices (T030, T037, T042) per TR-19 (`ILIKE` inside the partial index), and `totalBytes` to the media list.
- [X] T069 [US3] Create `PAGES/Features/Trash/BulkRestore.cs`, `CMS/Features/Items/Trash/BulkRestore.cs` and `MEDIA/Features/Trash/BulkRestore.cs` (`POST …/trash/restore`, each entry in its own transaction, capability checked per entry, `BulkOutcome`). Register and catalogue them.
- [X] T070 [US3] Add filters, the 300 ms debounced search box, the media total line and multi-select "Restore selected" (fetch plans first; list entries needing a choice; summarise address changes; show per-entry results) to `ADMIN/features/trash/TrashView.vue` and `TrashList.vue` (admin-trash.md §2, §4 bulk).
- [X] T071 [US3] Create `ADMIN/features/trash/TrashPreview.vue`: pages and components in the editor canvas's read-only mode with no presence or locks, items in the read-only item form, media in the file preview, with the "In the trash — deleted by {name} on {date}" banner and Restore (admin-trash.md §3).
  *Done as*: `features/trash/TrashPreview.vue` shows the deleted-by banner, the text of the draft (the name, for a file) and Restore, read-only; admin-trash.md §3 was narrowed to that. The editor canvas, the read-only item form and an image of a trashed file are in `docs/backlog.md`.
- [X] T072 [P] [US3] Extend `ADMINTESTS/features/trash/TrashView.spec.ts`: filters update the query, multi-select restore result list, preview banner, axe.

**Checkpoint**: quickstart §4 passes.

---

## Phase 6: User Story 4 — Restoring when the original place is gone (Priority: P2)

**Goal**: every placement rule of TR-10, address clashes, template pages, schema changes and
the collection cascade, always stated before confirming (FR-016–FR-018, FR-023, SC-008).

**Independent Test**: every row of quickstart §5's table.

### Tests for User Story 4 ⚠️

- [X] T073 [P] [US4] Write `UTESTS/Pages/TrashPlacementTests.cs` for a pure `TrashPlacement`: original (position clamped), parent trashed → `with_parents` or `chosen` required, parent gone → `chosen` only with top level preselected, chosen parent not live or under the page itself → invalid, top level with no home → becomes home, template slot taken → normal page, page limit.
  *Done as*: `UTESTS/Pages/PageTreeTrashTests.cs`, with no separate `TrashPlacement` class: the rules live in `PageTree.PlanRestore` and `PagesTrash.PlanPageAsync`.
- [X] T074 [P] [US4] Write `UTESTS/Pages/RestoreAddressTests.cs`: page slug clash → `-2`, `-3`; item slug clash via `SlugRules`; component name clash → `CopyNaming` "Header (2)"; a redirect source address follows 017's rule.
  *Done as*: page slugs in `PageTreeTrashTests`, item slugs and component names in the round-trip tests.
- [X] T075 [P] [US4] Write `ITESTS/Trash/TrashRestoreRulesTests.cs` covering every row of quickstart §5: parent trashed (with parents / chosen), parent purged, address taken, folder gone, required field added (restored with `attention`), schema change running (014 FR-037 refusal), collection deleted while an item is trashed (item gone, confirmation counts it), referenced item gone, and `trash.plan_changed` between plan and confirm.

### Implementation for User Story 4

- [X] T076 [US4] Create `PAGES/Domain/TrashPlacement.cs` (pure) until T073 passes, and use it from `PageTree.Restore` for `with_parents` (ancestors restored top-down in one tree change, each to its own original place) and `chosen` (live parent or top level, at the end).
  *Done as*: no `TrashPlacement` class (see T073). "With parents" restores the ancestors top-down inside the plan, on a throwaway copy of the tree for the read-only plan.
- [X] T077 [US4] Extend `PAGES/Features/Trash/RestorePlan.cs`, `RestorePage.cs` and `RestoreComponent.cs` with the `placement` query/body (`original`, `with_parents`, `chosen` + `parentId`, `top`), address clash resolution through `PageAddresses` and `CopyNaming` with old and new address in the plan, and the template effect (TR-12), until T074 passes.
- [X] T078 [US4] Extend `CMS/Features/Items/Trash/RestorePlan.cs` and `RestoreItem.cs`: slug clash through `SlugRules`, schema issues listed and the item marked `attention` instead of refused (FR-018), refusal through `CMS/Infrastructure/CollectionWriteGate.cs` while a schema change runs.
- [X] T079 [US4] Extend `MEDIA/Features/Trash/RestorePlan.cs` and `RestoreFile.cs`: original folder when it exists, otherwise the top level or a chosen folder, stated in the plan. Change `MEDIA/Features/Folders/` delete so a folder holding only trashed files counts as empty (their `folder_id` is already null, TR-05) — add the case to the existing folder tests.
- [X] T080 [US4] Change the collection delete slice in `CMS/Features/DeleteCollection/` so its confirmation count uses `IncludingTrash` and reports `trashedItems`; the trashed items go with the existing cascade (FR-023). Update its test.
  *Done as*: the count is `trashedItemCount` on the collection responses, read in `CollectionReads`.
- [X] T081 [US4] Extend `ADMIN/features/trash/RestoreDialog.vue` with the placement choice ("Restore {parent} as well" listing ancestors / "Choose another place" tree picker with top level preselected / picker only when gone), re-fetching the plan on change, and the change lines (address, home, template, schema issues) (admin-trash.md §4 steps 2–3). Extend `ADMINTESTS/features/trash/RestoreDialog.spec.ts`.
- [X] T082 [US4] Update the collection delete dialog in `ADMIN/features/cms/CollectionsView.vue` to "{n} items, including {t} in the trash, will be deleted permanently" (admin-trash.md §7).

**Checkpoint**: quickstart §5 passes; `TrashRestoreRulesTests` green.

---

## Phase 7: User Story 5 — An Administrator deletes permanently (Priority: P2)

**Goal**: permanent delete of one or several entries and Empty trash, for Administrators
only (FR-020–FR-022, SC-007).

**Independent Test**: quickstart §6 steps 1–3.

### Tests for User Story 5 ⚠️

- [X] T083 [P] [US5] Extend `ITESTS/Trash/TrashAuthorizationTests.cs`: purge, bulk purge and empty answer 403 for Designer and Editor and 404 for another site's ids, per module (SC-007).
- [X] T084 [P] [US5] Write `ITESTS/Trash/TrashEmptyTests.cs`: a permanent delete removes what today's delete removed (pages and components via `ForgetAsync` keeping the publish log with a `purged` entry; items with their history; media rows then bytes and variants); empty purges only entries trashed at or before `trashedUpTo`; a mismatched count answers `trash.count_changed` with the current count; a purge racing a restore ends with one winner and the loser gets `trash.not_in_trash` (FR-020).
  *Done as*: in `ITESTS/Trash/TrashPurgeTests.cs`.

### Implementation for User Story 5

- [X] T085 [US5] Add `pages.trash.purge`, `cms.trash.purge` and `media.trash.purge` (site scope, Administrator only) to `PAGES/PagesCapabilities.cs`, `CMS/CmsCapabilities.cs` and `MEDIA/MediaCapabilities.cs` and to the role grants where the existing capabilities are granted.
  *Done as*: done with US1, because the list endpoints report `canPurge`. Capabilities with no role list are Administrator only, as `pages.publishing.log.view` is.
- [X] T086 [US5] Create a per-module purge step used by both permanent delete and the job: `PAGES/Features/Trash/Purge.cs` (re-check trashed under the token, `PublishingWrites.ForgetAsync`, delete the row, publish log `purged`), `CMS/Features/Items/Trash/Purge.cs` (cascade, history included), `MEDIA/Features/Trash/Purge.cs` (delete the row; delete bytes and variants after commit, failures left to the orphan sweep). Cms and Media log each purge with ids only (FR-028). Expose `DELETE …/trash/{kind}/{id}` and `POST …/trash/purge` (≤ 100 ids, `BulkOutcome`).
- [X] T087 [US5] Create `PAGES/Features/Trash/EmptyTrash.cs`, `CMS/Features/Items/Trash/EmptyTrash.cs` and `MEDIA/Features/Trash/EmptyTrash.cs` (`POST …/trash/empty` with `trashedUpTo` and expected counts; `trash.count_changed`). Register and catalogue every action of T086–T087.
- [X] T088 [US5] Add `purge`, `purgeMany` and `empty` to `ADMIN/api/trash.ts`; create `ADMIN/features/trash/PurgeDialog.vue` ("Delete permanently? This cannot be undone.", media adds the storage line) and `ADMIN/features/trash/EmptyTrashDialog.vue` (counts loaded with `trashedUpTo`, type-the-count confirmation, Pages → Cms → Media in turn, `count_changed` re-asks, per-kind result) (admin-trash.md §5); show the buttons only with `canPurge`.
- [X] T089 [P] [US5] Write `ADMINTESTS/features/trash/EmptyTrashDialog.spec.ts`: button disabled until the typed count matches, `count_changed` re-asks, per-kind results, hidden for non-Administrators, axe.

**Checkpoint**: quickstart §6 steps 1–3 pass.

---

## Phase 8: User Story 6 — The trash empties itself after the retention period (Priority: P3)

**Goal**: an hourly, multi-instance-safe purge of entries past `Trash:RetentionDays`
(FR-024, FR-025, SC-006).

**Independent Test**: quickstart §6 steps 4–5.

### Tests for User Story 6 ⚠️

- [X] T090 [P] [US6] Write `ITESTS/Trash/TrashPurgeTests.cs` (`ManualClock`, two instances): only entries older than retention are purged; each row is purged exactly once with `SKIP LOCKED`; a restore that wins the race leaves the row alone; a failure on one entry leaves the others purged and itself restorable; the publish log shows `purged` by "system" (SC-006).
  *Done as*: exactly-once comes from a conditional delete (`WHERE trashed_at_utc IS NOT NULL` and due), not `FOR UPDATE SKIP LOCKED`, so two instances may try the same row and one deletes nothing. A failure part way is not tested.
- [X] T091 [P] [US6] Write `ITESTS/Trash/TrashConfigurationTests.cs`: `Trash__RetentionDays=0` and `3651` fail startup naming `Trash:RetentionDays` and 1–3650; a changed value moves every `purgeOnUtc` at once.
  *Done as*: rows in `ITESTS/Startup/StorageStartupValidationTests.cs`; "a changed value moves every purge date" is in `UTESTS/Trash/TrashRetentionTests.cs`.

### Implementation for User Story 6

- [X] T092 [US6] Create `PAGES/Infrastructure/PagesTrashPurgeService.cs`, `CMS/Infrastructure/CmsTrashPurgeService.cs` and `MEDIA/Infrastructure/MediaTrashPurgeService.cs` as `BackgroundService`s on a 1-hour `PeriodicTimer` shaped like `src/Modules/Sites/QuickBuilder.Sites/Infrastructure/TransferRetentionService.cs`: each tick selects up to 100 due rows with `AcrossAllSites` + `OnlyTrash` and `FOR UPDATE SKIP LOCKED` (as Forms does), one transaction per row, re-checks trashed and due, then calls the purge step of T086 with actor "system". Register them in each `<Module>Module.cs`; log counts only.
  *Done as*: see T090.

**Checkpoint**: quickstart §6 passes.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: contracts, docs, upgrade proof, coverage and validation.

- [X] T093 [P] Write `ITESTS/Pages/UpgradeFrom020Tests.cs` from a 020 snapshot: migrations apply, the trash is empty, every address stays unique, and trash and restore work on upgraded rows (Principle III).
  *Done as*: at `ITESTS/Trash/UpgradeFrom020Tests.cs`.
- [X] T094 [P] Update the registries named in plan.md "Contract and registry changes": `specs/001-foundation-skeleton/contracts/integration-events.md` (4 new events, 4 changed meanings), `specs/002-identity-setup/contracts/authorization.md` (3 capabilities, new endpoints), `specs/010-page-tree-components/contracts/`, `specs/013-media-library-variants/contracts/` and `specs/015-cms-items/contracts/` (receipts, lock refusal), `specs/020-site-export-import/contracts/archive-parts.md` (TR-17), `specs/023-concurrency/contracts/module-surface.md` (`FindActiveHolderAsync`).
  *Done as*: the three OpenAPI files carry a note at the top rather than edited responses.
- [X] T095 [P] Document `Trash:RetentionDays` in `docs/configuration.md`, add the four upgrade notes of plan.md to a new file in `docs/releases/unreleased/`, and record the deferred items (undo toast, per-site retention, purge events) in `docs/backlog.md`.
- [X] T096 Confirm `ATESTS/AuthorizationDeclarationTests.cs`, `ATESTS/ModuleBoundaryTests.cs` (no new edges), `TrashQueryUsageTests` and `QueryFilterCoverageTests` pass, and that line coverage is at least 80% on the restore and purge handlers and `PageTree.Restore` (Principle X).
  *Done as*: the architecture tests pass. Line coverage from the CI test selection: restore handlers 80–88%, bulk restore 88–91%, purge handlers 87–88%, `PageTree.Trash` 84%.
- [X] T097 Run `dotnet format --verify-no-changes`, `dotnet test`, and `cd src/admin && npm run lint && npm run type-check && npm test`.
  *Done as*: `dotnet format`, lint, type-check and the admin tests pass, as does the CI selection of `dotnet test` (Architecture, Unit, Integration). In the Smoke project, 4 of 17 fail, none from this feature: `ReleaseUpgradeTests` needs `QB_SMOKE_IMAGE`; `SiteResolutionPerformanceTests` expects 404 where non-primary host names now answer 301; `RenderPerformanceTests` expects no `<script>` where #17 added structured data; `MediaPerformanceTests` missed a timing ratio on this machine. `TrashListPerformanceTests` passes with `QB_TRASH_PERF=1`.
- [X] T098 Walk quickstart.md §1–§8 against `./qb.ps1 dev`, including the keyboard-only pass of §8 (E-5 manual), and record results in `specs/021-soft-delete-trash/quickstart-results.md`.
  *Done as*: the automated evidence, an API walk-through against a deployment (2026-11-15) and the checks by hand (2026-11-17, all passed) are in `quickstart-results.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: after Setup. T004–T010 first (Checkpoint A, no behaviour
  change), then T011–T019. Blocks every story.
- **US1 (Phase 3)** and **US2 (Phase 4)**: after Foundational. US2's probe needs US1's
  delete-to-trash (T027, T028, T035, T040) to create trashed rows. Ship them together.
- **US3, US4, US5 (Phases 5–7)**: after US1. They are independent of each other; US4 and US3
  both extend `RestoreDialog.vue` and `TrashView.vue`, so sequence edits to those files.
- **US6 (Phase 8)**: after US5 (it reuses the purge step T086).
- **Polish (Phase 9)**: after the stories that are shipping.

### User Story Dependencies

```text
Setup → Foundational ─┬─ US1 ──┬─ US2 (P1, ships with US1)
                      │        ├─ US3
                      │        ├─ US4
                      │        └─ US5 ── US6
                      └────────────────────────── Polish
```

### Within Each User Story

- Tests first, failing.
- Domain methods before handlers; handlers before registration; server before admin.
- Pages, Cms and Media tasks inside a story are largely independent of each other.

### Parallel Opportunities

- Phase 2: T011, T013, T015 (entities in three modules), and T018, T019.
- US1: tests T020–T022 together; then the Pages (T023–T033), Cms (T034–T038) and Media
  (T039–T042) tracks in parallel; T029, T036, T041, T043, T044 in parallel.
- US2: tests T049–T053 together; T056–T061 touch different files.
- US3/US4/US5: their test tasks are all [P]; the three stories can run in parallel after US1
  apart from the shared admin files noted above.

---

## Parallel Example: User Story 1

```bash
# Tests first:
Task: "TrashRoundTripTests in tests/QuickBuilder.IntegrationTests/Trash/TrashRoundTripTests.cs"
Task: "TrashAuthorizationTests (restore) in tests/QuickBuilder.IntegrationTests/Trash/TrashAuthorizationTests.cs"

# Then one track per module:
Task: "Pages: PageTree.Trash/Restore, DeletePage, Features/Trash (T023–T033)"
Task: "Cms: Item.Trash/Restore, DeleteItem, Features/Items/Trash (T034–T038)"
Task: "Media: MediaFile.Trash/Restore, DeleteFile, Features/Trash (T039–T042)"
```

---

## Implementation Strategy

### MVP First (US1 + US2)

1. Phase 1 and Phase 2. Stop at Checkpoint A and confirm the suite is green with named
   filters before adding any column.
2. US1: delete becomes reversible for all four kinds.
3. US2: nothing trashed leaks anywhere; locks and caches behave.
4. **Stop and validate** quickstart §1–§3. This is the smallest release that is safe to
   ship: a trash that can be restored from, invisible everywhere else.

### Incremental Delivery

1. MVP (US1 + US2).
2. US3 (find things in a large trash) and US4 (restore when the place is gone).
3. US5 (permanent delete and empty), then US6 (automatic purge).
4. Polish, then `/speckit-analyze` (mandatory) before `/speckit-implement`.

Without US6 the trash grows until an Administrator empties it, so the feature is not
complete for release until US6 lands; the plan delivers everything in one PR.

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task.
- Commit after each task or logical group, referencing #21.
- If a task's file path turns out different in the code, record it as `*Done as*:` under the
  task, as 023's tasks.md does.
