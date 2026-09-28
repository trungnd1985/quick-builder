---

description: "Task list for 011-editor-core"
---

# Tasks: Editor Core

**Input**: Design documents from `/specs/011-editor-core/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires integration tests for every endpoint, unit tests for
  domain logic, and golden-file tests for schema validation of every block type.
- The spec measures behaviour through automated checks: SC-002, SC-003, SC-004, SC-006,
  SC-007, SC-008 and SC-009.
- Within each story, test tasks come first and must fail before implementation starts.

**Organization**: tasks are grouped by user story (US1–US7 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US7)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `EDITOR` = `src/Modules/Editor/QuickBuilder.Editor`
- `EDITORC` = `src/Modules/Editor/QuickBuilder.Editor.Contracts`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `HOST` = `src/QuickBuilder.Host`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `ED` = `src/admin/src/editor`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`

**Conventions**:
- **Backend slices** follow 001's one-folder-per-slice rule. Every controller action
  declares exactly one `[RequireCapability(...)]`, checks the site with
  `ISiteDirectory.FindByIdAsync` first (returning `sites.not_found`), and refuses through the
  module's `*ErrorResults.cs`.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task that uses them. `npm run lint` enforces this.
- **Admin state**: every editor change goes through a pure operation in `ED/ops/`
  (research ED-10). Components never mutate the document.

**References**:
- research decisions: `ED-xx`;
- block properties: [data-model.md §1](./data-model.md);
- token rules: [data-model.md §2](./data-model.md);
- endpoints: [contracts/editor-api.yaml](./contracts/editor-api.yaml);
- the schema change: [contracts/page-schema-blocks.md](./contracts/page-schema-blocks.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, the new module's projects, the admin dependencies and
build wiring, and the constitution amendment. Nothing here has behaviour.

- [ ] T001 Commit `specs/011-editor-core/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `011-editor-core`, referencing #11 (plan.md "Development Workflow").
- [ ] T002 Amend the constitution in `.specify/memory/constitution.md`:
  - replace "The editor canvas library is decided in the editor-core spec and then recorded here." with the decision in plan.md "Technology Constraints" (ED-01, ED-02, ED-03, ED-09);
  - bump the version from 2.3.1 to 2.4.0 (MINOR) and set Last Amended to today;
  - prepend a Sync Impact Report naming specs/011-editor-core as the affected spec.
- [ ] T003 Create the two Editor projects and add both to `QuickBuilder.slnx`:
  - `EDITORC/QuickBuilder.Editor.Contracts.csproj`, referencing only `src/Shared/QuickBuilder.Shared.Kernel`, with an `EmbeddedResource` for `../../../../docs/contracts/design-tokens/v1.schema.json` whose `LogicalName` is `QuickBuilder.Editor.Contracts.DesignTokens.v1.schema.json`, in the same form as `PAGESC/QuickBuilder.Pages.Contracts.csproj`;
  - `EDITOR/QuickBuilder.Editor.csproj`, referencing `EDITORC`, `Shared.Kernel`, `Shared.Infrastructure`, `QuickBuilder.Sites.Contracts` and `QuickBuilder.Identity.Contracts`. Copy the property groups and the `InternalsVisibleTo` entries from `PAGES/QuickBuilder.Pages.csproj`.
- [ ] T004 [P] Add `ProjectReference`s to both Editor projects in `ITESTS/QuickBuilder.IntegrationTests.csproj`, `UTESTS/QuickBuilder.UnitTests.csproj` and `ATESTS/QuickBuilder.ArchitectureTests.csproj`. Add `EDITOR` to `HOST/QuickBuilder.Host.csproj`.
- [ ] T005 [P] Add the Editor module and contracts assemblies to the lists in `ATESTS/ModuleBoundaryTests.cs`, next to the Pages entries. Assert that `QuickBuilder.Editor.Contracts` references nothing but `Shared.Kernel` and the BCL.
- [ ] T006 [P] Add the admin dependencies to `src/admin/package.json` and update `src/admin/package-lock.json` with `npm install`:
  - runtime, pinned to one exact minor version (plan Risks): `@tiptap/core`, `@tiptap/pm`, `@tiptap/vue-3`, `@tiptap/extension-document`, `@tiptap/extension-paragraph`, `@tiptap/extension-text`, `@tiptap/extension-hard-break`, `@tiptap/extension-bold`, `@tiptap/extension-italic`, `@tiptap/extension-link` and `@tiptap/extension-list`;
  - dev only: `fake-indexeddb`.
- [ ] T007 [P] Add the `@contracts` alias in `src/admin/vite.config.ts`, resolving to `path.resolve(__dirname, '../../docs/contracts')`, and allow that folder in `server.fs.allow`. Mirror the alias in `compilerOptions.paths` in `src/admin/tsconfig.app.json`, with `resolveJsonModule` on (ED-08).
- [ ] T008 [P] In the admin build stage of `Dockerfile`, add `COPY docs/contracts/ /docs/contracts/` before `RUN npm run build`, so that `/admin/../../docs/contracts` resolves the same way as in a checkout. Add a comment citing ED-08.
- [ ] T009 [P] Make `vue/no-v-html` an error for all of `src/**` in `src/admin/eslint.config.js` (plan, Principle IX row).
- [ ] T010 [P] Register the Editor module in `HOST/Program.cs`: `builder.Services.AddEditorModule(builder.Configuration)` after `AddPagesModule`, and `.AddApplicationPart(typeof(EditorModule).Assembly)`. Create an empty `EDITOR/EditorModule.cs` with `AddEditorModule` so that it compiles.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the extended page schema, golden files and validators on both sides;
- the design token contract and read path;
- the editor's pure operations and store;
- the typed API clients.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Page schema: contract, schema file and golden files

- [ ] T011 Add the ten block types to `CONTRACTS/page-schema/v1.schema.json`, exactly as [contracts/page-schema-blocks.md](./contracts/page-schema-blocks.md) "Schema fragment" gives them, with every `NODE(...)` expanded and no comments:
  - the shared defs: `rootChild`, `contentChild`, `noChildren`, `contentChildren`, `layerLabel`, `tokenId`, `colorRef`, `spaceRef`, `textSizeRef`, `align` and `link`;
  - the rich text defs: `rtDoc`, `rtBlock`, `rtParagraph`, `rtBulletList`, `rtOrderedList`, `rtListItem`, `rtInline`, `rtText`, `rtHardBreak` and `rtMark`;
  - the ten block defs.

  Change `root.children.items` to `rootChild` and remove `childNode`. Keep `schemaVersion` `const: 1`.
- [ ] T012 [P] Update `CONTRACTS/page-schema.md` per [contracts/page-schema-blocks.md](./contracts/page-schema-blocks.md): the §2 table, the new §2a, the §3 rule 9 and validation-order additions, the §7 keywords and pattern portability, the §8 golden-file rows, and the new §9 styling hand-off.
- [ ] T013 [P] Add the valid golden files `valid/every-block.json`, `valid/tokens-missing.json`, `valid/rich-text-lists.json` and `valid/component-page.json` under `CONTRACTS/page-schema/examples/`, per page-schema-blocks.md §8.
- [ ] T014 [P] Add the ten invalid golden files named in page-schema-blocks.md §8 under `CONTRACTS/page-schema/examples/invalid/`, and add their expected rule and pointer to `CONTRACTS/page-schema/examples/invalid/expected.json`. Also add one `props_invalid` file for each block type not yet covered (`section`, `container`, `text`, `image`, `spacer`, `divider`), each with one bad enum or token value, so that every block type has at least one invalid example (SC-009).

### Page schema: C# validator (tests first)

- [ ] T015 [P] Extend `UTESTS/Pages/SchemaSubsetValidatorTests.cs`. Tests must fail first:
  - `default` is accepted and ignored, including next to `$ref`;
  - `minItems` is enforced with a pointer to the array;
  - a pattern is matched with ECMAScript semantics;
  - loading a schema whose pattern uses lookbehind or a named group throws.
- [ ] T016 [P] Extend `UTESTS/Pages/PageDocumentValidatorTests.cs` so that it runs every file in `CONTRACTS/page-schema/examples/{valid,invalid}` and asserts the rule and pointer from `expected.json`. Add cases:
  - `nesting_not_allowed` for a Section in a Section and for a Heading at the root;
  - `unknown_type` for `paragraph`: 010's `unknown_type--paragraph.json` keeps its result, although `paragraph` is a rich-text node name;
  - `props_invalid` for an unknown rich-text mark, at `…/marks/0/type`;
  - `rich_text_too_long` at exactly 20,001 characters, split over several text nodes;
  - every 010 golden file still gives its old result.
- [ ] T017 [P] Extend `ITESTS/Pages/PageSchemaContractTests.cs` and `ITESTS/Pages/PageSchemaSubsetTests.cs`:
  - the §2 table's types and props equal the schema's block defs;
  - each block type has at least one valid and one invalid golden file;
  - every `pattern` in the schema compiles with `RegexOptions.ECMAScript`;
  - the subset list in the §7 text equals `SupportedKeywords`.
- [ ] T018 Add `default` (annotation) and `minItems` to `SupportedKeywords` and their handling in `PAGES/Infrastructure/Schema/SchemaSubsetValidator.cs`. Compile patterns with `RegexOptions.ECMAScript | RegexOptions.CultureInvariant`, and refuse at load any pattern containing `(?<`, `(?<=` or `(?<!`, or `\p{` (ED-06, ED-07). Update its remarks.
- [ ] T019 Extend `PAGES/Infrastructure/Schema/PageDocumentValidator.cs` (ED-05, ED-06; data-model §1.4):
  - In `FromSchema`, any failure whose location contains a `/props` segment becomes `props_invalid`, **checked before** the discriminator branch. A discriminator failure under `/props` is reported at the failing object's `…/type`.
  - A discriminator failure on a node whose `type` is `root` is still `root_misplaced`. One whose `type` is a member of `rootChild` or `contentChild` (read from the loaded schema) becomes `nesting_not_allowed` at `…/type`. Anything else, including rich-text node names such as `paragraph`, is `unknown_type` (ED-05).
  - After the component rules, sum the `text` lengths of every `rtText` under each `rich-text` node's `props.content`. Over `PageSchema.MaxRichTextChars`, fail with `rich_text_too_long` at `…/props/content`.

  Add `public const int MaxRichTextChars = 20_000;` to `PAGESC/PageSchema.cs`.
- [ ] T020 Update the `DocumentProblem.rule` description in `specs/010-page-tree-components/contracts/pages-api.yaml` to include `nesting_not_allowed` and `rich_text_too_long`, noting "added by 011".
- [ ] T021 Create `ITESTS/Pages/UpgradeFrom010Tests.cs` following `UpgradeFrom009Tests.cs`'s snapshot pattern. Take a 010 snapshot with a page draft holding two `component-ref`s and an empty component draft, and upgrade it. Assert that:
  - every stored draft is byte-for-byte unchanged and still validates;
  - `GET /api/v1/editor/sites/{id}/design-tokens` returns the default set with `version: 0` for every existing site (FR-043).

### Page schema: TypeScript validator (ED-08)

- [ ] T022 [P] Create `ADMINTESTS/editor/documentValidator.spec.ts`. It imports every file under `@contracts/page-schema/examples/` with `import.meta.glob` and asserts the same rule and pointer as `expected.json`. It must fail first.
- [ ] T023 [P] Create `ED/schema/schema.ts`. It imports `@contracts/page-schema/v1.schema.json` and exports:
  - the raw schema;
  - `blockTypes` (every `$defs` key referenced from `rootChild` or `contentChild`);
  - `placement`: `{ rootChild: Set<string>, contentChild: Set<string> }`;
  - `propSchema(type)`, `defaultOf(type, prop)` and `requiredProps(type)`;
  - `canDrop(parentType, childType, kind)`. Component documents exclude `component-ref`, per data-model §1.3.
- [ ] T024 Create `ED/schema/subsetValidator.ts`, a TypeScript port of `SchemaSubsetValidator.cs`:
  - the same keywords (including `default` and `minItems`), the same fixed keyword order, and the same first-failure `{ keyword, location, message }`;
  - `oneOf` told apart by a `type` const, reporting `discriminator`;
  - patterns compiled with `new RegExp(p, 'u')`.
- [ ] T025 Create `ED/schema/documentValidator.ts`, porting `PageDocumentValidator.cs`:
  - size (UTF-8 byte length of `JSON.stringify`);
  - `schemaVersion`;
  - the structural walk (depth 32, 5,000 nodes, unique ids);
  - the schema through `subsetValidator.ts`, with the same rule mapping as T019;
  - `component_ref_not_allowed` and `rich_text_too_long`.

  It returns `{ valid: true } | { valid: false, rule, pointer, message }`. Malformed JSON and duplicate properties cannot arise from an object, so they are not ported; note this in a comment. It makes T022 pass.

### Design tokens: contract, module and read path

- [ ] T026 [P] Create `CONTRACTS/design-tokens.md` from [contracts/design-tokens.md](./contracts/design-tokens.md), without the "planning copy" note, and `CONTRACTS/design-tokens/v1.schema.json` using only the page schema's subset:
  - `colors` (minItems 1, maxItems 24);
  - `typography` (`headingFont`, `bodyFont` as an enum of the §3 ids, `baseSizePx` 12–24, `sizes` minItems 1, maxItems 12);
  - `spacing` (minItems 1, maxItems 12);
  - the id pattern `^[a-z0-9-]{1,40}$`, the colour pattern `^#[0-9a-f]{6}([0-9a-f]{2})?$`, `name` 1–40, and the `rem` ranges from data-model §2.

  List it in `CONTRACTS/README.md`.
- [ ] T027 [P] Add golden files `CONTRACTS/design-tokens/examples/valid/default.json` (exactly the default set in data-model §2) and `valid/full.json` (24 colours, 12 sizes, 12 spacing steps). Add `invalid/<code>--<case>.json` for each case in design-tokens.md §5, and `invalid/expected.json` with the error code and pointer for each.
- [ ] T028 [P] Create `EDITORC/DesignTokens.cs` with:
  - `CurrentVersion = 1`;
  - `MaxDocumentBytes = 65_536`;
  - `TokenIdPattern`, `MaxColors = 24`, `MaxSizes = 12` and `MaxSpacing = 12`;
  - `OpenSchema(int version)` over the embedded resource, as `PAGESC/PageSchema.cs` does.
- [ ] T029 [P] Create `EDITORC/FontStacks.cs`, an ordered `IReadOnlyDictionary<string, string>` from id to `font-family`, exactly as design-tokens.md §3 lists them (ED-17).
- [ ] T030 [P] Create `EDITORC/DefaultDesignTokens.cs`. It exposes the default set from data-model §2 as a `JsonElement` built from an embedded literal, plus `public static string Json`.
- [ ] T031 [P] Create `EDITORC/IDesignTokenReader.cs`: `Task<DesignTokenSetDto> ReadAsync(Guid siteId, CancellationToken ct)`, returning the stored or default set with `Version` and `IsDefault`. Put `DesignTokenSetDto` in the same file.
- [ ] T032 Create `EDITOR/Infrastructure/EditorDbContext.cs` with schema `editor` and entity `DesignTokenSetRow` (`site_id` PK, `schema_version`, `document` jsonb, `updated_at_utc`, `updated_by`, `xmin` concurrency token), implementing `ISiteOwned` with the global query filter. Create `EditorDbContextFactory.cs` as `PAGES/Infrastructure/PagesDbContextFactory.cs` does, with its own migrations history table in `editor`. Then generate `EDITOR/Infrastructure/Migrations/<ts>_InitialEditor.cs` with `dotnet ef migrations add InitialEditor`.
- [ ] T033 Create `EDITOR/EditorCapabilities.cs` (ED-20):
  - `DesignTokensView = "editor.design_tokens.view"` for Designer and Editor;
  - `DesignTokensManage = "editor.design_tokens.manage"` for Designer;
  - both site-scoped, in `All`.

  Fill `EDITOR/EditorModule.cs`: DbContext, `IModuleDatabaseProvider`, capability registration as `PagesModule` does, `IDesignTokenReader`, handlers.
- [ ] T034 Create `EDITOR/Infrastructure/DesignTokenReader.cs`, implementing `IDesignTokenReader` with an `AsNoTracking` projection. No row gives `DefaultDesignTokens` with `Version = 0` and `IsDefault = true` (ED-14).
- [ ] T035 Create `EDITOR/EditorErrors.cs` and `EDITOR/EditorErrorResults.cs` with the `editor.*` codes of data-model §6, in the problem shape Pages uses.
- [ ] T036 Create `EDITOR/Features/GetDesignTokens/GetDesignTokens.cs`:
  - `GET /api/v1/editor/sites/{siteId}/design-tokens`, with `[RequireCapability(EditorCapabilities.DesignTokensView)]`;
  - the site check;
  - the response `DesignTokens` from editor-api.yaml, including `permissions.canManage`.
- [ ] T037 [P] Create `ITESTS/Editor/EditorApi.cs` (client helpers) and `ITESTS/Editor/DesignTokensApiTests.cs` with the GET cases:
  - defaults with `version: 0` and `isDefault: true`;
  - an unknown site gives 404 `sites.not_found`;
  - another site's grant only gives 404;
  - an Editor can read.
- [ ] T038 [P] Create `ITESTS/Isolation/Probes/EditorIsolationProbe.cs` following `PagesIsolationProbe.cs`: site A's token set is never returned for site B, on either surface. Register it in `ITESTS/Isolation/IsolationProbes.cs`.
- [ ] T039 [P] Create `ITESTS/Editor/DesignTokenContractTests.cs`, `FontStackParityTests.cs` and `DefaultTokensParityTests.cs`:
  - the document's tables equal the schema's enums and ranges;
  - `FontStacks` equals §3;
  - `DefaultDesignTokens.Json` equals `valid/default.json` and the admin's `ED/tokens/defaultTokens.ts` export, which is read as text and parsed;
  - the font ids equal `ED/tokens/fontStacks.ts`.

### Admin foundations: tokens, API clients, operations and store

- [ ] T040 [P] Create `ED/tokens/defaultTokens.ts`, with the default set as `export const defaultTokens = {...} as const`, byte-equal as JSON to `valid/default.json`, and `ED/tokens/fontStacks.ts`, mirroring §3.
- [ ] T041 [P] Create `ED/tokens/tokenCss.ts`. `tokenCss(set)` returns CSS custom properties, per page-schema-blocks.md §9:
  - `--qb-color-<id>`, `--qb-space-<id>` (`rem`) and `--qb-text-<id>` (`rem`);
  - `--qb-font-heading` and `--qb-font-body`;
  - `--qb-base-size` (px).

  `tokenCss(set)` first emits a variable for every id of `defaultTokens` with its built-in value, then overrides them with the site's set, so every default id always resolves (data-model §2, "Fallbacks always resolve"). `tokenValue(ref, kind, set, fallbackRef)` returns `var(--qb-…-<id>)`, or the fallback's variable when the id is missing from the set (clarification Q4). Unit-test it in `ADMINTESTS/editor/tokenCss.spec.ts`, including a set without `color-text` and `text-3xl`, where the variables are still defined.
- [ ] T042 [P] Extend `ADMIN/api/pages.ts` with:
  - `Draft` and `ContentDocument` types;
  - `getPageDraft`, `savePageDraft(siteId, pageId, { document, version })`, `getComponentDraft` and `saveComponentDraft`;
  - `tokenUsage(siteId, tokenId)`.

  Create `ADMIN/api/editor.ts` with `DesignTokens` types, `getDesignTokens` and `saveDesignTokens`. Both follow `api/media.ts`'s style.
- [ ] T043 [P] Create `ADMINTESTS/editor/ops.spec.ts`, which must fail first. For each op in T045:
  - it returns a new root and leaves the old root untouched;
  - unchanged subtrees keep identity;
  - it refuses with a reason on nesting, depth, node-count and estimated-size limits (ED-10);
  - `duplicate` and `paste` re-id every node, and ids stay unique and match the contract grammar.
- [ ] T044 [P] Create:
  - `ED/ops/ids.ts`: `newNodeId()`, 22 base64url characters from `crypto.randomUUID()`;
  - `ED/schema/limits.ts`: `MAX_DOCUMENT_BYTES = 1_048_576`, `MAX_NODES = 5000`, `MAX_NODE_DEPTH = 32` and `MAX_RICH_TEXT_CHARS = 20_000`, mirroring `PAGESC/PageSchema.cs`;
  - `ED/ops/limits.ts`: node count, depth and UTF-8 byte size of a subtree, checked against `ED/schema/limits.ts`.

  Add `ITESTS/Pages/PageSchemaLimitsParityTests.cs`, which reads `src/admin/src/editor/schema/limits.ts` as text and asserts that each constant equals its `PageSchema` counterpart.
- [ ] T045 Create the pure operations in `ED/ops/`, each returning `{ ok: true, root, selection } | { ok: false, reason }`, and re-export them from `ED/ops/index.ts` (ED-10):
  - `insert.ts`: `insert(root, parentId, index, type, kind, initialProps)`;
  - `move.ts`: `move(root, nodeId, parentId, index, kind)`;
  - `remove.ts`: `remove(root, nodeId)`;
  - `duplicate.ts`: `duplicate(root, nodeId)`;
  - `paste.ts`: `paste(root, parentId, index, nodes, kind)`;
  - `setProp.ts`: `setProp(root, nodeId, prop, value)` (value `undefined` removes the prop), `setLabel(root, nodeId, label)`.

  Each op uses `canDrop` from T023 and the limits from T044. `setProp` validates the new value against `propSchema(type)` using `subsetValidator.ts`. This makes T043 pass.
- [ ] T046 Create `ED/state/editorStore.ts`, a Pinia store holding one `EditingSession` (data-model §5):
  - `document` in a `shallowRef`, plus `baseVersion`, `kind`, `siteId`, `documentId`, `selection`, `tokens` and `readOnlyReason`;
  - `load(siteId, kind, id)`, which fetches the draft and `getDesignTokens` in parallel, then runs `documentValidator` on the draft. On failure, including an unsupported `schemaVersion`, it sets `readOnlyReason = 'invalid'` with the rule and pointer, and autosave never starts (spec Edge Cases, "a draft that no longer passes validation");
  - `apply(opResult)`, which commits a successful op and returns the reason for a refused one;
  - `select(id)`.

  Leave hooks for history (US3) and autosave (US1, US3).

**Checkpoint**:
- `dotnet test` and `npm test` are green, apart from the story tests not yet written.
- Both validators pass every golden file.
- `GET design-tokens` works.
- The ops are complete.

---

## Phase 3: User Story 1 - Build a page by dragging blocks onto a canvas (Priority: P1) 🎯 MVP

**Goal**: open a page in the editor, drag the ten block types from the palette onto the
canvas, arrange and nest them, type text in place, duplicate and delete them, with every
action also possible by keyboard, and have the draft saved without pressing anything.

**Independent Test**:
1. Open an empty page and add one block of each type.
2. Nest a Heading in a Container in a Section, reorder two blocks, move a block to another
   Section, and delete one.
3. Reload. The page is identical, and the stored draft validates (quickstart §1).

### Tests for User Story 1 ⚠️

- [ ] T047 [P] [US1] Create `ADMINTESTS/editor/BlockRegistryCoverage.spec.ts`, which must fail first:
  - every `blockTypes` entry has a registry entry, and every registry entry is a schema block type;
  - every schema prop of each type has a control in its definition, and no definition names a prop the schema lacks;
  - every required prop has an `initialProps` value that validates.
- [ ] T048 [P] [US1] Create `ADMINTESTS/editor/EditorView.spec.ts`, with Testing Library, a fake API and axe:
  - opening a page shows the canvas, palette, panel placeholder and layer region;
  - a stored draft with `schemaVersion: 2` opens read-only with the reason and never saves;
  - dropping a palette Section onto the empty canvas inserts it and selects it;
  - dropping a Section onto a Section is refused and changes nothing;
  - by keyboard only: insert each block type from the palette, move up and down, duplicate, delete (SC-006);
  - typing in a Heading on the canvas changes the document;
  - after 2 s idle, `savePageDraft` is called with a document that `documentValidator` accepts, and the status reads saved;
  - axe finds no violations.
- [ ] T049 [P] [US1] Create `ADMINTESTS/editor/CanvasStyleIsolation.spec.ts`. Render an `h1` and a `button` inside `.qb-canvas` and outside it, and assert that the admin rules from `styles.css` apply only outside (ED-01).

### Implementation for User Story 1

- [ ] T050 [US1] Wrap every element-type selector in `ADMIN/styles.css` (`h1`, `h2`, `label`, `select`, `input[...]`, `button` and their variants, `*`) in `:where(:not(.qb-canvas *))` or `:where(:not(.qb-canvas, .qb-canvas *))` as appropriate, leaving class selectors untouched (ED-01).
- [ ] T051 [US1] Create `ED/blocks/registry.ts`. The `BlockDefinition` interface has:
  - `type`, `paletteLabelKey`, `icon`;
  - `groups: { labelKey, props: { name, control } [] }[]`;
  - `initialProps(t)`;
  - `component`.

  `registry` maps each type to its definition. `ED/blocks/blocks.css` opens `@layer qb-blocks` with the canvas-local reset (`.qb-canvas` resets margins, fonts to `var(--qb-font-body)` and base size) and the shared rules.
- [ ] T052 [P] [US1] Create `ED/blocks/section/definition.ts` and `SectionBlock.vue`: background, `paddingY` and width via `tokenValue`; a `width: contained` max width of 1,200 px; renders its children through `CanvasNode`. Add its rules to `blocks.css`.
- [ ] T053 [P] [US1] Create `ED/blocks/container/definition.ts` and `ContainerBlock.vue`: a flex layout by `direction`, with gap, padding, align and background. When `stackOnMobile` is set, add `@container page (max-width: 480px) { flex-direction: column }` in `blocks.css` (ED-09).
- [ ] T054 [P] [US1] Create `ED/blocks/heading/definition.ts` and `HeadingBlock.vue`:
  - renders `h{level}` with the heading font and the level-to-size map from data-model §2;
  - edits in place with `contenteditable="plaintext-only"`;
  - strips line breaks;
  - commits through `setProp` on input;
  - shows a translated placeholder when empty.
- [ ] T055 [P] [US1] Create `ED/blocks/text/definition.ts` and `TextBlock.vue`: plain text with line breaks kept (`white-space: pre-wrap`), in-place editing as T054, and size, colour and align from tokens.
- [ ] T056 [P] [US1] Create `ED/blocks/rich-text/definition.ts` and `RichTextBlock.vue`. The canvas render of the stored `doc` uses Vue render functions mapping `paragraph`, `bulletList`, `orderedList`, `listItem`, `text` with marks and `hardBreak` to elements, with **no `v-html`**. In-place editing is added in US2 (T071).
- [ ] T057 [P] [US1] Create `ED/blocks/image/definition.ts` and `ImageBlock.vue`:
  - renders `mediaApi.previewUrl(siteId, mediaId)` as `<img>`, with `alt`, or `alt=""` when `decorative`, and the `width` mode;
  - with no media, or on a load error, shows the translated "image missing / choose an image" placeholder (spec Edge Cases).
- [ ] T058 [P] [US1] Create `ED/blocks/button/definition.ts` (with `initialProps` label `t('editor.blocks.button.initialLabel')`) and `ButtonBlock.vue`: styles `primary`, `secondary` and `outline` from the brand colour token, and in-place label editing as T054. It never navigates in the canvas.
- [ ] T059 [P] [US1] Create the definitions and components for `ED/blocks/spacer/` (`SpacerBlock.vue`: a height from the token, with a hatched editing aid), `ED/blocks/divider/` (`DividerBlock.vue`: `<hr>` with colour, thickness and spacing) and `ED/blocks/embed/` (`EmbedBlock.vue`: a placeholder card with the provider name derived from the URL, the URL as text and the aspect ratio, and "choose a video" when there is no URL; **never an iframe or any network request**, ED-06, SC-008). Add their rules to `blocks.css`.
- [ ] T060 [US1] Create `ED/canvas/CanvasNode.vue` and `ED/canvas/Canvas.vue`:
  - `CanvasNode.vue` renders `registry[node.type].component` with `node` as a prop, a selection outline, a `data-node-id` attribute and a click to select;
  - `Canvas.vue` is the `.qb-canvas` frame with `container-type: inline-size; container-name: page` and the `tokenCss(tokens)` style, iterating `root.children`, with an empty state inviting a first block (spec Edge Cases).

  Unchanged node objects must not re-render (ED-10). Verify this with a render counter in `EditorView.spec.ts`.
- [ ] T061 [US1] Create `ED/dnd/useDragDrop.ts` and `ED/canvas/DropIndicator.vue` (ED-02):
  - `dragstart` from the palette carries `{ source: 'palette', type }`, and from the canvas or layers carries `{ source: 'node', id }`;
  - `dragover` computes `{ parentId, index }` from the pointer position (before or after a sibling, or inside an empty or hovered Section or Container) and checks `canDrop`;
  - `drop` applies `insert` or `move` through the store;
  - it sets `dropEffect = 'none'` and shows the "not allowed" indicator when refused.
- [ ] T062 [US1] Create `ED/palette/Palette.vue`: a listbox of the ten block types (translated labels and icons from the registry), draggable. Arrow keys move; `Enter` inserts after the selection, or as the last child when the selection takes children and `canDrop` allows (ED-11).
- [ ] T063 [US1] Create `ED/state/keyboard.ts` with editor shortcuts bound on the editor root (not globally): arrows for selection, `Enter` and `Escape` for text editing, `Alt+↑/↓/→/←` moves, `Ctrl/⌘+D`, `Delete`, `Ctrl/⌘+C/V` and `Ctrl/⌘+S`, per ED-11. Announce moves and inserts in a polite live region in `EditorView.vue`. Undo and redo are added in US3.
- [ ] T064 [US1] Create `ED/state/clipboard.ts` (ED-18):
  - `copy(nodes)` writes `{ userId, siteId, nodes }` to `localStorage['qb.editor.clipboard']`, and the blocks' plain text to `navigator.clipboard.writeText`, ignoring failure;
  - `read(siteId, userId)` returns nodes only for a matching site and user;
  - `clear()`.

  Paste uses `paste` from T045. Cover it in `ADMINTESTS/editor/clipboard.spec.ts`: another site is refused, and nodes are re-ided.
- [ ] T065 [US1] Create `ED/state/autosave.ts` with the base state machine from data-model §5 (`saved`, `dirty`, `saving`):
  - a 2 s debounce after the last change, and `saveNow()`;
  - one request in flight at a time;
  - a pre-check with `documentValidator` (a failure goes to `invalid` and sends nothing);
  - on 200, set `baseVersion` from the response and go to `saved`, or to `dirty` if the document changed meanwhile.

  Wire it into `editorStore.apply`. Retry and stop states are added in US3.
- [ ] T065a [US1] Create `ED/editorErrors.ts`, the single map from refusals to i18n keys (FR-042):
  - every `editor.*` code and `pages.invalid_token_id` in data-model §6;
  - every `DocumentProblem.rule` (010's rules plus `nesting_not_allowed` and `rich_text_too_long`);
  - 010's `pages.*` codes that the editor can meet;
  - the op refusal reasons from `ED/ops/`.

  Add the keys to `ADMIN/i18n/en.json`. Create `ADMINTESTS/editor/editorErrors.spec.ts`, asserting that every code, rule and reason has a key present in `en.json`. T072, T082, T084 and T106 show messages only through this map.
- [ ] T066 [US1] Create `ED/EditorView.vue`, `ED/EditorTopBar.vue` and `ED/SaveStatus.vue`:
  - a layout with the palette on the left, the canvas in the centre, and the property panel and layer view regions on the right (placeholders filled by US2 and US4);
  - a top bar with the page title, a back link to Pages, a save status and Save now;
  - loading, error and not-found states (spec US1 #7).
- [ ] T067 [US1] Add the lazy route `{ path: '/sites/:siteId/pages/:pageId/edit', name: 'page-editor', component: () => import('../editor/EditorView.vue'), meta: { bare: true } }` in `ADMIN/app/router.ts`, with `props` for `kind: 'page'`. Add an **Edit** action per page, shown when `permissions.canEditPageDrafts`, in `ADMIN/features/pages/PageTreeView.vue` (ED-22).
- [ ] T068 [US1] Add every `editor.*` key used in T050–T067 to `ADMIN/i18n/en.json`. Run `npm run lint`, `npm run type-check` and `npm test`; T047–T049 now pass.

**Checkpoint**: US1 is fully functional, and quickstart §1 passes. This is the MVP.

---

## Phase 4: User Story 2 - Edit a block's properties in the property panel (Priority: P1)

**Goal**: a property panel generated from each block's schema, with controls for text,
choices, tokens, links and media, rich text editing with TipTap, and in-place and panel
editing kept in step, refusing invalid values with a reason.

**Independent Test**: for each block type, change every property and check that it shows on
the canvas and survives a reload. Check that a `javascript:` link, a non-YouTube or Vimeo
embed and a Heading over 300 characters are refused in the panel (quickstart §2).

### Tests for User Story 2 ⚠️

- [ ] T069 [P] [US2] Create `ADMINTESTS/editor/PropertyPanel.spec.ts`, which must fail first:
  - for each block type, the panel shows exactly the schema props with their defaults;
  - a change updates the document and the canvas;
  - a `javascript:alert(1)` link, a `https://example.com/video` embed and a 301-character heading are refused with a translated reason, and the last valid value is kept (FR-021);
  - a colour control lists the site's tokens and stores `{ token }`;
  - axe finds no violations.
- [ ] T070 [P] [US2] Create `ADMINTESTS/editor/toContractRichText.spec.ts`:
  - TipTap JSON with null attrs, an unknown mark, underline, a heading node and an image node is normalised to the contract, validated by `documentValidator`;
  - links map to `{ to: { type: 'page' | 'url' } }`;
  - a pasted HTML fragment with a script, a style and an image keeps only the allowed formats.

### Implementation for User Story 2

- [ ] T071 [US2] Create `ED/richtext/tiptap.ts`, which makes a TipTap `Editor` with only Document, Paragraph, Text, HardBreak, Bold, Italic, the lists (bullet, ordered, list item) and a Link mark extended with a `to` attribute. There is no history extension (ED-03, ED-12). Create `ED/richtext/toContractRichText.ts`, which normalises TipTap JSON to data-model §1.1 `richText`. Wire in-place editing into `ED/blocks/rich-text/RichTextBlock.vue`: TipTap mounts on `Enter` or double click, commits `setProp(content)` on each document-changing transaction, and unmounts on blur or `Escape`. Add a small floating toolbar (bold, italic, link, bulleted, numbered).
- [ ] T072 [P] [US2] Create the basic controls in `ED/panel/controls/`: `TextControl.vue` (single or multi-line, `maxLength` from the schema, a live character count), `ChoiceControl.vue` (enum), `ToggleControl.vue` (boolean) and `NumberControl.vue` (integer with `minimum` and `maximum`, for example Heading level). Each validates with `subsetValidator` against `propSchema` before emitting, and shows the reason inline with `aria-invalid` and `aria-describedby`.
- [ ] T073 [P] [US2] Create `ED/panel/controls/ColorTokenControl.vue`, `SpaceTokenControl.vue` and `TextSizeTokenControl.vue`:
  - pick from the store's `tokens` and store `{ token: id }`, or clear back to the default;
  - swatches or sizes rendered from `tokenCss`;
  - a reference to a missing id is shown as "deleted colour (using default)" (FR-031, clarification Q4).
- [ ] T074 [P] [US2] Create `ED/panel/controls/LinkControl.vue`:
  - a choice of Page or Address, with no link as a third option;
  - Page uses a searchable list of the site's pages from `pagesApi.tree`, and stores `{ type: 'page', pageId }`;
  - Address validates the scheme against the schema pattern (FR-015);
  - a `pageId` not in the tree shows "broken link" (spec Edge Cases).
- [ ] T075 [P] [US2] Create `ED/panel/controls/MediaControl.vue`:
  - a picker dialog over `mediaApi.list` (images only) with previews via `previewUrl`, and upload through `mediaApi.upload` when the member may upload (FR-017);
  - stores `{ mediaId }`;
  - shows "missing" for an unavailable file.

  Pair it with alt text and decorative fields in the Image definition's groups.
- [ ] T076 [US2] Create `ED/panel/PropertyPanel.vue`, which generates the panel for the selected node from `registry[type].groups` and `propSchema(type)` (FR-018):
  - one control per prop, with the value or the schema default shown as the placeholder;
  - emits `setProp`;
  - an empty state when nothing is selected;
  - `layerLabel` is shown for every block in a common "Layer" group.

  Mount it in `ED/EditorView.vue`.
- [ ] T077 [US2] Make in-place text editing and the panel stay in step (FR-019): both read the same node from the store, and in-place components do not overwrite the caret when the value changes from the panel (compare before assigning `textContent`). Add the Embed URL control's provider hint listing YouTube and Vimeo. Add all `editor.panel.*` keys to `ADMIN/i18n/en.json`. T069 and T070 now pass.

**Checkpoint**: US1 and US2 work together, and quickstart §2 passes.

---

## Phase 5: User Story 3 - Work safely with undo, redo and autosave (Priority: P1)

**Goal**: undo and redo of every change with typing coalescing; autosave that retries,
stops safely on conflict, forbidden, gone or invalid; a read-only conflict view with
copy, reload and paste; crash recovery from the browser; and leave warnings.

**Independent Test**: quickstart §3 steps 1–7 (20 changes undone and redone; offline and
back online; a two-tab conflict with copy, reload and paste; recovery after a closed tab;
the leave warning; sign-out clearing).

### Tests for User Story 3 ⚠️

- [ ] T078 [P] [US3] Create `ADMINTESTS/editor/ops.property.spec.ts`, which must fail first. Seeded random sequences of 50–200 ops (insert, move, remove, duplicate, paste, setProp, typing) through the store with history must show:
  - after each step the document validates;
  - undoing all equals the start, and redoing all equals the end, deep-equal (SC-003);
  - ids stay unique;
  - a new change after undo clears redo.
- [ ] T079 [P] [US3] Create `ADMINTESTS/editor/autosave.spec.ts` with fake timers and a fake API:
  - the debounce;
  - one request in flight;
  - a network error or 503 leads to `retrying` with a backoff of 2, 4, 8, 16 then 30 s, and "Retry now";
  - 409 `pages.out_of_date` leads to `conflict` and read-only;
  - 403 leads to `forbidden`, and 404 to `gone`;
  - 400 `invalid_document` leads to `invalid` with the node at `pointer` selected;
  - a failed pre-check sends nothing;
  - the `beforeunload` handler and router guard are active in every state except `saved`.
- [ ] T080 [P] [US3] Create `ADMINTESTS/editor/recovery.spec.ts` with `fake-indexeddb/auto`:
  - records are written in `dirty` and removed after a covering save;
  - reopening with an equal `baseVersion` offers Restore;
  - reopening after the server version changed offers read-only copy only;
  - declining removes the record;
  - `clearForUser(userId)` removes only that user's records;
  - no offer is made when the draft is gone or forbidden (ED-19).

### Implementation for User Story 3

- [ ] T081 [US3] Create `ED/state/history.ts` (ED-12): undo and redo stacks of `{ document, selection }`, capped at 200; `record(before, coalesceKey?)`; typing coalescing on the same `nodeId:prop` key until 1 s idle, a word boundary, or another action; and `undo()` and `redo()`. Wire it into `editorStore.apply` and `load` (which clears it). Bind `Ctrl/⌘+Z`, `Ctrl/⌘+Shift+Z` and `Ctrl/⌘+Y` in `ED/state/keyboard.ts`, and add undo and redo buttons to `ED/EditorTopBar.vue`.
- [ ] T082 [US3] Extend `ED/state/autosave.ts` (ED-13):
  - the `retrying` state with backoff and `retryNow()`;
  - the stopped states `conflict`, `forbidden`, `gone` and `invalid`, which set `editorStore.readOnlyReason` and stop timers;
  - pointer-to-node selection for `invalid`;
  - a `window` `beforeunload` handler and an `onBeforeRouteLeave` guard in `ED/EditorView.vue` whenever the state is not `saved` (FR-030).
- [ ] T083 [US3] Make read-only mode real: every op path in `editorStore.apply`, the palette, drag and drop, the panel controls, in-place editing and the keyboard refuse changes when `readOnlyReason` is set, while selection, the layer view and `Ctrl/⌘+C` still work. Show it in `ED/SaveStatus.vue`.
- [ ] T084 [US3] Create `ED/ConflictBanner.vue` (ED-15). For `conflict` it explains, offers **Reload latest** with a confirmation that uncopied changes will be lost, and offers no overwrite. **Reload latest** calls `editorStore.load`, clearing history and keeping the clipboard. It shows matching messages for `forbidden`, `gone` and `invalid`.
- [ ] T085 [US3] Create `ED/state/recovery.ts` (ED-19):
  - IndexedDB `qb-editor-recovery`, keyed `userId/siteId/kind/documentId`;
  - `write` debounced to 500 ms in `dirty` or `retrying`, and on `pagehide`;
  - `remove` after a covering save;
  - `find` on open;
  - `clearForUser(userId)`;
  - every call wrapped so that an unavailable IndexedDB silently disables recovery.

  Create `ED/RecoveryDialog.vue`, which offers Restore or Discard when the versions are equal, and Open read-only or Discard otherwise. Wire it into `editorStore.load`. Remove the record without offering it when the load returns 404 or 403.
- [ ] T086 [US3] Clear editor browser state on sign-out in `ADMIN/App.vue`'s `signOut()`: `await recovery.clearForUser(user.id)` and `clipboard.clear()`, before the redirect, with failures ignored (FR-030; spec Edge Cases, "shared computer").
- [ ] T087 [US3] Add the `editor.save.*`, `editor.conflict.*` and `editor.recovery.*` keys to `ADMIN/i18n/en.json`. T078–T080 now pass.

**Checkpoint**: all three P1 stories work, and quickstart §1–§3 pass.

---

## Phase 6: User Story 4 - Navigate the page in the layer view (Priority: P2)

**Goal**: an outline of every block, in step with the canvas selection, with pointer and
keyboard re-ordering and re-parenting, collapse, and layer labels.

**Independent Test**: quickstart §4 with `tests/fixtures/editor/deep-page.json`.

### Tests for User Story 4 ⚠️

- [ ] T088 [P] [US4] Create `ADMINTESTS/editor/LayerView.spec.ts`, which must fail first:
  - it renders an ARIA `tree` with `treeitem`s nested as the document, labelled with `layerLabel` or the translated type;
  - selecting in the tree selects on the canvas, and the reverse;
  - `Alt+↑/↓/→/←` moves blocks under the nesting rules, and the live region announces each move;
  - collapsing hides children in the tree only;
  - axe finds no violations.

### Implementation for User Story 4

- [ ] T089 [US4] Create `ED/layers/LayerView.vue` and `ED/layers/LayerRow.vue`:
  - a `role="tree"` with roving `tabindex`, `aria-expanded` for Sections and Containers, and `aria-selected`;
  - the arrow-key tree navigation from the WAI-ARIA tree pattern, plus the ED-11 move keys;
  - rows draggable through `useDragDrop` with the same `canDrop`;
  - collapse state held locally.

  Mount it in `ED/EditorView.vue`.
- [ ] T090 [US4] Keep the canvas and the layer view scrolled to the selection: on a selection change, `scrollIntoView({ block: 'nearest' })` the `[data-node-id]` element in the view that did not originate the change (FR-022).
- [ ] T091 [US4] Allow renaming a layer by double click or `F2` on a row, using `setLabel`. An empty value removes `layerLabel`. Add the `editor.layers.*` keys to `ADMIN/i18n/en.json`. T088 now passes.
- [ ] T092 [P] [US4] Create `tests/fixtures/editor/deep-page.json`: a valid page document with at least 30 blocks nested 4 levels deep, using every block type, and validated by the golden-file runner in `PageDocumentValidatorTests`.

**Checkpoint**: quickstart §4 passes.

---

## Phase 7: User Story 5 - Preview at desktop, tablet and mobile widths (Priority: P2)

**Goal**: switch the canvas between 1,280, 768 and 375 px. Layouts adapt through container
queries, editing works at every width, and the choice is remembered.

**Independent Test**: quickstart §5.

### Tests for User Story 5 ⚠️

- [ ] T093 [P] [US5] Extend `ADMINTESTS/editor/EditorView.spec.ts`, which must fail first:
  - the width switcher sets the frame's width to 1280, 768 or 375 px;
  - the choice is stored in `localStorage['qb.editor.previewWidth.<userId>']` and restored for that user only;
  - editing still works after switching;
  - a missing or broken `localStorage` falls back to desktop.

### Implementation for User Story 5

- [ ] T094 [US5] Add the width switcher (a segmented control, keyboard operable, `aria-pressed`) to `ED/EditorTopBar.vue`. In `ED/canvas/Canvas.vue`, set the frame's width from the store, and `transform: scale()` the frame to fit when the editor area is narrower, with a `ResizeObserver` (ED-09). Persist the choice through `try`/`catch` `localStorage` under `qb.editor.previewWidth.<userId>` (FR-036, per member).
- [ ] T095 [US5] Add tablet rules (`@container page (max-width: 767px)`) and mobile rules (`(max-width: 480px)`) to `ED/blocks/blocks.css`: Section padding scales down, Headings step down one size, images are max-width 100%, and horizontal Containers wrap at tablet width and stack at mobile width when `stackOnMobile` is set. Add the `editor.preview.*` keys to `ADMIN/i18n/en.json`. T093 now passes.

**Checkpoint**: quickstart §5 passes.

---

## Phase 8: User Story 6 - A designer sets the site's design tokens (Priority: P2)

**Goal**: Designers edit the site's colours, fonts, text sizes and spacing. Blocks follow by
token id. Deleting a used token warns with a count and then falls back to defaults. Editors
see the tokens read-only.

**Independent Test**: quickstart §6.

### Tests for User Story 6 ⚠️

- [ ] T096 [P] [US6] Create `UTESTS/Editor/DesignTokenSetTests.cs` and `UTESTS/Editor/TokenSetValidatorTests.cs`, which must fail first. They cover each data-model §2 rule and its order:
  - too large;
  - unsupported version;
  - an empty list gives `token_required`;
  - schema failures with pointer;
  - an id duplicated across lists;
  - a name duplicated ignoring case;
  - the first save only at version 0;
  - every design-tokens golden file.
- [ ] T097 [P] [US6] Extend `ITESTS/Editor/DesignTokensApiTests.cs` with the PUT cases:
  - a Designer saves and reads back with a new version;
  - a stale version gives 409 `editor.out_of_date`;
  - each 400 code;
  - 413 over 64 KiB;
  - two concurrent first saves: exactly one succeeds.
- [ ] T098 [P] [US6] Create `ITESTS/Editor/EditorAuthorizationTests.cs`:
  - an Editor gets GET 200 and PUT 403 `identity.forbidden`;
  - a Designer gets both;
  - a user with no role on the site gets 404 for both;
  - Administrator gets both everywhere.

  Update `ITESTS/Isolation/Probes/EditorIsolationProbe.cs` to cover the PUT.
- [ ] T099 [P] [US6] Create `ITESTS/Pages/TokenUsageApiTests.cs` and `ITESTS/Pages/TokenUsageSiteFilterTests.cs`:
  - the counts over page and component drafts, with a block using a token in two props counted once;
  - a text value equal to the token id is not counted;
  - an invalid token id gives 400 `pages.invalid_token_id`;
  - site B's drafts are never counted for site A (the explicit filter);
  - the capability is `pages.content.view`.

  Update `ITESTS/Isolation/Probes/PagesIsolationProbe.cs` for the new endpoint.
- [ ] T100 [P] [US6] Create `ADMINTESTS/design/DesignTokensView.spec.ts` and `ADMINTESTS/design/designTokens.spec.ts`:
  - the view lists colours, fonts, sizes and spacing;
  - add, rename and change value, then save;
  - deleting a colour first calls `tokenUsage` and shows the count in the confirmation;
  - deleting the last colour is not offered;
  - a read-only view without `canManage`;
  - axe finds no violations;
  - `designTokens.spec.ts` runs the design-tokens golden files through `subsetValidator.ts` with `v1.schema.json`.

### Implementation for User Story 6

- [ ] T101 [US6] Create `EDITOR/Infrastructure/TokenSetValidator.cs`: the subset validator scoped to the token schema, as plan "Complexity Tracking" describes, a copy of `PAGES/Infrastructure/Schema/SchemaSubsetValidator.cs`'s keyword semantics with a header comment naming the original and `TokenSetValidatorTests`.
- [ ] T102 [US6] Create `EDITOR/Domain/DesignTokenSet.cs` with `Replace(document, expectedVersion, userId, now)`, validating in data-model §2's order through `TokenSetValidator`, and returning `Result` with the `editor.*` codes. Map it in `EDITOR/Infrastructure/EditorDbContext.cs` (`xmin` as the concurrency token).
- [ ] T103 [US6] Create `EDITOR/Features/SaveDesignTokens/SaveDesignTokens.cs`:
  - `PUT /api/v1/editor/sites/{siteId}/design-tokens`, with `[RequireCapability(EditorCapabilities.DesignTokensManage)]`;
  - a request size limit of 64 KiB plus the envelope, before parsing;
  - `version: 0` inserts, and a unique violation or concurrency conflict gives 409 `editor.out_of_date`;
  - otherwise it loads, calls `Replace` and saves, with a concurrency exception giving 409.

  Log only the site id and version (FR-045).
- [ ] T104 [US6] Create `PAGES/Features/GetTokenUsage/GetTokenUsage.cs` (ED-16):
  - `GET /api/v1/pages/sites/{siteId}/token-usages/{tokenId}`, with `[RequireCapability(PagesCapabilities.ContentView)]`;
  - validate `tokenId` against `^[a-z0-9-]{1,40}$`, else `pages.invalid_token_id`;
  - run one `SqlQuery<TokenUsageRow>` over `pages.page_drafts UNION ALL pages.component_drafts`, `WHERE site_id = @siteId` explicitly, counting nodes with `jsonb_path_query(document, 'strict $.** ? (exists(@.props.** ? (@.token == $id)))', jsonb_build_object('id', @tokenId))`, and summing to `{ blockCount, documentCount }`;
  - a comment citing the Technology Constraint on raw SQL and `TokenUsageSiteFilterTests`.

  Add `pages.invalid_token_id` to `PAGES/PagesErrors.cs` and `PAGES/PagesErrorResults.cs`.
- [ ] T105 [US6] Add the two Editor capabilities to §2 of `specs/002-identity-setup/contracts/authorization.md`, and add `GetTokenUsage` to the Pages list there, as that document requires.
- [ ] T106 [US6] Create `ADMIN/features/design/DesignTokensView.vue`, `ColorList.vue`, `ScaleList.vue` (used for both text sizes and spacing) and `FontSelect.vue`:
  - edit colours (a name, plus a colour input with a hex field), fonts (the stack names, previewed in their own family), the base size, text sizes and spacing steps;
  - new entries get an id of `<kind>-` plus 8 random `[a-z0-9]` characters, and ids are never shown for editing;
  - Save with the version, and 409 shows a reload prompt;
  - delete asks `pagesApi.tokenUsage` and confirms with the block count (clarification Q4); deleting the last entry of a kind is disabled;
  - read-only when `permissions.canManage` is false.
- [ ] T107 [US6] Add the route `/sites/:siteId/design` (lazy) in `ADMIN/app/router.ts` and a **Design** link in `ADMIN/features/sites/SiteDetailView.vue`. Make the editor refresh its tokens when the editor route is entered (`editorStore.load` already fetches them). Add the `design.*` keys to `ADMIN/i18n/en.json`. T096–T100 now pass.

**Checkpoint**: quickstart §6 passes.

---

## Phase 9: User Story 7 - A designer edits a shared component in the editor (Priority: P3)

**Goal**: components open in the same editor; pages show component references with the
component's content, read-only in place; the palette inserts components into pages.

**Independent Test**: quickstart §7.

### Tests for User Story 7 ⚠️

- [ ] T108 [P] [US7] Extend `ADMINTESTS/editor/EditorView.spec.ts`, which must fail first:
  - the component editor loads with `kind: 'component'`, and its palette has no Components group;
  - saving calls `saveComponentDraft`;
  - on a page, a `component-ref` renders the component draft's content with a *Shared* badge, is selected as one unit, has no in-place editing, and its panel offers **Open component**;
  - the palette's Components group inserts a reference at the root only (`canDrop`);
  - an Editor sees no Edit on the component list.

### Implementation for User Story 7

- [ ] T109 [US7] Create `ED/canvas/ComponentRefBlock.vue`. It fetches `getComponentDraft` for `props.componentId` (cached per editing session in `editorStore.componentCache`) and renders its root's children through `CanvasNode` with editing disabled (`inert` on the subtree for pointer and focus). It adds a *Shared* badge and the component's name from `pagesApi.listComponents`. Register `component-ref` in `ED/blocks/registry.ts` with no editable groups, and a panel action **Open component** routing to the component editor.
- [ ] T110 [US7] Add a Components group to `ED/palette/Palette.vue` for `kind === 'page'`, listing the site's components from `pagesApi.listComponents`, which inserts `{ type: 'component-ref', props: { componentId } }` through `insert`.
- [ ] T111 [US7] Add the lazy route `/sites/:siteId/components/:componentId/edit` (`kind: 'component'`) in `ADMIN/app/router.ts`, and an **Edit** action shown when `permissions.canManageComponents` in `ADMIN/features/pages/ComponentsView.vue`. Make `ED/state/autosave.ts` call `saveComponentDraft` for components. Add the `editor.components.*` keys to `ADMIN/i18n/en.json`. T108 now passes.

**Checkpoint**: all seven stories work, and quickstart §1–§7 pass.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: no outbound requests, logging, smoke and performance, docs, CI, and the manual
validation run.

- [ ] T112 [P] Create `ADMINTESTS/editor/NoThirdPartyRequests.spec.ts`. Render every block type (including an Embed with a YouTube URL and every font stack) in `Canvas.vue`, and assert that no element in the canvas has a `src`, `href`, `srcset` or CSS `url()` outside the installation's origin, and that no `iframe`, `script` or `link[rel=stylesheet]` exists (SC-008, FR-035).
- [ ] T113 [P] Extend `ITESTS/Observability/LoggingTests.cs`: saving a draft with block text and saving tokens writes no document content, token names or values to the logs (FR-045).
- [ ] T114 [P] Create `tests/fixtures/editor/page-500.json`, a valid 500-block page document using every block type, checked by the golden runner.
- [ ] T115 [P] Extend `STESTS/ReferenceDeploymentSmokeTests.cs` after step 9c (FR-044, E-5):
  - GET `/admin/sites/{id}/pages/{pageId}/edit` returns 200 with the app shell;
  - PUT the page draft with a Section holding a Heading, read it back, and assert the two node types;
  - GET `/api/v1/editor/sites/{id}/design-tokens` returns the defaults.

  Update the class remarks to name E-5.
- [ ] T116 [P] Create `STESTS/EditorOpenPerformanceTests.cs`: with `page-500.json` saved, the draft, tokens and component list are fetched in parallel in under 1 s at p95 over 20 runs. Create `STESTS/TokenUsagePerformanceTests.cs`: a site with 2,000 pages, each with a 20-block draft, and one token-usage count takes under 1 s. Follow `PageTreePerformanceTests.cs`'s pattern.
- [ ] T117 [P] Add `QuickBuilder.Editor` to the per-package coverage loop in `.github/workflows/ci.yml` (Principle X, plan).
- [ ] T118 [P] Update the documentation:
  - `docs/contributing.md` "Adding a block type": the `$defs` entry, the placement list, golden files, `ED/blocks/<type>/definition.ts` and component, and `blocks.css`;
  - `docs/operations.md` "Editor": browser storage (recovery copies, clipboard), clearing on sign-out, no outbound requests, embeds as placeholders, fonts as system stacks;
  - the upgrade notes from plan.md in the release notes draft.
- [ ] T119 Run [quickstart.md](./quickstart.md) §1–§10 by hand on the reference compose setup, in Chrome or Edge and in Firefox. Record the results, the SC-001 time, SC-005's `qb:select`, `qb:set-prop` and `qb:drop` timings (add these `performance.measure` marks, development builds only, in `ED/state/editorStore.ts` and `ED/dnd/useDragDrop.ts`) and the browser versions in `specs/011-editor-core/quickstart-results.md`.
- [ ] T120 Run `dotnet format --verify-no-changes`, `dotnet test`, and `npm run lint && npm run type-check && npm test && npm run build` in `src/admin`. Fix any failures. Confirm the admin main chunk does not include TipTap: check the Vite build output and note the chunk sizes in `quickstart-results.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 (constitution) and T003 (projects) can start at
  once.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
  - The schema work (T011–T025), the token work (T026–T039) and the admin foundations
    (T040–T046) can proceed in parallel.
  - T021 needs T036 (GET tokens).
- **US1 (Phase 3)**: after Foundational. **The MVP.**
- **US2 (Phase 4)**: after US1. It needs the blocks, canvas and store to attach the panel
  to.
- **US3 (Phase 5)**: after US1. It extends autosave (T065) and the keyboard (T063), and is
  independent of US2.
- **US4 (Phase 6)** and **US5 (Phase 7)**: after US1, independent of US2, US3 and each
  other.
- **US6 (Phase 8)**:
  - the backend tasks (T096–T099, T101–T105) need only Foundational;
  - the admin screen (T106–T107) needs Foundational;
  - the deleted-token marking in the panel is done in US2 (T073).
- **US7 (Phase 9)**: after US1.
- **Polish (Phase 10)**: after the stories it covers. T119 is last but one, and T120 is
  last.

### Within Each User Story

- Tests are written first and fail.
- Block components and ops come before the canvas, the canvas before drag and drop, the
  store before autosave.
- The i18n keys and the green-test task close each story.

### Parallel Opportunities

- **Phase 1**: T004–T010.
- **Phase 2**: T012–T017 together (docs, golden files, C# tests); T022 and T023; T026–T031;
  T037–T041; T043 and T044.
- **US1**: the ten block folders T052–T059 are independent files once T051 exists.
- **US2**: the controls T072–T075.
- **US6**: the backend tests T096–T099 together, and the backend (T101–T105) alongside the
  admin screen (T106).
- **Across stories**, with more than one developer after US1: US3, US4, US5, US6 and US7.

---

## Parallel Example: User Story 1

```bash
# After T051 (registry), build the block folders side by side:
Task: "T052 [US1] Section block in src/admin/src/editor/blocks/section/"
Task: "T053 [US1] Container block in src/admin/src/editor/blocks/container/"
Task: "T054 [US1] Heading block in src/admin/src/editor/blocks/heading/"
Task: "T055 [US1] Text block in src/admin/src/editor/blocks/text/"
Task: "T057 [US1] Image block in src/admin/src/editor/blocks/image/"
Task: "T059 [US1] Spacer, Divider, Embed blocks"

# Tests for the story, together, before implementation:
Task: "T047 BlockRegistryCoverage.spec.ts"
Task: "T048 EditorView.spec.ts"
Task: "T049 CanvasStyleIsolation.spec.ts"
```

## Parallel Example: User Story 6

```bash
Task: "T096 DesignTokenSetTests + TokenSetValidatorTests"
Task: "T097 DesignTokensApiTests (PUT)"
Task: "T098 EditorAuthorizationTests"
Task: "T099 TokenUsageApiTests + TokenUsageSiteFilterTests"
Task: "T100 DesignTokensView.spec.ts + designTokens.spec.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational. Both validators pass all golden files.
3. Phase 3: US1.
4. **Stop and validate**: quickstart §1. A page can be built from blocks and is saved
   valid.

### Incremental Delivery

1. Setup and Foundational: the schema, the tokens read path and the ops.
2. US1: drag, arrange and save. **Demo.**
3. US2 (properties) and US3 (safety net). All P1 stories are done; this is a usable editor.
4. US4 (layers), US5 (preview) and US6 (tokens), in any order.
5. US7 (components).
6. Polish, the manual quickstart run, and the PR closing #11.

The feature ships in one PR, as with 010, so there is no feature flag (plan, Principle XI).

---

## Notes

- **[P]** tasks touch different files and have no dependency on an incomplete task.
- **[Story]** labels map to spec.md's user stories US1–US7.
- **Tests**: write them first and see them fail.
- **Commits**: commit after each task or logical group, with messages referencing #11.
- **Contract changes** (T011–T014, T020, T026–T027) land in the same PR as their tests
  (constitution, Development Workflow).
- **Avoid**:
  - `v-html` anywhere (lint enforces it);
  - mutating the document outside `ED/ops/`;
  - repeating schema limits or defaults in TypeScript (read them from `ED/schema/schema.ts`).
