---

description: "Task list for 024-navigation"
---

# Tasks: Navigation and Menus

**Input**: Design documents from `/specs/024-navigation/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - golden files for contracts and for the rendering of every block type;
  - 80 % coverage on critical paths (publish, render).
- The spec measures behaviour through automated checks: SC-002 … SC-007.
- Within each story, test tasks come first and must fail before implementation starts.

**Changed after analysis (2026-10-14)**: T024, T030, T032, T034, T036, T046, T048, T059, T063 and
T068 were extended or clarified (and T019, T032, T034, T037, T039 and T041 on the second
analysis), and the US2 dependency on T026 is recorded. No task was added
or renumbered.

**Organization**: tasks are grouped by user story (US1–US5 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CMSC` = `src/Modules/Cms/QuickBuilder.Cms.Contracts`
- `RENDER` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `EDITOR` = `src/admin/src/editor`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`

**Conventions**:
- **Backend slices** live under `PAGES/Features/<Slice>/<Slice>.cs`, modelled on the component
  slices (`CreateComponent`, `RenameComponent`, `DeleteComponent`, `SaveComponentDraft`).
  Every controller action:
  - is routed as in [contracts/menus-api.yaml](./contracts/menus-api.yaml);
  - declares exactly one `[RequireCapability(...)]` (research NV-18);
  - dispatches through `IDispatcher`;
  - refuses with the codes in [data-model.md §4.2](./data-model.md);
  - is registered in `PAGES/PagesModule.cs` and listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **Publishing** for menus goes through `PAGES/Infrastructure/PublishingWrites.cs` and
  `ScheduleWrites.cs` with `SubjectKinds.Menu`. No parallel publishing code is written (NV-01).
- **Generation bumps** go through `PAGES/Infrastructure/GenerationBump.cs` with the explicit
  site predicate, in the transaction of the change.
- **The public path** reads only through Contracts readers on read-only connections. Block
  renderers stay synchronous and do no I/O; data comes in through `RenderInputs`.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task. Public texts go through
  `RENDER/Features/NotFound/PublicTexts.cs`.
- **Logs and audit rows** carry ids, counts and codes only, never menu names, labels, anchors
  or addresses.

**References**:
- decisions: `NV-xx` in [research.md](./research.md);
- tables, graph, guards and errors: [data-model.md](./data-model.md);
- the menu document: [contracts/menu-document.md](./contracts/menu-document.md);
- the Navigation block: [contracts/navigation-block.md](./contracts/navigation-block.md);
- endpoints: [contracts/menus-api.yaml](./contracts/menus-api.yaml);
- public output: [contracts/public-navigation.md](./contracts/public-navigation.md);
- readers, events, export: [contracts/module-surface.md](./contracts/module-surface.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents. No new project references are needed
(module-surface.md §1).

- [X] T001 Commit `specs/024-navigation/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `024-navigation`, referencing #24.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the menu contract and the `navigation` node, validated on both sides;
- the `AddMenus` migration and entities;
- the new subject and dependency kinds;
- the Contracts reader additions;
- capabilities and error codes.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Menu contract (FR-006 to FR-011, FR-021, NV-03)

- [X] T002 Create `CONTRACTS/menu/v1.schema.json` (JSON Schema 2020-12, only the vocabulary of page-schema.md §7) from contracts/menu-document.md §1–§3 and §5: `entry`, `target` (`oneOf` page, item, url, anchor, none), label and anchor patterns, `additionalProperties: false`. The `url` target reuses the page schema's link scheme rule by copying the same pattern, with a comment naming page-schema §2a.
- [X] T003 Create `CONTRACTS/menu.md` from `specs/024-navigation/contracts/menu-document.md` (sections 1–7), and list it in `CONTRACTS/README.md`.
- [X] T004 [P] Create golden examples in `CONTRACTS/menu/examples/valid/` and `CONTRACTS/menu/examples/invalid/` as listed in menu-document.md §7, each invalid one with an `expected.json` naming `rule` and `pointer`.

### Navigation block in the page schema (FR-016, FR-017, FR-021, NV-05)

- [X] T005 Extend `CONTRACTS/page-schema/v1.schema.json`, staying at version 1: `$defs/navigation` and `$defs/navCollapse` exactly as contracts/navigation-block.md §2, and `navigation` added to `contentChild`. `PageSchemaSubsetTests` must pass.
- [X] T006 Update `CONTRACTS/page-schema.md`: the §2 row, the §3 note that `menuId` is not a site rule, and an "Additions in 024 (still version 1)" paragraph in §6, from navigation-block.md §1, §3 and §4. Run `ITESTS/Pages/PageSchemaContractTests.cs`; it must pass.
- [X] T007 [P] Create the five valid and five invalid examples of navigation-block.md §5 in `CONTRACTS/page-schema/examples/`.

### Validators, server and admin

- [X] T008 [P] Create `UTESTS/Pages/Menus/MenuSchemaGoldenTests.cs`, running T004's examples through `PAGES/Infrastructure/Schema/MenuDocumentValidator.cs` (rules 0–7 of menu-document.md §4, in that order). It must fail first.
- [X] T009 [P] Create `ADMINTESTS/editor/menuSchema.spec.ts`, running the same examples through `EDITOR/schema/menuValidator.ts`. It must fail first. Load the files the way `ADMINTESTS/editor/contractFiles.ts` does.
- [X] T010 [P] Create `UTESTS/Pages/NavigationSchemaGoldenTests.cs` and extend `ADMINTESTS/editor/documentValidator.spec.ts` with T007's examples, through `PageDocumentValidator` and `EDITOR/schema/documentValidator.ts`. They must fail first.
- [X] T011 Create `PAGESC/MenuSchema.cs`, embedding `menu/v1.schema.json` as `PAGESC/PageSchema.cs` embeds the page schema (same `.csproj` item pattern), and refresh the embedded page schema.
- [X] T012 Create `PAGES/Domain/Menus/MenuRules.cs` (pure: unique ids, entry count, depth, required labels, `newTab` placement; returns the first failure with `rule` and `pointer`) and `PAGES/Infrastructure/Schema/MenuDocumentValidator.cs` (size, JSON, duplicate properties, schema version, the schema through `SchemaSubsetValidator`, then `MenuRules`). Make T008 pass.
- [X] T013 [P] Create `EDITOR/schema/menuValidator.ts`, mirroring T012 with `EDITOR/schema/subsetValidator.ts` and the same rule order and codes. Make T009 pass.
- [X] T014 Accept the `navigation` node in `PAGES/Infrastructure/Schema/PageDocumentValidator.cs` (schema only, no new rule) and in `EDITOR/schema/schema.ts` and `EDITOR/schema/documentValidator.ts` (`blockTypes`, `canDrop`). Make T010 pass.

### Domain and database (data-model §1, NV-02)

- [X] T015 Add `SubjectKinds.Menu`, `DependencyKinds.Menu` and `DependencyKinds.Item` to `PAGES/Domain/PublishingTerms.cs`.
- [X] T016 Create `PAGES/Domain/Menus/Menu.cs` (aggregate: `Create` with the name rules of `Component.NormaliseName`, `Rename`, `MarkDeleted`; `MaxNameLength = 100`; `MaxPerSite = 50`), `MenuDraft.cs`, `MenuTarget.cs`, `MenuUsage.cs` and `MenuChange.cs` (actions as constants) in `PAGES/Domain/Menus/`.
  *Done as*: `MenuTarget`, `MenuUsage` and `MenuChange` share `PAGES/Domain/Menus/MenuIndexes.cs`. `Menu` is a plain site-owned entity, not an `AggregateRoot`: menus raise no domain events (data-model §6).
- [X] T017 Map the five tables in `PAGES/Infrastructure/PagesDbContext.cs`, exactly as data-model.md §1.1–§1.5: `citext` name with the unique index, `xmin` tokens, FKs with cascade, PKs and reverse indexes, and the site filter on all five. If `publications`, `published_versions`, `publish_schedules`, `publish_log` or `live_dependencies` have a check constraint on kinds, widen it. Create `PAGES/Infrastructure/Migrations/<ts>_AddMenus.cs`, forward-only and additive.
  *Done as*: `publish_schedules` keeps `ck_publish_schedules_unpublish_page`: only pages schedule an unpublish, so menus, like components, schedule a publish only, which is what FR-004a asks for.
- [X] T018 Extend `PAGES/Infrastructure/PagesOutbox.cs` with an explicit `SubjectPublishedEvent { SubjectKind: SubjectKinds.Menu }` and `SubjectUnpublishedEvent { SubjectKind: SubjectKinds.Menu }` case that adds nothing, before the component fallback (data-model §6). Add the cases to `UTESTS/Pages/PagesOutboxTests.cs` (create the file if there is none) so a menu publish never emits `Pages.ComponentPublished`.
  *Done as*: `UTESTS/Pages/PagesOutboxTests.cs` is new; it inspects the change tracker without a database.

### Contracts (module-surface.md §2)

- [X] T019 [P] Add `GetLiveMenusAsync` (returning `LiveMenu`: document, schema version, and the live version's `title` as `Name`, module-surface.md §2) to `PAGESC/Publishing/IPublishedContentReader.cs`, and the trailing `string? LiveTitle = null` to `PageLinkTarget`. Implement both in `PAGES/Infrastructure/PublishedContentReader.cs` over `PAGES/Infrastructure/PublishedReadDbContext.cs`: one query each, live versions of `subject_kind = 'menu'`, and the live version's title for page targets. Extend the existing reader integration test to cover them, including another site's menu id being absent and the name being the published one, not one renamed since.
  *Done as*: The name-is-published-not-renamed case needs menu publishing, so it is in T034. `ITESTS/Rendering/PublicPathReadOnlyTests.cs` covers `LiveTitle` and absent menus.

### Capabilities and errors (NV-18, data-model §4.2)

- [X] T020 Add `pages.menus.manage` (Designer) to `PAGES/PagesCapabilities.cs`, and record it in `specs/002-identity-setup/contracts/authorization.md` §2.
- [X] T021 [P] Add every error code of data-model.md §4.2 to `PAGES/PagesErrors.cs` and `PAGES/PagesErrorResults.cs` (`invalid_menu_name`, `duplicate_menu_name`, `menu_limit_reached`, `menu_not_found`, `invalid_menu_document`, `unpublished_menus`, `version_missing_menus`, `menu_in_use_by_published_content`, `invalid_reference_query`), with HTTP statuses as listed in menus-api.yaml. Create `ADMIN/features/menus/menuErrors.ts` with a message for each, and add the publishing ones to `ADMIN/features/publishing/errorMessages.ts`, with `ADMIN/i18n/en.json`.

**Checkpoint**: contracts, validators, tables and kinds are in place. User stories can start.

---

## Phase 3: User Story 1 - Building a multi-level menu once (Priority: P1) 🎯 MVP

**Goal**: Designers create, rename, duplicate and delete menus, and build their entry trees with
every target type, labels and attributes, up to three levels. Editors can view them.

**Independent Test**: quickstart §1. Create a menu with every target type over three levels,
reload, and confirm it is stored exactly. Every refusal gives its reason.

### Tests for User Story 1 ⚠️

- [X] T022 [P] [US1] Create `UTESTS/Pages/Menus/MenuTests.cs` for the `Menu` aggregate: name trimming, empty and 101-character names refused, rename, and the per-site limit as an input (`existingCount`).
  *Done as*: `UTESTS/Pages/Menus/MenuTests.cs`, with the project's `SteppedClock`.
- [X] T023 [P] [US1] Create `ITESTS/Pages/MenuEndpointsTests.cs` for list, get, create, duplicate, rename, save draft and delete (without publishing):
  - names unique ignoring case (`409 pages.duplicate_menu_name`), and the 51st menu refused (`409 pages.menu_limit_reached`);
  - a draft with every target type round-trips equal as a JSON value;
  - invalid documents give `400 pages.invalid_menu_document` with `rule` and `pointer` for a fourth level, a missing label, a `javascript:` href and a duplicate id;
  - a stale `version` gives `409 pages.out_of_date`;
  - rule 8: a new `pageId` of `$S2`, an unknown `itemId`, and an item of another collection are refused (`unknown_target`), while an unchanged target whose page was deleted is accepted;
  - duplicate copies the draft and publishes nothing;
  - Designer allowed; Editor can list and get but gets `403` on every write; a member of another site gets 404;
  - each write adds one `menu_changes` row with the right action and no names.
  *Done as*: `ITESTS/Pages/MenuEndpointsTests.cs` (12 cases, on `TemplatesWorld` for pages, items and a second site) with the helper `ITESTS/Pages/MenusApi.cs`. `MenuIsolationProbe` was started here for the US1 endpoints, because `IsolationCoverageTests` fails as soon as an endpoint takes a record id; T071 completes it.
- [X] T024 [P] [US1] Create `ADMINTESTS/features/menus/MenusView.spec.ts` and `ADMINTESTS/features/menus/MenuEditorView.spec.ts`: list, create, rename, duplicate, delete confirmation; adding root and child entries of each target kind through `MenuEntryDialog`; "add child" not offered at level 3; label required for url, anchor and none; `#` stripped from anchors; read-only for a member without `pages.menus.manage`; leave warning with unsaved changes; every invalid entry marked before saving (FR-011); the own-host hint appears for an address on one of the site's host names and not for others; axe.
  *Done as*: The specs are in `ADMINTESTS/menus/` (`MenusView.spec.ts`, `MenuEditorView.spec.ts`, `menuFixtures.ts`): the admin tests have no `features/` folder.

### Implementation for User Story 1

- [X] T025 [US1] Create `PAGES/Domain/Menus/MenuTargets.cs` (pure): extract `(kind, id)` page and item targets from a document, and, for item targets, their `collectionId`. Unit-test it in `UTESTS/Pages/Menus/MenuTargetsTests.cs`.
- [X] T026 [US1] Create `PAGES/Infrastructure/MenuWrites.cs`: create (limit under the site's menu count, new empty draft), duplicate, rename, save draft (validate with `MenuDocumentValidator`, then rule 8 against `menu_targets` using `Page` and `ICollectionSchemaReader.GetItemAddressPartsAsync`, rewrite `menu_targets`, keep the `xmin` check), and delete (unpublished case only for now: draft, indexes, `ForgetAsync` through `PublishingWrites`). Every write adds a `MenuChange` in its transaction.
  *Done as*: Delete already refuses a menu that live pages or components show (`InUseErrorAsync`), the guard of T042. A draft save does not touch the `menus` row, so a rename right after a save is not stale. `ReplaceDraftAsync` is the one path for saves and, later, copy-to-draft (T046).
- [X] T027 [US1] Create `PAGES/Infrastructure/MenuReads.cs` and `PAGES/Infrastructure/MenuTargetStates.cs`: list with entry count, broken count, used-by count (from `menu_usages` and `live_dependencies`) and publication summary through `PublicationReads`; get with the draft, draft version and the state of every target (data-model §4.3), using page live state and `GetItemAddressPartsAsync` plus the live template patterns.
  *Done as*: Item names come from a new optional `Name` on `CMSC/ICollectionSchemaReader.cs` `ItemAddressParts` (the item's draft name), filled by `CMS/Infrastructure/CollectionSchemaReader.cs`: an additive Cms.Contracts change that module-surface.md §3 did not foresee. `PublicationReads` and `PublishingWrites` (`SubjectAsync`, `DraftAsync`) learned the menu kind here, so menu summaries are right from the start.
- [X] T028 [US1] Create the slices `PAGES/Features/Menus/ListMenus.cs`, `GetMenu.cs`, `CreateMenu.cs` (with `duplicateOf`), `RenameMenu.cs`, `SaveMenuDraft.cs`, `DeleteMenu.cs` and `MenuResponses.cs`, routed as menus-api.yaml, with `pages.content.view` for reads and `pages.menus.manage` for writes. Register them, and add them to the endpoint catalog. Make T022 and T023 pass.
  *Done as*: Rename is `PUT …/menus/{id}` with `{ name, version }`, as components are renamed; contracts/menus-api.yaml is updated (it said PATCH).
- [X] T029 [P] [US1] Create `ADMIN/api/menus.ts`, a typed client for every operation of menus-api.yaml.
- [X] T030 [P] [US1] Generalise `ADMIN/components/RedirectToPagePicker.vue` into `ADMIN/components/PagePicker.vue` (searchable page tree, returns a page id), keep `RedirectToPagePicker.vue` as a thin wrapper, and create `ADMIN/components/ItemPicker.vue` (choose a collection, then search its items through the existing item list API in `ADMIN/api/items.ts`; returns `collectionId` and `itemId`). `PagePicker` leaves out template pages (016 TB-02). Keep the redirect specs green.
- [X] T031 [US1] Create `ADMIN/features/menus/MenusView.vue` (list with name, live state badge, entry count, broken count, "used by"; create, rename, duplicate and delete) and add the routes `/sites/:siteId/menus` (`site-menus`) and `/sites/:siteId/menus/:menuId` (`site-menu`) to `ADMIN/app/router.ts`, and a **Menus** link beside Components in `ADMIN/features/sites/SiteDetailView.vue`, visible with `pages.content.view`.
- [X] T032 [US1] Create `ADMIN/features/menus/MenuEntryTree.vue` (ARIA `tree` of entries with label or fallback title, target kind and state badge; add, add child, edit and remove actions) and `ADMIN/features/menus/MenuEntryDialog.vue` (label, target kind with `PagePicker`, `ItemPicker`, address, anchor; new tab; a **Shown** switch, stored inverted as `hidden`), validating with `EDITOR/schema/menuValidator.ts` before closing. Mark every invalid entry in the tree before saving, not only the first (FR-011). When a `url` target's host is one of the site's host names (from the existing site API), the dialog shows a non-blocking hint suggesting a page target instead.
  *Done as*: The tree's actions act on the selected entry from one toolbar, so the ARIA tree items hold text only, as in 011's layer view. `MenuEntryTree.vue` already holds the drag and drop and Alt+arrow moves of T056.
- [X] T033 [US1] Create `ADMIN/features/menus/MenuEditorView.vue`: loads the menu, edits the draft in memory, **Save draft** with the draft version, conflict and validation messages from `menuErrors.ts`, the leave warning used by the editor, and read-only mode without `pages.menus.manage`. Make T024 pass.

**Checkpoint**: quickstart §1 passes. Menus can be built and kept, but not yet shown.

---

## Phase 4: User Story 2 - Placing a menu in a block and seeing it on every page (Priority: P1)

**Goal**: menus are published like components. The Navigation block shows a live menu in pages
and components, rendered on the server with collapse, disclosures and the inline script. A menu
publish refreshes exactly the pages that show it.

**Independent Test**: quickstart §2. Publish a menu and a header that shows it; every page using
the header shows the menu; guards refuse publishing with an unpublished menu and unpublishing a
menu in use; output matches the golden HTML at every layout, with and without scripts.

### Tests for User Story 2 ⚠️

- [X] T034 [P] [US2] Create `ITESTS/Pages/MenuPublishingTests.cs`:
  - publish, unpublish, a scheduled publish (menus, like components, cannot schedule an unpublish), rollback, history, versions and copy-to-draft for menus, mirroring the component tests (numbers, log entries labelled with the menu name, `hasUnpublishedChanges`, rename does not mark the draft changed);
  - publishing a page or component whose draft has a Navigation block for a never-published or deleted menu gives `409 pages.unpublished_menus` naming it; a scheduled run fails with the same reason;
  - rolling back a page to a version naming a deleted menu gives `409 pages.version_missing_menus`;
  - unpublishing or deleting a menu shown by a live page, or by a live component, gives `409 pages.menu_in_use_by_published_content` with both counts; deleting a menu used only by drafts succeeds and leaves those drafts saveable;
  - capabilities: Editors cannot publish menus;
  - renaming a published menu does not change the live version's title in `GET …/history` until the next publish; rollback restores the earlier name;
  - `GET …/menus/{id}/preview` returns the resolved draft: an entry's own label, else the page's current title or the item's name; target states; `hidden: true` entries included; no addresses. `pages.content.view` is enough (Editors allowed), and another site's menu gives 404;
  - `GET …/menus/{id}/usages` lists the pages and components whose draft or live version shows the menu, with `inDraft` and `inLive`. Draft saves and publishes update it, and another site's menu gives 404.
  *Done as*: `ITESTS/Pages/MenuPublishingTests.cs` (8 cases). The scheduled run's failure reason and the version-title rename case are covered there; the preview and usages cases also exercise the `MenuIsolationProbe` routes.
- [X] T035 [P] [US2] Create `UTESTS/Pages/DependencyExtractorMenuTests.cs`: a page or component document yields `(menu, id)` per Navigation block; a menu document yields `(page, id)`, `(item, id)` and `(template, collectionId)`.
- [X] T036 [P] [US2] Create `UTESTS/Rendering/Navigation/MenuModelTests.cs` (NV-10): labels and fallbacks, links for live page and item targets with the base path and anchors, `url` scheme checks, `hidden` pruning with children, `levels` pruning, entries without links kept as text only when they have shown children, `none` pruning, a page target that is a template page treated as not live (016 TB-02), an empty result.
- [X] T037 [P] [US2] Create `UTESTS/Rendering/Navigation/NavigationGoldenTests.cs` with golden files in `UTESTS/Rendering/Navigation/golden/`: horizontal and vertical, levels 1–3, collapse off, mobile and tablet, alignment, colours, `ariaLabel` default and custom, new-tab text, `name` groups per block and level, two blocks on one page, and a label of `"><script>x</script>` encoded everywhere (SC-007); the `aria-label` default comes from the live menu's published name, not a later rename.
  *Done as*: Golden HTML is kept beside the page-schema examples (`tests/fixtures/rendering/`), as for the other blocks, and every existing golden was regenerated for the new stylesheet hash only.
- [X] T038 [P] [US2] Create `UTESTS/Rendering/Navigation/NavScriptTests.cs`: the script's bytes match a golden file, its minified size is at most 2 KB, its SHA-256 equals the hash in the CSP, the script is written once after the first `<nav class="qb-nav">`, and a page without a Navigation block has 012's CSP byte for byte and no `<script>` besides JSON-LD.
  *Done as*: The golden script is `tests/fixtures/rendering/navigation/nav-script.js`. The renderer's "no JavaScript" golden test exempts exactly `NavScript.Element`.
- [X] T039 [P] [US2] Create `ITESTS/Rendering/NavigationRenderTests.cs`: a published page with a component that shows a live menu renders it; a draft menu change is not visible until the menu is published; a menu publish changes the ETag of every page that shows it (directly and through a component, including item pages through their template) and of no other page; two instances with a shared cache both see it within 5 s (SC-002, menu rows of NV-07); renaming a menu changes no page's ETag, and publishing it afterwards does.
- [X] T040 [P] [US2] Create `ADMINTESTS/editor/blocks/NavigationBlock.spec.ts`: inserting the block from the palette, the menu picker, the collapse control, the canvas drawing the draft preview, links not navigating, a missing or unpublished menu marked on the block, and the markup matching the renderer's classes in `ADMINTESTS/editor/CanvasMarkupParity.spec.ts`; axe.
  *Done as*: The spec is `ADMINTESTS/editor/NavigationBlock.spec.ts` (the editor specs have no `blocks/` folder). Markup parity is in `CanvasMarkupParity.spec.ts`, which compares the `nav` only and drops the script.

### Implementation for User Story 2

- [X] T041 [US2] Teach `PAGES/Infrastructure/PublishingWrites.cs` the menu subject: `SubjectAsync` (menu name as label), `DraftAsync` (`menu_drafts`), the validator choice in `PublishContentAsync` (`MenuDocumentValidator` for menus, skipping the page-only checks: rich text, components, page parameters, collection uses), `AfterLiveChangeAsync` bumping `(menu, id)` targets, and the not-found error per kind (`pages.menu_not_found`). For menus, the version's `title` is the menu's name at publish (rollback copies the restored version's title, as for pages). Extend `PAGES/Infrastructure/ScheduleWrites.cs` and `PAGES/Infrastructure/PublicationReads.cs` for the kind.
  *Done as*: `PublishingWrites` gains `Validate(subject, document)` and `VersionTitle`, which `ScheduleWrites` now uses too, so the schedule runner validates menus the same way. The live version's title is the menu's name when published (U4).
- [X] T042 [US2] Add the guards of data-model.md §4.2 to `PAGES/Infrastructure/PublishingWrites.cs`: in `PublishContentAsync`, refuse `pages.unpublished_menus` for Navigation blocks naming menus that are missing or not live (beside the component check); in `RollbackAsync`, refuse `pages.version_missing_menus`; in `UnpublishLiveAsync` for menus, refuse `pages.menu_in_use_by_published_content` from `live_dependencies` (pages and components); and in `MenuWrites` delete, the same in-use refusal, taking down a live unused menu through `ForgetAsync`.
- [X] T043 [US2] Extend `PAGES/Domain/DependencyExtractor.cs`: `navigation` nodes yield `(menu, menuId)`, and a new `MenuDependencies(document)` yields `(page, id)`, `(item, id)` and `(template, collectionId)` for menu documents; use it in `ReplaceDependenciesAsync` when the subject is a menu. Make T035 pass.
- [X] T044 [US2] Extend `PAGES/Infrastructure/GenerationBump.cs` `AffectedPagesAsync` with the menu hop of data-model §3 (targets → pages, components, menus; menus → pages, components; components → pages).
- [X] T045 [US2] Maintain `menu_usages` in `PAGES/Infrastructure/DraftWrites.cs` beside `component_usages`, for page and component draft saves, and remove them with the page or component.
- [X] T046 [US2] Add menu routes to the publishing slices, as the component routes are: `PAGES/Features/Publish/Publish.cs`, `PAGES/Features/Unpublish/Unpublish.cs`, `PAGES/Features/Rollback/Rollback.cs`, `PAGES/Features/Schedules/Schedules.cs` and `PAGES/Features/PublishingHistory/PublishingHistory.cs` (history, versions, copy-to-draft with `pages.menus.manage`). Copying a menu version into the draft goes through `MenuWrites`' save path (T026), so `menu_targets` is rewritten and rule 8 applies to targets the current draft does not hold; a target deleted since that version is kept as an unchanged broken target. Create `PAGES/Features/Menus/PreviewMenu.cs` (NV-16) and `PAGES/Features/Menus/MenuUsages.cs`. Make T034 pass.
  *Done as*: Schedules for menus are publish-only (T017). `GET …/publish-log` now also treats menus as existing subjects, so their entries link to their history.
- [X] T047 [US2] Create `RENDER/Rendering/Navigation/ResolvedEntry.cs` and `RENDER/Rendering/Navigation/MenuModel.cs` (parse a live menu document and resolve it against `RenderInputs` as public-navigation.md §2). Make T036 pass.
  *Done as*: `ResolvedEntry` lives in `MenuModel.cs` beside `NavState`; `MenuModel.Prune` removes hidden and unresolved entries after `ActiveState.Mark`.
- [X] T048 [US2] Collect `menuId`s in `RENDER/Domain/RenderReferences.cs`; add `Menus`, `MenuItems` and `ItemPatterns` to `RENDER/Domain/RenderInputs.cs`; and in `RENDER/Features/RenderPage/PageResponder.cs`, read live menus, add menu page targets to the existing `GetLinkTargetsAsync` batch, and read item targets with `IPublishedItemReader.GetAsync` and `GetLiveTemplatePatternsAsync`, only when the page shows a menu. Add `CurrentItemId` (`Guid?`) to `RenderInputs`, filled by `RenderItemAsync` in `PageResponder.cs`, for active state on item pages (FR-027).
- [X] T049 [US2] Create `RENDER/Rendering/Navigation/NavScript.cs` (the inline script constant and its SHA-256, computed once) and `RENDER/Rendering/Blocks/NavigationRenderer.cs` (public-navigation.md §4: `nav`, lists, `details`/`summary` disclosures with per-block-and-level `name`, the open collapse panel, new-tab text, colours as custom properties). Write the script once after the first navigation block from `RENDER/Rendering/PageShell.cs`, and add `script-src 'sha256-…'` to `ContentSecurityPolicy` in `PageResponder.cs` only when it was written. Add "Menu", "Show submenu: {0}" and "(opens in a new tab)" to `RENDER/Features/NotFound/PublicTexts.cs`. Make T037 and T038 pass.
  *Done as*: `RenderedPage` gains an optional `ScriptHash`; the CSP adds `script-src 'sha256-…'` only when the page carries the script.
- [X] T050 [US2] Add the navigation styles of public-navigation.md §5 to `EDITOR/blocks/blocks.css` (shared with the block stylesheet): horizontal dropdowns, vertical inline submenus, the toggle and stacking inside `@container page (max-width: 480px)` and `(max-width: 767px)`, visible focus, 44 px hit areas at mobile width, reduced motion. Make T039 pass, and keep the existing golden HTML and stylesheet tests green (the renderer stamp changes).
- [X] T051 [P] [US2] Create `EDITOR/blocks/navigation/definition.ts` and `EDITOR/blocks/navigation/NavigationBlock.vue` (draws `GET …/preview` with the renderer's classes; links inert; marks for a missing or never-published menu), `EDITOR/panel/controls/MenuControl.vue` (menu picker with "Open in Menus" for `pages.menus.manage`) and `EDITOR/panel/controls/NavCollapseControl.vue`. Register the block in `EDITOR/blocks/registry.ts` and the control kinds `menu` and `navCollapse` in `EDITOR/panel/PropertyPanel.vue`. Keep `ADMINTESTS/editor/BlockRegistryCoverage.spec.ts` green. Make T040 pass.
  *Done as*: The menu list is loaded once per site by `EDITOR/state/useSiteMenus.ts`; the palette refuses the block with `editor.errors.ops.no_menus` while the site has none (`BlockDefinition.missing`).
- [X] T052 [US2] Add `menu` to `SubjectKind` in `ADMIN/api/publishing.ts` (`menus` path segment), and reuse `ADMIN/features/publishing/PublishMenu.vue`, `HistoryView.vue` and `ScheduleDialog.vue` for menus in `MenusView.vue` and `MenuEditorView.vue`, with history routes under `/sites/:siteId/menus/:menuId/history` in `ADMIN/app/router.ts`. Show the `pages.unpublished_menus` refusal in the page and component publish flow as missing components are shown. The delete confirmation lists `…/usages`.
  *Done as*: Kind-specific routes and draft reads are in `ADMIN/features/publishing/subjects.ts`, used by `PublishMenu`, `HistoryView` and `PublishLogView` (which gains a Menus filter). Menu versions have no read-only editor view, so History offers Roll back and Copy to draft only. The menu editor saves pending changes before publishing.

**Checkpoint**: quickstart §2 passes. A menu built once appears on every page that uses it.

---

## Phase 5: User Story 3 - Reordering and moving entries by drag and drop (Priority: P2)

**Goal**: entries move by drag and drop and by keyboard, with children, never past three levels
or into themselves, with undo.

**Independent Test**: quickstart §3. Every move by pointer and keyboard gives the expected stored
order; refused moves change nothing; moves are announced.

### Tests for User Story 3 ⚠️

- [X] T053 [P] [US3] Create `ADMINTESTS/features/menus/menuOps.spec.ts` for `ADMIN/features/menus/menuOps.ts`: `move` before, after and inside, with children; `canMove` refusing a fourth level counting the moved subtree, and a drop into the entry's own subtree; keyboard moves (up, down, indent into previous sibling, outdent after parent) at the edges; undo and redo through `useMenuHistory`.
  *Done as*: The spec is `ADMINTESTS/menus/menuOps.spec.ts`, beside the other menu specs.
- [X] T054 [P] [US3] Extend `ADMINTESTS/features/menus/MenuEditorView.spec.ts`: drag events produce the drop indicator and a refused position is shown as not allowed; `Alt+↑`, `Alt+↓`, `Alt+→`, `Alt+←` move the focused entry; each move is announced in the live region; `Ctrl/⌘+Z` and `Ctrl/⌘+Shift+Z` work; axe.
  *Done as*: In `ADMINTESTS/menus/MenuEditorView.spec.ts`. jsdom rows have no height, so a drop there lands after the row; the before, after and inside positions themselves are covered by `menuOps.spec.ts`.

### Implementation for User Story 3

- [X] T055 [US3] Create `ADMIN/features/menus/menuOps.ts` (pure tree operations on menu documents: `locate`, `canMove`, `move`, `moveUp`, `moveDown`, `indent`, `outdent`) and `ADMIN/features/menus/useMenuHistory.ts` (undo and redo stack of documents, cleared on save). Make T053 pass.
  *Done as*: Written with US1 (T032, T033), since adding and removing entries go through the same operations.
- [X] T056 [US3] Add native drag and drop to `ADMIN/features/menus/MenuEntryTree.vue`, following `EDITOR/dnd/useDragDrop.ts` (before, after and inside positions from the pointer, an indicator, `canMove` for allowed positions), and the ED-11 keys with announcements through the admin's live region. Wire undo and redo in `MenuEditorView.vue`. Make T054 pass.
  *Done as*: Written with US1 (T032). The drop indicator styles were added here, scoped to `MenuEntryTree.vue`; a refused place is drawn dashed in the danger colour with a not-allowed cursor.

**Checkpoint**: quickstart §3 passes.

---

## Phase 6: User Story 4 - Links that stay correct when content changes (Priority: P2)

**Goal**: pages that show a menu refresh when a target page or item is published, unpublished,
moved, renamed or deleted, or its template changes. The admin shows each entry's target state
and warns before removing a target that menus point at.

**Independent Test**: quickstart §4. Each row of the table gives the expected link on the site,
on two instances, and the expected state in Menus.

### Tests for User Story 4 ⚠️

- [X] T057 [P] [US4] Create `ITESTS/Rendering/MenuInvalidationTests.cs`, one test per row of research NV-07's table after the menu-publish row: target page published, unpublished, retitled at publish (entry without label), moved, slug changed, made home; item published with a new name or slug, unpublished, deleted; the item collection's template published, unpublished or moved. Each changes the ETag of exactly the pages that show the menu, directly or through a component, and of no other page, within 5 s on two instances (SC-002). Item pages under a template that shows the menu are included.
  *Done as*: One test per family of rows (target page, target item, item template), each row a checked step, in `ITESTS/Rendering/MenuInvalidationTests.cs`. Generations stand in for ETags (the ETag is derived from them); the two-instance case is `NavigationRenderTests.A_menu_publish_on_one_instance_is_served_by_the_other`, since every row moves the same shared generation rows.
- [X] T058 [P] [US4] Create `ITESTS/Rendering/MenuLinkIntegrityTests.cs` (SC-003): across every target type in every state (live, moved, not published, scheduled, deleted, item without a live template), no published page contains a link to a target that is not live, and an unpublished parent with shown children is written as text.
  *Done as*: To write an unpublished parent as text, `GetLinkTargetsAsync` now returns the title visitors last saw: the live version's, or the last published one's after an unpublish (`PageLinkTarget.LiveTitle`, documented in module-surface.md). The read-only `VersionRow` maps `subject_kind`, `subject_id` and `number` for it; no migration. `PublicPathReadOnlyTests` covers it.
- [X] T059 [P] [US4] Create `ITESTS/Pages/MenuReferencesTests.cs` for `GET …/menus/references`: drafts and live versions, `inDraft` and `inLive`, page and item queries, exactly one of the two parameters, another site's menus never listed; after copying a menu version into the draft, references reflect the copied targets.
- [X] T060 [P] [US4] Extend `ITESTS/Pages/MenuEndpointsTests.cs`: `GET` returns `live`, `not_published`, `deleted` and `no_page` states, and the list's broken count.
  *Done as*: `live` and the list's broken count are new; `not_published`, `deleted` and `no_page` were already covered by the US1 cases.
- [X] T061 [P] [US4] Create `ADMINTESTS/features/menus/MenuTargetStates.spec.ts` and extend `ADMINTESTS/pageTreeView.spec.ts` and the item dialog specs in `ADMINTESTS/cms/`: badges for each state, broken entries summarised at the top, and delete and unpublish confirmations for pages and items listing the menus that point at them.
  *Done as*: The specs are in `ADMINTESTS/menus/MenuEditorView.spec.ts` (state badges), `ADMINTESTS/publishing/PublishMenu.spec.ts` (page unpublish) and `ADMINTESTS/cms/items/DeleteItemDialog.spec.ts` (item delete), since the warning is one component, `MenuReferencesNote.vue`, used by all four confirmations.

### Implementation for User Story 4

- [X] T062 [US4] Create `PAGES/Features/RefreshPagesShowingMenuItems/RefreshPagesShowingMenuItemsHandler.cs` for `Cms.ItemPublished`, `Cms.ItemUnpublished` and `Cms.ItemDeleted` (when `WasPublished`): bump `(item, ItemId)` through `GenerationBump`, deduplicated with `RefreshOnce` under its own handler names. Register it in `PAGES/PagesModule.cs`, and add the consumer rows to `specs/001-foundation-skeleton/contracts/integration-events.md`.
- [X] T063 [US4] Confirm that each existing bump path goes through `GenerationBump.AffectedPagesAsync` (and so through T044's menu hop), one T057 row each:
  - `PublishingWrites.AfterLiveChangeAsync` → rows "target page published, unpublished, retitled";
  - `PageTreeWrites.BumpLinkersAsync` → rows "moved, slug changed, made home";
  - `GenerationBump.BumpForCollectionsAsync(includeTemplates: true)`, from `AfterLiveChangeAsync` and `BumpLinkersAsync` → rows "template published, unpublished, moved";
  - T062's handler → the item rows.

  A path that calls `BumpPagesAsync` with page ids it computed itself (and so skips the hop) is changed to `BumpForTargetsAsync`. Make T057 and T058 pass.
  *Done as*: Confirmed without code changes: every listed path calls `BumpForTargetsAsync` or `BumpForCollectionsAsync`, which go through `AffectedPagesAsync`. The two direct `BumpPagesAsync` calls (a page's own template change, and the descendants of a page whose address changed) bump pages for their own addresses, not for something they show.
- [X] T064 [US4] Create `PAGES/Features/Menus/MenuReferences.cs` (`GET …/menus/references`, `pages.content.view`) over `menu_targets` and `live_dependencies`. Make T059 pass, and make T060 pass through `MenuTargetStates`.
  *Done as*: Written with US1 (T028), so the admin client could use it.
- [X] T065 [US4] In the admin, show target state badges and the broken summary in `ADMIN/features/menus/MenuEntryTree.vue` and `MenuEditorView.vue`; call the references endpoint from the page delete and unpublish confirmations in `ADMIN/features/pages/PageTreeView.vue` and `ADMIN/features/publishing/PublishMenu.vue`, and from `ADMIN/features/cms/items/DeleteItemDialog.vue` and `ItemPublishBar.vue` (unpublish), listing the menus (FR-026). Make T061 pass.
  *Done as*: Badges and the broken summary were written with US1. `ADMIN/features/menus/MenuReferencesNote.vue` lists the menus, each linked, in the page delete (PageTreeView), page unpublish (PublishMenu), item delete (DeleteItemDialog) and item unpublish (ItemPublishBar) confirmations.

**Checkpoint**: quickstart §4 passes. Menus never send visitors to a page that is not live.

---

## Phase 7: User Story 5 - Visitors see where they are (Priority: P2)

**Goal**: the server marks the current page, the entries that contain it and the current
section, and opens the current path in vertical and collapsed menus.

**Independent Test**: quickstart §5. Each request in the table marks exactly the expected
entries, and the starting state of submenus matches FR-019a.

### Tests for User Story 5 ⚠️

- [ ] T066 [P] [US5] Create `UTESTS/Rendering/Navigation/ActiveStateTests.cs` (SC-004) for `RENDER/Rendering/Navigation/ActiveState.cs`: current, containing and section for nested pages, a page not in the menu, the home page only on `/`, item pages with and without an entry, paged lists (`?page=2`), anchors, the same target twice, `url`/`anchor`/`none` never current, hidden entries, and levels pruned below the current entry.
- [ ] T067 [P] [US5] Extend `UTESTS/Rendering/Navigation/NavigationGoldenTests.cs`: `aria-current="page"` and the containing and section classes; vertical blocks write `open` on the current path; horizontal blocks never do and mark `data-qb-current-path`.
- [ ] T068 [P] [US5] Create `ADMINTESTS/rendering/navScript.spec.ts`, a jsdom test that loads the golden script file of T038 and the golden HTML of T067: Escape closes the innermost submenu and focuses its summary; opening one closes its `name` siblings; an outside click closes horizontal dropdowns; a narrow container closes the panel and a wide one opens it; opening a collapsed horizontal panel opens the current path.

### Implementation for User Story 5

- [ ] T069 [US5] Create `RENDER/Rendering/Navigation/ActiveState.cs` (pure, NV-11), and call it from `NavigationRenderer` with the current page id or item id and path from `RenderInputs`. Make T066 pass.
- [ ] T070 [US5] Write `aria-current`, the state classes, `open` for vertical current paths and `data-qb-current-path` in `RENDER/Rendering/Blocks/NavigationRenderer.cs`, and the `activeColor` styles in `EDITOR/blocks/blocks.css`. Finish the script's current-path step in `RENDER/Rendering/Navigation/NavScript.cs`, updating its golden file and hash. Make T067 and T068 pass.

**Checkpoint**: quickstart §5 passes. All stories are done.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T071 [P] Create `ITESTS/Isolation/Probes/MenuIsolationProbe.cs`: site B cannot list, get, change, publish, delete or query references of site A's menus; a menu draft on A naming B's page or item is refused (`unknown_target`); a Navigation block on B naming A's menu renders nothing and cannot be published. Register it, and keep `ITESTS/Isolation/IsolationCoverageTests.cs` green (FR-035).
- [ ] T072 [P] Run `ATESTS/AuthorizationDeclarationTests.cs` and confirm every new action declares exactly one capability.
- [ ] T073 [P] Create `STESTS/MenuRenderPerformanceTests.cs` (Performance category): a page showing two 300-entry, three-level menus, p95 under 200 ms on a miss and under 20 ms on a hit (SC-005). Record the results here; if a budget fails, record the cause and the fix.
- [ ] T074 [P] Create `ITESTS/Rendering/UpgradeFrom017Tests.cs`, following `UpgradeFrom016Tests.cs`: a 017 snapshot upgrades, `AddMenus` applies, every existing page renders the same body and the same CSP, and no stored document changed.
- [ ] T075 [P] Add a `MenuRendererCoverageTests` case to `UTESTS/Rendering/BlockRendererCoverageTests.cs` (or a sibling file): the build fails when `CONTRACTS/menu/` holds a schema version the renderer does not list.
- [ ] T076 Add "create a menu, publish it, add a Navigation block to the header, publish, and find the `<nav>` on the home page" to the release smoke journey in `STESTS/ReferenceDeploymentSmokeTests.cs`.
- [ ] T077 [P] Create `ITESTS/Observability/MenuContentNotLoggedTests.cs` over every 024 path that logs (menu writes, publishing, the item handler, rendering): menu names, labels, anchors and addresses never appear in logs or `menu_changes`.
- [ ] T078 [P] Update the docs:
  - `specs/012-publishing-rendering/contracts/public-rendering.md` (Navigation section and CSP note, from public-navigation.md), `readers.md` (module-surface §2) and `publishing-api.yaml` (menu subject);
  - `docs/contributing.md` (the block checklist names the menu contract for blocks that read it);
  - `docs/backlog.md` (the deferred items in plan.md, and the #20 obligations of module-surface §5);
  - `docs/operations.md` (the CSP note of upgrade note 2);
  - `docs/configuration.md`: no new keys (state it).
- [ ] T079 [P] Run `dotnet test` with coverage. `PublishingWrites`, `GenerationBump`, `RENDER/Rendering/Navigation/` and `NavigationRenderer` keep at least 80 % line coverage. Run `npm run lint`, `npm run type-check` and `npm test`, and check that `dotnet format --verify-no-changes` and the analyzer gates are clean.
- [ ] T080 Run the whole of `quickstart.md` by hand on the reference compose setup: SC-001 with a stopwatch, and quickstart §8 with NVDA and VoiceOver (exception E-5). Record the results in `specs/024-navigation/quickstart-results.md`.
- [ ] T081 Open the PR closing #24, with the plan's operator impact and upgrade notes in the description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup. Blocks every story.
- **US1 (Phase 3)**: depends on Foundational. The MVP.
- **US2 (Phase 4)**: depends on Foundational and on US1's `MenuWrites` (T026), which T042
  extends with the in-use delete guard. Its admin publishing (T052) and the preview
  endpoint's use in the canvas need US1's menus and `ADMIN/api/menus.ts` (T029); its server
  tests create menus through the US1 endpoints (T028).
- **US3 (Phase 5)**: depends on US1 (the tree and editor view).
- **US4 (Phase 6)**: depends on US2 (rendering, the dependency hop and menu publishing) and on
  US1 (target states in the admin).
- **US5 (Phase 7)**: depends on US2 (`NavigationRenderer`, `NavScript`).
- **Polish (Phase 8)**: after all stories.

### Within Each User Story

- Tests first, and they must fail.
- Then domain, infrastructure, slices, renderer and admin.
- A story is done when its quickstart section passes.

### Parallel Opportunities

- **Foundational**: T004, T007, T008, T009 and T010 (examples and tests) are parallel; T013
  runs beside T012; T019 and T021 are parallel with the database tasks.
- **Stories**: after US1 lands its endpoints, US2 (server) and US3 (admin only) run in parallel
  by different people; after US2, US4 and US5 are parallel (different files except
  `NavigationRenderer.cs`, touched by US5 only).
- **Within a story**: every test task marked [P] is parallel, and the admin tasks marked [P]
  are parallel with the server tasks.

---

## Parallel Example: Foundational

```text
Task: "T004 menu golden examples in CONTRACTS/menu/examples/"
Task: "T007 navigation examples in CONTRACTS/page-schema/examples/"
Task: "T008 MenuSchemaGoldenTests in UTESTS/Pages/Menus/"
Task: "T009 menuSchema.spec.ts in ADMINTESTS/editor/"
Task: "T010 NavigationSchemaGoldenTests in UTESTS/Pages/"
```

## Parallel Example: User Story 2

```text
Task: "T034 MenuPublishingTests in ITESTS/Pages/"
Task: "T035 DependencyExtractorMenuTests in UTESTS/Pages/"
Task: "T036 MenuModelTests in UTESTS/Rendering/Navigation/"
Task: "T037 NavigationGoldenTests in UTESTS/Rendering/Navigation/"
Task: "T038 NavScriptTests in UTESTS/Rendering/Navigation/"
Task: "T040 NavigationBlock.spec.ts in ADMINTESTS/editor/blocks/"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. Menus can be built, stored and reviewed.

US1 alone shows nothing to visitors. The first demo that visitors can see is US1 + US2,
which is the smallest useful feature (spec, User Story 2's priority note).

### Incremental Delivery

1. Setup and Foundational.
2. US1: build menus. **Demo in the admin.**
3. US2: publish them and show them with the Navigation block. **Demo on the site.**
4. US3 (reordering) beside US4 (link integrity).
5. US5 (active state).
6. Polish, the manual quickstart run, and the PR closing #24.

One PR, as with 010–017. There is no feature flag mechanism. If the PR must split, follow
plan.md's Principle XI order: (1) US1; (2) US2, US4, US5; (3) US3.

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US5.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #24.
- **Contract changes** (T002–T007) land in the same PR as their tests (Development Workflow).
- **Avoid**:
  - a second publishing path for menus (reuse `PublishingWrites`);
  - caching menus on their own;
  - raw SQL;
  - scripts on pages without a Navigation block, or a CSP `script-src` other than the hash;
  - an ARIA `menu` widget for site navigation (NV-12);
  - menu names, labels or addresses in logs.
