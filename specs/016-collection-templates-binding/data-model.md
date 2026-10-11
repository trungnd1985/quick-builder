# Data Model: Collection Templates and Data Binding

**Feature**: `016-collection-templates-binding` | **Date**: 2026-10-10

Tables, document additions, derived records, error codes and events. Decisions are in
[research.md](./research.md) (`TB-xx`). Symbols: **+** new, **~** changed.

---

## 1. Tables

### 1.1 `pages.pages` ~ (Pages)

| Column | Type | Rule |
|---|---|---|
| **+** `template_collection_id` | `uuid null` | The collection this page is the template of. Null for ordinary pages (TB-02). |

Changed meaning of existing columns for a template page:
- `path` is the pattern `<parent path>/{slug}`, for example `/blog/{slug}`;
- `slug` is kept but unused, as for the home page.

| Index | Purpose |
|---|---|
| existing unique `(site_id, path)` | now also: one template per parent (FR-003) |
| **+** unique `(site_id, template_collection_id) where template_collection_id is not null` | one template per collection (FR-002) |

Invariants, enforced by `PageTree`:
- a template page has a parent, is not the home page, and has no child pages;
- `template_collection_id` names a collection of the same site when it is set (checked
  through Cms.Contracts, TB-02).

### 1.2 `pages.live_dependencies` ~ (Pages)

There are no new columns. `target_kind` gains two values (`DependencyKinds`):

| Kind | Target | Written for |
|---|---|---|
| **+** `collection` | collection id | each `collection-list` source in a live page or component version. For a multi-reference source, the field's target collection, read from the schema at publish. |
| **+** `template` | collection id | a live template page: its own collection |

### 1.2a `pages.publications` ~ (Pages)

| Column | Type | Rule |
|---|---|---|
| **+** `page_param_names` | `text[] not null default '{}'` | The `paging.param` values of the live version's paginated lists, written at publish and rollback, so the public path can build the cache key before reading the document (TB-11). |

### 1.3 `pages.collection_uses` + (Pages, TB-13)

| Column | Type | Rule |
|---|---|---|
| `site_id` | `uuid` | global site filter |
| `subject_kind` | `text` | `page` or `component` |
| `subject_id` | `uuid` | |
| `state` | `text` | `draft`, `live` or `scheduled` |
| `collection_id` | `uuid` | |
| `field_ids` | `text[]` | the field ids that bindings and filters of this subject read in this collection, for FR-011 counts |

- **Primary key**: `(site_id, subject_kind, subject_id, state, collection_id)`.
- **Index**: `(site_id, collection_id)`.
- Rewritten for one `(subject, state)` in the transaction that changes that state: draft
  save, publish, rollback, schedule set or cancel, template set, delete.

### 1.4 `cms.items` ~ (Cms)

| Column | Type | Rule |
|---|---|---|
| **+** `render_stamp` | `bigint not null default 1` | Incremented in the transaction of every change to what the item's page shows (TB-10). Never decreases. |
| **+** `published_text` | `jsonb null` | Folded published text values by field id, for `contains` (TB-08). Derived and rebuilt by `ItemIndexer`; null when nothing is published. |

| Index | Purpose |
|---|---|
| **+** `ix_items_collection_id_published_slug` on `(collection_id, published_slug) where published_slug is not null` | `FindBySlugAsync` (TB-03). 015 indexes only the draft `slug`. |

`render_stamp` is incremented for item X when:
1. X is published, scheduled-published, rolled back or unpublished;
2. an item that X's **published** values reference (`item_references`, location
   `published`) goes through any change in 1, or is deleted while published;
3. a media file that X's published values show is deleted or changed (015's
   `ReindexItemsForMedia` handler).

### 1.5 Read model for the public path (Cms, read-only)

`CmsPublishedReadDbContext` maps, with `NoTracking`, a read-only connection, and
`SaveChanges` that throws (012 PR-06):
- `items`: id, site, collection, published slug, name, first and last published time,
  published values, render stamp, status;
- `item_references`: only rows with location `published`;
- `collections`: id, site, schema document.

---

## 2. Page document additions (page schema v1, additive)

The full shapes are in [contracts/binding-expression.md](./contracts/binding-expression.md).

| Addition | Where |
|---|---|
| envelope `bindingVersion` | optional integer `1`; required when the document holds a `bind` or a `collection-list` (rule 13) |
| `props.bind` | optional, on `heading`, `text`, `button`, `image` and `rich-text`, with keys limited per type (TB-05) |
| node `collection-list` | in `contentChild`; children `[list-card, list-empty]` |
| node `list-card`, `list-empty` | only as children of `collection-list`, through a new `listChild` placement; hold `contentChild` |
| `$defs` | `binding`, `bindingValue`, `textTemplate`, `valueFormat`, `listSource`, `listFilter`, `listSort`, `listPaging` |

**Code rules** (page-schema.md §3), run after rule 12, in document order:

| # | Rule | Code | Pointer |
|---|---|---|---|
| 13 | a document with `bind` or `collection-list` carries `bindingVersion: 1` | `binding_version_required` | `/` |
| 14 | every list `source.collectionId` names a collection of the document's site | `unknown_collection` | `…/props/source` |
| 15 | every `scope` names `"page"` or an enclosing `collection-list` id | `unknown_scope` | the `scope` |
| 16 | in a component document: no `"page"` scope, and no multi-reference source outside a list | `page_scope_not_allowed` | the `scope` or `source` |
| 17 | at most two levels of `collection-list` | `list_too_deep` | the third list's `/type` |
| 18 | at most 20 `collection-list` nodes | `too_many_lists` | the 21st list's `/type` |
| 19 | paginated lists' `paging.param` values are distinct | `duplicate_page_param` | the second `…/paging/param` |
| 20 | a `collection-list` has exactly `[list-card, list-empty]` | `list_children_invalid` | `…/children` |

Rule 20 runs with the structural rules (§3 step 5 of page-schema.md, after the schema), so a
list missing its card fails before rules 13–19.

**Page row checks** (not part of the document, done by `SetPageTemplate`):
- `pages.template_needs_parent`;
- `pages.template_has_children`;
- `pages.unknown_collection`;
- `pages.collection_has_template`, which names the existing template's page id;
- `pages.template_parent_taken`, when another template already sits under that parent.

---

## 3. Domain rules and states

### 3.1 Page template state

```text
ordinary ──SetPageTemplate(collection)──► template(collection)
template ──SetPageTemplate(other)──────► template(other)      [bindings to old collection become broken]
template ──SetPageTemplate(null)───────► ordinary             [address becomes parent/slug again]
```

- Every transition requires no child pages and a matching `version`.
- Every transition moves the page's `version` and its publication `generation`.
- Every transition publishes `Pages.PageTemplateChanged`, and `PageAddressesChanged` when
  the path changes.
- Every transition bumps the `collection` and `template` dependents of the old and new
  collection and of the collections referencing them (TB-10).
- The page's publish state does not change. A live page that becomes a template stops
  serving its static address at once. The impact response warns about this.

### 3.2 Item address

```text
address(item) = replace("{slug}", item.published_slug, template.path)
  linked to iff  template is live ∧ item.published_slug ≠ null
  serves item iff linked to ∧ no live static page has that path   (route order, FR-005)
```

It is derived and never stored.

### 3.3 Binding resolution (renderer and canvas)

1. Find the scope: `scope` or the nearest one. If there is none, the binding is broken.
2. Find the item: for `"page"`, the template item; for a list id, the current card's item.
   Then follow `via` if present. A referenced item without published data is empty.
3. Read the value: a field by id, or a built-in value. If the field is not in the current
   schema, or its type does not fit the property (compatibility table), the binding is
   broken.
4. Format the value (contract §4), or build the text template from its parts. A value part
   that is empty contributes nothing; a template whose value parts are all empty is empty.
5. If the result is empty: with `empty: "hide"` the block is not rendered; otherwise the
   prop's fixed value is used.

### 3.4 List evaluation

1. **Source**: a collection, or the item ids of a multi-reference field of a scope's item
   (published values, kept in their stored order).
2. **Filters**: all must hold, against published values. A filter is broken when its field
   is gone, its operator no longer fits the type, or its literal no longer fits the type.
   A broken filter matches nothing.
3. **Sort**: the sort keys, then the field order for a multi-reference source, then the
   item id. Items without a value in a key come last.
4. **Count**: `limit` without paging. With paging, `pageSize` items per page over every
   matching item, up to page 10,000; `limit` does not apply.
5. **Render**: the card once per item, or the empty state.

---

## 4. Error codes (new)

| Code | HTTP | When |
|---|---|---|
| `pages.template_needs_parent` | 400 | template at top level or as home |
| `pages.template_has_children` | 409 | page has children; or creating or moving a page under a template |
| `pages.unknown_collection` | 400 | the template collection is not a collection of the site |
| `pages.collection_has_template` | 409 | the collection already has a template (`details.pageId`) |
| `pages.template_parent_taken` | 409 | the parent already holds a template |
| `pages.invalid_document` with rules 13–20 | 400 | §2 |
| `cms.collection_used_by_pages` | 409 | delete refused; `details.uses` (≤50) and `details.total` |

Warnings (200 responses, `warnings[]`):
- `items_change_address` with `count`;
- `items_lose_address` with `count`;
- `item_address_hidden` with `itemId`.

---

## 5. Events

| Event | Producer | Payload | Consumers |
|---|---|---|---|
| **+** `Pages.PageTemplateChanged` | Pages | `SiteId, PageId, CollectionId?, PreviousCollectionId?, ActorUserId?` | #17 (sitemaps); none in this feature |
| `Pages.PageAddressesChanged` ~ | Pages | unchanged; now also emitted for template pattern changes | unchanged |
| `Cms.ItemPublished`, `ItemUnpublished`, `ItemDeleted` | Cms | unchanged | **+** Pages `RefreshPagesListingItems` |
| `Cms.CollectionChanged`, `CollectionDeleted` | Cms | unchanged | **+** Pages `RefreshPagesUsingCollection` (closes E-6) |
| `Media.FileDeleted`, `FileChanged` | Media | unchanged | ~ Pages `RefreshPagesShowingMedia` (+ list pages); ~ Cms (+ render stamps) |

Events carry ids, numbers and flags only, never values, names or slugs.

---

## 6. Cache keys

| Page kind | Key parts (all under `CacheKey.ForSite(siteId, …)`) |
|---|---|
| static page | `page`, page id, generation, design number, site version, base path, renderer stamp, **+** `PageParams` |
| **+** item page | `item`, template page id, template generation, item id, item render stamp, design number, site version, base path, renderer stamp, `PageParams` |

`PageParams` holds the sorted `param=value` pairs of the document's paginated lists, with
values from 2 to 10,000 only (TB-11). The ETag is derived from the key, as in 012.
