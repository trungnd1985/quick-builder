# Implementation Plan: Border Styles for All Blocks

**Branch**: `066-block-borders` | **Date**: 2026-11-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/066-block-borders/spec.md`

**Issue**: #66. Open exceptions from earlier plans are unchanged. No new exception.

## Summary

Every content block gains one optional `border`:
- width: none, thin, medium or thick;
- line style: solid, dashed or dotted;
- a colour token;
- a corner radius token;
- an inner spacing token, except on Section and Container.

Designers get a new "Corner radius" token list. A block without a border looks exactly as
before.

The approach, detailed in [research.md](./research.md):
- **Contract**: additive within version 1 of both contracts (BB-01 to BB-03).
  - The page schema gains `$defs` `border`, `borderBox`, `radiusRef`, `borderWidth` and
    `borderStyle`, and a `border` prop on 15 types.
  - The token set gains an optional `radii` list with five default entries.
  - No migration, and no stored document changes.
- **Validation**: both validators already interpret the schema subset these use (BB-10). Only
  the token set's cross-list id and name checks learn the fourth list, on both sides.
- **Rendering**: one shared helper per side, `BorderStyle` (C#) and `useBorder` (canvas)
  (BB-04).
  - It adds the classes `qb-border`, `qb-border--{width}`, `qb-border--{style}`,
    `qb-border--radius` and `qb-border--pad`, plus token-only custom properties in the
    existing per-node style rule.
  - The CSS lives in `blocks.css`, after the block rules, so a border replaces a Button's
    own frame (clarification Q2).
  - The Image frames its `img`. Divider and Component reference get a wrapper only when
    bordered (BB-05).
  - Clipping applies to boxes only, never to Navigation or Form (BB-06).
  - `border-box` sizing means a border never widens a block (BB-07).
- **Editor**:
  - The property panel adds a "Border" group itself to every type whose schema has `border`,
    as it does for "Layer", so no block definition changes. One `BorderControl` edits the
    object as one undoable change (BB-08).
  - `TokenControl` and the Design page learn the radius kind (BB-09).
- **Tests**:
  - contract examples, token CSS fixtures and four rendering golden files;
  - unit tests for both helpers and the control;
  - one e2e spec with keyboard-only use and axe (BB-12, BB-13).

  The existing golden files change only by the five default `--qb-radius-*` properties and
  the stylesheet's hashed file name, which is the evidence that no page renders differently
  (SC-004).

## Technical Context

**Language/Version**: C# on .NET 10 (server modules); TypeScript strict with Vue 3.5 (admin
and editor).

**Primary Dependencies**: existing only. ASP.NET Core MVC, EF Core with Npgsql, Vue 3, Pinia,
Vite, vue-i18n. Tests use xUnit, Testcontainers, Vitest and Playwright (025). **No new
dependency.**

**Storage**: PostgreSQL `jsonb`. Page and component documents are in `pages`, token sets in
`editor`. No schema change and no migration.

**Testing**:
- `tests/QuickBuilder.UnitTests`: rendering golden files, `TokenCssParityTests`,
  `BorderStyleTests`;
- `tests/QuickBuilder.IntegrationTests`: `PageSchemaContractTests`,
  `DesignTokenContractTests`, a draft save and a token save over HTTP;
- `src/admin/tests`: `tokenCss.parity.spec.ts`, `CanvasMarkupParity.spec.ts`,
  `useBorder.spec.ts`, `BorderControl.spec.ts`;
- `tests/e2e`: `block-borders.spec.ts`.

**Target Platform**: the single Docker image (linux amd64 and arm64). The admin runs in
evergreen browsers. `overflow: clip` and `box-sizing` are supported by every browser the
admin supports.

**Project Type**: web application. A modular monolith backend with an embedded Vue admin.

**Performance Goals**: within the existing budgets (Principle VIII). Each bordered block adds
at most 3 declarations to its `[data-qb]` rule, and every page gains 5 token properties.
`page-200-blocks` stays the performance check (SC-007).

**Constraints**:
- no value from a document in CSS except token variables and fixed class names (Principle IX,
  FR-020);
- canvas and published markup identical (page-schema.md §9);
- additive contract changes only (Principle VI).

**Scale/Scope**:
- 15 block types;
- 2 contracts;
- 14 server renderers and 15 canvas components each gain one helper call;
- 1 new panel control and 1 new token list;
- no new endpoint.

## Constitution Check

*GATE: checked before Phase 0 and again after Phase 1.*

| # | Principle | How this plan complies |
|---|---|---|
| I | Modular Monolith with Vertical Slices | The Pages module (schema and validator), Editor (tokens) and Rendering (renderers and token CSS) each change only their own code. Contracts are shared through `docs/contracts/` as today. No cross-module call or table is added. |
| II | Easy to Install, Operate and Upgrade | No configuration key, no migration, no new dependency. It works offline. |
| III | Upgrades Never Lose Data | Additive within version 1, so no document is rewritten. Archives from before import unchanged. Old admin tabs refuse rather than lose (BB-11). The regenerated golden files prove existing pages render the same (BB-12). |
| IV | Pluggable Infrastructure | Not affected. |
| V | Multi-Site | Tokens and documents are already per site, and token references are resolved against the site's own set. No new site-owned entity. |
| VI | Content Schemas Are Versioned Contracts | Contract documents, schema files, examples and both validators change in one PR. It is additive, so it stays at version 1 (page-schema.md §6). The border is defined once in `$defs` and its defaults are read from the schema, never hard-coded (BB-04). Adding `border` to a type is one line, and no block's behaviour changes beyond it. |
| VII | Draft and Published Are Separate | A border is a draft prop until publish. Radius tokens are draft until the design is published (FR-017). |
| VIII | Rendering Is Server-Side, Cached, Invalidated | Rendered server-side with the existing per-node style rule. The existing publish invalidation covers documents and design (BB-11). It adds no JavaScript. |
| IX | Secure by Default | Only enum class names and token variables reach CSS. Token ids are pattern-checked, and the existing `[data-qb]` rule and CSP are unchanged. Saving tokens keeps its server-side capability check. |
| X | Test-First, Tests Always for Contracts | Contract examples and golden files for every block type, every width and style, missing tokens, and the two wrappers. Both parity suites cover the new kind. Integration tests cover the HTTP saves, and e2e covers keyboard and axe (BB-12, BB-13). |
| XI | Simplicity | One prop, one helper per side, and one control. No new token engine, flag or endpoint. Fixed widths, not tokens (BB-02). Per-side and per-breakpoint borders are deferred. |

**Operator impact**:
- new configuration keys: none;
- migrations: none;
- new dependencies: none;
- upgrade notes: "Blocks can have borders and rounded corners. Sites gain five default corner
  radii, which Designers can edit under Design. Existing pages look unchanged."

**Gate: PASS** before and after Phase 1. Phase 1 added no exception: the conditional wrappers
are within page-schema.md §9 because the canvas draws them too.

## Project Structure

### Documentation (this feature)

```text
specs/066-block-borders/
├── spec.md
├── plan.md                  # this file
├── research.md              # BB-01 to BB-14
├── data-model.md
├── quickstart.md
├── contracts/
│   └── block-border.md      # page-schema and design-tokens additions, rendering rules
├── checklists/
│   └── requirements.md
└── tasks.md                 # /speckit-tasks, not created here
```

### Source Code (repository root)

```text
docs/contracts/
├── page-schema.md                              # §2, §2a, §6, §9 (BB-14)
├── page-schema/v1.schema.json                  # $defs border, borderBox, radiusRef, …; border on 15 types
├── page-schema/examples/{valid,invalid}/       # borders-every-block.json + invalid cases
├── design-tokens.md                            # §1, §2: radii
├── design-tokens/v1.schema.json                # radii, $defs/radius
└── design-tokens/examples/
    ├── valid/default.json                      # + five default radii
    ├── valid/with-radii.json
    └── invalid/radii-*.json, radius-*.json

src/Modules/Editor/
├── QuickBuilder.Editor.Contracts/              # default.json embedded copy follows the contract
└── QuickBuilder.Editor/Infrastructure/TokenSetValidator.cs  # checks 3, 5, 6 include radii

src/Modules/Rendering/QuickBuilder.Rendering/
├── Domain/TokenCss.cs                          # TokenKind.Radius, --qb-radius-<id>
├── Domain/BlockDefaults.cs                     # OfDefinition(name, key)
└── Rendering/Blocks/
    ├── BorderStyle.cs                          # NEW: classes + declarations for a node
    ├── LayoutRenderers.cs                      # section, container, spacer, divider (+ frame)
    ├── TextRenderers.cs                        # heading, text, rich-text
    ├── MediaRenderers.cs                       # image (img), button, embed, component-ref (+ wrapper)
    ├── ListRenderers.cs                        # collection-list, list-card
    ├── NavigationRenderer.cs
    └── FormRenderer.cs

src/admin/src/
├── editor/blocks/useBorder.ts                  # NEW: same output as BorderStyle
├── editor/blocks/*/…Block.vue                  # each canvas component applies useBorder
├── editor/canvas/ComponentRefBlock.vue         # conditional wrapper classes
├── editor/blocks/blocks.css                    # .qb-border* rules, clipping, divider frame
├── editor/blocks/registry.ts                   # ControlKind 'border'
├── editor/panel/PropertyPanel.vue              # automatic Border group
├── editor/panel/controls/BorderControl.vue     # NEW
├── editor/panel/controls/TokenControl.vue      # kind 'radius'
├── editor/schema/schema.ts                     # definitionDefault(name, key)
├── editor/tokens/{defaultTokens,tokenCss,tokenValidator}.ts  # radii
├── features/design/DesignTokensView.vue        # Corner radius list
├── features/design/ScaleList.vue               # kind 'radii'
└── i18n/en.json                                # labels for the group, controls, token list

tests/
├── fixtures/rendering/token-css/radii.json     # NEW
├── fixtures/rendering/golden/borders-*.{json,html}   # NEW (4), existing regenerated
├── QuickBuilder.UnitTests/Rendering/Blocks/BorderStyleTests.cs   # NEW
├── QuickBuilder.IntegrationTests/{Pages,Editor}/…                # contract and save tests
└── e2e/specs/block-borders.spec.ts             # NEW

docs/operators/operations/content.md            # one sentence on radii (BB-14)
```

**Structure Decision**: the existing modular monolith and admin layout. No new project,
module or folder beyond the files marked NEW. The border is a cross-cutting prop, so it is
deliberately one helper on each side, called by every renderer. It is not a base class or a
pipeline stage.

## Complexity Tracking

None. Every constitution rule is met without an exception.

## Risks

| Risk | Mitigation |
|---|---|
| A border on one block type conflicts with that block's own CSS (Button frame, Divider line, Image figure) | BB-05 picks the framed element per type. The golden files and the quickstart §3 cover each case. |
| Clipping cuts off menus or focus rings | Clipping applies only to the box types in BB-06. Quickstart §3.7 checks a drop-down by hand. |
| The canvas and published markup drift | One helper per side, and `CanvasMarkupParity.spec.ts` runs on the new golden inputs. |
| Regenerating about 50 golden files hides a real change | The review checks that the diff of the existing files is only the five `--qb-radius-*` properties and the `blocks.{hash}.css` name (BB-12). |
| An old admin tab after the upgrade | Its validator refuses before saving, so nothing is lost silently (BB-11). It gets the existing error and a reload. |

## Deferred (recorded, not built)

- Different widths, styles or colours per side, and per-breakpoint borders (spec Out of scope).
- Width as tokens (BB-02).
- Shadows and other effects (the issue's possible follow-up).
- Borders on Navigation drop-down panels: they stay with the menu style (spec Assumptions).
