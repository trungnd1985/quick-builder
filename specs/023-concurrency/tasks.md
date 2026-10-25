---

description: "Task list for 023-concurrency"
---

# Tasks: Concurrent Editing Safety

**Input**: Design documents from `/specs/023-concurrency/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires integration tests for every endpoint, unit tests for
  domain logic, and 80% coverage on critical paths. Draft saves are on the save path.
- The spec measures behaviour through automated checks: SC-001, SC-003, SC-004 and SC-006.
- Within each story, test tasks come first and must fail before implementation starts.

**Changed after analysis (2026-10-25)**: the authorization convention was corrected;
T017, T019, T045, T051, T066, T068 and T072 were extended; T039 lost its [P] marker. No
task was added or renumbered. Spec FR-009, FR-009a and FR-022 were reworded to match
research CE-04 and CE-11, and quickstart §9 and §10 were added.

**Organization**: tasks are grouped by user story (US1–US4 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US4)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `EDITORM` = `src/Modules/Editor/QuickBuilder.Editor`
- `EDITORC` = `src/Modules/Editor/QuickBuilder.Editor.Contracts`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CMSC` = `src/Modules/Cms/QuickBuilder.Cms.Contracts`
- `ADMIN` = `src/admin/src`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`

**Conventions**:
- **Backend slices** live under `<Module>/Features/<Slice>/<Slice>.cs`, modelled on
  `PAGES/Features/SavePageDraft/SavePageDraft.cs`. Every controller action:
  - is routed as in [contracts/editing-api.yaml](./contracts/editing-api.yaml);
  - declares exactly one access attribute, as `ATESTS/AuthorizationDeclarationTests.cs`
    requires:
    - the copy endpoints declare `[RequireCapability(...)]` with the create capability;
    - the editing-session actions, whose capability depends on the document kind, declare
      `[RequireCapability(PageContentCapabilities.View)]`, which every site role holds, so a
      member with no role on the site gets the usual 404. Their handlers then check the
      kind's view or edit capability through `EditingAccess` (research CE-16). (Changed
      during T009 from `[RequireSignedIn]`, which would have needed the 404 rebuilt by
      hand.);
  - dispatches through `IDispatcher`;
  - refuses with the codes in [data-model.md §5](./data-model.md);
  - is registered in the module's `<Module>Module.cs` and listed in
    `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **Time** comes from `TimeProvider` on the server (tests use `ITESTS/Infrastructure/ManualClock.cs`)
  and from `serverNow` in responses in the admin. The admin never uses the device clock
  for "since" or "inactive" values.
- **Constants**: `EditingRules.Expiry = 2 min`, `EditingRules.StalePeriod = 30 min`,
  `HeartbeatInterval = 10 s` (admin), list marker refresh 15 s. No configuration keys.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task.
- **Logs and takeover rows** carry ids, kinds, codes and durations only, never titles,
  names, slugs or content.

**References**:
- decisions: `CE-xx` in [research.md](./research.md);
- tables, states, rules and errors: [data-model.md](./data-model.md);
- endpoints: [contracts/editing-api.yaml](./contracts/editing-api.yaml);
- admin behaviour: [contracts/admin-editing.md](./contracts/admin-editing.md);
- interfaces, edges, events and export: [contracts/module-surface.md](./contracts/module-surface.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents and the two new module edges.

- [X] T001 Commit `specs/023-concurrency/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `023-concurrency`, referencing #23.
- [X] T002 Add the project references of module-surface.md §1: `EDITORM/QuickBuilder.Editor.csproj` → `src/Modules/Cms/QuickBuilder.Cms.Contracts`, and `CMS/QuickBuilder.Cms.csproj` → `src/Modules/Editor/QuickBuilder.Editor.Contracts`. Allow both edges in `ATESTS/ModuleBoundaryTests.cs` with a comment citing research CE-21, and keep `Editor_contracts_reference_only_the_kernel` green.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: what every story builds on:
- the Contracts surface;
- the `EditingSession` aggregate and its rules;
- the Editor migration;
- the editing error codes.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Contracts (module-surface.md §2, §3)

- [X] T003 [P] Create `EDITORC/EditingDocumentKinds.cs` (`page`, `component`, `item`), `EDITORC/EditingHeaders.cs` (`X-QB-Editing-Session`) and `EDITORC/IEditingLocks.cs` with `EditingWriteCheck` exactly as module-surface.md §2, with XML docs citing CE-07.
  *Done as*: All three are in `EDITORC/IEditingLocks.cs`, as `IDesignTokenReader.cs` keeps its DTO beside the interface. `EditingHeaders.Parse` and `EditingWriteCheck.HeldBy` were added for the callers.
- [X] T004 [P] Create `CMSC/ItemCapabilities.cs` (`CollectionsView`, `ItemsEdit`) and make `CMS/CmsCapabilities.cs` alias them, as `PagesCapabilities` aliases `PageContentCapabilities` (CE-16). No capability string changes.

### Domain (data-model §1, CE-02 to CE-06)

- [X] T005 [P] Write `UTESTS/Editor/EditingRulesTests.cs` (fake clock), covering:
  - gone at exactly 2 min since `last_seen_at`;
  - stale at exactly 30 min since `last_activity_at`;
  - a viewer with no activity is never stale;
  - a claim is allowed when there is no holder or only a gone one, and refused otherwise;
  - `CanTakeOver` is true only when the named holder is still the holder, is stale and is
    another user;
  - `CanMove` is true only for the same user, whether the lock is active or stale;
  - activity is ignored for viewers.

  The tests must fail first.
- [X] T006 Create `EDITORM/Domain/EditingRules.cs` (constants and pure decisions), `EDITORM/Domain/EditingSession.cs` (aggregate with `ISiteOwned`: `Open`, `Seen(active, now)`, `TakeLock(now)`, `LoseLock(byUser, reason, now)`; invariants: `lock_taken_at` and `last_activity_at` are set if and only if `holds_lock`; `lost_lock_*` all set or all null) and `EDITORM/Domain/EditingTakeover.cs`, until T005 passes.

### Database (data-model §1)

- [X] T007 Map `editing_sessions` and `editing_takeovers` in `EDITORM/Infrastructure/EditorDbContext.cs`:
  - the global site filter;
  - the partial unique index `ux_editing_sessions_holder` on `(site_id, document_kind, document_id) WHERE holds_lock`;
  - the document and `last_seen_at` indexes;
  - the kind and reason check constraints;
  - the `(site_id, taken_at)` index on takeovers.

  Then generate `EDITORM/Infrastructure/Migrations/<ts>_AddEditingSessions.cs` and update the model snapshot.

### Errors and access (data-model §5, CE-16)

- [X] T008 [P] Add `editing.lock_held` (with `holder`, `since`, `stale`), `editing.not_stale` (with `holder`, `since`, `inactiveSeconds`), `editing.session_gone` and `editing.forbidden` to `EDITORM/EditorErrors.cs`, and map them in `EDITORM/EditorErrorResults.cs` (409, 409, 404, 403).
- [X] T009 Create `EDITORM/Infrastructure/EditingAccess.cs`: the kind-to-capability table of CE-16 (`PageContentCapabilities`, `pages.components.manage` through a constant in Editor citing Pages' value, and `ItemCapabilities`), `MayViewAsync(siteId, kind)` and `MayEditAsync(siteId, kind)` over `IAccessEvaluator`. Add unit cases to `UTESTS/Editor/EditingAccessTests.cs`.
  *Done as*: `pages.components.manage` moved to `PageContentCapabilities.ComponentsManage` in Pages.Contracts (aliased by `PagesCapabilities`), so Editor repeats no string. The Editor unit tests (46) and architecture tests (59) pass.

**Checkpoint**: the migration applies, the rules pass, and no endpoint exists yet.

---

## Phase 3: User Story 1 - No draft save ever overwrites someone else's change (Priority: P1) 🎯 MVP

**Goal**:
- every draft write to a page, component or item is refused when out of date;
- the refusal names who changed it and when;
- an item publish by someone else no longer refuses the next draft save;
- the admin shows a conflict dialog with reload and keep viewing. Save as a copy arrives in
  US4.

**Independent Test**: quickstart §1. Two sessions on each document kind and each write path;
every second write is refused with `changedBy` and `changedAtUtc`; the first write is
intact; the dialog names the member and time.

### Tests for User Story 1 ⚠️

- [X] T010 [P] [US1] Create `ITESTS/Concurrency/OverlappingDraftWritesTests.cs` (SC-001): 1,000 pairs of overlapping draft writes, spread over pages, components and items, from two members using the same base version. Expect zero overwrites (each stored draft equals exactly one of the pair), and every refusal carries `changedBy` naming the other member and a `changedAtUtc`.
  *Done as*: 1,000 pairs in 12 s: 8 pages, 8 components and 8 items, each round a pair per document from two Designers on the same base. Zero overwrites; every refusal names the stored writer.
- [X] T011 [P] [US1] Extend `ITESTS/Pages/` draft save tests (the existing page and component draft suites) with: `pages.out_of_date` carries `changedBy {id, displayName}`, `changedAtUtc` and `currentVersion`; an unchanged request shape from an old client still gets the code.
  *Done as*: In a new file, `ITESTS/Pages/DraftConflictDetailsTests.cs`, so the 010 suites stay as they were. `PagesApi.DocumentWithText` was added for documents that differ by one heading.
- [X] T012 [P] [US1] Create `ITESTS/Cms/Items/ItemPublishDoesNotConflictTests.cs`: after member B publishes, unpublishes or schedules the item, member A's draft save with the latest `draftRevision` succeeds. A save based on an older draft revision gets `cms.item_conflict` with `changeKind: member`, `changedBy`, `changedAtUtc` and `currentDraftRevision`. A save with no `draftRevision` falls back to `revision` (today's behaviour). After a schema change rewrites the draft, the refusal says `changeKind: schema_change` with `changedBy: null`. Copy-to-draft also moves `draftRevision`.
- [X] T013 [P] [US1] Create `UTESTS/Cms/ItemDraftRevisionTests.cs`: `SaveDraft` and `CopyToDraft` increment `DraftRevision` and set `DraftChangedBy`, `DraftChangedAtUtc` and `DraftChangeKind = member`. `Migrate` increments it and sets `schema_change` only when the draft values change. `Publish`, `Unpublish` and `RecordSchedule` do not move it.
- [X] T014 [P] [US1] Create `ADMINTESTS/editing/ConflictDialog.spec.ts` (with axe), covering:
  - the text names the member and time, or the system change;
  - the dialog states that nothing was overwritten;
  - Reload asks for confirmation;
  - Keep viewing closes into the read-only `conflict` banner;
  - there is no overwrite action;
  - focus starts on the first action;
  - the item variant lists changed fields.
- [X] T015 [P] [US1] Create `ADMINTESTS/editing/itemDiff.spec.ts`: values are compared by field id; added, removed and changed fields are reported; equal JSON with a different key order is not a change; reference and rich-text values compare structurally.
- [X] T016 [P] [US1] Extend `ADMINTESTS/editor/autosave.spec.ts`: two quick saves in one session never produce a conflict (FR-004), and a 409 with `changedBy` stores the details for the dialog.

### Implementation for User Story 1

- [X] T017 [US1] In `PAGES/Infrastructure/DraftWrites.cs`, return the conflict details on both `OutOfDate` paths (the version comparison and `DbUpdateConcurrencyException`): reload the draft's `UpdatedBy`, `UpdatedAtUtc` and `Version` with no tracking, and resolve the name through `IUserDirectory`. Add `PagesErrors.DraftOutOfDate(changedBy, changedAtUtc, currentVersion)` in `PAGES/PagesErrors.cs`, keeping the code `pages.out_of_date`, and map the extra fields in `PAGES/PagesErrorResults.cs`. Log each refusal with the site, the document kind and id, and the two member ids only (FR-024). Other `OutOfDate` uses stay unchanged.
  *Done as*: Both paths go through `DraftWrites.ConflictAsync`, which reads the stored draft again without tracking (the tracked one may be the loser) and logs ids only.
- [X] T018 [US1] In `CMS/Domain/Item.cs`, add `DraftRevision`, `DraftChangedBy`, `DraftChangedAtUtc` and `DraftChangeKind`, maintained by `ReplaceDraft` callers (`SaveDraft`, `CopyToDraft`) and by `Migrate` when the draft changed (CE-08), until T013 passes. Map the columns in `CMS/Infrastructure/CmsDbContext.cs` and generate `CMS/Infrastructure/Migrations/<ts>_AddItemDraftRevision.cs` with a backfill (`draft_revision = revision`, `draft_changed_by = updated_by`, `draft_changed_at_utc = updated_at_utc`, `draft_change_kind = 'member'`).
  *Done as*: The backfill and database defaults (`draft_revision` 1, `draft_change_kind` member, `draft_changed_at_utc` now()) are in `AddItemDraftRevision`: SQL seeding in `SitemapTests` and the smoke tests writes rows without these columns.
- [X] T019 [US1] In `CMS/Infrastructure/ItemWrites.cs` and `CMS/Features/Items/ItemResponses.cs`:
  - add an optional `DraftRevision` to `DraftRequest`;
  - draft saves compare it when present, else `Revision`;
  - `CmsErrors.ItemConflict` gains `changedBy`, `changeKind`, `changedAtUtc` and `currentDraftRevision` for draft saves (`CMS/CmsErrors.cs`);
  - item responses include `draftRevision`;
  - each draft conflict refusal is logged with ids and the change kind only (FR-024).
  *Done as*: A draft save based on the latest draft revision can still lose the row to a publish committed a moment earlier, because `revision` stays the concurrency token. `ItemWrites` retries such a save once when the draft revision is unchanged (CE-08). Refusals are logged with ids and the change kind only.

  Continue until T012 passes.
- [X] T020 [P] [US1] Create `ADMIN/editing/itemDiff.ts` (`changedFields(loaded, latest, fields)` returning field ids and labels) until T015 passes.
- [X] T021 [US1] Create `ADMIN/editing/ConflictDialog.vue` (`role="alertdialog"`) per admin-editing.md §3, without the copy action (US4 adds it). Wire it into `ADMIN/editor/EditorView.vue` in place of the conflict branch of `ADMIN/editor/ConflictBanner.vue`; the banner keeps the other reasons. Store `changedBy` and `changedAtUtc` from the 409 in `ADMIN/editor/state/autosave.ts`. Continue until T014 and T016 pass.
  *Done as*: `ConflictDialog.vue` and `conflict.ts` live in `ADMIN/editing/`. 011's banner still shows every other read-only reason, and the `conflict` reason after Keep viewing. Focus moves to the first action on the next tick, after the dialog is in the document.
- [X] T022 [US1] In `ADMIN/features/cms/items/ItemFormView.vue` and `ADMIN/api/items.ts`:
  - send `draftRevision`;
  - on `cms.item_conflict`, fetch the latest item and show `ConflictDialog` with the `itemDiff` field list;
  - Keep viewing keeps the entered values read-only.
  *Done as*: The fields and the slug sit in one `fieldset` that a conflict disables, so Keep viewing keeps the entries visible and read-only; Reload clears it.

  Extend `ADMINTESTS/features/cms/items/ItemFormView.spec.ts`.
- [X] T023 [US1] Add the US1 strings (dialog texts, system-change text, field list heading) to `ADMIN/i18n/en.json`, and update `specs/010-page-tree-components/contracts/` and `specs/015-cms-items/contracts/` with the new conflict fields and `draftRevision`.

**Checkpoint**: quickstart §1 passes, except the copy action.

---

## Phase 4: User Story 2 - Members see who else is editing before they start (Priority: P1)

**Goal**:
- opening a document creates a viewer session, and the first change takes the lock;
- others see the holder and get the document read-only;
- holders and viewers see each other;
- lists show markers;
- "Edit here instead" moves one's own lock;
- leaving releases the session.

**Independent Test**: quickstart §2. Two browsers open a page and both can edit; the first
to type takes the lock and the other turns read-only within 15 s; markers appear; leaving
frees it; same-member tabs move the lock; another site sees nothing.

### Tests for User Story 2 ⚠️

- [X] T024 [P] [US2] Create `ITESTS/Editor/EditingSessionEndpointsTests.cs` (ManualClock), covering:
  - open → viewer, and `others` lists the other sessions;
  - heartbeat returns presence, and `active` moves `last_activity_at` only for the holder;
  - claim with no holder → holder, and claiming again is idempotent;
  - claim with a live holder → `editing.lock_held` naming them;
  - claim with a gone holder (clock +2 min) succeeds;
  - move from the same user's session → the old session shows `lostLock.reason = moved`,
    and no takeover row is written;
  - move from another user's session → refused;
  - release → others see the document free;
  - heartbeat or claim on someone else's session id → 404;
  - `GET …/editing?kind=` lists live holders with `stale`;
  - a member without the edit capability gets `access: view-only` and refused claims;
  - a member with no role on the site gets 404 everywhere.
  *Done as*: 8 tests in `ITESTS/Editor/EditingSessionEndpointsTests.cs`, on an `EditingScenario` (`ITESTS/Editor/EditingApi.cs`) with a hand-moved clock. Releasing a session that is not the caller's, or is already gone, answers 404 `editing.session_gone` rather than 204, so the isolation suite can prove another site's session is out of reach (contract updated).
- [X] T025 [P] [US2] Create `ITESTS/Concurrency/LockRaceTests.cs` (FR-010, FR-025): 200 parallel claims on one document yield exactly one holder. The same holds with claims split across two app hosts built from `ITESTS/Infrastructure/QuickBuilderApp.cs` on one database. Repeat for takeover races once US3 lands (mark those cases for T047).
  *Done as*: 200 parallel claims (20 Designers × 10 tabs), and the same split across two app hosts on one database: one holder each time, and every instance names the same one. The takeover race is added in T047.
- [X] T026 [P] [US2] Create `ITESTS/Isolation/Probes/EditingIsolationProbe.cs`: site B's members cannot open, heartbeat, claim, move, release or list sessions of site A, and A's holders never appear in B's responses (FR-013). Register it so that `ITESTS/Isolation/IsolationCoverageTests.cs` stays green.
- [X] T027 [P] [US2] Create `ADMINTESTS/editing/useEditingSession.spec.ts` (fake timers, mocked API), covering:
  - open on load;
  - a heartbeat every 10 s and at once on `visibilitychange`;
  - `active` reported only after a change;
  - the first change triggers one claim and holds saving until it answers;
  - a refused claim reverts the change without an undo entry and sets `locked`;
  - "now free" leads to Edit, which reloads when the version changed and then claims;
  - release on router leave, on `pagehide` (keepalive fetch with the request header) and
    before sign-out;
  - `session_gone` reopens the session;
  - every save sends `X-QB-Editing-Session`.
- [X] T028 [P] [US2] Create `ADMINTESTS/editing/PresenceBanner.spec.ts` (with axe): the holder text with since, the "also viewing" names for the holder, the `locked-self` state with "Edit here instead", and the `view-only` text. Changes are announced in a polite live region.
- [X] T029 [P] [US2] Extend `ADMINTESTS/pageTreeView.spec.ts`, `ADMINTESTS/componentsView.spec.ts` and the item list spec under `ADMINTESTS/cms/`: `LockMarker` shows the holder and since, refreshes every 15 s while visible, and stops when hidden.

### Implementation for User Story 2

- [X] T030 [US2] Create `EDITORM/Features/EditingSessions/EditingResponses.cs` (the `Presence` and holder-list shapes of editing-api.yaml; names from `IUserDirectory`, `serverNow` from `TimeProvider`) and `EDITORM/Infrastructure/EditingPresence.cs` (one query for a document's live sessions, and the projection to `Presence` for a given session).
  *Done as*: `EditingResponses.cs` holds the shapes. The reads and the atomic writes are in `EDITORM/Infrastructure/EditingSessionStore.cs` instead of a separate `EditingPresence.cs`: heartbeats, claims and transfers are single `ExecuteUpdate` statements whose `WHERE` states the rule, so a tracked entity never writes back a lock that a takeover just moved.
- [X] T031 [US2] Create `EDITORM/Features/EditingSessions/OpenSession.cs` (`POST editing-sessions`; view capability; `collectionId` required for items) and `Heartbeat.cs` (own session only; `Seen(active)`; when `MayEditAsync` is false and the session holds the lock, release it and answer `access: view-only`; gone → `editing.session_gone`).
- [X] T032 [US2] Create `EDITORM/Features/EditingSessions/Claim.cs`. In one transaction:
  1. `ExecuteDelete` the document's gone holder row;
  2. set `holds_lock` on the caller's row;
  3. map a unique violation on `ux_editing_sessions_holder` to `editing.lock_held` naming the live holder.

  The claim is idempotent for the holder and requires the edit capability.
- [X] T033 [US2] Create `EDITORM/Features/EditingSessions/MoveHere.cs`. In one transaction, release the named holder session, which must belong to the caller, recording `lost_lock_reason = moved`; then claim. No takeover row is written.
- [X] T034 [US2] Create `EDITORM/Features/EditingSessions/Release.cs` (`DELETE`; own session; 204 even when gone) and `ListHolders.cs` (`GET …/editing?kind=&collectionId=`; view capability; live holders only). Register all slices in `EDITORM/EditorModule.cs` and `ITESTS/Infrastructure/EndpointCatalog.cs`, then run T024 to T026 until they pass.
  *Done as*: Every editing action declares `[RequireCapability(PageContentCapabilities.View)]` (see Conventions). `AuthorizationDeclarationTests` and the architecture tests pass.
- [X] T035 [P] [US2] Create `ADMIN/api/editing.ts` (typed calls for every editing operation; release uses `fetch(…, { keepalive: true })` with the `X-QuickBuilder-Request` header), and add the `X-QB-Editing-Session` header option to draft saves in `ADMIN/api/request.ts`, `ADMIN/api/pages.ts` and `ADMIN/api/items.ts`.
- [X] T036 [US2] Create `ADMIN/editing/useEditingSession.ts` per admin-editing.md §1 and §2, covering:
  - the session lifecycle;
  - heartbeat timer and visibility handling;
  - the activity flag;
  - the claim gate;
  - the read-only reasons `locked`, `locked-self`, `moved`, `view-only` and `free`;
  - release on router leave and on `pagehide`.
  *Done as*: Opening the session is not awaited by the editor (SC-007); the first claim waits for it, and a session that opens after its editor moved on is released (a generation counter).

  Continue until T027 passes.
- [X] T037 [US2] Integrate `useEditingSession` with the editor:
  - In `ADMIN/editor/state/editorStore.ts`, add the new `ReadOnlyReason` values and a hook that makes the first `apply` call claim the lock.
  - In `ADMIN/editor/state/autosave.ts`, hold saving until the session holds the lock, and revert pending changes without history when the claim is refused.
  - In `ADMIN/editor/EditorView.vue`, open the session for pages and components.
  *Done as*: The first change claims at once through `store.onChange`, and autosave asks again through a new `beforeSave` option; a refused claim reverts to the saved document and clears history. A save refused as `editing.lock_held` stops autosave in a new `locked` state.
- [X] T038 [US2] Integrate `useEditingSession` with `ADMIN/features/cms/items/ItemFormView.vue`: claim on the first input, make the form read-only while locked, and send the session header on save.
- [X] T039 [US2] Create `ADMIN/editing/PresenceBanner.vue` (texts of admin-editing.md §4; "Edit here instead" calls `move`; one polite live region) until T028 passes, and show it in `EditorView.vue` and `ItemFormView.vue`.
- [X] T040 [P] [US2] Create `ADMIN/editing/usePresenceList.ts` (15 s refresh while the document is visible) and `ADMIN/editing/LockMarker.vue`. Show them in `ADMIN/features/pages/PageTreeView.vue`, `ADMIN/features/pages/ComponentsView.vue` and `ADMIN/features/cms/items/ItemListView.vue`, until T029 passes.
- [X] T041 [US2] In `ADMIN/app/authStore.ts`, release every open editing session before sign-out (best effort, never blocking sign-out).
  *Done as*: In `ADMIN/App.vue`, where sign-out happens, through `editingApi.releaseAll()`.
- [X] T042 [US2] Add the US2 strings to `ADMIN/i18n/en.json`.

**Checkpoint**: quickstart §2 passes. US1 and US2 together are the first demo of locks.

---

## Phase 5: User Story 3 - A lock left by an idle or vanished member does not block work (Priority: P2)

**Goal**:
- stale locks can be taken over, and the takeover is recorded;
- the former holder goes read-only and their late saves are refused (fencing);
- expired rows are cleaned up;
- deleted documents and lost permissions end sessions;
- reconnecting after expiry is handled.

**Independent Test**: quickstart §3. A stale lock is taken over and recorded; A turns
read-only and A's queued save is refused; closing, navigating and signing out free the lock
within 15 s; a killed browser's lock expires within 2 min 15 s; lost permission and deletion
end the session.

### Tests for User Story 3 ⚠️

- [ ] T043 [P] [US3] Extend `ITESTS/Editor/EditingSessionEndpointsTests.cs` with takeover:
  - an active lock → `editing.not_stale`;
  - a stale lock (clock +30 min) → success, a takeover row with `stale_for_seconds`, and
    the former holder's heartbeat shows `lostLock {reason: takeover, by, at}`;
  - a takeover naming a holder that changed → `editing.not_stale`;
  - taking over one's own session is refused (use move).

  Also cover: a holder who reconnects after expiry gets the lock back only when it is free;
  otherwise they see the new holder (FR-019).
- [ ] T044 [P] [US3] Create `ITESTS/Concurrency/DraftFencingTests.cs` (CE-07):
  - a page, component or item draft save with the header of a session that does not hold a
    live lock, while another session does, gets `409 editing.lock_held` and stores nothing;
  - with the holder's header, or with no header, saves follow the version check only;
  - with no live lock at all (holder gone, or sessions table emptied), saves still work and
    stay version-checked (FR-026).
- [ ] T045 [P] [US3] Create `ITESTS/Editor/EditingCleanupTests.cs`:
  - `PageDeleted`, `ComponentDeleted`, `ItemDeleted` and `CollectionDeleted` delete the
    matching sessions, run twice for idempotency, and the next heartbeat gets
    `editing.session_gone`;
  - the cleanup service deletes rows gone for 2 minutes and takeovers older than 90 days, on
    every site, while live rows and recent takeovers on every site are untouched. This is the
    dedicated test that Principle V requires for the filter bypass in T051;
  - removing a holder's role makes the next heartbeat release the lock with
    `access: view-only`.
- [ ] T046 [P] [US3] Extend `ADMINTESTS/editing/useEditingSession.spec.ts` and `PresenceBanner.spec.ts`:
  - `locked-stale` shows "inactive for n min" and Take over, which asks for confirmation;
  - `taken-over` after `lostLock` or after a save refused with `editing.lock_held`;
  - in that state no save is ever sent, and Reload is offered;
  - a reconnect after expiry re-claims only when the lock is free and the version unchanged.

### Implementation for User Story 3

- [ ] T047 [US3] Create `EDITORM/Features/EditingSessions/TakeOver.cs` (CE-05). In one transaction:
  1. release the named holder only if it is still the holder and stale by `EditingRules.CanTakeOver`, setting `lost_lock_*`;
  2. claim for the caller;
  3. insert `EditingTakeover`;
  4. log the takeover with ids and the stale duration only.

  Register it and run T043 until it passes. Add the takeover race cases to `ITESTS/Concurrency/LockRaceTests.cs`.
- [ ] T048 [US3] Create `EDITORM/Infrastructure/EditingLocks.cs` implementing `IEditingLocks.CheckWriteAsync` (one indexed `AsNoTracking` read of the live holder) and register it in `EDITORM/EditorModule.cs`.
- [ ] T049 [US3] Fence page and component draft saves: read `EditingHeaders.Session` in `PAGES/Features/SavePageDraft/SavePageDraft.cs` and `PAGES/Features/SaveComponentDraft/SaveComponentDraft.cs`, pass it into `PAGES/Infrastructure/DraftWrites.cs`, and call `IEditingLocks.CheckWriteAsync` after validation and before the version comparison. On `HeldByOther`, return `PagesErrors.LockHeld` (code `editing.lock_held`, holder name via `IUserDirectory`) and log it with ids only.
- [ ] T050 [US3] Fence item draft saves the same way in `CMS/Features/Items/SaveItemDraft/SaveItemDraft.cs` and `CMS/Infrastructure/ItemWrites.cs`, adding `CmsErrors.LockHeld` in `CMS/CmsErrors.cs`. Run T044 until it passes.
- [ ] T051 [US3] Create `EDITORM/Features/ReleaseDeletedDocuments/ReleaseDeletedDocumentsHandler.cs`, handling `PageDeleted`, `ComponentDeleted`, `ItemDeleted` and `CollectionDeleted` idempotently with `ExecuteDelete`, and `EDITORM/Infrastructure/EditingSessionCleanupService.cs` (a hosted service running every minute: delete gone rows, delete takeovers older than 90 days). The handlers act on the event's site through the normal site filter. The cleanup service runs outside any site and bypasses the global filter (`IgnoreQueryFilters`) only for these two deletes. Each such place carries a code comment citing Principle V and research CE-03/CE-12. Register both, and run T045 until it passes.
- [ ] T052 [US3] In `ADMIN/editing/useEditingSession.ts` and `ADMIN/editing/PresenceBanner.vue`, add the `locked-stale` and `taken-over` states, Take over with confirmation, and the reconnect handling. In `ADMIN/editor/state/autosave.ts` and `ItemFormView.vue`, treat `editing.lock_held` on save as `taken-over`. Continue until T046 passes.
- [ ] T053 [US3] Add the US3 strings to `ADMIN/i18n/en.json`.

**Checkpoint**: quickstart §3 passes.

---

## Phase 6: User Story 4 - A member keeps their version as a copy instead of losing it (Priority: P2)

**Goal**:
- "Save my version as a copy" creates an unpublished page (after the original), component
  or item from the member's version;
- copies record their origin;
- the action is offered and allowed only to members who may create that kind;
- dropped item fields are reported.

**Independent Test**: quickstart §4. Copies are created from conflicts and takeovers with
the member's exact version, unpublished, with a unique slug and a shown origin. An Editor
on a page is not offered the action, and a crafted request gets 403.

### Tests for User Story 4 ⚠️

- [ ] T054 [P] [US4] Create `ITESTS/Pages/CopyEndpointsTests.cs`:
  - a page copy is placed right after the original under the same parent, with the given
    title, a unique slug (`-copy`, `-copy-2`), the submitted document as its draft, usage
    rows written, no publication, and `copyOrigin` set;
  - a component copy has its name, is used by no page and has `copyOrigin`;
  - an invalid document gets `pages.invalid_document` and creates nothing;
  - without `pages.structure.manage` (or `pages.components.manage`) the request gets
    `403 pages.copy_forbidden`;
  - publishing the original never publishes the copy;
  - another site's page id gets 404.
- [ ] T055 [P] [US4] Create `ITESTS/Cms/Items/CopyItemTests.cs`: the copy is unpublished in the same collection, with the submitted values, a unique slug, `copyOrigin` and a `created_as_copy` history entry naming the source. Values of removed fields are dropped and listed in `droppedFields`; values that fail field rules are kept, with the attention report. A request without `cms.items.edit` gets `403 cms.copy_forbidden`.
- [ ] T056 [P] [US4] Create `UTESTS/Pages/CopyNamingTests.cs`: copy slugs and titles across collisions, maximum lengths and trimming.
- [ ] T057 [P] [US4] Extend `ADMINTESTS/editing/ConflictDialog.spec.ts`:
  - Save as a copy appears only with the create permission;
  - otherwise the "copy blocks or ask a Designer" text is shown;
  - after saving, a link to the copy is shown and Reload is offered;
  - the action is also available from the `taken-over` view;
  - dropped item fields are listed.

### Implementation for User Story 4

- [ ] T058 [US4] Create `PAGES/Domain/CopyOrigin.cs` (owned value; all four fields or none) and add it to `PAGES/Domain/Page.cs` and `PAGES/Domain/Component.cs`. Add an insert-after-sibling operation to `PAGES/Domain/PageTree.cs` if it is missing. Map the columns in `PAGES/Infrastructure/PagesDbContext.cs`, and generate `PAGES/Infrastructure/Migrations/<ts>_AddCopyOrigin.cs` with the check constraint.
- [ ] T059 [US4] Create `PAGES/Features/CopyPage/CopyPage.cs` (`POST …/pages/{pageId}/copies`, `pages.structure.manage`). In one transaction: create the page after the original with a unique slug, then save the document as its draft through `DraftWrites` (validation, usage rows, media and menu references). Expose `copyOrigin` in `PAGES/Features/PageResponses.cs`.
- [ ] T060 [US4] Create `PAGES/Features/CopyComponent/CopyComponent.cs` (`POST …/components/{componentId}/copies`, `pages.components.manage`), with the same rules and `pages.duplicate_component_name` handling. Register both slices in `PAGES/PagesModule.cs` and `EndpointCatalog.cs`, and run T054 and T056 until they pass.
- [ ] T061 [US4] Add a `CopyOrigin` owned value to `CMS/Domain/Item.cs` (all four fields or none), map it in `CMS/Infrastructure/CmsDbContext.cs`, and generate `CMS/Infrastructure/Migrations/<ts>_AddItemCopyOrigin.cs` with the check constraint. Add the `created_as_copy` kind to `CMS/Domain/ItemHistoryEntry.cs`.
- [ ] T062 [US4] Create `CMS/Features/Items/CopyItem/CopyItem.cs` (`POST …/items/{itemId}/copies`, `cms.items.edit`): drop unknown field ids and report them, make the slug unique with 015's rules, create the item with `copyOrigin`, and record the history entry. Expose `copyOrigin` in item responses. Run T055 until it passes.
- [ ] T063 [US4] In `ADMIN/editing/ConflictDialog.vue`, add Save my version as a copy, using the permissions the editor and item form already load; call the copy endpoints from `ADMIN/api/pages.ts` and `ADMIN/api/items.ts`; show the link and dropped fields. Offer it from the `taken-over` state in `PresenceBanner.vue`. Show the copy origin note in `EditorView.vue` and `ItemFormView.vue` when `copyOrigin` is set. Continue until T057 passes.
- [ ] T064 [US4] In `ADMIN/editor/RecoveryDialog.vue` and `ADMIN/editor/state/recovery.ts` (CE-18), offer Restore only when the version is unchanged and a claim succeeds; otherwise open the copy read-only with Save as a copy where allowed. Extend the recovery spec under `ADMINTESTS/editor/`.
- [ ] T065 [US4] Add the US4 strings (including the localised "(copy)" suffix) to `ADMIN/i18n/en.json`, and add the copy endpoints and `copyOrigin` to `specs/010-page-tree-components/contracts/` and `specs/015-cms-items/contracts/`.

**Checkpoint**: quickstart §4 and §5 pass.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T066 [P] Publish and delete warnings (CE-17): in `ADMIN/features/publishing/PublishMenu.vue`, `ADMIN/features/cms/items/ItemPublishBar.vue`, `ADMIN/features/cms/items/DeleteItemDialog.vue` and the page and component delete flows in `ADMIN/features/pages/PageTreeView.vue` and `ComponentsView.vue`, warn when someone else holds the lock. In `ADMIN/features/cms/items/ItemHistoryPanel.vue`, warn before copying a published version into a draft someone else holds (spec edge case "Rolling back or copying a published version into a locked draft"). Add specs beside the existing ones.
- [ ] T067 [P] Create `ITESTS/Editor/UpgradeFrom019Tests.cs`, following `ITESTS/Editor/UpgradeFrom010Tests.cs`: a database at 019's migrations upgrades, every item has `draft_revision = revision`, and old-style draft saves (no `draftRevision`, no session header) work for pages, components and items.
- [ ] T068 [P] Create `ITESTS/Observability/EditingContentNotLoggedTests.cs`: titles, names, slugs and document content never appear in logs from claims, takeovers, fencing refusals, conflicts or copies, and `editing_takeovers` has no text column besides kind. Also check that every takeover, `editing.lock_held` refusal and draft conflict refusal (`pages.out_of_date`, `cms.item_conflict`) writes exactly one log entry with the expected ids (FR-024).
- [ ] T069 [P] Run `ATESTS/AuthorizationDeclarationTests.cs` and `ATESTS/ModuleBoundaryTests.cs`, and confirm that every new action declares its capability and only the two new Contracts edges exist.
- [ ] T070 [P] Update the docs:
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (Editor consumers);
  - `specs/002-identity-setup/contracts/authorization.md` (the editing operations, per-kind capabilities, copies);
  - `docs/backlog.md` (the plan's Deferred list and the #20 obligations of module-surface §5);
  - `docs/configuration.md` (no new keys; state it);
  - `docs/operations.md` (the heartbeat request rate; behaviour behind a rate-limiting proxy);
  - `docs/releases/unreleased/` (the plan's upgrade notes 1–3).
- [ ] T071 [P] Run `dotnet test` with coverage: `EditingRules`, `Claim`, `TakeOver`, `MoveHere`, `EditingLocks`, `DraftWrites` and `ItemWrites` keep at least 80% line coverage. Run `npm run lint`, `npm run type-check` and `npm test`, `dotnet format --verify-no-changes`, and the analyzer gates. Record the numbers here.
- [ ] T072 Run the whole of `quickstart.md` by hand on the reference compose setup:
  - two browsers, with a stopwatch for SC-003 and SC-004;
  - §6 with two app containers;
  - §7 with a screen reader (exception E-5);
  - §9: the SC-007 open-time comparison against `main`;
  - §10: the SC-005 usability check with at least five team members.

  Record the results in `specs/023-concurrency/quickstart-results.md`.
- [ ] T073 Open the PR closing #23, with the plan's operator impact, upgrade notes and the FR-015 spec alignment in the description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup. Blocks every story.
- **US1 (Phase 3)**: depends on Foundational only for its error plumbing; its server work
  (T017–T019) touches no editing session code. The MVP.
- **US2 (Phase 4)**: depends on Foundational. Its admin integration (T037, T038) uses US1's
  `ConflictDialog` wiring in `EditorView.vue` and `ItemFormView.vue`, so it follows T021 and
  T022 in those files.
- **US3 (Phase 5)**: depends on US2 (sessions, claim, presence, the composable).
- **US4 (Phase 6)**: depends on US1 (`ConflictDialog`). Its takeover entry point (part of
  T063) depends on US3's `taken-over` state. The copy endpoints (T058–T062) depend only on
  Foundational.
- **Polish (Phase 7)**: after all stories.

### Within Each User Story

- Tests first, and they must fail.
- Then domain, database, slices and admin.
- A story is done when its quickstart section passes.

### Parallel Opportunities

- **Foundational**: T003, T004, T005 and T008 are parallel. T006 follows T005. T007 follows
  T006.
- **Stories**:
  - US1's server tasks (T017–T019) and US2's server tasks (T030–T034) run in parallel, by
    different people, in different modules;
  - US4's copy endpoints (T058–T062) can start right after Foundational, beside US1 and
    US2.
- **Within a story**: every test task marked [P] is parallel, and the admin tasks marked [P]
  run beside the server tasks.

---

## Parallel Example: User Story 1

```text
Task: "T010 OverlappingDraftWritesTests in ITESTS/Concurrency/"
Task: "T012 ItemPublishDoesNotConflictTests in ITESTS/Cms/Items/"
Task: "T013 ItemDraftRevisionTests in UTESTS/Cms/"
Task: "T014 ConflictDialog.spec.ts in ADMINTESTS/editing/"
Task: "T015 itemDiff.spec.ts in ADMINTESTS/editing/"
```

## Parallel Example: User Story 2

```text
Task: "T024 EditingSessionEndpointsTests in ITESTS/Editor/"
Task: "T025 LockRaceTests in ITESTS/Concurrency/"
Task: "T026 EditingIsolationProbe in ITESTS/Isolation/Probes/"
Task: "T027 useEditingSession.spec.ts in ADMINTESTS/editing/"
Task: "T028 PresenceBanner.spec.ts in ADMINTESTS/editing/"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. Conflicts are explained, item publishing no longer
   causes false conflicts, and nothing is ever overwritten.

US1 alone already fixes the false conflicts after an item publish and gives members a clear
dialog. Presence (US2) is what the issue is mostly about, so US1 + US2 is the first demo
worth showing.

### Incremental Delivery

1. Setup and Foundational.
2. US1: version check details and the conflict dialog. **Demo**: a conflict, explained.
3. US2: presence and the lock. **Demo**: two browsers.
4. US3 (takeover, fencing, expiry, cleanup) beside US4 (copies).
5. Polish, the manual quickstart run (T072) and the PR (T073).

One PR, as with 010–024. There is no feature flag mechanism. If the PR must split, follow
this order: (1) US1; (2) US2 and US3, together, because a lock that never expires or can't
be taken over must not ship alone; (3) US4.

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US4.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #23.
- **Avoid**:
  - any lock state in the cache (CE-02);
  - refusing a save only because a session is missing (FR-026: version check only);
  - device-clock times in the admin;
  - a push channel;
  - configuration keys;
  - titles, names or content in logs or takeover rows;
  - offering an overwrite anywhere.
