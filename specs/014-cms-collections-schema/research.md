# Research: CMS Collections and Schema Builder

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #14

Decisions are numbered **CS-01 … CS-22** and referred to by number from the plan, the data
model and the contracts. Each one gives the decision, why, and what else was considered.

---

## CS-01 A new Cms module, and what it owns

**Decision**:
- This feature creates the `Cms` module the constitution names: `QuickBuilder.Cms.Contracts`
  and `QuickBuilder.Cms`, schema `cms`, its own `CmsDbContext`, migrations history table
  and outbox.
- **Cms owns** collections, their schemas, schema history, the item store (draft and
  published values, CS-05), schema changes and their migration (CS-06 … CS-10), and the
  references between collections (CS-11).
- **References out**:
  - `Pages.Contracts`: `SchemaSubsetValidator` (shared since 011) and the page schema, whose
    `rtDoc` definition is the rich text value format (CS-04).
  - `Media.Contracts`: `IPublicMediaReader`, to tell images from documents for the two rules
    that need it (CS-12).
  - `Sites.Contracts` and `Identity.Contracts`, as every module does.
- **Nothing references Cms yet.** #15 and #16 add their slices inside Cms or read through
  contracts added then.
- A rich domain model is required for Cms (Principle I): `Collection` is the aggregate root;
  `CollectionSchema`, `Field`, `FieldOption`, `SchemaProposal`, `SchemaDiff` and
  `FieldConversion` are domain types with unit tests.

**Rationale**: the constitution lists `Cms` as a module and puts `cms` among the schemas.
Items belong with their schema, because every schema change must rewrite them in the same
transaction (FR-021). No transaction may span modules.

**Alternatives considered**:
- *Collections in Pages*: Pages is already the largest module, and the constitution names
  Cms separately.
- *Items in a later module (#15)*: a schema change must migrate items atomically with the
  schema, so they must share a module and a database context.

---

## CS-02 The schema document, version 1

**Decision**: a collection's schema is one JSON document, stored as `jsonb`:

```json
{
  "schemaVersion": 1,
  "titleFieldId": "f1",
  "fields": [
    { "id": "f1", "key": "title", "name": "Title", "help": "", "required": true,
      "type": "text", "multiline": false, "maxLength": 256 },
    { "id": "f8", "key": "category", "name": "Category", "help": "", "required": false,
      "type": "option", "options": [ { "id": "o1", "label": "News" }, { "id": "o2", "label": "Guides" } ] }
  ]
}
```

- **Fields** are an ordered array, so the order is the array order (FR-008). Each field has
  the common properties `id`, `key`, `name`, `help` and `required`, then its `type` and that
  type's settings, flat beside them. Each type is one `oneOf` branch told apart by `type`,
  with `additionalProperties: false` (CS-04).
- **Type settings** (FR-010):
  - `text`: `multiline` (bool), `maxLength` (1–10,000).
  - `richText`: none.
  - `number`: `decimals` (0 = whole number, 1–6), optional `min` and `max`.
  - `boolean`: none.
  - `date`: `withTime` (bool).
  - `image`: none.
  - `file`: `accept`: `any`, `documents` or `images`.
  - `option`: `options`, 1–200 of `{ id, label }`.
  - `reference`: `target`, a collection id.
  - `multiReference`: `target`, a collection id.
- **Field ids** are `f` followed by a number (`f1`, `f2`, …). They come from a per-collection
  counter kept beside the document (`next_field_number`, data-model §1.1), so an id can never
  be reused, even after its field is removed (FR-006). **Option ids** are `o1`, `o2`, … from
  a second per-collection counter. Clients must treat both as opaque strings.
- **Temporary ids in proposals**: a new field or option sent by the admin has a temporary id
  `~1`, `~2`, … The server replaces them with permanent ids when it saves, and returns the
  mapping. `titleFieldId` and fill-in values may point at a temporary id. A stored document
  never contains one.
- **`titleFieldId`** names the field that names items (FR-011). It must be a single-line
  text field, and that field must be `required`. It is absent only while the schema has no
  single-line text field. The server designates the first one added automatically.
- **`schemaVersion`** is the contract version (Principle VI). The collection's **revision**
  (FR-013) is a column, not part of the document, and is returned beside it.

**Rationale**:
- **Sequential ids** guarantee "never reused" by construction, without a list of retired
  ids. They are short in every item's data (CS-03), and they make the hand-run seed and
  golden files predictable (CS-19).
- **Temporary ids** let one save add a field, make it the title field and give it a fill-in
  value, and let the preview refer to fields that do not exist yet.
- **Flat settings** keep the JSON Schema to one `oneOf` with no nested discriminator.

**Alternatives considered**:
- *UUIDs per field*: unique without a counter, but 36 characters as a key in every item for
  every field, and still needing server-side checks against forged or reused ids.
- *Server-assigned ids with id-less new fields*: the title field and fill-in values could not
  refer to a new field in the same save.
- *Settings in a nested `settings` object*: needs a second discriminator, which the subset
  validator does not support.

---

## CS-03 Item values, version 1

**Decision**: an item's draft and its published data are each one document:

```json
{ "schemaVersion": 1, "values": { "f1": "Hello", "f3": 7, "f8": "o2", "f9": { "mediaId": "0192…" } } }
```

- **Keyed by field id** (FR-014). A field with no value has no key. `null` is never stored,
  so "empty" has exactly one form.
- **Value formats**, one `$defs` entry each in the contract:

  | Type | Value |
  |---|---|
  | `text` | string, at most `maxLength` code points; no line breaks unless `multiline` |
  | `richText` | the page schema's `rtDoc` (page-schema.md §3, rule 9 length limit) |
  | `number` | JSON number; with `decimals` = 0, an integer within ±9,007,199,254,740,991; otherwise at most `decimals` places |
  | `boolean` | `true` or `false`; no key counts as `false` |
  | `date` | `"YYYY-MM-DD"`; with `withTime`, `"YYYY-MM-DDTHH:MM:SSZ"` (UTC, seconds) |
  | `image`, `file` | `{ "mediaId": uuid }`, a media file of the same site |
  | `option` | an option id of the field, e.g. `"o2"` |
  | `reference` | an item id (uuid) of the target collection |
  | `multiReference` | array of 1–100 distinct item ids |

- **Validity against settings** (`min`, `max`, `maxLength`, `accept`, required) is checked in
  code, not by the JSON Schema, because the settings are per collection. A stored value that
  breaks its field's settings is still well-formed. It is reported as **needing attention**,
  never rewritten (FR-027).
- **Derived states**: "missing a required value", "needs attention" and "incomplete" are
  computed from the schema and the values when asked for, and never stored. #15 shows them.
- **Media and reference values** are kept even when the file or item no longer exists
  (spec edge case). Media usage by items, and what happens to references when an item is
  deleted, come with #15.

**Rationale**: `{ mediaId }` matches the Image block's `media` property in the page schema,
and lets #15 add, for example, per-item alternative text without a new version. Seconds
precision and a fixed `Z` give each moment one textual form, so comparing and converting are
exact.

**Alternatives considered**:
- *A bare uuid for media values*: cannot grow without a breaking change.
- *Values without an envelope*: Principle VI requires `schemaVersion` on every stored
  document.

---

## CS-04 The contract, and validation on both sides

**Decision**:
- **The definition**: `docs/contracts/collection-schema/v1.schema.json`, in the same JSON
  Schema subset as the page schema. Its root is the **schema document** (CS-02). Its
  `$defs` hold one entry per item value type (`textValue`, `numberValue`, …) and the item
  envelope (`itemValues`). The companion `docs/contracts/collection-schema.md` describes the
  rules beyond the schema, the conversion table, limits and the versioning policy. Golden
  examples live in `docs/contracts/collection-schema/examples/{valid,invalid}/`.
- **Embedding**: `QuickBuilder.Cms.Contracts` embeds the file from where it lives (the
  `EmbeddedResource` pattern from 010 P-09). `CollectionSchemaContractTests` asserts the
  embedded bytes equal the file.
- **Rich text values** are checked against the page schema's `$defs/rtDoc`, so there is one
  rich text definition. `SchemaSubsetValidator` gains `ValidateDefinition(name, instance)`
  in C# and in its TypeScript port. This is an additive change to `Pages.Contracts`, with
  tests on both sides.
- **Server order**, stopping at the first failure and reporting a JSON Pointer:
  1. size (64 KiB for a proposal);
  2. JSON without duplicate property names;
  3. supported `schemaVersion`;
  4. the JSON Schema;
  5. rules in code (`CollectionSchemaRules`): unique keys ignoring case, reserved keys,
     unique option labels ignoring case, `min` ≤ `max`, `titleFieldId` valid, ids known to
     this collection or temporary, limits (FR-012, CS-18), targets in the same site
     (checked against the database by the handler).
- **The admin** imports the same file through `@contracts`, runs the TypeScript subset
  validator, then `collectionSchemaRules.ts`, a port of the code rules. Both sides run every
  golden example and must agree on the verdict, rule and pointer (SC-006). This is the same
  arrangement as the design tokens (011 ED-14).
- **Reserved keys**: `id`, `slug`, `status`, `created-at`, `updated-at`,
  `published-at`, `collection`, `site`, `schema-version`. They name item
  properties that #15 and #16 expose next to fields; reserving `slug` keeps it for #15's
  item address name. `title` is not reserved, because it is the natural key of the field
  that names items; bindings reach that field through `titleFieldId`.

**Rationale**: this follows Principle VI and the pattern already proven twice (page schema,
design tokens). It needs no new dependency, and validator drift is caught by golden files
on both sides.

**Alternatives considered**:
- *Copying the rich text `$defs` into the collection schema file*: two definitions to keep in
  step; a test could check them, but one definition is simpler.
- *A full JSON Schema library*: rejected in 010 P-09 for licence and size reasons. Nothing
  here needs more than the subset.

---

## CS-05 Storage

**Decision**: four tables in `cms` (data-model §1), plus the module's outbox tables:
- `collections`: details, the schema document, `schema_version`, `revision`, the two id
  counters and audit columns. The address name is unique per site.
- `items`: `id`, `site_id`, `collection_id` (FK, cascade), `draft` (jsonb, not null),
  `published` (jsonb, null until #15 publishes), and timestamps. #15 adds its columns
  (address name, status, publishing times) through its own migration.
- `collection_references`: one row per reference or multi-reference field that targets
  another collection. `target_collection_id` has `ON DELETE RESTRICT` (CS-11).
- `schema_changes`: one row per revision, with the summary (CS-13).

Both jsonb columns are mapped as `JsonDocument`, so reads can project single fields (CS-08).

**Rationale**:
- One row per item keeps #15's per-item writes and locks cheap.
- `jsonb` with field-id keys is what the constitution prescribes for item data.

**Alternatives considered**:
- *A row per item and field (EAV)*: type changes become column updates, but every item read
  becomes a pivot. It contradicts "jsonb stores … item data".
- *A table per collection*: needs DDL at run time. EF Core migrations are the only allowed
  schema change.

---

## CS-06 Saving a collection: one proposal, diffed by id

**Decision**:
- **One write**: `PUT …/collections/{id}` carries the whole proposed definition: `name`,
  `slug`, `description`, `schema`, plus `revision` (the one it was based on), optional
  `fillIns` and optional `confirmation` (CS-08). The builder sends its whole state on Save.
- **The diff** (`SchemaDiff`, domain) compares the stored and proposed schemas **by field
  id**. It turns the comparison into changes:
  - fields `added` (temporary id), `removed` (id missing) and `moved`;
  - per field: `renamed` (name, key or help), `requiredChanged`, `typeChanged`,
    `settingsChanged` (each setting classified, CS-07);
  - per option: `optionAdded`, `optionRemoved`, `optionRelabelled`, `optionsReordered`;
  - `titleFieldChanged`;
  - collection details: `detailsChanged`.
- **No difference**: a save that changes nothing is accepted without a new revision.
- **Unknown ids**: a permanent id that is not in the stored schema is refused
  (`cms.unknown_field_id`). This stops a client from inventing or reviving ids (FR-006,
  FR-019).
- **One revision per save** (FR-032): all changes of one save are applied together, with
  one combined preview and one history entry.

**Rationale**: whole-document saves are how pages, components and design tokens already
work, and identity by id makes the intent of every change unambiguous. A rename is never
mistaken for a remove plus an add.

**Alternatives considered**:
- *A list of operations (`addField`, `renameField`, …)*: the client would replay its edits,
  and the server would need ordering and conflict rules between operations. A diff of two
  documents gives the same information with less protocol.
- *Separate endpoints for details and schema*: FR-032 asks for one revision per save.

---

## CS-07 Change rules and conversions

**Decision**: `FieldConversion` (domain) classifies each field change into one of four
rules and, for each stored value, an outcome:
- **Rules**:
  - `free`: no item value can change and nothing needs reporting (rename, reorder, help
    text, option relabel or reorder, loosening a setting, an added field without a fill-in).
  - `reported`: no value is lost, but the preview must show counts (tightened settings,
    required turned on, a fill-in). Shown to the member, never needs confirmation.
  - `lossless`: values are rewritten and none is lost.
  - `lossy`: some values may be partly lost or cleared.
  - `forbidden`: refused, unless no item holds a value in the field
    (`cms.type_change_not_allowed`, `cms.target_change_not_allowed`).
- **Outcomes** per item: `kept`, `converted`, `partlyLost`, `cleared`, `nowInvalid`,
  `missing` (a media or reference value whose file or item is gone), `filled`
  (fill-in written) and `stillMissing` (required value still absent).
- **The table is FR-026**, implemented as one function per allowed pair, with these fixed
  formats:
  - **Number to text**: invariant, shortest round-trip, no grouping, `.` as the separator.
  - **Text to number**: trimmed, must match `^-?(0|[1-9][0-9]*)(\.[0-9]+)?$`. Extra decimals
    are rounded half away from zero and counted as `partlyLost`. A result outside
    `min`/`max` or the safe range is cleared.
  - **Text to date**: `YYYY-MM-DD`. With `withTime`, ISO 8601 with `Z` or an offset,
    normalised to UTC, and `YYYY-MM-DD` taken as midnight UTC. Anything else is cleared.
  - **Date to text**: the stored form.
  - **Rich text to text**: the text of each block, joined with a line break (multiline) or a
    space (single line); hard breaks likewise. It counts as `partlyLost` when marks, links or
    list structure were dropped or the result was shortened to `maxLength`; otherwise
    `converted`.
  - **Text to rich text**: each line becomes a paragraph. Past the page schema's 500-block
    limit, the remaining lines are joined into the last paragraph with hard breaks, so
    nothing is lost.
  - **Option to text**: the label. **Text to option**: a trimmed, case-insensitive label
    match, otherwise cleared.
  - **Several lines to single line**: lines joined with one space.
  - **Dates and times**: UTC. Sites have no time zone (012 assumption), so the spec was
    corrected during planning from "the site's time zone" to UTC, and the preview says
    "UTC".
- **Tightened settings** (`maxLength` lower, `min` raised, `max` lowered, `accept` narrowed,
  `decimals` lowered): values are kept and counted `nowInvalid`, except lowering
  `decimals`, which FR-026 makes lossy (rounding).
- **A converted value that breaks the new field's settings** (for example a number turned
  into text longer than a small `maxLength`) is kept and counted `nowInvalid`. Only rich text
  to text shortens, as FR-026 says.
- **Both copies**: draft and published values are converted independently. An item is
  counted once per outcome, using the worse of its two copies (order: `cleared`,
  `partlyLost`, `nowInvalid`, `missing`, `converted`, `kept`).

**Rationale**: pure functions per pair are exhaustively unit-testable (SC-004): every pair,
every boundary, golden values. One classification drives the preview, the history summary,
the confirmation rule and the migration itself, so they cannot disagree.

**Alternatives considered**:
- *Culture-aware parsing ("1.234,5")*: different results on different installations.
  Rejected by the spec's fixed-format assumption.
- *Shortening text on tighter `maxLength`*: contradicts FR-027 (keep and report).

---

## CS-08 Preview and confirmation

**Decision**:
- **`POST …/collections/{id}/preview`** takes the same body as the save and returns the
  diff with, for each change that can alter values: its rule, counts per outcome, and up to
  10 examples (item id and title-field text). It also returns `requiresConfirmation` (true
  when any `cleared` or `partlyLost` count is above 0, or a field with values is removed)
  and an **impact digest**.
- **The digest** is SHA-256 over a canonical serialisation of the change list and its counts,
  without examples or item ids. A save of a change that requires confirmation must carry
  `confirmation: { digest }`.
- **Inside the save transaction** (CS-09) the server recomputes the impact under the lock:
  - no digest when one is required: refused with `cms.confirmation_required` (409, with the
    fresh preview);
  - a different digest: refused with `cms.preview_outdated` (409, with the fresh preview),
    which is the spec's "counts now differ, confirm again" edge case;
  - otherwise the change is applied. Item changes that leave the counts the same do not
    force a second confirmation.
- **Reading**: the preview reads only the fields involved. `JsonDocument` mapping lets
  Npgsql translate `GetProperty(id)` to `->`, so 10,000 items cost a few megabytes, not
  whole documents (SC-003: under 5 s). The title value is projected for the examples.
- **Media kinds** are fetched once per preview for the media ids involved (CS-12).
- **No preview needed** (FR-030): when every change is `free`, the builder saves directly.
  Any `reported`, `lossless` or `lossy` change shows the preview first; only `lossy` ones
  (and removing a field with values) need confirmation.
  The server still computes the impact, finds nothing to confirm, and applies the change.

**Rationale**: the server enforces confirmation (FR-023) without remembering previews. It
needs no stored preview tokens or expiry, works across instances, and a stale confirmation
is detected exactly when it matters.

**Alternatives considered**:
- *A stored preview with an id and expiry*: state to clean up, and still racy unless locked.
- *A boolean `confirmed` flag*: cannot tell that the counts changed since the member saw
  them.

---

## CS-09 Applying a change: one transaction, locked, in batches

**Decision**: the save handler runs in **one transaction** on `CmsDbContext`:
1. Lock the collection row with `SELECT … FOR UPDATE NOWAIT`, through EF Core `FromSql`. It
   is parameterised, applies the site filter explicitly, and has a test proving it
   (Technology Constraints). If the row is locked by another save, a delete or an item write
   (CS-10), the save is refused with `cms.collection_busy` (409, "try again shortly").
2. Check `revision` against the request (`cms.revision_conflict`, 409, with the current
   revision) (FR-022).
3. Validate the proposal (CS-04), including targets in the same site.
4. Compute the impact (CS-07) and check the confirmation (CS-08).
5. Rewrite affected items in **batches of 500** tracked entities, saving each batch and then
   clearing the change tracker. Batches stay inside the transaction, so nothing is visible
   until commit.
6. Write the collection (schema, details, `revision + 1`, counters), replace its
   `collection_references` rows, add the `schema_changes` row and the outbox event, and
   commit.

- **Atomic by construction** (FR-021, SC-005): any failure or instance stop rolls everything
  back. PostgreSQL MVCC gives readers either the old or the new data, never a mix (FR-037).
- **Time**: about 20 batched round trips for 10,000 items. Target: under 30 s on the
  reference setup (SC-003), measured by `CollectionSchemaPerformanceTests`. Only items with
  a changed value are written. Renames and reorders write no item at all (SC-002).
- **Progress** (FR-036): the request stays open while the change runs, and the builder
  shows a progress state with "Updating N items" from the preview's counts. No background
  job is used.
- **Request size**: proposals are capped at 64 KiB. 100 fields with 200 options each fit,
  as the golden examples show.

**Rationale**: a single transaction is the simplest way to be all-or-nothing, and at 10,000
items it fits well within normal request times. The row lock is also the serialisation
point that FR-037 needs.

**Alternatives considered**:
- *A background job with progress polling*: needed only at much larger sizes. It would bring
  a visible mix of shapes (rejected in clarification Q3) or a long-held lock outside a
  request.
- *One SQL `UPDATE` with jsonb functions per change*: fast, but would reimplement the
  conversion rules in SQL beside the C# ones used by the preview. The preview and the result
  could disagree, which breaks SC-004.
- *Optimistic concurrency only (`xmin`)*: cannot refuse item writes during a migration, as
  clarification Q3 requires.

---

## CS-10 Refusing item writes during a change (FR-037)

**Decision**:
- `CollectionWriteGate` (internal to Cms) is the single entry point for any item write. In
  the writer's own transaction, it takes `FOR SHARE NOWAIT` on the collection row and
  checks the revision the writer validated against. While a schema change holds
  `FOR UPDATE`, the attempt fails at once with `cms.items_being_updated` (409, "this
  collection's fields are being updated, try again shortly").
- It returns the current schema, so the writer validates against exactly the schema that
  will hold at its commit. A schema change cannot start until the item write commits,
  because `FOR SHARE` blocks `FOR UPDATE NOWAIT`, which then reports `cms.collection_busy`.
- **In this feature** nothing writes items through the API. The gate ships with an
  integration test that runs a schema change and a gated write at the same time
  (`ItemWriteGateTests`). #15 must use it for every item write, and a code-review note in
  `data-model.md` §3 says so.
- **Collection delete** takes the same `FOR UPDATE NOWAIT` lock.

**Rationale**: the refusal is immediate, works across instances with no shared cache, and
needs no flag that could be left set by a crashed instance.

**Alternatives considered**:
- *A `migrating` flag column*: survives a crash and needs clean-up; a row lock cannot leak.
- *Advisory locks*: work, but are invisible to EF Core and need raw SQL for every write. A
  row lock on a row EF already reads is simpler.

---

## CS-11 References between collections, and deleting a collection

**Decision**:
- `collection_references` holds `(source_collection_id, field_id, target_collection_id)` for
  every reference and multi-reference field that targets **another** collection.
  Self-references are not stored, so they never block deletion (US5 scenario 3).
- `target_collection_id` references `collections(id)` with **`ON DELETE RESTRICT`**, and
  `source_collection_id` with `CASCADE`. The rows are replaced in the save transaction.
- **Creating a reference to another site's collection** is refused before any write:
  targets are loaded through `CmsDbContext.ForSite<Collection>(siteId)` (the admin pattern
  of 001 FR-011b), so another site's collection is
  "not found" (`cms.unknown_target_collection`).
- **Deleting a collection** (`DELETE …/collections/{id}?revision=n`):
  1. lock the collection (CS-09);
  2. if any row targets it, refuse with `cms.collection_referenced` (409), listing each
     referencing collection and field (id, name, field name);
  3. otherwise delete it. Items, references from it and its history cascade. The outbox
     event is written and the transaction commits.
- If a concurrent save adds a reference while the delete runs, the foreign key fails, and
  that save is reported as `cms.unknown_target_collection`. The database decides the race.

**Rationale**: the same composite-key approach 010 P-11 used for components. A rule kept by
the database cannot be lost to a race.

**Alternatives considered**:
- *Scanning every schema's jsonb on delete*: slow, and racy without a site-wide lock.

---

## CS-12 Rules that depend on media kinds

**Decision**:
- Two conversions need to know whether a media file is an image: **file to image**
  (non-images cleared) and **narrowing `accept`** (values of the wrong kind counted
  `nowInvalid`).
- The handler collects the distinct media ids involved and asks
  `IPublicMediaReader.GetAsync(siteId, ids)` once per preview or save. An image is a
  content type starting with `image/`. Absent ids are counted `missing` and kept.
- This reads outside the Cms transaction. That is safe because 013 refuses a replace that
  changes a file's kind (013 FR-017), so the answer cannot change under the lock. A file
  deleted in between becomes `missing`, which keeps the value.

**Rationale**: it reuses an existing read-only contract and adds no new Media surface. Kind
is immutable, so there is no cross-module consistency problem.

**Alternatives considered**:
- *Storing the kind inside each media value*: a denormalised copy for one rare conversion.
- *A new `IMediaKindReader`*: `IPublicMediaReader` already answers the question.

---

## CS-13 Schema history

**Decision**:
- `schema_changes` stores one row per revision: number, actor, time, and a **summary**, a
  JSON list of change entries `{ kind, fieldId, key, from, to, counts }`. It holds no
  examples and no item values (FR-033).
- Keys and display names are stored as they were at that revision, so the admin can render
  "Renamed field Price to Unit price" without the current schema.
- Revision 1 is the collection's creation. Detail-only saves ("Renamed the collection")
  create entries too.
- Read with `GET …/collections/{id}/history?before=<revision>&limit=50`, newest first.
- It is deleted with the collection.

**Rationale**: a structured summary lets the admin translate it later (backlog: admin
localisation) and lets tests assert exact counts.

**Alternatives considered**:
- *Storing every full schema document*: useful for a future "restore schema", which no spec
  asks for. The summary is enough for FR-033, and storing full documents could be added
  additively.

---

## CS-14 Integration events

**Decision**: written to the Cms outbox in the same transaction (data-model §4). Ids only:
- `Cms.CollectionCreated`: `siteId`, `collectionId`, `revision`, `actorUserId`.
- `Cms.CollectionChanged`: `siteId`, `collectionId`, `revision`, `schemaChanged` (bool:
  false for detail-only saves), `itemsRewritten` (bool), `actorUserId`.
- `Cms.CollectionDeleted`: `siteId`, `collectionId`, `actorUserId`.

They have no consumers in this feature. #16 will use them to invalidate rendered pages.
They are added to `specs/001-foundation-skeleton/contracts/integration-events.md`.

**Rationale**: FR-034. Ids only, as every event since 012 carries, with no names in event
payloads.

---

## CS-15 Authorization

**Decision**: two new site-scoped capabilities, declared by Cms and enforced by Identity:

| Capability | Administrator | Designer | Editor |
|---|---|---|---|
| `cms.collections.view` | ✓ | ✓ | ✓ |
| `cms.collections.manage` | ✓ | ✓ | — |

- **`view`**: the list, a collection with its schema, its history and the preview. The
  preview is read-only, but it shows item titles, so it needs at least view.
- **`manage`**: create, save and delete.
- Users with no role get the "site not found" response (002's existing rule).
- Added to `specs/002-identity-setup/contracts/authorization.md` §2.
  `AuthorizationDeclarationTests` covers every new action.

**Rationale**: the spec's role split (Assumptions). The names follow `<module>.<noun>.<verb>`.

---

## CS-16 HTTP API

**Decision**: under `api/v1/cms/sites/{siteId}/collections`, following the module-first
prefix of Pages (`api/v1/pages/sites/…`). The full definition is in
[contracts/cms-api.yaml](./contracts/cms-api.yaml):
- `GET /` (list, FR-003) and `POST /` (create: name, slug, description; the schema starts
  empty, at revision 1).
- `GET /{id}` (details, schema, revision, and an `itemCount`).
- `PUT /{id}` (save, CS-06/CS-09) and `POST /{id}/preview` (CS-08).
- `DELETE /{id}?revision=n` (CS-11).
- `GET /{id}/history` (CS-13).

Errors use the existing problem format with `errorCode`, and `rule` and `pointer` for
validation (as Pages does). Status codes: 400 validation, 404 not found, 409 conflicts.

---

## CS-17 The schema builder in the admin

**Decision**:
- A new **Collections** entry in the site navigation (`/sites/:siteId/collections`):
  - **`CollectionsView`**: the list, plus the create dialog (`CollectionFormDialog`). The
    address name is suggested from the name with the existing `slugify.ts`.
  - **`CollectionBuilderView`** (`/collections/:collectionId`): details at the top, the
    field list on the left, the selected field's editor on the right, and a save bar
    showing "unsaved changes".
- **Field list**: reordered by drag with `useDragDrop` (011), and by keyboard with
  Alt+↑/↓ plus a live announcement, matching the editor's layers panel. "Add field" opens a
  type menu.
- **`FieldEditor`**: common settings, then per-type settings components, an
  `OptionsEditor`, and a target picker for references. The key is suggested from the name
  until edited by hand.
- **Saving**:
  1. client validation (CS-04);
  2. if any change is not `free`, `POST preview`, then the **`SchemaPreviewDialog`**: counts
     per change, examples, the "cannot be undone" wording (FR-024), fill-in inputs where
     allowed, and confirm;
  3. `PUT`, with a progress state while it runs (FR-036);
  4. on `cms.preview_outdated`, the dialog reopens with the fresh preview; on
     `cms.revision_conflict`, the existing `ConflictBanner` pattern offers to reload.
- **`DeleteCollectionDialog`**: item count, the "cannot be undone" warning, typed-name
  confirmation, and the list of referencing fields when refused.
- **`SchemaHistoryPanel`**: a side panel on the builder.
- **Editors** see the same screens read-only, with controls disabled and a notice.
- **Accessibility**: the admin accessibility baseline. Every action works by keyboard, and
  vitest with axe runs on each new view (SC-007).

**Rationale**: it reuses the admin's existing drag-and-drop, conflict, slug and validator
pieces, and adds no new dependency.

---

## CS-18 Limits

**Decision**:
- **Per site**: 100 collections. Checked in the create handler by counting. A race between
  two creates may briefly allow 101, which is accepted and documented; the limit protects
  usability, not integrity.
- **Per collection**: 100 fields. **Per field**: 200 options. **Per multi-reference value**:
  100 items.
- **Lengths**: names 1–100, keys and address names 1–64, help text 300, description 500,
  option labels 100.
- **Proposals**: 64 KiB.
- All limits are constants in `Cms.Contracts.CollectionSchemaLimits` and stated in the
  contract document. Raising one later is additive.

---

## CS-19 Items for tests and hand-runs

**Decision**: until #15 there is no API that creates items.
- **Integration tests** insert items through `CmsDbContext` with a test builder
  (`ItemSeed`). It produces valid values per type from the schema.
- **Smoke and performance tests** insert 10,000 items with one SQL statement over
  `generate_series`, as `MediaPerformanceTests` does for files.
- **Hand-runs**: `./qb.ps1 seed-items [-Collection blog-posts] [-Count 300] [-Site name]`
  runs one SQL statement. It reads the collection's schema from `cms.collections` and builds
  values per field type with `jsonb_build_object`, filling about 70 % of optional fields so
  previews show real counts. It is a development script, like `./qb.ps1 seed`, and is never
  part of the product.

**Rationale**: no product surface is created only for testing, and the hand-run in
quickstart.md can still show a migration on hundreds of items.

**Alternatives considered**:
- *A development-only API endpoint or host command*: product code that ships but must stay
  disabled. A script keeps it outside the image.

---

## CS-20 Testing strategy

| Suite | Covers |
|---|---|
| `UnitTests/Cms/SchemaDiffTests` | Every change kind, ids, temporary ids, moves (CS-06) |
| `UnitTests/Cms/FieldConversionTests` | Every FR-026 pair plus forbidden pairs, boundaries, fixed formats, UTC, rounding, rich text both ways (SC-004) |
| `UnitTests/Cms/CollectionSchemaRulesTests` | Code rules, reserved keys, limits, title field |
| `UnitTests/Cms/ImpactDigestTests` | Canonical digest stability |
| `UnitTests/Cms/CollectionSchemaGoldenTests` | Every golden example: verdict, rule, pointer (SC-006) |
| `UnitTests/Pages/SchemaSubsetValidatorDefinitionTests` | `ValidateDefinition` |
| `IntegrationTests/Cms/CollectionApiTests` | Create, get, list, save, delete, errors, authorization per role (US1, US5) |
| `IntegrationTests/Cms/RenameReorderTests` | Zero item or id change across all types (SC-002) |
| `IntegrationTests/Cms/SchemaMigrationTests` | Add, remove, fill-in, type changes, options and settings; the stored result equals the confirmed preview (US3, US4, SC-004) |
| `IntegrationTests/Cms/ConfirmationTests` | `confirmation_required`, `preview_outdated` |
| `IntegrationTests/Cms/InterruptedSchemaChangeTests` | A fault injected after a batch rolls back everything (SC-005) |
| `IntegrationTests/Cms/ConcurrencyTests` | `revision_conflict`, `collection_busy` |
| `IntegrationTests/Cms/ItemWriteGateTests` | FR-037 refusal and readers seeing before or after |
| `IntegrationTests/Cms/CollectionReferenceTests` | Referenced delete refused, self-reference allowed, delete versus add-reference race |
| `IntegrationTests/Cms/SchemaHistoryTests` | One entry per save, with counts and no values (US6) |
| `IntegrationTests/Cms/CmsSiteFilterTests` | The `FromSql` lock applies the site filter |
| `IntegrationTests/Isolation/Probes/CmsIsolationProbe` | Cross-site reads and writes are invisible |
| `IntegrationTests/Cms/UpgradeFrom013Tests` | A 013 database upgrades and gains an empty `cms` schema |
| `ArchitectureTests` | Cms in the module lists; allowed references only |
| `SmokeTests/CollectionSchemaPerformanceTests` | 10,000 items: preview under 5 s, change under 30 s (SC-003) |
| admin `tests/cms/*.spec.ts` | Builder, preview dialog, delete dialog, history, keyboard reorder, axe (SC-007), and `collectionSchema.spec.ts` over the golden files (SC-006) |

The fault injection for SC-005 is a test-only `ISchemaMigrationObserver` that the handler
calls after each batch. Production registers a no-op.

---

## CS-21 Upgrade and migrations

**Decision**:
- One migration, `InitialCms`, creates the `cms` schema, its four tables, the outbox
  tables, and the migrations history table.
- It is applied automatically with the others. It is forward-only and has no data to move.
- `UpgradeFrom013Tests` restores the 013 snapshot, starts the app and checks that the Cms
  endpoints work and nothing else changed.

---

## CS-22 Dependencies

None. Cms uses EF Core and Npgsql (already central) and the in-house subset validator. The
admin uses nothing new.

---

## Things deliberately not built

- **Item creation and editing**, slugs, item status, publishing items, and media usage by
  items: these are #15.
- **Rendering items** and invalidating pages on schema changes: these are #16. The events are
  ready for it.
- **Unique field values**, which are in the backlog (clarification Q4).
- **Restoring removed fields or deleted collections** (clarifications Q2, Q5); trash comes
  with #21.
- **A background migration engine**: the 10,000-item target fits one transaction (CS-09).
- **Storing full schema snapshots per revision** (CS-13).
- **A per-site time zone**: conversions use UTC (CS-07).
- **Custom field types and localisation** (out of scope).

## Configuration keys

None. All limits are constants in the contract (CS-18).

## Resolved unknowns

| Unknown | Resolution |
|---|---|
| Which module owns collections and items | A new Cms module (CS-01) |
| How field ids are generated and kept unique | Per-collection counters (CS-02) |
| How rich text values are defined without a second definition | The page schema's `rtDoc` via `ValidateDefinition` (CS-04) |
| How migrations stay atomic and refuse item writes | One transaction plus a row lock taken with `NOWAIT` (CS-09, CS-10) |
| How the server knows the member confirmed the current preview | The impact digest (CS-08) |
| "The site's time zone" for date conversions: sites have none | UTC; spec corrected (CS-07) |
| How to test migrations without an item editor | `ItemSeed` and SQL seeding (CS-19) |
