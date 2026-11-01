# Research: Concurrent Editing Safety

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #23

Decisions are numbered CE-01 onwards. Each one states the decision, why, and what else was
considered. "Today" means `main` at 59827ae.

## What exists today

- **Pages and components** (010, 011): `page_drafts` and `component_drafts` carry `xmin` as
  the concurrency token, sent to the admin as `version`. `DraftWrites.SavePageDraftAsync` and
  `SaveComponentDraftAsync` compare it before writing and map `DbUpdateConcurrencyException`
  to `pages.out_of_date` (409). The draft also stores `UpdatedBy` and `UpdatedAtUtc`. No
  other code writes a page or component draft: rollback publishes without touching the
  draft, and schema upgrades are applied on read and stored on the next save (019).
- **Editor** (011): `autosave.ts` sends whole-document saves against `store.baseVersion`
  and stops on 409 with a read-only `conflict` state; `ConflictBanner.vue` offers only
  "Reload latest". `recovery.ts` keeps unsaved changes in the browser.
- **Collection items** (015): `Item.Revision` is incremented by every write (`Touch`):
  draft saves, publish, unpublish, schedule changes and copy-to-draft. `Migrate` (schema
  changes, 014) increments it without changing `UpdatedBy`. A draft save states the
  revision and is refused with `cms.item_conflict` (409, `currentRevision`). The item form
  saves on demand; it does not autosave.
- **No real-time channel**: there is no SignalR, WebSocket or server-sent events anywhere in
  the product.
- **Names**: `IUserDirectory.FindManyAsync` (Identity.Contracts) gives display names.
- **Module edges**: Editor → Pages.Contracts, Sites.Contracts, Identity.Contracts. Pages →
  Editor.Contracts, Cms.Contracts, Media.Contracts. Cms → Pages.Contracts. Cms does not
  reference Editor.Contracts.

---

## CE-01 Editing sessions live in the Editor module

**Decision**: A new aggregate, `EditingSession`, in the **Editor** module (schema `editor`)
holds viewers and lock holders for all three document kinds (`page`, `component`, `item`).
Pages and Cms do not store locks.

**Rationale**: Presence and locks are about the editing experience, not about the content.
One owner gives one set of rules for expiry, stale periods and takeover, and one place to
make them correct across instances. The Editor module already exists, owns the editor's
server-side concerns (design tokens), and already references Pages.Contracts.

**Alternatives considered**:
- *Locks in Pages and in Cms separately*: duplicates the whole state machine and its race
  tests; markers in lists would need two endpoints.
- *Shared.Infrastructure*: the constitution keeps it free of module-specific concepts;
  "who is editing a page" is one.
- *A new module*: the module list is fixed by Principle I; adding one needs an amendment for
  no gain.

## CE-02 Locks are rows in PostgreSQL, not cache entries

**Decision**: `editor.editing_sessions` stores one row per open editor tab. A partial unique
index on `(site_id, document_kind, document_id) WHERE holds_lock` guarantees at most one
holder (FR-010). Claims, takeovers and moves are single transactions using EF Core
`ExecuteDelete`/`ExecuteUpdate` and an insert/update that the unique index arbitrates.

**Rationale**: PostgreSQL is the only required dependency (Principle IV) and is shared by
every instance, so the guarantee holds on one instance with no configuration and on many
behind a load balancer (FR-025). The cache abstraction is in-memory by default and per
instance, which would let two instances grant the same lock.

**Alternatives considered**:
- *Distributed cache with Redis when configured*: correct only when Redis is configured;
  the default install would be wrong with two instances.
- *PostgreSQL advisory locks*: tied to a database connection, which a web request does not
  keep between heartbeats.

## CE-03 Liveness by heartbeat; expiry is computed, cleanup is housekeeping

**Decision**:
- The admin sends a heartbeat every **10 seconds** while an editor or item form is open.
- A session whose `last_seen_at` is older than **2 minutes** is treated as gone by every
  read and every claim (FR-016). Nothing depends on a job having run.
- A hosted service, `EditingSessionCleanupService`, deletes such rows every minute and
  prunes takeover records older than 90 days (CE-12).
- The heartbeat response is the presence snapshot (holder, viewers, stale state, server
  time), so one request per 10 seconds both keeps the session alive and keeps the screen
  current (SC-003: changes shown within 15 s).

**Rationale**: Polling is the simplest mechanism that works through every reverse proxy and
needs nothing new (Principle XI). At 10 seconds, 100 open editors make 10 small writes per
second, well within the reference setup. Background tabs are throttled by browsers to about
one timer per minute, which is still inside the 2-minute expiry.

**Alternatives considered**:
- *Server-sent events or WebSockets*: faster updates, but they need sticky sessions or a
  backplane across instances, and long-lived connections through operators' proxies; this
  is a new mechanism for a 15-second target that polling meets.
- *A 30-second heartbeat*: fewer requests, but misses SC-003.

## CE-04 Roles: viewer first, holder on the first change

**Decision** (clarification Q3): Opening a document creates a **viewer** session. The first
change claims the lock. In the editor and the item form:

1. The change is applied on screen at once, but autosave (and the form's Save) stays held
   until the claim answers.
2. If the claim succeeds, the session is the holder and saving proceeds.
3. If it is refused because someone else got there first, the change is reverted (undone,
   not recorded in the undo history) and the editor turns read-only naming the holder
   (FR-009a).

A claim is a cheap request (one indexed write); typing does not wait for it.

**Rationale**: Blocking the first keystroke on a round trip would make the editor feel
broken; applying it but never saving it until the lock is held meets FR-009 ("a change is
applied only once they hold it") in the sense that matters: it never reaches the draft.
Reverting on refusal keeps the read-only view equal to the stored draft.

**Alternatives considered**:
- *Claim on focus of an editable control*: claims locks for members who only click around,
  which clarification Q3 rejected.
- *Wait for the claim before applying the change*: correct but visibly laggy on slow
  connections.

## CE-05 Activity, stale locks and takeover

**Decision**:
- Each heartbeat states whether the holder made a change or an editing action since the
  previous one; the server then sets `last_activity_at` to its own time. Selecting,
  scrolling and previewing do not count (spec edge case).
- A lock is **stale** when `now − last_activity_at ≥ 30 minutes` (FR-017), computed by the
  server on every read.
- **Takeover** is one transaction:
  1. release the holder row, but only if it is still the holder named in the request and
     still stale;
  2. claim for the requester;
  3. insert an `editing_takeovers` row.

  The released row keeps `lost_lock_at`, `lost_lock_to_user_id` and
  `lost_lock_reason = 'takeover'`, so the former holder's next heartbeat tells them who
  took over and when (FR-018).
- The expiry (2 minutes) and the stale period (30 minutes) are constants in
  `EditingRules`, not configuration keys.

**Rationale**: Server time for every comparison meets the clock edge case. Naming the
expected holder in the request makes a double takeover, or a takeover racing with the
holder coming back, fail cleanly rather than take a lock from the wrong person. Constants
keep operator impact at zero; the spec's assumption allows making them settings later.

**Alternatives considered**: client-reported idle time (devices' clocks differ); operator
settings now (no demand, more to document and test).

## CE-06 Moving one's own lock ("Edit here instead")

**Decision** (clarification Q4): When the holder session belongs to the same user, the
`move` operation transfers the lock whether it is active or stale. The former session gets
`lost_lock_reason = 'moved'`. No takeover record is written.

**Rationale**: It is the same person, so there is no one to protect; recording it would
make the takeover history noisy. Unsaved changes in the former tab are protected by
fencing (CE-07) and the version check.

## CE-07 Draft saves are fenced by the editing session

**Decision**:
- The admin sends `X-QB-Editing-Session: <session id>` on every draft save of a page,
  component or item.
- Before writing, Pages and Cms ask `IEditingLocks.CheckWriteAsync` (Editor.Contracts). The
  answer is **HeldByOther** when a live lock on the document belongs to another session;
  the save is then refused with `editing.lock_held` (409), which carries the holder's name
  and since when.
- A save with no header, or on a document with no live lock, is checked only by version
  (FR-026).

**Rationale**: Without fencing, a former holder whose network comes back could have a queued
autosave land after a takeover (FR-018: "never into the document"). The check is a read in
the Editor module before the owning module's own transaction, so no transaction spans two
modules (Principle I). A takeover that happens between the check and the write is harmless:
the new holder loaded the draft before the write, so their next save is refused by version
and nothing is overwritten.

**Alternatives considered**:
- *Version check only*: leaves the takeover race open, which breaks FR-018.
- *Hard locks (refuse every save without the lock)*: breaks FR-026, because after a restart
  or an expiry nobody could save until they claimed again; it also breaks API clients
  outside the admin.

## CE-08 Item drafts get their own revision

**Decision**:
- `Item` gains `DraftRevision`, incremented only by draft changes: `SaveDraft`,
  `CopyToDraft` and `Migrate` when the draft values change.
- Draft saves compare `DraftRevision`. Publish, unpublish, schedule and rollback keep
  comparing `Revision`.
- `DraftRequest` gains an optional `draftRevision`; when it is absent (an older admin tab),
  the save falls back to `Revision`, as today.
- The migration sets `draft_revision = revision` for existing rows.

**Rationale**: The spec's publishing edge case says a publish by someone else must not make
the holder's next save fail. Today it does, because publishing increments `Revision`. A
draft-only counter fixes that without weakening the publish checks.

**Alternatives considered**: comparing draft contents on mismatch (no stored base to compare
with); dropping the revision check on draft saves (breaks FR-001).

## CE-09 Who changed it: conflict details in the refusal

**Decision**:
- `pages.out_of_date` refusals of draft saves gain `changedBy` (`{ id, displayName }`),
  `changedAtUtc` and `currentVersion`, read from the stored draft.
- `cms.item_conflict` for draft saves gains the same fields. `Item` gains
  `DraftChangedBy` (nullable), `DraftChangedAtUtc` and `DraftChangeKind` (`member` or
  `schema_change`). `Migrate` sets `schema_change` with no member.
- Names come from `IUserDirectory` (FR-003).
- Same-session saves are never reported as conflicts (FR-004), because the admin keeps one
  save in flight and always sends the version the last save returned (autosave.ts today);
  a test pins this.

**Rationale**: The fields are additive in both problem documents, so older admin tabs ignore
them.

## CE-10 Conflict dialog and item field diff

**Decision**:
- `ConflictBanner.vue` is replaced by `ConflictDialog.vue`, a modal with three actions:
  reload, keep viewing, and save as a copy (shown only when allowed, CE-11).
- "Keep viewing" leads to the existing read-only banner, so 011 FR-028's copy-and-paste
  path is unchanged.
- For items, the form fetches the latest item after the refusal and compares draft values
  by field id with the values it loaded, listing the fields whose values differ (FR-007).
  The comparison runs in the admin; no new endpoint is needed.

## CE-11 Copies are new documents, created by dedicated endpoints

**Decision** (clarification Q2):
- **Page copy**: `POST …/pages/{pageId}/copies` takes the member's document, the version it
  was based on and the localised title. In one Pages transaction, it creates a page directly
  after the original under the same parent, with title "<title> (copy)" and a slug made
  unique (`<slug>-copy`, then `-copy-2`, …). It then saves the document as the new page's
  draft through `DraftWrites`, so usage rows and validation are the same as any save.
  Requires `pages.structure.manage`.
- **Component copy**: `POST …/components/{componentId}/copies`, named "<name> (copy)".
  Requires `pages.components.manage`.
- **Item copy**: `POST …/items/{itemId}/copies` with the form's values and slug. The slug
  is made unique as in 015's duplicate. Requires `cms.items.edit`.
- Each copy is unpublished. The new document records a `CopyOrigin` (source id, the time of
  the source version the member loaded, who and when).
- The admin offers the action only when the member holds the capability (FR-020a), and the
  server refuses it otherwise.

**FR-022 in practice**:
- **Items**: draft values for fields that no longer exist are dropped and listed in the
  response; values that fail field rules are kept, as drafts already allow (015 attention
  report).
- **Pages and components**: a document can only become invalid through a contract change,
  and those are upgraded on read (019), so a document the editor holds still validates. If
  it does not (an editor bug), the copy is refused with the rule, and the read-only view
  still allows copying blocks.

**Alternatives considered**: reusing 015's `DuplicateItem` (it copies the *stored* draft, not
the member's version); a generic "copy" in Editor (it would write other modules' tables).

## CE-12 Takeover record and logging

**Decision**: `editor.editing_takeovers` keeps who took over from whom, the document, the
time and how long the lock had been stale. Records are pruned after 90 days by the cleanup
service. Takeovers, `editing.lock_held` refusals and draft conflicts are logged with ids
only (FR-024). There is no screen for the records; they exist for support and audits.

**Alternatives considered**: Identity's security events (they are about accounts, not
content); writing into each module's history (three places for one fact).

## CE-13 Release on leave, close and sign-out; revocation by expiry

**Decision**:
- **Leaving in the admin**: the router's leave guard releases the session (`DELETE`).
- **Closing the tab**: a `pagehide` handler sends the same `DELETE` with `fetch(…,
  { keepalive: true })`, which, unlike `sendBeacon`, carries the request header that the
  admin API requires.
- **Signing out**: the admin releases its open sessions before calling sign-out.
- **Revoked sessions** (002): heartbeats from a revoked session fail with 401, so the row
  lapses within the 2-minute expiry.
- **Lost permission** (FR-019): every heartbeat re-checks the kind's edit capability for the
  site; when it is gone, the server releases the lock and the response says `forbidden`.
- **Deleted documents**: Editor handles `PageDeleted`, `ComponentDeleted` and `ItemDeleted`
  from the outbox and deletes the sessions; the next heartbeat answers `gone`. For a deleted
  collection, item sessions carry the collection id and are removed on `CollectionDeleted`.

**Spec alignment**: FR-015 says a lock is released "at once" when a session "ends or is
revoked". Revocation is detected only by the expiry, within 2 minutes, unless Identity
starts publishing session events. This plan narrows FR-015 accordingly (spec updated in the
same change; see plan.md).

**Alternatives considered**: a new Identity integration event for revocations. It needs the
editing session to know the sign-in session id, which Identity does not expose, for a rare
case that expiry already covers within 2 minutes.

## CE-14 Becoming free: load the latest before editing

**Decision**: When a heartbeat shows that the lock was released or expired, a read-only
viewer gets a "now free" notice and an **Edit** action. Choosing it re-reads the draft. If
the version changed, the editor loads the new draft first, then claims. A member never
starts editing an outdated version (spec Story 2 scenario 6).

## CE-15 Markers in lists

**Decision**: `GET /api/v1/editor/sites/{siteId}/editing?kind=page|component|item[&collectionId=]`
returns the live holders of that kind on the site (document id, holder name, since, stale).
The page tree, component list and item list call it on load and every 15 seconds while
visible, and show `LockMarker.vue` (FR-012). It requires the kind's view capability.

**Rationale**: One small query per list. The lists themselves (Pages, Cms) stay unchanged on
the server.

## CE-16 Capabilities by kind, without new capabilities

**Decision**: Editor maps each kind to existing capabilities:

| Kind | Edit | View |
|---|---|---|
| `page` | `pages.page_drafts.edit` | `pages.content.view` |
| `component` | `pages.components.manage` | `pages.content.view` |
| `item` | `cms.items.edit` | `cms.collections.view` |

`cms.items.edit` and `cms.collections.view` move to a new `ItemCapabilities` in
Cms.Contracts, as Pages did with `PageContentCapabilities`, so Editor does not repeat the
strings. Members without the edit capability can open a viewer session (they see who is
editing) but cannot claim.

## CE-17 Publishing and deleting while locked

**Decision**: These are warnings in the admin, using the presence the screen already has:
- Publish menus on the editor, the component screen and the item form warn when someone
  else holds the lock.
- Delete dialogs in the page tree, component list and item list warn when the document has
  a holder.

No server rule changes: publishing never touches the draft (CE-08), and deletion ends
sessions (CE-13).

## CE-18 Crash recovery with locks

**Decision**: `RecoveryDialog` offers "Restore" only when the saved draft is unchanged (as
today) **and** a claim succeeds. Otherwise it opens the copy read-only, with "Save as a copy"
where allowed (spec edge case).

## CE-19 Accessibility

**Decision**:
- Presence changes are announced through one polite live region per editor, and conflicts
  and takeovers through the dialog's `role="alertdialog"`.
- Every action is a button reachable by keyboard.
- New components get axe checks in vitest, and manual screen-reader checks go in quickstart
  §7 (exception E-5 unchanged).

## CE-20 Tests

**Decision**:
- **Unit**: `EditingRulesTests` (stale, expiry, claim, takeover and move decisions with a
  fake clock); `ItemDraftRevisionTests`; `CopyNamingTests`. Admin:
  `useEditingSession.spec.ts` (fake timers: heartbeat, claim-on-first-change, revert on
  refusal, release on leave and pagehide), `ConflictDialog.spec.ts`,
  `PresenceBanner.spec.ts`, `itemDiff.spec.ts`, plus axe.
- **Integration** (Testcontainers):
  - `EditingSessionEndpointsTests`;
  - `LockRaceTests`: 200 parallel claims, and the same across two app instances against
    one database (FR-010, FR-025);
  - `OverlappingDraftWritesTests` (SC-001: 1,000 pairs over pages, components and items);
  - `DraftFencingTests`, `ItemPublishDoesNotConflictTests`, `CopyEndpointsTests`;
  - `EditingCleanupTests` (deleted documents, expiry, lost permission);
  - `EditingIsolationProbe` (site B sees nothing of site A, FR-013);
  - `UpgradeFrom019Tests` (backfill of `draft_revision`; existing drafts still save).
- **Architecture**: `AuthorizationDeclarationTests` and `EndpointCatalog` cover the new
  endpoints; `ModuleBoundaryTests` allows the two new Contracts edges (CE-21).

## CE-21 Module edges

**Decision**:
- Editor implementation → **Cms.Contracts** (new edge: item deletion events,
  `ItemCapabilities`).
- Cms implementation → **Editor.Contracts** (new edge: `IEditingLocks`).
- Pages → Editor.Contracts already exists.

All edges point at Contracts projects only. Editor.Contracts keeps referencing only the
kernel.
