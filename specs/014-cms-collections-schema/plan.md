# Implementation Plan: CMS Collections and Schema Builder

**Branch**: `014-cms-collections-schema` | **Date**: 2026-10-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-cms-collections-schema/spec.md`

**Issue**: #14. No exception from earlier plans is open except E-5, which is unchanged. This
feature adds exception **E-6** (Principle VII: schema changes rewrite published item data;
see Complexity Tracking), with its follow-up assigned to #16.

## Summary

Sites get **collections**: named, site-owned sets of items that share one schema. The issue
asks for the collection and schema side only. Items are stored here only so that schema
changes can migrate them. Editing items is #15; showing them on pages is #16.

The approach, detailed in [research.md](./research.md):
- **A new Cms module** (CS-01), the one the constitution names. It has its own schema,
  context, outbox and rich domain model, and owns collections, schema history, the item
  store and references between collections.
- **The contract** (CS-02 … CS-04):
  - **Schema document**: `docs/contracts/collection-schema/v1.schema.json` plus
    `collection-schema.md` and golden examples, checked by the server and the admin against
    the same file, with the same code rules on both sides.
  - **Field ids**: `f{n}` and option ids `o{n}`, from per-collection counters, so they are
    never reused. New fields use temporary ids `~{n}` in proposals.
  - **Item values**: keyed by field id, `{ schemaVersion, values }`.
  - **Rich text**: values reuse the page schema's `rtDoc`, through a new
    `SchemaSubsetValidator.ValidateDefinition`.
- **Saving** (CS-06 … CS-09):
  - The admin sends the whole proposed definition, and the server diffs it **by id** into
    typed changes.
  - `FieldConversion` classifies each change (free, lossless, lossy or forbidden) and
    converts values per the FR-026 table, with fixed formats and UTC.
  - A preview returns counts, examples and an **impact digest**. A lossy save must echo the
    digest, which the server recomputes under the lock (`confirmation_required`,
    `preview_outdated`).
  - The save runs in **one transaction**: `FOR UPDATE NOWAIT` on the collection row, a
    revision check, items rewritten in batches of 500, then the schema, history, references
    and outbox event, then commit. It is atomic by construction.
- **Item writes during a change** (CS-10): `CollectionWriteGate` takes `FOR SHARE NOWAIT`
  and refuses with `cms.items_being_updated`. #15 must route every item write through it.
- **References and delete** (CS-11): a `collection_references` table with `ON DELETE
  RESTRICT` on the target, so a referenced collection cannot be deleted and the database
  decides races. Self-references are not stored.
- **History and events** (CS-13, CS-14): a structured summary per revision, with no values,
  and `Cms.CollectionCreated`, `Cms.CollectionChanged` and `Cms.CollectionDeleted` (ids
  only).
- **Admin** (CS-17): Collections list, builder, preview, delete and history, reusing the
  editor's drag-and-drop, conflict banner, slugify and subset validator.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged.

**Primary Dependencies**: ASP.NET Core MVC, EF Core 10 + Npgsql, and the in-house
`SchemaSubsetValidator` on the backend; Vue 3, Pinia, vue-router and vue-i18n in the admin.
**Nothing new** (CS-22).

**Storage**: PostgreSQL 16+. **New `cms` schema** with `collections`, `items`,
`collection_references`, `schema_changes` and the outbox tables (data-model §1). The schema
and item values are stored as `jsonb`, mapped as `JsonDocument`.

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. The suites are listed in research CS-20. The main ones:
- `FieldConversionTests` and `SchemaDiffTests` (unit; SC-004);
- `CollectionSchemaGoldenTests` plus `collectionSchema.spec.ts` (SC-006);
- `RenameReorderTests` (SC-002), `SchemaMigrationTests` (SC-004) and
  `InterruptedSchemaChangeTests` (SC-005);
- `ItemWriteGateTests` (FR-037);
- `CollectionSchemaPerformanceTests` in the smoke project's Performance category (SC-003).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend and the Vue admin SPA. No
public-site change.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Preview** on 10,000 items: under 5 s.
- **Schema change** rewriting 10,000 items: under 30 s (SC-003).
- **Free changes** (rename, reorder): no item is written, so they take the same time as any
  small save.

**Constraints**:
- **Atomicity**: all-or-nothing per save, with readers never seeing a mix (FR-021, FR-037).
- **Logs**: no item values, titles, field names or collection names in logs, events or
  history beyond the summary (Principle IX).
- **Determinism**: conversions are culture-independent and in UTC (CS-07).
- **Offline**: the default install still makes zero outbound requests.

**Scale/Scope**:
- **Code**: 1 new module (2 projects).
- **Data**: 4 tables plus the outbox, in 1 migration.
- **Endpoints**: 7 admin endpoints.
- **Capabilities**: 2. **Events**: 3. **Configuration keys**: 0.
- **Admin**: 2 views, 7 components, 1 API client and 1 TypeScript rules port.
- **Contract**: 1 new contract (document, schema file, golden examples).
- **Shared change**: 1 additive change to `Pages.Contracts` (`ValidateDefinition`).
- **Stated limits**: 100 collections per site, 100 fields, 200 options, 100 references per
  value, 10,000 items as the tested size.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **New module** `Cms` with exactly two projects, `CmsModule.AddCmsModule()`, and `Domain/`, `Features/`, `Infrastructure/` folders. Registered in the host with one line plus `AddApplicationPart`. <br>• **Slices**: `ListCollections`, `CreateCollection`, `GetCollection`, `SaveCollection`, `PreviewCollection`, `DeleteCollection`, `GetSchemaHistory`. Thin controllers and in-house handlers; handlers use `CmsDbContext` directly. <br>• **Rich domain model** (required for Cms): `Collection` aggregate, `SchemaDiff`, `FieldConversion`, `CollectionSchemaRules`, with unit tests. <br>• **Cross-module**: Cms → `Pages.Contracts` (validator, page schema), `Media.Contracts` (`IPublicMediaReader`), `Sites.Contracts`, `Identity.Contracts`. Nobody references Cms. No transaction spans modules. The media read sits outside the Cms transaction and is safe because file kinds never change (CS-12). <br>• **Data**: the `cms` schema with its own migrations history table. <br>• **Enforcement**: `ModuleBoundaryTests` lists the new assemblies. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys. One forward-only migration, applied automatically, with no data to move. Nothing new to install. Health checks cover the new context through `IModuleDatabaseProvider`. |
| III | Upgrades Never Lose Data | **Complies.** <br>• **Upgrade**: `UpgradeFrom013Tests` restores the 013 snapshot and upgrades it. <br>• **Export**: the schema, item values and history are self-contained documents keyed by ids, ready for export and import (#20). <br>• **Intentional loss**: deleting values inside a schema change is a member's explicit, confirmed action (clarification Q2), not an upgrade effect. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. Locking uses PostgreSQL row locks, so multi-instance behaviour needs no shared cache or broker (CS-09, CS-10). |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Site ownership**: every new table carries `site_id` with a global filter. <br>• **Filter bypass**: admin reads use the module's single `ForSite<T>(siteId)` helper (`AcrossAllSites` plus an explicit `SiteId` predicate, as Pages and Editor do); the one raw query (`FOR UPDATE NOWAIT` / `FOR SHARE NOWAIT` through `FromSql`) applies the site filter explicitly, with a code comment and `CmsSiteFilterTests`. <br>• **Targets**: reference targets resolve through `ForSite<Collection>(siteId)`, so another site's collection is "not found". <br>• **Isolation**: `CmsIsolationProbe` joins the isolation suite. <br>• **Permissions**: site-scoped capabilities. |
| VI | Content Schemas Are Versioned Contracts | **Complies; this feature delivers the collection schema contract.** <br>• **Contract**: `docs/contracts/collection-schema.md` plus `v1.schema.json` plus golden examples, embedded by Cms and imported by the admin. Both validate against it, and golden files prove they agree (SC-006). <br>• **Versions**: `schemaVersion` on the schema and on every item values document. A versioning policy is in contract §9. <br>• **Ids**: stable, immutable field ids, never reused; item data keyed by field id (FR-006, FR-014). <br>• **Rich text**: shares the page schema's single `rtDoc` definition. |
| VII | Draft and Published Are Separate Worlds | **Complies except for exception E-6** (Complexity Tracking). <br>• **Separate copies**: items keep draft and published values separately. <br>• **Exception E-6**: a schema change migrates both copies at once (clarification Q1), so removing a field or a lossy conversion changes published item data without a publish. "Editing never changes what visitors see until publish" would be broken as soon as items are rendered. Today nothing renders items, so no visitor sees the change. The follow-up belongs to #16, which renders items and must decide how a schema change reaches live pages (for example invalidating them on `Cms.CollectionChanged`, or requiring the affected pages to be republished). <br>• **Public path**: none in this feature, so no write access to content tables from it. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Not affected.** No rendering changes. The events give #16 what it needs for the page → collection edges in the dependency graph. |
| IX | Secure by Default | **Complies.** <br>• **Authorization**: server-side capability on every action (CS-15). The preview needs view, because it shows item titles. <br>• **Rich text**: stays structured JSON (`rtDoc`), and text-to-rich-text conversion builds nodes, never markup. <br>• **Input**: proposals are size-capped (64 KiB) and duplicate JSON properties refused. Search-like inputs are absent. <br>• **Privacy**: logs, events and history carry no item values or titles (FR-033). |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: every one has integration tests on PostgreSQL. <br>• **Contract**: golden files run by both validators. <br>• **Domain**: unit tests for every conversion pair and diff kind. <br>• **Critical path**: schema migration is one, and keeps 80 % line coverage. CI's gate measures the whole solution, so Cms is included without a change. <br>• **Smoke test**: the release smoke test gains "create a collection" (one API call). |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies.** <br>• **Not built**: a background migration engine, a schema snapshot store, a preview store and a seeding endpoint. Items are seeded by tests and a dev script (CS-19). <br>• **Delivery**: one PR. The Collections entry is useful on its own, and #15 builds on it. No feature flag is needed. |

**Technology Constraints**:
- MVC controllers only.
- EF Core only. The single raw query is `FromSql` for the row lock, parameterised and
  site-filtered explicitly, with a test. Item rewrites go through tracked entities, not SQL.
- No Node process at run time; the admin build is unchanged.

**Development Workflow**:
- Issue #14 → `specs/014-cms-collections-schema` → branch `014-cms-collections-schema` → one
  PR that closes #14.
- Clarify has run: 5 questions, all answered. Analyze follows tasks.
- **Contract changes in the same PR**:
  - new `docs/contracts/collection-schema.md`, `collection-schema/v1.schema.json` and
    examples, and the `docs/contracts/README.md` table;
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (three Cms events);
  - `specs/002-identity-setup/contracts/authorization.md` §2 (two capabilities);
  - `SchemaSubsetValidator.ValidateDefinition` in C# and TypeScript (additive).
- **Spec corrections during planning**:
  - The date conversions said "the site's time zone", but sites have none (012
    assumption). FR-026 and the edge case now say UTC (CS-07).
  - FR-029, FR-024 and US3 scenario 1 now allow a fill-in value on any added field, not
    only required ones. Otherwise boolean fill-ins (FR-031) could never apply, since a
    missing boolean counts as false.

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 1, additive and automatic: `InitialCms` (Cms).
- **New dependencies**: none.
- **Upgrade notes**:
  1. A **Collections** area appears for each site, empty. Administrators and Designers can
     define collections; Editors can view them.
  2. Schema changes that delete or convert values ask for confirmation and cannot be
     undone. Back up the database before large restructurings.

**Post-Phase-1 re-check**:
- `cms-api.yaml` puts a capability on every operation;
- `module-surface.md` adds references only from Cms to existing contracts, with no cycle;
- every new table is site-filtered;
- events carry ids only;
- the contract has a versioning policy and golden examples;
- the one raw query is accounted for.

One recorded exception (E-6, Principle VII) with its justification and follow-up; no other
violation. **Gate: PASS with E-6.**

## Project Structure

### Documentation (this feature)

```text
specs/014-cms-collections-schema/
├── plan.md                      # This file
├── spec.md                      # Feature specification (clarified: 5 questions)
├── research.md                  # Phase 0: decisions CS-01 … CS-22
├── data-model.md                # Phase 1: tables, domain rules, events, error codes
├── quickstart.md                # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── collection-schema.md     # Draft of docs/contracts/collection-schema.md (schema, values, rules, limits)
│   ├── cms-api.yaml             # HTTP: collections, save, preview, delete, history
│   └── module-surface.md        # Projects, ValidateDefinition, capabilities, events, obligations on #15/#16
├── checklists/
│   └── requirements.md
└── tasks.md                     # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
QuickBuilder.slnx                         # ~ + the two Cms projects
Dockerfile                                # ~ restore stage: COPY the two Cms *.csproj (projects are listed one by one);
                                          #   docs/contracts/ is already copied whole to both stages
qb.ps1                                    # ~ + seed-items (CS-19)

docs/
~ contracts/README.md                     # + collection schema row
+ contracts/collection-schema.md          # from contracts/collection-schema.md
+ contracts/collection-schema/v1.schema.json
+ contracts/collection-schema/examples/valid/*.json  invalid/*.json
~ backlog.md                              # + unique field values (clarification Q4); schema snapshots/restore
~ operations.md                           # Collections: who can change schemas, irreversible changes, back up first

src/
├── QuickBuilder.Host/
│   ~ Program.cs  QuickBuilder.Host.csproj           # AddCmsModule, AddApplicationPart, project reference
├── Modules/Pages/QuickBuilder.Pages.Contracts/
│   ~ Schema/SchemaSubsetValidator.cs                # + ValidateDefinition
├── Modules/Cms/
│   ├── + QuickBuilder.Cms.Contracts/
│   │   + QuickBuilder.Cms.Contracts.csproj          # embeds docs/contracts/collection-schema/*.schema.json
│   │   + CollectionSchema.cs  CollectionSchemaLimits.cs
│   │   + Events/CollectionCreated.cs  CollectionChanged.cs  CollectionDeleted.cs
│   └── + QuickBuilder.Cms/
│       + QuickBuilder.Cms.csproj  CmsModule.cs  CmsCapabilities.cs  CmsErrors.cs  CmsErrorResults.cs
│       + Domain/Collection.cs  CollectionSchemaDocument.cs  Field.cs  FieldOption.cs  FieldType.cs
│       │        SchemaProposal.cs  SchemaDiff.cs  FieldChange.cs  FieldConversion.cs  ValueOutcome.cs
│       │        ImpactSummary.cs  ImpactDigest.cs  CollectionSchemaRules.cs  ReservedKeys.cs
│       │        Item.cs  ItemValues.cs  SchemaChangeEntry.cs  Events/DomainEvents.cs
│       + Infrastructure/CmsDbContext.cs  CmsDbContextFactory.cs  CmsOutbox.cs  CmsAccess.cs
│       │                CollectionLocks.cs (FromSql FOR UPDATE / FOR SHARE NOWAIT)
│       │                CollectionWriteGate.cs  ItemMigrator.cs  ImpactCalculator.cs  MediaKinds.cs
│       │                ItemValueValidator.cs  Schema/CollectionSchemaValidator.cs
│       │                ISchemaMigrationObserver.cs
│       + Infrastructure/Migrations/<ts>_InitialCms.cs
│       + Features/ListCollections/  CreateCollection/  GetCollection/  SaveCollection/
│                  PreviewCollection/  DeleteCollection/  GetSchemaHistory/

src/admin/
├── src/
│   + api/cms.ts                                     # client for contracts/cms-api.yaml
│   + features/cms/CollectionsView.vue  CollectionFormDialog.vue  CollectionBuilderView.vue
│   │             FieldList.vue  FieldEditor.vue  OptionsEditor.vue  AddFieldMenu.vue
│   │             SchemaPreviewDialog.vue  DeleteCollectionDialog.vue  SchemaHistoryPanel.vue
│   │             collectionSchema.ts  collectionSchemaRules.ts  cmsErrors.ts
│   ~ editor/schema/subsetValidator.ts               # + validateDefinition
│   ~ app/router.ts  i18n/en.json  (site navigation)  # + Collections
└── tests/
    + cms/CollectionsView.spec.ts  CollectionBuilderView.spec.ts  FieldList.spec.ts
    │     SchemaPreviewDialog.spec.ts  DeleteCollectionDialog.spec.ts  SchemaHistoryPanel.spec.ts
    │     collectionSchema.spec.ts (golden files)
    + cms/validateDefinition.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/         # Cms in ModuleImplementations/ModuleContracts;
                                          #   AuthorizationDeclarationTests covers the new actions
+ QuickBuilder.UnitTests/Cms/             # SchemaDiffTests, FieldConversionTests, CollectionSchemaRulesTests,
│                                         #   ImpactDigestTests, CollectionSchemaGoldenTests, CollectionTests
~ QuickBuilder.UnitTests/Pages/           # + SchemaSubsetValidatorDefinitionTests
~ QuickBuilder.IntegrationTests/
│   + Cms/CollectionApiTests.cs  RenameReorderTests.cs  SchemaMigrationTests.cs  ConfirmationTests.cs
│   │     InterruptedSchemaChangeTests.cs  ConcurrencyTests.cs  ItemWriteGateTests.cs
│   │     CollectionReferenceTests.cs  SchemaHistoryTests.cs  CmsSiteFilterTests.cs
│   │     UpgradeFrom013Tests.cs  ItemSeed.cs  CollectionSchemaContractTests.cs
│   + Isolation/Probes/CmsIsolationProbe.cs
│   ~ Infrastructure/EndpointCatalog.cs
~ QuickBuilder.SmokeTests/                # + CollectionSchemaPerformanceTests (Performance); journey: + create collection
specs/001-foundation-skeleton/contracts/integration-events.md   # ~ + Cms events
specs/002-identity-setup/contracts/authorization.md             # ~ §2 + cms capabilities
```

**Structure decision**:
- **Cms** follows the Pages layout. Domain types are pure and hold every rule that does not
  need the database: diff, conversions, rules, digest.
- **Infrastructure** holds the three database-bound pieces: the locks, the migrator and the
  impact calculator.
- **Admin**: the builder lives under `features/cms/`. It shares `subsetValidator.ts` and
  `slugify.ts`, and the editor's `useDragDrop`.

## Complexity Tracking

Status of exceptions carried over from earlier plans, as the Governance section requires.

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. The builder is covered by vitest and quickstart §1–§8, until #25. |

**New exceptions**:

| Exception | Rule broken | Why needed | Simpler alternative rejected because | Follow-up |
|---|---|---|---|---|
| **E-6** Schema changes rewrite published item data | Principle VII: "Editing never changes what visitors see until publish." | Clarification Q1 chose one live schema. Keeping published data in an older shape would force every reader and the renderer to handle every past revision of every collection, and a drafted schema would need its own publish workflow. Nothing renders items yet, so no visitor is affected while this feature stands alone. | *Migrate drafts only* (clarify option B) leaves published data in mixed shapes. *Draft and published schemas* (option C) duplicates publishing for schemas. Both were offered and declined in clarification Q1. | **#16** (templates and binding) must close E-6 before it renders items: decide how a schema change reaches live pages (invalidate on `Cms.CollectionChanged`, or require republishing the affected pages), and record the decision in its plan. Tracked in `docs/backlog.md` (T085). |

## Risks

- **Long requests on large collections**: a 10,000-item change holds a request for several
  seconds, and a reverse proxy with a short timeout could cut it.
  - *Mitigation*: the transaction rolls back if the connection drops, and the member
    retries. `docs/operations.md` states the proxy timeout to allow (60 s). SC-003 is
    measured on the reference setup. Past 10,000 items, a background engine is the recorded
    next step.
- **Preview and save disagreeing**: if the preview and the migration counted differently,
  members would lose values they were not warned about.
  - *Mitigation*: both use the same `FieldConversion` classification. `SchemaMigrationTests`
    asserts stored outcomes equal the preview for every pair (SC-004). The digest catches
    item changes between preview and save.
- **#15 bypassing the write gate**, which would let item writes slip past a migration.
  - *Mitigation*: the gate is the only documented path (data-model §3.3, module-surface §5),
    and #15's plan must list it in its Constitution Check. A later architecture rule can
    forbid `items` writes outside the gate once #15 exists.
- **TypeScript and C# rules drifting**.
  - *Mitigation*: shared golden files run by both suites (SC-006), as for design tokens.
- **Lock contention from long previews**: previews take no locks, so they never block
  saves. Only saves and deletes lock, briefly.

## Deferred (recorded, not built)

- **Unique field values** (clarification Q4): to `docs/backlog.md`.
- **Restoring a schema or a removed field**, and full schema snapshots per revision.
- **Background migration** for collections far beyond 10,000 items.
- **A per-site time zone** for date conversions.
- **Item editing, item media usage, reference integrity on item delete** (#15); rendering
  and invalidation (#16); trash (#21); export and import (#20).
