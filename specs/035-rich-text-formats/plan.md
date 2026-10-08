# Implementation Plan: Richer Rich Text Formats

**Branch**: `035-rich-text-formats` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/035-rich-text-formats/spec.md`

**Issue**: #35. Open exceptions from earlier plans: E-5 and E-6, both unchanged. No new
exception.

## Summary

Rich text, in the page editor's Rich Text block and in collection items' rich text fields,
gains these formats:
- headings 2–4 and quotes;
- underline, strikethrough and inline code;
- images from the media library;
- simple tables;
- paragraph alignment;
- undo and redo buttons.

It also gains paste conversion and per-field limits on the allowed formats. Rich text stays
structured JSON.

The approach, detailed in [research.md](./research.md):
- **Contract**: an **additive change to page schema version 1** (RT-01). There are new
  `$defs` for headings, quotes, images and tables, and three new marks. A new `rtListBlock`
  keeps list items exactly as they are, so nesting rules fall out of the schema. Three code
  rules cover image and table counts and table shape (RT-02). Both validators already
  support every keyword used (RT-03). **No migration**, and no stored document changes
  (SC-001).
- **Rendering**:
  - **Server renderer**: one allow-listed case per new node (RT-04). Images reuse 013's
    image pipeline. Alignment uses the existing `qb-align--*` classes.
  - **Canvas**: mirrors the renderer (RT-05).
  - **Shared styles**: in `blocks.css`, which both use.
- **Editor**: official TipTap extensions, pinned to the TipTap release already used and
  configured to the contract. Images are an own `ContractImage` node; `UndoRedo` is used in
  the item form only (RT-06, RT-14). `toContractRichText` stays the single gate into
  documents.
- **Toolbar**: rewritten as one shared component (RT-07, clarification Q3): icon buttons in
  groups with a roving tab stop, a block style menu, contextual table and image groups, and
  history buttons supplied by the host. The page editor keeps its one history.
- **Paste** (RT-08, clarification Q2):
  - `preparePastedHtml` removes images and splits merged cells, then the schema parses the
    rest;
  - pasted image files upload through 013;
  - a captured sample set checks the results.
- **Allowed formats per field** (RT-09, clarification Q1): an optional `formats` setting on
  `richText` fields.
  - **Contract**: additive under collection-schema §9.
  - **Item checks**: `format_not_allowed` sets attention and blocks publishing.
  - **Schema changes**: narrowing is a reported change.
- **Alt text at publish** (RT-10): `pages.image_alt_missing` for pages and components, and
  `image_alt_missing` for items. The Image block is unchanged.
- **Media usage and invalidation** come from the existing extractors (RT-11), with no new
  table.
- **Outdated admin tabs** (RT-13): an `X-QB-Rich-Text: 2` request header. Draft writes are
  refused with `admin_outdated` only when an old client would lose formats.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**: ASP.NET Core MVC, EF Core 10 and Npgsql; Vue 3, Pinia, vue-i18n and
TipTap 3.31. **New** (admin only, RT-18): `@tiptap/extension-heading`, `-blockquote`,
`-underline`, `-strike`, `-code`, `-text-align`, `-table` and `@tiptap/extensions`, all MIT,
pinned to the same TipTap release.

**Storage**: PostgreSQL 16+. **No migration** (data-model §1). The new content lives in the
existing `jsonb` documents, and its derived rows in existing tables.

**Testing**: xUnit, Testcontainers PostgreSQL, and vitest with Testing Library and axe. The
suites are in research RT-19. The main ones:
- the golden examples on both sides (SC-005, SC-007);
- `RichTextRendererTests` with unchanged old golden files (SC-001);
- `UpgradeFrom015Tests` (SC-001);
- `paste.spec.ts` with the captured sample set (SC-004);
- `ItemRichTextFormatsTests`, `PublishRichTextImageTests`, `RichTextMediaUsageTests` and
  `AdminOutdatedTests`;
- the toolbar specs with axe (SC-003).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend, the Vue admin SPA and the
public renderer.

**Performance Goals** (RT-15):
- **Admin normalisation**: a document at the limits normalises within 30 ms in jsdom, and
  typing shows within 100 ms in a browser (SC-006).
- **Server render**: a limit document renders within 5 ms.
- **Page render**: the existing targets hold (012).

**Constraints**:
- **Additive only**: SC-001 asks for byte-identical rendering of old content.
- **No markup stored**: nothing outside the allow-list reaches a document or a page
  (Principle IX).
- **Offline**: no request to another host. Pasted image addresses are never fetched.
- **Logs**: no rich text, alt text or file names in logs (FR-021).

**Scale/Scope**:
- **Backend**: 0 new projects, 0 migrations, 0 new endpoints, 0 new tables.
- **Errors and problems**: 2 new error codes and 6 new problem codes.
- **Request header**: 1.
- **Contracts**: 2 contract documents changed additively.
- **Admin**:
  - **Rewritten**: the toolbar.
  - **New**: about 5 components or modules (`ContractImage` node view, table group, image
    group, `paste.ts`, `richTextRules.ts`) and about 20 icons.
  - **Changed**: 4 (the Rich Text block, the item field, the field editor and the canvas
    view).

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module, slice or endpoint.** The rules live in `Pages.Contracts` (`RichTextRules`, beside `PageSchema`), which Cms already references for `rtDoc` (RT-02). <br>• **No new module edge.** The checks hook into existing write paths: Pages `DraftWrites` and `PublishingWrites`, Cms `ItemWrites`, `ItemValidation` and `ItemIndexer`, and Rendering's `RichTextRenderer` and `RenderReferences`. |
| II | Easy to Install, Operate and Upgrade | **Complies.** No configuration key and no migration. Upgrade notes cover the new formats, reloading open admin tabs, and the new alt-text check at publish (RT-20). |
| III | Upgrades Never Lose Data | **Complies.** <br>• **Existing content**: every stored document stays valid and renders the same (`UpgradeFrom015Tests`, SC-001). <br>• **Old tabs**: open admin tabs can't silently drop the new formats (RT-13). <br>• **Export (#20)**: carries the documents unchanged. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** No backend dependency. The admin's new packages are build-time only (RT-18). |
| V | Multi-Site Within One Installation | **Complies.** An image naming another site's file is treated as missing: never rendered, never counted as usage (spec edge case, RT-11). The media lookups stay site-scoped. Pasted uploads go to the current site's library. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **Additive changes only**: both are allowed inside version 1 by the contracts' own rules (page-schema §6, collection-schema §9; RT-01, RT-09). <br>• **Same PR**: the contract documents, the schema file, golden examples on both sides, and renderer support for every new type (`BlockRendererCoverageTests` is unchanged in kind). |
| VII | Draft and Published Are Separate Worlds | **Complies.** Formats change only drafts. The alt-text and allowed-format checks refuse publishing; they never alter published data. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** <br>• **Server-side**: the new formats render on the server through the allow-list (RT-04). <br>• **Invalidation**: rich text images join the media dependency edges, so replacing or deleting a file re-renders the pages that show it (RT-11). |
| IX | Secure by Default | **Complies.** <br>• **Structured JSON**: rich text stays JSON, and `toContractRichText` is the only way in. <br>• **Paste**: parsed in an inert document; the schema discards the rest; no addresses fetched (RT-08). <br>• **Allow-list**: rendering uses classes, never inline styles. <br>• **Tests**: hostile examples (RT-16). <br>• **Authorization**: uploads use 013's capability checks. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Contract**: golden examples on both sides. <br>• **Renderer**: golden files. <br>• **Endpoints**: integration tests for every changed write path. <br>• **Logic**: unit tests for the rules. <br>• **Coverage**: the publish path keeps its 80 % coverage gate. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **Dependencies**: 8 admin packages, justified in RT-18. <br>• **Not built**: code blocks, captions, image sizes, justified text and an Image block alt check (backlog). <br>• **Delivery**: one PR; each user story can be shown on its own. |

**Technology Constraints**: MVC controllers only (none added). EF Core only, with no raw SQL.
TipTap is the only rich text editor (011). No Node process at run time.

**Development Workflow**:
- Issue #35 → `specs/035-rich-text-formats` → branch `035-rich-text-formats` → one PR that
  closes #35.
- Clarify has run: 3 questions, all answered. Analyze follows tasks.
- **Contract and registry changes in the same PR** ([contracts/api-changes.md §5](./contracts/api-changes.md)):
  - `docs/contracts/page-schema.md` and `page-schema/v1.schema.json`, plus examples;
  - `docs/contracts/collection-schema.md` and `collection-schema/` examples;
  - `editor-api.yaml` (011), `publishing-api.yaml` (012), and `items-api.yaml` and
    `data-model.md` (015);
  - "extended by 035" notes in 011's `page-schema-blocks.md` and 012's
    `page-schema-rendering.md`.

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: none.
- **New dependencies**: admin build only (RT-18); nothing at run time.
- **Upgrade notes**:
  1. Rich text gains headings, quotes, underline, strikethrough, code, images, tables and
     alignment, in pages and in items. Existing content is unchanged.
  2. **Reload open admin tabs after upgrading.** An old tab can't save over content that
     uses the new formats (`admin_outdated`).
  3. Publishing a page or item with a rich text image needs alternative text, or the image
     marked decorative.
  4. Designers can limit a rich text field's formats. Narrowing them marks items that use
     them as needing attention.

**Post-Phase-1 re-check**:
- **Contract deltas** ([contracts/rich-text.md](./contracts/rich-text.md)): every new
  definition uses supported keywords only, and the old `rtBlock` set is preserved as
  `rtListBlock`.
- **Errors and problems** ([contracts/api-changes.md](./contracts/api-changes.md)): the
  errors and problem properties are additive.
- **Storage** ([data-model.md](./data-model.md)): no migration, and every derived row reuses
  an existing table.

**Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/035-rich-text-formats/
├── plan.md                 # This file
├── spec.md                 # Feature specification (clarified: 3 questions)
├── research.md             # Phase 0: decisions RT-01 … RT-20
├── data-model.md           # Phase 1: storage (none new), entities, rules, problem codes
├── quickstart.md           # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── rich-text.md        # page schema and collection schema deltas, rules, allow-list, examples
│   └── api-changes.md      # header, errors, report problem properties, registries
├── checklists/
│   └── requirements.md
└── tasks.md                # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
docs/
~ contracts/page-schema.md                     # §2, §3 rules 10–12, rendering allow-list, §8 examples
~ contracts/page-schema/v1.schema.json         # additive $defs (rich-text.md §2)
+ contracts/page-schema/examples/{valid,invalid}/rich-text-*.json …   (rich-text.md §7)
~ contracts/collection-schema.md               # richText `formats`, problems, §7 rules
~ contracts/collection-schema/v1.schema.json   # `formats` setting
+ contracts/collection-schema/examples/items/rich-text-*.json   ~ schema.json  expected.json
~ operations.md  backlog.md

src/
├── Modules/Pages/
│   ├── QuickBuilder.Pages.Contracts/
│   │   ~ PageSchema.cs                        # MaxRichTextImages, MaxRichTextTables
│   │   + RichTextRules.cs                     # rules 10–12, ImagesWithoutAlt, UsesExtendedFormats,
│   │                                          #   ClientHeader ("X-QB-Rich-Text") and CurrentClientLevel (2)
│   └── QuickBuilder.Pages/
│       ~ Infrastructure/Schema/PageDocumentValidator.cs   # rules 10–12 after rule 9
│       ~ Infrastructure/PublishingWrites.cs   # image alt check (RT-10)
│       ~ Infrastructure/DraftWrites.cs        # admin_outdated (RT-13)
│       ~ Features/SavePageDraft/  SaveComponentDraft/   # controllers read the header into the command
│       ~ Domain/DependencyExtractor.cs        # rich text images → media (RT-11)
│       ~ PagesErrors.cs  PagesErrorResults.cs # image_alt_missing, admin_outdated
├── Modules/Rendering/QuickBuilder.Rendering/
│   ~ Rendering/RichTextRenderer.cs            # new nodes and marks (RT-04)
│   ~ Rendering/Blocks/MediaRenderers.cs       # SourceSet/Sizes shared with rich text images
│   ~ Domain/RenderReferences.cs               # rich text images → render inputs
├── Modules/Cms/QuickBuilder.Cms/
│   ~ Domain/Field.cs  CollectionSchemaRules.cs   # `formats` setting
│   ~ Domain/SchemaDiff.cs  FieldChange.cs      # `formats` narrowing reported, widening free
│   ~ Infrastructure/Schema/CollectionSchemaValidator.cs
│   ~ Domain/FieldConversion.Values.cs         # richText → text (RT-12)
│   ~ Infrastructure/ItemValueValidator.cs     # rules 10–12 for values
│   ~ Infrastructure/ItemValidation.cs         # format_not_allowed, image_alt_missing (+ path)
│   ~ Infrastructure/ItemIndexer.cs            # rich text images → item_media
│   ~ Infrastructure/ItemWrites.cs             # admin_outdated
│   ~ Features/Items/SaveItemDraft/  DuplicateItem/   # controllers read the header
│   ~ Domain/ItemReport.cs  CmsErrors.cs  CmsErrorResults.cs

src/admin/
├── package.json                               # + 8 TipTap packages (RT-18)
├── src/
│   ~ api/request.ts                           # X-QB-Rich-Text: 2
│   ~ api/items.ts  api/pages.ts               # admin_outdated, new problem fields
│   ~ components/IconButton.vue                # ~20 icons, shortcut in tooltip
│   ~ editor/richtext/tiptap.ts                # extensions configured to the contract (RT-06)
│   ~ editor/richtext/toContractRichText.ts    # new shapes, restrictToAllowed
│   ~ editor/richtext/RichTextToolbar.vue      # rewritten (RT-07)
│   + editor/richtext/ContractImage.ts  ContractImageView.vue  ImageGroup.vue  TableGroup.vue
│   + editor/richtext/paste.ts                 # preparePastedHtml, file uploads (RT-08)
│   ~ editor/richtext/RichTextView.ts          # canvas drawing (RT-05)
│   + editor/schema/richTextRules.ts           # rules 10–12 twin
│   ~ editor/schema/documentValidator.ts  limits.ts
│   ~ editor/blocks/rich-text/RichTextBlock.vue  # toolbar with the page history
│   ~ editor/blocks/blocks.css                 # headings, quote, table, image, code (canvas and sites)
│   ~ features/cms/items/inputs/RichTextInput.vue   # UndoRedo, allowed formats, uploads
│   ~ features/cms/items/itemValueRules.ts     # format_not_allowed, image_alt_missing
│   ~ features/cms/FieldEditor.vue  collectionSchema.ts  collectionSchemaRules.ts  schemaChanges.ts  # `formats`
│   ~ editor/ConflictBanner.vue  features/cms/items/ItemFormView.vue   # admin_outdated with a reload button
│   ~ i18n/en.json  styles.css
└── tests/
    + editor/paste/*.html  *.expected.json  paste.spec.ts
    + editor/RichTextToolbar.spec.ts  contractImage.spec.ts  tables.spec.ts  richTextRules.spec.ts
    ~ editor/toContractRichText.spec.ts  documentValidator.spec.ts  CanvasMarkupParity.spec.ts
    ~ cms/items/RichTextInput.spec.ts (or ItemField.spec.ts)  cms/FieldEditor.spec.ts  cms/schemaChanges.spec.ts

tests/
~ QuickBuilder.UnitTests/Pages/                 # RichTextRulesTests
~ QuickBuilder.UnitTests/Rendering/             # RichTextRendererTests, RichTextRenderPerformanceTests
~ QuickBuilder.UnitTests/Cms/                   # FieldConversionTests, CollectionSchemaGoldenTests, SchemaDiffTests
~ QuickBuilder.IntegrationTests/Pages/PageSchemaContractTests.cs
+ QuickBuilder.IntegrationTests/Pages/PublishRichTextImageTests.cs  AdminOutdatedTests.cs
+ QuickBuilder.IntegrationTests/Media/RichTextMediaUsageTests.cs
+ QuickBuilder.IntegrationTests/Cms/Items/ItemRichTextFormatsTests.cs
+ QuickBuilder.IntegrationTests/Rendering/UpgradeFrom015Tests.cs
~ QuickBuilder.IntegrationTests/Observability/LoggingTests.cs
```

**Structure decision**:
- **Contract and rules**: in `Pages.Contracts`, the existing home of the page schema and its
  validators. Pages and Cms (items) use them.
- **Rendering**: in Rendering.
- **Admin**: the rich text pieces stay under `editor/richtext/`, shared by the canvas and the
  item form, as 015 already does.
- **The `X-QB-Rich-Text` header**: its name and level are constants in `RichTextRules`
  (`Pages.Contracts`). Each controller reads the header into its command, the way Media's
  upload controllers already read their headers. The write path decides.

## Complexity Tracking

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. Typing latency and the screen reader check are by hand in quickstart §5, §7 and §12, until #25. |
| **E-6** (schema changes rewrite published item data) | Unchanged. `formats` narrowing keeps values (reported), so it adds no rewrite. |

**New exceptions**: none.

## Risks

- **Paste fidelity varies by source**: clipboard HTML differs between Word versions,
  Google Docs and browsers.
  - *Mitigation*: a captured sample set with expected results (RT-08). New sources are
    added as samples when they show up. The normaliser guarantees safety even where
    fidelity is imperfect.
- **Large tables slow typing**: 4,000 cells at the limit.
  - *Mitigation*: the benchmark in RT-15, with the frame-debounced `onChange` fallback.
- **Old admin tabs**: covered by RT-13. The remaining risk is a tab that never reloads, and
  it gets a clear message rather than losing content.
- **Toolbar width in narrow panels**: wrapping by group, and table and image groups shown
  only in context. axe and keyboard specs cover every state.
- **The renderer changing old output by accident**: the existing golden files must stay
  byte-identical, and `UpgradeFrom015Tests` compares rendered pages.
- **Editors surprised by the publish check**: pasted screenshots have no alt text. The image
  shows an "Add alternative text" marker at once, and the refusal names each image.

## Deferred (recorded, not built)

- An alternative-text check on the Image block (its behaviour is unchanged).
- Code blocks, image captions, size and float choices, table captions and column widths,
  and justified alignment.
- Fetching pasted image addresses (rejected, clarification Q2).
- Right-to-left support beyond storing `start`/`end`.
