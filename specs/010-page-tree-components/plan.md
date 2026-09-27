# Implementation Plan: Pages, Page Tree and Shared Components

**Branch**: `010-page-tree-components` | **Date**: 2026-09-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-page-tree-components/spec.md`

**Issue**: #10. This feature narrows exception **E-3** from the
[001 plan](../001-foundation-skeleton/plan.md#complexity-tracking).

## Summary

Nothing can be edited or rendered until pages exist. This feature adds the `Pages`
module:
- a per-site tree of pages with titles, slugs and computed addresses;
- create, rename, reorder, move, delete and make-home;
- shared components, such as a header and footer, that page drafts reference by id;
- one draft content document per page and per component, versioned and validated against
  a page schema contract, which now lives in `docs/contracts/`.

Nothing visitors see changes (Principle VII). Publishing and rendering are #12.

The approach, detailed in [research.md](./research.md):
- **Tree as one aggregate**: `PageTree` per site holds every page's structural fields.
  Its `page_trees` row is the concurrency point for structural changes, retried up to 3
  times after a lost race. Each page's own `version` catches stale intent. Moves name an
  anchor sibling rather than an index (P-02, P-05, P-06).
- **Addresses** are computed only by the aggregate, stored, and unique per site through
  `UNIQUE (site_id, path)`. Clarification Q1 made uniqueness address-based rather than
  slug-based. The home page is `/`, contributes nothing to its children's addresses, and
  keeps its slug in reserve (P-03, P-07).
- **Reserved addresses**: `api`, `admin` and `health`. A coverage test keeps the list
  complete against `RoutePrefixes` (P-04).
- **Drafts** are separate aggregates (`page_drafts`, `component_drafts`, two entities over one unmapped base class), with
  `jsonb` documents, whole-document saves against a version, and no contention with
  structure (P-08).
- **Page schema**: `docs/contracts/page-schema/v1.schema.json` is the single definition
  and is embedded into the module. An in-house validator interprets a declared keyword
  subset, after a structural walk (depth, count, ids). Golden examples and contract
  tests keep the document and the schema in step. v1 defines only `root` and
  `component-ref` (P-09, P-10).
- **Components**: the usage rows are rewritten with each page-draft save. Composite
  foreign keys make references to other sites' components, and deletion of used
  components, impossible in the database (P-11).
- **Deletes are permanent**. A page with children is refused (clarification Q2), and so
  is the home page while other pages exist (P-12).
- **Capabilities**: four new ones. Designer builds, Editor writes drafts (clarification
  Q3, P-13).
- **Events**: domain events become 6 integration events through the Pages outbox. No
  consumer yet (P-14).
- **Admin shell**: a Pages screen and a Components screen, fully keyboard-operable, with
  no drag-only path (P-17).

There are no new NuGet or npm packages. JsonSchema.Net was rejected over its licence
(P-09).

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged.

**Primary Dependencies**:
- **Backend**: ASP.NET Core MVC, EF Core 10 + Npgsql (`jsonb`, `xmin`,
  composite FKs) and `System.Text.Json` (`JsonDocument`, duplicate-property detection).
  Nothing new.
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n and `String.prototype.normalize`.
  Nothing new.

**Storage**: PostgreSQL 16+. A new `pages` schema with six tables plus the outbox
([data-model.md](./data-model.md)). No other schema changes.

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL and vitest + axe,
as before. New:
- `PageTreeAggregateTests`, with random operation sequences asserting the invariants;
- `PageTreeConcurrencyTests`;
- `PageDocumentValidatorTests`, over the golden examples;
- `PageSchemaContractTests` and `PageSchemaSubsetTests`;
- `ReservedAddressCoverageTests`;
- `PagesIsolationProbe`;
- `PagesAuthorizationTests`;
- `ComponentUsageRaceTests`;
- `UpgradeFrom009Tests`;
- `PageTreePerformanceTests` (smoke);
- the admin specs for the tree, components and slugify.

**Target Platform**: Linux containers (amd64 in CI), unchanged.

**Project Type**: Web application: a modular-monolith backend plus the Vue admin SPA it
serves.

**Performance Goals**:
- Moving or reslugging a page with 200 descendants: under 2 s (SC-004).
- A 2,000-page tree listed and shown: under 2 s (SC-005).
- Both are measured on the reference compose setup (2 vCPU, 4 GB).

**Constraints**:
- No change is visible publicly.
- Documents are at most 1 MiB, 5,000 nodes and depth 32.
- Pages: at most 10 levels and 2,000 per site.
- No titles, names, slugs or content in logs or event payloads beyond paths (FR-037).
- `/api/v1/pages/...` is new, so there is no compatibility constraint yet. It is
  additive from here on.

**Scale/Scope**:
- **Code**: 1 new module (2 projects).
- **Data**: 6 tables.
- **Endpoints**: 17 admin endpoints, and 4 new capabilities.
- **Events**: 6 integration events.
- **Contract**: 1 contract document, 1 schema file and about 13 golden examples.
- **Admin shell**: 2 new screens.
- **Tests**: 1 new probe.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **Module**: `Pages` is one of the constitution's named modules, with the usual two projects, `internal` by default and `AddPagesModule()`. <br>• **Slices**: one per action under `Features/` (P-16), with thin controllers and in-house handlers. <br>• **Rich domain model with domain events**, as required for Pages: the `PageTree`, `Component` and `Draft` aggregates own the rules and record domain events (P-01, P-02, P-14). <br>• **Cross-module**: only through contracts. `ISiteDirectory` (Sites) checks the site, and `IUserDirectory` (Identity) supplies display names for "last saved by". No other module's tables. Events go out through the Pages outbox. <br>• **Shared code**: nothing added to `Shared.Kernel` or `Shared.Infrastructure`. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys. The limits are contract constants (research, Configuration keys). The migration is automatic and additive. Nothing outbound. Health checks pick the module up through `IModuleDatabaseProvider`. |
| III | Upgrades Never Lose Data | **Complies.** <br>• **Migration**: `InitialPages` creates a new schema only. <br>• **Upgrade test**: `UpgradeFrom009Tests` upgrades a 009 snapshot (P-18). <br>• **Contracts**: all new. `schemaVersion` is on every stored document, and the forward-migration policy for breaking schema changes is written into the contract (§6). <br>• **Export**: pages, drafts and components are plain rows plus `jsonb`, readable by #20. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. No cache, no storage and no new packages. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Isolation**: every table is `ISiteOwned` with a query filter. `SiteFilterCoverageTests` covers the new model automatically. <br>• **Crossing sites**: admin reads cross only through one `ForSite(siteId)` helper with one written reason (P-15). <br>• **Composite FKs**: a draft can't reference another site's component (P-11). <br>• **Isolation suite**: `PagesIsolationProbe` joins it, and the coverage guard enforces probe and endpoint coverage. <br>• **Permissions**: per site, through four site-scoped capabilities. |
| VI | Content Schemas Are Versioned Contracts | **Complies. This feature is the principle's first use.** <br>• **Location**: the page tree schema lives in `docs/contracts/` as `page-schema.md` plus `page-schema/v1.schema.json`. <br>• **One definition**: the product validates against that exact file, embedded (P-09). #11 will validate against the same file. <br>• **Versioning**: `schemaVersion` is on every stored document and in a matching column. <br>• **Breaking changes**: need a new version and a forward migration, as the contract states. <br>• **Block types**: defined once, as a `$defs` entry plus a `oneOf` branch. Adding one touches no other (P-10). <br>• **Drift**: `PageSchemaContractTests` fails the build when the document and the schema disagree (SC-007). |
| VII | Draft and Published Are Separate Worlds | **Complies.** Only drafts exist. They are separate tables, so #12 can add published tables beside them (P-08). No endpoint here is public, and none changes anything visitors see (US3 #6). |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **N/A for rendering**, which is #12. This feature prepares for it: <br>• stored, indexed addresses give a one-lookup path-to-page mapping; <br>• `component_usages` is the page → component edge of the dependency graph that invalidation needs; <br>• `PageAddressesChanged` and `ComponentDeleted` are the change signals. |
| IX | Secure by Default | **Complies.** <br>• **Authorization**: every endpoint declares one capability, enforced server-side. The UI hides controls only as a courtesy. <br>• **Not found versus forbidden**: another site's ids answer 404, identical to unknown ids. <br>• **No raw HTML**: none in documents, a written contract rule (P-10, §3.8). <br>• **Parsing**: size is capped before parsing; duplicate JSON properties are refused; depth and count are bounded before the schema walk (P-09). <br>• **Logs**: no content bodies in logs (FR-037). <br>• **CSRF**: the header rule applies unchanged. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Integration tests**: every endpoint has them against real PostgreSQL. <br>• **Unit tests**: the domain has them, including invariant tests over random operation sequences. <br>• **Golden files**: schema validation has them (the examples), and the contract has drift tests. <br>• **Smoke test**: gains create page, child and draft (FR-036). <br>• **Coverage gate**: `QuickBuilder.Pages` joins the per-package ≥ 80% gate, because the tree, validator and drafts are the start of the publish and render critical path (P-19). |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **Packages**: none. The in-house validator covers a declared subset, with a named fallback (Corvus) if #11 outgrows it. <br>• **Machinery kept out**: no ltree or closure table, no rank strings, no domain-event bus in the kernel, no drag-and-drop, no navigation or redirect engine. <br>• **Delivery**: the feature ships whole in one PR, so no feature flag. |

**Technology Constraints**:
- MVC controllers only.
- EF Core only:
  - `jsonb` for documents, as the constitution requires;
  - `xmin` tokens;
  - two draft entities over one unmapped base class, each with its own table;
  - composite FKs and partial unique indexes, declared through the model, with the
    partial index filter in `HasFilter`.
- No raw SQL beyond what EF migrations generate.
- Read paths use `AsNoTracking` projections.
- Admin assets are built into the image.

**Development Workflow**:
- Issue #10 → `specs/010-page-tree-components` → branch `010-page-tree-components` → one
  PR that closes #10.
- The prefix `010` resolves to issue #10 in `spec-ids.yml`. The issue exists.
- Clarify has run: three questions, all answered. Analyze follows tasks.
- The same PR updates `docs/contracts/` (the document, the schema and the examples), as
  the workflow requires for contract changes. There's no stored data to migrate, because
  this is version 1.

**Operator impact** (required by the workflow section):
- **New configuration keys**: none.
- **Migrations**: 1 (`InitialPages`, in the new `pages` schema). Additive and automatic.
- **New dependencies**: none.
- **Upgrade notes**:
  1. A new **Pages** area appears for each site in the admin shell.
  2. Designers can build the page structure and shared components. Editors can view them
     and save page drafts.
  3. Nothing on public sites changes until publishing arrives (#12).
  4. The addresses `/api…`, `/admin…` and `/health…` can never be page addresses.

**Post-Phase-1 re-check**: the data model and the two contracts introduce no violation:
- `pages-api.yaml` is all admin surface, with a capability on every operation.
- `page-schema.md` puts the schema in `docs/contracts/` with versioning and a migration
  policy.
- The only cross-module reads are through `Sites.Contracts` and `Identity.Contracts`.

E-3 narrows (see below), and no new exception is needed. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/010-page-tree-components/
├── plan.md              # This file
├── spec.md              # Feature specification (clarified; FR-003 wording refined, P-07)
├── research.md          # Phase 0: decisions P-01 … P-20
├── data-model.md        # Phase 1: pages schema, aggregates, invariants, events, error codes
├── quickstart.md        # Phase 1: validation guide, one section per story and criterion
├── contracts/
│   ├── pages-api.yaml   # HTTP contract: /api/v1/pages/sites/{siteId}/**
│   └── page-schema.md   # Planning copy of docs/contracts/page-schema.md, with v1 schema
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
docs/
+ contracts/page-schema.md                 # authoritative contract (from contracts/page-schema.md)
+ contracts/page-schema/v1.schema.json     # the single definition (P-09)
+ contracts/page-schema/examples/{valid,invalid}/*.json   # golden files
~ contracts/README.md                      # points at the page schema
~ operations.md                            # + "Pages": reserved addresses, limits, nothing public yet
~ contributing.md                          # + "Adding a block type" (a $defs entry + oneOf branch + examples)

src/
├── QuickBuilder.Host/
│   ~ Program.cs                           # + AddPagesModule, + application part
└── Modules/
    └── Pages/
        + QuickBuilder.Pages.Contracts/
        │   + PageAddresses.cs             # grammar, reserved segments, limits
        │   + PageSchema.cs                # versions, limits, OpenSchema() over the embedded file
        │   + IPageDocumentValidator.cs    # + DocumentKind, DocumentValidationResult
        │   + Events/PageCreated.cs  PageDeleted.cs  PageAddressesChanged.cs
        │   │        HomePageChanged.cs  ComponentCreated.cs  ComponentDeleted.cs
        │   + QuickBuilder.Pages.Contracts.csproj   # EmbeddedResource → docs/contracts/page-schema/*.schema.json
        + QuickBuilder.Pages/
            + PagesModule.cs  PagesCapabilities.cs  PagesErrorResults.cs
            + Domain/AggregateRoot.cs              # internal domain-event list (P-14)
            + Domain/PageTree.cs  Page.cs  PageAddress.cs  Slug.cs
            + Domain/Component.cs  Draft.cs  PageDraft.cs  ComponentDraft.cs
            + Domain/Events/*.cs                   # domain events
            + Infrastructure/PagesDbContext.cs     # schema, filters, composite FKs, ForSite helper
            + Infrastructure/PagesOutbox.cs        # domain → integration events
            + Infrastructure/TreeConcurrencyRetry.cs
            + Infrastructure/Schema/SchemaSubsetValidator.cs    # the keyword subset
            + Infrastructure/Schema/PageDocumentValidator.cs    # size → parse → version → walk → schema → components
            + Infrastructure/Migrations/<ts>_InitialPages.cs
            + Features/GetPageTree/  CreatePage/  GetPage/  UpdatePage/  MovePage/
            │         MakeHomePage/  DeletePage/
            + Features/GetPageDraft/  SavePageDraft/
            + Features/ListComponents/  CreateComponent/  GetComponent/  RenameComponent/
                      DeleteComponent/  ListComponentUsages/  GetComponentDraft/  SaveComponentDraft/

src/admin/
├── src/
│   + api/pages.ts                         # typed client for pages-api.yaml
│   ~ app/router.ts                        # + /sites/:siteId/pages, /sites/:siteId/components
│   ~ features/sites/SiteDetailView.vue    # links to Pages and Components
│   + features/pages/PageTreeView.vue  PageRow.vue  PageFormDialog.vue  MoveDialog.vue
│   │                 DeletePageDialog.vue  pageErrors.ts  slugify.ts
│   + features/pages/ComponentsView.vue  ComponentUsagesPanel.vue
│   ~ i18n/en.json
└── tests/
    + pageTreeView.spec.ts  componentsView.spec.ts  slugify.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/          # Pages obeys module rules (picked up by the existing
│                                          #   assembly scan; + Pages.Contracts references Kernel only)
+ QuickBuilder.UnitTests/Pages/            # PageTreeAggregateTests, SlugTests, PageAddressTests,
│                                          #   PageDocumentValidatorTests, SchemaSubsetValidatorTests
~ QuickBuilder.IntegrationTests/
│   + Pages/PagesApi.cs  PageTreeApiTests.cs  PageTreeConcurrencyTests.cs  DraftApiTests.cs
│   │       ComponentApiTests.cs  ComponentUsageRaceTests.cs  PagesAuthorizationTests.cs
│   │       PagesSiteScopingTests.cs  PageSchemaContractTests.cs  PageSchemaSubsetTests.cs
│   │       ReservedAddressCoverageTests.cs  UpgradeFrom009Tests.cs
│   + Isolation/Probes/PagesIsolationProbe.cs
│   ~ Isolation/IsolationProbes.cs         # + PagesIsolationProbe
│   ~ Observability/LoggingTests.cs        # + no page content in logs
~ QuickBuilder.SmokeTests/                 # + create page, child, draft; + PageTreePerformanceTests

.github/workflows/ci.yml                   # ~ QuickBuilder.Pages joins the per-package coverage gate
QuickBuilder.slnx                          # ~ + the two Pages projects
specs/002-identity-setup/contracts/authorization.md   # ~ §2 gains the four Pages capabilities
```

**Structure decision**:
- **Module**: all product behaviour lives in the new Pages module, laid out like Media.
- **Schema location**: the schema file lives in `docs/contracts/`, where the constitution
  puts contracts, and is embedded by `Pages.Contracts`. So the "shared schema package"
  on the .NET side is `QuickBuilder.Pages.Contracts`, which #12's renderer may reference.
  The TypeScript side will import the same file in #11.
- **Unchanged**: nothing moves into `Shared.*`.
- **002's contract**: updating `authorization.md` follows that document's rule that new
  capabilities are listed there.

## Complexity Tracking

Status of exceptions carried over from earlier plans, as the Governance section requires.

| Exception | Status after this feature |
|---|---|
| **E-1** | Closed by 002. |
| **E-2** | Closed by 003. |
| **E-3** (smoke test isn't the constitution's full sequence) | **Narrowed.** The smoke test now creates a page, and saves and reads its draft (FR-036). *Publish* and *view* still wait for Rendering (#12). Issue #4. |
| **E-4** (cache can't invalidate across instances) | Unchanged, as narrowed by 009. This feature adds no cache. |

**New exceptions**: none.

## Risks

- **The in-house validator may diverge from JSON Schema semantics** within the subset.
  - *Mitigation*:
    - `SchemaSubsetValidatorTests` pins each supported keyword's behaviour to the
      2020-12 specification text, with positive and negative cases.
    - `PageSchemaSubsetTests` refuses unsupported keywords in the file.
    - The golden examples are the shared oracle for #11's browser-side validator.
    - If #11's blocks need more than the subset, the recorded fallback is Corvus
      (Apache-2.0), with a source generator rather than runtime Roslyn (P-09).
- **Whole-tree load per structural change.** At the 2,000-page limit this is about
  300 KB per operation.
  - *Mitigation*: only structural columns are loaded, and SC-004 and SC-005 are measured
    in the smoke project. If sites need far more pages, the limit is a contract constant
    that a later spec can raise, together with a subtree-scoped load.
- **Serialising structural changes per site** means two designers reorganising the same
  site at the same moment retry against each other.
  - *Mitigation*: retries are automatic (P-05). Only a stale *page* version reaches the
    user as out of date. Content saves never contend with structure (P-08).
- **`jsonb` doesn't keep key order.** A future consumer that hashes document text would
  see changes that aren't there.
  - *Mitigation*: the contract defines equality as JSON value equality (P-08). #12's
    cache keys should hash a canonical serialisation, and this is noted there.
- **Events with no consumer yet** could drift from what #12 and #17 need.
  - *Mitigation*: the payloads are minimal (ids and paths). `PageAddressesChanged`
    includes old paths precisely for redirects (#17). Changing a payload later is
    additive.
- **The spec's FR-003 wording was refined during planning** (P-07).
  - *Mitigation*: the change is recorded in research P-07. `/speckit-analyze` will
    re-check consistency.

## Deferred (recorded, not built)

- **Content block types**, including the editor-side validator: #11.
- **Published copies, publish, rollback and rendering**, and the render dependency graph
  built on `component_usages`: #12.
- **Redirects from old addresses**, consuming `PageAddressesChanged`: #17.
- **Trash and restore** for pages and components: #21.
- **Pages generated from collection items**: #16.
- **Tracking which drafts use which media files**, and what happens when a used file is
  deleted (003 left this to Pages): with the first block type that references media, in
  #11.
- **Navigation menus** built from the tree, and **drag-and-drop** as an additional way to
  move pages.
- **Nested components.**
- **Localised, per-language pages and slugs**: backlog.
