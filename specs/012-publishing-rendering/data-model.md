# Data Model: Publishing and Rendering

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

This feature adds:
1. **Publishing storage in the `pages` schema** (§1): publications, versions, live
   dependencies, schedules and the publish log.
2. **Design publishing in the `editor` schema** (§2).
3. **The domain rules and state changes** that guard them (§3, §4).
4. **Rendering's in-memory model** (§5). Rendering has no tables.

Error codes are listed in §6.

All tables are site-owned (`site_id`, EF global query filter, `ISiteOwned`) unless noted,
and join the isolation suite through `PublishingIsolationProbe` (FR-037). Times are
`timestamptz` in UTC. Users are referred to by id.

---

## 1. `pages` schema (migration `AddPublishing`)

### 1.1 `publications`

One row per page or component that has ever been published. The row stays when the subject
is unpublished, so numbers and generations keep increasing.

| Column | Type | Rules |
|---|---|---|
| `subject_kind` | `text` | `page` or `component`. Part of the primary key. |
| `subject_id` | `uuid` | The page or component. Part of the primary key. Cascade-deleted with it. |
| `site_id` | `uuid` | The owning site. |
| `live_version_id` | `uuid` null | The version visitors get. Null means unpublished. |
| `live_number` | `int` null | The number of that version, kept here so status reads need no join. |
| `next_number` | `int` | Starts at 1. Incremented by every publish and rollback. |
| `published_from_draft_version` | `int` null | The draft version the live version was copied from. Null after a rollback (PR-02). |
| `live_since_utc`, `live_by` | `timestamptz`, `uuid` | When and by whom the live version became live. Null while unpublished. |
| `generation` | `bigint` | The render generation (PR-05). Starts at 1. Only pages use it; components keep it at 1. |
| `xmin` | system | Concurrency token (PR-03). |

**Indexes**:
- `ix_publications_site_live` on `(site_id)` where `live_version_id is not null`, for
  status lists.

### 1.2 `published_versions`

Immutable once inserted.

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` (v7) | Primary key. |
| `site_id` | `uuid` | |
| `subject_kind`, `subject_id` | `text`, `uuid` | Foreign key to `publications`, cascade. |
| `number` | `int` | Unique per subject: `ux_published_versions_subject_number` (PR-03). |
| `schema_version` | `int` | The page schema version of `document`. |
| `document` | `jsonb` | An exact copy of the draft (or of the scheduled capture, or of the restored version). |
| `title` | `text` null | The page title at publish (FR-022's document title). Null for components. |
| `kind` | `text` | `publish`, `scheduled` or `rollback`. |
| `restored_from_number` | `int` null | Set for `rollback`. |
| `published_at_utc`, `published_by` | `timestamptz`, `uuid` | For `scheduled`, `published_by` is the member who set the schedule. |

**Retention** (PR-15): after insert, the same transaction deletes this subject's versions
outside the 50 highest numbers, except `live_version_id`.

### 1.3 `live_dependencies`

What each **live** version depends on (PR-04). The rows for a subject are replaced on
publish or rollback and deleted on unpublish.

| Column | Type | Rules |
|---|---|---|
| `site_id` | `uuid` | |
| `subject_kind`, `subject_id` | `text`, `uuid` | The live page or component. |
| `target_kind` | `text` | `component`, `page` or `media`. |
| `target_id` | `uuid` | |

- **Primary key**: all five columns.
- **Index**: `ix_live_dependencies_target` on `(site_id, target_kind, target_id)`, for
  reverse lookups.

**Extraction** (`DependencyExtractor`, from the version document):
- `component-ref.props.componentId` → `component`;
- `button.props.link` and rich-text `link` marks with `to.type = page` → `page`
  (`pageId`);
- `image.props.media.mediaId` → `media`.

**Pages to refresh when target T changes** (`GenerationBump`):
1. pages with a row → T;
2. pages with a row → component C, where C has a row → T.

That is two levels, because components do not nest.

### 1.4 `publish_schedules`

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` (v7) | Primary key. |
| `site_id` | `uuid` | |
| `subject_kind`, `subject_id` | `text`, `uuid` | Cascade-deleted with the subject. |
| `action` | `text` | `publish` (pages and components) or `unpublish` (pages only). |
| `due_at_utc` | `timestamptz` | In the future when set or changed. |
| `revision` | `int` | Incremented on every change of time. The due message carries it (PR-14). |
| `state` | `text` | `pending`, `done`, `cancelled` or `failed` (§4.2). |
| `captured_document` | `jsonb` null | For `publish`: the draft when scheduled. |
| `captured_schema_version`, `captured_title`, `captured_draft_version` | `int`, `text`, `int` | For `publish`. |
| `failure_code` | `text` null | For `failed`: an error code from §6. |
| `created_at_utc`, `created_by` | | Who set it. |
| `completed_at_utc` | `timestamptz` null | When it reached `done`, `cancelled` or `failed`. |

**Constraint**: `ux_publish_schedules_pending` is unique on `(subject_kind, subject_id,
action)` where `state = 'pending'`.

### 1.5 `publish_log`

Append-only. There is **no foreign key** to the subject, so entries outlive pages and
components (FR-017).

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key. A v7 uuid for entries written in the action's transaction; the event id for design entries (PR-16). |
| `site_id` | `uuid` | |
| `subject_kind` | `text` | `page`, `component` or `design`. |
| `subject_id` | `uuid` | The page or component id, or the site id for `design`. |
| `subject_label` | `text` | The page title or component name at that moment. Empty for `design`. |
| `subject_path` | `text` null | The page address at that moment. |
| `action` | `text` | `publish`, `scheduled_publish`, `rollback`, `unpublish`, `scheduled_unpublish`, `schedule_set`, `schedule_changed`, `schedule_cancelled`, `schedule_failed`, `copied_to_draft` or `deleted`. |
| `version_number` | `int` null | The version created or affected. |
| `restored_from_number` | `int` null | For `rollback`. |
| `actor_user_id` | `uuid` null | Who acted. Null for a scheduled action, which records `schedule_owner_id` instead. |
| `schedule_owner_id` | `uuid` null | Who set the schedule that ran or failed. |
| `reason_code` | `text` null | For `schedule_failed`, and for a cancellation caused by a manual action (`superseded_by_manual_action`). |
| `occurred_at_utc` | `timestamptz` | When it took effect. |

**Indexes**:
- `ix_publish_log_site_time` on `(site_id, occurred_at_utc desc, id desc)`, for the
  cursor;
- `ix_publish_log_subject` on `(site_id, subject_kind, subject_id, occurred_at_utc desc)`.

### 1.6 Changes to existing Pages reads

- **The tree and component lists** gain `publication`:

  ```
  { state: "draft"|"published", liveNumber?, liveSinceUtc?, liveBy?,
    hasUnpublishedChanges, pendingSchedules: [{ action, dueAtUtc }],
    failedSchedule?: { action, failureCode } }
  ```

  `hasUnpublishedChanges` is computed as research PR-02 describes.
- **The page delete command** checks that there is no live version. **The component delete
  command** also checks for no live version, and that no `live_dependencies` row targets
  the component.

---

## 2. `editor` schema (migration `AddDesignPublishing`)

### 2.1 `design_publications`

One row per site that has published its design.

| Column | Type | Rules |
|---|---|---|
| `site_id` | `uuid` | Primary key. |
| `live_number` | `int` | The live design version. |
| `next_number` | `int` | |
| `published_from_token_version` | `bigint` null | The draft token set version (`xmin`) copied. Null after a rollback. |
| `live_since_utc`, `live_by` | | |
| `xmin` | system | Concurrency token. |

### 2.2 `design_versions`

| Column | Type | Rules |
|---|---|---|
| `site_id`, `number` | `uuid`, `int` | Primary key. |
| `schema_version` | `int` | The design-token contract version. |
| `document` | `jsonb` | An immutable copy of the token set. |
| `kind` | `text` | `publish` or `rollback`. |
| `restored_from_number` | `int` null | |
| `published_at_utc`, `published_by` | | |

Retention is the 50 newest, never the live one.

**No row means** the site renders with `DefaultDesignTokens` and design number `0`
(FR-010).

---

## 3. Domain rules

### 3.1 `Publication` aggregate (Pages)

| Operation | Preconditions (refusal code) | Effects |
|---|---|---|
| **Publish** (page or component) | Draft `version` equals the request's `draftVersion` (`pages.out_of_date`). The draft validates against the page schema contract (`pages.invalid_document`). For a page, every `component-ref` target has a live version (`pages.unpublished_components`, listing ids). | A new version with `kind = publish` becomes live. `next_number` increments. Dependencies are replaced. Generations are bumped (§3.2). A pending scheduled publish is cancelled (`superseded_by_manual_action`). Old versions are pruned. Log `publish`. Event `…Published`. |
| **Unpublish** (page) | Live (`pages.not_published`). | The live pointer is cleared. Dependencies are deleted. Generations are bumped. Pending scheduled publish and unpublish are cancelled. Log `unpublish`. Event `PageUnpublished`. |
| **Unpublish** (component) | Live (`pages.not_published`). No live page depends on it (`pages.component_in_use_by_published_pages`, with a count). | As for a page. |
| **Roll back to N** | Version N is retained (`pages.version_not_found`). Every component N references exists; for a page, each has a live version (`pages.version_missing_components`, `pages.unpublished_components`). | A new version copies N with `kind = rollback`, `restored_from_number = N` and `published_from_draft_version = null`. The rest is as for publish. The draft is unchanged. |
| **Copy version N to draft** | Version N is retained. The draft `version` equals the request's (`pages.out_of_date`). | The draft is replaced through 010's draft save (validation, `component_usages`). Nothing public changes. Log `copied_to_draft`. |
| **Schedule publish at T** | T > now (`pages.schedule_in_past`). The draft version matches, and the draft validates. | Upserts the pending `publish` schedule with the captured draft. `revision` increments. Writes the due message. Log `schedule_set` or `schedule_changed`. |
| **Schedule unpublish at T** (page) | T > now. | As above, with no capture. |
| **Cancel schedule** | A pending schedule exists (`pages.schedule_not_found`). | `cancelled`. Log `schedule_cancelled`. |
| **Run due schedule** (handler) | The schedule is `pending` and the revision matches; otherwise nothing happens. | Runs Publish or Unpublish with `kind = scheduled` and the captured content, skipping the draft-version check. On a domain refusal the schedule is `failed` with the code, and log `schedule_failed`. Otherwise it is `done`, with log `scheduled_publish` or `scheduled_unpublish`. |

**Delete** (010's page and component delete, extended):
- It is refused while live (`pages.page_published`), or for a component, while in use by
  live pages.
- Otherwise it cascades versions, the publication row and schedules, and writes log
  `deleted` when the subject has any log entry.

### 3.2 Generation bumps (same transaction as the cause)

| Cause | Pages whose `generation` increments |
|---|---|
| Page P published, rolled back or unpublished | P, plus pages that depend on P (links), directly or through a component |
| Component C published, rolled back or unpublished | Pages depending on C |
| 010 tree operation changing addresses of pages S (slug change, move, home change) | Pages depending on any page in S, directly or through a component |
| Media file M deleted (`Media.FileDeleted` handler) | Pages depending on M, directly or through a component |

It is one `UPDATE … SET generation = generation + 1 WHERE subject_kind = 'page' AND
subject_id IN (…)` built from the reverse index, parameterised through EF Core.

---

## 4. State changes

### 4.1 Page or component publication

```text
            publish / scheduled publish
  (never) ──────────────────────────────► LIVE ◄──┐ publish / rollback / scheduled publish
     │                                      │  └──┘   (new version, number + 1)
     │                                      │ unpublish / scheduled unpublish
     │                                      ▼
     │                                 UNPUBLISHED ── publish / rollback ──► LIVE
     │                                      │
     └────────────── delete ────────────────┴── delete (only when not LIVE)
```

### 4.2 Schedule

```text
  set ──► PENDING ──(time, runs ok)──────────────► DONE
            │  ├──(time, domain refusal)─────────► FAILED
            │  ├──(cancel, or superseded)────────► CANCELLED
            │  └──(change time: revision + 1)──► PENDING
            └──(subject deleted)──► row removed
```

### 4.3 Design publication

```text
  defaults (number 0) ── publish ──► LIVE n ── publish / rollback ──► LIVE n + 1
```

The design has no unpublish: a site always has a design.

---

## 5. Rendering's model (in memory, no tables)

| Type | Holds |
|---|---|
| `RenderRequest` | Site (`SiteDto`), normalised path, method, `If-None-Match`. |
| `LivePageRef` | `pageId` and `generation`, from `IPublishedContentReader.FindLiveByPathAsync`. |
| `RenderKey` | `siteId`, `pageId`, `generation`, `designNumber`, `siteVersion`, `rendererStamp`, and its string form (PR-05). |
| `RenderInputs` | The live page version (document and title), the live component versions it references, the live design (document or defaults), page link targets (address and live flag), and media (public path and availability). Read after the key, never before (PR-05). |
| `RenderedPage` | UTF-8 HTML, the ETag, and the style-block hash for the CSP. The value cached under `RenderKey`. |
| `PublicResponse` | Status, content type, headers and body. What `IPublicNotFoundPage` returns to the host (PR-08). |
| `BlockDefaults` | Per block type and property, the schema `default` (PR-09). |
| `TokenCss` | Custom-property map for a design document (PR-10). |

---

## 6. Error codes (new)

Pages (`pages.*`, HTTP `409` unless noted):

| Code | When |
|---|---|
| `pages.unpublished_components` | Publish, rollback or a due schedule references components without a live version. `componentIds` lists them. |
| `pages.component_in_use_by_published_pages` | Unpublish or delete of a component used by live pages. `pageCount` gives the count. |
| `pages.page_published` | Delete of a live page. |
| `pages.not_published` | Unpublish of something not live. |
| `pages.version_not_found` (`404`) | Rollback, view or copy of a version that is not retained. |
| `pages.version_missing_components` | Rollback to a version that references deleted components. `componentIds` lists them. |
| `pages.schedule_in_past` (`400`) | Schedule time not in the future. |
| `pages.schedule_not_found` (`404`) | Cancel with no pending schedule. |
| `pages.out_of_date`, `pages.invalid_document` | Reused from 010 for publish, schedule and copy. |

Editor (`editor.*`):

| Code | When |
|---|---|
| `editor.out_of_date` (`409`) | The design publish `version` is not the token set's current version. |
| `editor.design_version_not_found` (`404`) | Rollback or view of a design version that is not retained. |

Refusals follow the existing problem-details shape (`errorCode`, plus the extra fields
named above). The admin shell maps each code to a message (FR-040).
