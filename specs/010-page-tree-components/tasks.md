---

description: "Task list for 010-page-tree-components"
---

# Tasks: Pages, Page Tree and Shared Components

**Input**: Design documents from `/specs/010-page-tree-components/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The spec measures behaviour through automated checks (SC-002, SC-003,
SC-006, SC-007). FR-022 requires a build check that the contract and schema agree, and
FR-031 requires the isolation suite. Constitution Principle X makes tests mandatory,
including golden-file tests for schema validation. Within each story, test tasks come
first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US5 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

The layout is the modular monolith from [plan.md](./plan.md#project-structure). This
feature adds the `Pages` module. Shorthand used below:

- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `HOST` = `src/QuickBuilder.Host`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`

**Slices**:
- Feature folders follow 001's one-folder-per-slice rule. Each `Features/<Name>/<Name>.cs`
  holds the controller action, request/response, command or query, validator and handler.
- Each slice is registered in `PAGES/PagesModule.cs` in the same task.
- Every controller action declares exactly one `[RequireCapability(PagesCapabilities.X)]`
  (P-13).
- Every handler reads through `PagesDbContext.ForSite<T>(siteId)` (P-15), and checks the
  site with `ISiteDirectory.FindByIdAsync` first, returning `sites.not_found` as Media does.
- Every refusal goes through `PAGES/PagesErrorResults.cs`.

**References**: research decisions are cited as `P-xx`. Error codes are those in
[data-model.md](./data-model.md#validation-and-error-codes). Endpoints are those in
[contracts/pages-api.yaml](./contracts/pages-api.yaml).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, the two projects and their wiring. Nothing here has
behaviour.

- [X] T001 Commit `specs/010-page-tree-components/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `010-page-tree-components` at the repository root, referencing #10 (plan.md "Development Workflow")
- [X] T002 Create `PAGESC/QuickBuilder.Pages.Contracts.csproj`, referencing only `src/Shared/QuickBuilder.Shared.Kernel`, and `PAGES/QuickBuilder.Pages.csproj`, referencing `PAGESC`, `Shared.Kernel`, `Shared.Infrastructure`, `QuickBuilder.Sites.Contracts` and `QuickBuilder.Identity.Contracts`. Copy the property groups and the `InternalsVisibleTo` entries (UnitTests, IntegrationTests) from `src/Modules/Media/QuickBuilder.Media/QuickBuilder.Media.csproj`. Add both projects to `QuickBuilder.slnx`.
- [X] T003 [P] Add `ProjectReference`s to both Pages projects in:
  - `ITESTS/QuickBuilder.IntegrationTests.csproj`;
  - `UTESTS/QuickBuilder.UnitTests.csproj`;
  - `ATESTS/QuickBuilder.ArchitectureTests.csproj`.

  Add `PAGES` to `HOST/QuickBuilder.Host.csproj`.
- [X] T004 [P] Add `typeof(Pages.PagesModule).Assembly` and the `Pages.Contracts` assembly to the module and contract assembly lists in `ATESTS/ModuleBoundaryTests.cs`, next to Media's entries at lines 28 and 36. Add an assertion that `QuickBuilder.Pages.Contracts` references nothing but `Shared.Kernel` and the BCL.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the contract constants and the schema file;
- the entity classes and their mapping;
- the one migration;
- the site-scoping helper;
- capabilities, events, the outbox and the error mapping;
- the test harness and the isolation probe skeleton.

**⚠️ CRITICAL**: no user-story work can begin until this phase is complete.

### Contract constants and the schema file (P-04, P-09, P-10)

- [X] T005 [P] Create `CONTRACTS/page-schema/v1.schema.json` as the schema in [contracts/page-schema.md §7](./contracts/page-schema.md#7-the-schema-version-1), byte for byte.
- [X] T006 [P] Create `PAGESC/PageAddresses.cs` (public static):
  - `ReservedFirstSegments = ["api", "admin", "health"]`;
  - `MaxSlugLength = 100`, `MaxPathLength = 1024`, `MaxDepth = 10`, `MaxPagesPerSite = 2000`;
  - `IsValidSlug(string?)` using the generated regex `^[a-z0-9]+(-[a-z0-9]+)*$` and the length limit.
- [X] T007 [P] Create `PAGESC/PageSchema.cs` (public static):
  - `CurrentVersion = 1` and `SupportedVersions = {1}`;
  - `MaxDocumentBytes = 1_048_576`, `MaxNodes = 5000`, `MaxNodeDepth = 32`, `MaxNodeIdLength = 64`;
  - `Stream OpenSchema(int version)`, which reads the embedded resource.

  Embed `CONTRACTS/page-schema/v1.schema.json` in `PAGESC/QuickBuilder.Pages.Contracts.csproj` as `<EmbeddedResource Include="..\..\..\..\docs\contracts\page-schema\*.schema.json" LogicalName="QuickBuilder.Pages.Contracts.PageSchema.%(Filename)%(Extension)" />`, so that the file itself, not a copy, is what ships (P-09). `OpenSchema(1)` opens `QuickBuilder.Pages.Contracts.PageSchema.v1.schema.json` and throws `ArgumentOutOfRangeException` for an unsupported version.
- [X] T008 [P] Create `PAGESC/IPageDocumentValidator.cs`:
  - `IPageDocumentValidator.Validate(ReadOnlySpan<byte> utf8Json, DocumentKind kind)`;
  - `enum DocumentKind { Page, Component }`;
  - `sealed record DocumentValidationResult`: on success it carries `SchemaVersion`, `IReadOnlySet<Guid> ReferencedComponentIds` and the parsed `JsonElement`; on failure it carries `Code`, `Rule` and `Pointer`.

  Write the XML docs from contracts/page-schema.md §3.
- [X] T009 [P] Create the six integration events in `PAGESC/Events/`, each a record implementing `IIntegrationEvent` with a static `EventType`, mirroring `src/Modules/Sites/QuickBuilder.Sites.Contracts/Events/SiteDisabled.cs` and its "names are permanent" remark. The payloads are those in the data-model.md events table:
  - `PageCreated.cs`
  - `PageDeleted.cs`
  - `PageAddressesChanged.cs`, with `IReadOnlyList<PageAddressChange> Changes`
  - `HomePageChanged.cs`
  - `ComponentCreated.cs`
  - `ComponentDeleted.cs`

### Domain shells and mapping (P-02, P-08, P-11, data-model.md)

- [X] T010 [P] Create `PAGES/Domain/AggregateRoot.cs`: an internal abstract class with a private `List<IDomainEvent>`, `protected void Raise(IDomainEvent)`, `IReadOnlyList<IDomainEvent> DomainEvents` and `ClearDomainEvents()`. Also create the marker `internal interface IDomainEvent` in `PAGES/Domain/Events/IDomainEvent.cs` (P-14).
- [X] T011 [P] Create the entity shells with their properties, private EF constructors and **no behaviour yet**:
  - `PAGES/Domain/PageTree.cs` (root: `SiteId`, `PageCount`, `UpdatedAtUtc`, `uint Version`, `List<Page> _pages`);
  - `PAGES/Domain/Page.cs` (every column in data-model.md; `ISiteOwned`);
  - `PAGES/Domain/Draft.cs` (abstract: `SiteId`, `SchemaVersion`, `string Document`, `UpdatedAtUtc`, `UpdatedBy`, `uint Version`; `ISiteOwned`);
  - `PAGES/Domain/PageDraft.cs` (`PageId`);
  - `PAGES/Domain/ComponentDraft.cs` (`ComponentId`);
  - `PAGES/Domain/Component.cs` (`ISiteOwned`);
  - `PAGES/Domain/ComponentUsage.cs` (`SiteId`, `PageId`, `ComponentId`; `ISiteOwned`).

  `PageTree` implements `ISiteOwned`. `PageTree`, `Component`, `PageDraft` and `ComponentDraft` derive from `AggregateRoot` (data-model.md "Aggregates"). `Page` is a plain internal entity inside `PageTree`: its setters are private, it is changed only through `PageTree` methods, and it never raises events itself.
- [X] T012 Create `PAGES/Infrastructure/PagesDbContext.cs`, following `src/Modules/Media/QuickBuilder.Media/Infrastructure/MediaDbContext.cs`:
  - schema `pages`, with `CurrentSiteId` and a `HasQueryFilter(e => e.SiteId == CurrentSiteId)` per entity;
  - every column, check constraint and index in data-model.md, including `ux_pages_site_path`, the partial `ux_pages_site_home` (`HasFilter("is_home")`) and `ix_pages_site_parent_position`;
  - `xmin` as `IsRowVersion()` on `page_trees`, `components` and both draft tables;
  - TPC for `Draft`, with `UseTpcMappingStrategy()` and the tables `page_drafts` and `component_drafts`;
  - `citext` for `components.name`, and the alternate keys `(site_id, id)` on `pages` and `components`;
  - the composite FKs on `component_usages` (`ON DELETE CASCADE` to pages, `RESTRICT` to components) and `pages.parent_id` `RESTRICT`;
  - `AddOutbox(Schema)` and `UseSnakeCaseColumns()`;
  - `MarkTreeChanged(PageTree)`, as `SitesDbContext.MarkAggregateChanged`.
- [X] T013 In `PAGES/Infrastructure/PagesDbContext.cs`, add `internal IQueryable<T> ForSite<T>(Guid siteId) where T : class, ISiteOwned`, which returns `Set<T>().AcrossAllSites("Admin operation on a site named explicitly in the route (001 FR-011b).").Where(e => e.SiteId == siteId)`. This is the module's only `AcrossAllSites` call (P-15). Document why in its remarks.
- [X] T014 [P] Create `PAGES/Infrastructure/PagesDbContextFactory.cs` (design time), copying `MediaDbContextFactory.cs`.
- [X] T015 Generate `PAGES/Infrastructure/Migrations/<timestamp>_InitialPages.cs` with `dotnet ef migrations add InitialPages --project src/Modules/Pages/QuickBuilder.Pages --startup-project src/QuickBuilder.Host --context PagesDbContext --output-dir Infrastructure/Migrations`. Check that it creates `citext` with `IF NOT EXISTS`, every table, constraint and index in data-model.md, and the history table in `pages` (P-18).

### Module plumbing

- [X] T016 [P] Create `PAGES/PagesCapabilities.cs`, with `ContentView`, `StructureManage`, `PageDraftsEdit` and `ComponentsManage` and their role grants exactly as in P-13 (all `CapabilityScope.Site`). Add the four rows to the capability table in `specs/002-identity-setup/contracts/authorization.md` §2, and a sentence pointing at 010 research P-13.
- [X] T017 [P] Create `PAGES/PagesErrorResults.cs`, copying `src/Modules/Sites/QuickBuilder.Sites/ErrorResults.cs`, with the status map from the data-model.md codes table:
  - 409 for the conflict codes;
  - 404 for `*_not_found`;
  - 413 for `pages.document_too_large`;
  - 400 otherwise.

  Refusals that carry extra fields (`childCount`, `usageCount`, `rule`, `pointer`) are built by a Pages-internal `PAGES/PagesErrors.cs` factory. It returns an ordinary `Error` whose `Code` stays stable, and stores the extension values in a `ConditionalWeakTable<Error, IReadOnlyDictionary<string, object?>>` owned by `PagesErrorResults`. `ToActionResult` copies them into `ProblemDetails.Extensions`. `Shared.Kernel`'s `Error` is unchanged.
- [X] T018 [P] Create `PAGES/Infrastructure/TreeConcurrencyRetry.cs`, modelled on `src/Modules/Sites/QuickBuilder.Sites/Infrastructure/ConcurrencyRetry.cs`:
  - 3 attempts, clearing the change tracker between them;
  - it also retries a `DbUpdateException` whose inner `PostgresException` is a unique violation on `page_trees_pkey` (the first-page race, P-05);
  - after 3 attempts it returns `pages.out_of_date`.
- [X] T019 [P] Create `PAGES/Infrastructure/PagesOutbox.cs`: `AddFrom(PagesDbContext, AggregateRoot, Guid? actorUserId)` maps each domain event to its integration event, adds it as in `src/Modules/Sites/QuickBuilder.Sites/Infrastructure/SitesOutbox.cs`, and clears the aggregate's events (P-14).
- [X] T020 Create `PAGES/PagesModule.cs` with `AddPagesModule(IServiceCollection, IConfiguration)`, following `src/Modules/Media/QuickBuilder.Media/MediaModule.cs`:
  - the `DbContext` with its history table in `pages`;
  - `PagesDatabaseProvider : IModuleDatabaseProvider` (module name `"Pages"`), with the same `NoSiteContext` pattern;
  - the capabilities;
  - an `EventTypeRegistration<T>` for each of the six events.

  In `HOST/Program.cs`, add `builder.Services.AddPagesModule(builder.Configuration);` and `.AddApplicationPart(typeof(PagesModule).Assembly)`.

### Test harness and guards

- [X] T021 [P] Create `ITESTS/Pages/PagesApi.cs`: typed helpers for every operation in contracts/pages-api.yaml, plus `*OkAsync` variants and `ProblemAsync` (code, `rule`, `pointer`, `childCount`, `usageCount`). Reuse the request-header conventions of `ITESTS/Media/MediaApi.cs`, and add a `SeedTreeAsync(admin, siteId, spec)` helper that builds a tree from an indented outline.
- [X] T022 [P] Create `ITESTS/Isolation/Probes/PagesIsolationProbe.cs`, with `Module => "Pages"` and an initially empty `CoveredEndpoints`, `SeedAsync` and `AdminRequests`, and register it in `ITESTS/Isolation/IsolationProbes.cs`. Each story adds its endpoints to it. Until then, `IsolationCoverageTests.Every_module_with_site_owned_data_has_a_probe` passes.
- [X] T023 [P] Write `ITESTS/Pages/PagesSiteScopingTests.cs`:
  - `ForSite` returns only the named site's rows for every Pages entity;
  - the site's public filter (`CurrentSiteId`) matches nothing on the admin surface;
  - `ForSite` is the only `AcrossAllSites` call in the `QuickBuilder.Pages` assembly, checked with a reflection or IL scan like `SiteScopingTests`.
- [X] T024 [P] Write `ITESTS/Pages/UpgradeFrom009Tests.cs`, following `ITESTS/Sites/UpgradeFrom003Tests.cs`:
  - **Snapshot**: build a 009 database, with every Sites, Identity and Media migration and no `pages` schema, and seed two sites, a grant and a media file.
  - **Start**: start the current app.
  - **Assert**: the `pages` schema and its history table exist; sites, host names, the grant and the media address still work; `SiteFilterCoverageTests` covers the Pages model (FR-035).

**Checkpoint**: the solution builds; the migration applies to an empty database and to a 009 one; the architecture tests, `SiteFilterCoverageTests`, `IsolationCoverageTests` and `PagesSiteScopingTests` pass; every existing test passes. User-story work can begin.

---

## Phase 3: User Story 1 - A site member builds the page structure of a site (Priority: P1) 🎯 MVP

**Goal**: create a home page, top-level pages and child pages with titles and slugs;
compute addresses; enforce the slug grammar, address uniqueness, reserved addresses and
limits; list the tree and read a page. Each new page gets an empty draft.

**Independent Test**: quickstart §1. On a site with no pages, create a home page, three
top-level pages and two children. The tree shows them in order, under the right parents,
with the computed addresses. A duplicate address, an invalid slug and a reserved address
are each refused with a reason, and nothing is saved.

### Tests for User Story 1 ⚠️ write first, confirm they fail

- [X] T025 [P] [US1] Write `UTESTS/Pages/PageTreeCreateTests.cs`, covering `PageTree.CreatePage`:
  - **Placement**: the new page goes last by default; after an anchor; first when the anchor is `null`.
  - **Addresses** follow the P-03 rule, including a child of the home page at `/slug`.
  - **Home page**: created as home only when none exists (`pages.home_page_exists`), and only at the top level (`pages.home_page_must_be_top_level`).
  - **Refusals**: `pages.duplicate_address`, including a top-level `about` against home's child `about`; `pages.reserved_address` for `api`, `admin` and `health`; `pages.too_deep` at level 11; `pages.too_many_pages` at 2,001; `pages.address_too_long`; `pages.parent_not_found`; `pages.invalid_title` and `pages.invalid_slug`.
  - **Bookkeeping**: positions are contiguous afterwards, `PageCount` is updated, and `PageCreated` (plus `HomePageChanged` when home) is raised.
- [X] T026 [P] [US1] Write `ITESTS/Pages/PageTreeApiTests.cs` (create, get tree, get page):
  - **Order**: the flat depth-first order with `depth` and `childCount`;
  - **Draft**: every new page has an empty v1 draft (FR-015);
  - **Refusals**: each refusal code over HTTP, with `request.invalid` listing every field;
  - **Same slug**: `faq` under two different parents is accepted (clarification Q1);
  - **Location**: `Location` on 201;
  - **Outbox**: after `SaveChanges` the outbox holds `Pages.PageCreated`, with no title in its payload.
- [X] T027 [P] [US1] Write `ITESTS/Pages/PageTreeConcurrencyTests.cs`, create case: 10 parallel creates of the same slug under the same parent give exactly one 201, and every other request fails with `pages.duplicate_address`. Parallel first-page creates on an empty site all succeed or fail cleanly, with no 500s (P-05).
- [X] T028 [P] [US1] Write `ITESTS/Pages/ReservedAddressCoverageTests.cs`: every prefix in `RoutePrefixes.ReservedPrefixes` and `RoutePrefixes.PublicReservedPrefixes` (from `HOST/Routing/RoutePrefixes.cs`), with the leading `/` removed, is in `PageAddresses.ReservedFirstSegments` or fails `PageAddresses.IsValidSlug`. Include a fixture case showing the guard fires for a made-up `/preview` prefix (P-04).
- [X] T029 [P] [US1] Start `ITESTS/Pages/PagesAuthorizationTests.cs` with a theory over roles (Administrator, Designer, Editor, no grant):
  - `GET …/pages` and `GET …/pages/{id}`: 200, 200, 200 and 404;
  - `POST …/pages`: 201, 201, 403 and 404;
  - `permissions` in the tree response matches the role (clarification Q3).
- [X] T030 [P] [US1] Write `ADMINTESTS/slugify.spec.ts`:
  - `Dịch vụ & Giá` → `dich-vu-gia`;
  - `Đà Nẵng` → `da-nang`;
  - `Straße` → `strasse`;
  - `Ærø Ø` → `aero-o`;
  - `  --Hello,  World!--` → `hello-world`;
  - an emoji-only title gives no suggestion;
  - a 150-character title is cut to 100 or fewer at a hyphen (P-04).
- [X] T031 [P] [US1] Write `ADMINTESTS/pageTreeView.spec.ts`, US1 part:
  - **States**: loading, error and empty;
  - **Rendering**: indented tree rows with addresses and the home badge;
  - **Adding**: the Add page and Add child dialogs suggest a slug until the slug field is edited, show a refusal's message by error code, and keep keyboard focus in the dialog;
  - **Editor**: read-only when `permissions.canManageStructure` is false;
  - **Accessibility**: axe is clean.

### Implementation for User Story 1

- [X] T032 [P] [US1] Create `PAGES/Domain/Slug.cs` (value checks built on `PageAddresses.IsValidSlug`) and `PAGES/Domain/PageAddress.cs` (the address rule, the reserved check and the length check). Test them in `UTESTS/Pages/SlugTests.cs` and `UTESTS/Pages/PageAddressTests.cs`.
- [X] T033 [US1] Implement `PageTree.Start(siteId, clock)` and `PageTree.CreatePage(title, slug, parentId?, afterPageId?, isHome, actor, clock) → Result<Page>` in `PAGES/Domain/PageTree.cs`, and the matching `Page` factory in `PAGES/Domain/Page.cs`. Raise `PageCreated` and `HomePageChanged` as domain events in `PAGES/Domain/Events/`, so that T025 passes.
- [X] T034 [US1] Implement `PageDraft.CreateEmpty(siteId, pageId, actor, clock)` in `PAGES/Domain/PageDraft.cs`, holding the empty envelope from P-10 as a constant in `PAGES/Domain/Draft.cs`.
- [X] T035 [US1] Create `PAGES/Features/CreatePage/CreatePage.cs`: `POST api/v1/pages/sites/{siteId:guid}/pages`, with `StructureManage`.
  - The validator checks the title and the slug grammar, reporting every failure.
  - The handler, inside `TreeConcurrencyRetry`: load or start the tree for the site; create the page; add its empty draft; `MarkTreeChanged`; `PagesOutbox.AddFrom`; save.
  - Return 201 with `PageDetail` and `Location`.
  - Register it in `PAGES/PagesModule.cs`.
- [X] T036 [P] [US1] Create `PAGES/Features/GetPageTree/GetPageTree.cs`: `GET …/pages`, with `ContentView`. It runs one `AsNoTracking` projection ordered by `(parent_id, position)`, assembled depth-first in memory, with `depth` and `childCount`, and adds `permissions` from `IAccessEvaluator` (P-16, P-20). Register it.
- [X] T037 [P] [US1] Create `PAGES/Features/GetPage/GetPage.cs`: `GET …/pages/{pageId:guid}`, with `ContentView`. It returns `PageDetail`, including the `DraftSummary`, with `updatedBy.displayName` from `IUserDirectory` in Identity.Contracts, and returns `pages.not_found` for another site's id. Register it.
- [X] T038 [US1] Extend `ITESTS/Isolation/Probes/PagesIsolationProbe.cs`:
  - `SeedAsync` creates a home page and a child;
  - `CoveredEndpoints` gains `GET …/pages/{pageId:guid}`;
  - `AdminRequests` gains that endpoint with the other site's ids (Addressed), and `GET …/pages` (Listing).
- [X] T039 [P] [US1] Create `ADMIN/api/pages.ts`: types for `PageTree`, `PageNode`, `PageDetail`, `Permissions`, `DraftSummary` and `CreatePageRequest`, from contracts/pages-api.yaml, plus `getTree`, `getPage` and `createPage`, using `ADMIN/api/request.ts`.
- [X] T040 [P] [US1] Create `ADMIN/features/pages/slugify.ts` (P-04), so that T030 passes, and `ADMIN/features/pages/pageErrors.ts`, which maps every `pages.*` code to an i18n key.
- [X] T041 [US1] Create `ADMIN/features/pages/PageTreeView.vue`, `ADMIN/features/pages/PageRow.vue` and `ADMIN/features/pages/PageFormDialog.vue`:
  - an ARIA `tree` from the flat list;
  - **Add page** and **Add child page** dialogs, with the title, the suggested slug and a *Home page* checkbox shown only when the site has none.

  Add the route `/sites/:siteId/pages` (name `site-pages`) in `ADMIN/app/router.ts`, and a **Pages** link in `ADMIN/features/sites/SiteDetailView.vue`, so that T031 passes.
- [X] T042 [US1] Add every US1 string to `ADMIN/i18n/en.json`: screen titles, dialog labels, the home badge, the empty state, and the messages for each US1 error code.

**Checkpoint**: US1 is demoable. A site gets a page structure with correct addresses, and the rules hold on the server.

---

## Phase 4: User Story 2 - A site member renames, reorders and moves pages (Priority: P1)

**Goal**: change titles and slugs, reorder siblings, move subtrees and choose the home
page. Every affected address is recomputed in one operation. Stale changes are refused
without false conflicts.

**Independent Test**: quickstart §2. In a three-level tree:
- move a page that has children, reorder its siblings, and reslug a page that has
  descendants: every address is recomputed at once;
- a move that would clash with an existing address, or that goes into the page's own
  descendant, is refused and changes nothing.

### Tests for User Story 2 ⚠️ write first, confirm they fail

- [X] T043 [P] [US2] Write `UTESTS/Pages/PageTreeChangeTests.cs`, covering:
  - **`Rename`**: a title-only change keeps the slug and address (US2 #6); a reslug recomputes the whole subtree; a clash names the first conflicting path.
  - **`Move`**: to a new parent, after an anchor, or first; reordering within the same parent; a no-op move changes nothing and raises nothing; `pages.move_into_descendant`; `pages.too_deep` for the moved subtree's deepest page; clash and reserved checks across the subtree; moving the home page under a parent is refused.
  - **`MakeHome`**: the former home's descendants gain its slug, and the new home's lose theirs; a clash refuses the whole change; a child page is refused; the current home is a no-op.
  - **Versions**: a stale page `version` gives `pages.out_of_date`, and so does an anchor that is no longer a sibling.
  - **Versions of other pages**: only pages whose own fields changed get a new version.
  - **Events**: `PageAddressesChanged` carries every (id, old, new) in one event, and a reorder publishes none.
- [X] T044 [P] [US2] Write `UTESTS/Pages/PageTreeInvariantTests.cs`: 200 seeded random sequences of 50 operations each (create, rename, move and make-home, with random valid and invalid arguments). After every step, assert every invariant in data-model.md "Invariants of PageTree", including that every stored `path` equals a fresh recomputation from the slugs (SC-002).
- [X] T045 [P] [US2] Extend `ITESTS/Pages/PageTreeConcurrencyTests.cs`:
  - **Crossed moves**: A under B, raced against B under A, gives exactly one success and no cycle.
  - **Reslug race**: raced against a create that would take the resulting address, exactly one succeeds.
  - **No false conflict**: renaming Contact succeeds while Services is being reordered concurrently (P-05).
- [X] T046 [P] [US2] Write `ITESTS/Pages/PageStructureApiTests.cs`, for `PUT …/pages/{id}`, `POST …/move` and `POST …/make-home`:
  - each refusal code, and each success's response shape (the move and make-home responses return the whole tree);
  - **Nothing partial**: after any refusal, `GET …/pages` is identical to before;
  - **Outbox**: holds one `Pages.PageAddressesChanged` for a 3-page subtree move, and `Pages.HomePageChanged` for make-home.
- [X] T047 [P] [US2] Extend `ITESTS/Pages/PagesAuthorizationTests.cs` with rename, move and make-home. For each, Administrator and Designer succeed, Editor gets 403 and no grant gets 404.
- [X] T048 [P] [US2] Extend `ADMINTESTS/pageTreeView.spec.ts`:
  - **Rename dialog**: the slug suggestion stops once the slug has been edited.
  - **Move up / Move down** by keyboard. The buttons are disabled at the ends.
  - **Move to… dialog**: the target list excludes the page and its descendants; choose a position; keyboard only.
  - **Make home**.
  - **Out of date**: `pages.out_of_date` shows a reload prompt.
  - **Accessibility**: axe is clean.

### Implementation for User Story 2

- [X] T049 [US2] Implement `PageTree.Rename(pageId, title, slug, version, actor, clock)`, `PageTree.Move(pageId, parentId?, afterPageId?, version, actor, clock)` and `PageTree.MakeHome(pageId, version, actor, clock)` in `PAGES/Domain/PageTree.cs`:
  - one private `RecomputeSubtree(root)` that returns the (id, old, new) changes and validates every new address before anything is applied;
  - renumbering of the affected sibling groups (P-06);
  - `PageAddressesChanged` and `HomePageChanged` domain events.

  T043 and T044 must pass.
- [X] T050 [US2] Create `PAGES/Features/UpdatePage/UpdatePage.cs` (`PUT …/pages/{pageId:guid}`), `PAGES/Features/MovePage/MovePage.cs` (`POST …/pages/{pageId:guid}/move`) and `PAGES/Features/MakeHomePage/MakeHomePage.cs` (`POST …/pages/{pageId:guid}/make-home`):
  - all with `StructureManage`;
  - each handler runs load-tree, call, `MarkTreeChanged`, `PagesOutbox.AddFrom` and save inside `TreeConcurrencyRetry`;
  - move and make-home return the tree through the `GetPageTree` projection.

  Register all three.
- [X] T051 [US2] Extend `ITESTS/Isolation/Probes/PagesIsolationProbe.cs` with the three endpoints, sent with the other site's page ids (Addressed): `PUT …/pages/{pageId:guid}`, `POST …/pages/{pageId:guid}/move` and `POST …/pages/{pageId:guid}/make-home`. In `StillIntactAsync`, check that the other site's addresses are unchanged.
- [X] T052 [P] [US2] Extend `ADMIN/api/pages.ts` with `updatePage`, `movePage` and `makeHome`.
- [X] T053 [US2] Create `ADMIN/features/pages/MoveDialog.vue`, and extend `PageRow.vue`, `PageFormDialog.vue` (rename mode) and `PageTreeView.vue`:
  - the **Rename**, **Move up**, **Move down**, **Move to…** and **Make home** actions, gated by `permissions.canManageStructure`;
  - after each action, the tree is replaced by the response, or reloaded, and focus returns to the moved row.

  T048 must pass.
- [X] T054 [US2] Add every US2 string to `ADMIN/i18n/en.json`.

**Checkpoint**: US1 and US2 together give a fully manageable structure. SC-002 holds under concurrency.

---

## Phase 5: User Story 3 - A site member stores and retrieves a page's draft content (Priority: P1)

**Goal**: save and read a page's draft document, versioned and validated against the page
schema contract. The contract document lives in `docs/contracts/`, and a build check keeps
it in step with the schema.

**Independent Test**: quickstart §3:
- a valid draft round-trips, equal as a JSON value, with its schema version;
- every invalid golden example is refused with its `rule` and `pointer`, and the previous
  draft is kept;
- a stale `version` gives `pages.out_of_date`;
- editing the contract document so that it disagrees with the schema fails the build.

### Tests for User Story 3 ⚠️ write first, confirm they fail

- [X] T055 [P] [US3] Create the golden examples listed in [contracts/page-schema.md §8](./contracts/page-schema.md#8-examples-golden-files) under `CONTRACTS/page-schema/examples/valid/` and `CONTRACTS/page-schema/examples/invalid/`. Each invalid file is named `<rule>--<case>.json`, and `unsupported--version-2.json` expects `pages.unsupported_schema_version`. `too_deep` and `too_many_nodes` are generated in code, not stored.
- [X] T056 [P] [US3] Write `UTESTS/Pages/SchemaSubsetValidatorTests.cs`: a positive and a negative case per supported keyword from P-09 (`type`, `properties`, `required`, `additionalProperties: false`, `const`, `enum`, `pattern`, `minLength`, `maxLength`, `minimum`, `maximum`, `items`, `maxItems`, `format: uuid`, local `$ref`, and discriminated `oneOf`). The cases follow the JSON Schema 2020-12 specification. The reported pointer is the deepest failing location.
- [X] T057 [P] [US3] Write `UTESTS/Pages/PageDocumentValidatorTests.cs`:
  - every file in `CONTRACTS/page-schema/examples/`, as golden files: valid ones pass, and invalid ones fail with the `rule` in the name and the expected `pointer`;
  - generated documents at depth 33 and with 5,001 nodes;
  - a body of 1 MiB + 1 byte;
  - duplicate property names;
  - `component-ref` inside a `DocumentKind.Component` document gives `component_ref_not_allowed`;
  - the check order in contracts/page-schema.md §3 (a document that is both too deep and schema-invalid reports `too_deep`);
  - referenced component ids are collected and deduplicated.
- [X] T058 [P] [US3] Write `ITESTS/Pages/PageSchemaContractTests.cs` (FR-022, SC-007). It parses `CONTRACTS/page-schema.md` and asserts:
  - the node-type table in §2 equals the schema's node `$defs`, meaning those with a `type` const;
  - the limits table in §4 equals the `PageSchema` and `PageAddresses` constants;
  - the reserved-segment table in §5 equals `PageAddresses.ReservedFirstSegments`;
  - the embedded resource's bytes equal `CONTRACTS/page-schema/v1.schema.json`.

  Each failure message names what differs.
- [X] T059 [P] [US3] Write `ITESTS/Pages/PageSchemaSubsetTests.cs`: walk `v1.schema.json` and fail on any keyword outside the P-09 subset, naming it and its location. Include a fixture schema with `patternProperties` to prove the guard fires.
- [X] T060 [P] [US3] Write `ITESTS/Pages/DraftApiTests.cs`, for `GET` and `PUT …/pages/{id}/draft`:
  - **Round trip**: `valid/empty.json` comes back equal as a JSON value (P-08), with `schemaVersion` 1, a new `version`, `updatedAtUtc` and `updatedBy`;
  - **Refusals**: every invalid example gives 400 with its `rule` and `pointer`, and a `GET` afterwards is unchanged;
  - **Out of date**: a stale `version` gives 409 `pages.out_of_date`;
  - **Size**: a 1.2 MiB body gives 413 `pages.document_too_large`;
  - **Unknown component**: a `component-ref` to a random UUID gives 400 `pages.unknown_component`;
  - **Nothing public**: the public surface response for the page's path is identical before and after (US3 #6);
  - **No event**: no outbox row is written.
- [X] T061 [P] [US3] Extend `ITESTS/Pages/PagesAuthorizationTests.cs`:
  - `GET …/draft`: 200 for every role, 404 without a grant;
  - `PUT …/draft`: 200 for Administrator, Designer **and Editor** (clarification Q3), 404 without a grant.

### Implementation for User Story 3

- [X] T062 [US3] Implement `PAGES/Infrastructure/Schema/SchemaSubsetValidator.cs`: it loads a schema from `PageSchema.OpenSchema(v)` once, is cached as a singleton, and interprets the P-09 keyword subset against a `JsonElement`, returning the first failure as a JSON Pointer. It throws at load time on an unsupported keyword. T056 must pass.
- [X] T063 [US3] Implement `PAGES/Infrastructure/Schema/PageDocumentValidator.cs` (`IPageDocumentValidator`), in this order:
  1. size;
  2. `JsonDocument.Parse` with `AllowDuplicateProperties = false` and `MaxDepth` large enough for 32 node levels;
  3. `schemaVersion`;
  4. the structural walk (depth, count, unique ids);
  5. `SchemaSubsetValidator`;
  6. the component rules and the collection of referenced ids.

  Map each failure to its `rule` from contracts/pages-api.yaml `DocumentProblem`. Register it as a singleton in `PAGES/PagesModule.cs`. T057 must pass.
- [X] T064 [US3] Implement `Draft.Replace(document, schemaVersion, actor, clock)` in `PAGES/Domain/Draft.cs`.
- [X] T065 [US3] Create `PAGES/Features/GetPageDraft/GetPageDraft.cs` (`GET …/pages/{pageId:guid}/draft`, with `ContentView`) and `PAGES/Features/SavePageDraft/SavePageDraft.cs` (`PUT …/pages/{pageId:guid}/draft`, with `PageDraftsEdit`):
  - `[RequestSizeLimit]` of 1.1 MiB, with overflow mapped to `pages.document_too_large`;
  - the handler validates as `DocumentKind.Page`, then checks every referenced id against `ForSite<Component>(siteId)`, returning `pages.unknown_component` for any id not found;
  - it compares the caller's `version`, replaces the draft, and deletes and re-inserts the page's `component_usages` rows in one `SaveChanges`;
  - it maps `DbUpdateConcurrencyException` to `pages.out_of_date`, and an FK violation on `component_usages` to `pages.unknown_component`;
  - it publishes nothing.

  Register both.
- [X] T066 [US3] Copy [contracts/page-schema.md](./contracts/page-schema.md) to `CONTRACTS/page-schema.md` as the authoritative version:
  - drop the "planning copy" preamble;
  - keep §7 as a pointer to `page-schema/v1.schema.json`, not a second copy.

  Update `CONTRACTS/README.md` to list the page schema. T058 must pass.
- [X] T067 [US3] Extend `ITESTS/Isolation/Probes/PagesIsolationProbe.cs` with `GET` and `PUT …/pages/{pageId:guid}/draft`, sent with the other site's page ids (Addressed). In `StillIntactAsync`, check that the other site's draft `version` is unchanged.
- [X] T068 [P] [US3] Extend `ADMIN/api/pages.ts` with `getPageDraft`. In `ADMIN/features/pages/PageTreeView.vue`, add a details panel for the selected page that shows the draft's schema version and when and by whom it was last saved. There is no editing here (P-17). Add the strings to `ADMIN/i18n/en.json`.

**Checkpoint**: every P1 story is done. Drafts are stored behind the versioned contract, which now lives in `docs/contracts/`.

---

## Phase 6: User Story 4 - A site member defines shared components used by many pages (Priority: P2)

**Goal**: create, rename and delete shared components, each with its own draft. Page
drafts reference components of the same site, a component lists the pages that use it,
and a component in use can't be deleted, even under races.

**Independent Test**: quickstart §4:
- create Header and reference it from two page drafts; it then lists both pages;
- deleting it is refused with `usageCount: 2`, and succeeds once the references are
  removed;
- a reference to another site's component is refused as unknown.

### Tests for User Story 4 ⚠️ write first, confirm they fail

- [X] T069 [P] [US4] Write `UTESTS/Pages/ComponentTests.cs`:
  - **Name**: trimmed, 1–100 characters (`pages.invalid_component_name`);
  - **Rename**: raises nothing;
  - **Events**: `ComponentCreated` and `ComponentDeleted` domain events.
- [X] T070 [P] [US4] Write `ITESTS/Pages/ComponentApiTests.cs`, for every component endpoint:
  - **Create**: 201 with an empty draft; a duplicate name ignoring case gives 409 `pages.duplicate_component_name`.
  - **Rename**: to the same name in a different case is allowed; a stale `version` gives 409.
  - **Usages**: after two page drafts reference Header, `usageCount` is 2 and `/usages` lists both pages by address; after one reference is removed, it is 1.
  - **Delete**: while used, 409 `pages.component_in_use` with `usageCount`; once unused, 204.
  - **Other sites**: a page draft referencing site B's component gives 400 `pages.unknown_component`, identical to a random id.
  - **Component draft**: containing `component-ref`, 400 with rule `component_ref_not_allowed`; the component draft round-trips.
  - **Outbox**: holds `Pages.ComponentCreated` and `Pages.ComponentDeleted`, with no name in the payload.
- [X] T071 [P] [US4] Write `ITESTS/Pages/ComponentUsageRaceTests.cs`: race `DELETE` of a component against `PUT` of a page draft that adds a reference to it, 50 times. Each time exactly one of these holds: the delete succeeded and the save failed with `pages.unknown_component`, or the save succeeded and the delete failed with `pages.component_in_use`. There is never a dangling usage row (P-11).
- [X] T072 [P] [US4] Extend `ITESTS/Pages/PagesAuthorizationTests.cs`:
  - **Views** (list, get, usages, get draft): every role gets 200.
  - **Changes** (create, rename, delete, save draft): Administrator and Designer succeed, and Editor gets 403.
  - **No grant**: 404 everywhere.
- [X] T073 [P] [US4] Write `ADMINTESTS/componentsView.spec.ts`:
  - **States**: loading, error and empty;
  - **Actions**: create, rename, and the duplicate-name message;
  - **Delete**: refused with the usage count shown, and the usages panel lists pages with links to the page tree;
  - **Editor**: read-only;
  - **Accessibility**: axe is clean, and the journey works keyboard-only.

### Implementation for User Story 4

- [X] T074 [US4] Implement `Component.Create(siteId, name, actor, clock)`, `Rename(name, version, …)` and `MarkDeleted(actor)` in `PAGES/Domain/Component.cs`, and `ComponentDraft.CreateEmpty` in `PAGES/Domain/ComponentDraft.cs`. T069 must pass.
- [X] T075 [US4] Create the slices:
  - `PAGES/Features/ListComponents/ListComponents.cs` (`GET …/components`, `ContentView`; includes `usageCount` through a grouped count and `permissions`);
  - `PAGES/Features/CreateComponent/CreateComponent.cs` (`POST`, `ComponentsManage`; maps a unique violation on `(site_id, name)` to `pages.duplicate_component_name`);
  - `PAGES/Features/GetComponent/GetComponent.cs` (`GET …/components/{componentId:guid}`, `ContentView`);
  - `PAGES/Features/RenameComponent/RenameComponent.cs` (`PUT`, `ComponentsManage`).

  Register all four.
- [X] T076 [US4] Create `PAGES/Features/DeleteComponent/DeleteComponent.cs` (`DELETE …/components/{componentId:guid}?version=`, `ComponentsManage`). It counts usages first and returns `pages.component_in_use` with `usageCount`. It then deletes the component and its draft, and maps an FK `RESTRICT` violation to `pages.component_in_use`, so that a lost race answers the same way. Register it.
- [X] T077 [US4] Create `PAGES/Features/ListComponentUsages/ListComponentUsages.cs` (`GET …/components/{componentId:guid}/usages`, `ContentView`, ordered by path), `PAGES/Features/GetComponentDraft/GetComponentDraft.cs` (`ContentView`) and `PAGES/Features/SaveComponentDraft/SaveComponentDraft.cs` (`ComponentsManage`; validates as `DocumentKind.Component`; the same size, version and error mapping as T065). Register all three.
- [X] T078 [US4] Extend `ITESTS/Isolation/Probes/PagesIsolationProbe.cs`:
  - `SeedAsync` creates a component and references it from a page draft;
  - `CoveredEndpoints` and `AdminRequests` gain every `{componentId:guid}` endpoint (Addressed) and `GET …/components` (Listing).
- [X] T079 [P] [US4] Extend `ADMIN/api/pages.ts` with the component types and calls.
- [X] T080 [US4] Create `ADMIN/features/pages/ComponentsView.vue` and `ADMIN/features/pages/ComponentUsagesPanel.vue`. Add the route `/sites/:siteId/components` in `ADMIN/app/router.ts` and a **Components** link in `ADMIN/features/sites/SiteDetailView.vue`. T073 must pass.
- [X] T081 [US4] Add every US4 string to `ADMIN/i18n/en.json`.

**Checkpoint**: shared components work end to end, and the database enforces their usage rules.

---

## Phase 7: User Story 5 - A site member deletes pages (Priority: P2)

**Goal**: permanently delete a page without children, after confirmation. A page with
children is refused with its child count (clarification Q2), and so is the home page
while other pages exist.

**Independent Test**: quickstart §5:
- deleting a leaf removes it and its draft, and its address becomes reusable;
- deleting a parent is refused with `childCount`, and deleting the home page while other
  pages exist is refused;
- an Editor is forbidden from every structural change.

### Tests for User Story 5 ⚠️ write first, confirm they fail

- [X] T082 [P] [US5] Write `UTESTS/Pages/PageTreeDeleteTests.cs`:
  - **Leaf**: removed, the siblings renumbered, `PageCount` decremented, `PageDeleted` raised;
  - **Parent**: `pages.has_children` with `childCount`;
  - **Home page**: `pages.home_page` while others exist, but deletable when it is the only page;
  - **Out of date**: a stale `version`.
- [X] T083 [P] [US5] Write `ITESTS/Pages/DeletePageTests.cs`:
  - **Leaf**: `DELETE …/pages/{id}?version=` gives 204; the page, its draft and its `component_usages` rows are gone; the address can be created again; the outbox holds `Pages.PageDeleted`.
  - **Refusals**: 409 `pages.has_children` with `childCount: 2`, and 409 `pages.home_page`.
  - **Components**: a component used only by the deleted page becomes deletable.
- [X] T084 [P] [US5] Extend `ITESTS/Pages/PagesAuthorizationTests.cs` with delete (Administrator and Designer get 204, Editor gets 403, no grant gets 404). Assert the complete Editor matrix from quickstart §5 in one test (US5 #4 and #5).
- [X] T085 [P] [US5] Extend `ADMINTESTS/pageTreeView.spec.ts`:
  - **Delete**: confirmation dialog, and focus moves to the previous row afterwards;
  - **Refusals**: `pages.has_children` shows the child count, and `pages.home_page` shows its message;
  - **Accessibility**: axe is clean.

### Implementation for User Story 5

- [X] T086 [US5] Implement `PageTree.Delete(pageId, version, actor, clock)` in `PAGES/Domain/PageTree.cs`, raising `PageDeleted`. T082 must pass.
- [X] T087 [US5] Create `PAGES/Features/DeletePage/DeletePage.cs` (`DELETE …/pages/{pageId:guid}?version=`, `StructureManage`). It runs inside `TreeConcurrencyRetry`, and removes the page and its draft, whose usages cascade. Register it.
- [X] T088 [US5] Extend `ITESTS/Isolation/Probes/PagesIsolationProbe.cs` with `DELETE …/pages/{pageId:guid}` (Addressed).
- [X] T089 [US5] Create `ADMIN/features/pages/DeletePageDialog.vue`, add `deletePage` to `ADMIN/api/pages.ts`, add the **Delete** action to `PageRow.vue`, and add the strings to `ADMIN/i18n/en.json`. T085 must pass.

**Checkpoint**: every user story is complete. `SiteIsolationSuite` passes for Pages, with every page and component endpoint covered.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: logging, smoke and performance, CI, documentation, and the quickstart run.

- [X] T090 [P] Extend `ITESTS/Observability/LoggingTests.cs` with a Pages case: create, rename, draft saves and component operations with distinctive titles, slugs, names and document text. None of them appears in the captured logs, and ids do (FR-037).
- [X] T091 [P] Extend `STESTS/ReferenceDeploymentSmokeTests.cs`: after the site steps, create a page and a child page, save `valid/empty.json` as the child's draft, and read it back, against the built image (FR-036). Update the class remarks: E-3 is now narrowed to publish and view.
- [X] T092 [P] Create `STESTS/PageTreePerformanceTests.cs`, modelled on `STESTS/SiteResolutionPerformanceTests.cs`. Seed 2,000 pages, one subtree having 200 descendants. Time 20 runs each of `GET …/pages` and a move of that subtree back and forth, and assert p95 under 2 s for both (SC-004, SC-005).
- [X] T093 [P] Add a 2,000-row render check to `ADMINTESTS/pageTreeView.spec.ts`: the view mounts 2,000 rows from a stub response, and every row is present. Mounting must complete in under 1 s in jsdom, as a guard against accidental quadratic rendering. The 2 s screen budget itself is timed in T097 (SC-005).
- [X] T094 [P] Add `QuickBuilder.Pages` to the per-package list of the coverage gate in `.github/workflows/ci.yml`, with a comment citing P-19.
- [X] T095 [P] Update `docs/operations.md`: a **Pages** section on reserved addresses (`/api`, `/admin`, `/health`), the limits, the fact that nothing is public until #12, and the upgrade notes from plan.md.
- [X] T096 [P] Update `docs/contributing.md`:
  - **Adding a block type**: a `$defs` entry plus a `oneOf` branch in `docs/contracts/page-schema/v1.schema.json`, the node-type table row in `docs/contracts/page-schema.md`, and golden examples; a breaking change follows §6.
  - **Adding a module with site-owned data**: cite `PagesIsolationProbe` as the second worked example.
- [ ] T097 Run [quickstart.md](./quickstart.md) §1–§7 against `docker compose up`, including the timed SC-001 build, the time for the Pages screen to show a 2,000-page tree seeded with `PageTreePerformanceTests`' seeding helper (SC-005, pass under 2 s), and the keyboard-only journeys. Record the results, timings and any deviations in `specs/010-page-tree-components/quickstart-results.md`, following `specs/009-sites-hostname-resolution/quickstart-results.md`.
- [ ] T098 Run `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` and, in `src/admin`, `npm run type-check && npm run lint && npm run test`. Fix everything, then open the PR from `010-page-tree-components`. It must close #10, link the spec and plan, and list the operator impact from plan.md.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 comes before T003 and T004.
- **Foundational (Phase 2)**: depends on Setup and blocks every story. Within it:
  - T005–T011 in parallel;
  - then T012 → T013 → T015;
  - T016–T019 in parallel;
  - then T020, which needs T012 and T016–T019;
  - T021–T024 in parallel after T020.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on US1. It extends `PageTree` (T033) and the views (T041).
- **US3 (Phase 5)**: depends on Foundational and on US1 (a page must exist to hold a draft, T035). It is independent of US2.
- **US4 (Phase 6)**: depends on US3, because usage comes from page draft saves (T065) and component drafts reuse the validator (T063).
- **US5 (Phase 7)**: depends on US1. T083's usage-cascade check needs US3's draft save. It is otherwise independent of US2 and US4.
- **Polish (Phase 8)**: depends on every story it exercises.

### User Story Dependencies

```text
Setup ─▶ Foundational ─▶ US1 ─┬─▶ US2 ──────────────┐
                              ├─▶ US3 ─▶ US4 ───────┤
                              └─▶ US5 (T083 uses US3)┴─▶ Polish
```

### Within Each User Story

- Tests are written first and must fail.
- The order is domain, then slice, then isolation probe, then admin API client, then
  views, then strings.
- A slice's DI registration goes in `PAGES/PagesModule.cs` in the same task as the slice.
- No handler calls `AcrossAllSites` directly. Only `PagesDbContext.ForSite` does, and
  `PagesSiteScopingTests` (T023) enforces it.
- Every new endpoint that addresses a page or component is added to
  `PagesIsolationProbe` in the same story, or `IsolationCoverageTests` fails.

### Parallel Opportunities

- **Setup**: T003 and T004.
- **Foundational**:
  - T005–T011 in parallel;
  - T014 and T016–T019 in parallel;
  - T021–T024 in parallel.
- **US1**: tests T025–T031 in parallel; then T032, T036, T037, T039 and T040 alongside T033–T035.
- **US2**: T043–T048 in parallel; then T052 alongside T049–T050.
- **US3**: T055–T061 in parallel; then T068 alongside T062–T065.
- **US4**: T069–T073 in parallel; then T079 alongside T074–T077.
- **US5**: T082–T085 in parallel.
- **Across stories**: after US1, US2 and US3 can proceed in parallel. US2 touches
  `PageTree` and the structure slices; US3 touches the validator, drafts and
  `docs/contracts`. US5 can run alongside both.
- **Polish**: T090–T096 in parallel.

---

## Parallel Example: User Story 3

```text
# Tests and golden files first, all at once:
T055 examples/*.json            T056 SchemaSubsetValidatorTests.cs   T057 PageDocumentValidatorTests.cs
T058 PageSchemaContractTests.cs T059 PageSchemaSubsetTests.cs        T060 DraftApiTests.cs
T061 PagesAuthorizationTests.cs (draft rows)

# Then: T062 SchemaSubsetValidator → T063 PageDocumentValidator → T064 Draft.Replace → T065 slices
# Alongside: T066 docs/contracts/page-schema.md, T068 admin details panel
```

## Parallel Example: User Story 1

```text
# Tests together:
T025 PageTreeCreateTests.cs   T026 PageTreeApiTests.cs   T027 PageTreeConcurrencyTests.cs
T028 ReservedAddressCoverageTests.cs   T029 PagesAuthorizationTests.cs
T030 slugify.spec.ts          T031 pageTreeView.spec.ts

# Then the domain (T032 → T033 → T034) and slices (T035, with T036 and T037 in parallel),
# alongside the admin client and slugify (T039, T040).
```

---

## Implementation Strategy

### MVP first (US1 + US2 + US3)

1. Phases 1–2. Stop at the Foundational checkpoint, with the upgrade test green.
2. US1, the structure. It's demoable on its own.
3. US2, reorganising. SC-002 then holds under concurrency.
4. US3, drafts and the contract in `docs/contracts/`. This is the hand-off point the
   editor (#11) needs.
5. **Validate**: quickstart §1–§3. Every P1 promise holds. This is shippable, because the
   editor could start from here.

### Incremental delivery

- US4 adds shared components on top of drafts.
- US5 adds deletion. It is small, and can land any time after US1.
- Polish adds the smoke and performance tests, CI, the docs and the quickstart record.

### Parallel team strategy

After US1:
- **Developer A**: US2, then US5 (the `PageTree` aggregate and the structure UI).
- **Developer B**: US3, then US4 (the validator, drafts, components and `docs/contracts`).
- They meet at Polish.

---

## Notes

- `[P]` means different files and no dependency on an incomplete task.
- **Confirm each story's tests fail before implementing it.** Commit after each task, or
  each logical group, with the issue reference (#10).
- **Never change `docs/contracts/page-schema/v1.schema.json` without the contract
  document** in the same commit. `PageSchemaContractTests` fails otherwise. A breaking
  change needs v2 and a migration (contracts/page-schema.md §6).
- **Only the `PageTree` aggregate writes `path`.** Never compute or patch addresses in a
  handler or in SQL (P-03).
- **Keep structural changes and draft saves in separate transactions and on separate
  tokens.** Draft saves must never mark the tree changed (P-08).
- **No titles, slugs, names or document content in log messages or event payloads.**
  Only ids and paths (FR-037).

## Implementation notes

Where the delivered code differs from a task's wording, and why:

- **T011, drafts**: `PageDraft` and `ComponentDraft` share an abstract `Draft` base that
  is deliberately *not* in the EF model; each maps to its own table on its own. This
  replaces the EF TPC mapping in research P-08, because TPC needs one key property
  shared by both types, while the two tables are keyed by different owners (`page_id`,
  `component_id`) with different foreign keys. Tables, columns and behaviour are as
  data-model.md describes.
- **T032, `Slug.cs` and `PageAddress.cs`**: not created. The slug rule lives in
  `PageAddresses.IsValidSlug` (contracts), and the address rule in `PageTree.Plan`, the
  only place addresses are computed (P-03). `PageTreeCreateTests` covers both.
- **T055, golden examples**: `unsupported--version-2.json` became
  `unsupported_schema_version--version-2.json`, so every file name starts with its rule.
  Two examples were added (`malformed_json--truncated`, `schema_violation--bad-node-id`).
  Each example's expected pointer is in `invalid/expected.json`, which the editor (#11)
  can reuse. The contract document lists all of them.
- **T041, T053, the tree screen**: a table in site order, where selecting a row opens one
  keyboard-operable toolbar, instead of an ARIA `tree` with actions on every row. At 2,000
  pages, per-row actions would put about 14,000 buttons in the page (SC-005).
  `PageRow.vue` and `DeletePageDialog.vue` were not needed: rows are part of the table,
  and deletion reuses the sites `ConfirmDialog`.
- **T093**: the jsdom render check allows 5 s, not 1 s. jsdom is far slower than a
  browser; the 2 s screen budget itself is timed in a real browser in T097.
- **Delete logging** (FR-037): deleting a page or a component writes one information
  event naming ids only, like Media's delete. The other operations log nothing: their
  outbox events already record them.
- **Isolation suite**: `ProbeRequest` gained an optional `Body`, so endpoints that take a
  body are tested for another site's ids rather than refused for a missing body.
- **Draft size**: over-limit bodies are refused by a resource filter,
  `DraftSizeLimitAttribute`, modelled on Media's upload limit, as well as by the validator.
  The test server does not enforce request body limits, so the validator's own 1 MiB
  check is what the integration tests exercise.
