---

description: "Task list for 066-block-borders"
---

# Tasks: Border Styles for All Blocks

**Input**: Design documents from `/specs/066-block-borders/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/block-border.md](./contracts/block-border.md), [quickstart.md](./quickstart.md)

**Tests**: Included. Constitution Principle X requires golden examples for every contract
change and golden-file tests for the rendering of every block type. The spec asks for golden
fixtures and end-to-end coverage (FR-024, FR-025). Within each story, test tasks come first
and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US4 from spec.md), so each story can be
implemented and tested on its own. The shared contract shapes and the radius token CSS are
foundational, because every story reads them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US4)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `CONTRACTS` = `docs/contracts`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `EDITOR` = `src/Modules/Editor/QuickBuilder.Editor`
- `EDITORC` = `src/Modules/Editor/QuickBuilder.Editor.Contracts`
- `REND` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `ADMIN` = `src/admin/src`
- `BLOCKS` = `src/admin/src/editor/blocks`
- `ADMINTESTS` = `src/admin/tests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `E2E` = `tests/e2e`

**Conventions**:
- **One helper per side**: every block gets its border only through `REND/Rendering/Blocks/BorderStyle.cs`
  (server) and `BLOCKS/useBorder.ts` (canvas). They produce the same classes, in the same order,
  and the same custom properties (contracts/block-border.md §3). No renderer or canvas component
  writes border CSS of its own.
- **Defaults come from the schema**: no default of `border` is hard-coded. Read it from
  `$defs/border` (research BB-04).
- **Golden files**: `UTESTS/Rendering/RendererGoldenTests.cs` renders every
  `CONTRACTS/page-schema/examples/valid/*.json` to `tests/fixtures/rendering/golden/<name>.html`.
  New examples get golden files with `QB_UPDATE_GOLDEN=1`, which are reviewed before commit.
  Existing golden files may change **only** by the five `--qb-radius-*` properties and the
  `blocks.{hash}.css` name (BB-12, SC-004).
- **Admin text**: every visible string goes through vue-i18n keys added to `ADMIN/i18n/en.json`
  in the same task.
- **Invalid examples** are named `<code>--<case>.json`, as the existing ones are.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: capture what "unchanged" means before anything changes.

- [X] T001 Record the SC-004 baseline: run `dotnet test tests/QuickBuilder.UnitTests --filter RendererGoldenTests` and `npm test -- CanvasMarkupParity tokenCss` in `src/admin` on the untouched branch, and confirm they pass. Note the current `blocks.{hash}.css` name from `tests/fixtures/rendering/golden/every-block.html` in the PR description draft, so the later diff can be checked against it.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the two contract shapes, the radius token kind, nested schema defaults and the
two border helpers, which every story builds on.

**⚠️ CRITICAL**: no user story work starts until this phase is complete.

### Contract shapes (BB-01 to BB-03, contracts/block-border.md §1–§2)

- [X] T002 [P] Add invalid page-schema examples, which must fail until T004:
  - `CONTRACTS/page-schema/examples/invalid/props_invalid--border-unknown-width.json` (`"width": "huge"`);
  - `props_invalid--border-unknown-style.json` (`"style": "double"`);
  - `props_invalid--border-raw-colour.json` (`"color": "#ff0000"`);
  - `props_invalid--border-padding-on-section.json`;
  - `props_invalid--border-extra-key.json` (`"shadow": true`).

  Add each to `CONTRACTS/page-schema/examples/invalid/expected.json` with its pointer (for
  example `/root/children/0/props/border/width`).
- [X] T003 [P] Add design-token examples, which must fail until T005:
  - valid `CONTRACTS/design-tokens/examples/valid/with-radii.json` (three custom radii);
  - invalid `editor.token_required--radii-empty.json`, `editor.duplicate_token_id--radius-shares-spacing-id.json`, `editor.duplicate_token_name--radius-case.json`, `editor.invalid_tokens--radius-over-100.json` and `editor.invalid_tokens--13-radii.json`.

  List them in `CONTRACTS/design-tokens/examples/invalid/expected.json`.
- [X] T004 Edit `CONTRACTS/page-schema/v1.schema.json`:
  - add `$defs` `radiusRef`, `borderWidth`, `borderStyle`, `border` and `borderBox`, exactly as in contracts/block-border.md §1.1;
  - add `"border": { "$ref": "#/$defs/borderBox" }` to `section` and `container`;
  - add `"border": { "$ref": "#/$defs/border" }` to `heading`, `text`, `rich-text`, `image`, `button`, `spacer`, `divider`, `embed`, `navigation`, `form`, `collection-list`, `list-card` and `component-ref`.

  Confirm that the T002 cases now fail with `props_invalid` in `ITESTS/Pages/PageSchemaContractTests.cs`, and that every existing valid example still passes.
- [X] T005 Edit `CONTRACTS/design-tokens/v1.schema.json`: add the optional `radii` array and `$defs/radius` (contracts/block-border.md §2), and update `$defs/tokenId`'s description to name four lists. Add the five default radii (data-model.md §2) to `CONTRACTS/design-tokens/examples/valid/default.json`, after `spacing`.
- [X] T006 Extend token checks 3, 5 and 6 to `radii`, on both sides:
  - server: `EDITOR/Infrastructure/TokenSetValidator.cs` (empty list when present → `editor.token_required`; ids unique across all four lists; names unique within `radii`, ignoring case);
  - editor: `ADMIN/editor/tokens/tokenValidator.ts`, the same three rules.

  Run `ITESTS/Editor/DesignTokenContractTests.cs` and `ADMINTESTS/design/designTokens.spec.ts` against the T003 examples; all pass.
- [X] T007 [P] Add `radii?: ScaleToken[]` to `TokenSet` in `ADMIN/editor/tokens/defaultTokens.ts`. Confirm that `EDITORC` embeds the updated `default.json` (its `EmbeddedResource` path in `QuickBuilder.Editor.Contracts.csproj`), so `DefaultDesignTokens.Document` carries the radii.

### Radius token CSS (BB-03)

- [X] T008 [P] Add `tests/fixtures/rendering/token-css/radii.json` (a set with custom radii, one sharing nothing with the defaults), and extend `no-content-widths.json`'s expected output with the five default `--qb-radius-*` properties. The parity tests (`UTESTS/Rendering/TokenCssParityTests.cs`, `ADMINTESTS/editor/tokenCss.parity.spec.ts`) must fail until T009 and T010.
- [X] T009 [P] Server: in `REND/Domain/TokenCss.cs`, add `TokenKind.Radius` (prefix `radius`). Emit `--qb-radius-<id>` (scaled like spacing) for the default set's radii and then the site's, and read `radii` in `TokenSet.From`. A set without `radii` uses the defaults' only. `TokenKind` is declared at the end of the same file.
- [X] T010 [P] Editor: the same in `ADMIN/editor/tokens/tokenCss.ts`. `TokenKind` gains `'radius'`, and `tokenValue` / `tokenValueIfSet` resolve it against `tokens.radii ?? defaultTokens.radii`. Update `ADMINTESTS/editor/tokenCss.spec.ts` with one radius case.

### Nested defaults (BB-04)

- [X] T011 [P] Server: add `BlockDefaults.OfDefinition(string definition, string property)` in `REND/Domain/BlockDefaults.cs`, reading `$defs/<definition>/properties/<property>/default` and following one `$ref`. Unit test it in `UTESTS/Rendering/BlockDefaultsTests.cs`: `border.width` → `none`, `border.style` → `solid`, `border.color` → `{ "token": "color-border" }`, `border.radius` → none.
- [X] T012 [P] Editor: add `definitionDefault(definition, property)` to `ADMIN/editor/schema/schema.ts`, with the same behaviour and a test in a new `ADMINTESTS/editor/schemaDefaults.spec.ts`.

### The two helpers (BB-04, BB-05)

- [X] T013 Write `UTESTS/Rendering/Blocks/BorderStyleTests.cs` first, and it fails. It covers:
  - no `border` → no class, no declaration;
  - each width × each style → `qb-border qb-border--{width} qb-border--{style}` and `--qb-border-color`;
  - width `none` with style and colour → `qb-border` only;
  - a colour token that is gone → the default's variable;
  - a radius present / gone → `qb-border--radius` + `--qb-border-radius` / nothing;
  - padding present / gone → `qb-border--pad` + `--qb-border-pad` / nothing;
  - a `border` that isn't an object → treated as absent.
- [X] T014 Implement `REND/Rendering/Blocks/BorderStyle.cs`: `internal static class BorderStyle` with `Classes(JsonElement node, RenderContext context)` (the suffix to append to a block's class list, in contract order) and `Declarations(JsonElement node, RenderContext context)` (the `(property, value)` pairs to pass to `context.Style`). It reads defaults through T011 and tokens through `TokenCss.Value` / `ValueIfSet`. T013 passes.
- [X] T015 Write `ADMINTESTS/editor/useBorder.spec.ts` with the same cases as T013, and it fails. Then implement `BLOCKS/useBorder.ts`: `useBorder(node: () => ContentNode)` returns computed `classes: string[]` and `style: Record<string, string>`, using `useEditorStore().tokens` and T012. The spec passes.
- [X] T016 Add the border rules to the end of the block layer in `BLOCKS/blocks.css`:
  - the eight rules of research BB-04 (`.qb-border`, the three widths, dashed, dotted, `--radius` and `--pad`);
  - `.qb-divider-frame { display: block; }`;
  - `.qb-component-ref` as a plain block box.

  Clipping is added in US2.

**Checkpoint**: the contracts accept a `border` and `radii`, both token CSS ports emit radii,
and both helpers produce the same output. No block uses them yet, and every existing test
passes.

---

## Phase 3: User Story 1 - Frame a block with a border (Priority: P1) 🎯 MVP

**Goal**: an Editor gives any of the 15 block types a width, style, colour and inner spacing
from one "Border" group. The canvas and the published page show the same frame.

**Independent test**: quickstart §2, §3 and §6. Border one block of each type, save, reload and
publish. Every block shows its frame on the canvas and the published page, and blocks without
one are unchanged.

### Tests for User Story 1 ⚠️

- [X] T017 [P] [US1] Add the border examples and list cases (U1). Golden files wait until T026.
  - **`CONTRACTS/page-schema/examples/valid/borders-every-block.json`**: a Section with a `borderBox` holding a Container with a `borderBox`. The Container holds the `contentChild` types, each with a different width and style and each with a colour: Heading, Text, Rich Text, Image, Button, Spacer, Divider, Embed, Navigation, Form and Collection list.
    - Add a Text and a Button with `padding`, and a bordered Button for each of the three styles.
    - Navigation uses the menu `0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40` and the Image uses the media `…9e30` from `tests/fixtures/rendering/context.json`.
    - The Collection list's `list-card` carries a `border`.
    - It has `bindingVersion: 1` (page-schema.md rule 13).
  - **`borders-component-ref.json`**: under `root`, a bordered `component-ref` to `…9e0f` from `context.json` beside an unbordered one to the same component, then a Section.
  - **The card with items**: in `UTESTS/Rendering/Lists/CollectionListRenderTests.cs`, add a `bordered-card` case beside `cards`. It renders a three-item list whose `list-card` has a thin border and `radius-md`, written to `tests/fixtures/rendering/lists/bordered-card.html` by the same `Golden(...)` helper. It must show one `[data-qb]` rule and three framed `li.qb-list__card`.
- [X] T018 [P] [US1] Write `ADMINTESTS/editor/BorderControl.spec.ts`:
  - it shows Width, Style, Colour, Corner radius and Inner spacing in that order, and no Inner spacing for a Section or Container;
  - Style and Colour are disabled under width `none`, and their values are kept;
  - choosing width `none` with nothing else set emits `undefined`;
  - every control has a label and a reachable focus;
  - one change emits one value.
- [X] T019 [P] [US1] Extend `ADMINTESTS/editor/PropertyPanel.spec.ts`:
  - every type whose schema has `border` shows a "Border" group after its own groups and before "Layer";
  - `list-empty` shows none;
  - a change calls `setProp(…, 'border', …)` once and is one undo step.

  Extend `ADMINTESTS/editor/clipboard.spec.ts` (C2, FR-011): copying and pasting, and duplicating, a bordered block keeps its `border` exactly, with a new node id.
- [X] T020 [P] [US1] Extend `ITESTS/Pages/DraftApiTests.cs`: a draft with a valid `border` saves, and the T002 cases are refused with `props_invalid` at the pointer.

### Implementation for User Story 1

- [X] T021 [US1] Add `'border'` to `ControlKind` in `BLOCKS/registry.ts`. Write `ADMIN/editor/panel/controls/BorderControl.vue` (props: the `ControlProps` of `controls/types.ts` plus `boxOnly: boolean`):
  - two native `<select>`s for width and style, with options from `resolvedPropSchema`'s `$defs`;
  - `TokenControl` for colour (kind `color`), radius (kind `radius`, inherit label "Block default") and inner spacing (kind `space`, inherit label "None", hidden when `boxOnly`);
  - it emits the whole object, or `undefined` for "nothing" (BB-08).

  In `ADMIN/editor/panel/controls/TokenControl.vue`, accept `kind: 'radius'`, listing `store.tokens.radii ?? defaultTokens.radii` by name (I2). Add the i18n keys under `editor.panel.border.*` in `ADMIN/i18n/en.json`. T018 passes, including the radius control.
- [X] T022 [US1] In `ADMIN/editor/panel/PropertyPanel.vue`, append a `border` group (label `editor.panel.groups.border`) when `propNames(current.type).includes('border')`, after the definition's groups and before `layer`. Render `BorderControl` for `control === 'border'`, with `boxOnly` true when the prop's `$ref` is `borderBox`. T019 passes.
- [X] T023 [P] [US1] Server renderers: apply `BorderStyle` (classes appended after the block's own, declarations appended to its `context.Style` call) in:
  - `REND/Rendering/Blocks/LayoutRenderers.cs`: Section, Container, Spacer, and Divider, which wraps its `hr` in `div.qb-divider-frame` carrying the border **only** when `border` is present;
  - `TextRenderers.cs`: Heading, Text, Rich Text.
- [X] T024 [P] [US1] Server renderers, continued:
  - `REND/Rendering/Blocks/MediaRenderers.cs`: Image (the classes and style go on the `img`), Button (on `.qb-button`, after its style class), Embed, and Component reference, which wraps its children in `div.qb-component-ref` carrying the border **only** when `border` is present;
  - `ListRenderers.cs`: the Collection list's outer element and `li.qb-list__card`;
  - `NavigationRenderer.cs` (the `nav`);
  - `FormRenderer.cs` (the form's outer element).

  Style calls that today pass no declarations still get the border's.
- [X] T025 [P] [US1] Canvas components: apply `useBorder` (`:class` appended, `:style` merged) in `BLOCKS/section/SectionBlock.vue`, `container/ContainerBlock.vue`, `heading/HeadingBlock.vue`, `text/TextBlock.vue`, `rich-text/RichTextBlock.vue`, `image/ImageBlock.vue` (on the `img`), `button/ButtonBlock.vue`, `spacer/SpacerBlock.vue`, `divider/DividerBlock.vue` (with the conditional frame), `embed/EmbedBlock.vue`, `navigation/NavigationBlock.vue`, `form/FormBlock.vue`, `collection-list/CollectionListBlock.vue` and `list-card/ListCardBlock.vue`. In `ADMIN/editor/canvas/ComponentRefBlock.vue`, put the border classes and style on `.qb-component-ref` and leave the editor-only badge outside them. Match the server's element and class order exactly.
- [X] T026 [US1] Generate the golden files for T017: `QB_UPDATE_GOLDEN=1 dotnet test tests/QuickBuilder.UnitTests --filter RendererGoldenTests`. Also run `--filter CollectionListRenderTests` with the same variable for `tests/fixtures/rendering/lists/bordered-card.html`. Review `tests/fixtures/rendering/golden/borders-every-block.html`, `borders-component-ref.html` and `lists/bordered-card.html` by hand against contracts/block-border.md §3. Then run `ADMINTESTS/editor/CanvasMarkupParity.spec.ts` and make it pass on both. Add a case to `CanvasMarkupParity.spec.ts` that renders `borders-every-block.json` at each preview width (desktop, tablet 767 px, mobile 480 px; `PreviewWidthSwitch.vue`). Check that the border classes and custom properties are the same at all three, and that a horizontal Container's bordered blocks stay inside their columns when stacked (C1, FR-010). Check the diff of every **existing** golden file: only the `--qb-radius-*` properties and the `blocks.{hash}.css` name may change.
- [X] T027 [US1] Button override (clarification Q2): confirm in `BLOCKS/blocks.css` that `.qb-border--*` rules come after `.qb-button--primary/--secondary/--outline`, so a bordered button shows one frame in the border's colour and width. A Button with a `border` of width `none` keeps its own 2 px frame. Add both cases to `borders-every-block.json` if T017 missed them, and regenerate.
- [X] T028 [US1] Extend `ADMINTESTS/editor/BlockRegistryCoverage.spec.ts`: every type in FR-001 has `border` in its schema, and `list-empty` doesn't (SC-002).

**Checkpoint**: US1 works on its own. Quickstart §2, §3 (except 3.3 and 3.7) and §6 pass. The
radius control already lists the default radii (T021, T010). Rounding is drawn from T016's
rule, and US2 adds clipping and its golden coverage.

---

## Phase 4: User Story 2 - Round the corners of a block (Priority: P2)

**Goal**: a radius token rounds a block's border, background and content. Images round
themselves, and a "Full" radius makes a pill or circle.

**Independent test**: quickstart §4. Set a radius on an Image without a border and on a
Container with a background and a border. Both show rounded corners on the canvas and the
published page, and nothing pokes out.

### Tests for User Story 2 ⚠️

- [X] T029 [P] [US2] Add the valid example `CONTRACTS/page-schema/examples/valid/borders-radius.json`, holding:
  - an Image with radius `radius-lg` and no line;
  - a Container with background, a thin border and `radius-md`, holding a full-width Image;
  - a square Spacer with `radius-full` and a border;
  - a Primary Button with only `radius-full`;
  - a bordered Navigation with `radius-lg`.
- [X] T030 [P] [US2] Add the valid example `CONTRACTS/page-schema/examples/valid/borders-tokens-missing.json`: borders naming a colour, a radius and a spacing token that the default set doesn't have. The golden file must show the default colour, no `qb-border--radius` and no `qb-border--pad` (FR-016). The Button keeps its 6 px corners.

### Implementation for User Story 2

- [X] T031 [US2] Add clipping to `BLOCKS/blocks.css` (BB-06): `overflow: clip` for `.qb-border--radius` on `.qb-section`, `.qb-container`, `.qb-rich-text`, `.qb-embed`, `.qb-list__card` and `.qb-component-ref` only. Add a comment that names why Navigation and Form are left out.
- [X] T032 [US2] Generate and review the golden files for T029 and T030 (`QB_UPDATE_GOLDEN=1`), and run `CanvasMarkupParity.spec.ts` on them.
- [X] T033 [US2] Extend `ADMINTESTS/editor/BorderControl.spec.ts`: with a token set holding custom `radii`, the radius control lists them by name in list order. Choosing "Block default" removes `radius` from the emitted object, and a `radius` naming a deleted token shows as "Block default" (FR-009, FR-016).

**Checkpoint**: quickstart §3.3, §3.7 (by hand) and §4 pass.

---

## Phase 5: User Story 3 - Manage the site's radius tokens (Priority: P2)

**Goal**: Designers see, add, rename, change and delete corner radii in Design, like spacing.
Changes reach visitors when the design is published.

**Independent test**: quickstart §5. Change a radius used by several blocks; every one updates
on the canvas, and on the published page after the design is published.

### Tests for User Story 3 ⚠️

- [X] T034 [P] [US3] Extend `ADMINTESTS/design/DesignTokensView.spec.ts`:
  - a set without `radii` shows the five defaults in a "Corner radius" list after Spacing;
  - add, rename, change and delete work like spacing;
  - delete asks with the block count from `tokenUsage`;
  - the last entry can't be deleted;
  - saving sends `radii`;
  - a read-only user sees the list disabled.
- [X] T035 [P] [US3] Extend `ITESTS/Editor/DesignTokensApiTests.cs`:
  - a Designer saves a set with `radii`, and it reads back equal;
  - an Editor gets 403;
  - each T003 invalid example is refused with its code.
- [X] T036 [P] [US3] Extend `ITESTS/Pages/TokenUsageApiTests.cs`: a block whose `border.radius` and `border.color` name the token counts once (BB-01).
- [X] T037 [P] [US3] Extend `ITESTS/Publishing/DesignPublishApiTests.cs`: a published page using `radius-md` shows the old value until the design is published, and the new one after (FR-017, SC-005).

### Implementation for User Story 3

- [X] T038 [US3] `ADMIN/features/design/ScaleList.vue`: accept `kind: 'radii'`, with a preview square whose `border-radius` is the step's value (instead of a spacing bar).
- [X] T039 [US3] `ADMIN/features/design/DesignTokensView.vue`:
  - `editable()` fills `radii` from `defaultTokens.radii` when absent;
  - `newId` gains the prefix `radius`;
  - `addStep` and `remove` handle `'radii'`;
  - render `<ScaleList kind="radii" :min="0" :max="100" :max-steps="12">` after Spacing, with a step of 0.125.

  Add the i18n keys `design.radii.*` and `design.confirmRemove.radii` to `ADMIN/i18n/en.json`. T034 passes.
- [X] T040 [US3] Run T035–T037. If any fails, fix `EDITOR/Infrastructure/TokenSetValidator.cs` or the reader in `EDITOR/Infrastructure/DesignTokenReader.cs`, so that `radii` passes through unchanged.

**Checkpoint**: quickstart §5 passes.

---

## Phase 6: User Story 4 - Borders travel with the site (Priority: P3)

**Goal**: export and import carry borders and radii unchanged. Archives from before import as
before.

**Independent test**: quickstart §7.1 and §7.2.

### Tests for User Story 4 ⚠️

- [X] T041 [P] [US4] Extend `ITESTS/Transfer/RestoreTests.cs`: a site with bordered blocks on a page and a component, a bordered list card and custom `radii` is exported and imported into a new site. The documents and the token set read back equal, and the published page renders the same HTML.
- [X] T042 [P] [US4] Run `ITESTS/Transfer/ArchiveFromPreviousReleaseTests.cs` unchanged against `tests/fixtures/archives/0.2.qbsite`. It must still pass, and the imported token set has no `radii` (FR-022).

### Implementation for User Story 4

- [X] T043 [US4] If T041 fails, fix only what drops the new data: `EDITOR/Features/Transfer/DesignArchivePart.cs` and `DesignArchivePart.Import.cs` must pass `radii` through as JSON. Pages' archive part must keep `border` props. No new archive version is expected (BB-11).

**Checkpoint**: quickstart §7.1 and §7.2 pass.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T044 [P] Write `E2E/specs/block-borders.spec.ts` (BB-13), using the page objects in `E2E/pages/` and the axe fixture in `E2E/fixtures/a11y.ts`:
  1. the Designer adds a radius and publishes the design;
  2. the Editor borders a Container and a Text, **keyboard only**;
  3. the test checks the computed border on the canvas;
  4. the Editor publishes;
  5. the test checks the same computed border and radius as a visitor;
  6. axe runs on the open Border group with no violations.

  Select by role and label only (025 E2E-08). Add any page-object helpers to `E2E/pages/editor.ts`.
- [X] T045 [P] Contract documents (BB-14):
  - `CONTRACTS/page-schema.md`: §2 table, §2a Border bullet, §6 "Additions in 066", §9 rendering rules (contracts/block-border.md §1.3 and §3);
  - `CONTRACTS/design-tokens.md`: §1 example and §2 `radii` row; checks 3, 5 and 6 now name four lists.

  `PageSchemaContractTests` and `DesignTokenContractTests` pass.
- [X] T046 [P] `docs/operators/operations/content.md`: one sentence saying corner radii are part of the design tokens and travel with site exports. Add the upgrade note from plan.md "Operator impact" to `docs/operators/upgrade-notes.md`, under the next release. In `docs/backlog.md`, add the deferred items from plan.md "Deferred".
- [X] T047 [P] Performance: run `UTESTS/Rendering/RendererGoldenTests.The_two_hundred_block_page_is_valid_and_renders_to_its_golden_file` and the existing render timing check on `page-200-blocks`. Give a third of that example's blocks a border, regenerate, and confirm the timing stays within budget (SC-007).
- [X] T048 Run everything in Release:
  - `./qb.ps1 test` (all .NET tests, admin lint, type check and tests);
  - `dotnet format --verify-no-changes`;
  - `npm run build` in `src/admin`;
  - `npm test -- block-borders.spec.ts` in `tests/e2e`.

  Fix any failures.
- [ ] T049 By hand: walk [quickstart.md](./quickstart.md) §2–§8 in a browser, including the SC-001 timing, the Navigation drop-down (§3.7), the old-tab check (§7.3) and the screen reader walk (§8). Record the results in `specs/066-block-borders/quickstart-results.md`.
- [ ] T050 Push the branch and open the PR closing #66, with the plan's Operator impact and the golden-file diff note (T001, T026) in its description.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none. T001 runs before any change.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
- **US1 (Phase 3)**: after Foundational. This is the MVP.
- **US2 (Phase 4)**: after US1. T033 tests US1's `BorderControl`, and T029 and T030 need the
  renderers of T023 and T024. T031 is independent of US1.
- **US3 (Phase 5)**: after Foundational, and independent of US1 and US2. Only the Design page
  and tests change.
- **US4 (Phase 6)**: after US1 (it needs bordered documents) and US3 (custom radii).
- **Polish (Phase 7)**: after every story. T044 needs US1–US3.

### Within Each Phase

- T002 and T003 before T004–T006; T004 before T011–T016.
- T013 before T014; T015 has its test first.
- In US1: T017–T020, then T021 → T022 (panel), and T023, T024, T025 in parallel (server and
  canvas), then T026 → T027 → T028.
- Each story's checkpoint before the next.

### Parallel Opportunities

- **Foundational**: T002, T003; then T007–T012 in parallel (different files); T013/T014 beside
  T015.
- **US1**: T017–T020 together; T023, T024 and T025 together.
- **US3** can run beside US1 and US2 after Foundational: it touches different files, except
  `en.json`.
- **Polish**: T044–T047 together.

---

## Parallel Example: User Story 1

```text
# Tests together:
T017 golden examples · T018 BorderControl spec · T019 PropertyPanel spec · T020 draft-save test

# After T021–T022 (control and panel), together:
T023 layout + text renderers · T024 media, list, nav, form renderers · T025 canvas components

# Then, in order:
T026 golden files + parity → T027 button override → T028 coverage
```

## Parallel Example: Foundational

```text
T007 TokenSet type · T008 token-css fixtures · T009 TokenCss.cs · T010 tokenCss.ts · T011 BlockDefaults · T012 schema.ts
then T013 → T014 (server helper)   beside   T015 (canvas helper)   then T016 (CSS)
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 and Phase 2.
2. Phase 3 (US1): borders with width, style, colour and inner spacing on every block.
3. **Stop and validate** with quickstart §2, §3 and §6. This alone answers the issue's
   problem, boxed and outlined layouts without an Embed.

### Incremental delivery

- **Order**: US1 → US2 (corners) → US3 (radius tokens in Design) → US4 (transfer proof).
- **Demos**: each story is demoable at its checkpoint.
- **SC-004**: the existing golden files are checked at every checkpoint. Their diff stays the
  radius properties and the stylesheet name.

### One PR

The plan delivers one PR closing #66, as earlier features did. The checkpoints are where to
stop and verify, not separate merges.

---

## Implementation notes (deviations from the task text)

Recorded during `/speckit-implement`, so the tasks above can still be read as written:

- **Contract documents first** (T004, T005, T045): `PageSchemaContractTests` and
  `DesignTokenContractTests` fail the moment a schema changes without its document, so most of
  T045 (page-schema.md §2, §2a, §6, §8, §9 and design-tokens.md §1, §2, §5) was written with
  T004–T006, not in the polish phase.
- **No wrapper rules** (T016): every element on a page is already `border-box` from the reset
  layer, and the two conditional wrappers are plain block `div`s, so `.qb-divider-frame` and
  `.qb-component-ref` need no rule of their own.
- **`tokensOf`** (T021): one helper in `tokenCss.ts` lists a kind's tokens (radii falling back to
  the defaults), used by `TokenControl`, `BorderControl` and `tokenExists`.
- **Border control markup** (T021): `BorderControl` draws its own five labelled selects instead
  of nesting `TokenControl`s, so the panel keeps exactly one `data-prop` per property (which
  `PropertyPanel.spec.ts` checks). The colour label is "Line colour", because "Colour" was already
  the label of a heading's or text's own colour.
- **The Collection list** (T017, T024): its outer element is `ul.qb-list`, not `div.qb-list`. A
  page example cannot show a bordered list or card in parity, because the canvas draws the card
  as a pattern while the published page, with no items, draws the empty state. So
  `borders-every-block.json` leaves the list out, and the bordered list and card are covered by
  `CollectionListRenderTests.A_bordered_card_frames_every_listed_item` (published) and
  `CollectionListBlock.spec.ts` (canvas). `borders-component-and-list.json` became
  `borders-component-ref.json`.
- **Preview widths** (T026): jsdom lays nothing out, so `CanvasMarkupParity.spec.ts` checks that
  the border markup is the same at every width and that no border rule sits in a container query;
  the column fit is measured in the browser by `block-borders.spec.ts`.
- **A test heuristic** (T026): `RendererGoldenTests.A_published_page_ships_no_javascript` found
  forms by `class="qb-form"` exactly; it now also accepts a bordered form.
- **US2 after US1** (T033): the radius option moved into T021 (analysis I2), so T033 tests it.
- **Export and import** (T041–T043): the round trip is a restore of the site's own archive, after
  removing the borders and radii; `ArchiveFromPreviousReleaseTests` also checks the 0.2 archive
  imports without radii and gets the default ones. No product change was needed.
- **Performance** (T047): `page-200-blocks.json` is left alone, as it is the 012 budget page;
  `BorderRenderPerformanceTests` borders every third block in memory instead.
- **Docs** (T046): the operator change is a release-note fragment,
  `docs/releases/unreleased/66-block-borders.md`; `upgrade-notes.md` is generated from fragments
  and gains nothing, because the fragment asks nothing of operators. No visible-changes file: the
  release's upgrade check ignores `<style>` elements and stylesheet hashes, and existing pages
  gain no border classes.
