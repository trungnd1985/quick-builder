# Implementation Plan: Navigation and Menus

**Branch**: `024-navigation` | **Date**: 2026-10-14, extended 2026-10-20 (menu styles) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-navigation/spec.md`

**Issue**: #24.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none.

## Summary

Designers build named menus of up to three levels once per site. Entries point at pages,
collection items, external addresses, anchors, or nothing. Menus are drafted, published,
scheduled and rolled back like components. A new Navigation block shows a menu in any page or
component. Published pages render the menu on the server, with links that follow their
targets, entries that disappear when a target is not live, and the visitor's place marked.
Every change to a menu or its targets refreshes exactly the pages that show it.

The approach, detailed in [research.md](./research.md):
- **No new module** (NV-01): menus are a third publishing subject (`menu`) in **Pages**,
  reusing publications, versions, schedules, the publish log and the dependency graph.
  **Rendering** renders them. **Cms** is only read.
- **Storage** (NV-02 to NV-04):
  - `menus` and `menu_drafts`, mirroring components;
  - a new versioned contract, `docs/contracts/menu.md` v1, that both validators embed;
  - two indexes, `menu_targets` (the draft's targets) and `menu_usages` (drafts that show a
    menu);
  - only new or changed targets are checked against the site, so a deleted page never makes a
    menu unsaveable.
- **Navigation block** (NV-05): one additive page-schema node, `navigation`. Its `menuId` may
  dangle in drafts, and publishing refuses it.
- **Guards** (NV-06): pages and components cannot be published or rolled back with a menu that
  is not live. A menu cannot be unpublished or deleted while live content shows it
  (clarification 3, mirroring 012 for components).
- **Refresh** (NV-07, NV-08):
  - `DependencyKinds` gains `menu` and `item`;
  - `GenerationBump` walks one more hop (target → menu → page or component → page);
  - a new handler turns item publish, unpublish and delete events into `(item, id)` bumps.
- **Public output** (NV-09 to NV-15):
  - the menu is rendered inside each page's render and cached with it, so active state is
    per page;
  - active state is a pure function of resolved paths;
  - submenus are native `details`/`summary` disclosures that work without scripts;
  - collapsing follows the stylesheet's mobile and tablet container queries;
  - one inline script of at most 2 KB, pinned by hash in the CSP of pages that show a menu,
    adds Escape, outside-click and width handling.
- **Admin** (NV-16, NV-17):
  - a Menus list and a tree editor with native drag and drop and 011's keyboard moves;
  - an entry dialog with page and item pickers;
  - the Navigation block with a menu picker and collapse controls, drawing the draft on the
    canvas.
- **Menu styles** (added 2026-10-20, FR-017a to FR-017d; NV-25 to NV-31):
  - a site-wide `menuStyle` in the design tokens (look and eight colour token ids), additive
    in design-tokens v1, edited in a new Menus section of the Design view and published with
    the design;
  - per-block overrides as Navigation props **without schema defaults**, so absent means
    "follow the site";
  - CSS in two layers: the token CSS emits `--qb-menu-*`, the block writes `--qb-nav-*` only
    for its overrides, and the stylesheet reads `var(--qb-nav-x, var(--qb-menu-x))`;
  - the look as a `qb-nav--look-*` class chosen by the renderer (style queries are not
    available in Firefox);
  - visited entries styled like unvisited ones, a focus ring that cannot be removed, and
    contrast warnings computed in the admin only.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 + Npgsql, the in-house outbox and dispatcher, the
  in-house `SchemaSubsetValidator`, `System.Text.Json`.
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n, native HTML5 drag and drop.
- **Nothing new** (NV-24).

**Storage**: PostgreSQL 16+.
- **Pages** migration `AddMenus`: **+** `menus`, **+** `menu_drafts`, **+** `menu_targets`,
  **+** `menu_usages`, **+** `menu_changes`.
- Existing publishing tables take `subject_kind = 'menu'` with no column change; a kind check
  constraint, if any, is widened (NV-22).
- **No document or data rewrite.**

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. Suites are in research NV-23. The main ones:
- `MenuSchemaGoldenTests` with `menuSchema.spec.ts`, and `NavigationSchemaGoldenTests`;
- `MenuModelTests` (SC-003), `ActiveStateTests` (SC-004) and `NavigationGoldenTests` (SC-007);
- `NavScriptTests` (size, hash, CSP);
- `MenuEndpointsTests`, `MenuPublishingTests` and `MenuReferencesTests`;
- `MenuInvalidationTests` (SC-002, two instances);
- `MenuIsolationProbe`;
- `UpgradeFrom017Tests`;
- `MenuRenderPerformanceTests` (SC-005);
- `menuOps.spec.ts` and the admin specs with axe (SC-006).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend, the Vue admin SPA, and the
server-rendered public site.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Render** (SC-005): a page with two 300-entry menus stays within 012's budgets: p95 under
  200 ms on a miss and under 20 ms on a hit. A miss does at most three more batched reads.
- **Freshness** (SC-002): a menu or target change reaches every instance within 5 s, through
  the existing generation path.
- **Published JS**: at most 2 KB inline, only on pages that show a menu (FR-020's ceiling is
  10 KB).

**Constraints**:
- **Published only**: the public path reads live menus on the read-only connection
  (Principle VII).
- **Scripts**: core menu behaviour works without scripts. The one script is admitted by hash
  only where it is used.
- **Logs**: ids and counts only; no menu names, labels, anchors or addresses in logs or audit
  rows.
- **Offline**: no outbound requests.

**Scale/Scope**:
- **Code**: 0 new projects.
- **Migrations**: 1. Tables: 5 new, 0 changed.
- **Documents**: 1 new contract (`menu.md` v1); page schema gains 1 node type.
- **Endpoints**: 9 new admin operations (list, create, get, rename, delete, save draft, preview, usages, references), plus 6 publishing operations on the new subject kind;
  publish, rollback and schedule runs gain 2 refusals.
- **Readers**: 1 new method; `PageLinkTarget` gains 1 optional field.
- **Events**: 0 new; 3 new handlers in Pages.
- **Capabilities**: 1 new. **Configuration keys**: 0.
- **Admin**: about 10 components and 4 TypeScript modules.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module** (NV-01); the module list is unchanged. <br>• **Slices**: Pages `Features/Menus/` (list, get, create/duplicate, rename, delete, save draft, preview, usages, references), `Features/RefreshPagesShowingMenuItems/` (event handler), and the existing `Publish/`, `Unpublish/`, `Rollback/`, `Schedules/` and `PublishingHistory/` gain menu routes. Rendering `Rendering/Navigation/`. Controllers stay thin and use in-house handlers. <br>• **Rich domain model**: a `Menu` aggregate (name, limit, events), and pure `MenuRules` (document rules) and `MenuTree` operations, unit-tested. <br>• **Cross-module**: no new reference edge (module-surface.md §1). Item changes arrive through the outbox. No transaction spans modules. <br>• **Enforcement**: `ModuleBoundaryTests` unchanged and passing; `AuthorizationDeclarationTests` cover the new endpoints. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys. One forward-only, automatic migration. Health checks are unchanged. |
| III | Upgrades Never Lose Data | **Complies.** <br>• `UpgradeFrom017Tests`: no stored data rewritten, and pages without menus render the same body and CSP. <br>• **Site export (#20)**: obligations recorded (module-surface §5). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. No new cache entries. Multiple instances stay correct because refresh is by generation, read per request (012 PR-05). |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Site ownership**: all five tables carry `site_id` with the global filter. <br>• **Cross-site ids**: new targets are checked against the site (menu rule 8), and public reads are site-filtered. <br>• **Isolation suite**: + `MenuIsolationProbe`. <br>• **Raw SQL**: none. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **New contract**: `menu.md` v1 with schema, examples and a shared golden suite on both validators, plus `MenuRendererCoverageTests`. <br>• **Page schema**: `navigation` is an additive node type (v1, §6). <br>• **Block definition**: one `$defs` entry, one editor folder, one renderer; no other block changes (Principle VI's last rule). |
| VII | Draft and Published Are Separate Worlds | **Complies.** <br>• Menus have drafts and published versions (clarification 1); visitors see only live versions. <br>• Publish, unpublish and rollback are atomic and logged (012's transaction). <br>• Scheduled menu publishes run through 012's idempotent schedule worker. <br>• The public path reads menus through the read-only reader. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** <br>• **Server-side**: links, pruning, active state and starting state are rendered on the server; the script is enhancement only (NV-12 to NV-15). <br>• **Dependency graph**: extended with page/component → menu → page, item and template (NV-07), recorded at publish. <br>• **Exact refresh**: `MenuInvalidationTests` on two instances. <br>• **Budgets**: `MenuRenderPerformanceTests`. Script at most 2 KB, under the 100 KB page budget. |
| IX | Secure by Default | **Complies.** <br>• **Encoding**: labels, names, addresses and button texts go through `HtmlWriter`. <br>• **Schemes**: `url` targets use the page-link schemes; protocol-relative and `javascript:` are refused in both validators. <br>• **New tab**: `rel="noopener noreferrer"`. <br>• **CSP**: the script is admitted by its SHA-256 only, on pages that carry it; other pages keep 012's no-script policy. <br>• **Authorization**: server-side policies (`pages.menus.manage`, existing publishing capabilities). <br>• **Input bounds**: document size, entries, depth and label lengths. <br>• **Privacy**: no content in logs or audit rows. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests for every new and changed operation. <br>• **Contracts**: shared golden suites for the menu schema and the `navigation` node. <br>• **Rendering**: golden HTML for the Navigation block in every layout and state. <br>• **Coverage**: 80 % line coverage on the publish guards, the graph walk, menu resolution and active state. <br>• **Smoke test**: the release journey gains a menu. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies**. <br>• **Not built**: no new module, no menu fragment cache, no menu events, no automatic menus, no ARIA menu widget, no external script file. <br>• **Reuse**: 012's publishing for the whole menu lifecycle, the page picker, 011's drag model and keys, and the publishing UI. <br>• **Delivery**: one PR. If it must split: (1) menus, the contract and publishing (US1); (2) the Navigation block, rendering and refresh (US2, US4, US5); (3) drag and drop (US3). Each is usable alone. |

**Technology Constraints**:
- MVC controllers only; EF Core only, with no raw SQL.
- No Node process at run time.
- No new admin libraries.
- Editor canvas: in-house Vue components with native drag and drop and keyboard equivalents;
  container queries shared with published pages (constitution, Editor canvas).

**Development Workflow**:
- Issue #24 → `specs/024-navigation` → branch `024-navigation` → one PR that closes #24.
- Clarify has run: 2 questions at specify and 4 in clarify, all answered. Analyze follows
  tasks.
- **Contract and registry changes in the same PR**:
  - `docs/contracts/menu.md` (+), with `menu/v1.schema.json` and examples;
  - `docs/contracts/page-schema.md` (§2, §6) and `page-schema/v1.schema.json`, with golden
    examples;
  - `docs/contracts/README.md` (lists the new contract);
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (new consumers; no menu
    events);
  - `specs/002-identity-setup/contracts/authorization.md` §2 (`pages.menus.manage`);
  - `specs/012-publishing-rendering/contracts/public-rendering.md`, `readers.md` and
    `publishing-api.yaml` (menu subject kind, Navigation section, CSP note);
  - `docs/contributing.md` (the block checklist names the menu contract for blocks that read
    it), `docs/backlog.md` (#20 obligations);
  - **menu styles**: `docs/contracts/design-tokens.md` §2 and `design-tokens/v1.schema.json`
    (`menuStyle`, from [contracts/menu-style.md](./contracts/menu-style.md)), with golden
    examples and a `token-css` parity fixture; the `navigation` props in page-schema.md and
    its schema.

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 1, additive and automatic: Pages `AddMenus`.
- **New dependencies**: none.
- **Upgrade notes**:
  1. Designers can build menus under **Menus** and place them with the new Navigation block.
     Existing button-based navigation keeps working.
  2. Pages that show a Navigation block carry a small inline script, allowed by its hash in
     the page's Content-Security-Policy. A reverse proxy that rewrites or replaces the CSP
     must keep `script-src` for those pages, or menus lose Escape and collapse handling (they
     stay usable).
  3. Every cached page renders once after the upgrade (new stylesheet).

**Post-Phase-1 re-check**:
- `menus-api.yaml` declares a capability on every operation;
- `module-surface.md` adds no reference edge;
- the five new tables have `site_id` and the filter;
- the public reader is read-only;
- no event carries content;
- the page-schema change is additive under §6;
- the menu contract has examples for every rule;
- no raw SQL;
- the CSP changes only on pages with a Navigation block.

**Gate: PASS** (no new exception).

**Re-check for menu styles (2026-10-20)**:

| # | Principle | Effect of the addition |
|---|---|---|
| I | Modular monolith | No new reference edge: Rendering already reads the published design through Editor.Contracts; the token validator change is in the Editor module. |
| II, III | Install, upgrade | No migration and no configuration key: `menuStyle` lives inside the stored token set. A set without it renders as before; `UpgradeFrom017Tests` stays green. |
| VI | Versioned contracts | Additive in design-tokens v1 (like `contentWidths`) and in page-schema v1; golden examples on both validators; `TokenCss` and `tokenCss.ts` kept equal by a shared fixture. The `navigation` props lose their defaults, which is allowed because the node is unreleased. |
| VII | Draft and published | Site styles reach visitors only with a design publish; block styles only with the page or component publish. |
| VIII | Rendering | Server-rendered classes and custom properties; no script; a design publish already re-renders every page (design number in the render key). |
| IX | Secure by default | Values are token ids validated by pattern and written only as `var(--qb-color-<id>)`; no raw colour from a block reaches CSS. |
| X | Tests | NV-31: contract examples, parity fixture, golden HTML per look, the stylesheet cascade spec, contrast unit tests and axe. |
| XI | Simplicity | Four looks and eight colours, no per-state free styling, no visited colour, no server contrast check. |

**Gate: PASS** (no new exception).

## Project Structure

### Documentation (this feature)

```text
specs/024-navigation/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 6 questions)
├── research.md                # Phase 0: decisions NV-01 … NV-24
├── data-model.md              # Phase 1: tables, documents, graph, states, guards, errors
├── quickstart.md              # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── menu-document.md       # Draft of docs/contracts/menu.md
│   ├── navigation-block.md    # Draft of the page-schema addition
│   ├── menus-api.yaml         # HTTP: menus, drafts, preview, usages, references, publishing
│   ├── public-navigation.md   # Resolution, active state, HTML, stylesheet, script, CSP
│   └── module-surface.md      # Readers, events, capabilities, export obligations
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
docs/
+ contracts/menu.md  contracts/menu/v1.schema.json  contracts/menu/examples/
~ contracts/page-schema.md  contracts/page-schema/v1.schema.json  contracts/page-schema/examples/
~ contracts/README.md  contributing.md  backlog.md

src/Modules/
├── Pages/
│   ├── QuickBuilder.Pages.Contracts/
│   │   ~ Publishing/IPublishedContentReader.cs     # GetLiveMenusAsync (LiveMenu); PageLinkTarget.LiveTitle
│   │   + MenuSchema.cs                             # embeds menu/v1.schema.json
│   │   ~ PageSchema.cs                             # embeds the updated page schema
│   └── QuickBuilder.Pages/
│       + Domain/Menus/Menu.cs  MenuDraft.cs  MenuTarget.cs  MenuUsage.cs  MenuChange.cs
│       + Domain/Menus/MenuRules.cs  MenuTargets.cs # document rules, target extraction
│       ~ Domain/PublishingTerms.cs                 # SubjectKinds.Menu, DependencyKinds.Menu/Item
│       ~ Domain/DependencyExtractor.cs             # navigation → menu; Menus(document)
│       + Infrastructure/Schema/MenuDocumentValidator.cs
│       + Infrastructure/MenuWrites.cs  MenuReads.cs  MenuTargetStates.cs
│       ~ Infrastructure/PublishingWrites.cs        # menu subject; unpublished/missing menu guards; in-use guard
│       ~ Infrastructure/GenerationBump.cs          # the menu hop
│       ~ Infrastructure/DraftWrites.cs             # menu_usages
│       ~ Infrastructure/ScheduleWrites.cs  PublicationReads.cs  PagesOutbox.cs   # menu kind
│       ~ Infrastructure/PublishedContentReader.cs  PublishedReadDbContext.cs  PagesDbContext.cs
│       + Infrastructure/Migrations/<ts>_AddMenus.cs
│       + Features/Menus/ListMenus.cs  GetMenu.cs  CreateMenu.cs  RenameMenu.cs  DeleteMenu.cs
│       +                SaveMenuDraft.cs  PreviewMenu.cs  MenuUsages.cs  MenuReferences.cs  MenuResponses.cs
│       + Features/RefreshPagesShowingMenuItems/RefreshPagesShowingMenuItemsHandler.cs
│       ~ Features/Publish/  Unpublish/  Rollback/  Schedules/  PublishingHistory/   # menu routes
│       ~ PagesCapabilities.cs  PagesErrors.cs  PagesErrorResults.cs  PagesModule.cs
└── Rendering/QuickBuilder.Rendering/
    + Rendering/Navigation/MenuModel.cs  ResolvedEntry.cs  ActiveState.cs  NavScript.cs
    + Rendering/Blocks/NavigationRenderer.cs
    ~ Rendering/PageShell.cs                        # inline script after the first nav
    ~ Domain/RenderReferences.cs  RenderInputs.cs  RenderContext.cs
    ~ Features/RenderPage/PageResponder.cs          # menu reads; CSP script-src when needed
    ~ Features/NotFound/PublicTexts.cs              # "Menu", "Show submenu", "(opens in a new tab)"
    ~ RenderingModule.cs

src/admin/src/
+ editor/blocks/navigation/definition.ts  NavigationBlock.vue
+ editor/panel/controls/MenuControl.vue  NavCollapseControl.vue
~ editor/blocks/registry.ts  editor/blocks/blocks.css  editor/schema/schema.ts
+ editor/schema/menuValidator.ts
+ features/menus/MenusView.vue  MenuEditorView.vue  MenuEntryTree.vue  MenuEntryDialog.vue
+ features/menus/menuOps.ts  menuErrors.ts  useMenuHistory.ts
+ components/PagePicker.vue  ItemPicker.vue      # PagePicker generalises RedirectToPagePicker
~ components/RedirectToPagePicker.vue            # wraps PagePicker
~ features/publishing/PublishMenu.vue  HistoryView.vue  ScheduleDialog.vue  errorMessages.ts
~ features/pages/PageTreeView.vue  features/cms/items/DeleteItemDialog.vue  ItemPublishBar.vue   # FR-026 lists
~ features/sites/SiteDetailView.vue  app/router.ts  api/publishing.ts  i18n/en.json
+ api/menus.ts
src/admin/tests/
+ features/menus/*.spec.ts  menuOps.spec.ts  menuSchema.spec.ts (golden)
+ editor/blocks/navigation.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/            # AuthorizationDeclarationTests
+ QuickBuilder.UnitTests/Pages/Menus/        # MenuRulesTests, MenuSchemaGoldenTests, DependencyExtractor menu cases
+ QuickBuilder.UnitTests/Rendering/Navigation/   # MenuModelTests, ActiveStateTests, NavigationGoldenTests, NavScriptTests
~ QuickBuilder.UnitTests/Pages/              # NavigationSchemaGoldenTests, PagesOutboxTests
~ QuickBuilder.IntegrationTests/
│   + Pages/MenuEndpointsTests.cs  MenuPublishingTests.cs  MenuReferencesTests.cs
│   + Rendering/MenuInvalidationTests.cs  NavigationRenderTests.cs  UpgradeFrom017Tests.cs
│   ~ Isolation/Probes/ (+ MenuIsolationProbe)  Infrastructure/EndpointCatalog.cs
~ QuickBuilder.SmokeTests/                   # + MenuRenderPerformanceTests; journey: menu in header
specs/001-foundation-skeleton/contracts/integration-events.md   # ~
specs/002-identity-setup/contracts/authorization.md             # ~
specs/012-publishing-rendering/contracts/public-rendering.md  readers.md  publishing-api.yaml   # ~
```

Menu styles (added 2026-10-20; NV-25 to NV-31):

```text
docs/contracts/
~ design-tokens.md  design-tokens/v1.schema.json  design-tokens/examples/   # menuStyle
~ page-schema/v1.schema.json  page-schema/examples/                         # navigation style props

src/Modules/
├── Editor/QuickBuilder.Editor/Infrastructure/TokenSetValidator.cs   # ~ embeds the updated schema
└── Rendering/QuickBuilder.Rendering/
    ~ Domain/TokenCss.cs                     # --qb-menu-* properties
    ~ Rendering/Blocks/NavigationRenderer.cs # look class; --qb-nav-* only for present props

src/admin/src/
~ editor/tokens/tokenCss.ts  tokenValidator.ts   # --qb-menu-*; menuStyle validation
+ editor/tokens/contrast.ts                      # WCAG ratios, compositing (NV-29)
~ editor/blocks/blocks.css                       # looks, two-layer colours, visited, focus
~ editor/blocks/navigation/definition.ts  NavigationBlock.vue   # style props; look class
+ editor/panel/controls/MenuLookControl.vue      # look picker with "site default"
~ editor/panel/PropertyPanel.vue                 # contrast warnings for the block
~ features/design/DesignTokensView.vue           # + Menus section
+ features/design/MenuStyleSection.vue

tests/
~ fixtures/rendering/token-css/ (+ menu-style.json)  fixtures/rendering/golden/
~ QuickBuilder.UnitTests/Rendering/Navigation/NavigationGoldenTests.cs
~ QuickBuilder.IntegrationTests/Editor/DesignTokenContractTests.cs (menuStyle examples)
src/admin/tests/
~ editor/tokenCss.parity.spec.ts  + editor/contrast.spec.ts
~ editor/navCollapseCss.spec.ts → navStyleCss.spec.ts
+ design/MenuStyleSection.spec.ts  ~ editor/NavigationBlock.spec.ts
```

**Structure decision**:
- **Pages**: menus under `Domain/Menus/` and `Features/Menus/`, beside components, whose
  lifecycle they share.
- **Rendering**: a `Navigation/` folder for resolution, active state and the script, and one
  block renderer, like every other block.
- **Admin**: the menu screens in `features/menus/`, the block in `editor/blocks/navigation/`.
  The page and item pickers move to `components/` for reuse.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. Screen-reader and no-script checks of the published menu, and keyboard checks of the menu editor, are manual in quickstart §8, until #25. |

**New exceptions**: none.

## Risks

- **A proxy that overwrites the CSP** drops the script hash.
  - *Mitigation*: the menu stays usable without scripts (NV-12, NV-13); upgrade note 2.
- **`name` on `details`** is newer than the rest.
  - *Mitigation*: the script closes siblings where it is missing, and without either, several
    submenus can be open at once, which is harmless.
- **Refresh breadth**: a menu in the header is shown by every page, so a menu publish, or a
  publish of any page it targets, refreshes every page.
  - *Mitigation*: this is the correct set (FR-032). Re-renders happen only on request, as for
    component publishes (012).
- **Item publish storms** (bulk publishing): one bump per item event.
  - *Mitigation*: bumps are set-based and deduplicated (`RefreshOnce`). The cost is the same
    as 016's list refresh.
- **Flash of an open menu on phones** if the inline script is blocked.
  - *Mitigation*: the panel is written open by design, so the fallback shows the menu rather
    than hiding it.

## Deferred (recorded, not built)

- Menus built automatically from the page tree or a collection.
- Mega menus, icons, per-entry styles, per-role menus, localised menus.
- Anchor names on blocks and anchor checking (clarification 2).
- Integration events for menu changes (no consumer yet).
- Breadcrumb blocks (017 has the structured data).
