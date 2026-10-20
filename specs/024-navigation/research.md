# Research: Navigation and Menus

**Feature**: `024-navigation` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

Decisions are numbered `NV-xx`. Each has a decision, a rationale and the alternatives
considered. The spec's six clarifications (2026-10-14) are inputs, not open questions:
menus are drafted and published like components (FR-004, FR-004a); anchors are typed names
(FR-007a); a menu in use cannot be unpublished or deleted, and nothing that shows an
unpublished menu can be published (FR-003, FR-003a); submenus open with their own button
(FR-019); collapsing follows the mobile or tablet preview width (FR-017); and submenus on the
current path start open in vertical and collapsed menus (FR-019a).

---

## NV-01 Menus are a third publishing subject in the Pages module

**Decision**:
- Menus live in the **Pages** module, beside components, as a third publishing subject kind,
  `menu` (`SubjectKinds.Menu`).
- They reuse, unchanged in shape, everything 012 built for components:
  - `publications` (live pointer, numbering, render generation unused for menus);
  - `published_versions` (the immutable menu document);
  - `publish_schedules` (scheduled publish and unpublish);
  - `publish_log` (history, with the menu's name as the label);
  - `live_dependencies` (what a live menu points at, and who shows it).
- `PublishingWrites` learns the new kind in its three switch points: `SubjectAsync`,
  `DraftAsync` and the validator choice in `PublishContentAsync`.
- **Rendering** renders menus; **Cms** is read for item targets; **Editor** is not involved
  on the server.

**Rationale**: the clarified spec asks for exactly the component lifecycle: draft, publish,
versions, rollback, schedules, history, and "refused while in use". 012 built that once,
atomically and with a tested race model (PR-03). A menu's targets are pages and items, and the
pages that show a menu are pages, so the refresh graph (`live_dependencies`,
`GenerationBump`) is already in this module. A menu publish must refresh pages in the same
transaction (Principle VIII), which rules out any other module.

**Alternatives considered**:
- **A new `Navigation` module**: the constitution's module list is closed (Principle I), so
  this needs an amendment. It would also have to refresh Pages' generations through events,
  adding a window in which visitors see a stale menu, and duplicate publishing.
- **Menus as components holding a special block**: no separate list, no entry tree outside
  the page schema, and a header component could not embed a menu shared with the footer.
- **Menus as site settings saved live** (like redirects): rejected by clarification 1.

---

## NV-02 Storage: `menus` and `menu_drafts`, mirroring components

**Decision**:
- `pages.menus`: id, site, name (`citext`, unique per site), created and updated by and at,
  `xmin` as `Version`. The `Menu` aggregate enforces the name rules and the per-site limit.
- `pages.menu_drafts`: one row per menu, holding the draft document (`jsonb`) and its
  `schema_version`. Its `xmin` is the draft version that publish compares against
  (`PublishedFromDraftVersion`), exactly as for components.
- A rename touches `menus` only, so it never marks the draft as changed.
- Deleting a menu deletes its draft, publication, versions, schedules, live dependencies and
  indexes in one transaction, through the existing `PublishingWrites.ForgetAsync`.

**Rationale**: the split is the one 010 chose for components, for the same reason: renaming
must not make "has unpublished changes" true. `ForgetAsync` already removes every publishing
record of a subject.

**Alternatives considered**:
- **Entries as rows** (`menu_entries` with parent and position): versions and rollback need a
  copy of the whole tree anyway, and a document is what `published_versions` stores. A tree of
  at most 300 entries is small as one `jsonb` value.
- **The draft on the `menus` row**: a rename would change the draft's version.

---

## NV-03 The menu document is its own versioned contract

**Decision**: a new contract, `docs/contracts/menu.md`, with `menu/v1.schema.json` and golden
examples. The draft is in [contracts/menu-document.md](./contracts/menu-document.md).

```json
{
  "schemaVersion": 1,
  "entries": [
    { "id": "e1", "target": { "type": "page", "pageId": "…" }, "children": [
      { "id": "e2", "label": "Status", "target": { "type": "url", "href": "https://status.example" },
        "newTab": true, "children": [] }
    ] },
    { "id": "e3", "label": "Company", "target": { "type": "none" }, "children": [] }
  ]
}
```

- **Entry**: `id` (same pattern as node ids), optional `label`, `target`, optional `newTab`
  (default false), optional `hidden` (default false), `children` (always present).
- **Targets**: `page` (`pageId`, optional `anchor`), `item` (`collectionId`, `itemId`), `url`
  (`href`, schemes as page links), `anchor` (`anchor`), `none`.
- **Rules beyond the schema**, checked in code in a fixed order with a rule name and JSON
  Pointer as page-schema.md §3 does:
  1. ids unique (`duplicate_entry_id`);
  2. at most 300 entries (`too_many_entries`);
  3. at most 3 levels (`too_deep`);
  4. a label (after trimming) is required for `url`, `anchor` and `none` (`label_required`);
  5. `newTab` only on linking targets (`new_tab_not_allowed`);
  6. on the server, last: new or changed `page` and `item` targets name a page or item of the
     menu's site, and an item belongs to the named collection (`unknown_target`, NV-04).
- Labels: at most 100 characters, no control characters (schema pattern). Anchors:
  `^[A-Za-z][A-Za-z0-9_-]{0,99}$`; the editor strips a leading `#` before validating
  (FR-007a).
- Both validators embed the same schema file: the server's `MenuDocumentValidator` over the
  existing `SchemaSubsetValidator`, and the admin's `menuValidator.ts` over
  `subsetValidator.ts`. A shared golden suite runs on both (`MenuSchemaGoldenTests`,
  `menuSchema.spec.ts`).

**Rationale**: the menu is stored data shared between the admin and the renderer, so
Principle VI requires a versioned contract that both sides validate against. It is not a page
document (no `root`, no nodes), so it is not a page schema version.

**Alternatives considered**:
- **Entries as page-schema nodes** (a `menu-entry` node type): would make menus page
  documents, letting blocks appear in menus and every page-schema rule apply to them.
- **Flat entries with `parentId` and `position`**: easier to move one entry, but every reader
  then rebuilds the tree, and depth and cycles become rules instead of shape.

---

## NV-04 Target identity, and checking only what changed

**Decision**:
- `page` and `item` targets are ids, so they survive moves, renames and slug changes. An
  `item` target also stores its `collectionId`, so the menu knows which template's changes
  affect it even while the item is unpublished (NV-07).
- **Stored ids may name nothing**: a page or item deleted later stays in the document and
  shows as broken. Every save would otherwise be refused until every broken entry was fixed.
- **Save check** (rule 6): a `page` or `item` target that is not in the menu's
  `menu_targets` index (the targets of the previously saved draft) must name a page, or an item
  of the named collection, of the menu's site. That uses `Page` and
  `ICollectionSchemaReader.GetItemAddressPartsAsync` (017). An unchanged broken target is
  accepted; a new reference to another site's page or item is refused as unknown (FR-035).
- `pages.menu_targets` (site, menu, kind, target id) is rewritten with every draft save. It
  also answers "which menus point at this page or item" (NV-13).

**Rationale**: it is the same "may name nothing" stance that page-schema §2a takes for
`pageId`, with a guard that keeps cross-site ids out (Principle V). The index makes the guard
a set difference instead of a document diff.

**Alternatives considered**:
- **Check every target on every save**: one deleted page makes the whole menu unsaveable.
- **Never check**: a forged request could store another site's ids. Reads are site-filtered,
  so nothing would leak, but FR-035 asks for refusal, and the isolation suite tests for it.

---

## NV-05 The Navigation block: one new page-schema node type

**Decision**: page schema v1 gains the node type `navigation` (additive, §6), in
`contentChild`, so it can go in any Section or Container of a page or component.

| Prop | Type | Default |
|---|---|---|
| `menuId`\* | UUID | — |
| `layout` | `horizontal` \| `vertical` | `horizontal` |
| `levels` | 1 \| 2 \| 3 | 3 |
| `collapse` | `{ "enabled": bool, "below": "mobile" \| "tablet", "buttonLabel"?: string }` | `{ "enabled": false, "below": "mobile" }` |
| `align` | `start` \| `center` \| `end` | `start` |
| `ariaLabel` | string, ≤ 100, no control characters | the menu's name |
| `color`, `activeColor` | `colorRef` | the block's defaults |
| `layerLabel` | as every block | — |

- `menuId` **may name a menu that no longer exists**, like a page link's `pageId`. The editor
  marks it; publishing refuses it (NV-06). No document rule needs the site, so the page
  schema's server-only rules are unchanged.
- `buttonLabel` defaults to the translated "Menu" in the site's default language.
- The `ariaLabel` default is the **live** menu's name: `PublishContentAsync` stores the menu's
  name as the version's `title`, and `GetLiveMenusAsync` returns it. A rename touches only
  `menus.name`. Like any draft change, it reaches visitors at the next menu publish, so the
  published site never changes without a publish (Principle VII), and the existing
  `(menu, id)` refresh covers it (Principle VIII). A rollback restores that version's name, as
  page rollbacks restore the title.
- The block definition follows docs/contributing.md: `$defs`, placement, golden examples,
  editor `definition.ts` and canvas component, renderer.
  `BlockRegistryCoverage.spec.ts` and `BlockRendererCoverageTests` catch a missing piece.

**Rationale**: FR-016 and FR-017 list exactly these properties. Two colour tokens give the
active state a visible difference within the design system (FR-030) without per-entry styles,
which are out of scope.

**Alternatives considered**:
- **Embedding the menu tree in the block**: that is the problem the feature removes.
- **A required site rule for `menuId`** (like `componentId`, rule 6): a draft could no longer
  be saved after its menu was deleted, which FR-003 allows.

---

## NV-06 Guards: what may be published, unpublished and deleted

**Decision**:
- **Page or component publish** (manual, scheduled or rollback) goes through
  `PublishContentAsync`. Next to the component check (012 FR-003), it refuses menus that do
  not exist or are not live: `pages.unpublished_menus` (FR-003a), naming the menus. A
  scheduled run fails with the same reason, which is logged and shown as for a missing
  component.
- **Rollback** to a version whose Navigation block names a deleted menu is refused before
  publishing: `pages.version_missing_menus`, like `VersionMissingComponents`.
- **Menu unpublish** (manual or scheduled) is refused while any live page or component has a
  `menu` dependency on it: `pages.menu_in_use_by_published_content`, with the counts of pages
  and components (FR-003).
- **Menu delete**:
  - refused for the same reason while live pages or components show it;
  - otherwise allowed. The response to a preview request
    (`GET …/menus/{id}/usages`) lists the drafts that use it (`menu_usages`), and the admin
    asks for confirmation. A live menu that nothing live shows is taken down by the delete.
- `pages.menu_usages` (site, subject kind, subject id, menu) is rewritten with every page and
  component draft save, like `component_usages`, but without a restricting foreign key: a
  delete removes its rows by cascade and leaves the drafts holding a dangling `menuId`.

**Rationale**: mirrors 012 FR-003, FR-004 and the rollback check one for one (clarification
3). The usage index serves the delete confirmation and the menu list's "used by" column,
without scanning documents.

**Alternatives considered**:
- **A restricting foreign key from drafts** (as `component_usages` has): would refuse a delete
  that FR-003 allows when only drafts use the menu.
- **A `jsonb` containment query over drafts** instead of an index: needs raw SQL or a GIN
  index on every document, for one screen.

---

## NV-07 The refresh graph gains two kinds and one hop

**Decision**:
- `DependencyKinds` gains `menu` and `item`.
- **Pages and components** record `(menu, id)` for each Navigation block, from
  `DependencyExtractor` at publish.
- **A live menu** records, at publish:
  - `(page, id)` for each `page` target (with or without an anchor);
  - `(item, id)` for each `item` target;
  - `(template, collectionId)` for each item target's collection, which the existing template
    changes already bump (016 TB-10: template assigned, moved, published or unpublished).
- `GenerationBump.AffectedPagesAsync` walks one more level. Dependents of the targets that are
  menus lead to the pages and components that show those menus, and components lead to pages
  as before:

  ```text
  target ─▶ pages, components, menus
  menus  ─▶ pages, components
  components ─▶ pages
  ```

  Menus do not nest, and components do not nest, so the walk is bounded at three queries.
- **What now refreshes pages that show a menu**:

  | Change | Existing path | New hop |
  |---|---|---|
  | Menu published, rolled back (also by schedule) | `AfterLiveChangeAsync` bumps `(menu, id)` | the menu kind in `AfterLiveChangeAsync` |
  | Target page published, unpublished, retitled at publish | bumps `(page, id)` | menu → pages |
  | Target page moved, renamed slug, made home | `BumpLinkersAsync` bumps `(page, id)` | menu → pages |
  | Template of an item target's collection changed | `BumpForCollectionsAsync(includeTemplates)` bumps `(template, id)` | menu → pages |
  | Item published (name or slug changed), unpublished, deleted | none for single items | new handler bumps `(item, id)` (NV-08) |

- A menu publish bumps no menu generation: menus are never cached on their own. Only pages are
  (NV-09).

**Rationale**: Principle VIII asks for exact refreshes from a tracked graph. The graph already
answers "who shows this", so menus are one more edge type, recorded in the publish
transaction, before any visitor can see the version (012 PR-04).

**Alternatives considered**:
- **Copying a menu's targets into every page's dependencies**: every menu publish would
  rewrite the dependency rows of every page that shows it.
- **A site-wide "menus version" in every page key**: one menu change would refresh every page
  of the site, including the many that show no menu or another menu.

---

## NV-08 Item changes reach menus through a new event handler

**Decision**: `RefreshPagesShowingMenuItemsHandler` in Pages handles `Cms.ItemPublished`,
`Cms.ItemUnpublished` and `Cms.ItemDeleted` (when it was published). It bumps
`(item, ItemId)` through `GenerationBump`, deduplicated by `RefreshOnce`, so it is idempotent
like the 016 handlers. `ItemPublished` covers name and slug changes, because both reach
visitors only by publishing.

**Rationale**: item lists refresh by collection (016), which is too broad for menus: one item
publish would refresh every page whose menu points at any item of the collection. The event
already carries the item id, and the outbox gives the 5-second freshness that FR-033 requires.

**Alternatives considered**:
- **Extending `RefreshPagesListingItemsHandler`**: mixes two refresh rules in one
  deduplication key, so a failure in one would retry the other.

---

## NV-09 Rendering: the menu is part of each page's render

**Decision**:
- `RenderReferences` collects `menuId`s from `navigation` nodes in the page and in its
  components.
- `IPublishedContentReader.GetLiveMenusAsync(siteId, menuIds)` returns live menu documents with
    their published names (`LiveMenu`)
  (absent when not live), on the read-only connection.
- Targets are read in the existing batches:
  - pages: `GetLinkTargetsAsync`. `PageLinkTarget` gains `LiveTitle`, the live version's
    title, for entries without their own label;
  - items: `IPublishedItemReader.GetAsync` (name, slug, collection), then
    `GetLiveTemplatePatternsAsync` for the collections, as 016's item links do.
- The menu is rendered **inside the page's render**, so its active state is the page's, and it
  is cached with the page under the page's key. Item pages are cached per item (016), so they
  carry their own active state too. No fragment cache is added.
- A render with menus does at most three more reads (menus, items, template patterns), each
  one batched query, and none when the page shows no menu.

**Rationale**: active state depends on the page being rendered, so a shared menu fragment
cache would need a key per page anyway. The page cache already has the right key and is
refreshed by the graph (NV-07).

**Alternatives considered**:
- **A cached menu fragment with active state patched in on the way out**: string surgery on
  cached HTML, against the renderer's allow-list design.

---

## NV-10 Link integrity on the site

**Decision**: `MenuModel.Resolve` turns a live menu document into the entries visitors see,
before writing any HTML:
- **Page target**: live → link to `basePath + path` (+ `#anchor`); not live or missing → no
  link.
- **Item target**: published item of a collection with a live template → link to the
  pattern with the item's slug (016); otherwise no link.
- **URL target**: `LinkResolver` rules (schemes, `rel="noopener noreferrer"` for a new tab).
- **Anchor target**: `#anchor` on the current page.
- **None**: no link.
- **Labels**: the entry's own label, else the page's live title or the item's published
  name.
- **Pruning** (FR-023, FR-024): depth first. An entry with `hidden` is dropped with its
  children. An entry whose target has no link is dropped unless it keeps at least one shown
  child, in which case its label is shown without a link. A `none` entry with no shown
  children is dropped. Levels beyond the block's `levels` are dropped. A menu with nothing
  left renders nothing, not even an empty `<nav>`.
- A new tab is announced to assistive technology with visually hidden text, "(opens in a new
  tab)", translated in the site's default language like the not-found texts (012
  `PublicTexts`).

**Rationale**: FR-022 to FR-024 and the clarified "hidden over broken" assumption. Pruning
before writing keeps the HTML writer simple and makes the golden tests read as data.

**Alternatives considered**:
- **Plain text for every broken entry** (as 012 does for links in text): rejected in the spec's
  assumptions.

---

## NV-11 Active state is a pure function of paths

**Decision**: `ActiveState.Mark(entries, current)` runs after pruning. `current` is the page
id, or the item id and its address for an item page, plus the current path.
1. **Current**: entries whose target is the current page or item (page targets with an anchor
   included). Every match is marked (edge case "same target twice").
2. **Containing**: every ancestor entry of a current entry.
3. **Section**, only when step 1 found nothing: among page entries other than the home page,
   the one whose live path is the longest segment-wise prefix of the current path. For an
   item page, the current path is the item's address, so a "Blog" entry at `/blog` is the
   section of `/blog/hello-world`. Its ancestors are containing.
4. **Never**: `url`, `anchor`, `none` and hidden entries. The home page entry is current only
   on the home page, and never a section.
5. Query strings and fragments are never part of the comparison, so `?page=2` changes
   nothing (FR-029).
6. Entries pruned by `levels`: their nearest shown ancestor is marked containing, so the trail
   stays visible when a block shows fewer levels.

**Output**: the current entry's link carries `aria-current="page"`, and section and
containing entries carry classes (`qb-nav__item--section`, `--containing`) and are styled with
`activeColor`. **Rationale**: assistive technology announces exactly one "current page", as
FR-030 asks, while the trail is shown visually.

**Rationale**: a pure function over resolved paths is unit-testable against the whole matrix
of SC-004 without a database, and the page tree's paths are already normalised.

**Alternatives considered**:
- **Matching by page tree ancestry ids**: needs the ancestor chain of the current page, one
  more read, and gives the same answer as path prefixes, because addresses follow the tree.
- **Client-side highlighting**: rejected by FR-030 (server-side, no scripts).

---

## NV-12 HTML and accessibility: the disclosure pattern on native elements

**Decision**:

```html
<nav class="qb-nav qb-nav--horizontal" aria-label="Main">
  <ul class="qb-nav__list">
    <li class="qb-nav__item qb-nav__item--containing">
      <a href="/services">Services</a>
      <details class="qb-nav__sub" name="qb-nav-1-l1">
        <summary><span class="qb-nav__hidden">Show submenu: Services</span></summary>
        <ul class="qb-nav__list">…</ul>
      </details>
    </li>
    <li class="qb-nav__item"><details class="qb-nav__sub" name="qb-nav-1-l1">
      <summary>Company</summary>  <!-- a no-link entry: its label is the button (FR-019) -->
      <ul>…</ul></details></li>
  </ul>
</nav>
```

- Each entry with shown children has a `<details>` whose `<summary>` is the open/close button.
  Browsers give it button semantics, Enter and Space, touch, and an expanded state for
  assistive technology, **without scripts** (FR-020).
- The label stays a separate link (clarification 4). For a `none` entry, the label is inside
  the `summary`.
- Sibling submenus share a `name` (per block and level), so opening one closes the others at
  that level (FR-019). This is native in current browsers. NV-14's script provides it in older
  ones.
- **Horizontal**: submenus are absolutely positioned dropdowns. **Vertical**: they open
  inline.
- `ariaLabel` (default: the menu's name) names the `<nav>` landmark.

**Rationale**: `details`/`summary` is the one native disclosure control that works with
the keyboard, touch and no scripts, and it keeps the public CSP free of script for the core
behaviour.

**Alternatives considered**:
- **ARIA menu pattern** (`role="menu"`, roving focus): meant for application menus. For site
  navigation it hides links from browse mode, and it needs scripts to work at all.
- **`<button aria-expanded>` driven by a script**: needs the script for the basic behaviour,
  against FR-020.
- **Popover API**: top-layer placement breaks inline vertical submenus, and anchoring
  dropdowns needs CSS anchor positioning, which is not yet baseline.

---

## NV-13 Collapsing below a preview width

**Decision**:
- With `collapse.enabled`, the list is wrapped in
  `<details class="qb-nav__panel" open><summary>Menu</summary>…</details>`, rendered **open**.
- The block stylesheet's container queries on the `page` container use the editor's preview
  widths (011): `max-width: 480px` (mobile) and `max-width: 767px` (tablet). Below the
  block's `below` width the panel's `summary` is shown and the list stacks vertically. Above
  it, the `summary` is hidden.
- The enhancement script (NV-14) closes the panel when the block is narrower than its width,
  on load and on resize, and reopens it when wider. It runs inline right after the first
  Navigation block, so the panel is closed before first paint.
- **Without scripts** the panel stays open: every entry is visible, and the `summary` still
  toggles it natively (FR-020).
- The same CSS serves the editor canvas, so the responsive preview shows the collapsed menu
  (011's container-query decision ED-09).

**Rationale**: clarification 5 ties collapsing to the preview widths, which are already the
stylesheet's two breakpoints. Rendering the panel open means the fallback is "everything
visible", never "everything hidden".

**Alternatives considered**:
- **Panel rendered closed, opened by CSS on wide screens** (`::details-content`): newly
  baseline in 2025 and not in every supported browser. An older browser would hide the whole
  menu on desktop.
- **A pixel width per block**: rejected by clarification 5.

---

## NV-14 One small inline script, pinned by hash in the CSP

**Decision**:
- Pages that show at least one Navigation block carry one inline script, at most 2 KB
  minified (FR-020 allows 10 KB). It:
  1. closes the innermost open submenu on Escape and returns focus to its `summary`
     (FR-019);
  2. closes other submenus at the same level where `name` grouping is not native;
  3. closes horizontal dropdowns on a click outside;
  4. collapses and expands panels by width (NV-13);
  5. in a horizontal block, on opening a collapsed panel, opens the submenus on the current
     path (FR-019a).
- The script is a constant in the Rendering assembly (`NavScript`), with its SHA-256 computed
  once. The public CSP gains `script-src 'sha256-…'` **only on pages that carry it**. Every
  other page keeps 012's CSP byte for byte, with no script allowed.
- It reads no content, makes no requests, and has no configuration. Golden tests pin its
  bytes and hash together (`NavScriptTests`).

**Rationale**: Escape-to-close (FR-019) and width-based collapsing cannot be done without
script. A hash-pinned inline script admits exactly these bytes, which is stricter than
`'self'` (which would admit any same-origin script). Inline means no extra request and no
flash of an open menu on phones.

**Alternatives considered**:
- **`/_assets/nav.<hash>.js` with `script-src 'self'`**: admits every same-origin script path,
  and adds a request and a flash before the deferred script runs.
- **No script**: fails FR-019's Escape behaviour and leaves collapsed menus open on phones.

---

## NV-15 Starting state of submenus (clarification 6)

**Decision**:
- **Vertical** blocks: the server writes `open` on each `<details>` whose entry is current,
  containing or section (NV-11). Other submenus start closed.
- **Horizontal** blocks: no submenu has `open`, so dropdowns never cover the page on load.
  When a horizontal block collapses and its panel is opened, the script (NV-14, step 5) opens
  the submenus marked `data-qb-current-path`.
- Without scripts a horizontal collapsed panel shows its submenus closed, and every entry is
  still reachable through its `summary` (FR-020).

**Rationale**: FR-019a, with the server producing the starting state wherever the layout
allows it, as FR-030 asks.

---

## NV-16 Editor: the Navigation block on the canvas

**Decision**:
- `editor/blocks/navigation/`: `definition.ts` and `NavigationBlock.vue`. The canvas draws the
  chosen menu's **draft** (FR-018) from `GET …/menus/{id}/preview`. The preview returns the
  resolved draft tree: entries with their display label (own label, else the current page
  title or item name), target state, and depth. The canvas uses the same CSS classes as the
  renderer, so container queries collapse it at the preview widths.
- Links never navigate in the canvas (as for the Button block). Submenus open with the same
  `details` elements, so a designer can check them.
- New control kinds:
  - `menu`: a menu picker listing the site's menus, with "Open in Menus" for members with
    `pages.menus.manage`;
  - `navCollapse`: the on/off switch, the width choice and the button label.
- Missing or unpublished menus are marked on the block, and in the publish dialog as the
  server's `pages.unpublished_menus` refusal (FR-003a), as missing components are.
- Menu drafts change rarely during a page edit, so the canvas fetches the preview when the
  block is selected or the editor gains focus.

**Rationale**: the property panel is driven by the block definition (011 ED-08). Only the menu
picker and the collapse group need new controls.

---

## NV-17 Admin: the menu editor

**Decision**:
- **Routes**: `/sites/:siteId/menus` (`MenusView`: list with name, live state, "used by"
  count, and the publishing menu reused from components) and `/sites/:siteId/menus/:menuId`
  (`MenuEditorView`). There is a **Menus** link in the site navigation, beside Components.
- **Tree**: `MenuEntryTree.vue`, an ARIA `tree`. Each row shows the label, the target kind and
  a target state badge (live, not published, deleted, "no page for this item"). Broken entries
  are listed first in a summary banner (FR-025).
- **Ordering**:
  - native drag and drop with before, after and inside drop positions, computed by a pure
    `menuOps.ts` (`canMove`, `move`), which refuses a fourth level or a drop into the entry's
    own subtree (FR-014);
  - the keyboard model is 011 ED-11's: `Alt+↑`/`Alt+↓` move among siblings, `Alt+→` moves
    into the previous sibling, `Alt+←` moves out after the parent;
  - every move is announced through the admin's live region (FR-013).
- **Editing**: `MenuEntryDialog.vue` holds the label, the target kind and its picker, the
  anchor, new tab and shown.
  - Page picker: the existing page picker (`RedirectToPagePicker` generalised to
    `PagePicker`).
  - Item picker: a new `ItemPicker.vue` that chooses a collection, then searches its items
    through the existing Cms item list API.
- **Undo and save**:
  - an in-memory undo and redo stack of documents (`Ctrl/⌘+Z`, `Ctrl/⌘+Shift+Z`), cleared on
    save (FR-015);
  - an explicit **Save draft** with the draft version, and the leave warning of 011;
  - the admin validates with `menuValidator.ts` before sending.
- **Publishing**: `PublishMenu`, `HistoryView` and `ScheduleDialog` take a third subject kind
  (`api/publishing.ts` `SubjectKind` gains `menu`).

**Rationale**: the tree is small (≤ 300 entries). One pure operations module serves drag,
keyboard and tests. Reusing 011's keys means members learn one model (Principle XI).

**Alternatives considered**:
- **Autosave** (as the page editor has): menus are short edits, and an explicit save matches
  the component list and keeps one audit entry per save (NV-18).
- **A drag-and-drop library**: same reasons as 011 ED-02.

---

## NV-18 Capabilities, audit and concurrency

**Decision**:
- A new capability, `pages.menus.manage` ("create, rename, duplicate and delete menus, and save
  their drafts"), held by Designer (and Administrator). Viewing uses `pages.content.view`
  (every site role). Publishing, unpublishing, scheduling, rollback and history use the
  existing `pages.publishing.manage`; history and versions use `pages.content.view`, as for
  components; copying a version into the draft needs `pages.menus.manage`. Choosing a menu in a block
  needs only the right to save that page or component draft.
- **Audit** (FR-005): `pages.menu_changes` (id, site, menu id, action `created` | `renamed` |
  `duplicated` | `draft_saved` | `deleted`, actor, time). It holds no names, labels or
  addresses, and outlives the menu. Publishing actions are in the publish log as for
  components.
- Draft saves, renames and deletes carry the version the member saw. A stale one is refused
  with `pages.out_of_date` (the second save of two editors, edge case).

**Rationale**: "Designer builds, Editor writes" (010). Publishing permissions stay one
capability for every subject kind.

---

## NV-19 "Which menus point at this?" for page and item warnings

**Decision**: `GET /api/v1/pages/sites/{siteId}/menus/references?pageId=…|itemId=…` returns the
menus whose draft (`menu_targets`) or live version (`live_dependencies`) points at the page or
item, with their names. The admin calls it from:
- the page delete and unpublish confirmations (`PageTreeView`, `PublishMenu`);
- the item delete and unpublish confirmations (`DeleteItemDialog`, `ItemPublishBar`);

and lists the menus (FR-026). It needs `pages.content.view`.

**Rationale**: the warning is an admin concern. An admin call keeps Cms free of a new server
read, and adds no reference edge.

**Alternatives considered**:
- **A `Pages.Contracts` reader called by Cms's delete endpoint**: the Cms → Pages.Contracts
  edge exists, but it couples Cms's delete response to menus for a display-only list.

---

## NV-20 Limits

| Limit | Value | Enforced by |
|---|---|---|
| Menus per site | 50 | `Menu.Create` (`pages.menu_limit_reached`) |
| Entries per menu | 300 | menu rule 2 |
| Levels | 3 | menu rule 3 |
| Menu name | 100 characters, unique per site ignoring case | `Menu`, `citext` unique index |
| Label | 100 characters | menu schema |
| Anchor | 100 characters, pattern of FR-007a | menu schema |
| Menu document | 256 KiB | menu rule 0 (`pages.document_too_large`) |

Like page-schema §4, the limits are part of the contract, not operator settings.

---

## NV-21 Site export (#20) obligations

**Decision**: a site export carries each menu's name, draft document, live document and
version history, and the Navigation blocks inside page documents. On import, `pageId`,
`itemId` and `collectionId` in menu documents are remapped with the same id map as page
links, and `menuId` in Navigation blocks with the menu id map. Recorded in
[contracts/module-surface.md](./contracts/module-surface.md) §5 and `docs/backlog.md`, for #20
to build.

---

## NV-22 Upgrade

**Decision**:
- One additive Pages migration, `AddMenus`, adding `menus`, `menu_drafts`, `menu_targets`,
  `menu_usages` and `menu_changes`. No document is rewritten.
- Existing pages hold no `navigation` node. Their output, ETag inputs and CSP are unchanged
  (`UpgradeFrom017Tests`), except that the renderer stamp changes with the stylesheet, so each
  page renders once after the upgrade, as after every release.
- `publications.subject_kind` and the other subject-kind columns hold text, so `menu` needs no
  schema change. A check constraint, if present, is widened in the same migration.

---

## NV-23 Testing

| Suite | Proves |
|---|---|
| `MenuSchemaGoldenTests` + `menuSchema.spec.ts` | every menu rule, valid and invalid examples, same results in both validators |
| `NavigationSchemaGoldenTests` (page schema) | the `navigation` node type and its props |
| `MenuOpsTests` (`menuOps.spec.ts`) | moves, refused fourth level and self-nesting, keyboard moves |
| `ActiveStateTests` | SC-004 matrix: current, containing, section, home, item pages, paged lists, pruned levels |
| `MenuModelTests` | pruning and link integrity for every target type in every state (SC-003) |
| `NavigationGoldenTests` | HTML for each layout, levels, collapse, starting state, encoding (SC-007) |
| `NavScriptTests` | script bytes, size ≤ 2 KB, CSP hash, CSP unchanged on pages without menus |
| `MenuEndpointsTests` | CRUD, duplicate, limits, out-of-date, capabilities, `unknown_target` |
| `MenuPublishingTests` | publish, unpublish, schedule, rollback, history; refusals of FR-003 and FR-003a |
| `MenuInvalidationTests` | every row of NV-07's table refreshes exactly the pages that show the menu, on two instances (SC-002) |
| `MenuReferencesTests` | FR-026 lists, drafts and live |
| `MenuIsolationProbe` | menus, targets and usages separated by site; other sites' ids refused (FR-035) |
| `UpgradeFrom017Tests` | unchanged bodies and CSP for pages without menus |
| `MenuRenderPerformanceTests` | SC-005: two 300-entry menus within 012's budgets |
| Admin specs with axe | menu editor, entry dialog, keyboard ordering, Navigation block controls (SC-006) |

The release smoke journey gains: create a menu, publish it, place it in the header, publish,
and see the `<nav>` on the home page.

---

## NV-24 Dependencies

**Decision**: none new, on either side. `details`, `summary` and the `name` attribute are
HTML. The script is about 60 lines, written in-house. Drag and drop is native (011 ED-02).

---

# Menu styles (added 2026-10-20)

Inputs: the five clarifications of the 2026-10-20 session (FR-017a to FR-017d). Site-wide
defaults live in the design tokens and each block may override them; entries take one of four
looks; eight colours are offered; visited entries look like the others; contrast problems are
warned about, never refused.

---

## NV-25 The site-wide menu style is an optional `menuStyle` in the design tokens

**Decision**: the design-token contract gains an optional top-level object, additive in
version 1 like `contentWidths` (design-tokens.md §2):

```json
"menuStyle": {
  "look": "pill",
  "colors": { "background": "color-text", "text": "color-background", "currentBackground": "color-brand" }
}
```

- `look`: `link`, `underline`, `pill` or `button`; absent: `link`.
- `colors`: any of `background`, `text`, `hoverText`, `hoverBackground`, `currentText`,
  `currentBackground`, `focus`, `panelBackground`, each a **colour token id**. Absent keys
  take the built-in defaults of NV-27.
- A missing token id is not an error. It falls back like every token reference
  (design-tokens.md §2, "Fallbacks always resolve").
- Validated by the existing token validators (Editor C#, admin TypeScript), with golden
  examples, and edited in the Design view in a new **Menus** section.

It is published with the design, so it reaches visitors on the next design publish. Every
cached page already re-renders then: the design number is part of the render key (012).

**Rationale**:
- "Set once for the site" belongs where the site's other look-and-feel lives, and where
  publishing it already refreshes every page.
- Token ids, not raw colours, so the menu follows palette changes like every block does.

**Alternatives considered**:
- **A separate "menu theme" entity in Pages**: a second thing to publish and refresh, for
  what is plainly design.
- **Styles on the menu itself**: a menu is content shown by several blocks. FR-017a puts
  overrides on the block, and the same menu can then look different in header and footer.

---

## NV-26 Block overrides are optional props without schema defaults

**Decision**: the `navigation` node gains optional props, none with a schema `default`, so
"absent" means "the site-wide value" (the `ariaLabel` precedent):
- `look`: the four looks;
- `background`, `hoverColor`, `hoverBackground`, `activeBackground`, `focusColor`,
  `panelBackground`: colour references (`colorRef`).

`color` and `activeColor` **lose their schema defaults** and mean the text and current-entry
text colours (FR-017c). This is not a breaking change: the node is new in this unreleased
feature, so no stored document predates it. Golden examples and `BlockDefaults` are updated.

**Rationale**: a schema default would always win over the site-wide value, which FR-017a
forbids. Leaving the prop out is the only "follow the site" state that needs no sentinel
value.

**Alternatives considered**:
- **A sentinel value such as `{ "token": "inherit" }`**: a special token id every consumer
  must know, which breaks "an unknown token falls back to the property default".
- **A boolean "use site style" plus the props**: two sources of truth for one decision.

---

## NV-27 Colours reach the page as two layers of custom properties

**Decision**:
- **Site layer**: `TokenCss` (and its port, `tokenCss.ts`) emits the
  `--qb-menu-*` properties, each `var(--qb-color-<id>)` of the site's choice or of the
  built-in default; six are always emitted, the two look-dependent ones only when chosen:

  The properties, their order and their built-in defaults are in
  [contracts/menu-style.md](./contracts/menu-style.md) §2; the focus colour defaults to the
  current-entry text colour, the block's before the site's (§3).
  These defaults, with the `link` look, reproduce today's output.
- **Block layer**: the renderer writes `--qb-nav-*` in the block's style rule only for the
  props that are present (`context.Style`), and the stylesheet reads
  `var(--qb-nav-x, var(--qb-menu-x))`.
- **Visited**: the stylesheet sets `:visited` to the same colours as unvisited links, so
  the browser's visited colour never shows (FR-017c).

**Rationale**: custom properties cascade into dropdowns and the small-screen panel with no
extra markup. An absent block value falls through to the site value in CSS alone, so the
renderer needs no merge logic for colours.

**Alternatives considered**:
- **Merging site and block colours in the renderer**: every page would carry the site's menu
  colours in its own style rule, and a design publish would change page bodies, not just the
  token CSS.

---

## NV-28 The look is a class, decided by the renderer

**Decision**: the `nav` element carries `qb-nav--look-<look>`, where the look is the block's
`look` if present, else `menuStyle.look`, else `link`. The renderer reads `menuStyle.look`
from the published design it already holds; the editor reads it from the draft tokens. The
look styles apply to top-level entries only; submenu entries keep the list look (FR-017b).
The button look's border is `currentColor`, the entries' text colour.

**Rationale**: styling by the value of a custom property needs container style queries,
which Firefox does not support. A class works everywhere, and the golden files show it.

**Alternatives considered**:
- **`@container style(--qb-menu-look: pill)`**: unsupported in Firefox; the menu would fall
  back to plain links there.
- **Four sets of rules keyed by a `data-` attribute**: the same as a class, but the
  stylesheet's other modifiers are classes.

---

## NV-29 Contrast warnings are computed in the admin only

**Decision**: a shared admin helper, `editor/tokens/contrast.ts`, computes the WCAG 2.x
contrast ratio of two colours. It resolves token ids against the draft token set and
composites `#rrggbbaa` and `transparent` over the site's `color-background` value. It is used
in:
- the Design view's Menus section, for the site-wide pairs;
- the Navigation block's panel, for the block's effective pairs (override, else site value).

The pairs and minimums:

| Foreground | Background | Minimum |
|---|---|---|
| text | background | 4.5 |
| hover text | hover background (over the bar's background) | 4.5 |
| current text | current background (over the bar's background) | 4.5 |
| text | panel background | 4.5 |
| focus | background | 3 |

Each failing pair is named in a warning next to the colours (`role="status"`). Nothing is
refused, on either side: the server does not compute contrast (FR-017d).

**Rationale**: a warning needs the resolved colours and the place where they are chosen, and
both are in the admin. A server check could only refuse, which the clarification rules out.

**Alternatives considered**:
- **A server-side check returning warnings with the save**: duplicated colour maths in C#
  for a message the admin can already show as the colour is picked.

---

## NV-30 The focus ring cannot be removed

**Decision**: the stylesheet draws
`outline: 2px solid var(--qb-nav-focus, var(--qb-menu-focus))` with `outline-offset: 2px`
on `:focus-visible` for every link and `summary` of a Navigation block, in every look. No
prop and no token turns it off; only its colour is chosen (FR-017d).

**Rationale**: the focus ring is the accessibility baseline (011, SC-006). Making its colour,
not its presence, configurable meets FR-017d.

**Alternatives considered**:
- **Using the hover style as the focus style**: a hover background with poor contrast would
  then hide focus too.

---

## NV-31 Testing the styles

**Decision**:
- **Contracts**: design-token golden examples for `menuStyle` (valid: each look and every
  colour key; invalid: unknown look, unknown colour key, a malformed id); page-schema
  examples for the new props.
- **Parity**: a `token-css` fixture with a `menuStyle`, run by `TokenCss` and `tokenCss.ts`.
- **Rendering**: `NavigationGoldenTests` cases for each look, the site look with a block
  override, and only present overrides written (SC-008).
- **Stylesheet**: `navCollapseCss.spec.ts` generalised into `navStyleCss.spec.ts`, resolving
  the cascade for hover, focus, current and visited in each look.
- **Admin**: `editor/contrast.spec.ts` (WCAG values, alpha compositing), the Design view's Menus
  section, and the block panel warnings, with axe.
- **Upgrade**: `UpgradeFrom017Tests` stays green: a token set without `menuStyle` emits the
  defaults, and pages without a Navigation block are byte-identical apart from the
  stylesheet link.
