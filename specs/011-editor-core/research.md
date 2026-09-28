# Research: Editor Core

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #11

Decisions are numbered **ED-01 … ED-22** and referred to by number from the plan, the data
model and the contracts. Each one gives the decision, why, and what else was considered.

---

## ED-01 Canvas: in-house Vue components in the admin document, no iframe

**Decision**: The canvas is a tree of Vue components, one per block type, rendered inside
the admin page in a frame element whose width is set to the preview width (ED-09). There is
no iframe and no third-party page-builder framework. This is the "editor canvas library"
decision that the constitution's Technology Constraints defer to this feature, and this
feature records it there (plan, Constitution amendment).

**Rationale**:
- **The document is ours.** The page schema contract (010) is a `{ id, type, props,
  children }` tree. Every framework surveyed brings its own document model and would need a
  lossy two-way mapping.
- **Keyboard and accessibility.** FR-008 needs every action without a pointer, and the admin
  shell has axe tests in jsdom. Same-document components are reachable by those tests;
  iframe content in jsdom is not.
- **Drag between palette, canvas and layer view** is one coordinate space, with no
  cross-frame event forwarding.
- **Fidelity with visitors' pages** does not need an iframe: block styles use container
  queries against the canvas frame (ED-09), so the same stylesheet behaves the same in the
  canvas and on a published page (#12).

**Isolation from the admin stylesheet**: the canvas root carries `class="qb-canvas"`, and
block styles live in a cascade layer `qb-blocks` loaded after a canvas-local reset. The
admin's element selectors (`h1`, `h2`, `label`, `button`, `select` in `styles.css`) are
wrapped in `:where(:not(.qb-canvas *))`, a zero-specificity guard, so they stop applying
inside the canvas. `CanvasStyleIsolationTests` renders each block inside and outside the
canvas and checks the computed styles differ where the admin rules would leak.

**Alternatives considered**:
- **iframe canvas (Webflow, GrapesJS style)**: the strongest style isolation and real media
  queries. It was rejected because of cross-frame drag and drop, focus management across
  frames, and untestable canvas content in jsdom. Its benefit (real viewport width) is
  covered by container queries.
- **GrapesJS** (BSD-3): its document model is HTML and CSS components. Mapping to our JSON
  tree both ways is lossy, and it allows raw HTML, which conflicts with Principle IX.
- **Puck, Craft.js** (MIT): React only. The frontend stack is Vue 3 (Technology
  Constraints).
- **Shadow DOM for the canvas**: isolates styles, but breaks TipTap's selection handling
  in some browsers and complicates axe runs. The zero-specificity guard is enough for the
  small admin stylesheet.

---

## ED-02 Drag and drop: native HTML5 drag events plus a keyboard path, no library

**Decision**: Palette items, canvas blocks and layer-view rows are `draggable`. A single
`useDragDrop` composable computes the drop target from `dragover` positions: before or
after a sibling, or inside a Section or Container that is empty or when the pointer is in
its inner area. It asks `canDrop(parentType, childType, documentKind)` (ED-05) and shows
the indicator or a "not allowed" cursor. Every drag action has a keyboard equivalent
(ED-11), so drag is never the only path.

**Rationale**: the editor is used on desktop-class screens (spec Assumptions), where native
drag events are reliable in every evergreen browser. The work is the drop-position logic,
which a library would not remove. No dependency (Principle XI).

**Alternatives considered**:
- **SortableJS / vuedraggable** (MIT): list-oriented. Nested, typed drop rules and
  cross-container moves with a shared tree model fight its DOM-mutation approach.
- **Pragmatic drag and drop** (Apache-2.0, framework-agnostic): a good fit technically.
  It was not needed, but it is the recorded fallback if native drag events prove
  unreliable (for example touch support becomes a goal).

---

## ED-03 Rich text: TipTap (ProseMirror) for the Rich Text block only

**Decision**: The Rich Text block edits with TipTap 3 (`@tiptap/core`, `@tiptap/pm`,
`@tiptap/vue-3` and the Document, Paragraph, Text, HardBreak, Bold, Italic, Link and list
extensions), configured with only the formats FR-013 allows. The stored value is the
ProseMirror JSON that the constitution names (Principle IX), narrowed by the schema (ED-06).
A `toContractRichText()` function turns TipTap's output into the contract shape: it drops
null attributes, keeps only allowed marks, and maps the Link mark to our link object. Paste
goes through TipTap's schema, which drops everything it does not know, and then through the
same function.

Plain-text blocks (Heading, Text, Button label) edit in place with
`contenteditable="plaintext-only"`. It is supported in every evergreen browser, including
Firefox since version 136.

TipTap's own history extension is **not** loaded. Undo is the editor's (ED-12). Each TipTap
transaction that changes the document commits a document update, grouped as typing.

**Rationale**: rich text editing is where in-house code fails (selection, IME, paste). The
constitution already fixes the storage format as ProseMirror/TipTap JSON. TipTap has a
maintained Vue 3 binding and is MIT licensed, as is ProseMirror.

**Dependency justification** (Principle XI):
- **What**: `@tiptap/core`, `@tiptap/pm`, `@tiptap/vue-3` and the listed extensions, all
  MIT.
- **Maintenance**: actively maintained, with a v3 line current in 2026.
- **Size**: about 110 KB gzipped, in the editor's lazy-loaded chunk only. The admin
  shell's other screens and published sites don't load it; the renderer (#12) produces
  HTML server-side from the JSON.
- **Alternatives**: plain ProseMirror (MIT) is the same engine with several hundred more
  lines of glue. Lexical (MIT) has its own JSON format, not the constitution's. Quill's
  Delta format is not ProseMirror JSON.

---

## ED-04 Page schema: the ten blocks join version 1 as an additive change

**Decision**: Add `section`, `container`, `heading`, `text`, `rich-text`, `image`,
`button`, `spacer`, `divider` and `embed` to `docs/contracts/page-schema/v1.schema.json`
as `$defs` entries, with shared `$defs` for token references, links and rich text. The
`schemaVersion` stays `1`. Every document valid before (empty roots, and roots holding
`component-ref`s) stays valid, which the contract's §6 defines as additive. The full
definitions are in [data-model.md](./data-model.md) §1 and the planning copy of the contract
in [contracts/page-schema-blocks.md](./contracts/page-schema-blocks.md).

**Rationale**: Principle VI, and 010's contract §2, which anticipated exactly this. No
stored document needs a migration.

**Alternatives considered**: a `v2` schema. It is not needed: nothing is removed or
narrowed, and version 2 would oblige a forward migration for nothing.

---

## ED-05 Nesting rules live in the schema, and one new rule name reports them

**Decision**: The single `childNode` list becomes three lists that encode FR-012:
- `rootChild`: `section` or `component-ref`;
- `contentChild`: `container`, `heading`, `text`, `rich-text`, `image`, `button`,
  `spacer`, `divider` or `embed`;
- leaves: `maxItems: 0` on `children`, as `component-ref` already does.

`section.children` and `container.children` take `contentChild`.

A known type in the wrong place fails the `oneOf` discriminator. The validator maps a
discriminator failure whose `type` is a member of `rootChild` ∪ `contentChild` (the node
types) to a new rule **`nesting_not_allowed`**. It keeps `root_misplaced` for `root`, and
`unknown_type` for everything else. That includes rich-text node names such as
`paragraph`, which are not node types, so 010's `unknown_type--paragraph.json` keeps its
result. The new rule is added to the contract's §3 list and to
`DocumentProblem` in `pages-api.yaml`. This is additive: no document that passed before
fails now.

The editor's `canDrop` (ED-02) is derived from the same three lists: the TypeScript block
registry reads `rootChild` and `contentChild` from the imported schema, so the editor and
the server cannot disagree about nesting.

**Component references sit only at the root**, because a component's own root holds
Sections, and a Section inside a Section is not allowed.

**Rationale**: encoding nesting in the schema keeps "defined once" true, and it gives the
renderer (#12) the same guarantee for free.

---

## ED-06 Values: tokens as `{ "token": id }`, links as a typed object, rich text as narrowed ProseMirror JSON

**Decision**:
- **Token references** are objects: `{ "token": "<tokenId>" }`, with separate `$defs` for
  `colorRef`, `spaceRef` and `textSizeRef`. They differ only in their description, since
  ids are unique across all kinds within a site's token set (ED-14). The object shape makes
  "which blocks use token X" one JSON path (`$.**.token`) that can't collide with a text
  value (ED-16). A reference to a token that no longer exists is valid (clarification Q4):
  the renderer and the canvas fall back to the property's `default`.
- **Links** are `{ "type": "page", "pageId": uuid }` or `{ "type": "url", "href": string }`,
  a `oneOf` told apart by `type`, which is the only `oneOf` form the subset supports. `href`
  matches `^(https?://\S+|mailto:\S+|tel:[+0-9 ().-]+)$`, with at most 2,048 characters. A
  link to a deleted page is valid (spec Edge Cases).
- **Media** is `{ "mediaId": uuid }` on Image. Existence is not checked on save, so a
  deleted file never blocks autosave (spec Edge Cases).
- **Embeds** store `url`, which must match a pattern per provider: YouTube
  (`youtube.com/watch?v=`, `youtu.be/`) and Vimeo (`vimeo.com/<digits>`), `https` only. The
  provider is derived from the URL and not stored.
- **Rich text** is a `doc` with `content` of `paragraph`, `bulletList` or `orderedList`,
  recursively through `listItem`. Inline content is `text` (with `marks`: `bold`, `italic`,
  `link`) or `hardBreak`. Every `oneOf` is told apart by `type`, which is exactly TipTap's
  JSON shape. The link mark's attributes are `{ "to": <link> }`.
- **Rules the schema can't express**, checked in code after the schema:
  - `rich_text_too_long`: more than 20,000 characters of text in one Rich Text block.
  - The existing rules are unchanged.

**Pattern portability**: patterns must mean the same in .NET and JavaScript. They are
limited to the common subset (no lookbehind, no named groups, no `\p{}`), compiled with
`RegexOptions.ECMAScript` on the server. `PageSchemaSubsetTests` checks that each pattern
compiles with that option.

**Rationale**: Principle IX (no raw HTML, safe schemes), and the clarified rules that stale
references never block saving.

---

## ED-07 The subset validator gains `default` and `minItems`

**Decision**: Add the `default` annotation, which is read and never validated, and
`minItems`. `default` carries each property's default for the property panel, the canvas
and the renderer. `minItems: 1` keeps lists from having no items. The contract's §7 list
and `SupportedKeywords` change together, and `PageSchemaSubsetTests` keeps them in step.

**Rationale**: "defaults" are part of a block's single definition (Principle VI). Putting
them in the schema means the C# renderer (#12) and the TypeScript editor read the same
values.

**Alternatives considered**: defaults in a separate JSON file (a second place to drift), or
in TypeScript only (the renderer couldn't see them).

---

## ED-08 One block definition, three consumers; a TypeScript validator over the same file

**Decision**: The editor imports `v1.schema.json` directly from `docs/contracts/` (a Vite
alias `@contracts`). Each block type has one folder `src/admin/src/editor/blocks/<type>/`
holding:
- `definition.ts`: palette label and icon, property groups, and the control kind per
  property (for example `colorToken`, `spaceToken`, `link`, `media`, `choice`, `text`,
  `richText`);
- `<Type>Block.vue`: the canvas component;
- `blocks.css` entries under the `qb-blocks` layer.

Types, allowed values, limits and defaults are never repeated in TypeScript; they are read
from the schema's `$defs`. `BlockRegistryCoverage.spec.ts` fails the build if:
- a schema block type has no definition;
- a definition names a property the schema lacks;
- a property has no control.

A TypeScript port of the subset validator and the structural walk (`documentValidator.ts`)
checks documents before autosave (spec Edge Case "very large pages"). It runs the same
golden files as the C# validator, with the same rule and pointer, in
`documentValidator.spec.ts`. This is the "every other validator (the editor's) must use it
too" obligation in the contract.

**Rationale**: Principle VI ("a block type is defined once … adding a block MUST NOT
require changes to unrelated blocks"), and SC-009. Server-side rendering of blocks is #12's
part of the same definition.

**Docker build**: the admin build stage copies `docs/contracts/` next to `src/admin/`, so
the alias resolves the same way in the image as in a checkout.

---

## ED-09 Responsive preview: a width-constrained frame and container queries

**Decision**: The canvas frame has `container-type: inline-size; container-name: page` and
a fixed width: desktop 1,280 px, tablet 768 px, mobile 375 px (spec Assumptions). Block CSS
uses `@container page (max-width: 767px)` for tablet rules and
`@container page (max-width: 480px)` for mobile rules, for example a horizontal Container
with `stackOnMobile` stacking. When the editor area is narrower than the chosen width, the
frame is scaled with `transform: scale()` so it fits. The container width, and so the
layout, is unchanged. The chosen width is kept per member in `localStorage` (key
`qb.editor.previewWidth.<userId>`).

The renderer (#12) wraps the published page body in the same named container, so the
same block CSS behaves the same for visitors. This is recorded for #12 in the contract
document.

**Rationale**: real responsive behaviour without an iframe (ED-01); one stylesheet for
canvas and site.

**Alternatives considered**: media queries (they need an iframe to see the preview width),
or JavaScript width classes (they diverge from what visitors get).

---

## ED-10 Editor state: an immutable document in Pinia, updated by pure operations

**Decision**: An `editorStore` per open document holds:
- `document`: an immutable tree in a `shallowRef`;
- `baseVersion`: the draft version it was loaded or last saved at;
- `selection`, the undo and redo stacks (ED-12), and the save state (ED-13).

Every change is a pure function in `editor/ops/` (`insert`, `move`, `remove`, `duplicate`,
`paste`, `setProp`, `setRichText`, `setLabel`) that returns a new root. It copies only the
path from the root to the changed node and reuses every other subtree by reference.

Canvas and layer components receive their node object as a prop. Because unchanged subtrees
keep their identity, Vue skips re-rendering them, which is what makes SC-005 (100 ms on 500
blocks) reachable without memoisation tricks.

Ops enforce the rules before applying: nesting (ED-05), limits (node count, depth,
estimated size) and property validity (ED-06). A refused op changes nothing and returns a
reason, which the UI shows (FR-021, FR-042).

**New ids**: `insert`, `duplicate` and `paste` generate node ids with `crypto.randomUUID()`
shortened to 22 base64url characters, within the contract's `^[A-Za-z0-9_-]{1,64}$`.

**Rationale**: pure operations are easy to unit-test exhaustively (SC-003 and the property
tests), and they make undo a matter of keeping references.

---

## ED-11 Keyboard model

**Decision**:
- **Palette**: arrow keys move through the blocks; `Enter` inserts the focused block after
  the selected one, or as the last child if the selected block takes children and allows it.
- **Canvas and layer view**: the layer view is an ARIA `tree`, and the canvas mirrors its
  selection.
  - Arrows move the selection; `Enter` edits text in place; `Escape` leaves text editing,
    then clears the selection.
  - `Alt+↑` and `Alt+↓` move the block among its siblings. `Alt+→` moves it into the
    previous sibling if that sibling takes it; `Alt+←` moves it out to follow its parent.
  - `Ctrl/⌘+D` duplicates, `Delete` removes, and `Ctrl/⌘+C` and `Ctrl/⌘+V` copy and
    paste.
  - `Ctrl/⌘+Z` undoes; `Ctrl/⌘+Shift+Z` and `Ctrl/⌘+Y` redo.
  - `Ctrl/⌘+S` saves now.
- Moves announce the result through a polite live region, for example "Heading moved into
  Section 2, position 1".

**Rationale**: FR-008 and SC-006. It mirrors the pattern the page tree screen already uses
(010 P-17), so the admin shell is consistent.

---

## ED-12 Undo and redo: reference snapshots with typing coalescing

**Decision**: Each committed change pushes `{ document, selection }`, the state *before*
the change, onto the undo stack, and clears the redo stack.
- **Coalescing**: consecutive text changes to the same block and property coalesce into
  one step until 1 second passes without typing, a word boundary (whitespace or
  punctuation) is typed, or another action happens.
- **Limit**: the stack holds 200 steps (FR-025 requires at least 100). Older steps are
  dropped.
- **Cost**: steps are cheap, because they share every untouched subtree (ED-10).

Undo is independent of saving (spec Edge Cases): after undo or redo, the document is dirty
if it differs by reference from the last saved one, and autosave follows. History is
cleared when the editor opens another document or reloads (FR-025).

**Rationale**: with immutable documents, snapshots are simpler and more reliable than
inverse operations, and SC-003 ("undo all equals start") holds by construction.

---

## ED-13 Autosave: a small state machine with backoff and hard stops

**Decision**: The save state is one of `saved`, `dirty`, `saving`, `retrying`, or one of
three stopped states: `conflict`, `forbidden`, `gone` or `invalid`.
- **When**: 2 seconds after the last change (inside FR-026's 3 seconds, allowing for the
  request), or at once on `Ctrl/⌘+S`. One save is in flight at a time; changes made during
  a save start a new timer.
- **Pre-check**: before sending, the TypeScript validator (ED-08) runs. A failure means an
  editor bug: the save is not sent, and the state becomes `invalid` with the reason.
- **Temporary failures** (network error, `502`, `503`, `504`, `429`) go to `retrying`, with
  a backoff of 2, 4, 8, 16 and then 30 seconds repeating. "Retry now" skips the wait.
- **Refusals**:
  - `pages.out_of_date` → `conflict` (ED-15);
  - `403` → `forbidden`;
  - `pages.not_found` or `pages.component_not_found` → `gone`;
  - `pages.invalid_document`, `pages.unsupported_schema_version` or
    `pages.unknown_component` (all `400`) → `invalid`, selecting the node at the problem's `pointer`
    when it maps to a node id.
  - Every stopped state stops autosave, and editing becomes read-only (FR-028, FR-029).
- **Leaving**: `beforeunload`, and a router guard for leaving inside the app, warn
  whenever the state is not `saved` (FR-030).
- **Status**: the status line shows the state through the translation lookup, with an
  `aria-live="polite"` region.

**Rationale**: FR-026 … FR-030 and SC-004. The backoff avoids hammering a recovering server.

---

## ED-14 Design tokens: a new `Editor` module holding one versioned token document per site

**Decision**: Add the `Editor` module, one of the constitution's named modules, as
`QuickBuilder.Editor` and `QuickBuilder.Editor.Contracts`, with its own `editor` schema.
- **Storage**: one table, `editor.design_token_sets`, holds one row per site. The row has
  a `jsonb` document, a `schema_version`, an `xmin` concurrency version, and when and by
  whom it was last changed. A `DesignTokenSet` aggregate enforces the rules:
  - 1 to 24 colours, with names unique ignoring case;
  - valid colour values;
  - spacing and text-size steps with unique names and sizes in range;
  - fonts from the known list (ED-17).
- **Ids**: token ids are strings matching `^[a-z0-9-]{1,40}$`, unique across all kinds
  within a site. Default tokens have fixed ids (`color-brand`, `space-md`, `text-lg` and
  so on). New tokens get a generated id such as `color-` plus 8 random characters.
- **The format is a contract**: `docs/contracts/design-tokens.md` plus
  `design-tokens/v1.schema.json`, validated with the same subset validator. The editor and
  the renderer (#12) share it (Principle VI).
- **Defaults are lazy**: a site without a row reads the built-in default set, which is a
  constant in `Editor.Contracts`. The row is written on the first save. This covers new
  sites and every existing site on upgrade (FR-032, FR-043) with no migration data step,
  no cross-module read of the sites table, and no event handler.
- **Reads for other modules**: `Editor.Contracts` exposes `IDesignTokenReader` for #12. No
  other module needs it in this feature.
- **Draft only**: this feature stores the draft token set only (FR-034). #12 adds the
  published copy beside it.

**Rationale**: tokens are design settings used by the editor and later the renderer, not
site identity or hosting (Sites). The constitution names an `Editor` module, and this is
its first real content. A single document per site matches how tokens are edited: one
settings screen, one save, one version.

**Alternatives considered**:
- **The Sites module**: it would work. It mixes design data into the hosting module that
  the public request path resolves on every request, and #12's published tokens would then
  also live there.
- **Relational tables per token kind**: more tables and joins for a document that is always
  read and written whole.
- **Writing defaults per site on `SiteCreated`, plus a migration for existing sites**: two
  mechanisms where one lazy default does both.

---

## ED-15 Conflicts: stop, show the member's version read-only, copy, reload, paste

**Decision** (clarification Q3):
- On `conflict`, the editor keeps the member's document on screen but switches to
  read-only. Selection, the layer view and copy work; every changing action is disabled.
- A banner explains the conflict and offers **Reload latest**. It first warns that changes
  not copied will be lost.
- Reload fetches the draft, replaces the document, clears the history and keeps the
  in-app clipboard (ED-18), so copied blocks paste into the reloaded page.
- There is no overwrite path in the UI, and the API has none either: a save always carries
  `version` (010 FR-021).

**Rationale**: no work is ever overwritten, and no work is lost unless the member chooses.
Concurrent editing beyond this is #23.

---

## ED-16 Counting token usage: a Pages endpoint over the drafts, via a parameterised jsonpath

**Decision**: `GET /api/v1/pages/sites/{siteId}/token-usages/{tokenId}` in the Pages module
(capability `pages.content.view`) returns `{ blockCount, documentCount }`. It is counted
with one PostgreSQL `jsonb_path_query` over `page_drafts` and `component_drafts`:
- The path counts nodes whose `props` contain `token == $id` at any depth:
  `$.** ? (exists(@.props.** ? (@.token == $id)))`.
- It runs through EF Core `SqlQuery`, parameterised, inside the module that owns the
  tables, and applies the site filter explicitly (Technology Constraints).
- `TokenUsageSiteFilterTests` proves the filter.

The admin token screen calls this before deleting a colour or spacing step, and shows the
count in the warning (clarification Q4). Nothing is stored.

**Rationale**: Pages owns drafts, so Pages answers questions about them. The admin calls it
directly, so there is no synchronous cross-module call and no usage table to keep in step.
On the 2,000-page limit, a jsonpath scan inside PostgreSQL is well under a second, and it
is measured in `TokenUsagePerformanceTests` in the smoke project.

**Alternatives considered**:
- **Scanning in C#**: moves up to 2,000 documents over the wire.
- **A persistent usage table rewritten on every save**, like `component_usages`: the
  clarification explicitly dropped a lasting record.

---

## ED-17 Fonts: named system font stacks only

**Decision**: Typography offers the stacks from the Modern Font Stacks project (CC0):
`system-ui`, `transitional`, `old-style`, `humanist`, `geometric-humanist`,
`classical-humanist`, `neo-grotesque`, `monospace-slab`, `monospace-code`, `industrial`,
`rounded-sans`, `slab-serif`, `antique`, `didone` and `handwritten`. Each is an id in the
token contract that maps to a CSS `font-family` list, defined once in `Editor.Contracts`
and mirrored in the admin by a generated `fontStacks.ts`. `FontStackParityTests` compares
the two. Nothing is downloaded (FR-035, SC-008).

**Rationale**: Principle II (offline) with no font files, licences or serving path. Bundled
web fonts can be a later feature once rendering exists to serve them.

---

## ED-18 In-app clipboard

**Decision**: Copy writes `{ userId, siteId, nodes }` to `localStorage` under
`qb.editor.clipboard`. Paste reads it:
- it refuses a different `siteId` or `userId`;
- it applies FR-006 nesting and the limits;
- it re-ids every node (FR-005).

It survives reloads and works across tabs and documents of the same site (FR-007, ED-15).
Sign-out clears it. As well as the JSON, the plain text of the copied blocks is written to
the system clipboard, as a courtesy for pasting into other applications.

**Rationale**: system clipboard custom formats are not portable, and reading the clipboard
needs a permission prompt in some browsers. A same-origin store is enough, because pasting
is only ever within one site.

---

## ED-19 Recovery copies in IndexedDB

**Decision** (clarification Q5):
- **Where**: an IndexedDB database `qb-editor-recovery` holds one record per
  `(userId, siteId, kind, documentId)`: `{ document, baseVersion, savedAtUtc }`.
- **When written**: in `dirty` and `retrying` state, debounced to 500 ms. Also on
  `pagehide`.
- **When removed**:
  - after a successful save that covers the latest change;
  - when the member declines the offer;
  - on sign-out, when every record for that user is removed;
  - when the document no longer exists or the member may no longer save it (spec Edge
    Cases).
- **On open**: if a record exists, the editor compares its `baseVersion` with the loaded
  draft's `version`:
  - **equal**: it offers **Restore** or **Discard**;
  - **different**: it offers **Open read-only to copy blocks**, using the ED-15 view, or
    **Discard**.
  - It never restores without asking.
- **Failure**: if IndexedDB is unavailable (private mode, quota), recovery is silently
  off, and FR-030's warning still applies.

**Rationale**: documents can be up to 1 MiB, and several may be pending. `localStorage` is
too small and synchronous. IndexedDB is native, so there is no dependency.

---

## ED-20 Authorization

**Decision**:
- **Pages**: no new capabilities. The editor for a page needs `pages.page_drafts.edit`,
  and for a component `pages.components.manage` (010). Holding it allows every editor
  action (clarification Q1). The token-usage endpoint needs `pages.content.view`.
- **Editor module**: two new site-scoped capabilities.
  - `editor.design_tokens.view`: Designer and Editor, and Administrator everywhere. Every
    site role that can view pages can read tokens (FR-040).
  - `editor.design_tokens.manage`: Designer, and Administrator everywhere.
- Every endpoint declares its capability. `AuthorizationDeclarationTests` covers the new
  controllers. Not found versus forbidden follows 002.
- `specs/002-identity-setup/contracts/authorization.md` §2 gains the two capabilities.

---

## ED-21 Testing strategy

| Area | Tests |
|---|---|
| **Schema (C#)** | Golden valid and invalid examples per block type. New rules `nesting_not_allowed` and `rich_text_too_long`. `PageSchemaSubsetTests` with `default` and `minItems` and ECMAScript-compatible patterns. `PageSchemaContractTests` extended for the new types. |
| **Schema (TS)** | `documentValidator.spec.ts` runs the same golden files with the same rule and pointer. `BlockRegistryCoverage.spec.ts`. |
| **Editor ops** | `ops.spec.ts` unit tests per op. `ops.property.spec.ts` runs random sequences of 50+ ops and checks that the documents stay valid, that undoing all equals the start and redoing all equals the end (SC-003), and that ids stay unique. |
| **Autosave** | `autosave.spec.ts`, with fake timers and a fake API: debounce, backoff, each refusal state, no send on pre-check failure, and the leave warning. |
| **Recovery** | `recovery.spec.ts` with `fake-indexeddb`, a dev-only dependency (MIT): offer when equal, read-only when changed, cleared on save, decline and sign-out. |
| **UI** | `EditorView.spec.ts`, `LayerView.spec.ts`, `PropertyPanel.spec.ts` and `DesignTokensView.spec.ts`, using Testing Library and axe: keyboard paths for every block type (SC-006), the translation lookup, and conflict and read-only mode. |
| **Style isolation** | `CanvasStyleIsolation.spec.ts` (ED-01). |
| **No outbound requests** | `NoThirdPartyRequests.spec.ts` renders every block type and asserts that no element has a `src` or `href` outside the installation, which covers the embed placeholders (SC-008). |
| **Backend** | `DesignTokenSetTests` (unit). `DesignTokensApiTests`, `EditorAuthorizationTests`, `EditorIsolationProbe` and `TokenUsageApiTests` / `TokenUsageSiteFilterTests` (integration). `UpgradeFrom010Tests`: stored 010 drafts stay valid and unchanged, and existing sites read default tokens. `DesignTokenContractTests`. `FontStackParityTests`. |
| **Smoke** | Extend the journey: load `/admin/sites/{id}/pages/{pageId}/edit` and expect 200 with the app shell; save a draft holding a Section and a Heading through the API; read it back. `TokenUsagePerformanceTests` and `EditorOpenPerformanceTests`: time to fetch the draft, tokens and components for a 500-block page. |

**The browser gap**: the release smoke test has no browser, and the end-to-end browser
harness is #25. So FR-044's "open the editor … wait for autosave" is covered in two halves:
the editor's autosave path in vitest, and the served route plus a real save in the smoke
test. The part only a browser proves, and SC-005's interaction latency on a real engine, is
run by hand from [quickstart.md](./quickstart.md) and recorded in `quickstart-results.md`,
until #25 automates it. This is recorded as exception **E-5**.

---

## ED-22 Routing and entry points

**Decision**:
- **New admin routes**:
  - `/sites/:siteId/pages/:pageId/edit` (page editor);
  - `/sites/:siteId/components/:componentId/edit` (component editor);
  - `/sites/:siteId/design` (design tokens).
- **Loading**: the editor routes load lazily (`() => import(...)`), so TipTap and the
  editor code stay out of the admin shell's main chunk.
- **Entry points**: the page tree gains **Edit** per page, shown when `canEditPageDrafts`.
  The component list gains **Edit**, shown when the member may manage components. The site
  detail view links to **Design**.
- **Layout**: the editor route uses the `bare` layout meta with its own top bar, giving
  the canvas the full width.

---

## Configuration keys

None. The preview widths, autosave timing, undo depth and token limits are product
constants. Limits that affect stored data (token counts, rich text length) are contract
constants, as in 010.

## Resolved unknowns

The Technical Context had no open questions left after ED-01 … ED-22. The canvas library
question from the constitution is answered by ED-01, ED-02 and ED-03.
