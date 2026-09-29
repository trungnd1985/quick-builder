# Change: `docs/contracts/page-schema.md` for Rendering

**Feature**: 012 (issue #12) | **Research**: PR-09, PR-10, PR-11

This is the planning copy of the edits this feature makes to the page schema contract. The
schema file `page-schema/v1.schema.json` does **not** change: no stored document is
affected.

---

## §6 Versioning — one addition

> The renderer lists the schema versions it renders. `RendererSchemaVersionsTests` fails
> the build when `page-schema/` holds a `vN.schema.json` the renderer does not list, so a
> new version cannot merge without rendering support (Principle VI).

## §8 Examples — one addition

> Every file in `examples/valid/` also has a rendered golden file,
> `tests/fixtures/rendering/golden/<name>.html`, produced with the default design and a
> fixed set of link targets and media (`tests/fixtures/rendering/context.json`).
> `RendererGoldenTests` compares the output byte for byte, and `CanvasMarkupParity.spec.ts`
> compares the editor canvas's normalised markup with it.

## §9 Styling hand-off — becomes normative, renamed "Rendering"

> **9. Rendering**
>
> The editor canvas and the published page produce the same markup for each block and use
> one stylesheet, `src/admin/src/editor/blocks/blocks.css`, served to visitors at
> `/_assets/blocks.{hash}.css`.
>
> - **Wrapper**: the page body is wrapped in `<div class="qb-canvas qb-page">`.
>   `.qb-page` sets `container-type: inline-size; container-name: page`. Block CSS uses
>   `@container page (max-width: 767px)` for tablet and `@container page (max-width:
>   480px)` for mobile.
> - **Tokens**: the design becomes custom properties on `.qb-page`:
>   - `--qb-color-<id>`, `--qb-space-<id>` and `--qb-text-<id>`;
>   - `--qb-font-heading`, `--qb-font-body` and `--qb-base-size`;
>   - `--qb-width-narrow`, `--qb-width-normal` and `--qb-width-wide`.
>
>   Every id of the built-in default set is always emitted. The C# `TokenCss` and the
>   editor's `tokenCss.ts` produce the same map from the shared fixtures in
>   `tests/fixtures/rendering/token-css/`.
> - **Per-node values**: token-driven values (for example `--qb-section-bg`) are custom
>   properties on the block. The canvas sets them in a `style` attribute; the published
>   page sets them in one `<style>` rule per node, `[data-qb="<id>"]{…}`, so the page's CSP
>   allows exactly that style block. The values are identical.
> - **Defaults**: a missing property renders as its schema `default`. A token reference to
>   a token the design lacks renders as the default's token (011 clarification Q4).
> - **Markup differences allowed** between the canvas and the published page:
>   - editor-only attributes and elements (selection, drop indicators, `contenteditable`,
>     `draggable`, empty-state hints);
>   - the Embed body (a placeholder in the canvas, a lazy provider iframe when published);
>   - a missing image (a placeholder in the canvas, left out when published);
>   - a link to a page that is not live (a link in the canvas, plain text when published).
> - **Rich text**: rendered through an allow-list:
>   - `paragraph` → `p`, `bulletList` → `ul`, `orderedList` → `ol`, `listItem` → `li`;
>   - `hardBreak` → `br`;
>   - `bold` → `strong`, `italic` → `em`, `link` → `a`.
>
>   Nothing else is ever emitted.
> - **Adding a block type** now also means adding one renderer class, which
>   `BlockRendererCoverageTests` checks, and golden files.

## §5 Reserved segments — note only

> `/_assets` (block stylesheet) joins `/_media` as a platform prefix the slug grammar cannot
> produce; neither needs a table entry.
