# Data Model: CMS Collections and Schema Builder

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

All tables are PostgreSQL, created through EF Core migrations in the new `cms` schema
(CS-01, CS-21), with snake_case columns. Every table except the outbox tables carries
`site_id` and a global query filter (Principle V). Everything here is new.

---

## 1. Cms schema (`cms`)

### 1.1 `collections`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK. UUID v7. |
| `site_id` | uuid | no | Global filter. |
| `name` | varchar(100) | no | Display name, trimmed, no control characters. |
| `slug` | varchar(64) | no | Address name, `^[a-z][a-z0-9-]{0,63}$`. Unique `(site_id, slug)`. |
| `description` | varchar(500) | yes | |
| `schema` | jsonb | no | The schema document (CS-02). Starts as `{ "schemaVersion": 1, "fields": [] }`. |
| `schema_version` | int | no | Always equal to `schema->'schemaVersion'`. |
| `revision` | int | no | ≥ 1. EF concurrency token. Incremented by every saved change (FR-013, FR-022). |
| `next_field_number` | int | no | Next `f{n}`. Starts at 1 and only grows (CS-02). |
| `next_option_number` | int | no | Next `o{n}`. Starts at 1 and only grows. |
| `created_at_utc`, `created_by` | timestamptz, uuid | no | |
| `updated_at_utc`, `updated_by` | timestamptz, uuid | no | Last saved change. |

Indexes: unique `(site_id, slug)`; `(site_id, name)` for the list.

### 1.2 `items`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | uuid | no | PK. UUID v7. |
| `site_id` | uuid | no | Global filter. Always equals its collection's. |
| `collection_id` | uuid | no | FK → `collections(id)` `ON DELETE CASCADE`. |
| `draft` | jsonb | no | Item values document (CS-03): `{ "schemaVersion": 1, "values": {…} }`. |
| `published` | jsonb | yes | Same shape. Null until #15 publishes the item. |
| `created_at_utc`, `updated_at_utc` | timestamptz | no | |

Indexes: `(collection_id, id)`. **#15 adds** columns (address name, status, publishing
times, authors) and indexes through its own migration.

### 1.3 `collection_references`

| Column | Type | Null | Notes |
|---|---|---|---|
| `site_id` | uuid | no | Global filter. |
| `source_collection_id` | uuid | no | FK → `collections(id)` `ON DELETE CASCADE`. |
| `field_id` | varchar(8) | no | A reference or multi-reference field of the source. |
| `target_collection_id` | uuid | no | FK → `collections(id)` **`ON DELETE RESTRICT`** (CS-11). Never equal to the source. |

PK `(source_collection_id, field_id)`. Index `(target_collection_id)`. Rows are replaced in
every save transaction, so they always match the stored schema.

### 1.4 `schema_changes`

| Column | Type | Null | Notes |
|---|---|---|---|
| `collection_id` | uuid | no | FK → `collections(id)` `ON DELETE CASCADE`. |
| `revision` | int | no | PK `(collection_id, revision)`. |
| `site_id` | uuid | no | Global filter. |
| `changed_at_utc` | timestamptz | no | |
| `changed_by` | uuid | no | |
| `summary` | jsonb | no | Array of change entries (§3.4). No item values or examples (FR-033). |

### 1.5 Outbox

`cms.outbox_messages` and `cms.processed_events`, created by the shared
`OutboxModelBuilder`, as in every module.

---

## 2. Documents

### 2.1 Schema document (contract v1)

Described in [contracts/collection-schema.md](./contracts/collection-schema.md) §2. In
short: `schemaVersion`, optional `titleFieldId`, and `fields[]` of `{ id, key, name, help,
required, type, …type settings }`.

### 2.2 Item values document (contract v1)

Described in contracts/collection-schema.md §3: `{ schemaVersion, values: { <fieldId>:
<value> } }`. A value's format is fixed by its field's type, and no key means no value.

---

## 3. Domain rules and state changes

### 3.1 `Collection` (aggregate root)

| Operation | Rules | Effects |
|---|---|---|
| `Create(site, name, slug, description, actor)` | Name and slug rules; site under 100 collections (CS-18) | Revision 1, empty schema, counters at 1, history entry `created`, `Cms.CollectionCreated` |
| `Propose(proposal)` → `SchemaDiff` | The proposal passes CS-04. Permanent ids must exist in the stored schema. Temporary ids map to new ones. | None (pure) |
| `Apply(diff, impact, confirmation, actor)` | `revision` matches. Confirmation digest present and equal when required (CS-08). No forbidden change with values (CS-07). Title field stays valid. | Schema replaced; temporary ids replaced (counters advance); `revision + 1`; references replaced; history entry; `Cms.CollectionChanged` |
| `Delete(actor)` | Lock held. No `collection_references` row targets it. | Row deleted (items, history and own references cascade); `Cms.CollectionDeleted` |

A save whose diff is empty changes nothing and returns the current state.

### 3.2 Field change classification (CS-07)

| Change | Rule | Item effect |
|---|---|---|
| Rename (name, key, help), move, option relabel or reorder, loosened setting | free | none |
| Field added | free without a fill-in, reported with one | fill-in written where given (FR-029) |
| Field removed | lossy if any item has a value, otherwise free | value deleted from draft and published |
| Required turned on | reported | fill-in written where given; otherwise counted `stillMissing` |
| Option removed | lossy if chosen anywhere | value deleted |
| Tightened `maxLength`, `min`, `max` or `accept` | reported | counted `nowInvalid`, kept |
| `decimals` lowered | lossy | rounded |
| Type change or `multiline`/`withTime`/`decimals` change listed in FR-026 | lossless or lossy per the table | converted per CS-07 |
| Any other type change, or a reference target change | forbidden, unless no item has a value | — |

### 3.3 Item write gate (CS-10)

**Every item write in any later feature (#15) MUST go through `CollectionWriteGate`.** The
gate takes `FOR SHARE NOWAIT` on the collection row in the writer's transaction, returns the
current schema and revision, and turns a lock failure into `cms.items_being_updated`.
Reviewers of #15 should check that no item write bypasses it.

### 3.4 History summary entry

```json
{ "kind": "typeChanged", "fieldId": "f6", "key": "reading-time", "name": "Reading time",
  "from": "text", "to": "number",
  "counts": { "converted": 180, "cleared": 12, "kept": 108 } }
```

`kind` is one of: `created`, `detailsChanged`, `fieldAdded`, `fieldRemoved`, `fieldRenamed`,
`fieldsReordered`, `requiredChanged`, `typeChanged`, `settingsChanged`, `optionAdded`,
`optionRemoved`, `optionRelabelled`, `optionsReordered`, `titleFieldChanged`. `counts` keys
are the outcomes of CS-07, present only when not zero.

---

## 4. Integration events (Cms outbox)

| Event | Payload | When |
|---|---|---|
| `Cms.CollectionCreated` | `siteId`, `collectionId`, `revision`, `actorUserId` | A collection was created |
| `Cms.CollectionChanged` | `siteId`, `collectionId`, `revision`, `schemaChanged`, `itemsRewritten`, `actorUserId` | A save produced a new revision |
| `Cms.CollectionDeleted` | `siteId`, `collectionId`, `actorUserId` | A collection and its items were deleted |

No consumers in this feature (CS-14).

---

## 5. Error codes

| Code | Status | Extensions | When |
|---|---|---|---|
| `cms.collection_not_found` | 404 | | Unknown id, or another site's collection |
| `cms.slug_taken` | 409 | | Address name already used in the site |
| `cms.too_many_collections` | 409 | `limit` | 100 per site reached |
| `cms.invalid_schema` | 400 | `rule`, `pointer` | The proposal breaks the contract or a code rule (CS-04). `rule` values are listed in contracts/collection-schema.md §5. |
| `cms.unsupported_schema_version` | 400 | `pointer` | `schemaVersion` not supported |
| `cms.proposal_too_large` | 413 | | Over 64 KiB |
| `cms.unknown_field_id` | 400 | `pointer` | A permanent id the collection does not have |
| `cms.unknown_option_id` | 400 | `pointer` | A permanent option id the field does not have |
| `cms.unknown_target_collection` | 400 | `pointer` | Reference target missing or in another site |
| `cms.type_change_not_allowed` | 400 | `fieldId`, `from`, `to` | Forbidden pair while values exist |
| `cms.target_change_not_allowed` | 400 | `fieldId` | Target changed while values exist |
| `cms.invalid_fill_in` | 400 | `fieldId`, `pointer` | A fill-in for a type that takes none, or one not valid for the field |
| `cms.revision_conflict` | 409 | `currentRevision` | The proposal was based on an older revision |
| `cms.collection_busy` | 409 | | Another save, a delete or an item write holds the collection |
| `cms.confirmation_required` | 409 | `preview` | A lossy change without confirmation |
| `cms.preview_outdated` | 409 | `preview` | The confirmation digest no longer matches |
| `cms.collection_referenced` | 409 | `references[]` (`collectionId`, `collectionName`, `fieldId`, `fieldName`) | Delete refused |
| `cms.items_being_updated` | 409 | | Item write during a schema change (gate, for #15) |

`CmsErrorResults` maps these the way `PagesErrorResults` does. Messages never contain item
values.
