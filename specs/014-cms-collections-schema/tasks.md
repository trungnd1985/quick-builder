---

description: "Task list for 014-cms-collections-schema"
---

# Tasks: CMS Collections and Schema Builder

**Input**: Design documents from `/specs/014-cms-collections-schema/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - golden files for schema validation;
  - 80 % coverage on critical paths (schema migration is one).
- The spec measures behaviour through automated checks: SC-002 … SC-007.
- Within each story, test tasks come first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US6 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US6)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CMSC` = `src/Modules/Cms/QuickBuilder.Cms.Contracts`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `HOST` = `src/QuickBuilder.Host`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `CMSUI` = `src/admin/src/features/cms`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`

**Conventions**:
- **Backend slices** follow the one-folder-per-slice rule, modelled on
  `src/Modules/Pages/QuickBuilder.Pages/Features/CreateComponent/CreateComponent.cs`. Every
  controller action:
  - is routed under `api/v1/cms/sites/{siteId:guid}/collections`;
  - declares exactly one `[RequireCapability(...)]` from `CMS/CmsCapabilities.cs` (CS-15);
  - dispatches through `IDispatcher`, and its handler first checks the site with
    `CmsAccess.SiteExistsAsync`, returning `sites.not_found`;
  - refuses through `CMS/CmsErrorResults.cs` with the codes in
    [data-model.md §5](./data-model.md);
  - is registered in `CMS/CmsModule.cs` and listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **Admin reads** of the site named in the route use `CmsDbContext.ForSite<T>(siteId)`,
  which is `AcrossAllSites` plus an explicit `SiteId` predicate, as in
  `src/Modules/Editor/QuickBuilder.Editor/Infrastructure/EditorDbContext.cs`. No other
  `AcrossAllSites` call is allowed.
- **Row locks** are taken only in `CMS/Infrastructure/CollectionLocks.cs` (CS-09, CS-10).
- **Item writes** go only through `CMS/Infrastructure/ItemMigrator.cs` in this feature, and
  through `CollectionWriteGate` from #15 on.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task that uses them. `npm run lint` enforces this.
- **Logs, events and history** carry ids, counts and kinds only. They never contain item
  values, item titles, or field or collection names, except for the names stored in the
  history summary, which data-model §3.4 allows.

**References**:
- research decisions: `CS-xx`;
- tables, rules, events and error codes: [data-model.md](./data-model.md);
- schema and value formats, change rules and limits:
  [contracts/collection-schema.md](./contracts/collection-schema.md);
- endpoints: [contracts/cms-api.yaml](./contracts/cms-api.yaml);
- projects, capabilities and events: [contracts/module-surface.md](./contracts/module-surface.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, the two new projects and their wiring. Nothing here has
behaviour.

- [ ] T001 Commit `specs/014-cms-collections-schema/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `014-cms-collections-schema`, referencing #14 (plan.md "Development Workflow").
- [ ] T002 Create the two projects (module-surface.md §1) and add both to `QuickBuilder.slnx`:
  - **`CMSC/QuickBuilder.Cms.Contracts.csproj`**: references `Shared.Kernel` only. Add an `EmbeddedResource` of `..\..\..\..\docs\contracts\collection-schema\*.schema.json` with `LogicalName="QuickBuilder.Cms.Contracts.CollectionSchema.%(Filename)%(Extension)"`, plus a header comment, as `PAGESC/QuickBuilder.Pages.Contracts.csproj` does.
  - **`CMS/QuickBuilder.Cms.csproj`**: copy `src/Modules/Pages/QuickBuilder.Pages/QuickBuilder.Pages.csproj`'s framework reference and packages. Add project references to `Shared.Kernel`, `Shared.Infrastructure`, `Cms.Contracts`, `Pages.Contracts`, `Media.Contracts`, `Sites.Contracts` and `Identity.Contracts`, and `InternalsVisibleTo` for the integration, unit and architecture test projects.
- [ ] T003 Create `CMS/CmsModule.cs` with `public static IServiceCollection AddCmsModule(this IServiceCollection, IConfiguration)`, registering nothing yet. Then wire the host:
  - `HOST/QuickBuilder.Host.csproj`: a project reference to `CMS`;
  - `HOST/Program.cs`: `builder.Services.AddCmsModule(builder.Configuration);` after `AddEditorModule`, and `.AddApplicationPart(typeof(CmsModule).Assembly)`;
  - `Dockerfile`'s restore stage: two `COPY src/Modules/Cms/QuickBuilder.Cms*/*.csproj` lines beside the others.
- [ ] T004 [P] Add the Cms projects as references in `ITESTS/QuickBuilder.IntegrationTests.csproj`, `UTESTS/QuickBuilder.UnitTests.csproj` and `ATESTS/QuickBuilder.ArchitectureTests.csproj`.
- [ ] T005 [P] Extend `ATESTS/ModuleBoundaryTests.cs`:
  - add `typeof(Cms.CmsModule).Assembly` to `ModuleImplementations` and `typeof(Cms.Contracts.CollectionSchema).Assembly` to `ModuleContracts` (the latter compiles after T010);
  - `QuickBuilder.Cms` may reference `Pages.Contracts` and `Media.Contracts`;
  - no module references `QuickBuilder.Cms` or `QuickBuilder.Cms.Contracts`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the contract file, its golden examples and `ValidateDefinition`;
- the pure domain: schema document, rules, diff and change classification;
- item values;
- the database, migration, module registration, capabilities, errors and events;
- locks, isolation, the upgrade test and the test item builder;
- the admin client and its validator port.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### The contract

- [ ] T006 Create `CONTRACTS/collection-schema/v1.schema.json` (JSON Schema 2020-12) from contracts/collection-schema.md §2–§3, using only the keywords in `SchemaSubsetValidator.SupportedKeywords`, with ECMAScript-safe patterns (no lookbehind, named groups or `\p{}`):
  - **The root** is the schema document: `schemaVersion` (`const: 1`), optional `titleFieldId`, and `fields` (`maxItems: 100`, items `$ref: #/$defs/field`).
  - **`$defs/field`** is a `oneOf` of `textField`, `richTextField`, `numberField`, `booleanField`, `dateField`, `imageField`, `fileField`, `optionField`, `referenceField` and `multiReferenceField`, each with the common properties, its `type` const, its settings, and `additionalProperties: false`.
  - **Ids**: `$defs/fieldId` (`^(f[1-9][0-9]{0,5}|~[1-9][0-9]{0,2})$`), `$defs/optionId` and `$defs/option`.
  - **Item values**: `$defs/itemValues` (`{ schemaVersion, values }`, `values` an object whose properties match `$defs/fieldId`), plus `textValue`, `numberValue`, `booleanValue`, `dateValue`, `dateTimeValue`, `mediaValue`, `optionValue`, `referenceValue` and `multiReferenceValue` (`minItems: 1`, `maxItems: 100`).
- [ ] T007 [P] Create the golden examples under `CONTRACTS/collection-schema/examples/`:
  - **`valid/`**: an empty schema, one field of every type with every setting, 100 fields, a 200-option field, a self-reference, and item values documents for each type;
  - **`invalid/`**: one file per rule in contract §5, plus schema violations (unknown type, extra property, bad key pattern, an option field with no options, a stored document with a temporary id). Each has a sibling `<name>.expected.json`: `{ "code", "rule", "pointer" }`.
- [ ] T008 [P] Create `CONTRACTS/collection-schema.md` from contracts/collection-schema.md, replacing its "Where it will live" paragraph with the embedding sentence used in `CONTRACTS/page-schema.md`. Add the collection schema row to `CONTRACTS/README.md`, and remove the line "The collection schema … arrive with the CMS features" there, keeping the binding expression format as still to come.
- [ ] T009 [P] Create `ITESTS/Cms/CollectionSchemaContractTests.cs`:
  - the embedded resource bytes equal `docs/contracts/collection-schema/v1.schema.json`;
  - `SchemaSubsetValidator.UnsupportedKeywords` finds nothing in the file;
  - every type in contract §2's table has exactly one `$defs/field` branch, and every value `$defs` entry in §3's table exists.
- [ ] T010 Create `CMSC/CollectionSchema.cs` (`CurrentVersion = 1`, `SupportedVersions`, `OpenSchema(int)` over the embedded resource, mirroring `PAGESC/PageSchema.cs`) and `CMSC/CollectionSchemaLimits.cs` (contract §8 constants).

### `ValidateDefinition` (module-surface.md §2)

- [ ] T011 [P] Create `UTESTS/Pages/SchemaSubsetValidatorDefinitionTests.cs`:
  - validates an instance against a named `$defs` entry, with `$ref` inside it resolved;
  - an unknown definition name throws;
  - failure locations are relative to the instance;
  - the page schema's `rtDoc` accepts a valid rich text document and refuses one with an unknown node type.
- [ ] T012 Add `public SchemaFailure? ValidateDefinition(string definition, JsonElement instance)` to `PAGESC/Schema/SchemaSubsetValidator.cs`, with an XML doc comment citing 014 CS-04.
- [ ] T013 [P] Create `ADMINTESTS/cms/validateDefinition.spec.ts` with the same cases as T011.
- [ ] T014 Add `validateDefinition(definition, instance)` to `ADMIN/editor/schema/subsetValidator.ts`.

### Domain (tests first)

- [ ] T015 [P] Create `UTESTS/Cms/CollectionSchemaRulesTests.cs`: every rule in contract §5 in its stated order, every reserved key (§4), the limits (§8), and `titleFieldId` pointing at a non-text field, a multiline text field, a non-required field or a missing field.
- [ ] T016 [P] Create `UTESTS/Cms/SchemaDiffTests.cs`, covering each change kind in research CS-06:
  - `fieldAdded` (temporary id), `fieldRemoved`, `fieldsReordered`, `fieldRenamed` (name, key, help), `requiredChanged`, `typeChanged`, `settingsChanged` per setting;
  - `optionAdded`, `optionRemoved`, `optionRelabelled`, `optionsReordered`, `titleFieldChanged`, `detailsChanged`;
  - an empty diff;
  - a permanent id the stored schema lacks → `cms.unknown_field_id` (and an option id → `cms.unknown_option_id`).
- [ ] T017 [P] Create `UTESTS/Cms/FieldChangeClassificationTests.cs`: a table-driven test over every ordered pair of the ten types, and every setting change, asserting the rule (`free`, `reported`, `lossless`, `lossy`, `forbidden`) from contract §7 and data-model §3.2. Include an added field with and without a fill-in (`reported` versus `free`), required turned on (`reported`) and each tightened setting (`reported`).
- [ ] T018 [P] Create `UTESTS/Cms/CollectionSchemaGoldenTests.cs`: for every file under `docs/contracts/collection-schema/examples/`, the server validator's verdict, and for invalid files its `code`, `rule` and `pointer`, equal the sibling `.expected.json`.
- [ ] T019 [P] Create `UTESTS/Cms/ItemValuesTests.cs`:
  - well-formedness per type, including rich text through the page schema `rtDoc`;
  - `null` and unknown keys refused;
  - validity against settings (`maxLength`, `min`/`max`, `decimals`, `accept`, required) reported as attention, not refusal;
  - missing media or reference ids reported as missing.
- [ ] T020 [P] Create `UTESTS/Cms/CollectionTests.cs`:
  - `Create` gives revision 1, an empty schema and counters at 1;
  - applying a proposal assigns `f{n}` and `o{n}` from the counters and advances them, and never reuses an id after removal;
  - the first single-line text field becomes the title field and required;
  - an empty diff adds no revision;
  - each applied change raises the domain event that becomes `Cms.CollectionChanged`.
- [ ] T021 Create `CMS/Domain/FieldType.cs`, `Field.cs`, `FieldOption.cs`, `CollectionSchemaDocument.cs` and `ReservedKeys.cs`. They parse from and write to `JsonElement` and `JsonObject`, write in canonical property order, and tell temporary ids from permanent ones.
- [ ] T022 Create `CMS/Domain/CollectionSchemaRules.cs` (contract §5, in order, first failure with rule and pointer) and `CMS/Infrastructure/Schema/CollectionSchemaValidator.cs`. The validator runs research CS-04's order: 64 KiB size; duplicate JSON properties (as `src/Modules/Pages/QuickBuilder.Pages/Infrastructure/Schema/PageDocumentValidator.cs` does); `schemaVersion`; `SchemaSubsetValidator` over the embedded file; then the rules. Results carry `code`, `rule` and `pointer`. Makes T015 and T018 pass.
- [ ] T023 Create `CMS/Domain/SchemaProposal.cs`, `SchemaDiff.cs`, `FieldChange.cs` and `CMS/Domain/FieldConversion.cs`, the last with **`Classify(FieldChange)` only**. Value conversion comes in US4 (T069). Makes T016 and T017 pass.
- [ ] T024 Create `CMS/Domain/ItemValues.cs` (parse and write the item values document) and `CMS/Infrastructure/ItemValueValidator.cs`: well-formedness through the contract `$defs` and the page schema's `rtDoc` via `ValidateDefinition`, and validity states (`valid`, `needsAttention`, `missingRequired`, `missingTarget`). Makes T019 pass.
- [ ] T025 Create `CMS/Domain/Collection.cs`, the aggregate root of data-model §3.1:
  - `Create`, `Apply(diff, impact, confirmation, actor, clock)` and `MarkDeleted`;
  - id assignment from the counters, revision increments, and title-field designation;
  - domain events in `CMS/Domain/Events/DomainEvents.cs`.

  Also create `CMS/Domain/Item.cs`, `CollectionReference.cs`, `SchemaChangeEntry.cs` and a minimal `CMS/Domain/ImpactSummary.cs` (the change list with each change's rule, `requiresConfirmation`, and an empty counts map; US3's T060 adds counts, examples and the digest). Makes T020 pass.

### Database, module, capabilities, errors, events

- [ ] T026 Create `CMS/Infrastructure/CmsDbContext.cs`:
  - schema `cms`, with snake_case as in the other modules;
  - entities and indexes exactly as data-model §1: unique `(site_id, slug)`, foreign keys with `CASCADE`, and `RESTRICT` on `collection_references.target_collection_id`;
  - `revision` as a concurrency token, and the `jsonb` columns mapped as `JsonDocument`;
  - global site filters, the `ForSite<T>(siteId)` helper (copied from `EditorDbContext`), and `OutboxModelBuilder`.

  Also create `CMS/Infrastructure/CmsDbContextFactory.cs` (design time) and `CMS/Infrastructure/CmsOutbox.cs`, mirroring `PagesOutbox.cs`.
- [ ] T027 Generate the migration `CMS/Infrastructure/Migrations/<ts>_InitialCms.cs` with `dotnet ef migrations add InitialCms` (history table `cms.__EFMigrationsHistory`). Check that the generated SQL matches data-model §1.
- [ ] T028 [P] Create `CMSC/Events/CollectionCreated.cs`, `CollectionChanged.cs` and `CollectionDeleted.cs`, with the payloads in data-model §4 implementing `IIntegrationEvent` and the names `Cms.CollectionCreated`, `Cms.CollectionChanged` and `Cms.CollectionDeleted`.
- [ ] T029 [P] Create `CMS/CmsCapabilities.cs`, declaring `cms.collections.view` (Designer, Editor) and `cms.collections.manage` (Designer) as `CapabilityDefinition`s, as `PagesCapabilities.cs` does. Create `CMS/CmsErrors.cs`, with the factories for every code in data-model §5 and their extensions. Create `CMS/CmsErrorResults.cs`, which maps statuses as data-model §5 does (400, 404, 409, 413).
- [ ] T030 [P] Create `CMS/Infrastructure/CmsAccess.cs` (site exists, actor, `CanManageAsync`, display names), modelled on `src/Modules/Pages/QuickBuilder.Pages/Infrastructure/PagesAccess.cs`.
- [ ] T031 Complete `CMS/CmsModule.cs`:
  - `CmsDbContext` with its own migrations history table;
  - `CmsDatabaseProvider : IModuleDatabaseProvider`, mirroring `PagesDatabaseProvider`;
  - capabilities, the three `EventTypeRegistration`s, `CollectionSchemaValidator` and `ItemValueValidator` as singletons, and `CmsAccess` as scoped;
  - `ISchemaMigrationObserver` registered as a no-op (CS-20): create `CMS/Infrastructure/ISchemaMigrationObserver.cs` here, with `AfterBatch(int batchNumber, CancellationToken)`, and `NoSchemaMigrationObserver`.

  Slices are registered in their stories.
- [ ] T032 Create `CMS/Infrastructure/CollectionLocks.cs` with `LockForUpdateAsync(siteId, collectionId)` and `LockForShareAsync(siteId, collectionId)`:
  - each uses `FromSql($"SELECT * FROM cms.collections WHERE id = {collectionId} AND site_id = {siteId} FOR UPDATE NOWAIT")` (and `FOR SHARE NOWAIT`) on the current transaction;
  - each has a code comment that the site predicate replaces the global filter here;
  - PostgreSQL `55P03` becomes `CmsErrors.CollectionBusy`.
- [ ] T033 [P] Create `ITESTS/Cms/CmsSiteFilterTests.cs`: `CollectionLocks` never returns or locks another site's collection with the same id shape, and a lock held on site A's collection does not affect site B.
- [ ] T034 [P] Create `ITESTS/Cms/ItemSeed.cs`, a test builder that inserts items through `CmsDbContext` with well-formed values for each field of a given schema. It has options for the fill rate, published copies, specific values, and count (batched up to 10,000).
- [ ] T035 [P] Create `ITESTS/Isolation/Probes/CmsIsolationProbe.cs` and register it in `ITESTS/Isolation/IsolationProbes.cs`. From site B it checks every Cms endpoint for collections, preview, history and delete, and checks that `ForSite` reads never return site A's rows. `IsolationCoverageTests` must stay green.
- [ ] T036 [P] Create `ITESTS/Cms/UpgradeFrom013Tests.cs`, following `ITESTS/Media/UpgradeFrom012Tests.cs`: a 013 database upgrades, gains an empty `cms` schema with its history table, and pages, media and design rows are unchanged.
- [ ] T037 [P] Update the shared contract registries: add the two capabilities to `specs/002-identity-setup/contracts/authorization.md` §2 (with a "Registered by 014" heading), and the three events to `specs/001-foundation-skeleton/contracts/integration-events.md` "Events added by later features" (module-surface.md §3–§4).

### Admin foundations

- [ ] T038 [P] Create `ADMIN/api/cms.ts`: typed requests and responses for every operation in contracts/cms-api.yaml, using `ADMIN/api/request.ts` as `ADMIN/api/pages.ts` does, and surfacing `errorCode`, `rule`, `pointer`, `preview`, `currentRevision` and `references`.
- [ ] T039 [P] Create `ADMINTESTS/cms/collectionSchema.spec.ts`: it runs every golden example through the admin validator and expects the same verdict, `code`, `rule` and `pointer` as the `.expected.json` (SC-006).
- [ ] T040 Create `CMSUI/collectionSchema.ts` (types for the schema document and proposals, temporary-id allocation, loading `@contracts/collection-schema/v1.schema.json`, and validating in CS-04 order), plus `CMSUI/collectionSchemaRules.ts` (a port of `CollectionSchemaRules`) and `CMSUI/cmsErrors.ts` (error code → i18n key). Makes T039 pass.

**Checkpoint**: the contract, the domain and the database exist; stories can begin.

---

## Phase 3: User Story 1 - A Designer defines a new collection and its fields (Priority: P1) 🎯 MVP

**Goal**: create, list, view and save a collection with fields of every type, validated on
both sides, with the right access per role.

**Independent Test**: create a collection with one field of each type, save, reload, and
confirm that fields, settings, order and system-chosen ids come back unchanged. Confirm that
invalid schemas are refused with a reason pointing at the field (quickstart §1).

### Tests for User Story 1 ⚠️

- [ ] T041 [P] [US1] Create `ITESTS/Cms/CollectionApiTests.cs`:
  - **Create**: 201 at revision 1 with an empty schema; `cms.slug_taken`; `cms.too_many_collections` at 100.
  - **List**: sorted by name, with field and item counts. **Get**: works; another site's id gives `cms.collection_not_found`.
  - **Save**: one field of every type is saved with ids `f1…f10`, options `o1…`, an `idMap`, the auto title field, and a read-back equal as JSON.
  - **Errors**: each invalid golden example gives 400 with its `rule` and `pointer`; a reference to a site B collection gives `cms.unknown_target_collection`; a forged `f99` gives `cms.unknown_field_id`; a stale `revision` gives `cms.revision_conflict` with `currentRevision`; 65 KiB gives 413.
  - **Roles**: Editor gets 200 on list and get, and 403 on create and save; a user with no role gets 404 everywhere.
  - **Event**: `Cms.CollectionCreated` and `Cms.CollectionChanged` reach the outbox with ids only.
- [ ] T042 [P] [US1] Create `ADMINTESTS/cms/CollectionsView.spec.ts` (list, empty state, create dialog suggesting the address name, `cms.slug_taken` shown on the field, axe) and `ADMINTESTS/cms/CollectionBuilderView.spec.ts`:
  - add one field of each type with its settings; option editing;
  - the key is suggested until edited by hand;
  - client validation highlights the offending field before any request;
  - save sends temporary ids and applies the returned `idMap`;
  - Editors get a read-only builder with a notice;
  - axe reports no serious or critical issues.

### Implementation for User Story 1

- [ ] T043 [US1] Create the slices `CMS/Features/ListCollections/ListCollections.cs` (`GET`, view), `CMS/Features/CreateCollection/CreateCollection.cs` (`POST`, manage; name, slug and description rules; the limit count of CS-18; `Collection.Create`; history entry `created`; outbox `Cms.CollectionCreated`) and `CMS/Features/GetCollection/GetCollection.cs` (`GET {id}`, view; with `itemCount` and the updater's name). Register them and add them to `EndpointCatalog`.
- [ ] T044 [US1] Create `CMS/Infrastructure/ImpactCalculator.cs` with `ComputeAsync(siteId, collection, diff, fillIns, ct)` returning an `ImpactSummary`. **First version**: an empty impact when the collection has no items or every change is `free`. Otherwise it marks each non-free change as requiring confirmation with no counts. US3 (T061) replaces the body.
- [ ] T045 [US1] Create `CMS/Features/SaveCollection/SaveCollection.cs` (`PUT {id}`, manage), running research CS-09 in one transaction:
  1. `CollectionLocks.LockForUpdateAsync`;
  2. the `revision` check;
  3. `CollectionSchemaValidator`, then reference targets loaded through `ForSite<Collection>`;
  4. `SchemaDiff`;
  5. `ImpactCalculator`, refusing with `cms.confirmation_required` when it requires confirmation and none was given;
  6. `Collection.Apply`, replacing `collection_references` rows (self-references excluded), adding a `schema_changes` row with the summary of data-model §3.4, writing the outbox `Cms.CollectionChanged`, and committing.

  The response carries `idMap`. An empty diff returns 200 unchanged. A request-size attribute modelled on `DraftSizeLimitAttribute` returns `cms.proposal_too_large`. Makes T041 pass.
- [ ] T046 [US1] Add the routes `/sites/:siteId/collections` (`site-collections`) and `/sites/:siteId/collections/:collectionId` (`site-collection`) to `ADMIN/app/router.ts`, and a **Collections** link beside Media, Pages, Components and Design in `ADMIN/features/sites/SiteDetailView.vue` (the site navigation). The link is visible to anyone with `cms.collections.view`.
- [ ] T047 [US1] Create `CMSUI/CollectionsView.vue` and `CMSUI/CollectionFormDialog.vue` (name, address name suggested with `ADMIN/features/pages/slugify.ts`, description; server errors on their fields).
- [ ] T048 [US1] Create `CMSUI/CollectionBuilderView.vue`, `CMSUI/FieldList.vue` (list and select, without reordering yet), `CMSUI/AddFieldMenu.vue`, `CMSUI/FieldEditor.vue` and `CMSUI/OptionsEditor.vue`:
  - **Builder**: details at the top, fields and editor below, and a save bar showing unsaved changes.
  - **Field editor**: common settings, then the type settings from contract §2, plus a target picker listing the site's collections.
  - **Options**: add, relabel and remove with temporary ids.
  - **Saving**: client validation via `collectionSchema.ts`, then `PUT`, applying `idMap`.
  - **Editors**: read-only.

  Makes T042 pass.

**Checkpoint**: quickstart §1 passes. **MVP.**

---

## Phase 4: User Story 2 - Renaming and reordering fields never touches content (Priority: P1)

**Goal**: rename, rekey, reorder and relabel freely with zero item changes.

**Independent Test**: in a collection whose items hold every type, rename, rekey, reorder and
relabel, and rename the collection. Every stored value and every id is unchanged
(quickstart §2).

### Tests for User Story 2 ⚠️

- [ ] T049 [P] [US2] Create `ITESTS/Cms/RenameReorderTests.cs` (SC-002), using `ItemSeed` with 200 items that hold every type, draft and published:
  - rename the name, key and help of every field; reorder fields; relabel and reorder options; change the collection's name, slug and description;
  - assert every `draft` and `published` document is equal as JSON before and after, and that no `items` row was updated (`xmin` unchanged);
  - assert ids are unchanged and the revision advanced by exactly 1;
  - a new field with a removed field's key gets a fresh id and no values.
- [ ] T050 [P] [US2] Create `ADMINTESTS/cms/schemaChanges.spec.ts` (the client diff and classification agree with T016 and T017's cases) and `ADMINTESTS/cms/FieldList.spec.ts`:
  - reorder by drag and by Alt+↑/↓, with a live announcement;
  - a rename-only save makes no preview request.

### Implementation for User Story 2

- [ ] T051 [US2] Create `CMSUI/schemaChanges.ts`, a TypeScript port of `SchemaDiff` and `FieldConversion.Classify` that tells the builder whether a save needs a preview: all `free` → save directly (FR-030); any `reported`, `lossless` or `lossy` change → preview first. Makes the first half of T050 pass.
- [ ] T052 [US2] Add reordering to `CMSUI/FieldList.vue` with `ADMIN/editor/dnd/useDragDrop.ts`, plus the keyboard equivalent (Alt+↑/↓ and an `aria-live` announcement, as the editor's layers panel does). Wire `schemaChanges.ts` into `CMSUI/CollectionBuilderView.vue`'s save. Makes T050 pass.
- [ ] T053 [US2] Confirm that `SaveCollection` writes no item when the impact is empty: the item migration step is skipped entirely. Add an assertion log at Debug level with the count of items written (0). Makes T049 pass.

**Checkpoint**: quickstart §2 passes.

---

## Phase 5: User Story 3 - Adding and removing fields after items exist (Priority: P1)

**Goal**: add fields (with fill-ins), make fields required, remove fields and options with
an accurate preview, enforced confirmation and an all-or-nothing migration. Item writes are
refused while it runs.

**Independent Test**: in a collection with items, add an optional field, add a required field
with and without a fill-in, and remove a field that holds values. The preview's counts match
the stored result, and confirmation is required where values are lost (quickstart §3).

### Tests for User Story 3 ⚠️

- [ ] T054 [P] [US3] Create `ITESTS/Cms/SchemaMigrationTests.cs` (add and remove part):
  - an optional field without a fill-in changes nothing;
  - a fill-in on an added optional boolean, and on an added required option (temporary option id), is written to draft and to published where present, counted `filled`;
  - required turned on without a fill-in is counted `stillMissing` and saved;
  - removing a field with values gives `cms.confirmation_required` (with a `preview`) until the digest is sent, then deletes the key from both copies;
  - removing an option clears the values that chose it;
  - removing the title field gives `invalid_title_field`;
  - an invalid fill-in gives `cms.invalid_fill_in`;
  - for every case, the stored outcome equals the preview's counts.
- [ ] T055 [P] [US3] Create `ITESTS/Cms/ConfirmationTests.cs`:
  - a preview's digest is accepted;
  - after an SQL change that alters the counts, the save gives `cms.preview_outdated` with a fresh preview;
  - after an SQL change that leaves the counts the same, the save is accepted;
  - `free` and `reported` changes need no confirmation; a `reported` change (required turned on, a fill-in) still returns its counts from the preview.
- [ ] T056 [P] [US3] Create `ITESTS/Cms/InterruptedSchemaChangeTests.cs` (SC-005): with 10,000 seeded items, a test `ISchemaMigrationObserver` throws after the second batch. The schema, revision, references, history and every item are unchanged, and no outbox row was written. Repeat with the connection killed through `pg_terminate_backend`.
- [ ] T057 [P] [US3] Create `ITESTS/Cms/ItemWriteGateTests.cs` (FR-037, CS-10):
  - while a schema change holds the lock (paused through the observer), a `CollectionWriteGate` call gives `cms.items_being_updated` at once;
  - a concurrent plain read returns the before-state, then the after-state after commit, never a mix;
  - while a gated write holds `FOR SHARE`, a schema save gives `cms.collection_busy`.
- [ ] T058 [P] [US3] Create `UTESTS/Cms/ImpactDigestTests.cs`: the digest is stable across property order and example changes, and changes when any count or change entry changes.
- [ ] T059 [P] [US3] Create `ADMINTESTS/cms/SchemaPreviewDialog.spec.ts`:
  - counts and examples per change;
  - "cannot be undone" wording when values are cleared;
  - fill-in inputs for added and required fields;
  - confirm sends the digest;
  - `cms.preview_outdated` reopens the dialog with the new counts;
  - progress state while saving;
  - axe.

### Implementation for User Story 3

- [ ] T060 [US3] Create `CMS/Domain/ValueOutcome.cs` (outcomes and the worse-of-two-copies order from CS-07), counts per outcome and up to 10 examples added to `CMS/Domain/ImpactSummary.cs` (created minimal in T025), and `CMS/Domain/ImpactDigest.cs` (canonical serialisation without examples or item ids; SHA-256 as lower-case hex). Makes T058 pass.
- [ ] T061 [US3] Replace `CMS/Infrastructure/ImpactCalculator.cs`'s body (T044):
  - project only the affected fields and the title field from `draft` and `published` through `JsonDocument` `GetProperty` (CS-08);
  - compute outcomes for field removal, option removal, required on, and fill-ins;
  - take the worse outcome of the two copies per item;
  - keep up to 10 examples with the item id and title text;
  - set `requiresConfirmation` and the digest.

  Changes that convert values call `FieldConversion`, which is finished in US4.
- [ ] T062 [US3] Create `CMS/Infrastructure/ItemMigrator.cs`:
  - loads affected items tracked in batches of 500 by id;
  - applies removals, option clears and fill-ins to both copies;
  - saves each batch and clears the change tracker;
  - calls `ISchemaMigrationObserver.AfterBatch`.

  Wire the migrator into `SaveCollection` after the confirmation check. The check now compares the digest under the lock: `cms.confirmation_required` or `cms.preview_outdated`, each with the fresh preview. Validate fill-ins against the field (`cms.invalid_fill_in`). Makes T054–T056 pass.
- [ ] T063 [US3] Create `CMS/Features/PreviewCollection/PreviewCollection.cs` (`POST {id}/preview`, view). It is read-only, takes no lock, and returns the same validation errors as the save and the `Preview` of contracts/cms-api.yaml.
- [ ] T064 [US3] Create `CMS/Infrastructure/CollectionWriteGate.cs`: `EnterAsync(siteId, collectionId, expectedRevision?, ct)` in the caller's transaction takes `LockForShareAsync` and returns the current schema and revision. Turn the busy error into `cms.items_being_updated` here. Add an XML doc comment that every item write from #15 on must use it (data-model §3.3). Makes T057 pass.
- [ ] T065 [US3] Create `CMSUI/SchemaPreviewDialog.vue` and wire it into `CMSUI/CollectionBuilderView.vue`:
  - when `schemaChanges.ts` finds a change that is not `free`, `POST preview`, then show the dialog: per-change counts, examples, the "cannot be undone" warning, and fill-in inputs where `fillInAllowed`;
  - confirm sends `PUT` with the digest, and shows "Updating N items…" while it runs (FR-036);
  - on `cms.preview_outdated`, reopen with the new preview.

  Makes T059 pass.

**Checkpoint**: quickstart §3 passes. All P1 stories are done.

---

## Phase 6: User Story 4 - Changing a field's type or settings with a clear outcome (Priority: P2)

**Goal**: every type and setting change in contract §7, converted exactly as previewed;
forbidden changes refused unless the field is empty.

**Independent Test**: for every pair of types, change a field holding varied values. Lossless
changes keep every value, lossy ones match their preview, and forbidden ones are refused
without change (quickstart §4).

### Tests for User Story 4 ⚠️

- [ ] T066 [P] [US4] Create `UTESTS/Cms/FieldConversionTests.cs` (SC-004), covering every row of contract §7:
  - **fixed formats**: number text without grouping, rounding half away from zero, the safe integer range, `min`/`max` clearing;
  - **dates**: date and date-time parsing with offsets normalised to UTC, midnight UTC, the UTC calendar date;
  - **rich text**: to text with marks, links and lists dropped (`partlyLost`) versus plain paragraphs (`converted`), and shortened to `maxLength`; text to rich text past 500 lines using hard breaks;
  - **options**: label matching ignoring case and trimmed;
  - **other conversions**: several lines to one line; multi-reference to reference keeping the first;
  - **settings**: a converted value breaking the new settings is `nowInvalid`; tightened `maxLength`, `min`, `max` and `accept` are `nowInvalid`; lowered `decimals` rounds.
- [ ] T067 [P] [US4] Extend `ITESTS/Cms/SchemaMigrationTests.cs` with type and settings changes:
  - for every lossless and lossy pair, seed known values, preview, confirm, and assert each stored value and each count equals the preview;
  - **file to image** and **narrowing `accept`**: use an image and a PDF uploaded through the Media API, plus a deleted file, which is counted `missing` and kept;
  - **target change**: refused with values, allowed without;
  - **forbidden pair**: `cms.type_change_not_allowed` with values, allowed on an empty field.
- [ ] T068 [P] [US4] Create `ADMINTESTS/cms/FieldEditor.spec.ts`:
  - changing type offers every type, with lossless and lossy ones marked from `schemaChanges.ts`;
  - a forbidden change shows the server's reason and suggests adding a new field;
  - the preview labels each outcome (converted, partly lost, cleared, needs attention, missing) and says "UTC" for date changes;
  - axe.

### Implementation for User Story 4

- [ ] T069 [US4] Add `Convert(FieldChange, JsonNode value, MediaKindLookup)` → `(ValueOutcome, JsonNode?)` to `CMS/Domain/FieldConversion.cs`, with one private function per contract §7 row and the settings checks of CS-07. It is culture-invariant and uses `DateTimeOffset` in UTC. Makes T066 pass.
- [ ] T070 [US4] Create `CMS/Infrastructure/MediaKinds.cs`. It collects distinct media ids from the affected values and calls `IPublicMediaReader.GetAsync(siteId, ids)` once per preview or save. A content type starting with `image/` is an image, and absent ids are missing (CS-12).
- [ ] T071 [US4] Wire conversions into `CMS/Infrastructure/ImpactCalculator.cs` and `CMS/Infrastructure/ItemMigrator.cs` for `typeChanged` and `settingsChanged`. In `SaveCollection` and `PreviewCollection`, refuse forbidden changes on fields with any value in either copy (`cms.type_change_not_allowed`, `cms.target_change_not_allowed`), using an `EXISTS`-style query over the projected field. Makes T067 pass.
- [ ] T072 [US4] Extend `CMSUI/FieldEditor.vue` with type changing (lossless and lossy marked; a forbidden change explained after the server's refusal), and `CMSUI/SchemaPreviewDialog.vue` with outcome labels and the UTC note for date changes. Makes T068 pass.

**Checkpoint**: quickstart §4 passes.

---

## Phase 7: User Story 5 - Deleting a collection (Priority: P2)

**Goal**: delete a collection and its items permanently, refused while another collection
references it.

**Independent Test**: delete an empty collection, one with items, and one targeted by another
collection's reference field (quickstart §5).

### Tests for User Story 5 ⚠️

- [ ] T073 [P] [US5] Create `ITESTS/Cms/CollectionReferenceTests.cs`:
  - delete with items works: items, history and outgoing references are gone, the slug is free, and `Cms.CollectionDeleted` is written;
  - a referenced collection gives `cms.collection_referenced` listing each referencing collection and field;
  - a self-referencing collection deletes;
  - a stale revision gives `cms.revision_conflict`;
  - an Editor gets 403;
  - a race where a delete and a save adding a reference to it run concurrently leaves exactly one outcome, with the loser getting `cms.collection_referenced` or `cms.unknown_target_collection`.
- [ ] T074 [P] [US5] Create `ADMINTESTS/cms/DeleteCollectionDialog.spec.ts`: shows the item count and "cannot be undone"; Delete is enabled only once the exact name is typed; the referencing fields are listed when refused; axe.

### Implementation for User Story 5

- [ ] T075 [US5] Create `CMS/Features/DeleteCollection/DeleteCollection.cs` (`DELETE {id}?revision=n`, manage). It runs in one transaction: lock, revision check, a check of `collection_references` targeting it (`cms.collection_referenced` with names), `Collection.MarkDeleted`, the outbox `Cms.CollectionDeleted`, then remove and commit. Catch a foreign-key violation and report it as `cms.collection_referenced`. Makes T073 pass.
- [ ] T076 [US5] Create `CMSUI/DeleteCollectionDialog.vue` and open it from `CMSUI/CollectionsView.vue` and `CMSUI/CollectionBuilderView.vue`. After a delete, go back to the list. Makes T074 pass.

**Checkpoint**: quickstart §5 passes.

---

## Phase 8: User Story 6 - Seeing how a collection's schema has changed (Priority: P3)

**Goal**: an ordered history of revisions with counts, plus clear conflict and busy handling.

**Independent Test**: make several kinds of change, and confirm each appears once in the
history with the right revision, person, time and counts (quickstart §6).

### Tests for User Story 6 ⚠️

- [ ] T077 [P] [US6] Create `ITESTS/Cms/SchemaHistoryTests.cs`:
  - one entry per save, including creation and detail-only saves, newest first;
  - paging with `before` and `limit`;
  - each entry's kinds and counts equal the confirmed preview;
  - no seeded item value appears anywhere in the `summary` JSON (FR-033);
  - Editors may read the history.
- [ ] T078 [P] [US6] Create `ITESTS/Cms/ConcurrencyTests.cs`: two saves from the same revision give one 200 and one `cms.revision_conflict`; a save while another runs on 10,000 items gives `cms.collection_busy`; a delete during a save gives `cms.collection_busy`.
- [ ] T079 [P] [US6] Create `ADMINTESTS/cms/SchemaHistoryPanel.spec.ts`: entries rendered as sentences from their kinds and counts; "load more"; `cms.revision_conflict` shows `ADMIN/editor/ConflictBanner.vue` with "Reload"; `cms.collection_busy` shows "try again shortly"; axe.

### Implementation for User Story 6

- [ ] T080 [US6] Create `CMS/Features/GetSchemaHistory/GetSchemaHistory.cs` (`GET {id}/history`, view; `before` and `limit`; names of the members who made changes). Makes T077 pass. T078 should already pass from T045 and T075; fix any gap in `CollectionLocks`.
- [ ] T081 [US6] Create `CMSUI/SchemaHistoryPanel.vue`, a side panel on the builder with one i18n sentence per entry kind. Add conflict and busy handling to `CMSUI/CollectionBuilderView.vue`, reusing `ADMIN/editor/ConflictBanner.vue`. Makes T079 pass.

**Checkpoint**: quickstart §6 passes. All stories are done.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T082 [P] Create `STESTS/CollectionSchemaPerformanceTests.cs` (Category `Performance`, SC-003):
  1. Create a collection through the API.
  2. Insert 10,000 items with one `generate_series` SQL statement, as `MediaPerformanceTests.cs` does.
  3. Time a preview of, then the confirmed save of, a text-to-number change touching every item.
  4. Assert the preview is under 5 s and the save under 30 s, and record both in the test output.
- [ ] T083 [P] Extend the release smoke journey in `STESTS/ComposeDeployment.cs` (or its journey test) to create a collection with one text field and read it back (plan, Principle X).
- [ ] T084 [P] Add `seed-items` to `qb.ps1` (CS-19):
  - parameters `-Collection <slug>`, `-Count` (default 300) and `-Site`;
  - one `psql` statement that reads `cms.collections.schema` and inserts items with values per field type through `jsonb_build_object` and `generate_series`, filling about 70 % of optional fields;
  - a help block saying it is for development only.
- [ ] T085 [P] Update the documentation:
  - **`docs/operations.md`**: a "Collections" section: who can change schemas; that removals and lossy conversions cannot be undone, so back up first; and a reverse-proxy timeout of at least 60 s for large schema changes (plan Risks).
  - **`docs/backlog.md`**: a "Deferred by 014" list: unique field values (clarification Q4), schema snapshots and restore, background migration beyond 10,000 items, a per-site time zone, and **exception E-6 for #16**: decide how a schema change reaches live pages before items are rendered (plan Complexity Tracking).
- [ ] T086 [P] Extend `ITESTS/Observability/LoggingTests.cs`: a schema change over seeded items logs no item value, title, field name or collection name.
- [ ] T087 Run quickstart.md §1–§9 by hand on the compose setup, including SC-001 timed with a first-time user and SC-007 keyboard-only. Record results, timings and versions in `specs/014-cms-collections-schema/quickstart-results.md`.
- [ ] T088 Run `dotnet format --verify-no-changes`, `dotnet test` (with Docker, `QB_REQUIRE_DOCKER=1`), and `npm run lint && npm run type-check && npm test && npm run build` in `src/admin`. Fix any failures and confirm the coverage gate passes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T002 → T003; T004 and T005 in parallel after T002. T005's contracts
  line compiles after T010.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
  - Contract: T006 → T007, T008, T009 (parallel) → T010.
  - `ValidateDefinition`: T011 → T012; T013 → T014.
  - Domain: tests T015–T020 first (parallel), then T021 → T022 → T023, T024 (needs T012)
    and T025.
  - Database: T026 → T027; T028–T030 are parallel; T031 needs T026–T030; T032 → T033;
    T034–T037 are parallel after T027.
  - Admin: T038 and T039 in parallel, then T040 (needs T014).
- **US1 (Phase 3)**: after Foundational. **The MVP.**
- **US2 (Phase 4)**: after US1 (it needs the save slice and the builder).
- **US3 (Phase 5)**: after US1. It is independent of US2, except that T065 uses
  `schemaChanges.ts` from T051. If US2 is not done, T051 moves here.
- **US4 (Phase 6)**: after US3 (it plugs conversions into the calculator and migrator).
- **US5 (Phase 7)**: after US1. It is independent of US2–US4.
- **US6 (Phase 8)**: after US1. T078's busy case needs US3's 10,000-item seeding through
  `ItemSeed`, which is foundational.
- **Polish (Phase 9)**: after the stories it covers. T087 is last but one, and T088 is last.

### Within Each User Story

- Tests are written first and fail.
- The domain comes before infrastructure, infrastructure before slices, and the backend
  before the admin UI.
- The i18n keys land with the component that uses them.

### Parallel Opportunities

- **Phase 1**: T004, T005.
- **Phase 2**: T007–T009; T011 and T013; T015–T020 together; T028–T030; T033–T037; T038 and
  T039.
- **US1**: T041 and T042 together; backend (T043–T045) beside admin (T046–T048) once the
  API shapes are fixed by `ADMIN/api/cms.ts`.
- **US3**: tests T054–T059 together; T063 and T064 beside T062.
- **US4**: T066–T068 together; T070 beside T069.
- **Across stories**, after US1: US2, US3 and US5 can go to three developers; US4 follows
  US3, and US6 can go with any of them.

---

## Parallel Example: Foundational domain

```bash
# Tests together, before any domain code:
Task: "T015 CollectionSchemaRulesTests"
Task: "T016 SchemaDiffTests"
Task: "T017 FieldChangeClassificationTests"
Task: "T018 CollectionSchemaGoldenTests"
Task: "T019 ItemValuesTests"
Task: "T020 CollectionTests"
```

## Parallel Example: User Story 3

```bash
# Tests together:
Task: "T054 SchemaMigrationTests (add/remove)"
Task: "T055 ConfirmationTests"
Task: "T056 InterruptedSchemaChangeTests"
Task: "T057 ItemWriteGateTests"
Task: "T058 ImpactDigestTests"
Task: "T059 SchemaPreviewDialog.spec.ts"

# After T060–T062:
Task: "T063 PreviewCollection slice"
Task: "T064 CollectionWriteGate"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational. The contract, golden files on both sides, the domain and the
   database are green.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. A Designer can define a collection of every field
   type.

### Incremental Delivery

1. Setup and Foundational.
2. US1: define collections. **Demo.**
3. US2 (rename and reorder) and US3 (add and remove with migration). All P1 stories are
   done: the schema can evolve safely after items exist.
4. US4 (type and settings changes), US5 (delete), US6 (history).
5. Polish, the manual quickstart run, and the PR closing #14.

The feature ships in one PR, as with 010–013, so there is no feature flag (plan, Principle
XI).

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US6.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #14.
- **Contract changes** (T006–T008, T012, T014 and T037) land in the same PR as their tests
  (constitution, Development Workflow).
- **Avoid**:
  - writing items outside `ItemMigrator` (and, from #15, `CollectionWriteGate`);
  - converting values in SQL, because the preview and the result must share
    `FieldConversion`;
  - taking locks anywhere but `CollectionLocks`;
  - a second `AcrossAllSites` call;
  - item values or names in logs and events;
  - culture-dependent parsing or local time zones in conversions.
