# Data Model: Concurrent Editing Safety

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

New items are marked **+**, changed items **~**.

## 1. Editor module (schema `editor`)

### 1.1 + `editing_sessions` (aggregate `EditingSession`)

One row per open editor or item form (a browser tab), whether it holds the lock or only
views.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | Session id, created by the server on open; sent by the admin as `X-QB-Editing-Session`. |
| `site_id` | uuid | Global site filter (Principle V). |
| `document_kind` | text | `page`, `component` or `item` (check constraint). |
| `document_id` | uuid | Id of the page, component or item. Not a foreign key (other module's table). |
| `collection_id` | uuid null | For items only, so `CollectionDeleted` can remove sessions (CE-13). |
| `user_id` | uuid | The member. |
| `opened_at` | timestamptz | Server time. |
| `last_seen_at` | timestamptz | Set by every heartbeat. |
| `holds_lock` | boolean | True for the one holder. |
| `lock_taken_at` | timestamptz null | Since when the lock is held ("editing since"). |
| `last_activity_at` | timestamptz null | Last change or editing action reported while holding (CE-05). |
| `lost_lock_at` | timestamptz null | When this session last lost the lock to someone else. |
| `lost_lock_to_user_id` | uuid null | Who took it or moved it. |
| `lost_lock_reason` | text null | `takeover` or `moved`. |

**Indexes**:
- `ux_editing_sessions_holder` UNIQUE `(site_id, document_kind, document_id) WHERE holds_lock`:
  at most one holder (FR-010).
- `ix_editing_sessions_document` `(site_id, document_kind, document_id)`.
- `ix_editing_sessions_last_seen` `(last_seen_at)`, for cleanup.

**Derived state** (computed with the server's clock, never stored; constants in `EditingRules`):

| State | Condition |
|---|---|
| gone | `now − last_seen_at ≥ 2 min` (Expiry). Ignored by every read and claim. |
| viewer | not gone, `holds_lock = false` |
| holder, active | not gone, `holds_lock`, `now − last_activity_at < 30 min` (StalePeriod) |
| holder, stale | not gone, `holds_lock`, `now − last_activity_at ≥ 30 min` |

**Transitions**:

```text
            open                      first change (claim, lock free)
 (none) ─────────▶ viewer ───────────────────────────────────────────▶ holder (active)
                    ▲  │ claim refused: another holder                      │  │
                    │  └──▶ stays viewer, change reverted (CE-04)           │  │ 30 min, no activity
                    │                                                       │  ▼
  takeover by other │◀──────────────────────────────────────────── holder (stale)
  (holder was stale)│◀── move by own other session (active or stale) ──────┘
                    │      sets lost_lock_* on this row
 leave / pagehide / sign-out ─▶ row deleted          heartbeat missing 2 min ─▶ gone
 lost capability (heartbeat) ─▶ lock released, response `forbidden`
 document deleted (event)    ─▶ row deleted, next heartbeat `gone`
```

A claim is refused when another non-gone row holds the lock. Before claiming, a gone holder
row for the document is deleted in the same transaction.

### 1.2 + `editing_takeovers`

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `site_id` | uuid | Site filter. |
| `document_kind`, `document_id` | text, uuid | |
| `from_user_id`, `to_user_id` | uuid | |
| `taken_at` | timestamptz | |
| `stale_for_seconds` | integer | How long the lock had been inactive. |

Index `(site_id, taken_at)`. Rows older than 90 days are deleted by the cleanup service
(CE-12). This table is not exported with a site; it records events, not content.

## 2. Pages module (schema `pages`)

### 2.1 ~ `pages` and `components`: copy origin

An owned value `CopyOrigin`, null except on copies (CE-11, FR-021):

| Column | Type | Notes |
|---|---|---|
| `copied_from_id` | uuid null | The source page or component. It may be deleted later, so this is not a foreign key. |
| `copied_from_saved_at` | timestamptz null | `UpdatedAtUtc` of the source draft version the member had loaded. |
| `copied_by` | uuid null | |
| `copied_at` | timestamptz null | |

All four are set together or all are null (check constraint).

### 2.2 Drafts: no change

`page_drafts` and `component_drafts` keep `xmin` as `version`, with `updated_by` and
`updated_at_utc`. These are the source of `changedBy` and `changedAtUtc` in conflict
refusals (CE-09).

## 3. Cms module (schema `cms`)

### 3.1 ~ `items`

| Column | Type | Notes |
|---|---|---|
| + `draft_revision` | integer not null | Incremented by `SaveDraft`, `CopyToDraft`, and by `Migrate` when the draft values change (CE-08). Backfilled from `revision`. |
| + `draft_changed_by` | uuid null | Member of the last draft change; null for a system change. Backfilled from `updated_by`. |
| + `draft_changed_at_utc` | timestamptz not null | Backfilled from `updated_at_utc`. |
| + `draft_change_kind` | text not null | `member` or `schema_change`. Backfilled as `member`. |
| + `copied_from_id`, `copied_from_saved_at`, `copied_by`, `copied_at` | as §2.1 | Copy origin for items. |

`revision` is unchanged and still moves on every write. Publishing actions keep checking it.

### 3.2 ~ Item history

Item history gets a new kind, `created_as_copy`, recorded on the copy with the source item
id. History is item-scoped already, so the copy's history shows where it came from.

## 4. Validation rules

| Rule | Where | Requirement |
|---|---|---|
| Only a member with the kind's edit capability can claim, take over or move. | Editor handlers | FR-009, FR-023 |
| Takeover only when the named holder is still the holder and stale. | `EditingRules.CanTakeOver` | FR-017 |
| Move only when the holder belongs to the same user. | `EditingRules.CanMove` | FR-014 |
| Heartbeat, release and claim only on one's own session. | Editor handlers (user id match) | FR-023 |
| A draft save with a session header is refused when another live session holds the lock. | Pages `DraftWrites`, Cms `ItemWrites` | FR-018, CE-07 |
| Item draft saves compare `draft_revision` when sent, else `revision`. | Cms `ItemWrites` | FR-001, CE-08 |
| Copies require the create capability of the kind. | Pages, Cms copy handlers | FR-020a |
| A page copy's slug is unique among its siblings; an item copy's slug is unique in its collection. | Existing slug rules | FR-020 |

## 5. Errors (new or extended)

| Code | Status | Body fields | When |
|---|---|---|---|
| ~ `pages.out_of_date` | 409 | + `changedBy`, `changedAtUtc`, `currentVersion` | Page or component draft save on an older version (draft saves only). |
| ~ `cms.item_conflict` | 409 | + `changedBy` (null for system), `changeKind`, `changedAtUtc`, `currentDraftRevision` | Item draft save on an older draft revision. |
| + `editing.lock_held` | 409 | `holder` (`{ id, displayName }`), `since`, `stale` | Claim refused, or a fenced draft save by a non-holder session. |
| + `editing.not_stale` | 409 | `holder`, `since`, `inactiveSeconds` | Takeover of a lock that is active, or whose holder changed. |
| + `editing.session_gone` | 404 | | Heartbeat or claim on an expired or deleted session; the admin opens a new one. |
| + `editing.forbidden` | 403 | | No edit capability for a claim; no view capability to open. |
| + `pages.copy_forbidden` / `cms.copy_forbidden` | 403 | | Copy without the create capability. |
