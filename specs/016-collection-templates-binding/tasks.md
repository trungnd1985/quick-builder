---

description: "Task list for 016-collection-templates-binding"
---

# Tasks: Collection Templates and Data Binding

**Input**: Design documents from `/specs/016-collection-templates-binding/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - golden files for binding resolution and for rendering every block type;
  - 80 % coverage on critical paths (render and publish are both critical).
- The spec measures behaviour through automated checks: SC-003 … SC-008.
- Within each story, test tasks come first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US7 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US7)
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
- `FIX` = `tests/fixtures`

**Conventions**:
- **Backend slices** live under `<MODULE>/Features/<Slice>/<Slice>.cs`. Every controller
  action:
  - is routed as in [contracts/templates-api.yaml](./contracts/templates-api.yaml);
  - declares exactly one `[RequireCapability(...)]` (research TB-17);
  - dispatches through `IDispatcher`;
  - refuses with the codes in [data-model.md §4](./data-model.md);
  - is registered in its module and listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **The public path** reads only through Contracts readers on read-only connections:
  `IPublishedContentReader`, `IPublishedItemReader`, `IPublishedDesignReader` and
  `IPublicMediaReader` (012 PR-06, TB-08). Block renderers stay synchronous and do no I/O.
  Data comes in through `RenderInputs` and `ResolvedData` (TB-09).
- **Page structure changes** (template set or remove) go through `PageTree` and
  `PAGES/Infrastructure/PageTreeWrites.cs`, with the page `version` check (010 FR-010).
- **Generation bumps** go through `PAGES/Infrastructure/GenerationBump.cs` with the explicit
  site predicate. Cross-module handlers dedupe through `pages.processed_events`, like
  `RefreshPagesShowingMediaHandler`.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task. Public texts (pager labels) go in
  `RENDER/Features/NotFound/PublicTexts.cs`.
- **Logs and events** carry ids, counts and codes only. They never contain item values,
  names or slugs.

**References**:
- decisions: `TB-xx` in [research.md](./research.md);
- tables, document rules, states, errors, events and cache keys:
  [data-model.md](./data-model.md);
- the binding format: [contracts/binding-expression.md](./contracts/binding-expression.md);
- endpoints: [contracts/templates-api.yaml](./contracts/templates-api.yaml);
- readers and events: [contracts/module-surface.md](./contracts/module-surface.md);
- matching, paging and lists on the site:
  [contracts/public-rendering.md](./contracts/public-rendering.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents and the new project references. No behaviour.

- [X] T001 Commit `specs/016-collection-templates-binding/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `016-collection-templates-binding`, referencing #16.
- [X] T002 Add project references from `PAGES/QuickBuilder.Pages.csproj` and `RENDER/QuickBuilder.Rendering.csproj` to `CMSC/QuickBuilder.Cms.Contracts.csproj`. In `ATESTS/ModuleBoundaryTests.cs`, allow `QuickBuilder.Pages` → `QuickBuilder.Cms.Contracts` and `QuickBuilder.Rendering` → `QuickBuilder.Cms.Contracts`, keep every other rule, and assert that no Contracts project references another module's Contracts (module-surface.md §1).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the binding contract, its schema `$defs` and its golden examples;
- validation rules 13–20 on both sides;
- the two migrations;
- the Contracts interfaces;
- the read-only published item reader;
- the binding resolver core;
- error codes and the new event type.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Binding contract (FR-013, TB-04 … TB-07)

- [X] T003 Create `CONTRACTS/binding-expression.md` from `specs/016-collection-templates-binding/contracts/binding-expression.md`, dropping the "(draft)" mark and the implementation note. Add it to `CONTRACTS/README.md`.
- [X] T004 Extend `CONTRACTS/page-schema/v1.schema.json`, staying at version 1:
  - an optional envelope `bindingVersion` (`const: 1`);
  - `$defs` `binding`, `bindingValue`, `textTemplate`, `valueFormat`, `listSource`, `listFilter`, `listSort` and `listPaging`, exactly as binding-expression.md §2 and §5;
  - an optional `bind` prop on `heading`, `text`, `button`, `image` and `rich-text`, whose object allows only the TB-05 keys (`additionalProperties: false`);
  - the node types `collection-list` (in `contentChild`) and `list-card` and `list-empty` (in a new `listChild` placement, admitted only under `collection-list`), with the props of data-model §2.

  Use only the vocabulary in page-schema.md §7. `PageSchemaSubsetTests` must pass.
- [X] T005 Update `CONTRACTS/page-schema.md`: the §2 table and placements for the three node types, `bind` in the props column, rules 13–20 in §3 with their order (rule 20 with the structural checks, then 13–19 after rule 12), and an "Additions in 016 (still version 1)" paragraph in §6. Run `ITESTS/Pages/PageSchemaContractTests.cs`; it must pass.
- [X] T006 [P] Create golden examples in `CONTRACTS/binding-expression/examples/`:
  - `valid/`: one document per bindable prop, a text template, `via`, every format, `empty: hide`, a list with every filter operator, sort, paging, a nested list and a multi-reference source;
  - `invalid/`: one per rule 13–20 and per schema failure in a `bind`, each with `expected.json` listing `rule` and `pointer`;
  - `resolve/`: cases holding a collection schema, published items and a document, with the expected value per node (`text`, `hidden`, `fallback`, `broken:<reason>`), covering every row of binding-expression.md §4 and every empty case.

### Validators, server and admin (TB-06)

- [X] T007 [P] Create `UTESTS/Pages/PageSchemaBindingTests.cs`. It runs every `valid/` and `invalid/` example through `PAGES/Infrastructure/Schema/PageDocumentValidator.cs` and expects the listed rule and pointer. Also check that a `"page"` scope in a non-template page document is accepted (TB-06). It must fail first.
- [X] T008 [P] Extend `ADMINTESTS/editor/documentValidator.spec.ts` to run the same `valid/` and `invalid/` examples through `EDITOR/schema/documentValidator.ts`. It must fail first.
- [X] T009 Implement rules 13 and 15–20 in `PAGES/Infrastructure/Schema/PageDocumentValidator.cs`, in data-model §2 order. Implement rule 14 through `ICollectionSchemaReader.ExistingAsync` (T013): an unknown id and another site's id give the same failure. Component documents also get rule 16. Make T007 pass.
- [X] T010 Mirror rules 13–20 in `EDITOR/schema/documentValidator.ts` and `EDITOR/schema/limits.ts`. Rule 14 checks against the site's collection ids, which the editor store passes in (`EDITOR/state/editorStore.ts` loads them through `ADMIN/api/cms.ts`). Make T008 pass.

### Database

- [X] T011 Extend `PAGES/Infrastructure/PagesDbContext.cs` and `PAGES/Domain/Page.cs`:
  - `Page.TemplateCollectionId` (`Guid?`, private setter);
  - the unique partial index on `(site_id, template_collection_id)`;
  - a new `CollectionUse` entity in `PAGES/Domain/CollectionUse.cs` mapped to `collection_uses`, with the key and index of data-model §1.3 and the global site filter;
  - `publications.page_param_names` (`text[]`, default empty), the paging parameters of the live version (data-model §1.2a, used by T073).

  Add `DependencyKinds.Collection = "collection"` and `DependencyKinds.Template = "template"` to `PAGES/Domain/PublishingTerms.cs`. Generate `PAGES/Infrastructure/Migrations/<ts>_AddTemplates.cs` with `dotnet ef migrations add AddTemplates` and check it against data-model §1.1 and §1.3.
- [X] T012 [P] Add `RenderStamp` (`long`, starts at 1) to `CMS/Domain/Item.cs` and map it in `CMS/Infrastructure/CmsDbContext.cs`, with the partial index `ix_items_collection_id_published_slug`. Generate `CMS/Infrastructure/Migrations/<ts>_AddRenderStamp.cs` and check it against data-model §1.4.

### Contracts interfaces (module-surface.md §2, §3)

- [X] T013 [P] Create, in `CMSC`:
  - `Publishing/IPublishedItemReader.cs`, `Publishing/PublishedItem.cs` (the records `PublishedItemRef`, `PublishedItem`, `PublishedItemPage`) and `Publishing/PublishedListQuery.cs` (`PublishedListQuery`, `ListFilter`, `ListSort`, `ListOperand`, operator and built-in enums matching binding-expression.md §5);
  - `ICollectionSchemaReader.cs`;
  - `CollectionsShowingPublishedAsync` on `Media/IItemMediaUsageReader.cs`.

  XML docs name TB-08 and say that only published data is visible.
- [X] T014 [P] Create, in `PAGESC`:
  - `ITemplateReader.cs` (`TemplateInfo`);
  - `ICollectionUsageReader.cs` (`CollectionUse`, `CollectionUses`);
  - `Events/PageTemplateChanged.cs`, with event type `Pages.PageTemplateChanged`.

  Add `FindLiveTemplateAsync` (`LiveTemplateRef`) and `GetLiveTemplatePatternsAsync` to `Publishing/IPublishedContentReader.cs`.

### Published item reader, Cms (TB-08)

- [X] T015 Create `CMS/Infrastructure/CmsPublishedReadDbContext.cs`, following `PAGES/Infrastructure/PublishedReadDbContext.cs`:
  - the read-only connection helper;
  - `NoTracking`;
  - `SaveChanges` that throws;
  - row types for `items` (published columns only), `item_references` (location `published` only) and `collections` (id, site, schema).

  Register it in `CMS/CmsModule.cs`.
- [X] T016 [P] Create `ITESTS/Cms/PublishedItemReaderTests.cs`. It must fail first, and covers:
  - `FindBySlugAsync` finds only published slugs, ignores draft-only items and is exact on the lower-case slug;
  - `GetAsync` returns published values, never draft values;
  - `GetSchemasAsync` returns the current schemas;
  - `CountPublishedAsync`;
  - every method returns nothing for another site's ids;
  - a write through the context fails with SQLSTATE `25006`.
- [X] T017 Implement `CMS/Infrastructure/PublishedItemReader.cs`: `FindBySlugAsync`, `GetAsync`, `GetSchemasAsync` and `CountPublishedAsync`. `QueryAsync` throws `NotImplementedException` until T064. Also implement `CMS/Infrastructure/CollectionSchemaReader.cs` (`ExistingAsync`, `GetSchemasAsync`, `CollectionsReferencingAsync`, which scans the site's schemas for `reference` and `multiReference` fields targeting the id). Register both in `CMS/CmsModule.cs`. Make T016 pass for the implemented methods.

### Binding resolver core, Rendering (TB-09)

- [X] T018 [P] Create `UTESTS/Rendering/Binding/BindingRulesGoldenTests.cs`. It must fail first, and runs every `CONTRACTS/binding-expression/examples/resolve/` case through `BindingRules` and `BindingResolver`, expecting the listed values.
- [X] T019 [P] Create `ADMINTESTS/editor/bindings/bindingRules.spec.ts`, which runs the same `resolve/` cases through `EDITOR/bindings/bindingRules.ts` and `EDITOR/bindings/resolve.ts`. It must fail first.
- [X] T020 Create in `RENDER/Binding/`:
  - `BindingRules.cs`: the compatibility table of binding-expression.md §4 and broken reasons `field_missing`, `type_mismatch`, `scope_missing`, `via_not_reference`;
  - `ScopeStack.cs`: `"page"` and list scopes, nearest first;
  - `ResolvedData.cs`: items, schemas, list results keyed by list node and outer-item key, and referenced items;
  - `BindingResolver.cs`: `Resolve(node, prop, context)` → `Value | Fallback | Hide`, following data-model §3.3.

  `ValueFormatter.cs` starts with plain values only (text, option label, reference name, slug); formats and text templates come in US2. Make T018 pass for the non-format cases.
- [X] T021 Create `EDITOR/bindings/bindingRules.ts`, `EDITOR/bindings/scopes.ts` and `EDITOR/bindings/resolve.ts`, mirroring T020. Make T019 pass for the same cases.
- [X] T022 Extend `RENDER/Domain/RenderContext.cs` and `RENDER/Domain/RenderInputs.cs` with `ResolvedData`, the scope stack (push and pop around list cards) and `Resolve(...)`. Change the renderers of the bindable props, `heading`, `text` and `button` in `RENDER/Rendering/Blocks/TextRenderers.cs` and `image` and `rich-text` in `MediaRenderers.cs` and `TextRenderers.cs`: each asks `context.Resolve` for each bindable prop, renders the value through its existing encoding path, and skips the whole block on `Hide`. A document with no `bind` renders byte for byte as before: run `UTESTS/Rendering/RendererGoldenTests.cs` unchanged.

### Errors, events, isolation

- [X] T023 [P] Add the data-model §4 codes:
  - to `PAGES/PagesErrors.cs` and `PAGES/PagesErrorResults.cs`: `pages.template_needs_parent`, `template_has_children`, `unknown_collection`, `collection_has_template`, `template_parent_taken`, and the warning codes;
  - to `CMS/CmsErrors.cs` and `CMS/CmsErrorResults.cs`: `cms.collection_used_by_pages` and `cms.invalid_list`.

  Add the new event and subscriptions to `specs/001-foundation-skeleton/contracts/integration-events.md`.
- [X] T024 [P] Add helpers to `ITESTS/Infrastructure/` (existing fixture files): create a collection with items in given published states; create a template page; publish a document with bindings; read a public address with a query string; and count renders through `RenderMetrics`. Every story's tests use them.

**Checkpoint**: contract, validators, migrations, readers and the resolver core are in place.
The existing suites are green.

---

## Phase 3: User Story 1 - A Designer turns a page into a template and every item gets its own page (Priority: P1) 🎯 MVP

**Goal**: a page becomes a collection's template, and every published item is served at
`<parent>/<slug>` with bound values. Static pages win, and everything else is the not-found
page.

**Independent Test**: quickstart §1. A template under a published parent, with three
bindings, serves each published item. Draft-only, unpublished, unknown and other-site slugs
get the identical not-found page, and a static page at a matching address wins.

### Tests for User Story 1 ⚠️

- [X] T025 [P] [US1] Create `UTESTS/Pages/TemplateTreeTests.cs` for the TB-02 rules in `PageTree`:
  - the pattern is the parent's address plus `/{slug}`;
  - a template at the top level or as home is refused;
  - a page with children cannot become a template, and no child can be added under or moved into a template;
  - one template per parent, through the address uniqueness rule;
  - a template's pattern follows moves and ancestor slug changes;
  - turning it back restores `parent/slug`.
- [X] T026 [P] [US1] Create `ITESTS/Pages/SetPageTemplateTests.cs` for `GET`, `PUT` and `DELETE …/template`:
  - every refusal code;
  - one template per collection;
  - `409` version conflict;
  - Editor `403`, no role `404`;
  - the impact counts (published items gaining or losing an address; live static address lost);
  - `Pages.PageTemplateChanged` and `PageAddressesChanged` in the outbox.
- [X] T027 [P] [US1] Create `ITESTS/Rendering/RouteMatchingTests.cs` for contracts/public-rendering.md §1:
  - static, then template, then not found;
  - the item's published values in the response;
  - letter case ignored;
  - draft-only, unpublished, unknown, other-collection and other-site slugs, and `/blog/{slug}` literally, all byte-identical to 012's not-found page (extend `NotFoundParityTests` cases);
  - a template that is not live gives not found;
  - one-segment paths never reach the template lookup;
  - the document title is the item's name, else the template's title;
  - with the template's parent page unpublished, item addresses still return `200` (spec Edge Cases);
  - publishing and then unpublishing a static page `/blog/archive` switches that address between the page and the item `archive` from the next request, and a list linking to that item keeps the same `href` (FR-005, FR-033);
  - a Button linking to the template page itself renders as plain text (TB-02);
  - changing the collection's address name leaves every item address unchanged (FR-012).
- [X] T028 [P] [US1] Create `ITESTS/Rendering/ItemPageCacheTests.cs`:
  - a second request is a cache hit;
  - publishing the item changes its ETag and content at the next request;
  - publishing another item of the collection leaves this item's cache entry and ETag unchanged;
  - `304` on a matching `If-None-Match`.
- [X] T029 [P] [US1] Create `ADMINTESTS/features/pages/TemplateDialog.spec.ts` (collection choice, impact text, refusal messages, keyboard-only, axe) and `ADMINTESTS/editor/SampleItemPicker.spec.ts` (the choice is kept per page in `localStorage`, draft badge, fallback when the item is deleted).

### Implementation for User Story 1

- [X] T030 [US1] Implement the template rules in `PAGES/Domain/PageTree.cs`: `SetTemplate(pageId, collectionId?)`, the pattern in `ComputeAddresses`, and refusals on create and move. Use `Page.SetTemplate` in `PAGES/Domain/Page.cs`. Make T025 pass.
- [X] T031 [US1] Create `PAGES/Features/SetPageTemplate/SetPageTemplate.cs` (PUT and DELETE, `pages.structure.manage`) and `GetTemplateImpact.cs` (GET).
  - Check the collection through `ICollectionSchemaReader.ExistingAsync`, and count through `IPublishedItemReader.CountPublishedAsync` and a draft scan for page-scope bindings.
  - Write through `PageTreeWrites` in one transaction: the page `version`, the publication `generation`, and the outbox events `PageTemplateChanged` and `PageAddressesChanged`.
  - Responses follow `PageWithWarnings`, and `templateCollectionId` is added to `PAGES/Features/PageResponses.cs`.

  Make T026 pass.
- [X] T032 [P] [US1] Implement `PAGES/Infrastructure/TemplateReader.cs` (`ITemplateReader`) and, in `PAGES/Infrastructure/PublishedContentReader.cs`, `FindLiveTemplateAsync`, `GetLiveTemplatePatternsAsync`, `FindLiveByPathAsync` ignoring template pages, and `GetLinkTargetsAsync` returning template pages with `IsLive = false`. Update `PAGES/Infrastructure/PublishedReadDbContext.cs` to map `template_collection_id`. Register the reader in `PAGES/PagesModule.cs`.
- [X] T033 [US1] In `CMS/Domain/Item.cs` and `CMS/Infrastructure/ItemPublishing.cs`, increment `RenderStamp` in the same transaction as every publish, scheduled publish, rollback and unpublish of the item itself (data-model §1.4, case 1). Add a case to `UTESTS/Cms/Items/ItemTests.cs`.
- [X] T034 [US1] Create `RENDER/Features/RenderPage/TemplateMatcher.cs` (TB-03) and `ItemRenderKey.cs` (data-model §6; same ETag and `IsMatchedBy` as `RenderKey`, with the `item` prefix). Extend `RENDER/Features/RenderPage/PageResponder.cs`: after a static miss, match the template, find the item, build the key first, then render. The render fetches the template's live version, the item (`GetAsync`) and the schemas into `ResolvedData` under the `"page"` scope. The title is the item's name, else the template's title. Any miss goes to `notFound`. Make T027 and T028 pass.
- [X] T035 [US1] Add `BindingPlanner.cs` and `BindingFetcher.cs` to `RENDER/Binding/`. For US1, the planner finds the page scope and the media files and page links inside bound values (images, files, rich text). The fetcher calls `IPublicMediaReader` and `GetLinkTargetsAsync` for them, so they render like fixed values. `RENDER/Domain/RenderReferences.cs` collects from resolved values too.
- [X] T036 [US1] Extend `CMS/Features/Items/GetItem/GetItem.cs` and `CMS/Features/Items/ItemResponses.cs` with `address` (`path`, `reason`) through `ITemplateReader` and `IPublishedContentReader.FindLiveByPathAsync` (FR-007, TB-12). Add the cases to `ITESTS/Cms/Items/ItemApiTests.cs`. Show it in `ADMIN/features/cms/items/ItemFormView.vue`, with a link to the live address and the reason text.
- [X] T037 [P] [US1] Add `getTemplateImpact`, `setTemplate` and `removeTemplate` to `ADMIN/api/pages.ts`. Create `ADMIN/features/pages/TemplateDialog.vue` (collection list from `ADMIN/api/cms.ts`, impact, confirm). In the page tree under `ADMIN/features/pages/`, add a "Use as template…" and "Stop using as template…" action, a template badge and the pattern; hide "Add child page" on templates. Exclude template pages from the link page picker (`EDITOR/panel/controls/`).
- [X] T038 [US1] Create `EDITOR/SampleItemPicker.vue`, reusing 015's `ReferencePicker` against the template's collection. Place it in `EDITOR/EditorTopBar.vue` on template pages only, and keep the choice in `localStorage` under `qb.sample.<siteId>.<pageId>`. The editor store loads the item's draft values through `ADMIN/api/items.ts` into the `"page"` scope. `EDITOR/canvas/CanvasNode.vue` shows bound values through `EDITOR/bindings/resolve.ts`, with a "Draft" badge (clarification 2026-10-10). Make T029 pass.
- [X] T039 [US1] In `EDITOR/panel/PropertyPanel.vue`, add a minimal "Bind to field" listbox for each bindable prop when a page scope exists. It lists the compatible fields of `bindingRules.ts`, writes `props.bind.<prop>.value.field`, sets `bindingVersion: 1` on the document, and offers "Unbind". US2 replaces it with the full control.

**Checkpoint**: quickstart §1 passes. A Designer can make a template, bind fields and serve
every published item.

---

## Phase 4: User Story 2 - Binding block properties to item fields in the property panel (Priority: P1)

**Goal**: the full binding control: compatible fields grouped by scope, built-in values, one
reference step, text templates, formats, the empty-value choice and broken marks. The
canvas and the site agree.

**Independent Test**: quickstart §2. Every bindable prop × field type gives the expected
value in the canvas and on the site, and only compatible fields are offered. Everything can
be done with the keyboard, and the stored binding uses ids.

### Tests for User Story 2 ⚠️

- [X] T040 [P] [US2] Create `UTESTS/Rendering/Binding/ValueFormatterTests.cs`:
  - date formats `short`, `medium`, `long` and `iso`, for date and date-time values, in `en` and `vi`, with times in UTC;
  - number decimals and grouping;
  - boolean yes and no;
  - multi-line text to paragraphs;
  - a format that does not fit the value is ignored.
- [X] T041 [P] [US2] Add binding cases to `UTESTS/Rendering/RendererGoldenTests.cs`, with golden HTML in `FIX/rendering/bindings/`:
  - every bindable prop × every compatible field type;
  - text templates;
  - `via` to a published and to an unpublished reference;
  - `empty: hide` and `fallback`;
  - a rich text value limited by its field's `formats`;
  - link values: item page, a referenced item's page, a file, a text URL with an allowed and a refused scheme;
  - broken bindings render as fallback (SC-003).
- [X] T042 [P] [US2] Create `ADMINTESTS/editor/panel/BindingControl.spec.ts` and `TextTemplateEditor.spec.ts`:
  - only compatible fields are offered, grouped by scope;
  - references expand one step;
  - format options per type;
  - the empty choice;
  - unbinding restores the fixed value;
  - broken reasons are shown;
  - chips can be inserted, moved and deleted with the keyboard;
  - axe finds no serious or critical issue (SC-008).

### Implementation for User Story 2

- [X] T043 [US2] Complete `RENDER/Binding/ValueFormatter.cs` (dates and numbers with the site's default-language `CultureInfo`, booleans, multi-line to paragraphs) and text templates in `BindingResolver.cs` (data-model §3.3 step 4). Make T040 pass, and T018 for every case.
- [X] T044 [US2] Implement `via` in `RENDER/Binding/BindingPlanner.cs` and `BindingFetcher.cs`: collect the reference ids from fetched items for every `via` binding, then make one batched `IPublishedItemReader.GetAsync`. A reference without published data is empty. Implement the link values in `RENDER/Rendering/LinkResolver.cs`: item URLs from `GetLiveTemplatePatternsAsync` under the base path, file URLs from the media reader, and text URLs only with the 011 schemes; anything else renders as text. Make T041 pass.
- [X] T045 [US2] Mirror T043 and T044 in `EDITOR/bindings/format.ts` (using `Intl` with the site language) and `EDITOR/bindings/resolve.ts`. Make T019 pass for every case.
- [X] T046 [US2] Create `EDITOR/panel/controls/BindingControl.vue` and `BindingPicker.vue` (TB-14):
  - a listbox of compatible fields and built-in values, grouped by scope, nearest first, with references expandable one step;
  - format options;
  - the empty-value choice;
  - the field's current display name and scope on bound props;
  - broken reasons from `bindingRules.ts`;
  - unbind.

  Replace T039's minimal control in `EDITOR/panel/PropertyPanel.vue`.
- [X] T047 [US2] Create `EDITOR/panel/controls/TextTemplateEditor.vue` for text props. It holds chips for values and typed text between them, up to 20 parts and 10 values, with the keyboard behaviour of TB-14, and announces chips to assistive technology. Make T042 pass.
- [X] T048 [P] [US2] Mark the bindable props in each block definition (`EDITOR/blocks/heading/definition.ts`, `text/`, `button/`, `image/`, `rich-text/`) with `bindable: true`, and extend `EDITOR/blocks/registry.ts`'s `BlockDefinition` type. Add a check in `ADMINTESTS/editor/` that the bindable props equal the schema's `bind` keys per type (Principle VI).
- [X] T049 [US2] Show broken bindings in the layer view (`EDITOR/layers/`) and on the canvas node (a warning outline and an accessible label). Saving and publishing stay allowed (FR-018).

**Checkpoint**: quickstart §2 passes. Every binding the contract allows can be made in the
panel, and the canvas matches the site.

---

## Phase 5: User Story 3 - Visitors always see the current published item, fast (Priority: P1)

**Goal**: exact refresh. An item page refreshes when its item or a referenced item changes.
List pages refresh when their collections change, and every page that uses a collection
refreshes on a schema change (closes E-6), across instances.

**Independent Test**: quickstart §3. Each row of the TB-10 table refreshes exactly the
expected pages and keeps the others cached, also with two instances.

### Tests for User Story 3 ⚠️

- [X] T050 [P] [US3] Create `ITESTS/Rendering/ItemPageInvalidationTests.cs`, which warms 100 item pages and 5 static pages (2 with lists of the collection, 1 with a list of a collection referencing it). For each row of the research TB-10 table, it asserts which pages re-render, through `RenderMetrics`, and that every other page is served from the cache:
  - publish, unpublish, rollback, scheduled publish and delete of one item;
  - publish of a referenced item;
  - template publish and move;
  - schema change with and without rewritten values;
  - media change.

  Two of the scenarios repeat in `ITESTS/Rendering/RenderingAcrossInstancesTests.cs` with two hosts and a shared cache (SC-005, SC-006).

  *Implementation note*: until list queries exist (US4), list pages are checked through their generations and item pages through what visitors get; the world has 3 item pages, not 100. T061 adds the rendered list checks and the two-host repeat.
- [X] T051 [P] [US3] Create `ITESTS/Cms/RenderStampTests.cs`:
  - the stamp moves for the item itself (cases 1–3 of data-model §1.4);
  - it moves for the items whose published values reference a published or unpublished item;
  - it does not move for items that reference it only in a draft;
  - it does not move on draft saves.
- [X] T052 [P] [US3] Extend `ITESTS/Publishing/GenerationBumpSiteFilterTests.cs`: a Cms event on site A never bumps a page of site B, even when B has a collection with the same id shape.

### Implementation for User Story 3

- [X] T053 [US3] Create `CMS/Infrastructure/RenderStampBump.cs`. In the publish, unpublish, rollback, scheduled-run and delete paths (`CMS/Infrastructure/ItemPublishing.cs`, `ItemWrites.cs`), increment `render_stamp` for items whose `item_references` rows (location `published`) target the changed item. Use one set-based `ExecuteUpdateAsync` with the site predicate, in the same transaction. In `CMS/Features/Items/ReindexItemsForMedia/ReindexItemsForMedia.cs`, increment it for items whose published values show the file. Make T051 pass.
- [X] T054 [US3] Extend `PAGES/Domain/DependencyExtractor.cs` to emit `(collection, id)` for every `collection-list` source with a `collectionId`. For a multi-reference source, `PAGES/Infrastructure/PublishingWrites.cs` resolves the field's target collection through `ICollectionSchemaReader.GetSchemasAsync` at publish (the admin-side reader, never the public one). `PublishingWrites` also writes `(template, collectionId)` for a live template page. Add cases to `UTESTS/Pages/DependencyExtractorTests.cs` (or the existing extractor test file).
- [X] T055 [US3] Add `AffectedByCollectionsAsync(siteId, collectionIds, includeTemplates)` to `PAGES/Infrastructure/GenerationBump.cs`. It expands each collection with `ICollectionSchemaReader.CollectionsReferencingAsync`, then bumps the pages depending on those `collection` targets (and `template` targets when asked), directly or through a component.
- [X] T056 [US3] Create `PAGES/Features/RefreshPagesListingItems/RefreshPagesListingItemsHandler.cs` for `Cms.ItemPublished`, `ItemUnpublished` and `ItemDeleted` with `WasPublished`. It calls `AffectedByCollectionsAsync(includeTemplates: false)` and dedupes through `processed_events` with one handler name per event type, as in `RefreshPagesShowingMediaHandler`. Register it in `PAGES/PagesModule.cs`.
- [X] T057 [US3] Create `PAGES/Features/RefreshPagesUsingCollection/RefreshPagesUsingCollectionHandler.cs` for `Cms.CollectionChanged` (only when `SchemaChanged` or `ItemsRewritten`) and `Cms.CollectionDeleted`, using `includeTemplates: true`. This closes E-6.
- [X] T058 [US3] In `PAGES/Features/SetPageTemplate/SetPageTemplate.cs`, `PAGES/Features/Publish/Publish.cs`, `Unpublish/`, `Rollback/` and `MovePage/`, when the subject is or contains a template, also bump the pages with `collection` and `template` dependencies on its collection and the collections referencing it, in the same transaction (TB-10 row 3).
- [X] T059 [US3] Extend `PAGES/Features/RefreshPagesShowingMedia/RefreshPagesShowingMediaHandler.cs` to also bump the `collection` dependents of `IItemMediaUsageReader.CollectionsShowingPublishedAsync(siteId, fileId)`. Implement that method in `CMS/Infrastructure/ItemMediaUsageReader.cs`. Extend `ITESTS/Rendering/MediaChangeRefreshTests.cs`. Make T050 and T052 pass.

**Checkpoint**: quickstart §3 passes, including the schema-change step (E-6) and two
instances.

---

## Phase 6: User Story 4 - Showing items with a collection list block (Priority: P2)

**Goal**: the Collection list block, with source, filters, sort, limit, card and empty
state, in the editor and on the site, with item-page links.

**Independent Test**: quickstart §4. Every operator, sort and limit lists exactly the
expected published items in order, and the empty state appears when nothing matches.

### Tests for User Story 4 ⚠️

- [ ] T060 [P] [US4] Create `ITESTS/Cms/PublishedListQueryTests.cs` for `IPublishedItemReader.QueryAsync`:
  - every operator × type of binding-expression.md §5, including `contains` ignoring case and accents, reference `eq` and multi-reference `contains` through `item_references`;
  - up to 3 sorts with values-last;
  - a stable order by id;
  - offset and count, and the total;
  - only published items, with published values, never another site's;
  - a broken filter matches nothing.
- [ ] T061 [P] [US4] Create `ITESTS/Rendering/CollectionListRenderTests.cs` with golden HTML in `FIX/rendering/lists/`:
  - cards bound per item;
  - columns and gap;
  - limit;
  - the empty state;
  - an item-page button, with and without a live template;
  - a list inside a component on two pages.
  - *Carried over from T050*: the list pages of `ItemPageInvalidationTests` re-render (through `RenderMetrics`) exactly when their generation moves, and two of its scenarios repeat in `ITESTS/Rendering/RenderingAcrossInstancesTests.cs` with two hosts and a shared cache (SC-005, SC-006).
- [ ] T062 [P] [US4] Create `ITESTS/Cms/ListPreviewTests.cs` for `POST …/list-preview`:
  - capabilities (page drafts or components: allowed; no role: `404`);
  - published data only;
  - `referenced` for the `via` ids;
  - `cms.invalid_list` for an unknown collection;
  - broken filters match nothing.
- [ ] T063 [P] [US4] Create `ADMINTESTS/editor/blocks/CollectionListBlock.spec.ts` and `ADMINTESTS/editor/panel/ListControls.spec.ts`:
  - inserting a list creates a card and an empty state;
  - the source, filter and sort controls offer only valid operators per type;
  - broken filters are marked;
  - the canvas shows preview items;
  - keyboard and axe checks pass.

### Implementation for User Story 4

- [ ] T064 [US4] Implement `QueryAsync` in `CMS/Infrastructure/PublishedItemReader.cs`, with the translation in `CMS/Infrastructure/PublishedListTranslation.cs`, following 015's `ItemListQuery` approach on the published columns (TB-08):
  - filters through EF Core `jsonb` translation;
  - reference operators as `EXISTS` over published `item_references`;
  - sorts with values last, then id;
  - `count(*)` with the same filter.

  Literals are checked against the field type, and a misfit makes the filter broken (matches nothing). No raw SQL: if a translation is missing, stop and record it in this task. Make T060 pass.
- [ ] T065 [US4] Extend `RENDER/Binding/BindingPlanner.cs` and `BindingFetcher.cs` for top-level lists: one `QueryAsync` per list, with filter operands from the page scope, then the `via` batch and the schemas. Store the results in `ResolvedData` by list node id.
- [ ] T066 [US4] Create `RENDER/Rendering/Blocks/CollectionListRenderer.cs` for `collection-list` (markup from contracts/public-rendering.md §5, cards pushing the list scope) and renderers for `list-card` and `list-empty`. Add the list rules (`--qb-cols`, gap, mobile stacking) to `RENDER/Rendering/BlockStylesheet.cs`, mirroring `EDITOR/blocks/blocks.css`. `UTESTS/Rendering/BlockRendererCoverageTests.cs` must pass. Make T061 pass.
- [ ] T067 [US4] Create `CMS/Features/ListPreview/ListPreview.cs`: `POST api/v1/cms/sites/{siteId}/list-preview`, under `[RequireCapability(pages.page_drafts.edit)]` (Designers, Editors and Administrators hold it, and Designers are the only role that edits components, research TB-17). It validates like rules 14–16 and returns the published items, `referenced` and the schemas, through `IPublishedItemReader`. Add it to `ITESTS/Infrastructure/EndpointCatalog.cs`. Make T062 pass.
- [ ] T068 [P] [US4] Create `EDITOR/blocks/collection-list/definition.ts` and `CollectionListBlock.vue`, plus `EDITOR/blocks/list-card/definition.ts` and `EDITOR/blocks/list-empty/definition.ts`; register them in `EDITOR/blocks/registry.ts`. Insertion creates the card and an empty state holding a Text block ("Nothing to show yet.", translated). The canvas renders preview items from `EDITOR/bindings/listPreview.ts`, which calls the endpoint, debounced and cached per list settings. Drag and drop and the keyboard equivalents admit content only into the card and the empty state.
- [ ] T069 [US4] Create `EDITOR/panel/controls/ListSourceControl.vue` (collection), `ListFiltersControl.vue` (up to 10; operators per type from binding-expression.md §5; literal inputs reusing the item form's inputs where possible) and `ListSortControl.vue` (up to 3). Add limit, columns and gap controls to the list definition's groups. Mark broken filters. Make T063 pass.
- [ ] T070 [US4] Add the "Item page" choice for link props of blocks inside a card (`item: "url"`, and `via`) to `EDITOR/panel/controls/BindingPicker.vue`. Its canvas preview links to `#` with the address in the title.

**Checkpoint**: quickstart §4 passes.

---

## Phase 7: User Story 5 - Paging through a long list (Priority: P2)

**Goal**: paginated lists with named parameters, plain links, independent lists, bounded
cache keys, and correct out-of-range behaviour.

**Independent Test**: quickstart §5. Each item appears on exactly one page, two lists page
independently, unknown parameters do not change the ETag, and everything works without
scripts.

### Tests for User Story 5 ⚠️

- [ ] T071 [P] [US5] Create `UTESTS/Rendering/PageParamsTests.cs`:
  - only declared parameters are kept;
  - only values 2–10,000, so `1` and malformed values are dropped;
  - sorted pairs;
  - the same key for `/blog`, `/blog?page=1` and `/blog?utm=x`.
- [ ] T072 [P] [US5] Create `ITESTS/Rendering/PagingTests.cs`:
  - 40 items at 10 per page and 2 lists;
  - every item on exactly one page, including for a list whose `limit` is 12 (paging ignores `limit`);
  - links keep the other list's parameter;
  - `aria-current` and `rel` attributes;
  - at most 7 numbered links with gaps;
  - a page past the last shows the empty state and a link to the first, with status `200`;
  - ETags equal for ignored parameters;
  - golden HTML of the pager in `FIX/rendering/lists/`.

### Implementation for User Story 5

- [ ] T073 [US5] Create `RENDER/Features/RenderPage/PageParams.cs` (TB-11). `RenderPageController.cs` passes `Request.Query` to `RenderPageQuery.cs`. `PageResponder.cs` reads the live document's paginated lists' parameters. The key needs the document's list parameters before the content is read, so `PublishingWrites.cs` stores them at publish in `publications.page_param_names` (T011), and `LivePageRef` and `LiveTemplateRef` carry them as `PageParamNames`. Add `PageParams` to `RenderKey.cs` and `ItemRenderKey.cs`. Make T071 pass.
- [ ] T074 [US5] Create `RENDER/Rendering/Blocks/Pager.cs` (contracts/public-rendering.md §3), and apply paging in `CollectionListRenderer.cs` and `BindingFetcher.cs`: the offset comes from the list's parameter, the count is `pageSize`, `limit` is ignored when paging is set (FR-024), and past-the-end renders the empty state with a first-page link. Add the pager labels to `RENDER/Features/NotFound/PublicTexts.cs`, and pager styles to `BlockStylesheet.cs`. Make T072 pass.
- [ ] T075 [P] [US5] Create `EDITOR/panel/controls/ListPagingControl.vue` (on or off, page size, parameter name with slug validation, rule 19 shown inline). The canvas shows the first page and a static pager.

**Checkpoint**: quickstart §5 passes.

---

## Phase 8: User Story 6 - Lists inside templates and lists inside lists (Priority: P2)

**Goal**: filters and bindings that read enclosing scopes, "not the current item", lists
sourced from a multi-reference field, two levels of lists, and the query cap.

**Independent Test**: quickstart §6. Each scope resolves to the right item, and outer
scopes can be reached by name. Multi-reference sources keep stored order, and too-deep
nesting is refused on both sides.

### Tests for User Story 6 ⚠️

- [ ] T076 [P] [US6] Create `ITESTS/Rendering/NestedScopeTests.cs`:
  - a list on a template filtered by the page item's option and by `ne current`;
  - a list in a list filtered by the outer card's item, with a binding inside it reading the outer scope by id;
  - a multi-reference source in stored order, skipping unpublished items, with filters and limit applied after;
  - inner queries made once per distinct outer value (counted);
  - a render needing more than 200 queries renders the rest as empty states and logs ids only.
- [ ] T077 [P] [US6] Extend `ADMINTESTS/editor/bindings/scopes.spec.ts`: the scopes offered at each depth, nearest first, named by list label; filter operands from enclosing scopes; `current` offered only when the source collection matches an enclosing scope's collection.

### Implementation for User Story 6

- [ ] T078 [US6] Extend `RENDER/Binding/BindingPlanner.cs` and `BindingFetcher.cs` for nested lists (TB-09): after the outer lists are fetched, group the inner list's operand values per outer item and run one `QueryAsync` per distinct combination. Store the results in `ResolvedData` keyed by `(listId, operandKey)`. Enforce the 200-query cap with a warning log of ids. Resolve `{ "current": true }` to an id `ne` filter.
- [ ] T079 [US6] Implement multi-reference sources: read the field's published ids from the scope item, call `QueryAsync` with `ItemIds` (the order is kept in `PublishedListTranslation.cs` when no sort is set), then apply filters and limit. Make T076 pass.
- [ ] T080 [US6] Extend `EDITOR/bindings/scopes.ts`, `BindingPicker.vue` and `ListFiltersControl.vue` for scope choice and outer operands. Extend `ListSourceControl.vue` with "a multi-reference field of …" sources, and `listPreview.ts` to pass enclosing item ids (`scopes`). Make T077 pass.

**Checkpoint**: quickstart §6 passes.

---

## Phase 9: User Story 7 - Changing templates, schemas and addresses safely (Priority: P3)

**Goal**: usage tracking, the collection delete guard, schema-change and address warnings,
and turning a template back into a page.

**Independent Test**: quickstart §7. Every warning and refusal appears as specified, and
the site never fails.

### Tests for User Story 7 ⚠️

- [ ] T081 [P] [US7] Create `UTESTS/Pages/CollectionUseExtractorTests.cs` and `ITESTS/Pages/CollectionUsesTests.cs`. The rows must be correct after each of: draft save, publish, rollback, schedule set or cancel, template set or remove, page or component delete, and copy-to-draft, for each state (`draft`, `live`, `scheduled`), with the field ids read.
- [ ] T082 [P] [US7] Create `ITESTS/Cms/CollectionDeleteGuardTests.cs`:
  - refused with `cms.collection_used_by_pages`, listing up to 50 uses with states and the total, for a template, a list source in a draft only, in a live version only, and in a scheduled version only;
  - allowed after the uses are removed;
  - when another collection also references it, 014's `cms.collection_referenced` refusal is returned first, unchanged, and the page check runs only after it passes;
  - `POST …/collections/{id}/preview` returns `boundPages` with counts per removed or retyped field across draft, live and scheduled uses, zero for fields no page uses, and no entries for details-only changes (FR-011).
- [ ] T083 [P] [US7] Create `ITESTS/Rendering/BindingFailureTests.cs` (SC-007). After each of these, every affected address returns `200` with fallback or hidden blocks, never `5xx`:
  - a bound field removed;
  - a bound field retyped (number to text);
  - a list's filter option deleted;
  - a referenced item deleted;
  - a bound image deleted;
  - a collection deleted between a draft save and its next render (the race in TB-13, simulated by deleting through the database).

  The next save of that draft is refused with rule 14.
- [ ] T084 [P] [US7] Extend `ITESTS/Pages/MovePageTests.cs` (or the existing move tests) and `SetPageTemplateTests.cs`:
  - the `items_change_address` count on moving a template or an ancestor;
  - `items_lose_address` on unpublish and remove;
  - `item_address_hidden` when creating `/blog/archive` while an item has the slug `archive`.

### Implementation for User Story 7

- [ ] T085 [US7] Create `PAGES/Domain/CollectionUseExtractor.cs`: collections and field ids from a document (list sources, filters, sorts, bindings, with `via` fields counted against the scope's collection) and from a page row (template). Create `PAGES/Infrastructure/CollectionUses.cs`, which rewrites the rows for one subject and state. Call it from `DraftWrites.cs`, `PublishingWrites.cs`, `ScheduleWrites.cs`, `SetPageTemplate.cs` and the delete slices, in their transactions. Make T081 pass.
- [ ] T086 [US7] Implement `PAGES/Infrastructure/CollectionUsageReader.cs` (`ICollectionUsageReader`) and register it. In `CMS/Features/DeleteCollection/DeleteCollection.cs`, call `ListUsesAsync` and refuse with `cms.collection_used_by_pages`. In `CMS/Features/PreviewCollection/PreviewCollection.cs` and `CMS/Features/PreviewResponses.cs`, add `boundPages` for fields that the change removes or retypes, through `CountFieldUsesAsync`. Make T082 pass.
- [ ] T087 [US7] Add the address warnings: `items_change_address` in the move warning of `PAGES/Features/MovePage/MovePage.cs` and `UpdatePage/` (slug changes of ancestors), and `items_lose_address` in unpublish and remove, through `IPublishedItemReader.CountPublishedAsync`. Add `item_address_hidden` on create and move, through `IPublishedItemReader.FindBySlugAsync`. Make T084 pass.
- [ ] T088 [US7] Admin:
  - show `boundPages` in the schema builder's change preview (`ADMIN/features/cms/CollectionBuilderView.vue`);
  - show the uses list in the delete refusal (`ADMIN/features/cms/CollectionsView.vue`);
  - show the item-address warnings in the page tree's move and rename confirmations (`ADMIN/features/pages/`);
  - in `TemplateDialog.vue`, show the remove flow and its broken-binding count.

  Add vitest cases next to the existing specs.
- [ ] T089 [US7] Make T083 pass: fix any path that throws, logging warnings with ids only (FR-038), in `RENDER/Binding/` or `RENDER/Rendering/`.

**Checkpoint**: quickstart §7 passes.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T090 [P] Create `ITESTS/Isolation/Probes/TemplateIsolationProbe.cs`:
  - site B cannot make a template of site A's collection;
  - site B cannot list site A's collection;
  - site B cannot reach site A's item addresses or list previews.

  Register it, and keep `ITESTS/Isolation/IsolationCoverageTests.cs` green (FR-037).

  *Progress*: the probe (module `Templates`) covers the template endpoints, item addresses and another site's collection; list previews join it with US4 (T067).
- [ ] T091 [P] Run `ATESTS/AuthorizationDeclarationTests.cs` and confirm every new action declares exactly one capability.
- [ ] T092 [P] Create `STESTS/ItemPagePerformanceTests.cs` (Performance category) for SC-004:
  - an item page of 200 blocks with a list of 20;
  - a paginated list of 100 over 10,000 published items;
  - a nested list of 20 × 2;
  - each under 200 ms p95 on a miss and under 20 ms on a hit.

  Record the query counts. If the budget fails, apply TB-20's fallback and record it here.
- [ ] T093 [P] Create `ITESTS/Rendering/UpgradeFrom035Tests.cs`, following `ITESTS/Rendering/UpgradeFrom015Tests.cs`: a 035 snapshot upgrades, `AddTemplates` and `AddRenderStamp` apply, and every existing page renders the same body as before the upgrade (only ETags change).
- [ ] T094 Add "make a template, publish an item, open its address" to the release smoke journey in `STESTS/ReferenceDeploymentSmokeTests.cs`.
- [ ] T095 [P] Add `seed-template -Collection <address-name> -Parent <path>` to `qb.ps1`. It creates a template page with a heading and image bound to the title and first image fields, through the API. Update the help text.
- [ ] T096 [P] Update the docs:
  - `docs/operations.md`: templates, item addresses and route order, the collection delete guard, schema changes refreshing live pages, and the over-refresh note (plan Risks);
  - `docs/backlog.md`: E-6 marked closed by 016, plus the deferred items in plan.md;
  - `specs/002-identity-setup/contracts/authorization.md` §2 notes;
  - `specs/012-publishing-rendering/contracts/readers.md`;
  - `specs/015-cms-items/contracts/module-surface.md` §5 (obligations met);
  - `docs/contributing.md` "adding a block type" (bindable props).
- [ ] T097 [P] Run `dotnet test` with coverage. The render path (`PageResponder`, `RENDER/Binding/`, `CollectionListRenderer`) and the publish path keep at least 80 % line coverage. Run `npm run lint`, `npm run type-check` and `npm test`, and check that the formatting and analyzer gates are clean.
- [ ] T098 Run the whole of `quickstart.md` by hand on the reference compose setup, including SC-001 and SC-002 with a stopwatch and SC-008 keyboard-only, and record the results in `specs/016-collection-templates-binding/quickstart-results.md`.
- [ ] T099 Scan the logs of a full test run for item values, names and slugs: none may appear. Then open the PR closing #16, with the plan's operator impact and upgrade notes in the description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup. Blocks every story.
- **US1 (Phase 3)**: depends on Foundational. The MVP.
- **US2 (Phase 4)**: depends on US1 (page scope, sample item, minimal control T039).
- **US3 (Phase 5)**: depends on US1 (item pages and keys). It can run in parallel with US2.
  Its list rows need US4's lists to be demonstrable, but tests can publish list documents
  directly once T004 lands.
- **US4 (Phase 6)**: depends on Foundational and US2's binding control (cards bind like
  template blocks). It can start its Cms query (T064) right after Foundational.
- **US5 (Phase 7)**: depends on US4.
- **US6 (Phase 8)**: depends on US4. It uses US1's page scope.
- **US7 (Phase 9)**: depends on US1 (templates). Its list and binding cases need US4.
- **Polish (Phase 10)**: after all stories.

### Within Each User Story

- Tests first, and they must fail.
- Then domain, readers, slices, renderer and admin.
- A story is done when its quickstart section passes.

### Parallel Opportunities

- **Foundational**:
  - T006, T007, T008, T013, T014, T016, T018 and T019 (examples, tests, interfaces) are parallel;
  - T011 and T012 (the two migrations) are parallel;
  - T023 and T024 are parallel.
- **Stories**:
  - after US1, US2 and US3 run in parallel;
  - T064 (Cms list query) can run during US2 and US3;
  - after US4, US5 and US6 run in parallel;
  - US7's usage tracking (T081, T085) can start after US1.
- **Within a story**: every test task marked [P] is parallel, and the admin tasks marked
  [P] are parallel with the server tasks.

---

## Parallel Example: Foundational

```text
Task: "T006 golden examples in CONTRACTS/binding-expression/examples/"
Task: "T007 PageSchemaBindingTests in UTESTS/Pages/PageSchemaBindingTests.cs"
Task: "T013 Cms.Contracts readers in CMSC/Publishing/"
Task: "T014 Pages.Contracts readers in PAGESC/"
Task: "T018 BindingRulesGoldenTests in UTESTS/Rendering/Binding/"
```

## Parallel Example: User Story 1

```text
Task: "T025 TemplateTreeTests in UTESTS/Pages/TemplateTreeTests.cs"
Task: "T026 SetPageTemplateTests in ITESTS/Pages/SetPageTemplateTests.cs"
Task: "T027 RouteMatchingTests in ITESTS/Rendering/RouteMatchingTests.cs"
Task: "T028 ItemPageCacheTests in ITESTS/Rendering/ItemPageCacheTests.cs"
Task: "T037 TemplateDialog.vue in ADMIN/features/pages/"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. Every published item is served at its address with
   bound title, image and body.

### Incremental Delivery

1. Setup and Foundational.
2. US1: item pages. **Demo.**
3. US2 (full binding) and US3 (exact refresh, closes E-6). All P1 stories are done.
4. US4 (lists), then US5 (paging) and US6 (nested scopes).
5. US7 (safe changes).
6. Polish, the manual quickstart run, and the PR closing #16.

One PR, as with 010–015. There is no feature flag mechanism, so lists are not registered in
the editor until US4 is complete (plan, Principle XI).

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US7.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #16.
- **Contract changes** (T003–T006) land in the same PR as their tests (Development
  Workflow).
- **Avoid**:
  - I/O inside block renderers;
  - writes on the public path;
  - raw SQL for list queries (stop and record instead, T064);
  - values, names or slugs in logs and events;
  - refusing saves because of broken bindings.
