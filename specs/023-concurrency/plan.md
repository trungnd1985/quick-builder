# Implementation Plan: Concurrent Editing Safety

**Branch**: `023-concurrency` | **Date**: 2026-10-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/023-concurrency/spec.md`

**Issue**: #23.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none.

## Summary

Members who open the same page, component or collection item are told about each other.
The first one to change it holds a soft lock, and everyone else sees it read-only with the
holder's name and since when. A lock left idle for 30 minutes can be taken over. Locks go
away when their holder leaves, and expire within 2 minutes when the holder vanishes. Every
draft write is still checked against the version the member loaded. A refused save opens a
conflict dialog that names who changed the document and when, and offers reload, keep
viewing, or save the member's version as a new unpublished copy.

The approach, detailed in [research.md](./research.md):
- **Owner** (CE-01, CE-02):
  - editing sessions are a new aggregate in the **Editor** module;
  - they are stored as rows in `editor.editing_sessions`, where a partial unique index allows
    one holder per document, so the rule holds on one instance and on many with no
    configuration.
- **Liveness** (CE-03):
  - a heartbeat every 10 s returns the presence snapshot, so one request keeps the session
    alive and the screen current;
  - expiry (2 min) and staleness (30 min) are computed with the server's clock on every
    read;
  - a cleanup service deletes lapsed rows. Correctness does not depend on it.
- **Lock on first change** (CE-04, clarification Q3): opening makes a viewer. The first
  change claims the lock. Saving waits for the claim, and a refused claim reverts the change.
- **Takeover and move** (CE-05, CE-06):
  - a takeover is allowed only when the lock is stale and the request names the holder the
    member saw, and it is recorded in `editing_takeovers`;
  - "Edit here instead" moves the member's own lock and is not recorded.
- **Fencing** (CE-07): draft saves carry `X-QB-Editing-Session`. Pages and Cms refuse a save
  from a session that is not the live holder (`editing.lock_held`). Saves with no header or
  no live lock rely on the version alone (FR-026).
- **Version check** (CE-08, CE-09):
  - items get a draft-only `draft_revision`, so a publish by someone else no longer breaks
    the holder's next save;
  - conflict refusals gain `changedBy`, `changedAtUtc` and, for items, `changeKind`
    (`member` or `schema_change`).
- **Copies** (CE-11, clarification Q2): three copy endpoints create a new unpublished page
  (right after the original), component or item from the member's version. Each requires
  the create capability and records a `CopyOrigin`.
- **Admin** (CE-10, CE-13 to CE-19):
  - `useEditingSession` drives every open editor;
  - new `PresenceBanner`, `ConflictDialog` and `LockMarker` components;
  - lock-aware read-only states;
  - release on route leave, on `pagehide` (`fetch` keepalive) and on sign-out;
  - publish and delete warnings;
  - crash recovery that respects locks.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 + Npgsql, the in-house dispatcher and outbox,
  hosted services.
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n.
- **Nothing new**: no SignalR, no WebSocket or SSE library (CE-03).

**Storage**: PostgreSQL 16+.
- **Editor** migration `AddEditingSessions`: **+** `editing_sessions`, **+**
  `editing_takeovers`.
- **Pages** migration `AddCopyOrigin`: **~** `pages`, **~** `components`, each with 4
  nullable columns.
- **Cms** migrations:
  - `AddItemDraftRevision`: **~** `items`, with `draft_revision`, `draft_changed_by`,
    `draft_changed_at_utc` and `draft_change_kind`, backfilled from `revision`, `updated_by`
    and `updated_at_utc`;
  - `AddItemCopyOrigin`: **~** `items`, with 4 nullable copy-origin columns.
- **No document rewrite.**

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. The suites are listed in research CE-20. The main ones:
- `EditingRulesTests` (fake clock);
- `LockRaceTests`, single-instance and two-instance (FR-010, FR-025);
- `OverlappingDraftWritesTests` (SC-001);
- `DraftFencingTests`, `ItemPublishDoesNotConflictTests` and `CopyEndpointsTests`;
- `EditingSessionEndpointsTests`, `EditingCleanupTests` and `EditingIsolationProbe`;
- `UpgradeFrom019Tests`;
- in the admin: `useEditingSession.spec.ts` (fake timers), `ConflictDialog.spec.ts`,
  `PresenceBanner.spec.ts` and `itemDiff.spec.ts`, with axe.

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend and the Vue admin SPA. The
public site is untouched.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Presence updates** (SC-003): shown within 15 s, using a 10 s heartbeat.
- **Release** (SC-004): within 15 s after leaving or closing; expiry within 2 min 15 s.
- **Load**: one heartbeat per open editor every 10 s, each one indexed `UPDATE` plus one
  indexed read. 100 open editors make about 10 writes per second.
- **No added latency on open** (SC-007): the session opens in parallel with the draft load;
  list markers are one extra request per list.
- **Typing never waits** for a claim (CE-04).

**Constraints**:
- **Soft lock**: the version check alone still protects every draft (FR-026). The lock and
  fencing reduce conflicts and close the takeover race, but never replace the check.
- **Server time** for every "since" and "inactive" value.
- **Logs**: ids and counts only. No titles, names or content in logs or takeover rows.
- **Offline**: no outbound requests.

**Scale/Scope**:
- **Code**: 0 new projects.
- **Migrations**: 4. Tables: 2 new, 3 changed.
- **Endpoints**: 7 new editing operations (open, heartbeat, claim, takeover, move, release,
  list holders) and 3 new copy operations. 3 draft saves change: a header, a refusal and
  conflict fields.
- **Contracts interfaces**: 1 new (`IEditingLocks`); 1 constants class moves
  (`ItemCapabilities`).
- **Events**: 0 new; 4 new consumers in Editor.
- **Capabilities**: 0 new. **Configuration keys**: 0.
- **Admin**: about 5 components and 3 TypeScript modules.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module**: sessions live in Editor (CE-01). <br>• **Slices**: Editor `Features/EditingSessions/` (open, heartbeat, claim, takeover, move, release, list holders) and `Features/ReleaseDeletedDocuments/` (event handlers); Pages `Features/CopyPage/` and `Features/CopyComponent/`; Cms `Features/Items/CopyItem/`. Controllers are thin and use in-house handlers. <br>• **Domain model**: an `EditingSession` aggregate with pure `EditingRules` (claim, stale, takeover, move), unit-tested; `Item` gains draft-revision invariants (Cms requires a rich model). <br>• **Cross-module**: two new Contracts-only edges, Editor → Cms.Contracts and Cms → Editor.Contracts (module-surface.md §1). Deletions arrive through the outbox. The fencing read happens before, not inside, the owning module's transaction (CE-07). <br>• **Enforcement**: `ModuleBoundaryTests` lists the two edges; `AuthorizationDeclarationTests` cover the new endpoints. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys (CE-05). Four forward-only, automatic, re-runnable migrations. Health checks are unchanged. |
| III | Upgrades Never Lose Data | **Complies.** <br>• `UpgradeFrom019Tests`: the backfill keeps every item saveable, and old admin requests still work (no `draftRevision`, no session header). <br>• **Site export (#20)**: copies export as ordinary documents; sessions and takeovers are not exported (module-surface §5). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. Locks do not use the cache, so the single-instance default and multi-instance setups behave the same (CE-02). Built-in hosted service for cleanup. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Site ownership**: both new tables carry `site_id` with the global filter. <br>• **Authorisation**: every editing operation checks the kind's capability on that site (CE-16), and sessions act only for their own user. <br>• **Isolation**: `EditingIsolationProbe` joins the isolation suite. <br>• **Raw SQL**: none; `ExecuteUpdate` and `ExecuteDelete` are EF Core. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** No change to the page schema, collection schema or binding format. Copies are validated by the existing validators. |
| VII | Draft and Published Are Separate Worlds | **Complies.** Locks and fencing apply to drafts only. Copies are unpublished drafts. Publishing is unchanged, and it no longer conflicts with item draft saves (CE-08). |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Not affected.** Nothing reaches the public path. A copy enters the dependency graph only when published, as any document does. |
| IX | Secure by Default | **Complies.** <br>• **Server-side checks**: claim, takeover, move and copy are enforced on the server; the admin only hides actions. <br>• **Personal data**: presence shows display names only, never email (FR-013). <br>• **Logs**: ids only (FR-024). <br>• **Requests**: the keepalive release carries the admin request header like any request (CE-13). <br>• **Abuse**: sessions are bounded by expiry, and heartbeats need a signed-in user with view rights. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests for every new and changed operation. <br>• **Race and multi-instance**: `LockRaceTests`. <br>• **Domain**: `EditingRulesTests` with a fake clock. <br>• **Coverage**: 80% line coverage on `EditingRules`, the claim, takeover and move handlers, and the fenced draft writes, which sit on the critical save path. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies**. <br>• **Not built**: no push channel, no settings, no request-to-edit (clarification Q5), no takeover screen, no merge. <br>• **Reuse**: 011's read-only and clipboard path, 015's slug rules, and the existing outbox events. <br>• **Delivery**: one PR. Pages and items can ship behind the existing admin build; no feature flag is needed because the version check is unchanged for old clients. |

**Technology Constraints**:
- MVC controllers only; EF Core only, with no raw SQL.
- No Node process at run time.
- No new admin libraries.

**Development Workflow**:
- Issue #23 → `specs/023-concurrency` → branch `023-concurrency` → one PR that closes #23.
- Clarify has run: 2 questions at specify and 3 in clarify, all answered. Analyze follows
  tasks.
- **Spec alignment** (CE-13): FR-015 is narrowed so that a revoked session's lock lapses by
  expiry, within 2 minutes, instead of "at once". This is recorded in spec.md in the same
  change and should be confirmed in `/speckit-analyze`.
- **Contract and registry changes in the same PR**:
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (Editor consumes
    `PageDeleted`, `ComponentDeleted`, `ItemDeleted` and `CollectionDeleted`);
  - `specs/002-identity-setup/contracts/authorization.md` (the editing operations and the
    capability they check per kind; the copy operations);
  - `specs/010-page-tree-components/contracts/` and `specs/015-cms-items/contracts/` (the
    draft save header, the new refusal, the conflict fields, `draftRevision`, `copyOrigin`
    and the copy endpoints);
  - `docs/backlog.md` (#20 obligations: copies export, sessions do not).
  - `docs/contracts/` is not touched: no schema changes.

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 4, additive and automatic: Editor `AddEditingSessions`, Pages
  `AddCopyOrigin`, Cms `AddItemDraftRevision` (with backfill) and `AddItemCopyOrigin`.
- **New dependencies**: none.
- **Upgrade notes** (`docs/releases/unreleased/`):
  1. The editor and the item form show who else has a document open. The first person to
     change it holds it, and others see it read-only until it is free or idle for 30
     minutes, when they can take it over.
  2. Admin tabs left open from before the upgrade keep working with the old protection
     (refused out-of-date saves). Reload them to get presence and locks.
  3. Each open editor sends a small request every 10 seconds. A reverse proxy that limits
     the admin API's request rate must allow it.

**Post-Phase-1 re-check**:
- `editing-api.yaml` declares a capability on every operation;
- `module-surface.md` adds only Contracts edges;
- both new tables have `site_id` and the filter;
- no event carries content;
- no raw SQL;
- old clients (no header, no `draftRevision`) keep today's behaviour;
- the public path is untouched.

**Gate: PASS** (no new exception).

## Project Structure

### Documentation (this feature)

```text
specs/023-concurrency/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 5 questions)
├── research.md                # Phase 0: decisions CE-01 … CE-21
├── data-model.md              # Phase 1: tables, states, rules, errors
├── quickstart.md              # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── editing-api.yaml       # HTTP: editing sessions, holders, copies; changed draft saves
│   ├── admin-editing.md       # Admin behaviour: session lifecycle, read-only reasons, dialogs
│   └── module-surface.md      # IEditingLocks, ItemCapabilities, edges, events, export
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
src/Modules/
├── Editor/
│   ├── QuickBuilder.Editor.Contracts/
│   │   + IEditingLocks.cs  EditingDocumentKinds.cs  EditingHeaders.cs
│   └── QuickBuilder.Editor/
│       + Domain/EditingSession.cs  EditingTakeover.cs  EditingRules.cs
│       + Features/EditingSessions/OpenSession.cs  Heartbeat.cs  Claim.cs  TakeOver.cs
│       +                          MoveHere.cs  Release.cs  ListHolders.cs  EditingResponses.cs
│       + Features/ReleaseDeletedDocuments/ReleaseDeletedDocumentsHandler.cs
│       + Infrastructure/EditingLocks.cs  EditingAccess.cs  EditingSessionCleanupService.cs
│       ~ Infrastructure/EditorDbContext.cs
│       + Infrastructure/Migrations/<ts>_AddEditingSessions.cs
│       ~ EditorErrors.cs  EditorErrorResults.cs  EditorModule.cs  QuickBuilder.Editor.csproj (+ Cms.Contracts)
├── Pages/QuickBuilder.Pages/
│   ~ Domain/Page.cs  Component.cs  PageTree.cs        # CopyOrigin; insert after a sibling
│   + Domain/CopyOrigin.cs
│   ~ Infrastructure/DraftWrites.cs                    # fencing; conflict details
│   + Features/CopyPage/CopyPage.cs  Features/CopyComponent/CopyComponent.cs
│   ~ Features/PageResponses.cs  PagesErrors.cs  PagesErrorResults.cs
│   ~ Features/SavePageDraft/  SaveComponentDraft/     # read the session header
│   ~ Infrastructure/PagesDbContext.cs  + Infrastructure/Migrations/<ts>_AddCopyOrigin.cs
└── Cms/
    ├── QuickBuilder.Cms.Contracts/ + ItemCapabilities.cs
    └── QuickBuilder.Cms/
        ~ Domain/Item.cs                               # DraftRevision, DraftChange*, CopyOrigin
        ~ Domain/ItemHistoryEntry.cs                   # created_as_copy
        ~ Infrastructure/ItemWrites.cs                 # draft revision check; fencing; conflict details
        + Features/Items/CopyItem/CopyItem.cs
        ~ Features/Items/SaveItemDraft/  ItemResponses.cs
        ~ CmsCapabilities.cs  CmsErrors.cs  QuickBuilder.Cms.csproj (+ Editor.Contracts)
        ~ Infrastructure/CmsDbContext.cs  + Infrastructure/Migrations/<ts>_AddItemDraftRevision.cs  <ts>_AddItemCopyOrigin.cs

src/admin/src/
+ api/editing.ts
+ editing/useEditingSession.ts  usePresenceList.ts  itemDiff.ts
+ editing/PresenceBanner.vue  ConflictDialog.vue  LockMarker.vue
~ api/request.ts  api/pages.ts  api/items.ts           # session header; copy calls; new fields
~ editor/EditorView.vue  editor/ConflictBanner.vue  editor/RecoveryDialog.vue
~ editor/state/editorStore.ts  autosave.ts  recovery.ts  # read-only reasons; claim gate; revert
~ features/cms/items/ItemFormView.vue  ItemListView.vue  DeleteItemDialog.vue  ItemPublishBar.vue
~ features/pages/PageTreeView.vue  ComponentsView.vue
~ features/publishing/PublishMenu.vue
~ app/authStore.ts                                     # release sessions before sign-out
~ i18n/en.json
src/admin/tests/
+ editing/useEditingSession.spec.ts  ConflictDialog.spec.ts  PresenceBanner.spec.ts  itemDiff.spec.ts
~ editor/autosave.spec.ts  features/cms/items/ItemFormView.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/ModuleBoundaryTests.cs  AuthorizationDeclarationTests.cs
+ QuickBuilder.UnitTests/Editor/EditingRulesTests.cs
+ QuickBuilder.UnitTests/Cms/ItemDraftRevisionTests.cs  QuickBuilder.UnitTests/Pages/CopyNamingTests.cs
~ QuickBuilder.IntegrationTests/
│   + Editor/EditingSessionEndpointsTests.cs  EditingCleanupTests.cs
│   + Concurrency/LockRaceTests.cs  OverlappingDraftWritesTests.cs  DraftFencingTests.cs
│   + Pages/CopyEndpointsTests.cs  Cms/Items/ItemPublishDoesNotConflictTests.cs  Cms/Items/CopyItemTests.cs
│   + Editor/UpgradeFrom019Tests.cs
│   ~ Isolation/Probes/ (+ EditingIsolationProbe)  Infrastructure/EndpointCatalog.cs
specs/001-foundation-skeleton/contracts/integration-events.md     # ~
specs/002-identity-setup/contracts/authorization.md               # ~
specs/010-page-tree-components/contracts/  specs/015-cms-items/contracts/   # ~
docs/backlog.md  docs/releases/unreleased/                        # ~ / +
```

**Structure decision**:
- **Editor**: editing sessions under `Domain/` and `Features/EditingSessions/`, beside
  design tokens. It is the module about the editing experience.
- **Pages and Cms**: copies are features of the module that owns the document, so they
  reuse its rules (slugs, validation, usage rows) in its own transaction.
- **Admin**: one `editing/` folder shared by the editor and the item form. Changes in
  `editor/` and `features/` are limited to wiring.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. Two-browser presence checks and screen-reader checks are manual in quickstart §2, §3 and §7, until #25. |

**New exceptions**: none.

## Risks

- **Throttled background tabs** may heartbeat only once a minute.
  - *Mitigation*: still within the 2-minute expiry. Presence in such a tab updates when it
    is visible again (the heartbeat runs at once on `visibilitychange`).
- **A claim refused after the member typed** (two members' first changes at the same
  moment): the loser sees their keystrokes disappear.
  - *Mitigation*: it can only happen within one round trip of each other. The notice names
    the holder, and the change was never saved, so nothing is lost that existed before.
- **Abandoned rows after a crash of the app**: rows lapse by time.
  - *Mitigation*: the cleanup service deletes them, and every read ignores them.
- **Old admin tabs** do not send the session header, so they are not fenced.
  - *Mitigation*: the version check still protects them (FR-026); upgrade note 2.
- **Request volume** through proxies with rate limits.
  - *Mitigation*: upgrade note 3. At 6 requests a minute per tab, the volume is small.

## Deferred (recorded, not built)

- Push updates (SSE or WebSocket) for faster presence.
- Operator settings for the expiry and stale periods.
- A screen listing takeovers.
- Locks for site settings, design tokens, collection schemas and menus (spec assumption).
- Releasing locks at once on session revocation, which would need an Identity session event
  (CE-13).
- "Ask to edit" requests (clarification Q5: not wanted).
