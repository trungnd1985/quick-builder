---

description: "Task list for 015-cms-items"
---

# Tasks: CMS Items

**Input**: Design documents from `/specs/015-cms-items/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - golden files for validation;
  - 80 % coverage on critical paths (publish is one).
- The spec measures behaviour through automated checks: SC-002 … SC-007.
- Within each story, test tasks come first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US7 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US7)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CMSC` = `src/Modules/Cms/QuickBuilder.Cms.Contracts`
- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `KERNEL` = `src/Shared/QuickBuilder.Shared.Kernel`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `ITEMUI` = `src/admin/src/features/cms/items`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`

**Conventions**:
- **Backend slices** live under `CMS/Features/Items/<Slice>/<Slice>.cs` and follow 014's
  slices (for example `CMS/Features/SaveCollection/SaveCollection.cs`). Every controller
  action:
  - is routed under `api/v1/cms/sites/{siteId:guid}` as in contracts/items-api.yaml;
  - declares exactly one `[RequireCapability(...)]`: `cms.collections.view`,
    `cms.items.edit` or `cms.items.publish` (research IT-16);
  - dispatches through `IDispatcher`, and its handler first checks the site with
    `CmsAccess.SiteExistsAsync`;
  - refuses through `CMS/CmsErrorResults.cs` with the codes in
    [data-model.md §6](./data-model.md);
  - is registered in `CMS/CmsModule.cs` and listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **Every item write** goes through `CMS/Infrastructure/ItemWrites.cs` (IT-06). That means
  the transaction, `CollectionWriteGate`, the revision and schema-revision checks, validation,
  the domain operation, `ItemIndexer.Refresh`, history and outbox, then commit. No slice
  writes `Item`, `ItemVersion`, `ItemSchedule` or the derived tables itself.
- **Admin reads** use `CmsDbContext.ForSite<T>(siteId)` only.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task. `npm run lint` enforces this.
- **Logs, events and history** carry ids, counts, kinds and codes only. They never contain
  item values, names or slugs (FR-032).

**References**:
- decisions: `IT-xx` in [research.md](./research.md);
- tables, rules, states, events and error codes: [data-model.md](./data-model.md);
- endpoints: [contracts/items-api.yaml](./contracts/items-api.yaml);
- capabilities, events and the media usage reader:
  [contracts/module-surface.md](./contracts/module-surface.md);
- value formats: `CONTRACTS/collection-schema.md` §3.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, the `SearchText` move and the new project reference.
No behaviour.

- [X] T001 Commit `specs/015-cms-items/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `015-cms-items`, referencing #15.
- [X] T002 Move `MEDIA/Domain/SearchText.cs` to `KERNEL/Text/SearchText.cs` (namespace `QuickBuilder.Shared.Kernel.Text`, class made `public`, code unchanged, the remark naming 013 MV-03 and 015 IT-13 kept). Update every `using` in `MEDIA`, and move `UTESTS/Media/SearchTextTests.cs` to `UTESTS/Shared/SearchTextTests.cs`. Run `dotnet test --filter "SearchText"`; it must stay green, including `ITESTS/Media/SearchTextBackfillTests.cs`.
- [X] T003 [P] Add a project reference from `MEDIA/QuickBuilder.Media.csproj` to `CMSC/QuickBuilder.Cms.Contracts.csproj`. In `ATESTS/ModuleBoundaryTests.cs`, allow `QuickBuilder.Media` → `QuickBuilder.Cms.Contracts` and keep every other rule (module-surface.md §1).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the database (migration, entities, `schema_revision`);
- the `Item` aggregate's core;
- the validation report and its golden examples;
- the indexer and the single write path;
- capabilities, errors and events;
- the upgrade and backfill;
- isolation;
- the admin client, the value rules port and the routes.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Database

- [X] T004 Extend `CMS/Infrastructure/CmsDbContext.cs` with everything in data-model §1:
  - `collections.schema_revision`;
  - the 14 new `items` columns (`name` as `text`, its index on `left(name, 256)`): `revision` as the concurrency token, `attention` as `jsonb` mapped to `JsonDocument`, and the five `items` indexes, including the partial one on `attention`;
  - the entities `ItemSlug`, `ItemReference`, `ItemMediaUse`, `ItemVersion`, `ItemSchedule` and `ItemHistoryEntry`, with their keys and the foreign keys exactly as specified: **cascade** on sources, and **`ON DELETE NO ACTION`** on `item_references.target_item_id` (IT-10);
  - `item_slugs.slug` as `citext` (`HasPostgresExtension("citext")`);
  - the partial unique index `(item_id, action) WHERE state = 'pending'` on `item_schedules`;
  - global site filters on every new entity.

  Create minimal entity classes in `CMS/Domain/` for the six new types (properties and private setters only; behaviour comes in later tasks).
- [X] T005 Generate `CMS/Infrastructure/Migrations/<ts>_AddItems.cs` with `dotnet ef migrations add AddItems`. Then hand-add the IT-22 backfill SQL to `Up`, after the schema changes:
  - `schema_revision = revision`;
  - for existing items: `slug = 'item-' || left(replace(id::text,'-',''),12)`, `status` from `published IS NULL`, `revision = 1`, `index_version = 0`, empty-guid `created_by`/`updated_by`, and `next_version_number` = 2 for published items, otherwise 1;
  - for published items: an `item_versions` row (number 1, kind `publish`, values `published`, slug, `published_at_utc = updated_at_utc`), plus `live_version = 1`, `published_slug = slug`, `published_at_utc` and `published_by`;
  - one `item_slugs` row per item.

  Check that the generated SQL matches data-model §1.
- [X] T006 [P] Create `ITESTS/Cms/Items/UpgradeFrom014Tests.cs`, following `ITESTS/Cms/UpgradeFrom013Tests.cs`. Use a 014 snapshot holding a collection with draft-only and published items:
  - after the upgrade, every item has a slug, a status and `revision` 1;
  - published items have version 1 and `live_version` 1;
  - after `ItemIndexBackfillService` runs (T022), names, search text, attention and the derived tables are filled, slugs come from names (made unique), and `index_version` is current.
- [X] T007 Add `SchemaRevision` to `CMS/Domain/Collection.cs`. `Apply` sets it to the new revision when the diff changes the schema document, and leaves it unchanged for details-only saves (IT-07). Return it from `GetCollection` in `CMS/Features/CollectionResponses.cs`. Add a case to `UTESTS/Cms/CollectionTests.cs`.

### Domain core (tests first)

- [X] T008 [P] Create `UTESTS/Cms/Items/SlugRulesTests.cs`:
  - the format `^[a-z0-9][a-z0-9-]{0,99}$`;
  - slugify from a name, matching `ADMIN/features/pages/slugify.ts` (including Vietnamese and other accented names);
  - the fallback `item-<yyyymmddhhmmss>` for empty or non-Latin names;
  - numbered suggestions `-2`, `-3`;
  - copy slugs `-copy`, `-copy-2`, shortened to 100 characters.
- [X] T009 [P] Create `UTESTS/Cms/Items/ItemStatusTests.cs`:
  - `draft` with no published values;
  - `published` when draft values and slug equal the published ones as JSON values (property order ignored);
  - `changed` when the values or the slug differ;
  - a hand-made edit that restores the published content returns to `published`.
- [X] T010 [P] Create `UTESTS/Cms/Items/ItemValidationTests.cs`, one case per problem code in research IT-05:
  - **Malformed** (refused on save): `malformed`, `unknown_field`, `unknown_option`, `reference_missing`, `reference_wrong_collection`, `reference_self`, `duplicate_reference`, `too_many_references`, a slug problem.
  - **Reported** (allowed on save, refused on publish): `missing_required`, `too_long`, `out_of_range`, `too_many_decimals`, `file_kind`, `media_missing`.
  - **Booleans**: a boolean is never missing.
  - The database lookups are faked through the interfaces T013 introduces.
- [X] T011 Create `CMS/Domain/SlugRules.cs` (format, slugify from name, fallback, numbered and copy suggestions; the C# twin of `slugify.ts`, with a comment linking them) and `CMS/Domain/ItemStatus.cs` (`Compute(draft, draftSlug, published?, publishedSlug?)` using `JsonNode.DeepEquals`). Makes T008 and T009 pass.
- [X] T012 Create `CMS/Domain/ItemReport.cs`: the report of data-model §2, with `Problems`, `HasMalformed` and `IsEmpty`, writing to and reading from `JsonDocument`, problem codes as constants, and `slug` as a pseudo field id.
- [X] T013 Create `CMS/Infrastructure/ItemValidation.cs`: `ValidateAsync(siteId, collectionId, itemId, schema, slug, ItemValues, ct)` returns an `ItemReport`. It works in three steps:
  1. Per field, `ItemValueValidator.Check` (014) mapped to problem codes, with the specific setting broken (`too_long` with `limit`, and so on), plus keys that are not fields.
  2. For media values, one `MediaKinds.LookupAsync` (014 CS-12), giving `media_missing`, or `file_kind` against `accept`.
  3. For references, one query over `ForSite<Item>`, giving `reference_missing`, `reference_wrong_collection`, `reference_self`, duplicates and the count.

  Behind two small internal interfaces, `IItemMediaLookup` and `IItemReferenceLookup`, so T010 can fake them. Makes T010 pass.
- [X] T014 Extend `CMS/Domain/Item.cs` into the aggregate root of data-model §3.1, **core operations only**: `Create`, `SaveDraft`, `Duplicate`, `Delete`, revision increments, status recompute, and domain events in `CMS/Domain/Events/ItemDomainEvents.cs`. Publishing, schedules and rollback are added by their stories. Keep `Migrate` from 014, which now also increments `Revision`. Create `UTESTS/Cms/Items/ItemTests.cs` covering these operations and their events.

### Golden item values (FR-012)

- [X] T015 [P] Create `CONTRACTS/collection-schema/examples/items/`:
  - **one schema** `schema.json`, with one field of every type and setting, a required title, and a reference and a multi-reference;
  - **about 30 values documents**: one valid example per type, and one per problem code of IT-05 that needs no database;
  - **`expected.json`**, mapping each file to its expected report.

  Add a §5a paragraph to `CONTRACTS/collection-schema.md` describing these examples. This is examples only; the version stays 1.
- [X] T016 [P] Create `UTESTS/Cms/Items/ItemValueGoldenTests.cs`: every example's report from `ItemValidation`, with fakes that know no media and no items, equals `expected.json`.

### Indexer and the write path

- [X] T017 [P] Create `ITESTS/Cms/Items/ItemIndexerTests.cs`. After `Refresh`:
  - `name`, `search_text` (via `SearchText`), `attention`, `status` and `index_version` are right;
  - `item_slugs` holds the draft slug and, when different, the published slug;
  - `item_references` holds one row per distinct reference with `in_draft`, `in_published` and `in_scheduled`, and no self rows;
  - `item_media` holds the right flags;
  - refreshing twice changes nothing.
- [X] T018 Create `CMS/Infrastructure/ItemIndexer.cs` (IT-11). `RefreshAsync(item, schema, ct)` recomputes every derived column and replaces the item's rows in `item_slugs`, `item_references` and `item_media` from its draft, published values, pending captured content and kept versions (`in_history`). It sets `index_version` to `ItemIndexer.CurrentVersion = 1`. Makes T017 pass.
- [X] T019 Create `CMS/Infrastructure/ItemWrites.cs` (IT-06; needs T023 and T024 for its errors and events). `ExecuteAsync(siteId, collectionId, itemId?, expectedItemRevision?, expectedSchemaRevision?, Func<ItemWriteContext, Task<Result<T>>> operation, ct)` runs these steps, in order:
  1. begins a transaction;
  2. `CollectionWriteGate.EnterAsync` (014), mapping `cms.revision_conflict` from the gate to nothing, because item writes do not pass the collection revision;
  3. compares `expectedSchemaRevision` with `collections.schema_revision`, giving `cms.schema_changed` with the current one;
  4. loads the item through `ForSite<Item>` and checks `revision`, giving `cms.item_conflict` with `currentRevision`;
  5. runs the operation, which receives the schema, the item, `ItemValidation`, the clock and the actor;
  6. calls `ItemIndexer.RefreshAsync`;
  7. adds the outbox events through `CmsOutbox`;
  8. saves and commits.

  It also maps the database errors: a primary-key violation on `item_slugs` becomes `cms.item_slug_taken` with a suggestion, a foreign-key violation on `item_references` becomes `cms.item_referenced` (re-read), and `DbUpdateConcurrencyException` becomes `cms.item_conflict`.
- [X] T020 [P] Create `ATESTS/ItemWritesOnlyTests.cs`: within `QuickBuilder.Cms`, only `ItemWrites`, `ItemIndexer`, `ItemMigrator` and the schedule runner may call `Add`, `Remove` or setters on `Item`, `ItemVersion`, `ItemSchedule`, `ItemHistoryEntry` or the derived entities. This is a reflection or IL scan in the style of the existing architecture tests (module-surface.md §5).
- [X] T021 Extend `CMS/Infrastructure/ItemMigrator.cs` (014): every item it rewrites gets `revision + 1` and `ItemIndexer.RefreshAsync`. When the diff changes the title field, any `required` flag, a setting or a type, **every** item of the collection is refreshed, in the same batches, even if no value changed (IT-11). Add the cases to `ITESTS/Cms/SchemaMigrationTests.cs`: names follow a new title field, and attention follows a newly required field.
- [X] T022 Create `CMS/Infrastructure/ItemIndexBackfillService.cs`, a `BackgroundService` modelled on `MEDIA/Infrastructure/SearchTextBackfillService.cs`. It refreshes items with `index_version < CurrentVersion` in batches of 500, each batch through `CollectionWriteGate` (a busy collection is skipped and retried on the next pass). For `index_version` 0 it also replaces `item-…` slugs with `SlugRules` slugs from the name, made unique (IT-22). Register it in `CMS/CmsModule.cs`. Makes T006 pass.

### Module, capabilities, errors, events

- [X] T023 [P] Add `ItemsEdit = "cms.items.edit"` and `ItemsPublish = "cms.items.publish"` to `CMS/CmsCapabilities.cs`, both held by Designer and Editor. Add every code in data-model §6 to `CMS/CmsErrors.cs` (with extensions: `report`, `suggestion`, `currentRevision`, `schemaRevision`, `referrers`, `total`, `schedule`, `parameter`) and to `CMS/CmsErrorResults.cs` (statuses as listed).
- [X] T024 [P] Create `CMSC/Events/ItemCreated.cs`, `ItemDraftSaved.cs`, `ItemPublished.cs`, `ItemUnpublished.cs` and `ItemDeleted.cs`, with the payloads in data-model §4 implementing `IIntegrationEvent`. Map them from the domain events in `CMS/Infrastructure/CmsOutbox.cs`, and register the five `EventTypeRegistration`s in `CMS/CmsModule.cs`.
- [X] T025 [P] Update the shared registries: add the two capabilities to `specs/002-identity-setup/contracts/authorization.md` §2 under "Registered by 015", the five events to `specs/001-foundation-skeleton/contracts/integration-events.md`, and mark the #15 obligations met in `specs/014-cms-collections-schema/contracts/module-surface.md` §5.
- [X] T026 Register in `CMS/CmsModule.cs`: `ItemValidation`, `ItemIndexer`, `ItemWrites` (scoped), the two lookups, and an `ItemReads` stub (`CMS/Infrastructure/ItemReads.cs`, which maps an `Item` plus names to the `Item` response shape of items-api.yaml). Create `CMS/Features/Items/ItemResponses.cs` with `ItemResponse`, `ItemSummaryResponse`, `ItemRefResponse`, `ReportResponse` and `ScheduleResponse`.

### Isolation and test support

- [X] T027 [P] Update `ITESTS/Cms/ItemSeed.cs` to give seeded items slugs, statuses and an index. It writes through `ItemIndexer`, and has options for published copies with version 1, pending schedules, and references between seeded items.
- [X] T028 [P] Extend `ITESTS/Cms/CmsSiteFilterTests.cs`: every new table is filtered by site, and `ForSite` reads of items, slugs, references, media, versions, schedules and history never return site A's rows to site B.

### Admin foundations

- [X] T029 [P] Create `ADMIN/api/items.ts`: typed requests and responses for every operation in contracts/items-api.yaml, built on `ADMIN/api/request.ts`, surfacing `errorCode`, `report`, `suggestion`, `currentRevision`, `schemaRevision`, `referrers` and `schedule`.
- [X] T030 [P] Create `ADMINTESTS/cms/items/itemValues.spec.ts`: every example in `CONTRACTS/collection-schema/examples/items/` gives the expected report through the admin rules (FR-012).
- [X] T031 Create `ITEMUI/itemValueRules.ts`, a port of `ItemValidation`'s schema-only checks over `collectionSchema.ts`'s contract validator, and `ITEMUI/itemErrors.ts` (error and problem code → i18n key). Makes T030 pass.
- [X] T032 Add the routes `site-collection-items` (`/sites/:siteId/collections/:collectionId/items`), `site-collection-item-new` (`…/items/new`) and `site-collection-item` (`…/items/:itemId`) to `ADMIN/app/router.ts`, with placeholder views. Add an **Items** link per collection in `ADMIN/features/cms/CollectionsView.vue`, and an **Items** tab in `ADMIN/features/cms/CollectionBuilderView.vue`.

**Checkpoint**: the database, the item core, validation, the indexer and the write path
exist; stories can begin.

---

## Phase 3: User Story 1 - An Editor writes and saves a new item (Priority: P1) 🎯 MVP

**Goal**: create, open and save items through a form generated from the schema, with
validation on both sides, slug uniqueness and per-role access, and a basic list of the
collection's items.

**Independent Test**: create an item with every field type through the form, save, reopen,
and get every value back. A malformed value is refused against its field, an empty required
field is saved and flagged, and a taken slug is refused (quickstart §1).

### Tests for User Story 1 ⚠️

- [X] T033 [P] [US1] Create `ITESTS/Cms/Items/ItemApiTests.cs` covering create, get, save draft and slug availability:
  - **Create**: 201 with `revision` 1 and status `draft`.
  - **Round trip** (SC-002): one value of every type and setting is read back equal as JSON.
  - **Malformed values**: each one gives `cms.invalid_item` with the report, and nothing is stored.
  - **Incomplete drafts**: missing required values and broken settings are stored, with the report and `attention`.
  - **Slugs**: a taken slug (case-insensitive) gives `cms.item_slug_taken` with a suggestion; `slug-availability` answers and suggests `-2`; the item's own slug counts as free.
  - **Conflicts**: a stale `revision` gives `cms.item_conflict`. A stale `schemaRevision` after a field rename gives `cms.schema_changed`, but not after a details-only change (IT-07).
  - **Limits**: a 1 MiB + 1 body gives 413 `cms.item_too_large`; duplicate JSON properties give 400.
  - **Roles**: Editor, Designer and Administrator are allowed; a user with no role gets 404 on every endpoint.
  - **Events**: `Cms.ItemCreated` and `Cms.ItemDraftSaved` carry ids only.
- [X] T034 [P] [US1] Create `ITESTS/Cms/Items/ItemWriteGateTests.cs`. While a schema change holds the collection (014's `TestMigrationObserver`), create, save and delete give `cms.items_being_updated`, and the item is unchanged (FR-006).
- [X] T035 [P] [US1] Create `ADMINTESTS/cms/items/ItemFormView.spec.ts` and `ADMINTESTS/cms/items/ItemField.spec.ts`:
  - one input per field type, in schema order, with help, a required mark and the remaining length;
  - the slug follows the name until edited, and shows the taken-slug message from `slug-availability`;
  - local problems are shown before saving, and server report problems are mapped to fields and to a summary with links;
  - `aria-invalid` and `aria-describedby` are set;
  - the unsaved-changes guard runs on leave;
  - `cms.schema_changed` reloads the schema, keeps values of fields that still exist and highlights changed ones;
  - pasting HTML into `RichTextInput` keeps only what `rtDoc` allows (no raw markup in the value);
  - axe reports no serious or critical issues.

### Implementation for User Story 1

- [X] T036 [US1] Create the slices:
  - `CMS/Features/Items/CreateItem/CreateItem.cs` (`POST`, `cms.items.edit`): `ItemWrites` with no item; `Item.Create`; refused on `HasMalformed`; history `created`.
  - `CMS/Features/Items/GetItem/GetItem.cs` (`GET`, view): `ItemReads` with the report, schedules, member names and `schemaRevision`.
  - `CMS/Features/Items/SaveItemDraft/SaveItemDraft.cs` (`PUT`, `cms.items.edit`).
  - `CMS/Features/Items/CheckSlug/CheckSlug.cs` (`GET slug-availability`, view).

  Bodies are capped at 1 MiB by an attribute modelled on `DraftSizeLimitAttribute`, and duplicate JSON properties are refused as in `SaveCollection`. Register the slices and add them to `EndpointCatalog`. Makes T033 and T034 pass.
- [X] T037 [US1] Create `CMS/Features/Items/ListItems/ListItems.cs` (`GET`, view), **first version**: newest change first, `page` and `pageSize` (25/50/100), and `total`, using `ItemSummaryResponse`. US3 adds search, filters and sorts in `CMS/Infrastructure/ItemListQuery.cs`.
- [X] T038 [US1] Create `CMS/Features/Items/ReindexItemsForMedia/ReindexItemsForMedia.cs`: idempotent handlers for `Media.FileDeleted` and `Media.FileChanged` that re-index, through `ItemWrites`, the items `item_media` lists for the file. A busy collection makes the handler throw, so the outbox retries. Register them in `CMS/CmsModule.cs`. Add a case to `ITESTS/Cms/Items/ItemApiTests.cs`: deleting the cover image's media file gives the item `media_missing` attention.
- [X] T039 [P] [US1] Create the inputs under `ITEMUI/inputs/`:
  - `TextInput.vue` (single line or several lines, remaining length);
  - `RichTextInput.vue`, a standalone TipTap editor from `ADMIN/editor/richtext/tiptap.ts`, `RichTextToolbar.vue` and `toContractRichText.ts`, emitting `rtDoc`;
  - `NumberInput.vue` (decimals, min and max shown);
  - `BooleanSwitch.vue` (`role="switch"`);
  - `DateInput.vue` (date, or date and time in the member's zone, converted to and from UTC `…Z`);
  - `MediaInput.vue`, wrapping `ADMIN/editor/panel/controls/MediaControl.vue`'s behaviour and `ADMIN/features/media/MediaBrowser.vue`, limited by `accept`, with clear;
  - `OptionSelect.vue` (options in order, clear).

  Each takes `{ field, value, error, id }` and emits `update:value`, where `undefined` means no value.
- [X] T040 [US1] Create `ITEMUI/ItemField.vue`, which picks the input by type and renders the label, help, required mark and error with ids for `aria-describedby`. Reference and multi-reference fields render the chosen ids as plain read-only chips with Clear; US4 replaces them with pickers.
- [X] T041 [US1] Create `ITEMUI/ItemFormView.vue` for the new and edit routes:
  - loads the collection schema and the item;
  - keeps the slug suggested until it is edited, with a debounced `slug-availability` check;
  - validates locally with `itemValueRules.ts`;
  - has a Save draft button and a report summary with links to fields;
  - guards against leaving with unsaved changes (router leave hook and `beforeunload`);
  - handles `cms.item_conflict` and `cms.schema_changed` with a notice in the style of 014's builder;
  - shows `cms.items_being_updated` as a retryable message that keeps the input;
  - treats `cms.collection_not_found` and `cms.item_not_found` on save the same: the item no longer exists.

  Makes T035 pass.
- [X] T042 [US1] Create `ITEMUI/ItemListView.vue`, **first version**: a table of name (or slug), slug, status, attention, updated time and updater, a pager with page size and total, a **New item** button, and rows linking to the form. Replace the T032 placeholders.

**Checkpoint**: quickstart §1 passes. **MVP.**

---

## Phase 4: User Story 2 - Publishing and unpublishing an item (Priority: P1)

**Goal**: publish (with versions and retention), unpublish, statuses, and the warnings about
unpublished references and referrers.

**Independent Test**: publish, change the draft, and confirm the published data is unchanged
until the next publish. Publishing with a missing required value is refused, naming the
field. Unpublishing keeps the draft (quickstart §2).

### Tests for User Story 2 ⚠️

- [ ] T043 [P] [US2] Create `ITESTS/Cms/Items/ItemDraftAndPublishTests.cs`:
  - **Publish**: creates version 1 and sets `published`, `published_slug`, `live_version`, `published_at_utc` and `published_by`; status becomes `published`.
  - **Drafts never touch published data** (SC-004): every later draft write (save, copy to draft, duplicate) leaves `published` byte-for-byte unchanged, and the status becomes `changed`.
  - **Refusals**: publishing with attention gives `cms.item_needs_attention`, listing every field.
  - **Unpublished references**: publishing with them is allowed, with `unpublishedReferences` listed.
  - **Unpublish**: clears the published values and slug, keeps versions, logs `unpublished`, and cancels a pending scheduled unpublish. Unpublishing a draft gives `cms.item_not_published`.
  - **Retention**: 52 publishes keep 50 versions, including the live one.
  - **Referrers**: `GET …/referrers` lists up to 50 plus the total with names and flags; `published=true` counts only references in published values.
  - **Events**: `Cms.ItemPublished` (with `slugChanged`) and `Cms.ItemUnpublished` carry ids only.
- [ ] T044 [P] [US2] Create `ITESTS/Cms/Items/ItemConcurrencyTests.cs`:
  - two concurrent publish requests: one succeeds; the other gets `cms.item_conflict` with `currentRevision`; retried with it, it produces version 2. Both versions are complete, in commit order;
  - a concurrent save and publish give one `cms.item_conflict`;
  - two items racing for one slug give exactly one `cms.item_slug_taken`;
  - publishing a slug equal to another item's published slug is refused.
- [X] T045 [P] [US2] Create `ADMINTESTS/cms/items/ItemPublishBar.spec.ts`:
  - Publish shows the unpublished-references warning before confirming;
  - Unpublish shows the referrer count from `referrers?published=true`;
  - a `cms.item_needs_attention` refusal focuses the summary;
  - `ItemStatusBadge` shows draft, published and changed;
  - a `cms.item_conflict` on Publish reloads and retries once, saying when the content changed;
  - a changed slug on a published item adds the address-change warning to the confirmation;
  - axe reports no serious or critical issues.

### Implementation for User Story 2

- [ ] T046 [US2] Add `Publish`, `Unpublish` and retention to `CMS/Domain/Item.cs`, with `ItemVersion` creation in `CMS/Domain/ItemVersion.cs`, the `published` and `unpublished` history entries in `CMS/Domain/ItemHistoryEntry.cs`, and the version counter. Add the cases to `UTESTS/Cms/Items/ItemTests.cs`.
- [ ] T047 [US2] Create the slices:
  - `CMS/Features/Items/PublishItem/PublishItem.cs` (`POST publish`, `cms.items.publish`): it refuses on a non-empty report, computes `unpublishedReferences` from `item_references` joined to target statuses, and returns them.
  - `CMS/Features/Items/UnpublishItem/UnpublishItem.cs`.
  - `CMS/Features/Items/ListItemReferrers/ListItemReferrers.cs` (`GET referrers`, view): up to 50 plus the total, with names, collection and field names, and the flags; `published=true` filters.

  Register them and add them to `EndpointCatalog`. Makes T043 and T044 pass.
- [ ] T048 [US2] Extend `CMS/Infrastructure/ItemMigrator.cs` to also migrate `item_versions.values` (IT-12): the same `FieldPlan`s **without fill-ins**, in batches of 500, loading only versions that hold a value in a changed field (`EF.Functions.JsonExistAny` on the `values` element, with an in-memory filter fallback and a code comment). Then refresh `item_media.in_history`. Create `ITESTS/Cms/Items/ItemSchemaMigrationTests.cs` with: a removed field's values leave every version, a lossy conversion converts versions, a fill-in is not written to versions, and a rename writes no version.
- [X] T049 [P] [US2] Create `ITEMUI/ItemStatusBadge.vue` (status, attention and, from US6, schedules) and `ITEMUI/ItemPublishBar.vue` (Save draft, Publish and Unpublish, each with its warning dialog, using the 012 `ConfirmDialog`). On `cms.item_conflict` from Publish, the bar reloads the item and retries once, telling the member if the content changed. When the draft slug differs from the published slug, the publish confirmation says the item's published address will change (spec Edge Cases). Use them in `ItemFormView.vue` and in the list's status column. Makes T045 pass.

**Checkpoint**: quickstart §2 passes. Items can be published.

---

## Phase 5: User Story 3 - Finding items in a large collection (Priority: P1)

**Goal**: search, filters, sorts and numbered pages over 10,000 items within 1 s, with the
list's state in the page address.

**Independent Test**: in 10,000 items, search by name and by slug, use every filter and sort,
and page through. Results are correct, complete and stable, and fast (quickstart §3).

### Tests for User Story 3 ⚠️

- [ ] T050 [P] [US3] Create `ITESTS/Cms/Items/ItemListQueryTests.cs`:
  - **Search**: matches name or slug, ignoring case and accents (`héllo` = `HELLO`), with `%` and `_` taken literally; text field content is not searched (clarification Q5).
  - **Filters**: `status`, `attention`, `scheduled`, and `field[...]` for option, boolean (where `false` also matches no value), reference and multi-reference (contains), combined with AND.
  - **Sorts**: every column, plus number and date fields, in both directions, with missing values last and `id` as the tie-breaker.
  - **Paging**: every item appears on exactly one page across all pages, and `total` is right.
  - **Refusals**: a filter on a text field, a sort on a rich text field, an unknown field id, page size 30 and a 101-character term each give `cms.invalid_query` with `parameter`.
- [X] T051 [P] [US3] Create `ADMINTESTS/cms/items/ItemListView.spec.ts`:
  - search, filters, sort, page and page size are written to and restored from the route query, and returning from an item restores them;
  - the search is debounced;
  - sortable headers carry `aria-sort`;
  - filters are built from the schema's option, boolean and reference fields;
  - the empty state and the "no results" state are shown;
  - axe reports no serious or critical issues.

### Implementation for User Story 3

- [ ] T052 [US3] Create `CMS/Infrastructure/ItemListQuery.cs` (IT-13, IT-14):
  - parses and checks the query against the schema;
  - search: `SearchText.LikePattern`, with `EF.Functions.Like` and the escape;
  - filters: column predicates, and draft-value predicates through Npgsql's `JsonElement` translation (`GetProperty("values").GetProperty(id).GetString()` and `GetBoolean()`, and `EF.Functions.JsonContains` for multi-references);
  - sorts: columns (`name` on `left(name, 256)`, matching its index), `GetDecimal()` for numbers, `GetString()` for dates, with `NULLS LAST`, then `id`;
  - paging: `Skip`/`Take` plus `CountAsync`.

  If a translation fails, stop and record the case in research.md rather than adding raw SQL. Use it from `ListItems.cs`. Makes T050 pass.
- [X] T053 [P] [US3] Create `ITEMUI/listQuery.ts` (route query ↔ typed list query), `ITEMUI/ItemFilters.vue` and `ITEMUI/ItemPager.vue`. Complete `ITEMUI/ItemListView.vue` with search, filters, sortable headers and the pager. Makes T051 pass.

**Checkpoint**: quickstart §3 passes, except the SC-003 timing, which is measured in Polish
(T081).

---

## Phase 6: User Story 4 - Linking items with reference pickers (Priority: P2)

**Goal**: pick, reorder and remove references with the mouse or the keyboard alone, and
show the names and statuses of chosen items.

**Independent Test**: pick, reorder and remove references by mouse and by keyboard, save, and
get the stored order back. Another collection's item, the item itself and more than 100 are
refused (quickstart §4).

### Tests for User Story 4 ⚠️

- [ ] T054 [P] [US4] Add to `ITESTS/Cms/Items/ItemApiTests.cs`:
  - `POST items/lookup` returns names, slugs, statuses and collections, and lists unknown ids, including site B's ids, in `missing`;
  - more than 200 ids gives 400;
  - saving a reference to the item itself gives `reference_self`.
- [X] T055 [P] [US4] Create `ADMINTESTS/cms/items/ReferencePicker.spec.ts` and `ADMINTESTS/cms/items/MultiReferencePicker.spec.ts`:
  - the picker searches the target collection and pages through results;
  - options use `role="listbox"`, with arrow keys and Enter;
  - a single reference replaces the earlier choice, and Clear removes it;
  - in a multi-reference, Move up, Move down, Remove and Alt+Arrow change the order, and moves are announced through `aria-live`;
  - the item itself and items already chosen are disabled;
  - the limit of 100 is enforced;
  - unpublished items are tagged "Not published", and missing ones "Missing";
  - axe reports no serious or critical issues.

### Implementation for User Story 4

- [ ] T056 [US4] Create `CMS/Features/Items/LookupItems/LookupItems.cs` (`POST /sites/{siteId}/items/lookup`, view; up to 200 ids, one `ForSite<Item>` query joined to collection names). Register it and add it to `EndpointCatalog`. Makes T054 pass.
- [X] T057 [US4] Create `ITEMUI/ReferencePickerDialog.vue` (a dialog over the target collection's `ListItems` with `q` and `pageSize=25`, the listbox semantics of IT-21, and a disabled set), `ITEMUI/ReferencePicker.vue` (single) and `ITEMUI/MultiReferencePicker.vue` (an ordered list with native drag events and the keyboard rule of 014's `FieldList.vue`: Alt+↑/↓, buttons and an `aria-live` region). Both resolve names through `items/lookup`. Replace the read-only chips in `ITEMUI/ItemField.vue`. Makes T055 pass.

**Checkpoint**: quickstart §4 passes.

---

## Phase 7: User Story 5 - Deleting and duplicating items safely (Priority: P2)

**Goal**: delete with referential integrity, duplicate, and show item usage in the media
library.

**Independent Test**: delete items referred to from drafts and from published values; only
the unreferenced item is deleted, and
refusals list the referrers. Duplicate an item and check its copy (quickstart §5, §9).

### Tests for User Story 5 ⚠️

- [ ] T058 [P] [US5] Create `ITESTS/Cms/Items/ItemReferenceIntegrityTests.cs` (SC-005):
  - **Refused deletes**: an item referred to from another item's draft, published values or pending scheduled content gives `cms.item_referenced`, with referrers (names, collection, field, flags) and the total, up to 50 of 60 listed.
  - **Self-references**: a self-reference cannot be stored (`reference_self` on save), so it never blocks deletion.
  - **Race**: a reference added in a concurrent transaction is caught by the `NO ACTION` foreign key and reported as `cms.item_referenced`.
  - **Collection delete**: deleting a whole collection whose items refer to each other still succeeds (014).
  - **Delete**: `Cms.ItemDeleted` carries `wasPublished`, and the slug is free again.
  - **No dangling references** remain after a randomised sequence of saves, publishes, unpublishes and deletes.
- [ ] T059 [P] [US5] Create `ITESTS/Cms/Items/ItemMediaUsageTests.cs`:
  - the media library's usage summary and list include item places (draft, live, scheduled and history flags; item and collection names);
  - item places are shown only with `cms.collections.view`;
  - deleting a file used by a published item without `publishing.manage` gives `media.in_live_use`;
  - any item use needs `confirmInUse`.
- [ ] T060 [P] [US5] Add duplicate cases to `ITESTS/Cms/Items/ItemApiTests.cs`: the copy has the same draft values, a name with " (copy)" shortened to fit, and the slug `-copy` or `-copy-2`; it has no published values, versions or schedules, and status `draft`.
- [X] T061 [P] [US5] Create `ADMINTESTS/cms/items/DeleteItemDialog.spec.ts`:
  - the confirmation says when the item is live;
  - a refusal lists the referrers with links;
  - Duplicate opens the copy;
  - axe reports no serious or critical issues.

### Implementation for User Story 5

- [ ] T062 [US5] Create the slices:
  - `CMS/Features/Items/DeleteItem/DeleteItem.cs` (`DELETE ?revision=`, `cms.items.edit`): reads referrers and refuses with up to 50 plus the total; otherwise `Item.Delete`, and the foreign-key race is mapped in `ItemWrites`.
  - `CMS/Features/Items/DuplicateItem/DuplicateItem.cs` (`POST duplicate`, `cms.items.edit`): the slug from `SlugRules` copy suggestions, retried on `cms.item_slug_taken` up to 3 times.

  Register them and add them to `EndpointCatalog`. Makes T058 and T060 pass.
- [ ] T063 [US5] Create `CMSC/Media/IItemMediaUsageReader.cs` with the records of module-surface.md §2, and `CMS/Infrastructure/ItemMediaUsageReader.cs` over `item_media` with `ForSite`, registered in `CMS/CmsModule.cs`.
- [ ] T064 [US5] Use `IItemMediaUsageReader` in Media:
  - `MEDIA/Infrastructure/MediaFileMapper.cs`: usage counts include items;
  - `MEDIA/Features/GetFileUsage/GetFileUsage.cs`: item places after pages and components, only when the caller has `cms.collections.view`;
  - `MEDIA/Features/DeleteFile/DeleteFile.cs` and `MEDIA/Features/ReplaceFile/ReplaceFile.cs`: live item use counts as live use, and any item use as in use (013 MV-06, unchanged).

  Update `specs/013-media-library-variants/contracts/readers.md`. Makes T059 pass.
- [ ] T065 [P] [US5] Create `ITEMUI/DeleteItemDialog.vue`, and add Duplicate and Delete to `ITEMUI/ItemPublishBar.vue`. In `ADMIN/features/media/UsageList.vue`, show item places (item name or slug, collection, flags) linking to the item form. Makes T061 pass.

**Checkpoint**: quickstart §5 and §9 pass.

---

## Phase 8: User Story 6 - Scheduling a publish or an unpublish (Priority: P2)

**Goal**: scheduled publishes and unpublishes that capture the draft, run exactly once, and
survive restarts, concurrent instances and schema changes.

**Independent Test**: a scheduled publish publishes the content captured at scheduling, not a
later draft. A scheduled unpublish happens. Across a restart, each runs once (quickstart §6).

### Tests for User Story 6 ⚠️

- [ ] T066 [P] [US6] Create `ITESTS/Cms/Items/ItemScheduleTests.cs`:
  - **Setting**: a publish schedule captures the draft and `scheduledDiffersFromDraft` follows later saves; a draft with attention gives `cms.item_needs_attention`; a past time gives `cms.schedule_in_past`; an unpublish not later than the publish gives `cms.schedule_order`; an unpublish of a draft with no pending publish gives `cms.item_not_published`.
  - **Changing**: reschedule bumps the revision and the old message does nothing; `recapture` replaces the content; cancel works, with nothing pending gives `cms.schedule_not_found`, and with a stale revision gives `cms.item_conflict`.
  - **Manual actions**: a manual publish, unpublish or rollback without `cancelSchedule` gives `cms.schedule_pending`, and with it cancels the schedule and records it.
  - **Failure**: a captured content that becomes invalid (a field made required, no fill-in) fails at run time, records `scheduleFailed` and puts the item in attention.
  - **Schema changes**: they migrate captured content, including fill-ins.
  - **History**: `scheduled`, `rescheduled` and `scheduleCancelled` are recorded.
- [ ] T067 [P] [US6] Create `ITESTS/Cms/Items/ItemScheduledExactlyOnceTests.cs` (SC-006), modelled on 012's `ScheduledPublishExactlyOnceTests`:
  - two dispatchers run it once;
  - a redelivery does nothing;
  - stopping across the due time, it runs once after start;
  - a schema change holding the collection at the due time makes the run retry, and it succeeds afterwards;
  - each case runs within a minute of the due time, or of start-up.
- [X] T068 [P] [US6] Create `ADMINTESTS/components/ZonedDateTimeInput.spec.ts` (names the member's zone, converts to and from UTC, refuses past times) and `ADMINTESTS/cms/items/ItemScheduleDialog.spec.ts` (set, move, recapture, cancel, the "draft differs" notice, axe).

### Implementation for User Story 6

- [ ] T069 [US6] Complete `CMS/Domain/ItemSchedule.cs` (`ForPublish` with capture, `ForUnpublish`, `Reschedule`, `Recapture`, `IsDue`, `Complete`, `Cancel`, `Fail`, mirroring `src/Modules/Pages/QuickBuilder.Pages/Domain/PublishSchedule.cs`). Add `Schedule`, `CancelSchedule`, the cancellation of a pending publish by manual actions (`cancelSchedule`), and the history kinds to `CMS/Domain/Item.cs`. Add the cases to `UTESTS/Cms/Items/ItemTests.cs`.
- [ ] T070 [US6] Create `CMS/Features/Items/RunItemSchedule/ItemScheduleDue.cs` (internal event `Cms.ItemScheduleDue`, registered with `EventTypeRegistration`) and `RunItemScheduleHandler.cs`, modelled on `src/Modules/Pages/QuickBuilder.Pages/Features/RunDueSchedule/` and `ScheduleWrites.RunAsync`:
  - its own scope;
  - claims the schedule by a guarded update inside `ItemWrites` (owner = who set it, kind `scheduled`);
  - a domain refusal rolls back, then marks the schedule `failed` with `scheduleFailed` and re-indexes the item;
  - `cms.items_being_updated` throws, so the outbox retries.
- [ ] T071 [US6] Create `CMS/Features/Items/ScheduleItem/ScheduleItem.cs` (`PUT schedules/{action}`, `cms.items.publish`; writes the due message with `OutboxMessage.From(..., availableAtUtc: dueAtUtc)`) and `CMS/Features/Items/CancelItemSchedule/CancelItemSchedule.cs` (`DELETE ?revision=`, guarded like a run). Add `cancelSchedule` handling to `PublishItem.cs` and `UnpublishItem.cs`. Register the slices and add them to `EndpointCatalog`. Makes T066 and T067 pass.
- [ ] T072 [US6] Extend `CMS/Infrastructure/ItemMigrator.cs` to migrate `item_schedules.captured_values` of pending publishes, **with fill-ins**, in the same batches (IT-12). Add the case to `ITESTS/Cms/Items/ItemSchemaMigrationTests.cs`.
- [X] T073 [P] [US6] Extract `ADMIN/components/ZonedDateTimeInput.vue` from `ADMIN/features/publishing/ScheduleDialog.vue` and use it there. The existing `ScheduleDialog` tests must pass unchanged. Create `ITEMUI/ItemScheduleDialog.vue`, add Schedule to `ITEMUI/ItemPublishBar.vue`, show pending schedules in `ITEMUI/ItemStatusBadge.vue` and the list, and add the `scheduled` filter option to `ITEMUI/ItemFilters.vue`. Makes T068 pass.

**Checkpoint**: quickstart §6 passes.

---

## Phase 9: User Story 7 - Seeing an item's history and rolling back (Priority: P3)

**Goal**: the history of versions and events, rollback that is checked against today's
rules, and copying a version into the draft.

**Independent Test**: publish three times, roll back to version 1. The published data equals
version 1, a version 4 records the rollback, and the draft is unchanged (quickstart §7).

### Tests for User Story 7 ⚠️

- [ ] T074 [P] [US7] Create `ITESTS/Cms/Items/ItemRollbackTests.cs`:
  - **Rollback** creates version 4 of kind `rollback`, with `restoredFrom` 1, and leaves the draft unchanged.
  - **Refusals**: a version referring to a deleted item gives `cms.version_not_publishable` (`reference_missing`); a version whose slug is now used by another item is refused the same way; a version made invalid by a newly required field is refused; an unknown version gives `cms.version_not_found`.
  - **Copy to draft** always works and publishes nothing.
  - **History** lists versions newest first with `live`, and events with members, and holds no values.
- [X] T075 [P] [US7] Create `ADMINTESTS/cms/items/ItemHistoryPanel.spec.ts`: versions and events listed, rollback confirmation with refusal reasons, copy to draft reloading the form, the live version marked, and axe.

### Implementation for User Story 7

- [ ] T076 [US7] Add `Rollback(number)` and `CopyToDraft(number)` to `CMS/Domain/Item.cs`, with the `rolledBack` and `copiedToDraft` history kinds. Create the slices `CMS/Features/Items/GetItemHistory/GetItemHistory.cs` (view), `CMS/Features/Items/RollbackItem/RollbackItem.cs` (`cms.items.publish`; validates the version through `ItemValidation`, including the slug; honours `cancelSchedule`) and `CMS/Features/Items/CopyVersionToDraft/CopyVersionToDraft.cs` (`cms.items.edit`). Register them and add them to `EndpointCatalog`. Makes T074 pass.
- [X] T077 [P] [US7] Create `ITEMUI/ItemHistoryPanel.vue` (a side panel in `ItemFormView.vue`), modelled on `ADMIN/features/publishing/HistoryView.vue`'s layout. Makes T075 pass.

**Checkpoint**: quickstart §7 passes. All stories are done.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T078 [P] Extend `ITESTS/Isolation/Probes/CmsIsolationProbe.cs`: from site B, every item endpoint, including lookup, referrers and history, with site A's ids gives 404 or `missing`. `IsolationCoverageTests` must stay green.
- [ ] T079 [P] Extend `ATESTS/AuthorizationDeclarationTests` coverage, and confirm that every new action declares exactly one capability.
- [ ] T080 [P] Add to `ITESTS/Cms/Items/ItemSchemaMigrationTests.cs` the cases not yet covered: an interrupted migration (014's `TestMigrationObserver`) leaves items, versions, schedules and derived rows exactly as before (014 SC-005 still holds); and the indexer output equals a fresh recomputation after every scenario.
- [ ] T081 [P] Create `STESTS/ItemListPerformanceTests.cs` (Performance category): 10,000 items, p95 under 1 s for the list, search, each filter and sort, and the picker search (SC-003); item save and publish under 300 ms p95 (plan Performance Goals).
- [ ] T082 [P] Create `STESTS/ItemMigrationPerformanceTests.cs` (Performance category): 10,000 items with 3 versions each and 100 pending schedules; a field removal and a lossy type change complete within 30 s (014 SC-003, IT-12). If it fails, apply IT-12's fallback and record it here.
- [ ] T083 Add "create an item and publish it" to the release smoke journey in `STESTS/ReferenceDeploymentSmokeTests.cs`.
- [ ] T084 [P] Update `qb.ps1` `seed-items`: create items through `POST …/items` with generated values, slugs from names, and `-Publish <percent>` publishing that share. Update the help text.
- [ ] T085 [P] Update the docs:
  - `docs/operations.md`: items, who can publish them, schedules, media in live use through items, and that schema changes also rewrite kept versions;
  - `docs/backlog.md`: field-content search (clarification Q5), bulk item actions, and in the E-6 entry, "versions are migrated too";
  - `README.md`'s feature list, if it has one.
- [ ] T086 Run the whole of `quickstart.md` by hand on the reference compose setup, including SC-001 with a stopwatch and SC-007 keyboard-only, and record the results in `specs/015-cms-items/quickstart-results.md`.
- [ ] T087 [P] Run `dotnet test` with coverage. The item publish path (`ItemWrites`, `Item`, `PublishItem`, `RunItemScheduleHandler`) must be at ≥ 80 % line coverage (Principle X). Run `npm run lint`, `npm run type-check` and `npm test`, and check that the formatting and analyzer gates are clean.
- [ ] T088 Scan the logs of a full test run for item values, names and slugs: none may appear (FR-032). Then open the PR closing #15, with the plan's operator impact and upgrade notes in the description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup. Blocks every story.
- **US1 (Phase 3)**: depends on Foundational. The MVP.
- **US2 (Phase 4)**: depends on US1 (form, get and save).
- **US3 (Phase 5)**: depends on US1's list. It can run in parallel with US2. Its status and
  scheduled filters only gain values once US2 and US6 exist.
- **US4 (Phase 6)**: depends on US1's form and US3's list endpoint (the picker uses `q`).
- **US5 (Phase 7)**: depends on US1. It uses the referrers slice from US2 (T047).
- **US6 (Phase 8)**: depends on US2 (publish and unpublish operations).
- **US7 (Phase 9)**: depends on US2 (versions). The rollback and schedule interaction needs
  US6.
- **Polish (Phase 10)**: after all stories.

### Within Each User Story

- Tests first, and they must fail.
- Then domain, slices, migration hooks and admin.
- A story is done when its quickstart section passes.

### Parallel Opportunities

- **Setup**: T003 runs alongside T002.
- **Foundational**:
  - T006, T008, T009, T010, T015, T016 and T017 (tests and examples) are parallel;
  - T023, T024, T025, T027, T028, T029 and T030 are parallel once T004 lands.
- **Stories**: after US1, US2 and US3 can proceed in parallel; after US2, US5 and US6 can
  too.
- **Within a story**: every test task marked [P] is parallel, and admin inputs (T039) are
  parallel with the slices (T036, T037).

---

## Parallel Example: Foundational domain

```text
Task: "T008 SlugRulesTests in UTESTS/Cms/Items/SlugRulesTests.cs"
Task: "T009 ItemStatusTests in UTESTS/Cms/Items/ItemStatusTests.cs"
Task: "T010 ItemValidationTests in UTESTS/Cms/Items/ItemValidationTests.cs"
Task: "T015 golden item values in CONTRACTS/collection-schema/examples/items/"
Task: "T017 ItemIndexerTests in ITESTS/Cms/Items/ItemIndexerTests.cs"
```

## Parallel Example: User Story 1

```text
Task: "T033 ItemApiTests in ITESTS/Cms/Items/ItemApiTests.cs"
Task: "T034 ItemWriteGateTests in ITESTS/Cms/Items/ItemWriteGateTests.cs"
Task: "T035 ItemFormView.spec.ts and ItemField.spec.ts in ADMINTESTS/cms/items/"
Task: "T039 inputs in ITEMUI/inputs/"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. Editors can write and save items of every field
   type.

### Incremental Delivery

1. Setup and Foundational.
2. US1: write items. **Demo.**
3. US2 (publish) and US3 (find). All P1 stories are done: items can be written, published
   and found.
4. US4 (pickers), US5 (delete, duplicate, media usage), US6 (schedules).
5. US7 (history and rollback).
6. Polish, the manual quickstart run, and the PR closing #15.

One PR, as with 010–014, so there is no feature flag (plan, Principle XI).

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US7.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #15.
- **Contract and registry changes** (T015, T025, T064) land in the same PR as their tests.
- **Avoid**:
  - writing items anywhere but `ItemWrites`, `ItemIndexer`, `ItemMigrator` and the
    schedule runner;
  - raw SQL for list queries (stop and record instead, T052);
  - a second `AcrossAllSites` call;
  - values, names or slugs in logs and events;
  - local time zones on the server: the API is UTC, and the admin converts.
