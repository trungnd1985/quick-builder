# Research: Richer Rich Text Formats

**Feature**: 035 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Each decision has an id (RT-nn) that the plan, data model, contracts and tasks refer to.

---

## RT-01 The new formats are an additive change to page schema version 1

**Decision**: the new node types, marks and attributes are added to `docs/contracts/page-schema/v1.schema.json`. Page schema §6 explicitly allows this inside version 1: "a new node type, or a new optional prop on an existing type". `schemaVersion` stays 1, and no stored document is migrated (FR-004).

The changes to `$defs` (exact shapes in [contracts/rich-text.md](./contracts/rich-text.md)):

| Definition | Change |
|---|---|
| `rtBlock` | gains `rtHeading`, `rtBlockquote`, `rtImage` and `rtTable`. It is now used only by `rtDoc.content`, the top level. |
| `rtListBlock` | **new**: paragraph, bullet list or ordered list. This is exactly what `rtBlock` allowed before. `rtListItem.content` now refers to it, so list items accept the same content as before, no more and no less. |
| `rtParagraph` | gains optional `attrs.align`. |
| `rtHeading`, `rtBlockquote`, `rtImage`, `rtTable`, `rtTableRow`, `rtTableCell`, `rtTableHeader` | **new**. |
| `rtMark` | gains `underline`, `strike` and `code`. |
| `rtText.marks` | `maxItems` rises from 3 to 6, so every mark can apply at once. Raising a limit is additive. |

**Nesting falls out of the definitions** (spec edge case "Nesting"):
- headings, quotes, images and tables exist only at the top level;
- quotes hold `rtListBlock` items;
- table cells hold paragraphs only.

So no code rule is needed for nesting.

**Alignment values** are `start`, `center` and `end`, the vocabulary blocks already use (`align` on Heading, Text and Rich Text blocks). The toolbar labels them left, centre and right. The admin and sites are left-to-right today, and `start`/`end` stay correct if a right-to-left site ever exists. `align` is absent when the paragraph follows its block (FR-016 / US6 scenario 2). `start` is stored explicitly when the editor chose "left" in a block aligned otherwise.

**Rationale**: additive changes keep every stored document valid (SC-001) and need no migration or `v2` file. Every reader ships in the same image (server validator, admin validator, renderer, canvas), so no reader sees a type it doesn't know, except an admin tab opened before the upgrade (RT-13).

**Alternatives considered**:
- **A `v2` schema with a forward migration**: nothing breaking is happening, so it would be pure cost.
- **Adding the new types to the existing `rtBlock`**: list items would then accept headings, tables and images, against the nesting rules.

---

## RT-02 Rules beyond the schema: counts and table shape

**Decision**: page-schema §3 gains three rules, checked in code on both sides after the existing rich text length rule (rule 9). The order stays deterministic: §3, "checks run in this order".

| Rule | Code | Where it is reported |
|---|---|---|
| 10. At most **50** images and **20** tables in one rich text | `rich_text_too_many_images`, `rich_text_too_many_tables` | at `…/props/content` (a Rich Text block) or at the field (an item value) |
| 11. Every row of a table has the same number of cells (no merged cells, spec Out of scope) | `table_not_rectangular` | at the row |
| 12. Header cells appear only in the first row, and a first row with any header cell is all header cells | `table_header_misplaced` | at the cell |

The schema itself bounds the other dimensions:
- rows per table: `maxItems` 200;
- cells per row: `maxItems` 20;
- paragraphs per cell: `maxItems` 50;
- `alt`: `maxLength` 300, as on the Image block.

Text in table cells, headings and quotes counts toward rule 9's 20,000 characters, because `richTextChars` already sums every `text` node at any depth.

**Emptiness**: `RichTextRules.IsEmpty(doc)` (and `isEmptyRichText` in `richTextRules.ts`) is true when the document has no `text` node and no `image` node. The item form, the item checks and the server's `missing_required` all use it instead of "zero characters", so an image-only value is kept and counts as a value (spec edge case "Empty structures").

**Where the code lives**: one `RichTextRules` class in `QuickBuilder.Pages.Contracts`, beside `PageSchema` and `SchemaSubsetValidator`, so the Cms module reuses it for item values without a new module edge. Cms already reads `rtDoc` through `Pages.Contracts`. Its TypeScript twin is `editor/schema/richTextRules.ts`. The constants go in `PageSchema` and `limits.ts`, and `PageSchemaLimitsParityTests` covers them.

**Alternatives considered**: expressing rule 11 in JSON Schema is impossible, because the subset has no way to compare sibling arrays. Rule 10 would need `contains` with `maxContains`, which the subset doesn't support (research P-09). Widening the subset for two rules isn't worth it.

---

## RT-03 The schema subset validators need no change

**Decision**: the new definitions use only keywords both validators already support: `oneOf`, `const`, `enum`, `required`, `additionalProperties`, `maxItems`, `minItems` and `maxLength` (`SchemaSubsetValidator.SupportedKeywords`, and the TypeScript `subsetValidator.ts`). `BranchTypes` keeps choosing the `oneOf` branch by its `type` constant. So errors inside a heading or a table still point at the right property, not at "matches no branch".

**Check**: the existing `UnsupportedKeywords` scan fails the build if a keyword slips in.

---

## RT-04 Rendering: allow-listed elements, existing image pipeline

**Decision**: `RichTextRenderer` gains one case per new node and mark. The allow-list, which page-schema §"Rendering" also lists:

| Contract | HTML |
|---|---|
| `heading` level n | `<h2>` … `<h4>` |
| `blockquote` | `<blockquote>` |
| `align` | `class="qb-align--start|center|end"` on the `p` or `h*`, the classes blocks already use (FR-016: no inline style) |
| `underline` | `<u>` |
| `strike` | `<del>`, announced as deleted text (US2 scenario 4) |
| `code` | `<code>` |
| `table` | `<div class="qb-rt-table" tabindex="0"><table>`, then `<tr>`, `<th scope="col">` and `<td>` |
| `image` | `<figure class="qb-rt-image"><img src srcset sizes width height alt loading="lazy" decoding="async"></figure>` |

**Tables**: there is no `thead`. The first row's `th scope="col"` gives screen readers the column headers (US5 scenario 3). The wrapper scrolls sideways (`overflow-x: auto`), and `tabindex="0"` lets keyboard users scroll it: axe `scrollable-region-focusable`.

**Images** reuse `ImageRenderer.SourceSet`, and its `Sizes` rule with the enclosing section width, capped by the file's natural width. That is 013's pipeline, so variants, the public paths and the `missing file → nothing` rule (FR-017) behave exactly like the Image block. To collect the media files a page needs, `RenderReferences.WalkRichText` now also collects `image.attrs.media.mediaId`.

**Old output is unchanged**: a paragraph without `align` still renders as a bare `<p>`, and existing marks render as before. The existing renderer golden files must not change (SC-001), and new golden files cover the new nodes.

**Styling**: in `editor/blocks/blocks.css`, which the canvas and sites share (it's embedded in Rendering):
- `.qb-rich-text h2–h4` use `--qb-font-heading` and sizes derived from `--qb-base-size`;
- `blockquote` gets a border and an inset;
- tables get borders, padding and a header background drawn from the colour tokens;
- `.qb-rt-image img` has `max-width: 100%`.

**Alternatives considered**: `<s>` for strikethrough, but the spec asks for "deleted text". `thead`/`tbody` would need a second wrapper for no gain, since `scope` already gives the headers.

---

## RT-05 The canvas mirrors the renderer

**Decision**: `editor/richtext/RichTextView.ts`, the read-only canvas drawing, gains the same nodes with Vue render functions, still never `v-html`. Images on the canvas use the editor's existing media lookup for thumbnails, and show the "image missing" placeholder the Image block uses. `CanvasMarkupParity.spec.ts` (011) extends to the new nodes.

---

## RT-06 Editor: TipTap extensions configured to the contract

**Decision**: add the official TipTap extensions in the same release train (`~3.31.3`) and configure each to produce only contract shapes:

| Format | Extension | Configuration |
|---|---|---|
| Headings | `@tiptap/extension-heading` | `levels: [2, 3, 4]`; extra `parseHTML` rules map `h1` to 2 and `h5`/`h6` to 4 (US4 scenario 1) |
| Quote | `@tiptap/extension-blockquote` | `content: '(paragraph|bulletList|orderedList)+'` |
| Underline, strike, code | `@tiptap/extension-underline`, `-strike`, `-code` | strike shortcut overridden to `Mod-Shift-x` (FR-008; TipTap's default is `Mod-Shift-s`) |
| Alignment | `@tiptap/extension-text-align` | `types: ['heading', 'paragraph']`, `alignments: ['start', 'center', 'end']`, `defaultAlignment: null` |
| Tables | `@tiptap/extension-table` (Table, TableRow, TableCell, TableHeader) | `resizable: false`; cells and headers `content: 'paragraph+'`; `colspan`, `rowspan` and `colwidth` are dropped by `toContractRichText` |
| Images | **own node** `ContractImage` | atom, block, attributes `media`, `alt` and `decorative`, a Vue node view (thumbnail, missing state, upload placeholder); `parseHTML` matches only `img[data-qb-media]`, so pasted `<img>` never parses (RT-08) |
| Undo, redo | `@tiptap/extensions` (`UndoRedo`) | **item form only** (RT-07) |

`@tiptap/extension-image` isn't used: its node stores `src`, and the contract stores a media id with alt and decorative. A 60-line node is simpler than overriding every attribute.

`toContractRichText` and `fromContractRichText` (011's normaliser) extend to the new shapes. As today, everything the editor emits passes through `toContractRichText` before it reaches a document. That function enforces:
- the nesting (unwrapping blocks where they aren't allowed);
- `align` only on paragraphs and headings;
- images without a `media` id are dropped (the upload placeholder, RT-08);
- merged table cells are split (a fallback to RT-08);
- marks are limited to the six, in a fixed order.

**Rationale**: these are the same editor and the same normaliser (Principle IX, 011 ED-03). Each extension is a few hundred lines, maintained with the core TipTap already pinned.

**Alternatives**: writing table editing by hand. Row and column commands and cell selection are what prosemirror-tables already does, and `@tiptap/pm` already bundles prosemirror-tables.

---

## RT-07 Toolbar: one component, icon buttons, a roving tab stop

**Decision**: `RichTextToolbar.vue` is rewritten and shared by the canvas Rich Text block and the item field (clarification Q3, FR-027):

- **Groups, in order**:
  - history (undo, redo);
  - block style, a native `<select>` of Paragraph and Headings 2 to 4;
  - inline styles (bold, italic, underline, strikethrough, code);
  - lists and quote;
  - insert (link, image, table);
  - alignment (left, centre, right as a pressed-button group).

  Separators are `role="separator"`. The toolbar wraps by group (`flex-wrap` on group containers).
- **Contextual groups** appear after the main groups while the selection is in a table (add or remove a row or column, header row, delete table) or on an image (alternative text field, decorative switch, replace, remove).
- **Keyboard**: the WAI-ARIA toolbar pattern. The toolbar has one tab stop, with a roving `tabindex`: Left and Right move, Home and End jump, and the `<select>` keeps Up and Down for itself (FR-007).
- **Icons**: `IconButton` (015), whose tooltip already shows its accessible name on hover and on focus. The tooltip text becomes "name (shortcut)", with the shortcut written for the platform (⌘ on macOS, Ctrl elsewhere). About 20 new paths are drawn inline in `IconButton`'s path table, with no icon library (Principle XI).
- **Props**:
  - `editor`, `siteId`;
  - `allowed`: the field's format groups (RT-09); the page editor passes everything;
  - `history`: `{ undo, redo, canUndo, canRedo }` from the host.

  The page editor passes its store's history, so undo stays the page's one history (FR-009, 011 ED-12). The item field passes TipTap's `UndoRedo` commands.

**AltGr**: on Windows, Ctrl+Alt is AltGr, and on some layouts (German, Polish) AltGr+2 types a character. The heading shortcuts check `event.key`: when it is a printable character other than the digit, the key press is typing and the shortcut doesn't fire.

The editor's global key handler (`editor/state/keyboard.ts`) already forwards Ctrl/Cmd+Z and Ctrl/Cmd+Shift+Z from inside a rich text to the store, so the page editor's shortcuts need no change.

---

## RT-08 Paste: prepare the HTML, parse with the schema, upload pasted files

**Decision**: there are three steps, all in `editor/richtext/paste.ts`.

1. **`preparePastedHtml(html)`**, which runs as TipTap's `transformPastedHTML`. It parses with `DOMParser` into an inert document (never attached, so no scripts run) and:
   - counts and removes every `<img>` (images that exist only as addresses, clarification Q2);
   - splits merged cells: each `colspan`/`rowspan` cell keeps its content in the first position, and empty cells fill the rest (US4 scenario 5);
   - removes `style`, `class`, `font`, `script`, `style` elements, `iframe` and the like;
   - returns the cleaned HTML and the number of images removed.

   ProseMirror then parses it with the editor's schema. Anything the schema has no rule for becomes paragraphs or text, and `toContractRichText` normalises the rest. When images were removed, the editor raises a polite live-region notice (FR-012).
2. **Pasted and dropped files** go through `handlePaste`/`handleDrop` in `editorProps`. For each file that is an accepted image type:
   - an upload placeholder node is inserted at the cursor: `ContractImage` with a local `uploadId` and no `media`, so `toContractRichText` drops it and no document ever stores it;
   - the file is uploaded with `mediaApi.upload(siteId, file)` into the library's top folder, through 013's checks;
   - on success, the node gets `media` and an empty `alt`, which the publish check catches (RT-10);
   - on refusal or failure, the placeholder is removed and the reason is announced (FR-026).

   Other file types are ignored, as today. When the member lacks `media.files.upload`, or the field doesn't allow images, the images are left out with the notice and nothing is uploaded.
3. **Allowed formats** (RT-09) apply after parsing: content from a disallowed group is converted (FR-023) by `restrictToAllowed(doc, allowed)`, which is shared with `toContractRichText`.

**The paste test set** (FR-013, SC-004) lives in `src/admin/tests/editor/paste/`. Each sample is a captured `text/html` clipboard payload with an `expected.json`:
- Microsoft Word for the web and Word desktop;
- Google Docs;
- LibreOffice Writer;
- a web article with an image, inline styles and a table with merged cells;
- a hostile page with scripts, event handlers, `style`, `iframe` and `data:` images.

`paste.spec.ts` runs `preparePastedHtml`, TipTap's `generateJSON` with the editor's extensions, and `toContractRichText`, compares with the expected result, and validates against `rtDoc`.

**Alternatives**:
- *Fetching images from pasted addresses*: rejected in clarification Q2 (server-side request forgery, and the product must work offline).
- *A sanitiser library such as DOMPurify*: the schema parser already throws away everything without a rule. The preparation step only handles what parsing can't (images, merged cells), so a new dependency isn't justified.

---

## RT-09 Allowed formats per field: an optional `formats` setting

**Decision**: the collection schema contract's `richText` type gains one optional setting (clarification Q1, FR-022):

```json
{ "type": "richText", "formats": { "headings": false, "images": true, "tables": false } }
```

- Each property is optional and defaults to `true`. A missing `formats` means everything is allowed, which is today's behaviour. That makes it an additive change under collection-schema §9: "a new optional setting with a default that matches today's behaviour".
- The groups are:
  - `headings`: headings and quotes;
  - `images`;
  - `tables`.
- **Validity**: a value that uses a disallowed group is **well-formed but invalid**. The new problem is `format_not_allowed`, with `format`: `headings`, `images` or `tables`. Like `too_long`, it is stored, makes the item need attention, and blocks publishing (FR-024, 015 clarification Q2).
- **Schema changes** (contract §7):
  - changing a group from allowed to not allowed is **reported**, a tightened setting: values are kept and counted as needing attention (FR-025);
  - changing it back is **free**;
  - removing the `formats` object, or setting everything to `true`, is free.
- **Builder**: `FieldEditor.vue` shows three checkboxes for a rich text field, "Allow headings and quotes", "Allow images" and "Allow tables", all checked by default. Unchecking writes `false`.
- **Admin**: `itemValueRules.ts` mirrors the check, and the item field passes `allowed` to the toolbar and to paste.
- **Golden examples**: in `docs/contracts/collection-schema/examples/items/`, add `rich-text-format-not-allowed.json`, plus a schema example with `formats`.

**Alternatives**:
- **An array of allowed names**: the subset validator has no `uniqueItems`, and an object with booleans reads more clearly in the schema document.
- **Per-node allow-lists**: too fine-grained for the summary and caption case the clarification targets.

---

## RT-10 Publish check: rich text images need alternative text or "decorative"

**Decision** (FR-014):

- **Pages and components**: `PublishingWrites.PublishContentAsync`, the step shared by manual publish, scheduled publish and rollback, runs `RichTextRules.ImagesWithoutAlt(document)` after the schema check. It refuses with a new `pages.image_alt_missing` (422), whose `problems` list gives each image's JSON pointer, so the editor can select the block. Drafts are unaffected.

  A scheduled publish that meets it fails like any other failed check (012 `schedule_failed`).
- **Items**: `ItemValidation` adds the problem `image_alt_missing` with `fieldId` and a `path` (a JSON pointer inside the value). Like `missing_required`, it is reported, sets attention, and blocks publishing.

  `ItemReport` problems gain an optional `path` property. 015's `items-api.yaml` declares `Report.problems` items with `fieldId`, `problem` and `limit` and doesn't forbid other properties, so adding `path` (and `format`, RT-09) is additive. [contracts/api-changes.md](./contracts/api-changes.md) records it.
- **Admin**: the Rich Text block and the item field show an "Add alternative text" marker on such images, and the publish bar's error lists them.

**The Image block is unchanged.** It has no such check today (spec US3 scenario 2 corrected). Adding one would change how existing pages publish, so it goes to the backlog.

---

## RT-11 Media usage and invalidation come from the existing extractors

**Decision**:

- **Pages**: `DependencyExtractor.WalkRichText` collects `image` media ids beside page links. Draft media references (`DraftMediaReference`, rewritten on every draft save), version media references (written at publish) and the render dependency graph (`media` edges, so `RefreshPagesShowingMedia` re-renders pages when a file is replaced or deleted) all derive from it. So usage, "in live use", the delete warning and cache invalidation work for rich text images with no new table (FR-018, Principle VIII).
- **Items**: `ItemIndexer.MediaValues` adds the image ids inside `richText` fields, so `item_media` (draft, live, scheduled, history) and `IItemMediaUsageReader` include them. `ItemValidation`'s existing `media_missing` covers missing and other-site files (spec edge case "Another site's media").
- **No backfill**: no stored rich text can contain an image before this release, so existing reference rows are already complete. `index_version` stays unchanged.

---

## RT-12 Converting a rich text field to text

**Decision**: `FieldConversion.Values` (C#) and its preview twin in `schemaChanges.ts` extend 014's `richText → text` conversion. It is already **lossy**, so the classification doesn't change (FR-019).

- headings and quote paragraphs become lines, like paragraphs;
- each table row becomes a line, with its cells' text joined by single spaces;
- images are dropped, without their alternative text.

The preview's examples show the converted text, so editors see what's lost. New golden conversion cases join 014's `FieldConversionTests`.

---

## RT-13 An admin tab opened before the upgrade

**Problem** (FR-020): an old admin bundle doesn't know the new nodes. Its `fromContractRichText` turns a heading into a paragraph the moment the Rich Text block is edited, and drops images and tables. Saving would silently lose formatting.

**Decision**:
- From this release, every admin request carries `X-QB-Rich-Text: 2`, added in `api/request.ts`.
- The three write paths that accept rich text content compare it with what's stored:
  - page and component draft save (`DraftWrites`);
  - item draft save (`SaveItemDraft`). Item create has no stored draft to protect, and duplicate copies on the server, so an old tab loses nothing by either.

  When the header is missing or below 2 **and** the stored draft uses any version-2 format (`RichTextRules.UsesExtendedFormats`), the write is refused with **409 `pages.admin_outdated` or `cms.admin_outdated`**. The message reads "This page of the admin is out of date. Reload it to keep editing; your last change was not saved."
- Old tabs editing content without the new formats keep working (spec edge case).
- **Old tabs on new content mostly fail before saving anyway**: the old bundle's own validator rejects unknown node types in untouched blocks. This check covers the remaining case, a block the old tab re-normalised.

The admin shows `admin_outdated` with a reload button in `ConflictBanner` (the existing pattern for 409s).

**Alternatives considered**:
- **Comparing an admin build id**: the build id changes with every release, so every old tab would have to reload, even when nothing would be lost.
- **Refusing every request without the header**: harsher than the spec asks for.

---

## RT-14 Undo and redo in the item form

**Decision**: the item field's TipTap gets `UndoRedo` with TipTap's defaults (`depth: 100`, a 500 ms grouping delay). Ctrl/Cmd+Z, Ctrl/Cmd+Shift+Z and Ctrl+Y work while the field has focus. The toolbar buttons reflect `editor.can().undo()` and `editor.can().redo()`. Each rich text field has its own history (FR-009). A reload or a copied version replaces the content without adding history steps (`setContent` with `emitUpdate: false` and `addToHistory: false`).

---

## RT-15 Performance

- **Typing at the limits** (SC-006): a 20 by 200 table is 4,000 cells. ProseMirror's incremental updates keep typing local. The cost is `toContractRichText` plus `JSON.stringify` on every change in the item field and the page block.
  - *Measure*: a vitest benchmark normalises a document at the limits (50 images, 20 tables with one at 20 × 200, 20,000 characters), with a budget of 30 ms in jsdom. Real typing latency is checked by hand in quickstart §7.
  - *Fallback*: debounce `onChange` to animation frames. The editor state stays live, and only the emitted document lags by one frame.
- **Rendering**: the renderer is linear in the nodes. `RichTextRenderPerformanceTests` (unit) renders the limit document 100 times, with a budget of 5 ms each. The existing 012 page render targets must hold for a page with one such block (smoke performance report).
- **Validation**: rules 10–12 are one walk, beside rule 9's walk, merged into it.

---

## RT-16 Security tests

**Decision**: new invalid examples in `docs/contracts/page-schema/examples/invalid/`, run by both validators (SC-005, SC-007):
- heading levels 1 and 5;
- unknown `align`;
- a style attribute on a paragraph;
- a table nested in a list item, and an image in a list item or in a cell;
- a quote holding a quote;
- a ragged table, and a header cell in row 2;
- `href` on an image, and a non-uuid `mediaId`;
- 51 images, and 21 tables;
- an unknown mark, and 7 marks.

New valid examples:
- `rich-text-every-format.json`;
- `rich-text-limits.json`.

Renderer tests feed hostile values (`<script>` in text and in alt, `javascript:` in a link, quotes in alt) and assert that only the allow-listed elements and attributes appear, and that all text is encoded.

---

## RT-17 Upload placeholders and image editing in the editor

**Decision**: `ContractImage`'s node view shows:
- the thumbnail from the media library's thumbnail variant, the same lookup as the Image block;
- a "Missing image" state when the lookup says the file is gone;
- an uploading state with progress.

Selecting the image shows the image group in the toolbar (RT-07):
- an alternative text input, `maxlength` 300, prefilled from the library's alt when inserted from the picker (FR-010);
- a "Decorative" switch, which disables the input;
- Replace, which opens the picker;
- Remove.

Inserting from the picker uses `MediaBrowser` with `initialKind="image"`, which 015 added.

---

## RT-18 New dependencies

| Package | Why | Licence | Maintenance | Alternative considered |
|---|---|---|---|---|
| `@tiptap/extension-heading`, `-blockquote`, `-underline`, `-strike`, `-code`, `-text-align`, `-table` | the formats, with the same editor as today | MIT | released together with the TipTap core already pinned (`~3.31.3`) | hand-written nodes: more code to maintain, and tables especially |
| `@tiptap/extensions` | `UndoRedo` for the item field | MIT | same | the page editor's store, but the item form has no store |

No backend dependency. HTML editors (TinyMCE, CKEditor) were rejected before the spec (`docs/backlog.md`): they store HTML (Principle IX) and are GPL.

---

## RT-19 Tests

| Suite | Covers |
|---|---|
| `PageSchemaContractTests` (C#) and `documentValidator.spec.ts` | the new valid and invalid examples, same verdict on both sides (SC-005, SC-007) |
| `RichTextRulesTests` (unit, C#) and `richTextRules.spec.ts` | rules 10–12, `ImagesWithoutAlt`, `UsesExtendedFormats` |
| `RichTextRendererTests` (unit, beside `TextRendererTests`), render golden files, and `CanvasMarkupParity.spec.ts` | every format, hostile values, unchanged output for old documents (SC-001) |
| `RichTextRenderPerformanceTests` | RT-15 |
| `PublishRichTextImageTests` (integration) | `pages.image_alt_missing` for page, component and scheduled publish |
| `RichTextMediaUsageTests` (integration) | page, component and item usage, live use, delete warning, re-render on replace (FR-018) |
| `ItemRichTextFormatsTests` (integration) | `formats` setting: `format_not_allowed`, attention, publish refused, narrowing reported with counts, widening free (FR-022–FR-025) |
| `AdminOutdatedTests` (integration) | `admin_outdated` for pages and items, and old tabs still saving old content (FR-020) |
| `FieldConversionTests` (unit) and `schemaChanges.spec.ts` | RT-12 |
| collection-schema golden item examples (both sides) | `format_not_allowed`, `image_alt_missing` |
| `UpgradeFrom015Tests` (integration) | a snapshot of 015 pages and items: valid, rendered byte-identical (SC-001) |
| admin: `RichTextToolbar.spec.ts`, `contractImage.spec.ts`, `tables.spec.ts`, `toContractRichText.spec.ts`, `paste.spec.ts`, `RichTextInput.spec.ts`, `RichTextBlock.spec.ts`, `FieldEditor.spec.ts` | toolbar pattern and shortcuts, axe on every state (SC-003), paste set (SC-004), field restrictions, undo |
| `PageSchemaLimitsParityTests` | new constants |
| `LoggingTests` | no rich text text, alt or file names in logs (FR-021) |

---

## RT-20 Documentation

- **`docs/contracts/page-schema.md`**:
  - §2: the rich text bullet lists the new formats;
  - §3: rules 10–12;
  - §8: the examples table;
  - "Rendering": the allow-list;
  - a short "Additions in 035" note under §6.
- **`docs/contracts/collection-schema.md`**:
  - §2: `richText` settings `formats`;
  - §3: validity;
  - §5: the problem codes `format_not_allowed` and `image_alt_missing`;
  - §7: the tightened `formats` rule and the extended `richText → text` conversion.
- **`docs/operations.md`**: upgrade note covering the new formats, reloading open admin tabs, and the alt check on publish.
- **`docs/backlog.md`**: remove the follow-up entry, and add "alt check on the Image block", "code blocks, captions, image size" and "justified alignment".
