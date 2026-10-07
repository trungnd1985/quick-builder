# Data Model: CMS Items

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

PostgreSQL, `cms` schema, EF Core migration **`AddItems`** (research IT-22), snake_case
columns. Every new table carries `site_id` with the Cms global query filter (Principle V).
Values documents keep the 014 contract shape (`{ schemaVersion, values }`, contract §3).
Changes to 014 tables are marked **~**, new tables **+**.

---

## 1. Tables

### 1.1 ~ `collections`

| Column | Type | Null | Notes |
|---|---|---|---|
| + `schema_revision` | int | no | The collection `revision` at which the schema document last changed (IT-07). Set by `Collection.Apply` when `schemaChanged`; backfilled to `revision`. |

### 1.2 ~ `items`

014 columns (`id`, `site_id`, `collection_id`, `draft`, `published`, `created_at_utc`,
`updated_at_utc`) are kept. Added:

| Column | Type | Null | Notes |
|---|---|---|---|
| `slug` | varchar(100) | no | Draft slug, `^[a-z0-9][a-z0-9-]{0,99}$` (IT-03). |
| `published_slug` | varchar(100) | yes | Null exactly when `published` is null. |
| `status` | varchar(10) | no | `draft`, `published` or `changed` (IT-04). |
| `revision` | int | no | ≥ 1, EF concurrency token. +1 on every write to the item (FR-005). A schema migration that rewrites the item also increments it. |
| `name` | text | yes | The title field's draft value (IT-11), which may be up to 10,000 characters. Null when empty or when the collection has no title field. Sorted and indexed on `left(name, 256)`. |
| `search_text` | text | no | `SearchText.Normalise(name + " " + slug)` (IT-13). |
| `attention` | jsonb | yes | The draft's validation report (§3.2) when it has any problem, otherwise null. `attention IS NOT NULL` is the "needs attention" filter. |
| `index_version` | int | no | Version of `ItemIndexer` that last refreshed the row; 0 for rows upgraded from 014. |
| `created_by`, `updated_by` | uuid | no | Empty guid for rows upgraded from 014. |
| `published_at_utc` | timestamptz | yes | Time of the publish that produced the live version. |
| `published_by` | uuid | yes | Member of that publish (for a scheduled publish, who set it). |
| `live_version` | int | yes | `item_versions.number` of the live version; null when unpublished. |
| `next_version_number` | int | no | Starts at 1; only grows (version numbers are never reused). |

Indexes: `(collection_id, updated_at_utc DESC, id)`, `(collection_id, left(name, 256))`,
`(collection_id, slug)`, `(collection_id, status)`, `(collection_id) WHERE attention IS NOT
NULL`.

### 1.3 + `item_slugs`

| Column | Type | Null | Notes |
|---|---|---|---|
| `collection_id` | uuid | no | FK → `collections` cascade. |
| `slug` | citext | no | PK `(collection_id, slug)`. |
| `item_id` | uuid | no | FK → `items` cascade. Index. |
| `site_id` | uuid | no | Global filter. |

One row per distinct slug an item holds (draft, published). Replaced in every item write
(IT-03). A primary-key violation is `cms.item_slug_taken`.

### 1.4 + `item_references`

| Column | Type | Null | Notes |
|---|---|---|---|
| `site_id` | uuid | no | Global filter. |
| `source_item_id` | uuid | no | FK → `items` **cascade**. |
| `field_id` | varchar(8) | no | A reference or multi-reference field. |
| `target_item_id` | uuid | no | FK → `items` **`ON DELETE NO ACTION`** (IT-10). Index. Never equal to the source. |
| `in_draft`, `in_published`, `in_scheduled` | bool | no | Where the reference is; at least one is true. |

PK `(source_item_id, field_id, target_item_id)`. Replaced by the indexer.

### 1.5 + `item_media`

| Column | Type | Null | Notes |
|---|---|---|---|
| `site_id` | uuid | no | Global filter. |
| `item_id` | uuid | no | FK → `items` cascade. |
| `media_id` | uuid | no | Media file id (no FK: another module's table). Index `(site_id, media_id)`. |
| `in_draft`, `in_live`, `in_scheduled`, `in_history` | bool | no | Draft values, live published values, pending scheduled content, kept versions other than the live one (IT-15). |

PK `(item_id, media_id)`.

### 1.6 + `item_versions`

| Column | Type | Null | Notes |
|---|---|---|---|
| `item_id` | uuid | no | FK → `items` cascade. PK `(item_id, number)`. |
| `number` | int | no | 1, 2, 3 … per item, never reused. |
| `site_id` | uuid | no | Global filter. |
| `values` | jsonb | no | Item values document, migrated by schema changes (IT-12). |
| `slug` | varchar(100) | no | |
| `kind` | varchar(10) | no | `publish`, `scheduled`, `rollback`. |
| `restored_from` | int | yes | For `rollback`: the version restored. |
| `published_at_utc`, `published_by` | timestamptz, uuid | no | |

At most 50 per item, never deleting the live one (FR-023).

### 1.7 + `item_schedules`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK, UUID v7. |
| `site_id` | uuid | no | Global filter. |
| `item_id` | uuid | no | FK → `items` cascade. |
| `action` | varchar(10) | no | `publish` or `unpublish`. |
| `due_at_utc` | timestamptz | no | |
| `revision` | int | no | +1 on each reschedule; the due message carries it (IT-09). |
| `state` | varchar(10) | no | `pending`, `done`, `cancelled`, `failed`. |
| `captured_values` | jsonb | yes | For `publish`: the draft values when set (or last recaptured). Migrated by schema changes. |
| `captured_slug` | varchar(100) | yes | For `publish`. |
| `failure_code` | varchar(64) | yes | For `failed`. |
| `created_by`, `created_at_utc` | uuid, timestamptz | no | Who set (or last moved) it. |
| `completed_at_utc` | timestamptz | yes | |

Partial unique index `(item_id, action) WHERE state = 'pending'`.

### 1.8 + `item_history`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK, UUID v7 (orders entries). |
| `site_id` | uuid | no | Global filter. |
| `item_id` | uuid | no | FK → `items` cascade (FR-008: deleted with the item). |
| `kind` | varchar(24) | no | §3.5. |
| `version_number` | int | yes | For `published`, `rolledBack`. |
| `schedule_action` | varchar(10) | yes | For schedule kinds. |
| `due_at_utc` | timestamptz | yes | For `scheduled`, `rescheduled`. |
| `failure_code` | varchar(64) | yes | For `scheduleFailed`. |
| `at_utc`, `by` | timestamptz, uuid | no | `by` is who set the schedule for scheduled runs. |

Append-only. No values, names or slugs (FR-032).

### 1.9 Outbox

`cms.outbox_messages` (014) also carries the item events (§4) and the delayed
`Cms.ItemScheduleDue` messages.

---

## 2. Documents

- **Item values document**: unchanged from 014 contract §3. Used by `items.draft`,
  `items.published`, `item_versions.values` and `item_schedules.captured_values`.
- **Attention report** (`items.attention`, and in every item response as `report`):

```json
{ "problems": [
  { "fieldId": "f1", "problem": "missing_required" },
  { "fieldId": "f6", "problem": "too_long", "limit": 256 },
  { "fieldId": "f4", "problem": "media_missing" } ] }
```

Problem codes: research IT-05. `slug` problems use `"fieldId": "slug"`.

---

## 3. Domain rules and state changes

### 3.1 `Item` (aggregate root)

| Operation | Preconditions (besides gate and revision, IT-06) | Effects |
|---|---|---|
| `Create(collection, slug, values, actor)` | No malformed value; slug format | `revision` 1, `status` draft, history `created`, `ItemCreated` |
| `SaveDraft(slug, values, schemaRevision, actor)` | `schemaRevision` current; no malformed value | Draft replaced; status recomputed; `ItemDraftSaved` |
| `Duplicate(actor)` | — | New item: draft copy, name with " (copy)" (shortened to fit), slug `-copy[-n]`; no versions, schedules or published data; `ItemCreated` |
| `Publish(actor, cancelSchedule)` | Report empty (FR-020); `cancelSchedule` when a scheduled publish is pending | New version (`publish`); `published`, `published_slug`, `live_version`, `published_at_utc/by`; retention; pending scheduled publish cancelled; `ItemPublished` |
| `Unpublish(actor, cancelSchedule)` | Published; `cancelSchedule` when a scheduled publish is pending | Published values and slug cleared; `live_version` null; pending schedules cancelled; `ItemUnpublished` |
| `Rollback(number, actor, cancelSchedule)` | Version kept; its values and slug pass today's checks | New version (`rollback`, `restored_from`); as publish |
| `CopyToDraft(number, actor)` | Version kept | Draft values and slug replaced; `ItemDraftSaved` |
| `Schedule(action, dueAt, actor)` | `dueAt` in the future; publish: report empty, captures draft; unpublish: published or publish pending, and later than a pending publish | Schedule created or moved (`revision` + 1); due message; history `scheduled`/`rescheduled` |
| `CancelSchedule(action, actor)` | Pending schedule exists | `cancelled`; history `scheduleCancelled` |
| `RunSchedule(id, revision)` | Pending at that revision (claimed by guarded update) | As publish (`scheduled`) or unpublish; on domain refusal: `failed` + `scheduleFailed`, item re-indexed |
| `Delete(actor)` | No `item_references` row targets it | Row deleted; slugs, references, media, versions, schedules, history cascade; `ItemDeleted` |

`status` after every operation: §3.3. Every operation ends with `ItemIndexer.Refresh`
(IT-11).

### 3.2 Validation thresholds (IT-05)

| Value state | Save draft | Publish / schedule publish / rollback |
|---|---|---|
| Malformed (wrong shape, unknown field or option, reference to a missing item, another collection or itself, duplicate or > 100 references, media of another site) | refused `cms.invalid_item` | refused |
| Missing required value | stored, reported | refused `cms.item_needs_attention` |
| Breaks a setting (`maxLength`, `min`/`max`, `decimals`, `accept`) | stored, reported | refused |
| Media file deleted | stored, reported (`media_missing`) | refused |
| Slug malformed or taken | refused `cms.invalid_item` / `cms.item_slug_taken` | refused |

A reference to an existing item that is not published is valid (clarification Q4); the
publish response lists such references in `unpublishedReferences`.

### 3.3 Status (IT-04)

```text
            publish / rollback / scheduled publish
  ┌───────┐ ─────────────────────────────────────► ┌───────────┐
  │ draft │                                        │ published │
  └───────┘ ◄───────────────────────────────────── └───────────┘
      ▲              unpublish (manual/scheduled)      │    ▲
      │                                    save draft  │    │ publish, or save that makes
      │ unpublish                       (differs)      ▼    │ draft equal to published
      │                                          ┌─────────┐
      └───────────────────────────────────────── │ changed │
                                                 └─────────┘
```

### 3.4 Schedules

```text
pending ──run ok──► done
pending ──run refused──► failed      (history scheduleFailed; item re-indexed)
pending ──cancel / manual publish, unpublish, rollback──► cancelled
pending ──reschedule──► pending (revision + 1; old due message is a no-op)
```

A manual unpublish also cancels a pending scheduled unpublish. A scheduled unpublish of an
item that has since been unpublished by hand was cancelled then, so it never runs on a draft.

### 3.5 History kinds

`created`, `published`, `rolledBack`, `unpublished`, `scheduled`, `rescheduled`,
`scheduleCancelled`, `scheduleFailed`, `copiedToDraft`. A `published` or `unpublished`
entry that came from a schedule has `schedule_action` set to that schedule's action. Draft saves are
not history entries; `updated_at_utc`/`updated_by` show the last one.

### 3.6 Schema change effects (changes to 014 `ItemMigrator`, IT-12)

| Data | Conversions, clears, removals | Fill-ins | Indexer |
|---|---|---|---|
| `items.draft`, `items.published` | yes (014) | yes (014) | rewritten items; all items when title, required, settings or type changed |
| `item_schedules.captured_values` (pending) | yes | yes | — |
| `item_versions.values` | yes | **no** | `item_media.in_history` refreshed |

Rewritten items get `revision + 1`, so an open form saving afterwards gets
`cms.schema_changed` or `cms.item_conflict`, never a silent overwrite.

---

## 4. Integration events (Cms outbox, IT-17)

| Event | Payload |
|---|---|
| `Cms.ItemCreated` | `siteId`, `collectionId`, `itemId`, `actorUserId` |
| `Cms.ItemDraftSaved` | `siteId`, `collectionId`, `itemId`, `actorUserId` |
| `Cms.ItemPublished` | `siteId`, `collectionId`, `itemId`, `versionNumber`, `kind`, `slugChanged`, `actorUserId` |
| `Cms.ItemUnpublished` | `siteId`, `collectionId`, `itemId`, `scheduled`, `actorUserId` |
| `Cms.ItemDeleted` | `siteId`, `collectionId`, `itemId`, `wasPublished`, `actorUserId` |
| `Cms.ItemScheduleDue` *(internal)* | `siteId`, `scheduleId`, `revision` — `available_at_utc` = due time |

Consumed in this feature: `Media.FileDeleted`, `Media.FileChanged` (re-index items using the
file) and `Cms.ItemScheduleDue`.

---

## 5. Capabilities (IT-16)

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `cms.items.edit` | Site | ✓ | ✓ | ✓ |
| `cms.items.publish` | Site | ✓ | ✓ | ✓ |

Reads use `cms.collections.view` (014).

---

## 6. Error codes

| Code | Status | Extensions | When |
|---|---|---|---|
| `cms.item_not_found` | 404 | | Unknown id, another collection's or site's item |
| `cms.invalid_item` | 400 | `report` | A malformed value or slug (§3.2) |
| `cms.item_slug_taken` | 409 | `suggestion` | Slug used by another item's draft or published slug |
| `cms.item_conflict` | 409 | `currentRevision` | Item changed since the request's `revision` |
| `cms.schema_changed` | 409 | `schemaRevision` | The form's schema is out of date (IT-07) |
| `cms.items_being_updated` | 409 | | Schema change in progress (014) |
| `cms.item_needs_attention` | 409 | `report` | Publish, schedule publish or rollback with problems |
| `cms.version_not_publishable` | 409 | `report` | Rollback of a version that fails today's checks |
| `cms.version_not_found` | 404 | | Version not kept |
| `cms.item_not_published` | 409 | | Unpublish, or schedule unpublish, of an item with nothing published or pending |
| `cms.item_referenced` | 409 | `referrers[]` (`itemId`, `itemName`, `collectionId`, `collectionName`, `fieldId`, `fieldName`, `inDraft`, `inPublished`, `inScheduled`), `total` | Delete refused (FR-008) |
| `cms.schedule_pending` | 409 | `schedule` | Manual publish, unpublish or rollback without `cancelSchedule` while a scheduled publish is pending |
| `cms.schedule_in_past` | 400 | | Due time not in the future |
| `cms.schedule_order` | 400 | | Unpublish not later than the pending publish |
| `cms.schedule_not_found` | 404 | | Nothing pending to cancel |
| `cms.invalid_query` | 400 | `parameter` | Unknown filter or sort field, wrong type for a filter, term over 100 characters, bad page size |
| `cms.item_too_large` | 413 | | Request body over 1 MiB |

Messages never contain item values (Principle IX).
