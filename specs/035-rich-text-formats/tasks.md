---

description: "Task list for 035-rich-text-formats"
---

# Tasks: Richer Rich Text Formats

**Input**: Design documents from `/specs/035-rich-text-formats/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - golden examples for every contract change;
  - tests for every changed endpoint;
  - unit tests for rules;
  - 80 % coverage on the publish path.
- The spec measures behaviour through automated checks: SC-001 and SC-003 … SC-007.
- Within each story, test tasks come first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US6 from spec.md), so each story can
be implemented and tested on its own. The per-field allowed formats (FR-022 … FR-025,
clarification Q1) cut across stories. They have their own phase after US6.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US6)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `REND` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `RT` = `src/admin/src/editor/richtext`
- `ITEMUI` = `src/admin/src/features/cms/items`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`

**Conventions**:
- **One shape, many places**: every format is added in the same order everywhere:
  1. the contract (`CONTRACTS/page-schema/v1.schema.json`) and its golden examples;
  2. the C# and TypeScript rules;
  3. `RT/toContractRichText.ts` (the only way into a document);
  4. the TipTap configuration in `RT/tiptap.ts`;
  5. the toolbar;
  6. the server renderer `REND/Rendering/RichTextRenderer.cs`;
  7. the canvas `RT/RichTextView.ts`;
  8. styles in `ADMIN/editor/blocks/blocks.css`, shared by the canvas and sites.

  A story isn't done until every place has its format.
- **Old output never changes**: the existing renderer golden files and
  `CanvasMarkupParity.spec.ts` cases must stay byte-identical (SC-001).
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task. `npm run lint` enforces this.
- **Logs**: no rich text, alt text or file names (FR-021).

**References**:
- decisions: `RT-xx` in [research.md](./research.md);
- contract shapes: [contracts/rich-text.md](./contracts/rich-text.md);
- errors and problems: [contracts/api-changes.md](./contracts/api-changes.md) and
  [data-model.md §4](./data-model.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the dependencies and the SC-001 baseline, captured before anything changes.

- [X] T001 Add `@tiptap/extension-heading`, `@tiptap/extension-blockquote`, `@tiptap/extension-underline`, `@tiptap/extension-strike`, `@tiptap/extension-code`, `@tiptap/extension-text-align`, `@tiptap/extension-table` and `@tiptap/extensions`, all `~3.31.3`, to `src/admin/package.json`. Run `npm install` so `src/admin/package-lock.json` updates, then confirm `npm run build` and `npm test` still pass (RT-18).
- [X] T002 [P] Capture the SC-001 baseline before any renderer change: a 015-era fixture under `ITESTS/Rendering/Fixtures/upgrade-015/`. It holds:
  - page, component and item documents using every 015 rich text feature (paragraphs, nested lists, ordered start, hard breaks, bold, italic, both link kinds, 20,000 characters);
  - the HTML the current renderer produces for each **page and component**, as `*.html`. Items aren't rendered until #16, so the fixture stores their values only.

  Write `ITESTS/Rendering/UpgradeFrom015Tests.cs`, which seeds the fixture and validates every document. It renders pages and components and compares them byte for byte, and checks that item values still validate and are unchanged after loading. It passes now and must keep passing.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the contract structure, the shared rules, the outdated-admin guard and the new toolbar shell, which every story builds on.

**⚠️ CRITICAL**: no user story work starts until this phase is complete.

### Contract structure (RT-01, RT-02)

- [X] T003 In `CONTRACTS/page-schema/v1.schema.json`, add `$defs/rtListBlock` (paragraph, bullet list, ordered list) and point `rtListItem.content` to it. `rtBlock` keeps its three branches for now. Raise `rtText.marks.maxItems` from 3 to 6. Add the "Additions in 035" note under §6 of `CONTRACTS/page-schema.md`. Confirm that `PageSchemaContractTests` (`ITESTS/Pages/PageSchemaContractTests.cs`) and `ADMINTESTS/editor/documentValidator.spec.ts` still give the same verdict on every existing example.
- [X] T004 [P] Add `MaxRichTextImages = 50` and `MaxRichTextTables = 20` to `PAGESC/PageSchema.cs` and `ADMIN/editor/schema/limits.ts`, and extend `PageSchemaLimitsParityTests` to cover both.
- [X] T005 [P] Write `UTESTS/Pages/RichTextRulesTests.cs` (it must fail first) for `RichTextRules`:
  - counting images and tables (rule 10), at 50/51 and 20/21;
  - ragged rows (rule 11);
  - a header cell in row 2, and a mixed first row (rule 12);
  - `ImagesWithoutAlt` (blank, whitespace, decorative, missing attrs);
  - `UsesExtendedFormats` (each 035 node, mark and `align`; false for every 015 document).
- [X] T006 Implement `PAGESC/RichTextRules.cs` so T005 passes. It provides one walk over an `rtDoc` `JsonElement` and returns:
  - the first rule failure as a `(rule, pointer)` pair, for rules 10 → 11 → 12;
  - `ImagesWithoutAlt(doc, basePointer)`, a list of pointers;
  - `UsesExtendedFormats(doc)`.

  It also holds the constants `ClientHeader = "X-QB-Rich-Text"` and `CurrentClientLevel = 2` (RT-13).
- [X] T007 [P] Write `ADMIN/editor/schema/richTextRules.ts`, the TypeScript twin of T006, and `ADMINTESTS/editor/richTextRules.spec.ts` with the same cases as T005.
- [X] T008 Run rules 10–12 after rule 9:
  - in `PAGES/Infrastructure/Schema/PageDocumentValidator.cs`, for every Rich Text block, at `…/props/content`;
  - in `CMS/Infrastructure/ItemValueValidator.cs`, for every `richText` value, as the malformed-value problem with the rule name, like `rich_text_too_long`;
  - in `ADMIN/editor/schema/documentValidator.ts` and `ITEMUI/itemValueRules.ts`.

  Add `rich_text_too_many_images`, `rich_text_too_many_tables`, `table_not_rectangular` and `table_header_misplaced` to the rule-name mapping and to `CONTRACTS/page-schema.md` §3.

### Emptiness (spec edge case "Empty structures", RT-02)

- [X] T009 [P] Add `IsEmpty` cases to `UTESTS/Pages/RichTextRulesTests.cs` and `ADMINTESTS/editor/richTextRules.spec.ts` (they must fail first). An empty doc, empty paragraphs, an empty table and an empty quote are empty. One image alone, and one character, are not.
- [X] T010 Implement `RichTextRules.IsEmpty` in `PAGESC/RichTextRules.cs` and `isEmptyRichText` in `ADMIN/editor/schema/richTextRules.ts`. Use them:
  - in `ITEMUI/inputs/RichTextInput.vue`, in place of `richTextChars(doc) === 0`;
  - in `ITEMUI/itemValueRules.ts`;
  - in `CMS/Infrastructure/ItemValidation.cs`, for `missing_required` on rich text fields.

  T009 passes. *(Done: the check lives in `CMS/Infrastructure/ItemValueValidator.cs` `Check`, which `ItemValidation` calls. The admin case "an image-only value is sent, not cleared" needs image nodes, so it moved to T051.)*

### Outdated admin tabs (FR-020, RT-13)

- [ ] T011 [P] Write `ITESTS/Pages/AdminOutdatedTests.cs` (it must fail first). Store a page draft, a component draft and an item draft that use a 035 format by inserting the rows directly, since no format exists in the API yet. Save each without the header and expect 409 `admin_outdated`, with nothing written. Save each with `X-QB-Rich-Text: 2` and expect success. Save content without 035 formats with no header and expect success. Cover item draft save and duplicate. Item create has no stored draft, so it can't trigger the check.
- [ ] T012 Pages:
  - the controllers in `PAGES/Features/SavePageDraft/` and `PAGES/Features/SaveComponentDraft/` read `RichTextRules.ClientHeader` into their commands;
  - `PAGES/Infrastructure/DraftWrites.cs` refuses with `PagesErrors.AdminOutdated` (409, `admin_outdated`) when the level is below 2 and the stored draft `UsesExtendedFormats`;
  - map the error in `PAGES/PagesErrorResults.cs`.
- [ ] T013 Cms: the controllers in `CMS/Features/Items/SaveItemDraft/` and `DuplicateItem/` pass the header level. `CMS/Infrastructure/ItemWrites.cs` refuses the same way (`CmsErrors.AdminOutdated`), mapped in `CMS/CmsErrorResults.cs`. T011 passes.
- [ ] T014 [P] Admin: in `ADMIN/api/request.ts`, send `X-QB-Rich-Text: 2` on every request, and add a test to the existing request spec. Show `admin_outdated` with a Reload button in `ADMIN/editor/ConflictBanner.vue` and in `ITEMUI/ItemFormView.vue`, with i18n keys `errors.adminOutdated` and `common.reload`. Add a spec case to `ADMINTESTS/editor/EditorView.spec.ts` and `ADMINTESTS/cms/items/ItemFormView.spec.ts`.
- [ ] T015 [P] Document `admin_outdated` in `specs/011-editor-core/contracts/editor-api.yaml` (draft save) and `specs/015-cms-items/contracts/items-api.yaml` (draft save, duplicate).

### Editor shell (RT-06, RT-07)

- [ ] T016 [P] Restructure `RT/toContractRichText.ts` around two block contexts: top-level blocks and list or quote blocks. Keep the marks in a fixed order, from a single list that later stories extend. Output for every existing case must not change: `ADMINTESTS/editor/toContractRichText.spec.ts` stays green, and a new case pins the mark order.
- [ ] T017 [P] In `ADMIN/components/IconButton.vue`:
  - add an optional `shortcut` prop, shown in the tooltip as "Name (Ctrl+B)", with ⌘ on macOS;
  - add the icon paths for undo, redo, bold, italic, underline, strikethrough, code, bulleted list, numbered list, quote, link, image, table, the three alignments, and the table commands (add row above and below, add column before and after, delete row, delete column, header row, delete table).

  Extend `ADMINTESTS/iconButton.spec.ts`.
- [ ] T018 Write `ADMINTESTS/editor/RichTextToolbar.spec.ts` (it must fail first) for the toolbar shell, with today's controls only:
  - **Keyboard pattern**: `role="toolbar"` with one tab stop; Left, Right, Home and End move; groups have `role="separator"` between them.
  - **Buttons**: each has a name and a shortcut tooltip, and `aria-pressed` follows the cursor.
  - **Props**: `allowed` hides groups, and `history` absent hides the history group.
  - **axe**: passes in every state.
- [ ] T019 Rewrite `RT/RichTextToolbar.vue` as the grouped icon toolbar (RT-07):
  - props `editor`, `siteId`, `allowed` (default: all) and optional `history`;
  - groups for history, block style, inline, lists and quote, insert, and alignment, each rendered only when it has controls;
  - a roving `tabindex`;
  - the existing bold, italic, list and link controls moved into it.

  Keep `.qb-canvas .qb-rt-toolbar` (`ADMIN/editor/editor.css`) and `.item-rich-text .qb-rt-toolbar` (`ADMIN/styles.css`) styles working, with group separators. T018 passes.

**Checkpoint**: the contract has its new structure, the rules run on both sides, old tabs are guarded, and the toolbar is the new shell. Nothing visible has changed for content.

---

## Phase 3: User Story 1 - Structure a text with headings and quotes (Priority: P1) 🎯 MVP

**Goal**: headings 2–4 and quotes in Rich Text blocks and item fields, rendered on sites.

**Independent Test**: quickstart §1. Format a text with headings 2, 3 and 4 and a quote, publish it, and check the editor and the published page.

### Tests for User Story 1 ⚠️

- [ ] T020 [P] [US1] Add golden examples under `CONTRACTS/page-schema/examples/`:
  - valid: `valid/rich-text-headings-quotes.json`;
  - invalid: `invalid/props_invalid--rich-text-heading-level-1.json`, `…-heading-level-5.json`, `…-quote-in-quote.json`, `…-heading-in-list.json` and `…-table-in-quote.json` (a stub table shape; it stays invalid after US5 because quotes can't hold tables).

  List them in `CONTRACTS/page-schema.md` §8. They run in `PageSchemaContractTests` and `documentValidator.spec.ts`.
- [ ] T021 [P] [US1] Write `UTESTS/Rendering/RichTextRendererTests.cs` for headings (`h2`–`h4`) and quotes (`blockquote` holding `p` and lists), with encoded text and no extra attributes. Add the new golden HTML files, and assert the existing ones are unchanged.
- [ ] T022 [P] [US1] Extend `ADMINTESTS/editor/toContractRichText.spec.ts`:
  - heading levels 2–4 kept;
  - levels 1, 5 and 6 from TipTap JSON mapped to 2, 4 and 4;
  - a heading in a list item unwrapped to a paragraph;
  - a quote in a quote flattened;
  - empty quotes kept.
- [ ] T023 [P] [US1] Extend `ADMINTESTS/editor/RichTextToolbar.spec.ts`:
  - the block style menu sets paragraph and headings 2–4 and follows the cursor;
  - the quote button toggles;
  - Ctrl/Cmd+Alt+0, 2, 3 and 4 work;
  - a key press whose `event.key` is a printable character other than the digit (AltGr typing on a German or Polish layout) types the character and changes no heading (RT-07).

  Extend `ADMINTESTS/editor/CanvasMarkupParity.spec.ts` with headings and quotes.
- [ ] T024 [P] [US1] Add a case to `ADMINTESTS/cms/items/ItemField.spec.ts`: an item rich text field with a heading and a quote, saved and reloaded, stays unchanged.
- [ ] T025 [P] [US1] Add `richText → text` cases for headings and quote paragraphs as lines (RT-12) to `UTESTS/Cms/FieldConversionTests.cs` and `ADMINTESTS/cms/schemaChanges.spec.ts`.

### Implementation for User Story 1

- [ ] T026 [US1] In `CONTRACTS/page-schema/v1.schema.json`, add `rtHeading` (`attrs.level` enum 2–4, required) and `rtBlockquote` (content `rtListBlock`, 1–200) to `rtBlock`. Update the rich text bullet in `CONTRACTS/page-schema.md` §2. T020 passes.
- [ ] T027 [US1] In `RT/tiptap.ts`:
  - configure Heading with `levels: [2, 3, 4]`, plus parse rules mapping `h1` to 2 and `h5`/`h6` to 4, and heading shortcuts that ignore AltGr typing (RT-07);
  - configure Blockquote with `content: '(paragraph|bulletList|orderedList)+'`;
  - add both to `richTextExtensions()`.

  Extend `toContractRichText` and `fromContractRichText` in `RT/toContractRichText.ts`. T022 passes.
- [ ] T028 [US1] Add the block style `<select>` (Paragraph, Heading 2, 3, 4) and the quote button to `RT/RichTextToolbar.vue`, with i18n keys under `editor.richText.*`. T023's toolbar cases pass.
- [ ] T029 [P] [US1] Render `heading` and `blockquote` in `REND/Rendering/RichTextRenderer.cs`. T021 passes.
- [ ] T030 [P] [US1] Draw `heading` and `blockquote` in `RT/RichTextView.ts`. T023's parity cases pass.
- [ ] T031 [P] [US1] Style `.qb-rich-text h2`, `h3` and `h4` (`--qb-font-heading`, sizes from `--qb-base-size`) and `blockquote` (an inset with a start border from the colour tokens) in `ADMIN/editor/blocks/blocks.css`. Style the item field copies in `ADMIN/styles.css` (`.item-rich-text__body`).
- [ ] T032 [P] [US1] Convert headings and quotes in `richText → text` in `CMS/Domain/FieldConversion.Values.cs` and `ADMIN/features/cms/schemaChanges.ts`. T025 passes.
- [ ] T033 [US1] Run T024 against `ITEMUI/inputs/RichTextInput.vue` and confirm it passes. No code change is expected, since the field uses `richTextExtensions()` and `toContractRichText` from T027.

**Checkpoint**: US1 is demoable on its own, as quickstart §1.

---

## Phase 4: User Story 2 - Inline styles, undo and keyboard shortcuts (Priority: P2)

**Goal**: underline, strikethrough and inline code everywhere, plus undo and redo buttons, with the page editor keeping its one history.

**Independent Test**: quickstart §2.

### Tests for User Story 2 ⚠️

- [ ] T034 [P] [US2] Add golden examples:
  - valid: `valid/rich-text-inline-marks.json`, with all six marks on one text node;
  - invalid: `invalid/props_invalid--rich-text-seven-marks.json` and `…-unknown-mark-highlight.json`.

  List them in `CONTRACTS/page-schema.md` §8.
- [ ] T035 [P] [US2] Extend `UTESTS/Rendering/RichTextRendererTests.cs` with `u`, `del` and `code`, in the stored nesting order, alongside existing marks and links.
- [ ] T036 [P] [US2] Extend `ADMINTESTS/editor/RichTextToolbar.spec.ts`:
  - the underline, strikethrough and code buttons, with `aria-pressed`;
  - the shortcuts Ctrl/Cmd+U, Ctrl/Cmd+Shift+X and Ctrl/Cmd+E;
  - the history group, whose buttons are disabled when `canUndo`/`canRedo` are false.

  Extend `toContractRichText.spec.ts` and `CanvasMarkupParity.spec.ts` with the new marks.
- [ ] T037 [P] [US2] Write `ADMINTESTS/cms/items/RichTextInput.spec.ts`:
  - five edits, three undone by button, one redone by Ctrl+Y, in order;
  - the buttons disabled at the ends;
  - a reload of the value adds no history step.
- [ ] T038 [P] [US2] Extend `ADMINTESTS/editor/EditorView.spec.ts`: undo from the Rich Text block's toolbar takes back the page store's last operation, the same one Ctrl/Cmd+Z takes back.

### Implementation for User Story 2

- [ ] T039 [US2] Add `underline`, `strike` and `code` to `rtMark` in `CONTRACTS/page-schema/v1.schema.json` and `CONTRACTS/page-schema.md` §2. T034 passes.
- [ ] T040 [US2] In `RT/tiptap.ts`, add Underline, Strike (with `addKeyboardShortcuts` overridden to `Mod-Shift-x`) and Code. Add the marks to the ordered list in `RT/toContractRichText.ts`.
- [ ] T041 [US2] Add the inline group (underline, strikethrough, code) and the history group (undo and redo, driven by the `history` prop) to `RT/RichTextToolbar.vue`, with i18n keys. T036 passes.
- [ ] T042 [US2] In `ITEMUI/inputs/RichTextInput.vue`:
  - add `UndoRedo` from `@tiptap/extensions` to this editor only (pass extra extensions through `createRichTextEditor`'s options in `RT/tiptap.ts`);
  - pass `history` from `editor.can()` to the toolbar, refreshed on transactions;
  - set content from outside with `emitUpdate: false` and `addToHistory: false`.

  T037 passes.
- [ ] T043 [US2] In `ADMIN/editor/blocks/rich-text/RichTextBlock.vue`, pass the page store's undo, redo, `canUndo` and `canRedo` as the toolbar's `history`. T038 passes.
- [ ] T044 [P] [US2] Render `u`, `del` and `code` in `REND/Rendering/RichTextRenderer.cs` (T035 passes), and draw them in `RT/RichTextView.ts`.
- [ ] T045 [P] [US2] Style `.qb-rich-text code` (monospace, a tinted background from tokens) in `ADMIN/editor/blocks/blocks.css` and the item field copy in `ADMIN/styles.css`.

**Checkpoint**: US1 and US2 both work.

---

## Phase 5: User Story 3 - Images from the media library (Priority: P2)

**Goal**: images from the media library in rich text, with alt text required at publish, counted in usage, rendered responsively.

**Independent Test**: quickstart §3.

### Tests for User Story 3 ⚠️

- [ ] T046 [P] [US3] Add golden examples:
  - valid: `valid/rich-text-image.json` and `valid/rich-text-image-without-alt.json`;
  - invalid: `invalid/props_invalid--rich-text-image-in-list.json`, `…-image-src.json`, `…-image-media-not-uuid.json`, `…-image-alt-301.json` and `rich_text_too_many_images--51.json`.

  List them in `CONTRACTS/page-schema.md` §8.
- [ ] T047 [P] [US3] Extend `UTESTS/Rendering/RichTextRendererTests.cs` for images:
  - `figure.qb-rt-image > img`, with `srcset`/`sizes` from the section width and the natural-width cap;
  - `width`/`height`, `loading` and `decoding`;
  - empty `alt` when decorative;
  - a hostile alt encoded;
  - a missing file, or another site's file, renders nothing.

  Extend the `RenderReferences` tests (`UTESTS/Rendering/`) so that rich text image ids are inputs.
- [ ] T048 [P] [US3] Write `ITESTS/Pages/PublishRichTextImageTests.cs`:
  - a page and a component with an image lacking alt: the draft saves, publish gives 422 `pages.image_alt_missing` with each pointer;
  - a scheduled publish fails as `schedule_failed`;
  - decorative or alt-filled images publish.
- [ ] T049 [P] [US3] Write `ITESTS/Media/RichTextMediaUsageTests.cs`. A rich text image in a page draft, a published page, a component and an item draft and published value appears in the file's usage, with "in live use" after publishing. The delete warning lists them. Replacing the file re-renders the published page (`MediaChangeRefreshTests` pattern). Deleting it leaves no image in the page HTML.
- [ ] T050 [P] [US3] Add item golden example `CONTRACTS/collection-schema/examples/items/rich-text-image-without-alt.json`, giving `image_alt_missing` with its `path`, and update `expected.json`. Run it through `UTESTS/Cms/CollectionSchemaGoldenTests.cs` and `ADMINTESTS/cms/items/itemValues.spec.ts`. Add an item case to T048's file: publishing refused, attention set, draft saved.
- [ ] T051 [P] [US3] Write `ADMINTESTS/editor/contractImage.spec.ts` (plus, in `ADMINTESTS/cms/items/RichTextInput.spec.ts`, an item value holding only an image is sent, not cleared, moved from T010):
  - insert from the picker, with alt prefilled from the library;
  - edit alt, toggle decorative (input disabled), replace and remove;
  - the missing-file state;
  - the "Add alternative text" marker;
  - the image group appears only with an image selected;
  - axe;
  - `toContractRichText` drops an image with no media.

### Implementation for User Story 3

- [ ] T052 [US3] Add `rtImage` (`media` required, `alt` ≤ 300, `decorative`) to `rtBlock` in `CONTRACTS/page-schema/v1.schema.json` and `CONTRACTS/page-schema.md` §2. Rule 10's image count already runs (T006). T046 passes.
- [ ] T053 [US3] Collect rich text image media ids in `PAGES/Domain/DependencyExtractor.cs` `WalkRichText`, which feeds draft and version media references and render dependencies, and in `REND/Domain/RenderReferences.cs`.
- [ ] T054 [US3] Render `image` in `REND/Rendering/RichTextRenderer.cs`, reusing `ImageRenderer.SourceSet` and `Sizes` from `REND/Rendering/Blocks/MediaRenderers.cs` (make them `internal static` helpers if they aren't). T047 passes.
- [ ] T055 [US3] Pages publish check: in `PAGES/Infrastructure/PublishingWrites.cs` `PublishContentAsync`, after the schema check, collect `RichTextRules.ImagesWithoutAlt` for every Rich Text block. Refuse with `PagesErrors.ImageAltMissing(pointers)` (422 `pages.image_alt_missing`, with `problems[].pointer`), mapped in `PAGES/PagesErrorResults.cs`. Document it in `specs/012-publishing-rendering/contracts/publishing-api.yaml`. T048 passes.
- [ ] T056 [US3] Items:
  - `CMS/Infrastructure/ItemValidation.cs` adds `image_alt_missing` with `path` per image;
  - `CMS/Domain/ItemReport.cs` gains the optional `path`;
  - `CMS/Infrastructure/ItemIndexer.cs` `MediaValues` includes images in `richText` fields;
  - `ITEMUI/itemValueRules.ts` mirrors the problem.

  Add the codes, marked "added by 035", to `specs/015-cms-items/data-model.md` §2 and to `items-api.yaml` `Report`. T049's item cases and T050 pass.
- [ ] T057 [US3] Write `RT/ContractImage.ts`: a TipTap node (atom, block, attributes `media`, `alt`, `decorative` and a non-contract `uploadId`; `parseHTML` matching only `img[data-qb-media]`). Write `RT/ContractImageView.vue`, a node view with a thumbnail through the editor's existing media lookup, the missing state, the uploading state and the "Add alternative text" marker. Add the node to `richTextExtensions()`, and add `image` to `RT/toContractRichText.ts` (an image without `media` is dropped).
- [ ] T058 [US3] Write `RT/ImageGroup.vue`, the contextual toolbar group for a selected image: an alt input with `maxlength` 300, a decorative switch, Replace and Remove. Add an insert-image button to `RT/RichTextToolbar.vue` that opens `MediaBrowser` with `initialKind="image"` and prefills alt from the library (FR-010). Add i18n keys. T051 passes.
- [ ] T059 [P] [US3] Draw `image` in `RT/RichTextView.ts` (the thumbnail, or the Image block's missing placeholder), and extend `CanvasMarkupParity.spec.ts`. Style `.qb-rt-image` (`max-width: 100%`, spacing) in `ADMIN/editor/blocks/blocks.css` and `ADMIN/styles.css`.
- [ ] T060 [US3] Show publish refusals by place:
  - `ADMIN/features/publishing/errorMessages.ts` turns `pages.image_alt_missing` into a message with the number of images, and `ADMIN/features/publishing/PublishMenu.vue` emits the first pointer so the page editor selects that block;
  - `ITEMUI/ItemPublishBar.vue` lists `image_alt_missing` per field.

  Add i18n keys, and spec cases in the existing publishing specs under `ADMINTESTS/publishing/` and in `ADMINTESTS/cms/items/ItemPublishBar.spec.ts`.
- [ ] T061 [US3] Images in rich text drop out of `richText → text` in `CMS/Domain/FieldConversion.Values.cs` and `ADMIN/features/cms/schemaChanges.ts`, with test cases in `UTESTS/Cms/FieldConversionTests.cs` and `schemaChanges.spec.ts`.

**Checkpoint**: US3 works on its own on top of the foundation.

---

## Phase 6: User Story 4 - Paste from other sources (Priority: P2)

**Goal**: pasted content keeps what the contract allows, web images are left out with a notice, and pasted image files upload.

**Independent Test**: quickstart §4. `paste.spec.ts` covers the sample set.

**Depends on**: US1 (heading mapping) and US3 (image upload placeholder). Tables in the samples are checked after US5 (T078).

### Tests for User Story 4 ⚠️

- [ ] T062 [P] [US4] Capture the paste sample set in `ADMINTESTS/editor/paste/`: the `text/html` clipboard payload of Word for the web, Word desktop, Google Docs, LibreOffice Writer, a web article (inline styles, an image, a table with a `colspan` and a `rowspan`), and a hostile page (`<script>`, `onerror`, `style`, `iframe`, `data:` and remote images). Each sample gets a `*.expected.json` (contract document) and a `*.expected-notice.json` (images left out). Until US5, table expectations are paragraphs of cell text, marked `// until US5`.
- [ ] T063 [P] [US4] Write `ADMINTESTS/editor/paste.spec.ts`. For each sample, run `preparePastedHtml`, then `generateJSON` with `richTextExtensions()`, then `toContractRichText`. Compare with the expected result, validate against `rtDoc`, and check the image count. Assert no `style`, `class` or script content survives (SC-004, SC-005).
- [ ] T064 [P] [US4] Write `ADMINTESTS/editor/pasteUpload.spec.ts`, with the media API mocked:
  - a pasted PNG file shows a placeholder, then an image with empty alt;
  - an upload refused (type, size, 403) removes the placeholder and announces the reason;
  - the rest of the paste is kept;
  - without `media.files.upload`, nothing uploads and a notice shows;
  - dropped files behave the same;
  - the placeholder is never in the emitted document.

### Implementation for User Story 4

- [ ] T065 [US4] Write `RT/paste.ts` with `preparePastedHtml(html)`. It parses with `DOMParser` into an inert document, removes and counts `img`, splits `colspan`/`rowspan` cells (US4 scenario 5), drops `style` and `class` attributes and `script`, `style`, `iframe`, `object` and `embed` elements, and returns `{ html, imagesLeftOut }`. T063 passes for its non-table expectations.
- [ ] T066 [US4] Wire `transformPastedHTML` in `RT/tiptap.ts` to `preparePastedHtml`, reporting `imagesLeftOut` through a new `onNotice` option. In `RT/RichTextToolbar.vue`'s host area, show it in a polite live region: "N images were left out. Add images from the media library." It appears in both the Rich Text block and `ITEMUI/inputs/RichTextInput.vue`. Add i18n keys with plural forms.
- [ ] T067 [US4] Add file paste and drop to `RT/paste.ts` and `RT/tiptap.ts` (`handlePaste` and `handleDrop`). For accepted image types (`ACCEPTED_BY_KIND.image` in `ADMIN/api/media.ts`):
  - insert a `ContractImage` with an `uploadId`;
  - upload with `mediaApi` into the top folder;
  - set `media` on success, or remove the node and announce the reason on failure;
  - skip with a notice when the member lacks `media.files.upload` (the existing permission store) or when `allowed.images` is false.

  T064 passes.

**Checkpoint**: pasting keeps headings, lists, inline styles and uploaded images; the rest is cleaned.

---

## Phase 7: User Story 5 - Simple tables (Priority: P3)

**Goal**: rectangular tables with an optional header row, editable by keyboard, accessible and scrollable on sites.

**Independent Test**: quickstart §5.

### Tests for User Story 5 ⚠️

- [ ] T068 [P] [US5] Add golden examples:
  - valid: `valid/rich-text-table.json` and `valid/rich-text-limits.json` (50 images, 20 tables, one of 200 rows × 20 columns, 20,000 characters);
  - invalid: `invalid/table_not_rectangular.json`, `table_header_misplaced.json`, `rich_text_too_many_tables--21.json`, `props_invalid--rich-text-table-in-list.json`, `…-image-in-cell.json`, `…-cell-colspan.json`, `…-table-201-rows.json` and `…-table-21-columns.json`.

  List them in `CONTRACTS/page-schema.md` §8.
- [ ] T069 [P] [US5] Extend `UTESTS/Rendering/RichTextRendererTests.cs` for tables: `div.qb-rt-table[tabindex=0] > table`, `tr`, `th[scope=col]` in the first row, `td`, and paragraphs in cells.
- [ ] T070 [P] [US5] Write `ADMINTESTS/editor/tables.spec.ts`:
  - insert gives a 3 × 3 table with a header row;
  - Tab and Shift+Tab move between cells, and Tab in the last cell adds a row;
  - add and remove a row and a column;
  - toggle the header row;
  - adding a 21st column or a 201st row is refused with a message;
  - the table group shows only inside a table;
  - axe.

  Extend `toContractRichText.spec.ts`: colspan, rowspan and colwidth dropped, and a merged cell split.
- [ ] T071 [P] [US5] Add `richText → text` table cases (each row a line, cells joined by spaces) to `UTESTS/Cms/FieldConversionTests.cs` and `schemaChanges.spec.ts`.

### Implementation for User Story 5

- [ ] T072 [US5] Add `rtTable`, `rtTableRow`, `rtTableCell` and `rtTableHeader` to `rtBlock` in `CONTRACTS/page-schema/v1.schema.json` and `CONTRACTS/page-schema.md` §2. Rules 10–12 already run. T068 passes.
- [ ] T073 [US5] In `RT/tiptap.ts`, add Table (`resizable: false`), TableRow, and TableCell and TableHeader with `content: 'paragraph+'`. In `RT/toContractRichText.ts`, map tables, drop span and width attributes, split merged cells, and pad short rows.
- [ ] T074 [US5] Write `RT/TableGroup.vue`, the contextual group: add a row above or below, add a column before or after, delete a row or column, toggle the header row, delete the table. Refuse beyond 20 columns or 200 rows, and beyond 20 tables, with a toolbar message. Add insert-table (3 × 3 with a header row) to `RT/RichTextToolbar.vue`. Add i18n keys. T070 passes.
- [ ] T075 [P] [US5] Render tables in `REND/Rendering/RichTextRenderer.cs` (T069 passes), and draw them in `RT/RichTextView.ts` with parity cases.
- [ ] T076 [P] [US5] Style `.qb-rt-table` (`overflow-x: auto`, borders and padding from tokens, a header background) in `ADMIN/editor/blocks/blocks.css` and `ADMIN/styles.css`.
- [ ] T077 [P] [US5] Convert tables in `richText → text` in `CMS/Domain/FieldConversion.Values.cs` and `ADMIN/features/cms/schemaChanges.ts`. T071 passes.
- [ ] T078 [US5] Update the paste samples' table expectations in `ADMINTESTS/editor/paste/*.expected.json` (remove `// until US5`) so tables are kept and merged cells split. `paste.spec.ts` passes.

**Checkpoint**: tables work in both editors and on sites.

---

## Phase 8: User Story 6 - Align text (Priority: P3)

**Goal**: left, centre and right alignment on paragraphs and headings, with the block's alignment as the default.

**Independent Test**: quickstart §6.

### Tests for User Story 6 ⚠️

- [ ] T079 [P] [US6] Add golden examples: valid `valid/rich-text-align.json`, and `valid/rich-text-every-format.json` (every node, mark and alignment, with links inside a heading, a quote and a table cell), plus a renderer case for those links in `UTESTS/Rendering/RichTextRendererTests.cs`; invalid `invalid/props_invalid--rich-text-align-justify.json`, `…-paragraph-style.json` and `…-align-on-quote.json`.
- [ ] T080 [P] [US6] Extend `UTESTS/Rendering/RichTextRendererTests.cs`: `class="qb-align--center"` on `p` and `h*`, never `style`; a paragraph without `align` renders unchanged inside an aligned block. Extend `RichTextToolbar.spec.ts` (the alignment group as pressed buttons, following the cursor) and `toContractRichText.spec.ts` (`textAlign` mapped to `align`, `left` stored as `start`).

### Implementation for User Story 6

- [ ] T081 [US6] Add optional `attrs.align` (`start`, `center`, `end`) to `rtParagraph` and `rtHeading` in `CONTRACTS/page-schema/v1.schema.json` and `CONTRACTS/page-schema.md` §2. T079 passes.
- [ ] T082 [US6] Configure TextAlign in `RT/tiptap.ts` (`types: ['heading', 'paragraph']`, `alignments: ['start', 'center', 'end']`, no default), map it in `RT/toContractRichText.ts`, and add the alignment group to `RT/RichTextToolbar.vue` with i18n keys.
- [ ] T083 [P] [US6] Render the `qb-align--*` classes in `REND/Rendering/RichTextRenderer.cs` and `RT/RichTextView.ts`. T080 passes. If the block-level align class on the Rich Text wrapper would otherwise win, add `.qb-rich-text :is(p, h2, h3, h4).qb-align--…` in `ADMIN/editor/blocks/blocks.css`.

**Checkpoint**: all six stories work.

---

## Phase 9: Allowed formats per field (FR-022 … FR-025, clarification Q1)

**Purpose**: Designers limit a rich text field's formats; items are checked against them, and narrowing is a reported schema change.

### Tests ⚠️

- [ ] T084 [P] Add the collection schema examples:
  - `CONTRACTS/collection-schema/examples/items/schema.json` gains a `richText` field with `formats: { headings: false, tables: false }`;
  - `rich-text-format-not-allowed.json` gives `format_not_allowed` for `headings` and `tables`;
  - add schema-document examples for `formats` (valid; invalid with an unknown key or a non-boolean).

  Update `expected.json`, run through `UTESTS/Cms/CollectionSchemaGoldenTests.cs` and `ADMINTESTS/cms/items/itemValues.spec.ts`.
- [ ] T085 [P] Extend `UTESTS/Cms/SchemaDiffTests.cs` and `ADMINTESTS/cms/schemaChanges.spec.ts`: narrowing `formats` (any group true → false, or a missing `formats` → some false) is reported, with attention counts; widening, or removing `formats`, is free.
- [ ] T086 [P] Write `ITESTS/Cms/Items/ItemRichTextFormatsTests.cs`:
  - a field with `images: false` refuses publishing an item with an image (`format_not_allowed`, `format: images`), and its draft saves with attention;
  - narrowing a field used by 3 items previews 3 needing attention and marks them;
  - widening clears the attention.
- [ ] T087 [P] Extend `ADMINTESTS/cms/FieldEditor.spec.ts` (three checkboxes on rich text fields, checked by default, unchecking writes `false`) and `ADMINTESTS/cms/items/RichTextInput.spec.ts`:
  - the toolbar hides disallowed groups;
  - pasting headings and a table into a restricted field gives paragraphs;
  - a pasted image file isn't uploaded.

### Implementation

- [ ] T088 Add the `formats` setting to `richText` in `CONTRACTS/collection-schema/v1.schema.json` and `CONTRACTS/collection-schema.md` (§2 settings, §3 validity, §5 problem codes, §7 rules).
- [ ] T089 Server schema: read and write `formats` in `CMS/Domain/Field.cs`. Accept it in `CMS/Domain/CollectionSchemaRules.cs` and `CMS/Infrastructure/Schema/CollectionSchemaValidator.cs`. Classify it in `CMS/Domain/SchemaDiff.cs` and `CMS/Domain/FieldChange.cs`: narrowing reported, widening free. T085's C# cases pass.
- [ ] T090 Server items: `CMS/Infrastructure/ItemValidation.cs` reports `format_not_allowed` with `format`, once per field and format. `CMS/Domain/ItemReport.cs` gains the optional `format`. Document it in `items-api.yaml` `Report` and 015 `data-model.md` §2. T086 passes.
- [ ] T091 [P] Admin schema: in `ADMIN/features/cms/collectionSchema.ts` and `collectionSchemaRules.ts`, type and check `formats`. In `ADMIN/features/cms/schemaChanges.ts`, classify it. In `ADMIN/features/cms/FieldEditor.vue`, add the three checkboxes with i18n keys. T085's admin cases and T087's FieldEditor cases pass.
- [ ] T092 Admin items: `ITEMUI/itemValueRules.ts` reports `format_not_allowed`. Add `restrictToAllowed(doc, allowed)` to `RT/toContractRichText.ts`: headings and quotes become paragraphs, tables become paragraphs of row text, and images are dropped and counted, with the count shown through the T066 notice. `ITEMUI/inputs/RichTextInput.vue` passes the field's `allowed` to the toolbar, to paste, and to upload. T084's admin cases and T087 pass.

**Checkpoint**: every functional requirement is met.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T093 [P] Complete `CONTRACTS/page-schema.md`: the rendering allow-list (contracts/rich-text.md §5), §3 rules 10–12 and the §8 examples table, checked against the files. Add the one-line "extended by 035" notes in `specs/011-editor-core/contracts/page-schema-blocks.md` and `specs/012-publishing-rendering/contracts/page-schema-rendering.md`.
- [ ] T094 [P] Write `UTESTS/Rendering/RichTextRenderPerformanceTests.cs`: render `valid/rich-text-limits.json` 100 times, at 5 ms each (RT-15). Add `ADMINTESTS/editor/richTextPerformance.spec.ts`: normalising the limit document with `toContractRichText` takes 30 ms or less in jsdom. If it doesn't, apply the frame-debounce fallback in `RT/tiptap.ts`.
- [ ] T095 [P] Write the hostile set (SC-005, RT-16) in `UTESTS/Rendering/RichTextRendererTests.cs`: `<script>` in text, alt and link labels; `javascript:` links; attribute injection in alt. Assert that only allow-listed elements and attributes appear in the output.
- [ ] T096 [P] Write `ADMINTESTS/rendering/renderedRichTextA11y.spec.ts` (SC-003). It loads every rich text golden HTML file written by `RichTextRendererTests` (headings, quotes, marks, images, tables, alignment) into jsdom inside a page shell with one `h1`, runs axe, and expects no violations. Rules in scope include `heading-order`, `image-alt`, `th-has-data-cells` and `scrollable-region-focusable`.
- [ ] T097 [P] Extend `ITESTS/Observability/LoggingTests.cs`: a journey that saves and publishes rich text with images, alt text and pasted uploads logs no text, alt or file names (FR-021).
- [ ] T098 [P] Docs: `docs/operations.md` gets the upgrade notes (plan, Operator impact). In `docs/backlog.md`, remove the "Richer rich text" entry and add an Image block alt check, code blocks, captions, image size and float, table captions and widths, justified alignment, and right-to-left beyond start/end.
- [ ] T099 Run `UpgradeFrom015Tests` (T002): every baseline page and item validates and renders byte-identically (SC-001).
- [ ] T100 Run everything in Release: `dotnet test` (unit, architecture, integration), `dotnet format --verify-no-changes`, the per-package coverage gate (Pages and Rendering stay at 80 % or more on the publish and render paths), and `npm run lint`, `npm run type-check`, `npm test` and `npm run build` in `src/admin`. Fix any failures.
- [ ] T101 By hand: walk [quickstart.md](./quickstart.md) §1–§12 in a browser (SC-002 timing, SC-006 typing latency, the keyboard-only walk, a screen reader on a published table, the outdated-tab check). Record the results in `specs/035-rich-text-formats/quickstart-results.md`.
- [ ] T102 Push the branch and open the PR closing #35, with the plan's Operator impact in its description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 must land before any renderer change.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
- **US1 (Phase 3)**: after Foundational. This is the MVP.
- **US2 (Phase 4)** and **US3 (Phase 5)**: after Foundational. They are independent of US1
  and of each other, but they touch the same files (schema, `tiptap.ts`, toolbar, renderer),
  so run them one after another or merge with care.
- **US4 (Phase 6)**: after US1 and US3.
- **US5 (Phase 7)**: after Foundational. T078 also needs US4.
- **US6 (Phase 8)**: after Foundational. It touches US1's heading node.
- **Allowed formats (Phase 9)**: after US1, US3 and US5, because the groups need those
  formats to exist, and T092's paste case needs US4.
- **Polish (Phase 10)**: after everything.

### Within Each User Story

- Tests first, and they fail.
- Then follow the "one shape, many places" order: contract, rules, normaliser, TipTap,
  toolbar, renderer, canvas, styles.
- The story's checkpoint before the next.

### Parallel Opportunities

- **Setup**: T001 and T002.
- **Foundational**: T004, T005 and T007 in parallel; T011, T014 and T015 in parallel; then
  T016, T017 and T018.
- **Within each story**: the test tasks marked [P], and the renderer, canvas, styles and
  conversion tasks, which touch different files.
- **Allowed formats**: T084–T087 in parallel; T091 beside T089 and T090.

---

## Parallel Example: User Story 1

```text
# Tests together:
T020 golden examples · T021 renderer tests · T022 normaliser tests · T023 toolbar/parity tests · T024 item field test · T025 conversion tests

# After T026–T028 (contract, TipTap, toolbar), together:
T029 renderer · T030 canvas · T031 styles · T032 conversion
```

## Parallel Example: User Story 3

```text
# Tests together:
T046 examples · T047 renderer · T048 publish check · T049 media usage · T050 item example · T051 image node UI

# Server and admin streams:
T053 → T054 → T055 (server)     T057 → T058 (admin)     T056 (items)     T059 (canvas, styles)
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 and Phase 2. The foundation includes the outdated-tab guard, so the first
   release with any new format is safe for open tabs.
2. Phase 3 (US1): headings and quotes.
3. **Stop and validate** with quickstart §1. It is a shippable improvement on its own.

### Incremental delivery

- **Order**: US1 → US2 → US3 → US4 → US5 → US6 → allowed formats.
- **Demos**: each story is demoable at its checkpoint.
- **SC-001**: `UpgradeFrom015Tests` runs at every checkpoint.

### One PR

The plan delivers one PR closing #35, as earlier features did. The checkpoints are where to
stop and verify, not separate merges.

---

## Implementation notes (deviations from the task text)

Recorded during `/speckit-implement`, so the tasks above can still be read as written:

- **Packages** (T001): the new TipTap packages are pinned at 3.31.3, written as `~3.31.3` like
  the others. A plain `~` install picked 3.31.4, whose peer is a newer `@tiptap/pm` than the
  one installed.
- **Baseline** (T002): the fixture is built through today's API, not stored as SQL (035 has no
  migration). Only the Rich Text blocks' markup is compared, because the page around it embeds
  a hash of `blocks.css`, which 035 changes. The golden file is
  `tests/fixtures/rendering/upgrade-015/page.html`; the existing renderer golden files under
  `tests/fixtures/rendering/golden/` also pin every valid example.
- **Limits parity** (T004): the parity check is
  `DesignTokenContractTests.The_admins_page_schema_limits_match_the_contract`; the plan's
  `PageSchemaLimitsParityTests` doesn't exist.
- **Rule names** (T006): the failure record's pointer is `Location`, like `SchemaFailure`,
  because the analyzers forbid "pointer" in public names. Item values have no rule names: a
  value breaking rules 10–12 is simply malformed, as for rule 9.
- **Emptiness** (T010): the check is in `ItemValueValidator.Check`, which `ItemValidation`
  calls. A required rich text that holds an empty document is now `missing_required`.
- **Outdated admins** (T011–T015):
  - error codes are module-prefixed like every other: `pages.admin_outdated` and
    `cms.admin_outdated`;
  - item **duplicate** is not checked: it copies on the server, so an old tab loses nothing;
    the test asserts that an old tab's duplicate keeps the 035 format;
  - the item check is in the `SaveItemDraft` slice, which reads the stored draft inside the
    write; `ItemWrites` is unchanged;
  - copying a version into a page draft (`PublishingHistory`) passes "keeps every format",
    since the server does the copy;
  - the test clients send `X-QB-Rich-Text: 2`, as the admin does; `AdminOutdatedTests`
    removes it to act as an old tab;
  - the page draft saves are documented in 010's `pages-api.yaml`, where those endpoints
    live, not in 011's `editor-api.yaml`;
  - the admin shows a "Reload the page" button (a full reload, to get the new admin) in
    `ConflictBanner` and the item form; the autosave state is `outdated`.
- **Mark order** (T016): marks keep their stored order (the renderer nests them in that
  order) and repeats are dropped, up to 6; a fixed order would change the nesting of text an
  editor re-saves.
- **Toolbar** (T018, T019): the allowed format groups are typed in `RT/formats.ts`
  (`AllowedFormats`, `ALL_FORMATS`). The toolbar counts the editor's transactions to keep
  its pressed states current. Shared layout styles are in `ADMIN/styles.css`; the canvas's
  colours stay in `ADMIN/editor/editor.css`.
- **Conversion** (T025, T032 and later): the admin only classifies `richText → text` as lossy;
  the server converts and builds the preview's examples, so only `FieldConversion.Values.cs`
  and its tests change. `schemaChanges.ts` needs nothing.
- **Headings** (T027): the heading and paragraph extensions' own shortcuts are switched off,
  and `BlockStyleKeys` handles Ctrl/Cmd+Alt+0, 2, 3 and 4: on Windows it needs the typed key
  to be the digit, so AltGr typing wins; on macOS it reads the physical key, since Option
  changes `key`. List items hold `paragraph (paragraph|bulletList|orderedList)*` in TipTap,
  so headings can't be typed into them. The quote's shortcut is TipTap's `Mod+Shift+B`.
- **Golden pages** (T029): the full-page golden files link `blocks.css` by a content hash, so
  every one changes by that one line when `blocks.css` does; the rich text markup is unchanged.
- **Styles** (T031): rich text heading sizes use the Heading block's tokens for the same level
  (`text-2xl`, `text-xl`, `text-lg`) with fallbacks, since a site can edit its tokens.
- **Unknown mark example** (T034): the existing
  `invalid/props_invalid--rich-text-unknown-mark.json` used `underline`, which 035 allows; it now
  uses `highlight`, rather than adding a second unknown-mark example. The same change was made
  in `PageDocumentValidatorTests`.
- **Page history** (T043): `EditorView` provides its history (`pageHistoryKey` in
  `state/history.ts`), and the Rich Text block injects it; the store has no history of its own.
  Undo and redo are disabled while the page is read-only.
- **Shortcut tests** (T036, T037): key events carry `keyCode`, as browsers send it, because
  ProseMirror reads it for shortcuts with Shift.
- **Images, server** (T046–T055, T061):
  - the schema has no shared `media` definition, so `rtImage` repeats the Image block's inline
    `{ mediaId: uuid }` shape;
  - the publish test is `ITESTS/Publishing/PublishRichTextImageTests.cs`, beside the other
    publishing tests, and also covers the item case;
  - an item's rich text field reports, in order: `image_alt_missing`, then `media_missing` or
    `file_kind` for the first image whose file is gone or not an image, each with the image's
    `path`. Alternative text comes first because the item golden validators know no media,
    and a second golden example, `rich-text-image-missing.json`, covers the missing file;
  - `ProblemResponse` leaves out `format` and `path` when they are null, so 015 responses are
    unchanged; `ItemReport` stores them in the `attention` column;
  - `RichTextRules` gained `RichTextBlocks` (a page document's Rich Text blocks) and
    `ImageMedia` (a rich text's images by media id), used by the publish check and the indexer.
- **Images, admin** (T051, T056–T060):
  - the image node's view is plain DOM in `RT/ContractImage.ts` (there is no
    `ContractImageView.vue`): TipTap's Vue node views need `EditorContent`, which neither
    editor uses; its words come in through `richTextExtensions({ siteId, imageLabels })`;
  - the canvas draws images with `RT/RichTextImage.ts`; the valid example no longer holds an
    image whose file is gone, since the canvas can't know that before the image fails to load
    (the renderer unit tests cover leaving it out);
  - the media library dialog moved out of `MediaControl` into
    `features/media/MediaPickerDialog.vue`, which the toolbar also opens; it carries
    `data-qb-dialog`, so a Rich Text block keeps editing while it has focus;
  - the image settings are a panel row (`RT/ImageGroup.vue`) outside the toolbar's roving tab
    stop, like the link panel;
  - a page's publish refusal names how many images, and `PublishMenu` emits `refused` with
    the pointers, so the editor selects the first image's block; an item shows
    `image_alt_missing` through `problemMessage`, on its field and in the form's summary
    (there is nothing to change in `ItemPublishBar`).
- **Paste** (T062–T067):
  - the samples are **written to match** each source's clipboard HTML (Word desktop's `mso`
    markup and conditional comments, Word for the web's heading paragraphs and classes, Google
    Docs' style-based bold, LibreOffice's classes, a web article, a hostile page); none was
    captured from a live clipboard. Capturing real payloads is part of the by-hand walk (T101),
    and any difference becomes a new sample;
  - `preparePastedHtml` keeps inline `style` while parsing, because Google Docs and Word for the
    web carry bold, italic and underline only there; the schema never stores a style. It also
    turns Word for the web's `role="heading"` paragraphs into headings, drops Word desktop's
    hidden list bullets, removes paragraphs left empty by a removed image, and drops the
    browser's `Apple-interchange-newline`;
  - Word desktop's lists arrive as paragraphs: Word sends them as styled paragraphs, not lists;
  - there is no client-side upload permission check: every site role holds
    `media.files.upload`, and a refusal (403) takes the same path as any failed upload, with
    a notice;
  - notices are shown in the toolbar's status line (`RT/notices.ts` words them), passed in by
    the Rich Text block and the item field.
- **Tables** (T068–T078):
  - the renderer and the canvas write `<tbody>`: HTML parsers add one to a `<table><tr>`, so
    the canvas parity check would otherwise compare different trees;
  - `valid/rich-text-limits.json` is written without indentation; indented, it is 1.27 MB, over
    the 1 MiB document limit;
  - table commands live in `RT/ContractTable.ts` (`insertContractTable`, `addContractRow`,
    `addContractColumn` and the Tab key), which stop at the limits and report through the
    editor's notices; `TableGroup.vue` is a row below the toolbar, outside its roving tab stop;
  - `toContractRichText` cuts a table to 200 rows and 20 columns. A paste is the only way to get
    past them, and refusing the whole paste would lose more than cutting off the excess;
  - the page editor's `setProp` now refuses rich text that breaks rules 10–12, with a message,
    as it does for the character limit.
- **Alignment** (T079–T083):
  - TipTap stores alignment as `textAlign`; `fromContractRichText` now maps the contract's
    `align` to it, and `toContractRichText` maps it back, so alignment round-trips;
  - pressing an alignment that is already on removes it, so the paragraph follows its block again;
  - no extra CSS was needed: a paragraph's own `qb-align--*` class beats the alignment it would
    inherit from its block.
