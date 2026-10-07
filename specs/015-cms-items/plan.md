# Implementation Plan: CMS Items

**Branch**: `015-cms-items` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-cms-items/spec.md`

**Issue**: #15. Open exceptions from earlier plans: E-5 (unchanged) and E-6 (014; this
feature widens what it covers, see Complexity Tracking). No new exception.

## Summary

Editors, Designers and Administrators can now write a collection's items: a list with
search, filters, sort and pages, and a form generated from the schema. Items have separate
draft and published data, publish and unpublish, schedules, versions and rollback,
reference pickers, and deletion that is refused while other items refer to the item.
Rendering items is #16.

The approach, detailed in [research.md](./research.md):
- **`Item` becomes an aggregate** in the existing Cms module (IT-01), with versions,
  schedules and history as its parts. Every item write goes through one `ItemWrites` path
  (IT-06). That path:
  - enters 014's `CollectionWriteGate` (share lock, `cms.items_being_updated`);
  - checks the item `revision` and the collection's new `schema_revision` (IT-07);
  - validates, applies the operation and runs `ItemIndexer` (IT-11);
  - writes history and outbox events in the same transaction.
- **Storage** (IT-02, IT-03): `items` gains columns (slug, published slug, status,
  revision, name, search text, attention, publishing fields). New tables: `item_slugs`
  (one key enforces slug uniqueness across draft and published slugs), `item_references`
  (a `NO ACTION` foreign key on the target decides delete races), `item_media`,
  `item_versions`, `item_schedules` and `item_history`. The values documents keep the 014
  contract shape, so the contract does not change.
- **Validation** (IT-05): one report from 014's `ItemValueValidator`, extended with the
  database checks. A draft save refuses only malformed values (clarification Q2).
  Publishing, scheduling a publish and rolling back refuse any problem.
- **Publishing** (IT-08, IT-09): 012's model, inside Cms:
  - full-copy versions, 50 kept, rollback as a new version;
  - a manual action cancels a pending scheduled publish;
  - schedules are delayed Cms outbox messages, claimed once.
  A scheduled run that meets a running schema change retries; one that fails the publish
  checks is recorded as failed and the item marked as needing attention.
- **Schema changes now migrate** kept versions and pending scheduled content as well as
  drafts and published values (IT-12), so 014 FR-024's "no hidden copy" still holds. Fill-in
  values never go into versions.
- **List queries** (IT-13, IT-14): `search_text` with `LIKE`, using `SearchText` moved to
  the shared kernel. Filters and sorts on draft field values go through EF Core's JSON
  translation, with numbered pages.
- **Media usage** (IT-15): a new `IItemMediaUsageReader` in `Cms.Contracts`, which Media
  merges into the library's usage and in-use rules. Media deletes re-index the affected
  items.
- **Admin** (IT-19 … IT-21):
  - **List**: the list's state lives in the page address.
  - **Form**: one input per field type, reusing the editor's TipTap, `MediaControl`,
    `useDragDrop` and `ConflictBanner`.
  - **Pickers**: reference pickers that can be used with the keyboard alone.
  - **Publishing**: a publish bar, a history panel, and schedule and delete dialogs.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**: ASP.NET Core MVC, EF Core 10 + Npgsql (JSON translation, `citext`),
the in-house outbox; Vue 3, Pinia, vue-router, vue-i18n, TipTap (011). **Nothing new**
(IT-24).

**Storage**: PostgreSQL 16+, `cms` schema. Migration `AddItems` (data-model §1):
- **~** `collections`: + `schema_revision`.
- **~** `items`: + 14 columns and 5 indexes.
- **+** `item_slugs`, `item_references`, `item_media`, `item_versions`, `item_schedules`,
  `item_history`.

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. The suites are in research IT-23. The main ones:
- `ItemDraftAndPublishTests` (SC-004);
- `ItemReferenceIntegrityTests` (SC-005);
- `ItemScheduledExactlyOnceTests` (SC-006);
- `ItemListQueryTests` and `ItemListPerformanceTests` (SC-003);
- `ItemSchemaMigrationTests` (IT-12);
- `ItemValueGoldenTests` with `itemValues.spec.ts` (FR-012);
- the admin specs with axe (SC-007).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend and the Vue admin SPA. No
public-site change.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **List**: list, search, filter, sort and picker search at 10,000 items under 1 s p95
  (SC-003).
- **Single writes**: item save and publish under 300 ms p95 (one item, indexer included).
- **Scheduled actions**: within 1 minute (5 s poll) (SC-006).
- **Schema changes**: still within 014 SC-003 (30 s) with versions and schedules migrated
  (IT-12, measured).

**Constraints**:
- **Draft and published apart**: no draft write changes published data (SC-004,
  Principle VII).
- **Write gate**: every item write goes through the gate (014 obligation).
- **Logs**: no item values, names or slugs in logs, events or history (FR-032).
- **Offline**: zero outbound requests.

**Scale/Scope**:
- **Code**: 0 new projects.
- **Data**: 1 migration (6 new tables, 2 changed).
- **Endpoints**: 16 admin endpoints.
- **Capabilities**: 2. **Events**: 5 public and 1 internal.
- **Event handlers**: 3 (2 for Media events, 1 for schedules). **Background services**: 1
  (backfill). **Configuration keys**: 0.
- **Shared changes**:
  - **Shared kernel**: `SearchText` moves there.
  - **Media**: + item usage.
  - **Cms**: `ItemMigrator` and `Collection` are extended.
- **Admin**: 3 views, about 16 components, 1 API client and 1 TypeScript value-rules port.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module.** Items belong to Cms, the module the constitution names (IT-01). <br>• **Slices**: under `Features/Items/` (list, create, get, save, duplicate, delete, publish, unpublish, schedule, cancel schedule, history, referrers, rollback, copy to draft, slug availability, lookup), with thin controllers and in-house handlers over `CmsDbContext`. <br>• **Rich domain model**: the `Item` aggregate (status, schedules, versions, retention, rollback rules), `ItemValidation` and `ItemIndexer`, with unit tests. <br>• **Cross-module**: Media → `Cms.Contracts` (new, for `IItemMediaUsageReader`) and Cms → `Media.Contracts` (existing). Cms handles `Media.FileDeleted` and `Media.FileChanged` through the outbox, idempotently. No transaction spans modules. <br>• **Shared kernel**: gains `SearchText`, a pure text function with no module concept (IT-13). <br>• **Enforcement**: `ModuleBoundaryTests` adds the Media → `Cms.Contracts` edge, and `ItemWritesOnlyTests` keeps all item writes on one path. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys. One forward-only, automatic migration. The backfill service is idempotent and runs at start-up (IT-22). Health checks are unchanged. |
| III | Upgrades Never Lose Data | **Complies.** <br>• **Upgrade**: `UpgradeFrom014Tests` covers 014 items, both draft-only and published. Published items gain a version 1. <br>• **Export**: items, versions and schedules are self-contained documents for #20, and the derived tables can be rebuilt. <br>• **Intentional loss**: values removed by schema changes now also leave versions. That is 014's confirmed, intended behaviour (014 FR-024), not an upgrade effect. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. Schedules use the built-in outbox (IT-09), with no external job system. Multi-instance behaviour comes from row locks and `SKIP LOCKED`. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Site ownership**: every new table carries `site_id` with the global filter. <br>• **Admin reads** use `ForSite<T>(siteId)`. <br>• **Cross-site references**: references, media and lookups resolve only within the site, so another site's ids are "missing" or "malformed". <br>• **Raw SQL**: none new. <br>• **Tests**: `CmsSiteFilterTests` and `CmsIsolationProbe` are extended. <br>• **Permissions**: site-scoped capabilities. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **No contract change**: the slug is a column, not a value (IT-02). Item values are validated against the 014 contract on both sides. <br>• **New golden examples** for item values, run by the server and the admin (IT-05); these are additive examples, not a schema change. <br>• **Versions**: every stored values document, including versions and schedules, carries `schemaVersion` and is migrated forward by schema changes. |
| VII | Draft and Published Are Separate Worlds | **Complies; E-6 still open, with a wider scope.** <br>• **Separate**: draft and published values are kept separately, and only publish, unpublish, rollback and scheduled actions write published values (SC-004). <br>• **Publish**: atomic and audited (versions, history), with rollback (IT-08). Scheduled actions use idempotent outbox jobs (IT-09). <br>• **No public path** in this feature. <br>• **E-6**: schema changes still rewrite published values, and now kept versions too (IT-12). Nothing renders items yet, so visitors are unaffected; #16 must close E-6 before it renders items. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Not affected.** The new events (IT-17) give #16 its item → page invalidation edges. |
| IX | Secure by Default | **Complies.** <br>• **Authorization**: a server-side capability on every action (IT-16), and 404 for users without a role. <br>• **Rich text**: stays `rtDoc` JSON, and the form's editor only produces contract nodes (`toContractRichText`). <br>• **Input**: bodies capped at 1 MiB, duplicate JSON properties refused, search terms capped at 100 characters and escaped for `LIKE`, and filter and sort fields checked against the schema. <br>• **Privacy**: no values, names or slugs in logs, events or history. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: every one has PostgreSQL integration tests. <br>• **Contract**: golden item-value examples on both sides. <br>• **Domain**: unit tests for the aggregate, validation, status and slugs. <br>• **Critical path**: the publish path keeps 80 % line coverage. <br>• **Smoke test**: the release smoke test gains "create an item and publish it". |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies.** <br>• **Not built**: field-content search, keyset paging, expression indexes, autosave, bulk actions. <br>• **Reuse**: the outbox scheduler, `SearchText`, the write gate and the admin's editor pieces are reused, not rebuilt. <br>• **Delivery**: one PR. The Items tab is useful on its own. |

**Technology Constraints**:
- MVC controllers only.
- EF Core only. No new raw SQL: the filters and sorts on `jsonb` use Npgsql's translation
  (IT-14). 014's lock queries are reused unchanged.
- TipTap only for rich text (011 decision).
- No Node process at run time.

**Development Workflow**:
- Issue #15 → `specs/015-cms-items` → branch `015-cms-items` → one PR that closes #15.
- Clarify has run: 5 questions, all answered. Analyze follows tasks.
- **Contract and registry changes in the same PR**:
  - `docs/contracts/collection-schema/examples/items/` (golden item values) and a §5a note
    in `collection-schema.md` pointing to them (examples only, no version change);
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (five Cms item events);
  - `specs/002-identity-setup/contracts/authorization.md` §2 (two capabilities);
  - `specs/013-media-library-variants/contracts/readers.md` (Media now reads item usage);
  - 014 `contracts/module-surface.md` §5: the obligations on #15 are marked met.

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 1, additive and automatic: `AddItems` (Cms). A start-up backfill indexes
  existing items once.
- **New dependencies**: none.
- **Upgrade notes**:
  1. Each collection gains an **Items** tab. Every site role (Administrator, Designer,
     Editor) can create, edit, publish, schedule and delete items. Unlike pages, Editors
     can publish items.
  2. Items created before this release (test or demo data) get a slug from their name and
     keep their published data as version 1.
  3. Media used by published items counts as "in live use" in the media library.
  4. Schema changes that remove or convert values now also apply to the published versions
     kept for rollback.

**Post-Phase-1 re-check**:
- `items-api.yaml` declares a capability on every operation;
- `module-surface.md` adds only a contract-to-contract edge (Media → `Cms.Contracts`), with
  no cycle;
- every new table has `site_id` and the filter;
- events carry ids and flags only;
- the values contract is unchanged, and the new golden examples cover the report;
- no new raw SQL.

**Gate: PASS** (E-6 carried, with a wider scope recorded below).

## Project Structure

### Documentation (this feature)

```text
specs/015-cms-items/
├── plan.md                 # This file
├── spec.md                 # Feature specification (clarified: 5 questions)
├── research.md             # Phase 0: decisions IT-01 … IT-24
├── data-model.md           # Phase 1: tables, aggregate rules, states, events, error codes
├── quickstart.md           # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── items-api.yaml      # HTTP: items, publishing, schedules, history, referrers, lookup
│   └── module-surface.md   # SearchText move, IItemMediaUsageReader, capabilities, events, obligations
├── checklists/
│   └── requirements.md
└── tasks.md                # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
qb.ps1                                    # ~ seed-items: through the item API, + -Publish <percent>

docs/
~ contracts/collection-schema.md          # §5a: item-value golden examples
+ contracts/collection-schema/examples/items/*.json  expected.json
~ backlog.md                              # + field-content search (clarification Q5); bulk item actions
~ operations.md                           # Items: roles, schedules, media in live use via items

src/
├── Shared/QuickBuilder.Shared.Kernel/
│   + Text/SearchText.cs                  # moved from Media (IT-13)
├── Modules/Media/QuickBuilder.Media/
│   - Domain/SearchText.cs                # moved
│   ~ QuickBuilder.Media.csproj           # + Cms.Contracts reference
│   ~ Infrastructure/MediaFileMapper.cs  Features/GetFileUsage/  DeleteFile/  ReplaceFile/   # + item usage (IT-15)
├── Modules/Cms/
│   ├── QuickBuilder.Cms.Contracts/
│   │   + Media/IItemMediaUsageReader.cs
│   │   + Events/ItemCreated.cs  ItemDraftSaved.cs  ItemPublished.cs  ItemUnpublished.cs  ItemDeleted.cs
│   └── QuickBuilder.Cms/
│       ~ CmsModule.cs  CmsCapabilities.cs  CmsErrors.cs  CmsErrorResults.cs
│       ~ Domain/Item.cs                  # aggregate (IT-01)
│       ~ Domain/Collection.cs            # schema_revision (IT-07)
│       + Domain/ItemVersion.cs  ItemSchedule.cs  ItemHistoryEntry.cs  ItemStatus.cs
│       │        ItemSlug.cs  SlugRules.cs  ItemReport.cs  Events/ItemDomainEvents.cs
│       ~ Infrastructure/CmsDbContext.cs  CmsOutbox.cs  ItemMigrator.cs  ItemValueValidator.cs
│       + Infrastructure/ItemWrites.cs  ItemValidation.cs  ItemIndexer.cs  ItemReads.cs
│       │                ItemListQuery.cs  ItemMediaUsageReader.cs  ItemIndexBackfillService.cs
│       + Infrastructure/Migrations/<ts>_AddItems.cs
│       + Features/Items/ListItems/  CreateItem/  GetItem/  SaveItemDraft/  DuplicateItem/
│                  DeleteItem/  PublishItem/  UnpublishItem/  ScheduleItem/  CancelItemSchedule/
│                  GetItemHistory/  ListItemReferrers/  RollbackItem/  CopyVersionToDraft/
│                  CheckSlug/  LookupItems/  RunItemSchedule/  ReindexItemsForMedia/
│                  ItemResponses.cs

src/admin/
├── src/
│   + api/items.ts
│   + components/ZonedDateTimeInput.vue                       # extracted from ScheduleDialog (IT-20)
│   ~ features/publishing/ScheduleDialog.vue                  # uses ZonedDateTimeInput
│   + features/cms/items/ItemListView.vue  ItemFilters.vue  ItemPager.vue  ItemStatusBadge.vue
│   │                    ItemFormView.vue  ItemField.vue  ItemPublishBar.vue  ItemHistoryPanel.vue
│   │                    ItemScheduleDialog.vue  DeleteItemDialog.vue  ReferencePickerDialog.vue
│   │                    MultiReferencePicker.vue  ReferencePicker.vue
│   │                    inputs/TextInput.vue  RichTextInput.vue  NumberInput.vue  BooleanSwitch.vue
│   │                           DateInput.vue  MediaInput.vue  OptionSelect.vue
│   │                    itemValueRules.ts  itemErrors.ts  listQuery.ts
│   ~ features/cms/CollectionsView.vue  CollectionBuilderView.vue   # Items links and tab
│   ~ features/media/UsageList.vue                                  # item places
│   ~ app/router.ts  i18n/en.json
└── tests/
    + cms/items/*.spec.ts  itemValues.spec.ts (golden)
    + components/ZonedDateTimeInput.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/         # Media → Cms.Contracts edge; ItemWritesOnlyTests;
                                          #   AuthorizationDeclarationTests covers new actions
+ QuickBuilder.UnitTests/Cms/Items/       # ItemTests, ItemValidationTests, ItemStatusTests,
                                          #   SlugRulesTests, ItemValueGoldenTests
~ QuickBuilder.UnitTests/Media/           # SearchText tests follow the move
~ QuickBuilder.IntegrationTests/
│   + Cms/Items/ItemApiTests.cs  ItemDraftAndPublishTests.cs  ItemConcurrencyTests.cs
│   │           ItemReferenceIntegrityTests.cs  ItemScheduleTests.cs  ItemScheduledExactlyOnceTests.cs
│   │           ItemRollbackTests.cs  ItemListQueryTests.cs  ItemSchemaMigrationTests.cs
│   │           ItemMediaUsageTests.cs  UpgradeFrom014Tests.cs
│   ~ Cms/CmsSiteFilterTests.cs  Isolation/Probes/CmsIsolationProbe.cs  Infrastructure/EndpointCatalog.cs
│   ~ Media/ (usage tests include items)
~ QuickBuilder.SmokeTests/                # + ItemListPerformanceTests, ItemMigrationPerformanceTests;
                                          #   journey: + create and publish an item
specs/001-foundation-skeleton/contracts/integration-events.md   # ~ + item events
specs/002-identity-setup/contracts/authorization.md             # ~ §2 + item capabilities
specs/013-media-library-variants/contracts/readers.md           # ~ item usage
```

**Structure decision**:
- **Items** live in the Cms module under `Features/Items/`. Database-bound pieces are in
  `Infrastructure/`: the write path, the indexer, the list query and the usage reader. The
  rules sit in `Domain/`.
- **Admin**: under `features/cms/items/`, reusing the editor's rich text, media, drag and
  drop and conflict pieces.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. The item screens are covered by vitest with axe and quickstart §1–§10, until #25. |
| **E-6** (schema changes rewrite published item data) | **Still open, scope widened.** Items can now be published, and a schema change rewrites their published values and, to honour 014 FR-024, their kept versions (IT-12). Nothing renders items yet, so no visitor is affected. The follow-up is unchanged: #16 must decide how a schema change reaches live pages before it renders items. `docs/backlog.md`'s E-6 entry gains "versions are migrated too". |

**New exceptions**: none.

## Risks

- **Version migration cost** (IT-12): a schema change now rewrites up to four times as many
  documents.
  - *Mitigation*: only versions and schedules that hold a value in a changed field are
    loaded. `ItemMigrationPerformanceTests` (10,000 items, 3 versions each) runs against
    014 SC-003. The fallback, migrating versions in a second phase under the same lock, is
    recorded in IT-12.
- **Derived data drifting** from values: `name`, `attention`, references, media and slugs
  could disagree with the stored values.
  - *Mitigation*: one `ItemIndexer` is called by every writer and the migrator.
    `index_version` with the backfill service repairs rows after a future indexer change.
    The tests compare indexer output with a fresh recomputation after each scenario.
- **Admin and server validation disagreeing** (FR-012).
  - *Mitigation*: golden item-value examples run by both suites.
- **Scheduled run during a schema change**: the run retries rather than failing (IT-09),
  and `ItemScheduledExactlyOnceTests` covers a lock held across the due time.
- **Media rule surprise**: Editors cannot delete images that only published items use
  (IT-15). This is the 013 rule unchanged, and the upgrade notes and `operations.md` say so.
- **`jsonb` translation gaps** in Npgsql for nested key-existence (IT-12) or sorts (IT-14).
  - *Mitigation*: both have a stated fallback that costs speed, not correctness. Performance
    tests show which one is in use.

## Deferred (recorded, not built)

- Searching field content (clarification Q5), bulk list actions, keyset paging and
  per-field indexes beyond 10,000 items.
- Autosave and local recovery for the item form.
- Rendering items and closing E-6 (#16), export and import (#20), trash (#21), preview links
  (#22), approvals.
