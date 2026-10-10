# Research: Collection Templates and Data Binding

**Feature**: `016-collection-templates-binding` | **Date**: 2026-10-10 | **Spec**: [spec.md](./spec.md)

Decisions are numbered `TB-xx`. Each has a decision, a rationale and the alternatives
considered. The spec's five clarifications (2026-10-10) are inputs, not open questions.

---

## TB-01 No new module: templates in Pages, item reads in Cms, resolution in Rendering

**Decision**:
- **Pages** owns what a page *is*: a page becomes a template through a column on
  `pages.pages` (TB-02). Bindings and the Collection list block are part of the page
  document (TB-04, TB-07), so they are validated, drafted, published, versioned and rolled
  back with it, by the existing 010–012 code.
- **Cms** owns items. It gains a read-only `IPublishedItemReader` for the public path
  (TB-08) and a render stamp per item (TB-10).
- **Rendering** resolves bindings and list queries while rendering (TB-09). It still owns no
  tables.

**Rationale**: every rule already has an owner. A template's address is a page-tree rule
(010). Draft and published copies of bindings come free with page versions (012).
Published item data and its queries belong to the module that owns `cms.items`
(Principle I).

**Alternatives considered**:
- **A Templates entity in Cms** (collection → page id and pattern): the pattern would
  duplicate the page tree's address rules across modules, and moves would need an event to
  stay in step.
- **Bindings stored beside the document** (a separate table keyed by node id): two things to
  publish atomically, and copy-to-draft and rollback would have to learn about them.

---

## TB-02 A template page is a page with a collection, and its address is a pattern

**Decision**:
- `pages.pages` gains `template_collection_id uuid null`.
- A template page's `path` is its parent's path followed by `/{slug}`, written literally,
  for example `/blog/{slug}`. The template keeps its own `slug` column unused, as the home
  page does (010 P-07), so it has an address again if it is turned back into a normal page.
- The existing unique index on `(site_id, path)` therefore allows **one template per
  parent** (FR-003) without a new rule. A new unique partial index on
  `(site_id, template_collection_id)` allows **one template per collection** (FR-002).
- `PageTree` refuses, with new error codes (data-model §4):
  - a template at the top level or as the home page (`pages.template_needs_parent`);
  - a child under a template, and making a page with children a template
    (`pages.template_has_children`);
  - a collection of another site, or one that does not exist (`pages.unknown_collection`,
    checked through `ICollectionSchemaReader`, TB-08).
- Moving a template or renaming an ancestor recomputes the pattern like any address (010).
  `PageAddressesChanged` already announces it.
- `IPublishedContentReader.FindLiveByPathAsync` ignores template pages, so `/blog/{slug}`
  typed by a visitor is not a page. `GetLinkTargetsAsync` reports template pages as not
  live, so a page link to a template renders as plain text (012 FR-026), and the editor's
  page picker leaves templates out. A new `FindLiveTemplateAsync(siteId, parentPath)` returns
  the live template under a parent: page id, generation and collection id.
- Making a page a template, changing its collection, or turning it back is a new slice,
  `SetPageTemplate`, under `pages.structure.manage` (Designers and Administrators, FR-001).
  It uses the page's `version` like every structure change (010 FR-010).

**Rationale**:
- The pattern follows from the clarified spec: the address comes from the tree, with one
  item segment.
- Writing the pattern into `path` keeps uniqueness, the depth limit, the address length,
  reserved segments and move warnings in the one place that already enforces them.
- `{` and `}` cannot occur in a slug, so no static page can ever take the pattern's address.

**Alternatives considered**:
- **A separate `template_pattern` column**: two address columns to keep in step, and a
  second uniqueness rule.
- **`/blog/*` or `/blog/%` as the stored pattern**: equally safe (the path normaliser
  refuses `%`), but less readable in the admin and in logs.

---

## TB-03 Route matching: reserved, static page, template, not found

**Decision**: `PageResponder` gains one step between "no static page" and "not found":
1. ASP.NET routing still answers reserved and platform prefixes first (`/api`, `/admin`,
   `/health`, `/_media`, `/_assets`); unchanged.
2. `FindLiveByPathAsync(path)`: a live static page wins (FR-005).
3. Otherwise, when the path has at least two segments and its last segment matches the slug
   grammar (`^[a-z0-9]+(-[a-z0-9]+)*$`, at most 100 characters), call
   `FindLiveTemplateAsync(parentPath)`. Then call
   `IPublishedItemReader.FindBySlugAsync(collectionId, slug)`, which returns the item id and
   render stamp of an item with published data and that published slug, or null.
4. Anything else gets the not-found page, byte for byte the same as for any unknown address
   (FR-006, 012 FR-030).

The path is already lower case (010, `PathNormaliser`), and published slugs are stored lower
case (015), so the lookups are exact matches on indexed columns: one for the static page,
one for the template, one for the item.

**Rationale**: two extra indexed lookups, only on addresses with no static page. Static
pages pay nothing.

**Alternatives considered**:
- **A route table rebuilt on every publish**: faster by microseconds, but a cache to keep
  coherent across instances for no measurable gain.
- **Templates before static pages**: contradicts the issue's stated order.

---

## TB-04 The binding format: structured JSON inside `props`, versioned on the document

**Decision**: a binding is a JSON object, never a string expression to parse. It lives in
an optional `bind` prop of the block, keyed by the property it fills:

```json
{ "id": "h1", "type": "heading",
  "props": { "text": "Untitled post", "level": 1,
             "bind": { "text": { "value": { "field": "f1" }, "empty": "fallback" } } },
  "children": [] }
```

- **Value** (one of):
  - `{ "field": "f3" }`: a field of the scope's item, by field id (014 contract);
  - `{ "item": "url" | "slug" | "firstPublishedAt" | "lastPublishedAt" }`: a built-in item
    value;
  - either form with `"via": "f5"`: the same, read from the item that the single reference
    field `f5` points at. One step only (clarification 2026-10-10);
  - `"scope"`: optional. `"page"` (the template's item) or the `id` of an enclosing
    `collection-list` node. When omitted, the nearest scope is used (FR-028);
  - `"format"`: optional, by value type: `{ "date": "short" | "medium" | "long" | "iso" }`,
    `{ "number": { "decimals": 0–6 } }`, `{ "yes": "…", "no": "…" }` for booleans.
- **Text template**, for text properties only: `{ "parts": ["By ", { "field": "f1", "via": "f5" }, ", ", { "field": "f6" }] }`,
  with 1–20 parts, of which up to 10 are values (FR-015).
- **`empty`**: `"fallback"` (the default: the property's own fixed value is shown) or
  `"hide"` (the whole block is left out) (FR-019).
- **Version**: the document envelope gains an optional `"bindingVersion": 1`. It is required
  when the document holds any `bind` or `collection-list` (rule 13, data-model §2). The page
  schema stays at version 1, because both additions are optional props or new types
  (page-schema.md §6).
- **Where it is written down**:
  - A new contract document `docs/contracts/binding-expression.md` (FR-013) holds the
    semantics: scopes, empty values, formats and the compatibility table (TB-05).
  - The shapes are `$defs` in `page-schema/v1.schema.json` (`binding`, `bindingValue`,
    `textTemplate`, `listSource`, `listFilter`, `listSort`), so the one embedded schema file
    remains the single validator input that both sides use (Principle VI).
  - The draft of the contract document is [contracts/binding-expression.md](./contracts/binding-expression.md).

**Rationale**:
- Structured JSON is validated by the schema subset that already runs on both sides, cannot
  be injected into, and needs no parser or grammar to version.
- Field ids keep bindings valid across renames (Principle VI, 014).
- Putting `bind` in `props` makes the addition "a new optional prop on an existing type",
  which page-schema.md §6 declares additive.

**Alternatives considered**:
- **A string language** (`{{ item.author.name | date:'long' }}`): needs a grammar, a parser
  in TypeScript and in C# that must agree, escaping rules, and error positions. It also
  invites growing into a programming language, which the spec rules out.
- **A fifth node property `bindings`**: page-schema.md §1 fixes nodes at four properties;
  changing that is a breaking change needing schema version 2.
- **The version on each binding**: noise on every bound property, and nothing could mix
  versions inside one document anyway.

---

## TB-05 Which properties can be bound, and to what

**Decision**: the page schema enumerates the bindable properties per block type. Each
type's `bind` object allows only these keys:

| Block | Bindable props |
|---|---|
| `heading` | `text` |
| `text` | `text` |
| `button` | `label`, `link` |
| `image` | `media`, `alt`, `link` |
| `rich-text` | `content` |

Which value fits which property is in the compatibility table of
[contracts/binding-expression.md](./contracts/binding-expression.md) §4 (FR-014). That table
is implemented twice, as `BindingRules.cs` (Rendering, server) and `bindingRules.ts`
(admin). Both run the same golden examples in `docs/contracts/binding-expression/examples/`,
the way 015 shares its item-value rules (015 IT-05).

The server **does not refuse** a binding that is incompatible, or that names a field that
does not exist: such a binding is *broken* (FR-018). The editor marks it, and the renderer
treats it as empty. The server refuses only what is malformed (TB-06).

**Rationale**:
- The schema enumeration is the "defined once" block definition (Principle VI): adding a
  block type adds its `bind` keys there and nowhere else.
- Field existence and type cannot be refused at save time, because schema changes happen in
  another module afterwards and must not be refused because of bindings (FR-011).

**Alternatives considered**:
- **Validate compatibility on save**: would accept a page today and then hold a page that
  would be refused tomorrow, after a schema change. "Broken" has to exist anyway.
- **Binding colours, spacing and alignment**: out of scope (spec).

---

## TB-06 Validation rules added to the page schema

**Decision**: the page document validator (Pages `PageDocumentValidator` and the admin's
`documentValidator.ts`) gains code rules 13–19, run after rule 12 in document order
(data-model §2):
- 13 `binding_version_required`;
- 14 `unknown_collection`, for a list source or a template collection that is not a
  collection of the document's site. Unknown and other-site ids give the same failure, as
  rule 6 does for components;
- 15 `unknown_scope`, for a `scope` that names no enclosing list;
- 16 `page_scope_not_allowed`, for `"page"` scope or a multi-reference source with no
  enclosing list in a component document (FR-029);
- 17 `list_too_deep`, for a list inside a list inside a list (FR-028);
- 18 `too_many_lists`, for more than 20 lists in one document;
- 19 `duplicate_page_param`, for two paginated lists with the same page parameter (TB-11).

`"page"` scope in an ordinary (non-template) page is *not* refused. It is a broken binding,
so that turning a template back into a page never makes its draft unsaveable (US7
scenario 5).

Rule 14 needs to know which collections the site has. The Pages validator asks Cms through
`ICollectionSchemaReader.ExistingAsync(siteId, ids)` (Cms.Contracts, TB-08). The admin
validator receives the site's collection ids from the editor's loaded collection list.

**Rationale**: these are the only conditions that make a document meaningless or unsafe
rather than merely stale.

---

## TB-07 The Collection list block: one node with a card and an empty state

**Decision**: three new node types, all additive in page schema v1:
- **`collection-list`**: in `contentChild`. Its children are exactly one `list-card`
  followed by one `list-empty`. Props:
  - `source`: `{ "collectionId": uuid }`, or `{ "field": "f9", "scope"?: … }` for a
    multi-reference field of a scope's item (FR-025);
  - `filters`: 0–10 conditions, each `{ "field" | "item", "op", "value" }`. `op` is one of
    `eq`, `ne`, `contains`, `gt`, `lt`, `empty`, `notEmpty`. `value` is a literal, or
    `{ "field": …, "scope"?: … }` from an enclosing scope, or `{ "current": true }` for "is
    not the current item" with `ne` (FR-023);
  - `sort`: 0–3 `{ "field" | "item", "dir": "asc" | "desc" }`;
  - `limit`: 1–100, default 12;
  - `paging`: optional `{ "pageSize": 1–100, "param": slug-like, default "page" }`;
  - `columns` (1–4, default 3), `gap` (`spaceRef`) and `layerLabel`.
- **`list-card`**: holds `contentChild`. It is repeated once per item and opens a binding
  scope named by the parent list's id.
- **`list-empty`**: holds `contentChild` and is shown when no item is listed. A new list
  starts with a Text block reading "Nothing to show yet."

Rules beyond the schema: a `collection-list` must have exactly the two children in that
order (`list_children_invalid`), and `list-card` and `list-empty` appear only there (schema
placement).

Filter operators per field type are listed in the contract, §5. Literal values are checked
against the field's type when rendering. A literal that no longer fits after a schema
change makes the condition broken: the list shows nothing and the editor marks it. Showing
everything instead would leak items the designer meant to filter out.

**Rationale**: explicit slot nodes keep the "every node has id, type, props, children" shape,
so the editor's canvas, layer view, drag and drop, copy and paste, and undo all work on
cards and empty states without special cases.

**Alternatives considered**:
- **Card and empty state as props holding node arrays**: every tree walker (layers,
  dependency extraction, validators) would have to learn a second place where nodes live.
- **A separate "list item template" document**: two documents to publish together.

---

## TB-08 Reading published items: a read-only reader in Cms.Contracts

**Decision**: `IPublishedItemReader`, implemented in Cms over a read-only connection and a
`CmsPublishedReadDbContext` that maps only what visitors may see, as 012 PR-06 does for
pages:
- `FindBySlugAsync(siteId, collectionId, slug)` → `(ItemId, RenderStamp)` or null;
- `GetAsync(siteId, itemIds)` → `PublishedItem` (id, collection id, published slug, name,
  first and last published times, published values document) for items with published data;
- `QueryAsync(siteId, PublishedListQuery)` → one page of items and the total. The query
  holds the collection, an optional list of item ids (multi-reference source, keeping their
  order), filters, sorts, offset and count;
- `GetSchemasAsync(siteId, collectionIds)` → the current schema documents, so the renderer
  knows field types, option labels, `withTime` and rich text formats;
- `CountPublishedAsync(siteId, collectionId)`, for address warnings (TB-12).

`ICollectionSchemaReader` (admin side, normal connection) offers `ExistingAsync`,
`GetSchemasAsync` (used by Pages at publish, never the public reader) and
`CollectionsReferencingAsync(siteId, collectionId)`: the collections that have a reference
or multi-reference field targeting it (TB-10).

The query translation reuses 015's `ItemListQuery` approach on the published columns: EF
Core's `jsonb` translation for values, `item_references` with location `published` for
reference equality and contains, and the item id as the last sort key so the order is
stable. Only items whose status has published data are visible.

**Rationale**: the public path stays read-only by construction (Principle VII), and queries
on Cms tables stay inside Cms (Principle I).

**Alternatives considered**:
- **Copy published items into Rendering or Pages**: a second copy that can lag, and a
  module owning another module's data.
- **One generic "query items" method used by admin and public alike**: the public one must
  never see drafts. Two methods on two connections make that impossible to get wrong.

---

## TB-09 Rendering: resolve first, then render

**Decision**: `PageResponder.RenderAsync` gains a resolution phase before rendering:
1. **Plan**: `BindingPlanner` walks the document (and its components' documents) and finds
   the template item, every list, and every `via` reference.
2. **Fetch**:
   - the template item (`GetAsync`);
   - the top-level lists (`QueryAsync`), with filter values taken from the page item;
   - the nested lists: one query per *distinct* outer value combination, not per card;
   - the referenced items of every fetched item that a `via` binding reads (`GetAsync`,
     batched);
   - the schemas of every collection involved (`GetSchemasAsync`).
3. **Collect** media and page links from the resolved values (images, files, rich text),
   then fetch them with the existing readers (`IPublicMediaReader`, `GetLinkTargetsAsync`),
   plus the item page patterns of the collections whose items are linked
   (`GetTemplatesAsync`, TB-12).
4. **Render**: `RenderContext` carries a scope stack. `IBlockRenderer` implementations ask
   `context.Resolve(node, prop)`, which returns the bound value, the fixed value, or "hide".
   The block's own encoding stays as it is (Principle IX, FR-021). The new
   `CollectionListRenderer` renders cards, the empty state and paging links (TB-11).

**Limits during render**:
- At most **200 list queries** per page render. Lists beyond that render their empty state,
  with a warning logged that carries ids only.
- The editor's limits (20 lists, two levels) keep real pages far below this.

**Values**:
- Dates and numbers are formatted with the site's default language (`CultureInfo`), never the
  server's.
- Multi-line text bound to rich text becomes paragraphs.
- A rich text value is rendered through `RichTextRenderer`, with the allow-list and the
  field's `formats` (035).
- A text value bound to a link must parse as a URL with an allowed scheme (011); otherwise
  the link renders as text.
- An empty value, as defined in FR-019, includes a referenced item that is not published, a
  missing media file, and a broken binding.

**Failure**: a resolution failure (a broken binding, a missing schema, an unknown scope)
never throws out of the renderer. It resolves to empty and is logged at warning level with
the node id, page id and collection id, and no values (FR-038).

**Rationale**: batching by phase keeps database round trips constant per page shape rather
than per item. The renderer's existing structure (one renderer per block type, one
`HtmlWriter`) is kept.

**Alternatives considered**:
- **Lazy loading inside block renderers**: N+1 queries, and renderers would need async I/O.
- **One SQL query with `LATERAL` joins for nested lists**: fewer round trips, but raw SQL on
  the hottest path, and the de-duplicated per-value queries meet the budget in measurement
  (SC-004, TB-20).

---

## TB-10 Caching and invalidation: generations for pages, render stamps for items

**Decision**: keep 012's model, where a cache key is read from the database before the
content and nothing is ever evicted (012 PR-05). Two kinds of key:
- **Static pages with lists**: the existing `RenderKey` gains `PageParams`, the normalised
  page numbers of the page's own paginated lists (TB-11). The page's `generation` is bumped
  when the lists' content may have changed.
- **Item pages**: a new `ItemRenderKey`. It holds the template page id and generation, the
  item id and the item's **render stamp**, plus the design number, site version, base path
  and `PageParams`.

**Item render stamp** (`cms.items.render_stamp bigint`): Cms increments it in the
transaction of:
- a publish, scheduled publish, rollback or unpublish of the item;
- the same events on any item that this item's *published* data references
  (`item_references`, location `published`), so "Author → Name" stays fresh (US3 scenario 3);
- a media change or deletion that affects the item's published data, in Cms's existing
  media handler (015 `ReindexItemsForMedia`).

**Page dependencies**: Pages' `live_dependencies` gains two target kinds, written at
publish from the document and the page row:
- `collection`: a list's source collection, including a multi-reference field's target
  collection. Written for pages and components;
- `template`: the template page's own collection.

**Bumps** (Pages handlers, idempotent through `processed_events` as in
`RefreshPagesShowingMediaHandler`). "Referencing C" means the collections with a reference
field targeting C, from `CollectionsReferencingAsync`.

| Event | Pages bumped (directly or through a component) |
|---|---|
| `Cms.ItemPublished`, `ItemUnpublished`, `ItemDeleted` (`WasPublished`) of an item of C | `collection` dependents of C and of every collection referencing C |
| `Cms.CollectionChanged` with `SchemaChanged` or `ItemsRewritten`; `Cms.CollectionDeleted` | `collection` and `template` dependents of C and of every collection referencing C |
| Template of C assigned, removed, published, unpublished, rolled back or moved (Pages, same transaction) | `collection` and `template` dependents of C and of every collection referencing C, because their item links change |
| `Media.FileDeleted`, `FileChanged` | as today, plus `collection` dependents of the collections whose published items show the file (`IItemMediaUsageReader`) |

**Closes E-6**: a schema change that rewrites published values now refreshes every live
page that binds to or lists the collection, including every item page through its template
generation. Live pages show the new data at their next request, and bindings to removed
fields render as empty (FR-018).

**Freshness**: Cms events reach Pages through the outbox within 012's 5-second target
(SC-005).

**Rationale**:
- Item pages are keyed per item, so publishing one post re-renders one post page, plus the
  pages that list posts (SC-006).
- Reference staleness is solved inside Cms in one transaction, without cross-module events.
- Over-refreshing (a list page refreshed because a referenced collection changed, although
  its cards do not show that reference) costs one render. Under-refreshing would show stale
  content.

**Alternatives considered**:
- **Bumping the template page's generation on every item publish**: correct but refreshes
  every item page on every publish, which breaks SC-006.
- **Per-collection "content numbers" read into every key**: an extra lookup per list on
  every request, cache hits included, against the 20 ms budget.
- **Recording exact (page, item) pairs while rendering**: renders would write, and the
  public path is read-only (PR-06).

---

## TB-11 Paging: a named parameter per list, plain links, bounded keys

**Decision**:
- A paginated list's `paging.param` (default `page`, slug grammar, unique within the
  document by rule 19) names its query parameter: `/blog?page=3`.
- `RenderPageController` passes the query string to the handler. The handler keeps only the
  parameters of the document's paginated lists. Each value is normalised: a whole number
  from 2 to 10,000 is kept; anything else, including `1`, is dropped, which means the first
  page (FR-027). The kept pairs, sorted, become `PageParams` in the key. Every other
  parameter is ignored, so it cannot create cache entries.
- The list knows its total from `QueryAsync`. A page number past the last page renders the
  empty state with a link to the first page, and the response is still 200 (US5 scenario 3).
- Navigation is a `<nav aria-label>` holding previous, next and up to 7 numbered links,
  with `aria-current="page"` on the current one. Links keep the other lists' parameters, so
  lists page independently (FR-027). No script.

**Rationale**: the issue wants paging that works without scripts and with shareable
addresses. Capping the numbers bounds the number of cache keys an attacker can create per
page.

**Alternatives considered**:
- **A path segment** (`/blog/page/3`): collides with template patterns and the page tree.
- **"Load more" with script**: breaks progressive enhancement (Principle VIII).

---

## TB-12 Address information and warnings across Pages and Cms

**Decision**:
- **Pages → Cms** (`IPublishedItemReader.CountPublishedAsync`): moving a template or an
  ancestor, unpublishing a template and turning a template back include "N published items
  will change address" in the existing move warning (012 FR-011) and the new template
  impact response (FR-008).
- **Cms → Pages** (new `ITemplateReader` in Pages.Contracts): `GetTemplateAsync(siteId,
  collectionId)` returns the pattern and whether the template is live, and
  `GetTemplatesAsync` does the same for several collections, for the renderer's item links.
  Cms's item responses gain `address`: the live address, or a reason it has none
  (`no_template`, `template_not_live`, `not_published`, `hidden_by_page`) (FR-007).
  `hidden_by_page` is checked with `IPublishedContentReader.FindLiveByPathAsync`.
- **Static page over an item**: creating or moving a page to an address under a template's
  parent where a published item has that slug succeeds, with a `warnings` entry
  `item_address_hidden` in the response (edge case). No refusal.

**Rationale**: each warning is computed by the module that owns the data counted, through
Contracts readers (Principle I).

---

## TB-13 Collection usage by pages, and refusing collection deletion

**Decision**:
- A new Pages table `collection_uses (site_id, subject_kind, subject_id, state, collection_id,
  field_ids text[])`, with `state` one of `draft`, `live` or `scheduled`.
- It is rewritten, for that subject and state, whenever a draft is saved, a version is
  published or rolled back, a schedule is set or cancelled, a template is assigned, or a
  page or component is deleted. It is derived by `CollectionUseExtractor` from the document
  and the page row, like `media_references` (013).
- **Pages.Contracts `ICollectionUsageReader`**:
  - `ListUsesAsync(siteId, collectionId, limit 50)`: the subjects, with kind, id, title and
    states, plus the total;
  - `CountFieldUsesAsync(siteId, collectionId, fieldIds)`.
- **Cms `DeleteCollection`** asks first and refuses with `cms.collection_used_by_pages`,
  listing the uses (FR-010, clarification 2026-10-10).
- **Cms `PreviewCollection`** adds `boundPages: { fieldId: count }`, so the schema builder
  can warn before applying a change that removes or retypes bound fields (FR-011).
- **Race**: a draft that starts using a collection while that collection is being deleted
  can slip through; no transaction spans both modules. The draft then holds a broken
  binding and an `unknown_collection` list. Its next save is refused by rule 14 until the
  list is removed, and rendering treats it as empty. This is recorded rather than prevented
  (Risks).

**Rationale**: the same guard 014 and 015 apply to references, extended to pages, as the
clarification asks.

**Alternatives considered**:
- **Scan every page document at delete time**: up to 2,000 pages × 1 MiB per site, plus
  versions and schedules: too slow, and not indexable.
- **A cross-module lock**: forbidden (Principle I).

---

## TB-14 The editor: binding control, sample item, list preview

**Decision** (admin, `src/admin/src/editor/`):
- **Scopes**: `bindings/scopes.ts` computes the scopes available at a node: the page item
  when the page is a template, and each enclosing list, nearest first. Each scope carries
  its collection's schema, loaded through the existing `api/cms.ts`.
- **Panel**: `panel/controls/BindingControl.vue` appears next to every bindable property
  that is inside a scope (FR-017). It holds:
  - a listbox of compatible fields and built-in values, grouped by scope, with references
    expandable one step;
  - format options and the empty-value choice;
  - for text properties, a text-template editor: chips for values, typed text between them,
    and the keyboard equivalents of 011 (Delete removes a chip; arrow keys move between
    chips).
  - Bound properties show the field's current display name and scope. A broken binding shows
    its reason (`bindingRules.ts`).
- **List settings**: `panel/controls/ListSourceControl.vue`, `ListFiltersControl.vue` and
  `ListSortControl.vue` for the `collection-list` block.
- **Sample item**: on a template page, a picker in the editor's top bar, reusing 015's
  `ReferencePicker` against the template's collection. The choice is kept in
  `localStorage` per page and member. The canvas shows the item's **draft** values with a
  "Draft" badge (clarification 2026-10-10), read through the existing items API
  (`cms.collections.view` guards item reads, and every site role holds it).
- **Lists in the canvas**: a new Cms endpoint, `POST …/cms/sites/{siteId}/list-preview`,
  runs a list's source, filters, sort and limit against **published** data, given the
  enclosing items' ids. It returns the items' published values and their referenced items'
  values for `via` bindings. It needs `pages.page_drafts.edit`, which Designers, Editors
  and Administrators hold; Designers are the only role that edits components, so this one
  capability covers everyone who places lists.
- **Canvas**: `editor/bindings/resolve.ts` mirrors the renderer's resolution (TB-09) for
  the canvas, using the same `bindingRules.ts`.
- **Template mode**: on the page tree, "Use as template…" opens a dialog to choose the
  collection and shows the impact (TB-12). The tree shows the pattern and a template badge.

**Rationale**: reuses the editor's panel, the canvas and 015's picker. The sample-item
choice is a per-person view preference, not page content (spec Key Entities).

---

## TB-15 Item page title and not found

**Decision**: an item page's document title is the item's published name (015's `name`
column, from the title field). When the item has no name, it is the template's published
title (FR-030). The not-found page is 012's, unchanged and identical for every miss
(FR-006).

---

## TB-16 Integration events

**Decision**: Pages publishes `Pages.PageTemplateChanged (SiteId, PageId, CollectionId?,
PreviousCollectionId?)` when a page becomes a template, changes collection or stops being
one (FR-036). Pattern changes are already announced by `Pages.PageAddressesChanged`, which
now includes template pages. Pages handles `Cms.ItemPublished`, `ItemUnpublished`,
`ItemDeleted`, `CollectionChanged` and `CollectionDeleted` (TB-10). All handlers are
idempotent through `processed_events`.

---

## TB-17 Authorization

**Decision**:
- **No new capability**:
  - setting or removing a template: `pages.structure.manage`;
  - bindings and lists in drafts: `pages.page_drafts.edit` (pages) and
    `pages.components.manage` (components);
  - list preview: `pages.page_drafts.edit`;
  - item address in Cms responses: existing item reads.
- Users without a role on the site get 404 everywhere, as before (FR-001).
- The public path is anonymous and read-only, as before.

---

## TB-18 Upgrade

**Decision**:
- **Migrations**:
  - Pages `AddTemplates`: `pages.template_collection_id`, the unique partial index and
    `collection_uses`;
  - Cms `AddRenderStamp`: `items.render_stamp`, default 1, and the partial index on
    `(collection_id, published_slug)` for published items, because 015 indexes only the
    draft `slug` (data-model §1.4).
- **No backfill**: no stored document holds bindings or lists, and no page is a template.
  `collection_uses` starts empty, which is correct.
- `UpgradeFrom035Tests` upgrades a 035 snapshot and checks that every page renders byte for
  byte as before. Only `RendererStamp` changes, and with it the cache keys.

---

## TB-19 Testing strategy

| Suite | What it proves |
|---|---|
| `BindingRulesGoldenTests` (C#) and `bindingRules.spec.ts` | the compatibility table and broken detection, from shared golden examples (FR-014, FR-018) |
| `PageSchemaBindingTests` | rules 13–19 and the new `$defs`, server and admin, from golden documents (FR-013, FR-028) |
| `TemplateTreeTests` (unit) and `SetPageTemplateTests` (integration) | TB-02 rules, the one-per-parent and one-per-collection rules, moves, version conflicts |
| `RouteMatchingTests` | static, then template, then not found; case; draft and unpublished items; another site's slug (FR-005, FR-006) |
| `BindingRenderGoldenTests` | every bindable property × every field type, text templates, formats, `via`, empty and hide, in golden HTML (SC-003) |
| `CollectionListRenderTests` | every operator, sort, limit, paging, empty state, nested scopes, multi-reference source (US4–US6) |
| `ItemPageInvalidationTests` | each row of the TB-10 table, with one and with two instances (SC-005, SC-006) |
| `BindingFailureTests` | removed or retyped fields, deleted collection race, missing media, unpublished references: never a 500 (SC-007, FR-038) |
| `CollectionDeleteGuardTests` | refusal and listing across draft, live and scheduled (FR-010) |
| `TemplateIsolationProbe` | registered in 009's isolation suite (FR-037) |
| `ItemPagePerformanceTests` (smoke, Performance) | SC-004 on the reference setup |
| Admin vitest with axe | binding control, list controls, template dialog, sample picker (SC-008) |
| Release smoke journey | + make a template, publish an item, open its address |

---

## TB-20 Performance

**Decision and measurements to take**:
- **Item page miss**: 3 lookups (static, template, item) plus resolution. 200 blocks and a
  list of 20 cost one `QueryAsync`, one `GetAsync` for references, one `GetSchemasAsync`
  and the existing media and link reads. Target under 200 ms p95 (SC-004).
- **Paginated list of 100 over 10,000 items**:
  - filters on `jsonb` values use a scan within the collection, as 015 IT-14 does for the
    admin list;
  - `count(*)` with the same filter.
  `ItemPagePerformanceTests` measures it. The fallback, if the budget is missed, is
  expression indexes on the sort keys that designers use, recorded and not built.
- **Hits**: unchanged path plus at most one template lookup and one item lookup on indexed
  columns. Target under 20 ms (SC-004).

---

## TB-21 Dependencies

**Decision**: no new package, server or admin. Formatting uses .NET's `CultureInfo` on the
server and `Intl` in the admin. Both are built in.

---

## Things deliberately not built

- Visitor filtering, sorting and search (clarification 2026-10-10).
- Redirects from old item addresses (clarification 2026-10-10, #17).
- More than one reference step in a binding (clarification 2026-10-10).
- Filters relative to the current time, binding inside components to the page item, and
  binding style props (spec, Out of Scope).
- Expression indexes per field, and a `LATERAL` query for nested lists (TB-09, TB-20).

## Configuration keys

None.

## Resolved unknowns

The Technical Context has no `NEEDS CLARIFICATION` entries. The open design questions were:
- the binding representation (TB-04);
- the item cache key (TB-10);
- how a schema change reaches live pages, E-6 (TB-10);
- how collection usage is known for deletion (TB-13).
