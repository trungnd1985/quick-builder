# Research: CMS Items

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Decisions IT-01 … IT-24. Each states the decision, why, and what else was considered. The
technical context had no open unknowns once these were made (see "Resolved unknowns").

---

## IT-01 Items stay in the Cms module, as an aggregate of their own

**Decision**:
- `Item` (already a stored type from 014) becomes an **aggregate root** in the Cms module,
  with `ItemVersion`, `ItemSchedule` and `ItemHistoryEntry` as its parts. It enforces the
  rules that need no database: status, slug format, the publish checks over a validation
  report, schedule ordering, retention, rollback eligibility.
- Every item operation is a vertical slice under `Features/Items/…`.
- `Collection` stays its own aggregate. An item refers to its collection by id and reads its
  schema only through `CollectionWriteGate` (014 CS-10) when writing, or `CollectionReads`
  when reading.

**Rationale**: the constitution names Cms as the owner of collection items and requires a
rich domain model there. Items and collections change at different rates and are locked
differently (a share lock per item write against an update lock per schema change), so they
must not be one aggregate.

**Alternatives considered**:
- **A separate `Items` module**: items and schema migration must change in one transaction
  (014 FR-021), and the constitution forbids transactions across modules.
- **Items inside the `Collection` aggregate**: loading 10,000 items to save one is absurd,
  and every item write would conflict with every other on the collection's revision.

---

## IT-02 Storage: columns beside the values documents

**Decision**: one additive migration `AddItems` to the `cms` schema (data-model §1):
- **`items` gains** `slug`, `published_slug`, `status`, `revision`, `name`,
  `search_text`, `attention`, `index_version`, `created_by`, `updated_by`,
  `published_at_utc`, `published_by`, `live_version`.
- **New tables**: `item_slugs`, `item_references`, `item_media`, `item_versions`,
  `item_schedules`, `item_history`.
- The values documents keep the 014 contract shape `{ schemaVersion, values }`. **The slug
  is a column, not part of the values document**, so the collection schema contract does not
  change (FR-003 still keeps the draft and the published slug apart: `slug` and
  `published_slug`).

**Rationale**: the list must search, filter, sort and page 10,000 items within 1 s
(SC-003). Name, status, attention and slug are read on every list row, so they are columns
with indexes rather than expressions over `jsonb`. Field values used for filters and sorts
stay in `jsonb` (IT-14).

**Alternatives considered**:
- **The slug inside the values document**: it would need a contract change (`itemValues`
  allows no other property), a golden-file update and a reader change in #16, for no gain.
- **A separate "item index" table**: one more join on every list query, and two rows to
  keep in step on every write.

---

## IT-03 Slugs: one table, so the database decides uniqueness

**Decision**:
- Format: `^[a-z0-9][a-z0-9-]{0,99}$` (FR-003, Edge Cases). `slugify.ts` (011) and its C#
  twin produce it from the item name; an empty or non-Latin name falls back to
  `item-<yyyymmddhhmmss>` (the creation-time fallback in Edge Cases).
- `cms.item_slugs (collection_id, slug citext, item_id)`, primary key
  `(collection_id, slug)`. Each item owns one row for its draft slug and, when published
  with a different slug, one for its published slug. Every write replaces the item's rows in
  its transaction.
- A unique violation maps to `cms.item_slug_taken`. The admin checks first, with a debounced
  `GET …/items/slug-availability?slug=` call, so the member sees the problem as they type,
  but only the database's answer counts.
- **Suggestion with a number**: when the suggested slug is taken, the admin asks for the
  next free one (`hello-world-2`, `-3`, …). The server answers in that one call.
- **Duplicate**: `<slug>-copy`, then `-copy-2` and so on, shortened to fit 100 characters.

**Rationale**: FR-003 makes a slug unique among every other item's draft *and* published
slug. One table with one key enforces exactly that, even under concurrent saves, with no
lock beyond the row insert. `citext` is already installed by other modules.

**Alternatives considered**:
- **Two unique indexes** (on `slug` and `published_slug`): they cannot stop one item's
  draft slug from equalling another item's published slug.
- **A check under the collection lock**: item writes hold only a share lock (014 CS-10), so
  two of them could both pass the check.

---

## IT-04 Item status and the "changed" flag

**Decision**:
- `status` ∈ `draft`, `published`, `changed` (FR-004), recomputed by the aggregate on every
  write and by the indexer after a schema migration (IT-11):
  - no published values → `draft`;
  - otherwise `published` when the draft values and slug equal the published ones (JSON
    value equality), and `changed` when they differ.
- Pending schedules and attention are columns of their own, so the list shows them beside
  the status, as FR-004 asks.

**Rationale**: a stored status is filterable and sortable without comparing documents at
query time. Equality rather than a "dirty" bit means that undoing an edit by hand returns
the item to `published`.

**Alternatives considered**:
- **A "draft changed since publish" bit**: never goes back once set, so it reports changes
  that are no longer there.

---

## IT-05 Validation: one report, two thresholds

**Decision**:
- `ItemValueValidator` (014) already classifies each value as `Valid`, `Empty`,
  `MissingRequired`, `NeedsAttention` or `Malformed`. A new `ItemValidation` builds an
  **item report** from it:
  - Per field: its state and a `problem` code (`malformed`, `missing_required`, `too_long`,
    `out_of_range`, `too_many_decimals`, `file_kind`, `unknown_option`, `media_missing`,
    `reference_missing`, `reference_wrong_collection`, `reference_self`,
    `duplicate_reference`, `too_many_references`, `unknown_field`).
  - Plus the database checks that 014 left out: media of the same site (through
    `MediaKinds`, 014 CS-12) and referenced items in the target collection, both in one
    query each for the whole item.
- **Save draft** (FR-013, FR-014; clarification Q2) refuses with `cms.invalid_item` when any
  field is **malformed**. Missing required values, values that break settings and missing
  media are stored, and the report becomes the item's `attention` column.
- **Publish** (FR-020) refuses with `cms.item_needs_attention` while the report has any
  problem at all, listing every field.
- `ItemValueValidator` gains the reference rules it lacks: a reference to the item itself
  (`reference_self`, FR-016), and the existence and collection of each referenced item.
- **The admin** runs the same contract definitions (`collectionSchema.ts`, 014 CS-04) and a
  TypeScript port of the settings checks as the member types (FR-012). The server's report
  is authoritative; the admin shows it after every save.
- **Golden examples** for item values: `docs/contracts/collection-schema/examples/items/`
  holds value documents with the expected report for each, run by both suites (like 014
  SC-006). This adds examples only, not a contract change.

**Rationale**: one report, computed the same way for saves, publishes, schema migrations
and media deletions, means the list's "needs attention" filter, the form's messages and the
publish refusal can never disagree.

**Alternatives considered**:
- **Enforce settings at every save** (clarify options B and C): declined in clarification Q2.

---

## IT-06 Writing an item: the gate, then one transaction

**Decision**: every item write (create, save, duplicate, delete, publish, unpublish,
schedule, cancel, rollback, copy to draft, and a scheduled run) follows the same steps,
through one `ItemWrites` service:
1. Begin a transaction.
2. `CollectionWriteGate.EnterAsync` (014 CS-10): `FOR SHARE NOWAIT` on the collection. A
   running schema change gives `cms.items_being_updated` (FR-006).
3. Load the item. Its `revision` is an EF concurrency token, and a mismatch with the
   request's `revision` gives `cms.item_conflict` with the current revision (FR-005).
4. A draft save also states `schemaRevision`. A mismatch with the collection's
   `schema_revision` (IT-07) gives `cms.schema_changed`.
5. Validate (IT-05), apply the domain operation, and run the indexer (IT-11) to refresh
   slugs, references, media and derived columns.
6. Add the history entry and the outbox events, then commit.

**Rationale**: one path makes the write gate impossible to skip, which was 014's main risk
for this feature. An architecture test asserts that only `ItemWrites` and `ItemMigrator`
modify `Item`.

**Alternatives considered**:
- **Each slice opening its own transaction**: eleven copies of the same five steps, and
  each one a chance to forget the gate.

---

## IT-07 Telling a schema change from a details change

**Decision**: `collections` gains `schema_revision`: the collection revision at which the
schema document last changed. It is set by `Collection.Apply` when `schemaChanged` is true
(014 data-model §3.1) and backfilled to `revision` by the migration. Item forms send it, and
item saves compare it (IT-06 step 4).

**Rationale**: the collection's `revision` also goes up for a renamed collection or a
changed description. Refusing an item save for that would be wrong, since the form is still
right.

**Alternatives considered**:
- **Comparing a hash of the schema**: the same check, but nothing readable to show.
- **Never refusing**: the server would then validate values from an old form against a new
  schema. That is safe, but the member would get confusing "unknown field" errors instead of
  being told the fields changed.

---

## IT-08 Publishing, unpublishing and versions

**Decision**, following 012 PR-02, PR-03 and PR-15:
- **Publish** (FR-019): copies the draft values and slug into `published` and
  `published_slug`, writes an `item_versions` row (`number`, values, slug, kind `publish`,
  member, time), points `live_version` at it, and sets `published_at_utc` and
  `published_by`. All in the write transaction.
- **Concurrent publishes** (US2 scenario 7): the item's concurrency token serialises them.
  The second gets `cms.item_conflict`, and the admin retries once with the new revision, so
  each produces its own complete version, one after the other.
- **Unpublish** (FR-021): clears `published`, `published_slug` and `live_version`, keeps
  every version, and logs `unpublished`. It also cancels a pending scheduled unpublish, which
  would otherwise have nothing to do. The member is told how many published items refer to
  it, from `item_references` (IT-10).
- **Rollback** (FR-022): re-validates the version's values against today's schema and data
  (IT-05). It is refused with `cms.version_not_publishable` and the report on any problem,
  including a slug now used by another item. Otherwise it publishes the content as a new
  version of kind `rollback` with `restored_from`.
- **Copy to draft**: replaces the draft values and slug, never publishes, and is always
  allowed. The values are well-formed by construction, because versions are migrated (IT-12).
- **Retention** (FR-023): in the same transaction, versions beyond the 50 newest are
  deleted, never the live one.
- **Manual actions cancel a pending scheduled publish** (FR-027): the request must carry
  `cancelSchedule: true` when one is pending, otherwise it is refused with
  `cms.schedule_pending` so the admin can warn first. History records `scheduleCancelled`.

**Rationale**: the same model as pages, so members and #16 meet one behaviour. Versions are
full copies, not diffs, so a rollback needs no reconstruction.

---

## IT-09 Scheduling: delayed messages in the Cms outbox

**Decision**, reusing 012 PR-14 unchanged in shape:
- `cms.item_schedules`: `action` (`publish` or `unpublish`), `due_at_utc`, `revision`,
  `state` (`pending`, `done`, `cancelled`, `failed`), `failure_code`, the captured values
  and slug for a publish, and who set it. A partial unique index allows one pending
  schedule per item and action (FR-024).
- **Setting a publish schedule** checks the draft against the publish checks *then*
  (FR-025, US6 scenario 1), captures it, and writes `Cms.ItemScheduleDue { scheduleId,
  revision }` to the Cms outbox with `available_at_utc` set to the due time, all in one
  transaction. A scheduled unpublish must be later than a pending scheduled publish, and
  needs a published item or a pending publish (US6 scenario 2).
- **Running it**: the Cms handler claims the schedule with a guarded update, then runs the
  same `ItemWrites` operation as a manual publish or unpublish, with kind `scheduled`.
  - A **domain refusal** (for example the captured content now needs attention after a
    schema change made a field required) marks the schedule `failed` with the code,
    records `scheduleFailed` in history, and refreshes the item's attention so it is shown
    as needing attention (FR-026, US6 scenario 5).
  - **`cms.items_being_updated`** (a schema change is running) is not a decision. The
    handler throws, so the outbox retries with its back-off, and the schema change (under
    30 s, 014 SC-003) is over before the third attempt.
- **Exactly once** (SC-006): `FOR UPDATE SKIP LOCKED` in the dispatcher, plus the guarded
  claim. Poll interval 5 s.
- **Draft differs from scheduled** (FR-025): the item response carries
  `scheduledDiffersFromDraft`, computed by JSON equality like the status.
- **Times** are UTC on the wire. The admin converts from and to the member's time zone and
  names it (Edge Cases), using the input extracted from 012's `ScheduleDialog` (IT-20).
  A time not in the future is refused with `cms.schedule_in_past`.

**Rationale**: the product's one background mechanism already gives claiming across
instances, restart safety and retries (Principle IV), and 012 proved it.

**Alternatives considered**:
- **Using the Pages schedule tables** for items: that would cross a module boundary, and a
  scheduled item publish must share a transaction with the Cms write gate.
- **A polling worker**: a second scheduler, rejected for the same reasons as in 012.

---

## IT-10 References between items, and deleting an item

**Decision**:
- `cms.item_references (site_id, source_item_id, field_id, target_item_id, in_draft,
  in_published, in_scheduled)`, primary key `(source_item_id, field_id, target_item_id)`,
  index on `target_item_id`. A row is written for each distinct reference in the draft, the
  published values and the captured content of a pending scheduled publish. A reference to
  the item itself cannot be saved (FR-016), so none is stored.
- **Foreign keys**: the source cascades, and the target uses `ON DELETE NO ACTION`. So:
  - deleting an item that something refers to fails in the database, even when the
    reference was added in a concurrent transaction (FR-008);
  - deleting a whole collection (014) still works: the collection delete first removes the
    reference rows of its own items, then cascades. *(Corrected during implementation: the
    plan expected `NO ACTION` alone to allow it, but PostgreSQL checks the key on the
    cascaded item delete before it cascades to the reference rows.)*
- **Delete** (FR-008) first reads the referrers. When there are any, it is refused with
  `cms.item_referenced` and the list (up to 50, with the total): item name, collection
  name, field name, and `inDraft` / `inPublished` / `inScheduled`. A foreign-key violation
  at commit (the race) maps to the same error, re-read.
- **Unpublish warning** (FR-021, US2 scenario 6): `GET …/items/{id}/referrers?published=true`
  returns the count the admin shows before confirming.
- **References in kept versions are not tracked.** A version that refers to a deleted item
  cannot be rolled back (IT-08, US7 scenario 4), but can still be copied into the draft,
  where the save then reports the reference as `reference_missing`.

**Rationale**: the same pattern as 014 CS-11, where the database decides races. Clarification
Q3 chose refusing over clearing.

**Alternatives considered**:
- **Scanning `jsonb` for the item id on delete**: a sequential scan of every item of every
  collection that targets this one, and it cannot stop a concurrent insert.

---

## IT-11 One indexer keeps everything derived in step

**Decision**: `ItemIndexer.Refresh(item, schema)` computes, from the item's draft,
published values and pending captured content:
- `name`: the title field's draft value, or null;
- `search_text`: `SearchText.Normalise(name + " " + slug)` (IT-13);
- `attention`: the IT-05 report of the draft, as `jsonb`, or null when there is nothing to
  report;
- `status` (IT-04);
- the item's `item_slugs`, `item_references` and `item_media` rows;
- `index_version`, the current indexer version.

It runs:
- on **every item write** (IT-06);
- on **every item rewritten by a schema migration** (`ItemMigrator`, 014 CS-09), and on
  **every item of the collection** when the saved diff changes the title field, the
  `required` flag, settings or a type. Those change `name` or `attention` without changing
  any value;
- on **`Media.FileDeleted` and `Media.FileChanged`**: a Cms handler re-indexes the items
  that `item_media` lists for that file, so a deleted image puts its items in "needs
  attention" (Edge Cases) and a replaced file's new kind is re-checked against `accept`;
- at **start-up**, through `ItemIndexBackfillService`, for items whose `index_version` is
  below the current one. This covers the items 014 stored (IT-22) and any future change to
  what is derived. It follows the pattern of 013's `SearchTextBackfillService`.

**Rationale**: every derived thing in one function means one place to test, and a list
that can never disagree with the form.

---

## IT-12 Schema changes now migrate versions and scheduled content too

**Decision**: `ItemMigrator` (014) applies the same `FieldPlan`s:
- to the **draft and published values** (as in 014);
- to the **captured values of pending scheduled publishes** (FR-025);
- to **every kept version** (`item_versions`).

The rules are the same, with one exception: **fill-in values are written to drafts,
published values and pending scheduled content, but not to versions**, which record what
was published.

Versions and schedules are loaded in batches of 500, like items, and only those holding a
value in a field the plans change are loaded. A `jsonb` key-existence filter does that
(`values ?| array[fieldIds]`, through Npgsql's `EF.Functions.JsonExistAny` on the `values`
element; if that translation is not available on a nested element, the migration filters
in memory and only the read cost grows). A rename or reorder still writes nothing
(014 SC-002). The preview's counts stay per item (014 FR-023). The
confirmation text adds "including published versions kept for rollback".

**Rationale**:
- **No hidden copy**: 014 FR-024 forbids keeping removed or cleared values anywhere. Kept
  versions would be exactly such a copy, so they must be migrated too.
- **Well-formed versions**: every stored values document stays well-formed in the current
  schema, which keeps rollback and copy-to-draft simple (IT-08).

**Alternatives considered**:
- **Migrating versions lazily at rollback**: the removed values would still be on disk in
  the meantime, which breaks 014 FR-024.
- **Deleting versions on a lossy schema change**: rollback would disappear without warning.

**Cost**: a collection of 10,000 items with on average 3 kept versions rewrites up to 40,000
documents for a change that touches every field value. `ItemMigrationPerformanceTests`
measures this against 014 SC-003 (30 s). If it fails there, the recorded fallback is to
migrate versions in a second phase after the item commit, under the same lock.

---

## IT-13 Search: a normalised column and `LIKE`, as in the media library

**Decision**:
- **What is searched** (clarification Q5): name and slug only. `search_text` holds both
  normalised. The term is normalised the same way and matched with
  `LIKE '%term%' ESCAPE '\'`, filtered by `collection_id`.
- **`SearchText` moves** from `QuickBuilder.Media/Domain` to
  `QuickBuilder.Shared.Kernel/Text`, unchanged, and Media uses it from there. It is a pure
  text function with no module concept, and two modules now need the same folding (013
  research MV-03 explains why it is a table and not `string.Normalize`).
- Terms are limited to 100 characters (`cms.invalid_query`).

**Rationale**: 013 measured the same approach on 10,000 rows well under 1 s with no
extension. Within one collection the scan is no larger.

**Alternatives considered**:
- **`pg_trgm` with `unaccent`**: two more extensions for no measured need, as 013 found.
- **Copying `SearchText` into Cms**: two fold tables that could drift.

---

## IT-14 Filters and sorts on field values, through EF Core

**Decision**:
- **Sources**: filters and sorts on field values read the **draft** values, which are what
  members edit and what the list shows. Status, attention and schedule filters read
  columns.
- **Filters** (FR-029), combined with AND:
  - `status=draft|published|changed`;
  - `attention=true`;
  - `scheduled=publish|unpublish|any`;
  - `field.<fieldId>=<value>`:
    - **option**: equals an option id;
    - **boolean**: `true` matches `true`; `false` matches `false` or no value;
    - **reference**: equals an item id;
    - **multi-reference**: contains an item id, through `EF.Functions.JsonContains`.
  - Field ids are checked against the schema: an unknown field, or a field type that cannot
    be filtered, gives `cms.invalid_query`.
- **Sorts**:
  - **Columns**: `name`, `slug`, `status`, `createdAt`, `updatedAt`, `publishedAt`.
  - **Number fields**: `field.<id>`, read through Npgsql's `JsonElement` translation
    (`GetProperty(...).GetDecimal()`).
  - **Date fields**: sorted as strings. Contract dates (`YYYY-MM-DD`, UTC `…Z`) sort
    correctly as text.
  - **Order**: items without a value come last in both directions (`NULLS LAST`), with `id`
    as the final tie-breaker so the order is stable (FR-029).
- **Paging**: numbered pages, `page` (from 1) and `pageSize` ∈ {25, 50, 100}, with
  `OFFSET`/`LIMIT` and a `COUNT(*)` of the filtered set.
- **Indexes**: `(collection_id, updated_at_utc DESC, id)` for the default order, and
  `(collection_id, left(name, 256))`, `(collection_id, slug)`, `(collection_id, status)`. Field-value
  sorts scan the collection's rows, at most 10,000.

**Rationale**:
- **Numbered pages**: FR-028 and FR-030 ask for page numbers, a total count and a page
  number in the address, and at 10,000 rows `OFFSET` is cheap.
- **Stable order**: the `id` tie-breaker keeps the order stable, so a page is the same on
  reload. US3 scenario 5 is about moving between pages, not about rows inserted meanwhile.
- **No raw SQL**: everything is expressible in EF Core, so no `FromSql` is needed beyond 014's
  lock queries.

**Alternatives considered**:
- **Keyset paging** (as 013): no page numbers and no "go to page 7", which FR-028 asks for,
  and keys over `jsonb` expressions per sort are complex.
- **Expression indexes per field**: field ids are per collection, so indexes would be
  created and dropped by schema changes. That is not needed at 10,000 rows.

---

## IT-15 Media usage by items

**Decision**:
- `cms.item_media (site_id, item_id, media_id, in_draft, in_live, in_scheduled,
  in_history)`, maintained by the indexer (IT-11). `in_history` comes from kept versions
  other than the live one, refreshed when a version is added or removed.
- **A new reader contract**: `QuickBuilder.Cms.Contracts.Media.IItemMediaUsageReader`, with
  `SummariseAsync(siteId, mediaIds)` and `ListAsync(siteId, mediaId)`. It returns places of
  kind `item`, with the item name, collection id and name, and the four flags.
- **Media** references `Cms.Contracts` and adds item places to what it already reads from
  Pages:
  - **Library**: the usage counts in the library and `UsageList.vue` include items.
  - **Delete and replace**: a file used by a published item counts as "in live use". As
    for pages, deleting it then needs `publishing.manage` (013 MV-06). Any use needs the
    in-use confirmation.
  - **Who sees item places**: only members with `cms.collections.view`, which every site
    role holds.

**Rationale**: 014 deferred item media usage to this feature. The reader pattern is 013's.
Keeping the live-use rule as it is avoids changing a security decision in another feature.
Since every role can view items, nobody loses information.

**Alternatives considered**:
- **Generalising `IMediaUsageReader` in `Pages.Contracts`**: the interface lives in Pages
  and describes pages and components, and making Cms implement a Pages contract would
  invert ownership.
- **Letting Editors delete files that only published items use**: a change to 013's rule,
  outside this feature's scope.

---

## IT-16 Authorization

**Decision**: two new site-scoped capabilities, both held by Designer and Editor
(Administrator holds all):

| Capability | Allows |
|---|---|
| `cms.items.edit` | create, save, duplicate, delete, copy a version to the draft |
| `cms.items.publish` | publish, unpublish, schedule, cancel a schedule, roll back |

Reading (list, get, history, referrers, slug availability, lookup) needs
`cms.collections.view` (014). A user without a role gets the same 404 as for a site that
does not exist (FR-001), through the existing `[RequireCapability]` filter.
`AuthorizationDeclarationTests` covers every new action.

**Rationale**: clarification Q1 gives every role every item action today. Two capabilities
rather than one keep "who may change what visitors see" separable later, for example for a
future Contributor role or approvals, without renaming a shipped capability.

---

## IT-17 Integration events

**Decision**: new events in `Cms.Contracts.Events`, written to the Cms outbox in the write's
transaction. Ids only, no values (FR-032):

| Event | Payload |
|---|---|
| `Cms.ItemCreated` | `siteId`, `collectionId`, `itemId`, `actorUserId` |
| `Cms.ItemDraftSaved` | `siteId`, `collectionId`, `itemId`, `actorUserId` |
| `Cms.ItemPublished` | `siteId`, `collectionId`, `itemId`, `versionNumber`, `kind` (`publish`, `scheduled`, `rollback`), `slugChanged`, `actorUserId` |
| `Cms.ItemUnpublished` | `siteId`, `collectionId`, `itemId`, `scheduled`, `actorUserId` |
| `Cms.ItemDeleted` | `siteId`, `collectionId`, `itemId`, `wasPublished`, `actorUserId` |

Plus the module-internal `Cms.ItemScheduleDue` (IT-09). No consumers in this feature: #16
subscribes. `ItemPublished`, `ItemUnpublished` and `ItemDeleted` with `wasPublished` are
the ones that change published data.

---

## IT-18 HTTP API

**Decision**: under `api/v1/cms/sites/{siteId}`, next to 014's routes. The full definition
is in [contracts/items-api.yaml](./contracts/items-api.yaml):
- **Items of a collection**, under `collections/{collectionId}/items`:
  - `GET` (list, IT-13, IT-14) and `POST` (create);
  - `GET /slug-availability`;
  - `GET`, `PUT` (save draft) and `DELETE /{itemId}`;
  - `POST /{itemId}/duplicate`, `/publish` and `/unpublish`;
  - `PUT` and `DELETE /{itemId}/schedules/{action}`;
  - `GET /{itemId}/history`, `GET /{itemId}/referrers`;
  - `POST /{itemId}/versions/{number}/rollback` and `/copy-to-draft`.
- **`POST items/lookup`**: given up to 200 item ids, returns each one's name, slug, status
  and collection. The form shows chosen references with it, and it is site-wide because a
  form shows references into several collections.
- **Shapes**:
  - Every write takes the item `revision`, and returns the full item: draft, published,
    report, status, schedules, `revision` and `schemaRevision`.
  - Request bodies are capped at 1 MiB: 100 fields of rich text at the 20,000-character
    limit fit, with room for markup.
  - Duplicate JSON properties are refused, as in 014.
- **Errors**: problem details with `errorCode`, listed in data-model §6.

---

## IT-19 The item list and form in the admin

**Decision**:
- **Routes**:
  - `/sites/:siteId/collections/:collectionId/items` (list);
  - `/items/new`;
  - `/items/:itemId` (form, with history in a side panel).
  The collection builder (014) gains an "Items" tab, and the Collections list links each
  collection to its items.
- **List** (`ItemListView.vue`):
  - **Query in the address** (FR-030): search, filters, sort, page and page size live in
    the route query, so a view can be reloaded and shared, and returning from an item
    restores it.
  - **Layout**: a table with a sortable header that uses `aria-sort`, a filter bar, and a
    pager with page-size choice and a total.
  - **Search**: debounced by 300 ms.
- **Form** (`ItemFormView.vue`):
  - **Generated from the schema** (FR-009): `ItemField.vue` picks one input per type.
    - **Text**: `TextInput` (single line or several lines), showing the remaining length.
    - **Rich text**: `RichTextInput`, a standalone TipTap editor built on the editor's
      `tiptap.ts`, `RichTextToolbar.vue` and `toContractRichText.ts` (011), so values stay
      within `rtDoc`.
    - **Number**: `NumberInput`.
    - **Boolean**: `BooleanSwitch`.
    - **Date**: `DateInput` (date, or date and time in the member's zone).
    - **Image and file**: `MediaInput`, which wraps the editor's `MediaControl` and
      `MediaBrowser` (013), limited by `accept`.
    - **Option**: `OptionSelect`.
    - **Reference and multi-reference**: `ReferencePicker` and `MultiReferencePicker`.
  - **Behaviour**:
    - **Slug**: follows the name until the member edits it (IT-03).
    - **Errors**: problems from the local check and from the server report are shown and
      announced per field (`aria-describedby`, `aria-invalid`, plus a summary with links to
      the fields) (FR-010).
    - **Leaving**: the unsaved-changes guard uses the router's leave hook and
      `beforeunload` (FR-011).
    - **Conflicts**: a conflict or a schema change shows the editor's `ConflictBanner`
      pattern. On `cms.schema_changed`, the form reloads the schema, keeps the values of
      fields that still exist and highlights changed fields (Edge Cases).
- **Publishing**:
  - `ItemPublishBar.vue`: Save draft, Publish (with the unpublished-references warning,
    FR-020), Unpublish (with the referrer count, FR-021), Schedule, Duplicate and Delete.
  - `ItemStatusBadge.vue` shows status, schedule and attention.
  - `ItemHistoryPanel.vue` lists versions and events, with rollback and copy to draft.
  - `ItemScheduleDialog.vue` sets and cancels schedules.
  - `DeleteItemDialog.vue` shows the referrers on refusal.

**Rationale**: reuse wherever 011 to 014 already built the piece: rich text, media picking,
conflict banners, drag and drop, slugify and time-zone input.

---

## IT-20 Reusing the publishing UI pieces

**Decision**:
- `ScheduleDialog.vue` (012) is tied to `publishingApi`. Its date-and-time input with time
  zone naming is extracted into `components/ZonedDateTimeInput.vue`, used by both the page
  and the item schedule dialogs. `ScheduleDialog` keeps its behaviour, and its tests still
  pass unchanged.
- `PublicationBadge.vue` stays page-only. Items get `ItemStatusBadge.vue`, because their
  statuses differ (`changed`, attention).

**Rationale**: share what is identical and keep what differs separate. Making the page
dialogs generic over two APIs would add indirection for one shared input.

---

## IT-21 Reference pickers

**Decision**:
- **Picker dialog** (`ReferencePickerDialog.vue`): a dialog over the target collection's
  item list endpoint (`pageSize=25`, search), showing name, slug and status. Options use
  `role="listbox"` with arrow-key movement, and Enter picks (FR-016).
- **Multi-reference** (`MultiReferencePicker.vue`): chosen items in an ordered list that
  reuses the editor's `useDragDrop` (011). Every drag has keyboard equivalents: "Move up",
  "Move down" and "Remove" buttons, plus Alt+Arrow keys. An `aria-live` region announces
  moves. Already chosen items and the item itself are disabled in the picker, and the
  limit of 100 is shown.
- **Names of chosen items** come from `POST items/lookup`. Unpublished ones show a "Not
  published" tag (FR-017), and missing ones show "Missing".

---

## IT-22 Upgrade from 014

**Decision**: migration `AddItems`:
- **Adds** the columns and tables. Existing items get:
  - a slug `item-` plus the first 12 hex digits of their id;
  - status `published` when `published` is not null, otherwise `draft`;
  - `revision` 1, `index_version` 0, `created_by` and `updated_by` set to the empty guid
    ("unknown");
  - for published items, one version 1 of kind `publish` from their published values, with
    `live_version` set to 1.
- **Sets** `collections.schema_revision` to `revision`.
- **At start-up**, `ItemIndexBackfillService` (IT-11) computes name, search text,
  attention, references, media and the final slug (from the name, made unique) for items at
  `index_version` 0.

`UpgradeFrom014Tests` restores a 014 snapshot that holds items and checks all of the above.

**Rationale**: forward-only and safe to re-run (Principle II). The SQL part needs no .NET
logic, and the .NET part (slugify, validation) runs where that logic lives. Only test and
demo items exist before this feature (014 CS-19).

---

## IT-23 Testing strategy

| Suite | Kind | Proves |
|---|---|---|
| `ItemTests`, `ItemValidationTests`, `ItemStatusTests`, `SlugRulesTests` | unit | Aggregate rules, report codes, status equality, slug format and suggestions |
| `ItemValueGoldenTests` + `itemValues.spec.ts` | golden, both sides | Admin and server give the same report for every example (FR-012) |
| `ItemApiTests` | integration | Every endpoint, capability, 404 for no role (FR-001), round trip of every type (SC-002) |
| `ItemDraftAndPublishTests` | integration | Draft never changes published data (SC-004); publish, unpublish, versions, retention |
| `ItemConcurrencyTests` | integration | Conflicts, concurrent publishes, slug races, write gate (FR-005, FR-006) |
| `ItemReferenceIntegrityTests` | integration | Delete refusal, race through the foreign key, self-references refused, collection delete still cascades (SC-005) |
| `ItemScheduleTests`, `ItemScheduledExactlyOnceTests` | integration | Capture, reschedule, cancel, failure on a now-invalid capture, two dispatchers, restart (SC-006) |
| `ItemRollbackTests` | integration | Rollback, refusal on today's checks, copy to draft |
| `ItemListQueryTests` | integration | Search with accents, every filter, sort and paging, stable order, unknown fields refused |
| `ItemSchemaMigrationTests` | integration | Versions and schedules migrated, fill-ins not written to versions, indexer re-run on title or required changes |
| `ItemMediaUsageTests` | integration | Media library usage includes items; deleting a file puts items in "needs attention" |
| `CmsSiteFilterTests` (extended), `CmsIsolationProbe` (extended) | integration | Every new table is site-filtered |
| `UpgradeFrom014Tests` | integration | IT-22 |
| `ItemListPerformanceTests`, `ItemMigrationPerformanceTests` | smoke, Performance category | SC-003 at 10,000 items; IT-12's cost |
| `ItemListView.spec.ts`, `ItemFormView.spec.ts`, `ItemField.spec.ts`, `ReferencePicker.spec.ts`, `MultiReferencePicker.spec.ts`, `ItemPublishBar.spec.ts`, `ItemHistoryPanel.spec.ts`, `DeleteItemDialog.spec.ts` | vitest + axe | Form generation, keyboard-only use, announcements, no serious or critical axe findings (SC-007) |
| Release smoke journey | smoke | + create an item and publish it |

The 80 % coverage gate applies to the item publish path, a critical path (publish).

---

## IT-24 Dependencies

**Decision**: no new dependency. TipTap (011), Npgsql's JSON translation, the outbox and
`citext` are already in use.

---

## Things deliberately not built

- Searching the content of text and rich text fields (clarification Q5, backlog).
- Bulk actions in the list, bulk import and export (#20), trash (#21), preview links (#22),
  approvals.
- Unique values for fields other than the slug (014 clarification Q4).
- Keyset paging or expression indexes for collections far beyond 10,000 items.
- Autosave and local recovery for the item form. The editor's recovery (011) is page-shaped,
  and the unsaved-changes guard covers accidental loss (FR-011).

## Configuration keys

None.

## Resolved unknowns

| Unknown | Resolved by |
|---|---|
| Where the slug lives, and how uniqueness across draft and published slugs is enforced | IT-02, IT-03 |
| How an item save knows the schema changed under its form, without refusing for a renamed collection | IT-07 |
| Whether kept versions and scheduled content follow schema changes, given 014 FR-024 | IT-12 |
| Accent-insensitive search speed at 10,000 items | IT-13 |
| Filters and sorts over `jsonb` values without raw SQL | IT-14 |
| How Media learns about item usage without a module cycle | IT-15 |
| What a scheduled run does while a schema change holds the collection | IT-09 |
| Deleting a collection while items refer to each other | IT-10 (`NO ACTION`) |
