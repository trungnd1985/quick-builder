# Implementation Plan: Editor Core

**Branch**: `011-editor-core` | **Date**: 2026-09-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-editor-core/spec.md`

**Issue**: #11. This feature settles the constitution's open Technology Constraint ("the
editor canvas library is decided in the editor-core spec") and adds exception **E-5**.

## Summary

Pages and components have drafts (010), but the only way to write one is to send JSON, and
the page schema knows only `root` and `component-ref`. This feature adds:
- **The visual editor** in the admin app: canvas, palette, property panel, layer view,
  undo and redo, responsive preview, autosave, conflict handling and crash recovery. It
  works for page drafts and for shared component drafts.
- **Ten content block types** in the page schema, version 1, as an additive change.
- **Site design tokens** (colours, typography, spacing), in a new `Editor` module.

Nothing visitors see changes (Principle VII). Publishing and rendering are #12.

The approach, detailed in [research.md](./research.md):
- **Canvas** (ED-01, ED-02, ED-09): Vue components in the admin document, with no iframe
  and no page-builder framework.
  - Drag and drop uses native drag events, with a keyboard path for every action.
  - The responsive preview is a width-constrained frame with CSS container queries, so
    the same block stylesheet will serve published pages.
  - This is the canvas decision the constitution asks for, and it is recorded there by an
    amendment in this PR.
- **Rich text** (ED-03): TipTap 3 (MIT) for the Rich Text block only, storing narrowed
  ProseMirror JSON, as Principle IX names. It is the only new runtime dependency, and it is
  lazy-loaded with the editor.
- **Schema** (ED-04 … ED-07): the blocks join `v1.schema.json`.
  - Nesting is encoded as two placement lists, `rootChild` and `contentChild`.
  - Token references are `{ token: id }`; links are typed; rich text is a narrowed
    ProseMirror `doc`.
  - Two new rules: `nesting_not_allowed` and `rich_text_too_long`.
  - The subset validator gains `default` and `minItems`.
  - Every stored draft stays valid.
- **One definition, three consumers** (ED-08): the admin imports the schema file itself.
  - Each block type has one folder: its definition (controls), its canvas component and
    its CSS.
  - Types, limits and defaults come only from the schema.
  - A TypeScript port of the subset validator runs the same golden files as the C# one.
- **Editor state** (ED-10 … ED-13): an immutable document in Pinia, changed by pure
  operations with structural sharing.
  - Undo is reference snapshots, 200 deep, with typing coalescing.
  - Autosave is a small state machine: a 2-second debounce, backoff retries, and read-only
    stops on conflict, forbidden, gone or invalid.
- **Conflicts and recovery** (ED-15, ED-18, ED-19):
  - Conflicts give a read-only view, **Reload latest**, and an in-app clipboard that
    survives the reload. There is never an overwrite.
  - Recovery copies live in IndexedDB, keyed by user. They are offered and never
    auto-applied, and cleared on save, decline and sign-out.
- **Design tokens** (ED-14, ED-16, ED-17):
  - **Storage**: the `Editor` module holds one versioned `jsonb` token set per site.
  - **Defaults** are lazy: no row means the built-in defaults, so there is no data
    migration.
  - **Contract**: the format is a new contract, `docs/contracts/design-tokens.md`.
  - **Usage count**: before a token is deleted, a Pages endpoint counts the blocks that use
    it, with one parameterised jsonpath query.
  - **Fonts** are named system font stacks only, so nothing is downloaded.
- **Authorization** (ED-20): no new Pages capabilities (clarification Q1). Two Editor
  capabilities: `editor.design_tokens.view` (every site role) and
  `editor.design_tokens.manage` (Designer).

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged.

**Primary Dependencies**:
- **Backend**: ASP.NET Core MVC, EF Core 10 + Npgsql (`jsonb`, `xmin`, `SqlQuery` with
  jsonpath) and `System.Text.Json`. Nothing new.
- **Admin**:
  - Vue 3, Pinia, vue-router and vue-i18n, as before.
  - **New**: `@tiptap/core`, `@tiptap/pm`, `@tiptap/vue-3` and the TipTap extensions
    Document, Paragraph, Text, HardBreak, Bold, Italic, Link and the lists. All MIT
    (ED-03).
  - **New dev-only**: `fake-indexeddb` (MIT, for `recovery.spec.ts`).

**Storage**: PostgreSQL 16+. A new `editor` schema with one table, `design_token_sets`
([data-model.md §3](./data-model.md)). The `pages` schema is unchanged; block types are
values inside its existing `jsonb` documents. In the browser: IndexedDB for recovery
copies, and `localStorage` for the clipboard and preview width.

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe, as before. The new suites are listed in research ED-21. The main
ones:
- **Admin**:
  - `ops.property.spec.ts`: random operation sequences with undo and redo, for SC-003;
  - `documentValidator.spec.ts`: the golden files, for parity with C#;
  - `autosave.spec.ts` and `recovery.spec.ts`;
  - `BlockRegistryCoverage.spec.ts` and `CanvasStyleIsolation.spec.ts`;
  - `NoThirdPartyRequests.spec.ts`.
- **Backend**:
  - `DesignTokensApiTests` and `EditorIsolationProbe`;
  - `TokenUsageApiTests` and `TokenUsageSiteFilterTests`;
  - `UpgradeFrom010Tests`;
  - the extended `PageSchemaContractTests` and `PageSchemaSubsetTests`;
  - `DesignTokenContractTests`, `FontStackParityTests` and `DefaultTokensParityTests`.

**Target Platform**:
- **Server**: Linux containers (amd64 in CI), unchanged.
- **Editor**: current evergreen desktop browsers (Chrome, Edge, Firefox, Safari), at least
  1,280 px wide.

**Project Type**: Web application: a modular-monolith backend plus the Vue admin SPA it
serves.

**Performance Goals**:
- A 500-block page opens in under 3 s. Select, property change and drop each paint in
  under 100 ms (SC-005).
- Autosave completes within 3 s of the last change for 95% of saves (SC-004).
- A token usage count on a site at the 2,000-page limit takes under 1 s.
- All of these are on the reference compose setup (2 vCPU, 4 GB).

**Constraints**:
- No change is visible publicly.
- Documents keep 010's limits (1 MiB, 5,000 nodes, depth 32). Rich text is at most 20,000
  characters per block. A token set is at most 64 KiB.
- **No third-party requests from the editor**: no fonts, embeds or scripts (SC-008,
  Principle II).
- No raw HTML anywhere in documents (Principle IX).
- No content or token values in logs (FR-045).
- `/api/v1/editor/...` is new. `/api/v1/pages/...` gains one read endpoint and two
  `DocumentProblem` rule values, both additive.

**Scale/Scope**:
- **Code**: 1 new module (2 projects).
- **Data**: 1 table.
- **Endpoints**: 3 (2 Editor, 1 Pages), and 2 new capabilities.
- **Contracts**: 10 block types and 2 new document rules. 1 new contract (design tokens)
  and 1 extended (page schema), with about 25 new golden files.
- **Admin shell**: 3 new routes (page editor, component editor, design tokens).

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **Module**: `Editor` is one of the constitution's named modules, with the usual two projects, `internal` by default, and `AddEditorModule()`. <br>• **Slices**: `GetDesignTokens` and `SaveDesignTokens` in Editor; `GetTokenUsage` in Pages. Thin controllers and in-house handlers. <br>• **Domain model**: Editor MAY use simple handlers. It still has a small `DesignTokenSet` aggregate, because the token rules are invariants (ED-14). Pages keeps its rich model; the validator gains rules only. <br>• **Cross-module**: none added. The admin calls the Pages usage endpoint directly (ED-16). `Editor.Contracts` exposes `IDesignTokenReader` for #12, and nothing consumes it yet. <br>• **Shared code**: nothing added to `Shared.*`. The subset validator stays in Pages. Editor validates token sets with its own small copy of the subset validator (see Complexity Tracking, "no shared-kernel move"). |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration keys. One automatic, additive migration. **Offline**: fonts are system stacks, embeds are placeholders in the editor, and there are no CDN assets. The admin build embeds TipTap. Health checks pick up the Editor database through `IModuleDatabaseProvider`. |
| III | Upgrades Never Lose Data | **Complies.** <br>• **Upgrade test**: `UpgradeFrom010Tests` upgrades a 010 snapshot. Drafts are unchanged and still valid, and existing sites read the default tokens. <br>• **Schema**: page schema changes are additive within v1 (§6 of the contract). The token contract carries `schemaVersion` and a forward-migration policy. <br>• **Export**: token sets are one `jsonb` row per site, readable by #20. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL only. The only runtime dependency is admin-side (TipTap), justified in ED-03. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Isolation**: `design_token_sets` is `ISiteOwned` and filtered, and `EditorIsolationProbe` joins the isolation suite. <br>• **Raw SQL**: the usage query applies `site_id` explicitly, with `TokenUsageSiteFilterTests`. <br>• **Browser storage**: the clipboard and recovery copies are keyed by site and user, and paste refuses another site. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **One definition**: the blocks are added to `docs/contracts/page-schema/v1.schema.json`, which is the one definition for server validation, the editor's validator, the property panel and the defaults (ED-08). <br>• **Additive**: no version bump is needed. <br>• **New contract**: `docs/contracts/design-tokens.md` plus its schema, versioned. <br>• **Adding a block**: one `$defs` entry, a placement-list entry, examples and one editor folder. `BlockRegistryCoverage.spec.ts` keeps the editor complete. <br>• **Drift**: contract tests on both sides (SC-009). |
| VII | Draft and Published Are Separate Worlds | **Complies.** Only drafts are edited. The token set is stored as the site's draft settings. #12 adds published copies beside both (FR-034). No public endpoint changes. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **N/A for rendering** (#12). Prepared for it: <br>• the block stylesheet and container-query hand-off (contract §9); <br>• token CSS custom-property naming; <br>• `IDesignTokenReader`. <br>No published-page JavaScript is added. The editor's JS is admin-only. |
| IX | Secure by Default | **Complies.** <br>• **No raw HTML**: rich text is narrowed ProseMirror JSON. Paste is filtered through TipTap's schema and `toContractRichText`. <br>• **Safe values**: link schemes are allow-listed (https, http, mailto, tel), and embeds allow only YouTube and Vimeo `https` URLs, validated in the schema on both sides. <br>• **No custom code**: no custom code blocks (out of scope). <br>• **Authorization**: every endpoint declares a capability, enforced server-side, with 404 versus 403 per 002. <br>• **Logs**: no content in logs. <br>• **Canvas output**: block components render text through Vue's escaping only; there is no `v-html` in the editor, and an ESLint rule (`vue/no-v-html` as an error) enforces it. <br>• **Shared browsers**: recovery copies and the clipboard are cleared on sign-out. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests against real PostgreSQL. <br>• **Golden files**: for every block type and for token sets, run by both validators. <br>• **Unit tests**: editor operations and the undo history, including property-based sequences. <br>• **Smoke test**: extended (FR-044), with its browser half under E-5. <br>• **Coverage gate**: `QuickBuilder.Editor` joins the per-package ≥ 80% gate, because tokens feed rendering. `QuickBuilder.Pages` stays in it. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **Dependencies**: one runtime dependency (TipTap), justified in ED-03. No drag-and-drop library, no page-builder framework and no font files. <br>• **Kept simple**: lazy default tokens instead of a seeding mechanism, and no usage table for tokens. <br>• **Delivery**: the feature ships whole in one PR, so no feature flag. |

**Technology Constraints**:
- MVC controllers only.
- EF Core only, with `jsonb` for the token set and `xmin` tokens. The usage count is the
  only raw SQL: through `SqlQuery`, parameterised, in the owning module (Pages), with an
  explicit site filter and a test.
- Admin assets are built into the image. The Dockerfile's admin stage copies
  `docs/contracts/` so the schema import resolves (ED-08).
- **The canvas library constraint is filled in**: "Editor canvas: in-house Vue 3
  components rendered in the admin document; native HTML5 drag and drop with keyboard
  equivalents; CSS container queries for responsive preview; TipTap (ProseMirror) for rich
  text only." This is a constitution amendment in this PR: MINOR, 2.3.1 → 2.4.0, with a
  Sync Impact Report.

**Development Workflow**:
- Issue #11 → `specs/011-editor-core` → branch `011-editor-core` → one PR that closes #11.
- The `spec-ids.yml` check accepts the prefix `011`, because #11 is an open issue and not
  a pull request.
- Clarify has run: five questions, all answered. Analyze follows tasks.
- **Contract changes**: the same PR updates `docs/contracts/` (the page schema document,
  schema and examples, plus the new design-token contract). No stored data migrates,
  because the changes are additive.

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 1 (`InitialEditor`, a new `editor` schema). Additive and automatic.
- **New dependencies**: TipTap in the admin bundle only. No new server package.
- **Upgrade notes**:
  1. Pages and components now have **Edit**, which opens the visual editor.
  2. Each site has **Design** settings for colours, fonts and spacing. Existing sites start
     with the defaults.
  3. Designers edit components and design tokens. Editors build page content.
  4. Unsaved editor changes are kept in the browser until saved, and removed on sign-out.
  5. Nothing on public sites changes until publishing arrives (#12).

**Post-Phase-1 re-check**: the data model and contracts add no violation:
- `editor-api.yaml` is all admin surface, with a capability on every operation.
- Both contracts are versioned and live in `docs/contracts/`.
- The one raw query stays in its owning module with an explicit site filter.
- There is no cross-module table access.

E-5 is new and justified below. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/011-editor-core/
├── plan.md                      # This file
├── spec.md                      # Feature specification (clarified: 5 questions)
├── research.md                  # Phase 0: decisions ED-01 … ED-22
├── data-model.md                # Phase 1: blocks, token set, storage, editor state, error codes
├── quickstart.md                # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── editor-api.yaml          # HTTP: /api/v1/editor/sites/{siteId}/design-tokens, + Pages token usage
│   ├── page-schema-blocks.md    # Change to docs/contracts/page-schema.md + schema fragment
│   └── design-tokens.md         # Planning copy of the new docs/contracts/design-tokens.md
├── checklists/
│   └── requirements.md
└── tasks.md                     # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
.specify/memory/constitution.md           # ~ Technology Constraints: canvas decision; 2.4.0
Dockerfile                                # ~ admin stage copies docs/contracts/
.github/workflows/ci.yml                  # ~ QuickBuilder.Editor joins the coverage gate
QuickBuilder.slnx                         # ~ + the two Editor projects

docs/
~ contracts/page-schema.md                # §2, §2a, §3, §7, §8, §9 (contracts/page-schema-blocks.md)
~ contracts/page-schema/v1.schema.json    # + block $defs, rootChild/contentChild, rich text
+ contracts/page-schema/examples/{valid,invalid}/*.json   # ~25 new golden files
+ contracts/design-tokens.md
+ contracts/design-tokens/v1.schema.json
+ contracts/design-tokens/examples/{valid,invalid}/*.json
~ contracts/README.md                     # lists design tokens
~ contributing.md                         # "Adding a block type" now includes the editor folder
~ operations.md                           # + "Editor": browser storage, sign-out clearing, no outbound requests

src/
├── QuickBuilder.Host/
│   ~ Program.cs                          # + AddEditorModule, + application part
└── Modules/
    ├── Pages/
    │   ├── QuickBuilder.Pages.Contracts/
    │   │   ~ PageSchema.cs               # + MaxRichTextChars
    │   └── QuickBuilder.Pages/
    │       ~ Infrastructure/Schema/SchemaSubsetValidator.cs   # + default, minItems; ECMAScript regex
    │       ~ Infrastructure/Schema/PageDocumentValidator.cs   # + nesting_not_allowed, rich_text_too_long,
    │       │                                                  #   props_invalid for any /props failure
    │       + Features/GetTokenUsage/GetTokenUsage.cs          # jsonpath count, explicit site filter
    └── Editor/
        + QuickBuilder.Editor.Contracts/
        │   + DesignTokens.cs             # contract constants, limits, token id grammar
        │   + DefaultDesignTokens.cs      # the lazy default set
        │   + FontStacks.cs               # id → font-family (ED-17)
        │   + IDesignTokenReader.cs       # for #12
        │   + QuickBuilder.Editor.Contracts.csproj   # EmbeddedResource → docs/contracts/design-tokens/*.schema.json
        + QuickBuilder.Editor/
            + EditorModule.cs  EditorCapabilities.cs  EditorErrorResults.cs  EditorErrors.cs
            + Domain/DesignTokenSet.cs
            + Infrastructure/EditorDbContext.cs  EditorDbContextFactory.cs  DesignTokenReader.cs
            + Infrastructure/TokenSetValidator.cs         # subset validator over the token schema
            + Infrastructure/Migrations/<ts>_InitialEditor.cs
            + Features/GetDesignTokens/  SaveDesignTokens/

src/admin/
├── package.json                          # ~ + @tiptap/*; dev + fake-indexeddb
├── vite.config.ts                        # ~ @contracts alias → ../../docs/contracts; server.fs.allow
├── eslint.config.js                      # ~ vue/no-v-html: error
├── src/
│   ~ styles.css                          # element rules wrapped in :where(:not(.qb-canvas *))
│   ~ App.vue                             # sign-out clears editor recovery + clipboard
│   ~ app/router.ts                       # + editor routes (lazy), + /sites/:siteId/design
│   ~ api/pages.ts                        # + getDraft/saveDraft/getComponentDraft/saveComponentDraft types, tokenUsage
│   + api/editor.ts                       # design tokens client
│   ~ features/pages/PageTreeView.vue  ComponentsView.vue   # + Edit
│   ~ features/sites/SiteDetailView.vue   # + Design link
│   + features/design/DesignTokensView.vue  ColorList.vue  ScaleList.vue  FontSelect.vue
│   + editor/
│   │   + EditorView.vue  EditorTopBar.vue  SaveStatus.vue  ConflictBanner.vue  RecoveryDialog.vue
│   │   + canvas/Canvas.vue  CanvasNode.vue  DropIndicator.vue  ComponentRefBlock.vue  canvas.css
│   │   + palette/Palette.vue
│   │   + panel/PropertyPanel.vue  controls/{Text,Choice,Toggle,Number,ColorToken,SpaceToken,
│   │   │        TextSizeToken,Link,Media,RichText}Control.vue
│   │   + layers/LayerView.vue  LayerRow.vue
│   │   + blocks/registry.ts  blocks.css
│   │   + blocks/{section,container,heading,text,rich-text,image,button,spacer,divider,embed}/
│   │   │        definition.ts  <Type>Block.vue
│   │   + richtext/tiptap.ts  toContractRichText.ts
│   │   + ops/index.ts  insert.ts  move.ts  remove.ts  duplicate.ts  paste.ts  setProp.ts  ids.ts  limits.ts
│   │   + schema/documentValidator.ts  subsetValidator.ts  schema.ts   # imports @contracts/page-schema/v1.schema.json
│   │   + state/editorStore.ts  history.ts  autosave.ts  recovery.ts  clipboard.ts  keyboard.ts
│   │   + tokens/tokenCss.ts  fontStacks.ts  defaultTokens.ts
│   │   + dnd/useDragDrop.ts
│   ~ i18n/en.json
└── tests/
    + editor/ops.spec.ts  ops.property.spec.ts  documentValidator.spec.ts  BlockRegistryCoverage.spec.ts
    │        autosave.spec.ts  recovery.spec.ts  clipboard.spec.ts  EditorView.spec.ts  LayerView.spec.ts
    │        PropertyPanel.spec.ts  CanvasStyleIsolation.spec.ts  NoThirdPartyRequests.spec.ts
    │        toContractRichText.spec.ts
    + design/DesignTokensView.spec.ts  designTokens.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/         # Editor obeys module rules (picked up by the assembly scan)
~ QuickBuilder.UnitTests/Pages/           # PageDocumentValidatorTests (+ block goldens), SchemaSubsetValidatorTests
+ QuickBuilder.UnitTests/Editor/          # DesignTokenSetTests, TokenSetValidatorTests
~ QuickBuilder.IntegrationTests/
│   ~ Pages/PageSchemaContractTests.cs  PageSchemaSubsetTests.cs
│   + Pages/TokenUsageApiTests.cs  TokenUsageSiteFilterTests.cs  UpgradeFrom010Tests.cs
│   + Editor/EditorApi.cs  DesignTokensApiTests.cs  EditorAuthorizationTests.cs
│   │        DesignTokenContractTests.cs  FontStackParityTests.cs  DefaultTokensParityTests.cs
│   + Isolation/Probes/EditorIsolationProbe.cs
│   ~ Isolation/IsolationProbes.cs  Observability/LoggingTests.cs
+ tests/fixtures/editor/page-500.json  deep-page.json
~ QuickBuilder.SmokeTests/                # + editor route + Section/Heading draft; + EditorOpenPerformanceTests,
                                          #   TokenUsagePerformanceTests
specs/002-identity-setup/contracts/authorization.md   # ~ §2 gains the two Editor capabilities
```

**Structure decision**:
- **Admin editor**: all editor code lives under `src/admin/src/editor/`, one folder per
  block type (ED-08). The route is lazy-loaded, so the admin shell's other screens don't
  carry TipTap or the canvas.
- **Backend**: the new `Editor` module is laid out like Media and Sites.
- **Pages**: gains one slice and validator rules.
- **Contracts**: stay in `docs/contracts/`. They are embedded by the .NET contracts
  projects and imported by the admin through the `@contracts` alias, so there is exactly one
  file per schema.

## Complexity Tracking

Status of exceptions carried over from earlier plans, as the Governance section requires.

| Exception | Status after this feature |
|---|---|
| **E-1**, **E-2** | Closed earlier. |
| **E-3** (the smoke test isn't the constitution's full sequence) | Unchanged in substance. It now also serves the editor route and saves a Section and Heading draft. *Publish* and *view* wait for #12 (issue #4). |
| **E-4** (the cache can't invalidate across instances) | Unchanged. No cache is added. |
| **E-5** (new): FR-044 wants the smoke test to "open the editor, add a Section with a Heading, wait for autosave". There is no browser in the release pipeline, and the end-to-end browser harness is #25. | **Why needed**: driving a real browser in the release smoke test is exactly what #25 specifies. Building a one-off harness here would duplicate it. **Covered meanwhile by**: the smoke test (route served, real draft save and read-back); vitest (the editor's autosave path against a fake API); and quickstart §1 and §8, run by hand and recorded in `quickstart-results.md`, including SC-005's browser timings. **Removal**: #25 adds the browser journey to the smoke test; tracked on issue #4. |

**Also considered and rejected**:
- **Moving the subset validator into `Shared.Kernel`** so that Editor and Pages share it.
  The constitution keeps shared code small and free of module concepts, and the validator
  is about 300 lines. Editor keeps a copy scoped to the token schema, and
  `TokenSetValidatorTests` pins it to the same keyword semantics. If a third schema
  appears (collections, #14), moving it to a small shared package is the recorded
  follow-up.

## Risks

- **Style leakage between the admin and the canvas** without an iframe (ED-01).
  - *Mitigation*: the zero-specificity guard on admin element rules, a cascade layer for
    block CSS, and `CanvasStyleIsolation.spec.ts`.
  - *Fallback*: if leakage proves unmanageable, the canvas moves into a same-origin iframe
    with Vue `Teleport`. The drag logic is already isolated in `useDragDrop`, so only
    coordinate translation changes.
- **SC-005 on real browsers** can't be measured in jsdom.
  - *Mitigation*: immutable structural sharing, so unchanged subtrees don't re-render;
    `performance.measure` marks; and the manual measurement in quickstart §8, recorded
    before merge. It is automated in #25.
- **TipTap output drifting from the contract** across TipTap versions (new default
  attributes).
  - *Mitigation*: `toContractRichText` normalises output, and `toContractRichText.spec.ts`
    round-trips TipTap output through the TS validator. TipTap is pinned to an exact minor
    version in `package.json`.
- **Regex dialect differences** between .NET and JavaScript.
  - *Mitigation*: patterns are compiled with `RegexOptions.ECMAScript` server-side, and
    `PageSchemaSubsetTests` refuses constructs outside the portable subset. The same golden
    files run on both sides.
- **`$ref` with a sibling `default`**: 2020-12 allows it, but the subset validator must
  treat `default` as an annotation next to `$ref`, not as an error.
  - *Mitigation*: covered in `SchemaSubsetValidatorTests` and its TS twin.
- **Token usage counts on large sites.**
  - *Mitigation*: the query runs inside PostgreSQL, and `TokenUsagePerformanceTests`
    measures it at 2,000 pages. It runs only when someone deletes a token, never on a hot
    path.
- **Recovery copies on shared machines.**
  - *Mitigation*: keyed by user, and cleared on explicit sign-out. A session that expires
    keeps them for the same user only, because the next user never matches the key.
    `operations.md` documents this.
- **Whole-document autosave on large pages**: up to 1 MiB per save, every few seconds
  while typing.
  - *Mitigation*: the debounce, one save in flight, and 010's size limit. Delta saves
    would belong to #23 (concurrent editing) if ever needed.

## Deferred (recorded, not built)

- **Rendering of blocks** for visitors, the published token set, and the page wrapper that
  matches the canvas container: #12.
- **Collection binding and list blocks**: #16. **Form blocks**: #18.
- **Media usage tracking**, and protection against deleting files in use: #13.
- **Real-time co-editing and presence**: #23. **Draft preview links**: #22.
- **Per-width property overrides**, and hiding blocks at some widths (clarification Q2): a
  later additive schema change.
- **Bundled web fonts and user-uploaded fonts.**
- **An automated browser smoke journey and SC-005 timings**: #25 (E-5).
- **A content-only editing mode for Editors** (clarification Q1): a later feature with its
  own capability.
