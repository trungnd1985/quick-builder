# Contract: Page Schema, Version 1

**Audience**: anyone who reads or writes page and component content: the Pages module
today, the visual editor (#11) and the renderer (#12) next.

**Introduced by**: [specs/010-page-tree-components](../../specs/010-page-tree-components/)
(research P-09 and P-10).

This document and [`page-schema/v1.schema.json`](page-schema/v1.schema.json) together are
the contract. The schema file is the single definition of a valid document: the product
embeds this exact file and validates against it, and every other validator (the editor's)
must use it too. `PageSchemaContractTests` fails the build when this document and the
schema disagree. Changing either needs this document, the schema file and, for a new
version, the forward migration of stored documents, all in the same pull request
(constitution, Development Workflow).

---

## 1. What a document is

A **content document** is the editable content of one page or one shared component. In
this release only drafts exist; publishing (#12) adds the published copy. Every document is
a JSON object:

```json
{
  "schemaVersion": 1,
  "root": {
    "id": "root",
    "type": "root",
    "props": {},
    "children": [
      { "id": "header", "type": "component-ref", "props": { "componentId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f" }, "children": [] }
    ]
  }
}
```

| Field | Rule |
|---|---|
| `schemaVersion` | Integer. The version this document was written in. This release supports `1` only. |
| `root` | A node of type `root`. |

Every **node** has exactly four properties:

| Property | Rule |
|---|---|
| `id` | String matching `^[A-Za-z0-9_-]{1,64}$`. **Unique within the document.** Stable across saves, so the editor can keep selection and the renderer can track changes. |
| `type` | A node type from §2. |
| `props` | An object matching that type's properties, with no properties beyond those listed. |
| `children` | An array of nodes, always present. Empty for types that take no children. |

A new page or component starts with `root` and an empty `children` array.

A document is stored as `jsonb`, which keeps its value but not its key order or
whitespace. "Read back unchanged" means **equal as a JSON value**.

## 2. Node types

| Type | Props | Children | Allowed in |
|---|---|---|---|
| `root` | none (`{}`) | `rootChild` | the envelope's `root` only |
| `component-ref` | `componentId`: UUID of a shared component of the **same site** | none | page documents, under `root` only |
| `section` | `background`, `paddingY`, `width`, `layerLabel` | `contentChild` | under `root` |
| `container` | `direction`, `columns`, `gap`, `padding`, `align`, `maxWidth`, `background`, `stackOnMobile`, `layerLabel` | `contentChild` | in `section` or `container` |
| `heading` | `text`\*, `level`, `align`, `color`, `link`, `layerLabel`, `bind` | none | in `section` or `container` |
| `text` | `text`\*, `align`, `color`, `size`, `layerLabel`, `bind` | none | in `section` or `container` |
| `rich-text` | `content`\*, `align`, `color`, `layerLabel`, `bind` | none | in `section` or `container` |
| `image` | `media`, `alt`, `decorative`, `width`, `link`, `layerLabel`, `bind` | none | in `section` or `container` |
| `button` | `label`\*, `link`, `style`, `align`, `newTab`, `layerLabel`, `bind` | none | in `section` or `container` |
| `spacer` | `height`, `layerLabel` | none | in `section` or `container` |
| `divider` | `color`, `thickness`, `spacing`, `layerLabel` | none | in `section` or `container` |
| `embed` | `url`, `title`, `aspectRatio`, `layerLabel` | none | in `section` or `container` |
| `collection-list` | `source`\*, `filters`, `sort`, `limit`, `paging`, `columns`, `gap`, `layerLabel` | `listChild`: its `list-card`, then its `list-empty` (rule 20) | in `section` or `container` (016) |
| `list-card` | `layerLabel` | `contentChild` | in `collection-list` only (016) |
| `list-empty` | `layerLabel` | `contentChild` | in `collection-list` only (016) |

\* required. Values, limits and defaults are in the schema file, which is the single
definition; this table is its index. `layerLabel` is an editor aid and is never rendered.
`bind` holds a block's bindings to item fields, and only the properties
[binding-expression.md](binding-expression.md) §4 names may be bound (016).

**Widths** (added by 011, clarification 2026-09-29):
- A Section's `width` caps its content, which is centred: `narrow`, `contained` (shown as
  "Normal") or `wide` take the site's content widths (design-tokens.md §2). `full` spans the
  page. The default is `contained`.
- A Container's `maxWidth` caps the Container itself at `narrow`, `normal` or `wide`, placed
  by its `align` (start, centre or end). It is never wider than its parent. The default,
  `full`, fills its parent.
- A horizontal Container's `columns` sets the share of each block: `equal` (the default),
  `2-1`, `1-2`, `3-1`, `1-3` or `2-1-1`, in parts of the row. Blocks beyond the pattern start
  a new row with the same pattern. The pattern holds at desktop and tablet widths; at mobile
  width the row stacks when `stackOnMobile` is on. A vertical Container ignores it.

The placement lists are three `$defs` in the schema:
- `rootChild` holds `section` and `component-ref`;
- `contentChild` holds every other block type;
- `listChild` holds `list-card` and `list-empty`, and only a `collection-list` admits it
  (016).

So every piece of content sits in a Section. A component document's root holds Sections
only, because rule 7 refuses `component-ref` there.

**Adding a block type** (added by 011):
1. a `$defs` entry;
2. a place in `rootChild` or `contentChild`;
3. golden examples (§8);
4. the editor's `definition.ts` and canvas component (docs/contributing.md);
5. from #12, its renderer.

No other type changes (constitution Principle VI).

## 2a. Shared values

- **Token reference** `{ "token": id }`, for colours (`colorRef`), spacing (`spaceRef`)
  and text sizes (`textSizeRef`). The id names an entry of the site's design token set
  ([design-tokens.md](design-tokens.md)). It **may name a token that no longer exists**:
  consumers then use the property's `default`.
- **Link**: `{ "type": "page", "pageId": uuid }` or `{ "type": "url", "href": … }`.
  - The schemes are `https`, `http`, `mailto` and `tel` only.
  - A `pageId` may name a page that no longer exists. The renderer (#12) decides how that
    is shown.
- **Media**: `{ "mediaId": uuid }`, a file of the site's media library. It may no longer
  exist.
- **Rich text**: a ProseMirror `doc` limited to:
  - paragraphs, bulleted and ordered lists, and hard breaks;
  - headings of levels 2 to 4 (`{ "type": "heading", "attrs": { "level": 2 } }`) and
    quotes (`blockquote`), at the top level only; a quote holds paragraphs and lists (035);
  - images (`{ "type": "image", "attrs": { "media": { "mediaId": uuid }, "alt"?, "decorative"? } }`)
    and tables of rows of cells (`tableCell`, or `tableHeader` in the first row only) holding
    paragraphs, at the top level only (035);
  - an alignment on paragraphs and headings (`"attrs": { "align": "start" | "center" | "end" }`),
    which, when absent, is the block's own (035);
  - the marks bold, italic, underline, strike and code (035), and link
    (`{ "type": "link", "attrs": { "to": <link> } }`), at most 6 on one text.

  The top level (`rtBlock`) admits every block; list items and quotes admit only paragraphs
  and lists (`rtListBlock`).

  There is never HTML.
- **Defaults**: a property's `default` in the schema is its value when it is missing. A
  newly inserted block holds only its required properties.

## 3. Rules beyond the schema

The schema file can't express these, so validators check them in code. They are equally
binding.

1. Node `id`s are unique within the document (`duplicate_node_id`).
2. A document holds at most **5,000** nodes, the root included (`too_many_nodes`).
3. Nesting depth is at most **32**, counting `root` as 1 (`too_deep`).
4. The document is at most **1 MiB** of UTF-8 JSON (`pages.document_too_large`).
5. No object has two properties with the same name (`duplicate_property`). Parsers
   disagree about which one wins, so neither is allowed.
6. Every `componentId` names a component of the document's own site (`unknown_component`).
   An unknown id and another site's id are the same failure.
7. A component document contains no `component-ref` (`component_ref_not_allowed`), so
   components never nest.
8. **No raw HTML, anywhere.** No property may carry markup. Rich text, when a block type
   needs it, is structured JSON, and the renderer produces HTML from an allow-list
   (Principle IX). A block type whose props include an HTML string is refused in review.
9. A Rich Text block holds at most **20,000** characters of text, counted in code points
   over all its `text` nodes (`rich_text_too_long`, at `…/props/content`).
10. A rich text shows at most **50** images (`rich_text_too_many_images`) and holds at most
    **20** tables (`rich_text_too_many_tables`), reported at `…/props/content` (035).
11. Every row of a table has as many cells as its first row (`table_not_rectangular`, at
    the first row that differs). Cells never span rows or columns (035).
12. Header cells appear only in the first row, and a first row with a header cell holds
    only header cells (`table_header_misplaced`, at the first misplaced cell) (035).

Rules 13–21 come with bindings and Collection lists (016, [binding-expression.md](binding-expression.md)):

13. A document holding a `bind` or a `collection-list` carries `bindingVersion: 1`
    (`binding_version_required`, at the document, pointer empty).
14. Every list `source.collectionId` names a collection of the document's own site
    (`unknown_collection`, at `…/props/source`). An unknown id and another site's id are the
    same failure. Like rule 6, it needs the site, so the server checks it last, after every
    other rule, and the editor checks it against the site's collections.
15. Every `scope` names `"page"` or an enclosing `collection-list` whose card the reader sits
    in (`unknown_scope`, at the `scope`). A list's own filters cannot read its own items, and
    its empty state has no item.
16. A component document never reads the page's item: no `"page"` scope, and nothing that
    reads the nearest scope outside every list (`page_scope_not_allowed`, at the `scope` or
    the reader).
17. Collection lists nest at most **2** deep, in a card or in an empty state
    (`list_too_deep`, at the third list's `/type`).
18. A document holds at most **20** Collection lists (`too_many_lists`, at the 21st list's
    `/type`).
19. Paginated lists in one document use distinct page parameters, `page` when none is given
    (`duplicate_page_param`, at the second list's `…/props/paging/param`, or `…/props/paging`
    when it names none).
20. A `collection-list` holds exactly its `list-card` and then its `list-empty`
    (`list_children_invalid`, at its `…/children`).
21. The paginated lists of a page and of the components it shows use distinct page
    parameters, so each pages on its own (016 FR-027) (`page_param_taken`, with `param` and
    `with`, the page or component it clashes with). In a page, it is reported at the page's
    own list, or at the second `component-ref` when two components clash; in a component, at
    its own list. Like rules 6 and 14 it needs other documents, so only the server checks it,
    last: a draft save against the other drafts, a publish against the live versions. The
    editor keeps a page's own lists clear of its components' names as they are set.

A `"page"` scope in an ordinary page is not refused: it is a broken binding, shown as empty,
so turning a template back into a page never makes its draft unsaveable. A binding to a field
that no longer exists, or whose type does not fit, is broken in the same way. The shapes the
schema subset cannot say — exactly one of `field`, `item` and `parts`, and the like
(binding-expression.md §6) — are checked with rules 15–19 and reported as `props_invalid`.

Rules 10–12 apply to rich text item values too (collection-schema.md §3), where a value that
breaks one is malformed. Rows beyond 200 and cells beyond 20 per row fail the schema's
`maxItems` (`props_invalid`, with the limit in the message), as every size limit does.

Validation stops at the first failure and reports a `rule` and a JSON `pointer` (RFC 6901)
into the submitted document. The checks run in this order, so a given document always
fails the same way:

1. size (rule 4);
2. well-formed JSON (`malformed_json`) without duplicate properties (rule 5);
3. `schemaVersion` supported (`unsupported_schema_version`). A missing or non-integer
   version is left to the schema;
4. a structural walk: depth, node count and unique ids (rules 1–3);
5. the schema: `schema_violation`, or more specifically:
   - `props_invalid` for any failure under a node's `props`. A failed `oneOf` there, such
     as an unknown rich-text mark, is reported at that object's `/type`;
   - `root_misplaced` for a `root` anywhere but the envelope;
   - `nesting_not_allowed` for a node type (a member of `rootChild`, `contentChild` or
     `listChild`) in a children list that doesn't admit it, at its `/type`;
   - `unknown_type` for any other `type`, at its `/type`;
   - `children_not_allowed` for children under a type that takes none;
6. the list's children (rule 20);
7. the code rules: the component rules (rules 6–7), then rich text length (rule 9) for
   every Rich Text block, then rules 10–12 for every Rich Text block, in document order;
8. rule 13, then one walk in document order that checks each list (rules 18, 17, its
   source, filters and sort keys, 19) and then each binding (rules 15–16) as it meets them;
9. on the server, rule 14.

Depth and count come before the schema on purpose. They bound the work the schema check
does, and they give an oversized document its own reason rather than a generic one.

## 4. Limits

| Limit | Value |
|---|---|
| Document size | 1,048,576 bytes |
| Nodes per document | 5,000 |
| Node depth | 32 |
| Node id length | 64 |
| Slug length | 100 |
| Address length | 1,024 |
| Page nesting | 10 |
| Pages per site | 2,000 |
| Collection lists per document | 20 |
| Collection list nesting | 2 |

The limits are part of the contract, not operator settings: a document valid on one
installation is valid on every installation, which site export and import (#20) rely on.

## 5. Addresses and reserved segments

- A slug matches `^[a-z0-9]+(-[a-z0-9]+)*$`.
- A page's address is its parent's address, then `/`, then its slug. The home page is `/`
  and contributes nothing to its children's addresses; it keeps its slug, unused, so it has
  an address again if another page becomes the home page.
- Addresses are unique per site. They never include the host name or base path; absolute
  addresses come from the site's base URL (009).
- **Templates** (016): a page that is a collection's template has no address of its own.
  Its stored address is a pattern, its parent's address followed by `/{slug}` (for example
  `/blog/{slug}`), and each published item of the collection is served at the pattern with
  its slug. `{` and `}` are outside the slug grammar, so no page can take a pattern's
  address, and the uniqueness of addresses allows one template per parent. A template has a
  parent other than the home page, and no children of its own.

**Reserved first segments**: no address may begin with one of these, because the platform
answers there before any page could.

| Segment | Owner |
|---|---|
| `api` | Administrative API |
| `admin` | Admin shell |
| `health` | Health probes |

Platform prefixes the slug grammar can't produce, such as `/_media` (media files) and `/_assets`
(the block stylesheet, 012), need no entry.
`ReservedAddressCoverageTests` fails the build if the platform gains a prefix that is
neither listed here nor impossible to produce.

## 6. Versioning

- **Additive changes stay in version 1**: a new node type, or a new optional prop on an
  existing type. Documents that were valid stay valid, and the renderer must render every
  type in the current schema.
- **Breaking changes need a new version**: removing or renaming a type or prop, making a
  prop required, or narrowing a value. A breaking change needs all of these in one PR:
  1. `vN.schema.json` beside the older files;
  2. this document updated, with a "Changes from vN-1" section;
  3. a forward migration that rewrites every stored document still at `N-1`;
  4. validators accepting `N`;
  5. the renderer able to render every version still present in the database.
- `schemaVersion` in the document and the `schema_version` column always agree.
- The renderer lists the schema versions it renders. `BlockRendererCoverageTests` fails the
  build when `page-schema/` holds a `vN.schema.json` the renderer does not list, so a new
  version cannot merge without rendering support (012; Principle VI).

**Additions in 016 (still version 1)**: an optional envelope `bindingVersion`; an optional
`bind` prop on `heading`, `text`, `rich-text`, `image` and `button`; the node types
`collection-list`, `list-card` and `list-empty` with the `listChild` placement; and rules
13–21. Later in 016, the optional `paging.layout` of `collection-list` (`numbers` by
default, or `steps`), and an optional `link` on `heading`, bindable like a button's. Every addition is optional or a new type, so every document valid
before stays valid and renders the same. Bindings have their own contract and version,
[binding-expression.md](binding-expression.md).

**Additions in 035 (still version 1)**: rich text gained headings (levels 2–4), quotes,
images, tables, the marks underline, strikethrough and code, and an alignment on paragraphs
and headings. List items now refer to `rtListBlock`, which holds exactly what `rtBlock` held
before, so list items accept the same content as ever; `rtBlock` is the top level only. A
text node may carry up to 6 marks instead of 3. Every document valid before stays valid and
renders the same (035 SC-001).

## 7. The schema file

[`page-schema/v1.schema.json`](page-schema/v1.schema.json), JSON Schema 2020-12. It uses
only this subset of the vocabulary, which the Pages validator interprets and
`PageSchemaSubsetTests` enforces:

- **Validation**: `type`, `properties`, `required`, `additionalProperties`, `const`, `enum`,
  `pattern`, `minLength`, `maxLength`, `minimum`, `maximum`, `items`, `minItems`,
  `maxItems`, `format: uuid`.
- **Composition**: `$ref` to `#/$defs/…`, and `oneOf` whose branches are told apart by a
  `type` property's `const`.
- **Annotations**: `$schema`, `$id`, `$defs`, `title`, `description`, `default`. A
  `default` is never validated; consumers use it as the value of a missing property.

**Patterns** mean the same in ECMAScript and .NET only within a common subset. They may
not use lookbehind, named groups or Unicode property classes (`\p{…}`). The server compiles
them with `RegexOptions.ECMAScript`, and the editor with `new RegExp(p, 'u')`.

A block type that needs more than this is a planning decision for its feature. The
recorded fallback is a complete JSON Schema implementation (010 research P-09).

## 8. Examples (golden files)

Every validator of this schema runs the same files:

- `page-schema/examples/valid/*.json` must pass.
- `page-schema/examples/invalid/<rule>--<case>.json` must fail with the rule its name
  begins with, at the pointer given in `page-schema/examples/invalid/expected.json`.

| File | Expected |
|---|---|
| `valid/empty.json` | passes |
| `valid/header-and-footer.json` | passes (a page document with two `component-ref`s) |
| `invalid/malformed_json--truncated.json` | `malformed_json` |
| `invalid/schema_violation--missing-schema-version.json` | `schema_violation` at the root |
| `invalid/schema_violation--bad-node-id.json` | `schema_violation` at `/root/id` |
| `invalid/unsupported_schema_version--version-2.json` | `unsupported_schema_version` |
| `invalid/unknown_type--paragraph.json` | `unknown_type` at `/root/children/0/type` |
| `invalid/props_invalid--component-id-not-uuid.json` | `props_invalid` at `…/props/componentId` |
| `invalid/props_invalid--extra-prop.json` | `props_invalid` at `…/props/label` |
| `invalid/children_not_allowed--ref-with-child.json` | `children_not_allowed` |
| `invalid/root_misplaced--nested-root.json` | `root_misplaced` |
| `invalid/duplicate_node_id--twice.json` | `duplicate_node_id` at the second occurrence |
| `invalid/duplicate_property--props.json` | `duplicate_property` |
| `valid/every-block.json` | passes: one of each type, nested Section › Container › Container › leaves |
| `valid/tokens-missing.json` | passes: token references to ids no token set holds |
| `valid/widths-and-columns.json` | passes: every Section width, Container maximum width and column pattern |
| `valid/rich-text-lists.json` | passes: nested lists, every mark, both link kinds |
| `valid/rich-text-inline-marks.json` | passes: underline, strikethrough and code, and all six marks on one text (035) |
| `valid/rich-text-image.json` | passes: an image with alternative text and a decorative one (035) |
| `valid/rich-text-image-without-alt.json` | passes: an image without alternative text is valid; publishing refuses it (035 FR-014) |
| `valid/rich-text-table.json` | passes: tables with and without a header row, cells with several paragraphs (035) |
| `valid/rich-text-limits.json` | passes: every rich text limit at once — 50 images, 20 tables, one of 200 rows × 20 columns, 20,000 characters (035); written without indentation to stay under 1 MiB |
| `valid/rich-text-align.json` | passes: aligned headings and paragraphs, and one that follows its block (035) |
| `valid/rich-text-every-format.json` | passes: every 035 format together, with links inside a heading, a quote and a table cell (035) |
| `valid/rich-text-headings-quotes.json` | passes: headings 2–4, quotes holding paragraphs and lists, empty heading and quote (035) |
| `valid/component-page.json` | passes as a component document: Sections only |
| `invalid/nesting_not_allowed--section-in-section.json` | at `/root/children/0/children/0/type` |
| `invalid/nesting_not_allowed--heading-at-root.json` | at `/root/children/0/type` |
| `invalid/children_not_allowed--child-in-heading.json` | at `…/children` of the heading |
| `invalid/props_invalid--javascript-link.json` | at `…/props/link/href` |
| `invalid/props_invalid--embed-other-provider.json` | at `…/props/url` |
| `invalid/props_invalid--rich-text-unknown-mark.json` | at `…/marks/0/type` |
| `invalid/props_invalid--heading-level-7.json` | at `…/props/level` |
| `invalid/props_invalid--token-bad-id.json` | at `…/props/background/token` |
| `invalid/props_invalid--missing-required-text.json` | at `…/props` |
| `invalid/props_invalid--container-direction.json` | at `…/props/direction` |
| `invalid/props_invalid--text-size-not-ref.json` | at `…/props/size` |
| `invalid/props_invalid--image-media-not-uuid.json` | at `…/props/media/mediaId` |
| `invalid/props_invalid--spacer-extra-prop.json` | at `…/props/color` |
| `invalid/props_invalid--divider-thickness.json` | at `…/props/thickness` |
| `invalid/props_invalid--section-width-huge.json` | at `…/props/width` |
| `invalid/props_invalid--container-max-width.json` | at `…/props/maxWidth` |
| `invalid/props_invalid--container-columns.json` | at `…/props/columns` |
| `invalid/rich_text_too_long--20001.json` | at `…/props/content` |
| `invalid/props_invalid--rich-text-heading-level-1.json` | at `…/attrs/level` (035) |
| `invalid/props_invalid--rich-text-heading-level-5.json` | at `…/attrs/level` (035) |
| `invalid/props_invalid--rich-text-quote-in-quote.json` | at the inner quote's `/type` (035) |
| `invalid/props_invalid--rich-text-table-in-quote.json` | at the table's `/type` (035) |
| `invalid/props_invalid--rich-text-heading-in-list.json` | at the heading's `/type` (035) |
| `invalid/props_invalid--rich-text-seven-marks.json` | at `…/marks`: at most 6 (035) |
| `invalid/props_invalid--rich-text-image-in-list.json` | at the image's `/type` (035) |
| `invalid/props_invalid--rich-text-image-src.json` | at `…/attrs/src`: images name a media file only (035) |
| `invalid/props_invalid--rich-text-image-media-not-uuid.json` | at `…/attrs/media/mediaId` (035) |
| `invalid/props_invalid--rich-text-image-alt-301.json` | at `…/attrs/alt`: at most 300 characters (035) |
| `invalid/rich_text_too_many_images--51.json` | at `…/props/content` (035, rule 10) |
| `invalid/props_invalid--rich-text-align-justify.json` | at `…/attrs/align`: start, center or end only (035) |
| `invalid/props_invalid--rich-text-paragraph-style.json` | at `…/attrs/style`: no styles, ever (035) |
| `invalid/props_invalid--rich-text-align-on-quote.json` | at the quote's `/attrs`: quotes take no alignment (035) |
| `invalid/rich_text_too_many_tables--21.json` | at `…/props/content` (035, rule 10) |
| `invalid/table_not_rectangular.json` | at the first row that differs (035, rule 11) |
| `invalid/table_header_misplaced.json` | at the header cell in row 2 (035, rule 12) |
| `invalid/props_invalid--rich-text-table-in-list.json` | at the table's `/type` (035) |
| `invalid/props_invalid--rich-text-image-in-cell.json` | at the image's `/type`: cells hold paragraphs only (035) |
| `invalid/props_invalid--rich-text-cell-colspan.json` | at the cell's `/attrs`: cells never span (035) |
| `invalid/props_invalid--rich-text-table-201-rows.json` | at the table's `/content`: at most 200 rows (035) |
| `invalid/props_invalid--rich-text-table-21-columns.json` | at the row's `/content`: at most 20 cells (035) |

Documents too deep or with too many nodes are generated by the tests rather than stored.
Every block type has at least one valid and one invalid example (011 SC-009);
`PageSchemaContractTests` checks this.

Every file in `examples/valid/` also has a rendered golden file,
`tests/fixtures/rendering/golden/<name>.html`, produced with the default design and a fixed
set of link targets and media (`tests/fixtures/rendering/context.json`). `RendererGoldenTests`
compares the output byte for byte, and `CanvasMarkupParity.spec.ts` compares the editor
canvas's normalised markup with it. The 200-block page the render budget is measured on,
`tests/fixtures/rendering/page-200-blocks.json`, is checked the same way (012).

## 9. Rendering

Normative since 012 (#12). The editor canvas and the published page produce the same markup
for each block and use one stylesheet, `src/admin/src/editor/blocks/blocks.css`, served to
visitors at `/_assets/blocks.{hash}.css`.

- **Wrapper**: the page body is wrapped in `<div class="qb-canvas qb-page">`. `.qb-page`
  sets `container-type: inline-size; container-name: page`. Block CSS uses
  `@container page (max-width: 767px)` for tablet and `@container page (max-width: 480px)`
  for mobile.
- **Tokens**: the design becomes custom properties on `.qb-page`:
  - `--qb-color-<id>`, `--qb-space-<id>` and `--qb-text-<id>`;
  - `--qb-font-heading`, `--qb-font-body` and `--qb-base-size`;
  - `--qb-width-narrow`, `--qb-width-normal` and `--qb-width-wide`, in `px`, from the
    content widths, or from the default set's when the site's set has none.

  Every id of the built-in default set (design-tokens.md) is always emitted, with its
  built-in value when the site's set lacks it, so a fallback always resolves. The C#
  `TokenCss` and the editor's `tokenCss.ts` produce the same map from the shared fixtures in
  `tests/fixtures/rendering/token-css/`.
- **Per-node values**: token-driven values (for example `--qb-section-bg`) are custom
  properties on the block. The canvas sets them in a `style` attribute. The published page
  sets them in one `<style>` rule per node, `[data-qb="<id>"]{…}`, so the page's content
  security policy allows exactly that style block. The values are identical.
- **Defaults**: a missing property renders as its schema `default`. A token reference to a
  token the design lacks renders as the default's token (011 clarification Q4).
- **Markup differences allowed** between the canvas and the published page:
  - editor-only attributes and elements (selection, drop indicators, `contenteditable`,
    `draggable`, empty-state hints);
  - the Embed body (a placeholder in the canvas, a lazy provider iframe when published);
  - a missing image, or an Embed with no video address (a placeholder in the canvas, left
    out when published);
  - a linked image (wrapped in its link only when published);
  - a link to a page that is not live (a link in the canvas, plain text when published);
  - an image's addresses (`src`, `srcset`) and `sizes`: admin addresses in the canvas, the
    site's `/_media` addresses when published.
- **Images** (since 013, #13): a resolved image is drawn as
  `<figure class="qb-image qb-image--{full|natural}">[<a …>]<img …>[</a>]</figure>`, and its
  `img` carries, in this order:
  - `src`: the largest resized copy no wider than 1,280 px, or the original when it has
    none;
  - `srcset`: every resized copy as `{address} {width}w`, then the original at its own
    width only when it is no wider than the largest configured width, so a large original
    is never offered. It is left out when the file has no resized copies (documents, GIFs,
    animated images, images not measured yet);
  - `sizes`, only with `srcset`: `(max-width: {C}px) 100vw, {C}px`, where `C` is the
    enclosing Section's content width (`narrow`, `contained` = normal, `wide`), capped by
    the image's own width when it is shown at its natural size; `100vw` in a `full` Section;
  - `width` and `height`: the displayed dimensions, left out while unknown;
  - `alt`, `loading="lazy"` and `decoding="async"`, as before.

  The page tree schema is unchanged: an Image still stores only the file's id, and the
  addresses and dimensions come from the media library when the page is rendered.
- **Rich text**: rendered through an allow-list:
  - `paragraph` → `p`, `bulletList` → `ul`, `orderedList` → `ol`, `listItem` → `li`;
  - `hardBreak` → `br`;
  - `bold` → `strong`, `italic` → `em`, `link` → `a`;
  - added by 035:
    - `heading` → `h2`, `h3`, `h4`, and `blockquote` → `blockquote`;
    - an `align` → `class="qb-align--start|center|end"` on the `p` or `h*`, never a style;
    - `underline` → `u`, `strike` → `del`, `code` → `code`;
    - `table` → `div.qb-rt-table[tabindex=0] > table > tbody`, `tableRow` → `tr`,
      `tableHeader` → `th[scope=col]`, `tableCell` → `td`;
    - `image` → `figure.qb-rt-image > img`, with `src`, `srcset`, `sizes` (the text's width,
      capped by the file's own), `width`, `height`, `alt` (empty when decorative),
      `loading="lazy"` and `decoding="async"`. A file that is gone, or another site's,
      renders nothing.

  Nothing else is ever emitted.
- **Adding a block type** also means adding one renderer class, which
  `BlockRendererCoverageTests` checks, and golden files (§8).
