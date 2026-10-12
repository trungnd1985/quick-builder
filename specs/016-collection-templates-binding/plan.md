# Implementation Plan: Collection Templates and Data Binding

**Branch**: `016-collection-templates-binding` | **Date**: 2026-10-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-collection-templates-binding/spec.md`

**Issue**: #16.
- **Open exceptions from earlier plans**: E-5 (unchanged) and E-6 (014, 015). **This
  feature closes E-6** (TB-10).
- **New exceptions**: none.

## Summary

A Designer makes a page the template of a collection, and every published item gets its own
address, such as `/blog/hello-world`. Block properties bind to item fields in the property
panel, and a Collection list block shows filtered, sorted, limited and paginated items, with
binding scopes that nest. Visitors see only published data, served from the cache, and pages
refresh exactly when an item, template or schema they depend on changes.

The approach, detailed in [research.md](./research.md):
- **No new module** (TB-01):
  - **Pages**: a page becomes a template through `pages.template_collection_id`. Its `path`
    becomes the pattern `/blog/{slug}`, so the existing unique path index gives one template
    per parent (TB-02).
  - **Page documents**: bindings and lists live in them, so drafts, versions, rollback and
    scheduling come from 010–012 unchanged.
- **Binding format** (TB-04, TB-05, TB-07):
  - **Shape**: structured JSON in an optional `props.bind`, keyed by property, holding a
    field id or built-in value, an optional single `via` reference step, a scope, a format,
    and an empty-value choice.
  - **Version**: the envelope carries `bindingVersion: 1`, and the page schema stays at v1.
  - **New node types**: `collection-list`, with `list-card` and `list-empty` children.
  - **Where it is defined**: `docs/contracts/binding-expression.md`, with `$defs` in the one
    embedded page schema file.
- **Validation** (TB-06): page-schema rules 13–20 (binding version, unknown collection,
  unknown scope, page scope in components, list depth and count, page parameters, list
  children). Incompatible or stale bindings are *broken*, not refused: the editor marks them
  and the renderer treats them as empty.
- **Public path** (TB-03, TB-08, TB-09):
  - **Address matching**: reserved routes, then a static page, then a template with a
    published item by slug, then not found.
  - **Reads**: Cms exposes `IPublishedItemReader` on a read-only connection.
  - **Rendering**: Rendering plans, batch-fetches and resolves bindings and list queries
    before rendering, with at most 200 list queries per render.
- **Caching** (TB-10, TB-11):
  - **Item pages** are keyed by the template generation and the item's new
    `cms.items.render_stamp`. Cms bumps the stamp for the item and for the items whose
    published data references it.
  - **Dependencies**: pages record `collection` and `template` dependencies at publish.
  - **Refresh**: Pages handlers for Cms item, collection and media events bump list pages.
    For schema changes they also bump templates (**closes E-6**).
  - **Paging**: named query parameters, capped at 10,000, become part of the key. Every
    other parameter is ignored.
- **Guards** (TB-12, TB-13):
  - a new `pages.collection_uses` table lets Cms refuse to delete a collection that pages
    use (clarification 2026-10-10);
  - Cms warns about bound fields before a schema change;
  - moves and template changes warn how many item addresses change.
- **Editor** (TB-14):
  - a binding control and a text-template editor in the property panel;
  - list source, filter, sort and paging controls;
  - a per-member sample item shown with its draft values (clarification 2026-10-10);
  - a list preview endpoint over published data.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 + Npgsql (JSON translation), the in-house outbox
  and dispatcher.
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n, TipTap.
- **Nothing new** (TB-21).

**Storage**: PostgreSQL 16+.
- **Pages** migration `AddTemplates`:
  - **~** `pages`: + `template_collection_id` and a unique partial index;
  - **~** `publications`: + `page_param_names`;
  - **+** `collection_uses`.
- **Cms** migration `AddRenderStamp`:
  - **~** `items`: + `render_stamp` and a partial index on `(collection_id, published_slug)`.
- **No data backfill** (TB-18).

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. The suites are in research TB-19. The main ones:
- `BindingRulesGoldenTests` with `bindingRules.spec.ts` (shared golden examples);
- `PageSchemaBindingTests`;
- `RouteMatchingTests`;
- `BindingRenderGoldenTests` and `CollectionListRenderTests` (SC-003);
- `ItemPageInvalidationTests` (SC-005, SC-006);
- `BindingFailureTests` (SC-007);
- `CollectionDeleteGuardTests`;
- `ItemPagePerformanceTests` (SC-004);
- the admin specs with axe (SC-008).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend, the Vue admin SPA, and the
server-rendered public site.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Renders** (SC-004): an item page of 200 blocks with a list of 20, and a paginated list of
  100 over 10,000 items, each under 200 ms p95 on a cache miss and under 20 ms on a hit.
- **Freshness** (SC-005): within 5 s p95 and 30 s at most.
- **Exact refresh** (SC-006): one item publish refreshes only its page, the list pages and
  the pages showing it through a reference.

**Constraints**:
- **Published only**: the public path reads published data only, through read-only
  connections (Principle VII).
- **Never fail**: rendering never fails because of a binding (FR-038).
- **Logs**: no item values, names or slugs in logs or events.
- **No script** on published pages, including paging.
- **Offline**: zero outbound requests.

**Scale/Scope**:
- **Code**: 0 new projects.
- **Migrations**: 2. Tables: 1 new, 2 changed.
- **Node types**: 3 new. **Code rules**: 8 new.
- **Endpoints**: 4 new or changed admin endpoints, 3 changed responses.
- **Contracts readers**: 4 new interfaces, 2 extended.
- **Events**: 1 new, and 3 new or changed handlers.
- **Capabilities**: 0. **Configuration keys**: 0.
- **Admin**: about 14 components, 4 TypeScript modules and 3 block definitions.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module** (TB-01). Template state is in Pages, published item reads in Cms, resolution in Rendering, which still has no tables. <br>• **Slices**: Pages `Features/SetPageTemplate/` (impact, set, remove), `RefreshPagesListingItems/`, `RefreshPagesUsingCollection/`; Cms `Features/ListPreview/`. Controllers are thin, with in-house handlers. <br>• **Rich domain model**: `PageTree` enforces template rules. The `Item` aggregate bumps render stamps. Rendering's binding resolution is pure and unit-tested. <br>• **Cross-module**: new edges Pages → `Cms.Contracts` and Rendering → `Cms.Contracts`. Cms → `Pages.Contracts` already exists. No Contracts-to-Contracts edge, no cycle. Side effects travel through outbox events with idempotent handlers. No transaction spans modules; the collection-delete race is recorded (TB-13). <br>• **Enforcement**: `ModuleBoundaryTests` gains both edges. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys. Two forward-only, automatic migrations. Health checks are unchanged. |
| III | Upgrades Never Lose Data | **Complies.** <br>• `UpgradeFrom035Tests` proves every existing page renders the same body. <br>• No stored data is rewritten. <br>• Bindings and template state travel in page documents and page rows, which export and import (#20) already carry. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. The cache provider abstraction is unchanged, and the keys still live in `ICacheProvider`. Freshness across instances comes from keys read from the database, so no cache messages are needed (012 PR-05). |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Site ownership**: `collection_uses` and the new read context carry `site_id` with the global filter. <br>• **Explicit site predicates**: generation bumps keep them (`GenerationBumpSiteFilterTests` extended). <br>• **Cross-site ids**: rule 14 and `pages.unknown_collection` treat another site's collection as unknown. <br>• **Cache keys**: `ItemRenderKey` is under `CacheKey.ForSite`. <br>• **Isolation suite**: + `TemplateIsolationProbe`. <br>• **Raw SQL**: none new. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **Binding contract**: `docs/contracts/binding-expression.md`, with `$defs` in the one embedded page schema file that both validators use. <br>• **Version**: `bindingVersion: 1` on the document. The page schema stays at v1 because every change is additive (page-schema.md §6). <br>• **Golden examples**: shared by server and admin. <br>• **Field ids, never names**: kept across renames. <br>• **Block types**: the new block types and `bind` keys are defined once, in the schema, the editor definition and the renderer. <br>• **Later additive changes (2026-10-12)**: `paging.layout`, `heading.link` and rule 21 each came with the contract document, the schema and golden examples in the same change; they stay within v1. |
| VII | Draft and Published Are Separate Worlds | **Complies; closes E-6.** <br>• **Read-only public path**: Rendering reads published items only, through `IPublishedItemReader` on a read-only connection. <br>• **Editor**: the canvas shows a sample item's draft only in the admin (clarification). <br>• **Publish**: bindings and lists are part of page versions, so they publish, roll back and schedule atomically with the page. <br>• **E-6**: schema changes that rewrite published values now refresh every live page that shows them (TB-10). |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** <br>• **Server-side HTML**, including paging, with no script. <br>• **Dependency graph**: extended with page → collection and template → collection; template → items through render stamps. <br>• **Exact refresh**: proven by `ItemPageInvalidationTests` (SC-006), on two instances too. <br>• **Budgets**: measured by `ItemPagePerformanceTests`. <br>• **Cache keys**: bounded by ignoring unknown parameters and capping page numbers. <br>• **Lists in components** page from the address: their parameters are part of the page's key, so each page of a footer list is its own cached copy. <br>• **One more query per request**, cache hits included, reads the components' page parameters. It is measured by `ItemPagePerformanceTests`, with a recorded fallback (TB-11). |
| IX | Secure by Default | **Complies.** <br>• **Encoding**: every bound value is encoded for its context by the existing writers. <br>• **Rich text** goes through the allow-list and field formats. <br>• **Link schemes** are checked, and anything else renders as text. <br>• **Authorization**: server-side on every endpoint, with no new capability (TB-17). <br>• **Input bounds**: page-number and parameter bounds, list query caps and the 1 MiB document limit. <br>• **Privacy**: no values, names or slugs in logs. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests for every changed endpoint. <br>• **Contract**: golden tests for the binding contract on both sides. <br>• **Rendering**: golden HTML for binding resolution and lists. <br>• **Coverage**: 80 % line coverage on the render and publish paths. <br>• **Smoke test**: the release smoke journey gains a template. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies.** <br>• **Not built**: no expression language, no `LATERAL` SQL, no route table, no per-field indexes, no redirects. <br>• **Reuse**: the editor's panel, canvas, slot nodes and 015's picker are reused. <br>• **Delivery**: one PR. Templates (US1–US3) are useful before lists. There is no feature flag mechanism in the codebase yet, so the plan is one complete PR; if it must split, lists (US4–US6) come in a second PR, and the first PR must not register the `collection-list` block. |

**Re-check 2026-10-12**: the pager and details-page clarifications were checked against every principle; only VI and VIII changed.

**Technology Constraints**:
- MVC controllers only; EF Core only, with no new raw SQL.
- TipTap only for rich text.
- No iframe in the editor; native drag and drop with keyboard equivalents for list cards.
- No Node process at run time.

**Development Workflow**:
- Issue #16 → `specs/016-collection-templates-binding` → branch
  `016-collection-templates-binding` → one PR that closes #16.
- Clarify has run: 5 questions, all answered. Analyze follows tasks.
- **Contract and registry changes in the same PR**:
  - `docs/contracts/binding-expression.md` (+), with `binding-expression/examples/`;
  - `docs/contracts/page-schema.md` (§2 new types, §3 rules 13–20, §6 additions) and
    `page-schema/v1.schema.json`;
  - `docs/contracts/README.md` (lists the new contract);
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (new event and
    subscriptions);
  - `specs/002-identity-setup/contracts/authorization.md` §2 (notes);
  - `specs/012-publishing-rendering/contracts/readers.md` (reader additions);
  - 015 `contracts/module-surface.md` §5 (obligations met);
  - `docs/backlog.md` (E-6 closed; deferred items).

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 3, additive and automatic: Pages `AddTemplates`, Cms `AddRenderStamp`
  (which fills each published item's first publication time) and Cms `AddPublishedText`.
  The item index backfill runs once after start-up to fill the folded text that list
  filters search (TB-08); until it finishes, `contains` filters miss the items not yet
  indexed.
- **New dependencies**: none.
- **Upgrade notes**:
  1. Designers can make a page the template of a collection. Its items are then served at
     the template's address with their slug.
  2. The page editor gains the Collection list block and property binding. Editors can use
     both in page drafts.
  3. A collection that pages or components use can no longer be deleted. Remove the uses
     first.
  4. Changing a collection's schema now refreshes live pages that show it at once. Bindings
     to removed fields show their fallback.
  5. The block stylesheet changes, so every cached page renders once again after the
     upgrade.

**Post-Phase-1 re-check**:
- `templates-api.yaml` declares a capability on every operation;
- `module-surface.md` adds only implementation-to-Contracts edges, with no cycle;
- `collection_uses` has `site_id` and the filter, and the public read context is read-only;
- events carry ids only;
- every binding change is additive under page-schema.md §6;
- golden examples cover every compatibility row and operator;
- no new raw SQL.

**Gate: PASS** (E-6 closed, no new exception).

## Project Structure

### Documentation (this feature)

```text
specs/016-collection-templates-binding/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 5 questions)
├── research.md                # Phase 0: decisions TB-01 … TB-21
├── data-model.md              # Phase 1: tables, document additions, rules, states, errors, events, keys
├── quickstart.md              # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── binding-expression.md  # Draft of docs/contracts/binding-expression.md
│   ├── templates-api.yaml     # HTTP: template impact/set/remove, list preview, changed responses
│   ├── module-surface.md      # Readers, events, reference edges, obligations
│   └── public-rendering.md    # Address matching, paging parameters and markup, item pages
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
qb.ps1                                    # ~ seed-template: makes a template for a seeded collection

docs/
+ contracts/binding-expression.md
+ contracts/binding-expression/examples/{valid,invalid,resolve}/*.json
~ contracts/page-schema.md  contracts/page-schema/v1.schema.json  contracts/README.md
~ backlog.md                              # E-6 closed; nested-list LATERAL query, per-field indexes, visitor filters
~ operations.md                           # templates, collection delete guard, schema change refresh

src/Modules/
├── Pages/
│   ├── QuickBuilder.Pages.Contracts/
│   │   + ITemplateReader.cs  ICollectionUsageReader.cs  Events/PageTemplateChanged.cs
│   │   ~ Publishing/IPublishedContentReader.cs        # FindLiveTemplateAsync, GetLiveTemplatePatternsAsync
│   │   ~ PageSchema.cs                                # embeds the updated v1 schema
│   └── QuickBuilder.Pages/
│       ~ QuickBuilder.Pages.csproj                    # + Cms.Contracts
│       ~ Domain/Page.cs  PageTree.cs                  # TemplateCollectionId, template rules (TB-02)
│       ~ Domain/PublishingTerms.cs                    # DependencyKinds.Collection, .Template
│       ~ Domain/DependencyExtractor.cs                # list sources, page parameter slots, heading links
│       + Domain/CollectionUseExtractor.cs  CollectionUse.cs
│       ~ Infrastructure/PagesDbContext.cs  PublishedReadDbContext.cs  PublishedContentReader.cs
│       ~ Infrastructure/PublishingWrites.cs  DraftWrites.cs  ScheduleWrites.cs  PageTreeWrites.cs  GenerationBump.cs
│       ~ Infrastructure/Schema/PageDocumentValidator.cs   # rules 13–20
│       + Infrastructure/PageParamRules.cs             # rule 21, across a page and its components (FR-027)
│       ~ Infrastructure/PublishedContentReader.cs     # + the components' page parameters (TB-11)
│       + Infrastructure/TemplateReader.cs  CollectionUsageReader.cs  CollectionUses.cs
│       + Infrastructure/Migrations/<ts>_AddTemplates.cs
│       + Features/SetPageTemplate/SetPageTemplate.cs  GetTemplateImpact.cs
│       + Features/RefreshPagesListingItems/RefreshPagesListingItemsHandler.cs
│       + Features/RefreshPagesUsingCollection/RefreshPagesUsingCollectionHandler.cs
│       ~ Features/RefreshPagesShowingMedia/RefreshPagesShowingMediaHandler.cs
│       ~ Features/MovePage/  CreatePage/  UpdatePage/  PageResponses.cs   # warnings, template fields
│       ~ PagesErrors.cs  PagesErrorResults.cs  PagesModule.cs
├── Cms/
│   ├── QuickBuilder.Cms.Contracts/
│   │   + Publishing/IPublishedItemReader.cs  PublishedItem.cs  PublishedListQuery.cs
│   │   + ICollectionSchemaReader.cs
│   │   ~ Media/IItemMediaUsageReader.cs               # CollectionsShowingPublishedAsync
│   └── QuickBuilder.Cms/
│       ~ Domain/Item.cs                               # render stamp
│       ~ Infrastructure/ItemPublishing.cs  ItemWrites.cs  ItemMediaUsageReader.cs  CmsDbContext.cs
│       + Infrastructure/CmsPublishedReadDbContext.cs  PublishedItemReader.cs  PublishedListTranslation.cs
│       + Infrastructure/CollectionSchemaReader.cs  RenderStampBump.cs
│       + Infrastructure/Migrations/<ts>_AddRenderStamp.cs
│       + Features/ListPreview/ListPreview.cs
│       ~ Features/DeleteCollection/  PreviewCollection/  Items/GetItem/  Items/ReindexItemsForMedia/
│       ~ CmsErrors.cs  CmsErrorResults.cs  CmsModule.cs
└── Rendering/QuickBuilder.Rendering/
    ~ QuickBuilder.Rendering.csproj                    # + Cms.Contracts
    + Binding/BindingPlanner.cs  BindingFetcher.cs  BindingResolver.cs  BindingRules.cs
    +         ValueFormatter.cs  ScopeStack.cs  ResolvedData.cs
    + Rendering/Blocks/CollectionListRenderer.cs  Pager.cs
    ~ Rendering/Blocks/TextRenderers.cs  MediaRenderers.cs  LayoutRenderers.cs   # resolve bound props, hide; heading link (FR-014)
    ~ Rendering/BlockStylesheet.cs  RichTextRenderer.cs  LinkResolver.cs
    ~ Domain/RenderContext.cs  RenderInputs.cs  RenderReferences.cs  BlockDefaults.cs
    + Features/RenderPage/TemplateMatcher.cs  ItemRenderKey.cs  PageParams.cs
    ~ Features/RenderPage/PageResponder.cs  RenderPageController.cs  RenderPageQuery.cs  RenderKey.cs
    ~ Features/NotFound/PublicTexts.cs                 # pager labels

src/admin/src/
+ editor/bindings/scopes.ts  bindingRules.ts  resolve.ts  format.ts  listPreview.ts
+ editor/blocks/collection-list/definition.ts  CollectionListBlock.vue
+ editor/blocks/list-card/definition.ts  list-empty/definition.ts
~ editor/blocks/registry.ts  heading/  text/  button/  image/  rich-text/   # bindable props; heading link control
~ editor/blocks/registry.ts                       # InsertContext.nameFieldOf, initialChildren(t, context, props)
~ editor/blocks/blocks.css  canvas/canvas.css    # .qb-heading__link, .qb-pager--steps; stylesheet hash changes every golden
~ editor/bindings/bindingContext.ts               # first collection's schema loaded when the editor opens (default card)
~ editor/panel/controls/ListPagingControl.vue     # layout choice; names taken by the page's components
+ editor/panel/controls/BindingControl.vue  BindingPicker.vue  TextTemplateEditor.vue
+                       ListSourceControl.vue  ListFiltersControl.vue  ListSortControl.vue  ListPagingControl.vue
~ editor/panel/PropertyPanel.vue
+ editor/SampleItemPicker.vue
~ editor/EditorTopBar.vue  canvas/CanvasNode.vue  layers/  schema/schema.ts  schema/documentValidator.ts  state/editorStore.ts
~ api/pages.ts  api/cms.ts  api/items.ts
+ features/pages/TemplateDialog.vue
~ features/pages/ (page tree: template badge and pattern, move warnings)
~ features/cms/items/ItemFormView.vue          # address and reason
~ features/cms/CollectionBuilderView.vue  features/cms/CollectionsView.vue   # bound-field warning, delete refusal
~ i18n/en.json
src/admin/tests/
+ editor/bindings/*.spec.ts  bindingRules.spec.ts (golden)  editor/panel/BindingControl.spec.ts  List*.spec.ts
+ features/pages/TemplateDialog.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/         # Pages → Cms.Contracts, Rendering → Cms.Contracts; AuthorizationDeclarationTests
+ QuickBuilder.UnitTests/Rendering/Binding/   # BindingRulesGoldenTests, BindingResolverTests, ValueFormatterTests, PageParamsTests
+ QuickBuilder.UnitTests/Pages/               # TemplateTreeTests, CollectionUseExtractorTests, PageSchemaBindingTests
~ QuickBuilder.IntegrationTests/
│   + Pages/SetPageTemplateTests.cs  CollectionUsesTests.cs
│   + Rendering/RouteMatchingTests.cs  BindingRenderGoldenTests.cs  CollectionListRenderTests.cs
│   │           ItemPageInvalidationTests.cs  BindingFailureTests.cs  PagingTests.cs
│   + Cms/CollectionDeleteGuardTests.cs  ListPreviewTests.cs  RenderStampTests.cs
│   + Rendering/UpgradeFrom035Tests.cs  ItemPageCacheTests.cs  NestedScopeTests.cs
│   ~ Isolation/Probes/ (+ TemplateIsolationProbe)  Infrastructure/EndpointCatalog.cs
│   ~ Pages/GenerationBumpSiteFilterTests.cs
~ QuickBuilder.SmokeTests/                # + ItemPagePerformanceTests; journey: + template and item page
specs/001-foundation-skeleton/contracts/integration-events.md   # ~
specs/002-identity-setup/contracts/authorization.md             # ~ notes
specs/012-publishing-rendering/contracts/readers.md             # ~
specs/015-cms-items/contracts/module-surface.md                 # ~ obligations met
```

**Structure decision**:
- **Pages**: template state and collection usage, as page-tree rules and derived tables.
- **Cms**: published item reads and render stamps.
- **Rendering**: binding resolution in a new `Binding/` folder beside `Rendering/`, keeping
  block renderers synchronous and free of I/O.
- **Admin**: binding logic in `editor/bindings/`, controls in `editor/panel/controls/`.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. Screen-reader checks of the binding control are manual in quickstart §2.9, until #25. |
| **E-6** (schema changes rewrite published item data) | **Closed.** A schema change that rewrites values now refreshes every live page that binds to or lists the collection, item pages included (TB-10). `ItemPageInvalidationTests` and quickstart §3.5 prove it. `docs/backlog.md` moves E-6 to "closed by 016". |

**New exceptions**: none.

## Risks

- **Nested list queries** (TB-09): a list of 100 cards with an inner list filtered by the
  outer item costs up to 100 queries.
  - *Mitigation*: de-duplication by value, the cap of 200 queries per render, and SC-004
    measured with a nested list.
  - *Fallback* (recorded, not built): one `LATERAL` query per inner list, inside Cms.
- **`jsonb` filters and sorts over 10,000 published items** (TB-20): scans within one
  collection.
  - *Mitigation*: the collection-scoped indexes from 015, measured.
  - *Fallback*: expression indexes for sort keys designers use.
- **Over-refresh on busy collections**: a template with a "related posts" list refreshes
  every post page on any post publish. This is correct, and the re-renders happen lazily on
  the next request, but it lowers the hit rate on busy sites.
  - *Mitigation*: documented in `operations.md`. A future refinement can narrow it to lists
    whose filters could match the changed item.
- **Collection delete racing a draft save** (TB-13): no shared transaction.
  - *Mitigation*: rule 14 refuses the next save, rendering treats the use as empty, and
    `BindingFailureTests` covers it.
- **Admin and server disagreeing on compatibility or resolution**.
  - *Mitigation*: the shared golden examples, including `resolve/` cases, run by both
    suites.
- **Cache-key growth through page numbers**.
  - *Mitigation*: only declared parameters are read, numbers are capped at 10,000, and the
    existing sliding expiry and size accounting apply.

## Deferred (recorded, not built)

- Visitor filtering, sorting and search; filters relative to the current time.
- Redirects from old item addresses and SEO metadata templates (#17).
- More than one reference step; binding style props; binding components to the page item.
- `LATERAL` nested-list query; per-field expression indexes; narrowing list refreshes by
  filter.
- Draft preview links for item pages (#22).
