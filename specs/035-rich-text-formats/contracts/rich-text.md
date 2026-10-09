# Contract Delta: Rich Text, Page Schema Version 1 (Additive)

**Feature**: 035 | **Decisions**: research RT-01, RT-02, RT-04, RT-09

This file is the exact change to make to `docs/contracts/page-schema/v1.schema.json`, `docs/contracts/page-schema.md` and `docs/contracts/collection-schema.md`. Each change is **additive** (page-schema §6, collection-schema §9): every document valid before stays valid, with the same meaning.

## 1. Rich text document, after this feature

```json
{ "type": "doc", "content": [
  { "type": "heading", "attrs": { "level": 2, "align": "center" },
    "content": [ { "type": "text", "text": "Opening hours" } ] },
  { "type": "paragraph", "content": [
    { "type": "text", "text": "Open ", "marks": [ { "type": "underline" } ] },
    { "type": "text", "text": "daily", "marks": [ { "type": "bold" }, { "type": "strike" } ] },
    { "type": "text", "text": "npm run dev", "marks": [ { "type": "code" } ] } ] },
  { "type": "blockquote", "content": [
    { "type": "paragraph", "content": [ { "type": "text", "text": "Best bread in town." } ] } ] },
  { "type": "image", "attrs": {
    "media": { "mediaId": "0192f5a1-0000-7000-8000-000000000010" },
    "alt": "The shop front", "decorative": false } },
  { "type": "table", "content": [
    { "type": "tableRow", "content": [
      { "type": "tableHeader", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Day" } ] } ] },
      { "type": "tableHeader", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Hours" } ] } ] } ] },
    { "type": "tableRow", "content": [
      { "type": "tableCell", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Mon" } ] } ] },
      { "type": "tableCell", "content": [ { "type": "paragraph" } ] } ] } ] }
] }
```

## 2. `$defs` changes

| Definition | Shape | Notes |
|---|---|---|
| `rtDoc.content` | items `rtBlock`, `maxItems` 500 (unchanged) | |
| `rtBlock` | `oneOf`: `rtParagraph`, `rtHeading`, `rtBlockquote`, `rtBulletList`, `rtOrderedList`, `rtImage`, `rtTable` | top level only |
| `rtListBlock` **new** | `oneOf`: `rtParagraph`, `rtBulletList`, `rtOrderedList` | the old `rtBlock` |
| `rtListItem.content` | items `rtListBlock` (was `rtBlock`, same set) | |
| `rtParagraph` | + `attrs`: `{ align?: "start" \| "center" \| "end" }`, `additionalProperties: false` | `attrs` optional |
| `rtHeading` **new** | `type: "heading"`, `attrs` (required): `{ level: 2 \| 3 \| 4 (enum), align?: … }`, `content?`: `rtInline[]` `maxItems` 1000 | |
| `rtBlockquote` **new** | `type: "blockquote"`, `content`: `rtListBlock[]`, `minItems` 1, `maxItems` 200 | |
| `rtImage` **new** | `type: "image"`, `attrs` (required): `{ media: $ref media (required), alt?: string maxLength 300, decorative?: boolean }` | no `content` |
| `rtTable` **new** | `type: "table"`, `content`: `rtTableRow[]`, `minItems` 1, `maxItems` 200 | |
| `rtTableRow` **new** | `type: "tableRow"`, `content`: `oneOf` `rtTableCell` \| `rtTableHeader`, `minItems` 1, `maxItems` 20 | |
| `rtTableCell`, `rtTableHeader` **new** | `type: "tableCell"` / `"tableHeader"`, `content`: `rtParagraph[]`, `minItems` 1, `maxItems` 50 | no attrs: no spans, no widths |
| `rtMark` | + `{ type: "underline" }`, `{ type: "strike" }`, `{ type: "code" }` | |
| `rtText.marks` | `maxItems` 3 → **6** | |

`media` is the existing `$defs/media` (`{ "mediaId": uuid }`).

## 3. Rules beyond the schema (page-schema §3), new

Checked after rule 9, in this order, for each Rich Text block and each rich text item value:

10. **Counts**: at most **50** `image` and **20** `table` nodes in one rich text: `rich_text_too_many_images` and `rich_text_too_many_tables`, at the rich text (`…/props/content`, or the field).
11. **Rectangular tables**: every `tableRow` of a table has as many cells as the first: `table_not_rectangular`, at the first row that differs.
12. **Header row**: `tableHeader` appears only in the first row, and a first row that holds one holds only `tableHeader`: `table_header_misplaced`, at the first misplaced cell.

Rows (more than 200) and cells per row (more than 20) fail the schema's `maxItems`, reported as `props_invalid` with the pointer and `maxItems` in the message, which is how the contract reports every size limit.

The constants are `PageSchema.MaxRichTextImages` (50) and `PageSchema.MaxRichTextTables` (20), mirrored in `limits.ts`.

## 4. Publish rule (not a validity rule)

A rich text `image` with neither a non-blank `alt` nor `decorative: true` is **valid** and may be saved in a draft. Publishing a page or component that holds one is refused with `pages.image_alt_missing`. An item reports `image_alt_missing` (research RT-10).

## 5. Rendering allow-list (page-schema "Rendering"), complete after this feature

- **Blocks**:
  - `paragraph` → `p`;
  - `heading` → `h2`, `h3`, `h4`;
  - `blockquote` → `blockquote`;
  - `bulletList` → `ul`, `orderedList` → `ol` (`start`), `listItem` → `li`;
  - `hardBreak` → `br`.
- **Alignment**: `align` → `class="qb-align--start|center|end"` on `p` and `h*`.
- **Marks**:
  - `bold` → `strong`, `italic` → `em`;
  - `underline` → `u`, `strike` → `del`, `code` → `code`;
  - `link` → `a` (unchanged rules).
- **Tables**: `table` → `div.qb-rt-table[tabindex=0] > table > tbody`, `tableRow` → `tr`, `tableHeader` → `th[scope=col]`, `tableCell` → `td`.
- **Images**: `image` → `figure.qb-rt-image > img`, with `src`, `srcset`, `sizes`, `width`, `height`, `alt` (empty when decorative), `loading="lazy"` and `decoding="async"`. A missing file, or another site's file, renders nothing.

Nothing else is ever emitted.

## 6. Collection schema: `richText` settings

| `type` | Settings | Defaults offered by the builder |
|---|---|---|
| `richText` | `formats`: optional object `{ headings?: bool, images?: bool, tables?: bool }`, each missing property `true`, no other properties | all allowed |

- **Validity** (§3): a value whose rich text holds a heading or quote while `headings` is `false`, an image while `images` is `false`, or a table while `tables` is `false` is invalid with `format_not_allowed` (`format`: `headings`, `images` or `tables`; reported once per field and format).
- **Changes** (§7): a group from allowed to not allowed is **reported** (tightened: values kept, counted as needing attention). From not allowed to allowed, or removing `formats`, is **free**.
- **`richText → text`** (§7): headings and quote paragraphs become lines; each table row becomes a line of its cells' text joined by single spaces; images are dropped.

## 7. Golden examples to add

Page schema `examples/`:
- **Valid**:
  - `rich-text-every-format.json`;
  - `rich-text-limits.json` (50 images, 20 tables, one of 200 × 20);
  - `rich-text-image-without-alt.json` (valid: the publish rule is not a validity rule).
- **Invalid**:
  - `props_invalid--rich-text-heading-level-1.json` and `…-heading-level-5.json`;
  - `…-align-justify.json`;
  - `…-paragraph-style.json`;
  - `…-table-in-list.json`;
  - `…-image-in-list.json` and `…-image-in-cell.json`;
  - `…-quote-in-quote.json`;
  - `…-cell-colspan.json`;
  - `…-image-src.json`;
  - `…-image-media-not-uuid.json`;
  - `…-seven-marks.json`;
  - `rich_text_too_many_images--51.json`;
  - `rich_text_too_many_tables--21.json`;
  - `table_not_rectangular.json`;
  - `table_header_misplaced.json`.

Collection schema `examples/items/`:
- `schema.json` gains a `richText` field with `formats` (`headings: false`, `tables: false`);
- `rich-text-format-not-allowed.json` gives `format_not_allowed` twice, for headings and tables;
- `rich-text-image-without-alt.json` gives `image_alt_missing` with its `path`.
