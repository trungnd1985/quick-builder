# Data Model: Navigation and Menus

**Feature**: `024-navigation` | **Date**: 2026-10-14 | **Research**: [research.md](./research.md)

Everything new is in the `pages` schema, owned by the Pages module (NV-01). Every table carries
`site_id` with the global site filter (constitution Principle V). Names follow the existing
snake_case conventions.

## 1. Tables

### 1.1 `pages.menus` (new): the `Menu` aggregate

| Column | Type | Rule |
|---|---|---|
| `id` | `uuid` PK | `Guid.CreateVersion7()` |
| `site_id` | `uuid` not null | site filter; FK to nothing (sites are another module) |
| `name` | `citext` not null | trimmed, 1–100 characters; unique `(site_id, name)` |
| `created_at_utc`, `created_by` | `timestamptz`, `uuid` | set once |
| `updated_at_utc`, `updated_by` | `timestamptz`, `uuid` | rename, duplicate source untouched |
| `xmin` | system | `Version`, the concurrency token for rename and delete |

Index: `ux_menus_site_name (site_id, name)`.
Invariant: at most 50 menus per site (`Menu.Create`, checked in the create transaction under
the site's menu count; a lost race is retried as in 010's tree writes).

### 1.2 `pages.menu_drafts` (new)

| Column | Type | Rule |
|---|---|---|
| `menu_id` | `uuid` PK, FK → `menus.id` cascade | one draft per menu |
| `site_id` | `uuid` not null | site filter |
| `document` | `jsonb` not null | a valid menu document ([menu-document.md](./contracts/menu-document.md)) |
| `schema_version` | `int` not null | equals `document.schemaVersion` |
| `xmin` | system | the draft version; publish compares it (`PublishedFromDraftVersion`) |

A new menu starts with `{ "schemaVersion": 1, "entries": [] }`.

### 1.3 `pages.menu_targets` (new): index of the draft's targets (NV-04)

| Column | Type |
|---|---|
| `site_id` | `uuid` |
| `menu_id` | `uuid`, FK → `menus.id` cascade |
| `target_kind` | `text`: `page` \| `item` |
| `target_id` | `uuid` |

PK `(site_id, menu_id, target_kind, target_id)`. Reverse index
`ix_menu_targets_target (site_id, target_kind, target_id)`. Rewritten in the draft save
transaction.

### 1.4 `pages.menu_usages` (new): index of drafts that show a menu (NV-06)

| Column | Type |
|---|---|
| `site_id` | `uuid` |
| `subject_kind` | `text`: `page` \| `component` |
| `subject_id` | `uuid` |
| `menu_id` | `uuid`, FK → `menus.id` **cascade** (a delete removes the rows, drafts keep a dangling `menuId`) |

PK `(site_id, subject_kind, subject_id, menu_id)`. Index `ix_menu_usages_menu (site_id, menu_id)`.
Rewritten in every page and component draft save, in `DraftWrites`, beside
`component_usages`. Removed with the page or component.

### 1.5 `pages.menu_changes` (new): audit (FR-005, NV-18)

| Column | Type | Rule |
|---|---|---|
| `id` | `uuid` PK | |
| `site_id` | `uuid` | |
| `menu_id` | `uuid` | no FK: the record outlives the menu |
| `action` | `text` | `created` \| `renamed` \| `duplicated` \| `draft_saved` \| `deleted` |
| `actor_user_id` | `uuid` | |
| `at_utc` | `timestamptz` | |

No names, labels or addresses (Principle IX). Publishing actions are in `publish_log`.

### 1.6 Existing tables reused with a new subject kind (no column change)

| Table | Use for menus |
|---|---|
| `publications` | `subject_kind = 'menu'`. `generation` is incremented as for every subject, but nothing reads it for menus (menus are not cached on their own). |
| `published_versions` | the live and earlier menu documents. `title` = the menu's name at publish (rollback restores it, as for page titles; NV-05). Retention: 50 versions (012 PR-15). |
| `publish_schedules` | scheduled publish and unpublish of a menu. |
| `publish_log` | label = the menu's name; actions as for components, plus `deleted`. |
| `live_dependencies` | subject `menu` → targets `page`, `item`, `template`; subjects `page` and `component` → target `menu` (§3). |

`SubjectKinds.Menu = "menu"`; `DependencyKinds.Menu = "menu"`, `DependencyKinds.Item = "item"`.
If a check constraint lists subject or target kinds, the `AddMenus` migration widens it.

## 2. Documents

### 2.1 Menu document v1

Defined in [contracts/menu-document.md](./contracts/menu-document.md). In short:

```text
MenuDocument  = { schemaVersion: 1, entries: Entry[] }
Entry         = { id, label?, target, newTab?, hidden?, children: Entry[] }
Target        = { type: "page", pageId, anchor? }
              | { type: "item", collectionId, itemId }
              | { type: "url", href }
              | { type: "anchor", anchor }
              | { type: "none" }
```

### 2.2 Page schema v1 addition: `navigation` node

Defined in [contracts/navigation-block.md](./contracts/navigation-block.md):
`menuId`\*, `layout`, `levels`, `collapse { enabled, below, buttonLabel? }`, `align`,
`ariaLabel`, `color`, `activeColor`, `layerLabel`. In `contentChild`; no children.

## 3. Dependency graph additions (NV-07)

```text
page ──(menu)──────▶ menu ──(page)──────▶ page
component ─(menu)──▶ menu ──(item)──────▶ item
                     menu ──(template)──▶ collection's template
page ─(component)─▶ component
```

Written at publish from the live document:

| Subject | Extracted from | Rows |
|---|---|---|
| page, component | each `navigation` node | `(menu, menuId)` |
| menu | each `page` target | `(page, pageId)` |
| menu | each `item` target | `(item, itemId)` and `(template, collectionId)` |

`GenerationBump.AffectedPagesAsync(targets)`:
1. dependents of `targets` → pages P₁, components C₁, menus M;
2. dependents of `(menu, M)` → pages P₂, components C₂;
3. dependents of `(component, C₁ ∪ C₂)` → pages P₃;
4. result P₁ ∪ P₂ ∪ P₃.

## 4. State and transitions

### 4.1 Menu publication (as components, 012 data-model §4.1)

```text
            publish / scheduled publish
 never ───────────────────────────────▶ live ◀──┐ publish, rollback (new version)
 published                               │  └───┘
    ▲                                    │ unpublish (refused while in use, FR-003)
    │                                    ▼
    └──────────── delete ─────────── not live ──── publish ───▶ live
          (refused while live pages or components use it; allowed otherwise,
           taking down a live, unused menu)
```

### 4.2 Guards

| Action | Refused when | Error |
|---|---|---|
| Publish, schedule run, rollback of a page or component | a `navigation` node names a menu that does not exist or is not live | `pages.unpublished_menus` (with ids and names) |
| Rollback of a page or component | the version names a deleted menu | `pages.version_missing_menus` |
| Unpublish (manual or scheduled) a menu | a live page or component depends on it | `pages.menu_in_use_by_published_content` (`pages`, `components` counts) |
| Delete a menu | same | same |
| Create a menu | 50 menus already | `pages.menu_limit_reached` |
| Create or rename | name empty, > 100, or taken | `pages.invalid_menu_name`, `pages.duplicate_menu_name` |
| Save draft, rename, delete | stale version | `pages.out_of_date` |
| Save draft | document invalid | `pages.invalid_menu_document` (`rule`, `pointer`) |

### 4.3 Target state (derived, shown in the admin, FR-025)

| Kind | `live` | `not_published` | `deleted` | `no_page` |
|---|---|---|---|---|
| page | page has a live version | page exists, not live | no page with that id in the site | — |
| item | published, and its collection has a live template | exists, not published | no such item in the site | published, but no live template |
| url, anchor, none | always shown | — | — | — |

## 5. Derived models in Rendering (not stored)

- **ResolvedEntry**: label, href or null, external, new tab, state (`current`, `containing`,
  `section`, none), children. Produced by `MenuModel.Resolve` (NV-10), then
  `ActiveState.Mark` (NV-11).
- **RenderInputs** gains `CurrentItemId` (the item of an item page, for active state), `Menus` (`LiveMenu` by id: document and published name), `MenuItems` (published items by
  id) and `ItemPatterns` (template patterns by collection). `PageLinkTarget` gains `LiveTitle`.

## 6. Audit, events and logs

- **Integration events**: none new. Pages adds a handler for `Cms.ItemPublished`,
  `Cms.ItemUnpublished` and `Cms.ItemDeleted` (NV-08).
- **Menu publishing writes no integration event.** `PagesOutbox` maps a
  `SubjectPublishedEvent` that is not a page to `Pages.ComponentPublished` today, so it gains
  an explicit `SubjectKinds.Menu` case that adds nothing. Everything a menu change affects is
  refreshed in its own transaction (NV-07), and no module consumes menu changes. An event is
  added when a consumer needs one (Principle XI). `PagesOutboxTests` pins the mapping.
- **Logs**: ids and counts only. No menu names, labels, anchors or addresses.

## 7. Cache keys

Unchanged. Pages that show a menu are refreshed through their existing `generation` (012 PR-05).
Item pages refresh through their template's generation, which the graph bumps when the template
shows a menu. No new cache entry exists.
