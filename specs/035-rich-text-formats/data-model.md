# Data Model: Richer Rich Text Formats

**Feature**: 035 | **Research**: [research.md](./research.md) | **Contracts**: [contracts/](./contracts/)

## 1. Storage

**No migration.** Rich text stays inside the documents that hold it today. The new derived rows reuse existing tables.

| Where rich text lives | Column | Change |
|---|---|---|
| Rich Text block `props.content` in page and component drafts and versions | `pages.page_drafts`, `pages.component_drafts`, `pages.published_versions` | may hold the new nodes (contract delta §2) |
| `richText` item values | `cms.items.draft` and `published`, `item_versions.values`, `item_schedules.captured_values` | same |
| `richText` field settings | `cms.collections.schema` | optional `formats` (contract delta §6) |

| Derived row | Table | Source after this feature |
|---|---|---|
| Draft media used by a page or component | `pages.draft_media_references` | Image blocks **and rich text images** (`DependencyExtractor.Media`, RT-11) |
| Version media | `pages.version_media_references` | same, at publish |
| Render dependencies (`media` edges) | `pages.live_dependencies` | same |
| Item media use | `cms.item_media` | image and file fields **and images inside rich text fields** (`ItemIndexer`, RT-11) |
| Item attention | `cms.items.attention` | + `format_not_allowed`, `image_alt_missing` |

**No backfill**: no stored rich text can contain an image or a disallowed format before this release.

## 2. Entities

### Rich text document

The contract's `rtDoc` ([contracts/rich-text.md §2](./contracts/rich-text.md)).

| Element | Allowed where | Attributes | Limits |
|---|---|---|---|
| paragraph | top level, list item, quote, table cell | `align?` | 1,000 inline nodes |
| heading | top level | `level` 2–4, `align?` | 1,000 inline nodes |
| blockquote | top level | — | 1–200 paragraphs or lists |
| bulletList, orderedList, listItem | top level, list item, quote | `start?` (ordered) | unchanged |
| image | top level | `media`, `alt?` (≤ 300), `decorative?` | ≤ 50 per rich text |
| table → tableRow → tableCell / tableHeader | top level | — | ≤ 20 per rich text; 1–200 rows; 1–20 cells; rectangular; header cells in row 1 only |
| text marks | anywhere text is | bold, italic, underline, strike, code, link | ≤ 6 per text node |
| text | | | 20,000 characters in total (rule 9) |

### Rich text image

A reference to a media file, `{ "mediaId": uuid }`.

- It may name a file that is missing or belongs to another site. Both are treated as missing: never rendered, shown as missing in the editor, `media_missing` for items, and never counted as usage.
- **Publishable** when `alt` has a non-blank value or `decorative` is `true`.

### Allowed formats setting

On a collection's `richText` field: `formats: { headings?, images?, tables? }`, where each missing property means `true`.

## 3. Rules and states

**Validity of a rich text value or block**: schema (§2), then rules 9–12 in order (contract delta §3). An invalid document is never stored.

**Publishability**, checked at publish only:

| Subject | Check | Outcome |
|---|---|---|
| Page or component | every rich text image has alt or decorative | `pages.image_alt_missing` (422), listing pointers |
| Item | the same, per rich text field | `image_alt_missing` problem with `path`; attention; publish refused |
| Item | each rich text field's value uses only allowed groups | `format_not_allowed` problem with `format`; attention; publish refused |

**Item attention after a schema change**: narrowing `formats` is a *reported* change (014 §7). The 014 migrator re-indexes affected items, which gain `format_not_allowed`. Widening clears it on re-indexing.

**Outdated admin** (RT-13):

| Request carries `X-QB-Rich-Text: 2` | Stored draft uses 035 formats | Result |
|---|---|---|
| yes | any | normal |
| no | no | normal |
| no | yes | 409 `pages.admin_outdated` or `cms.admin_outdated`, nothing written |

"Uses 035 formats" means any `heading`, `blockquote`, `image` or `table` node, any `align` attribute, or any `underline`, `strike` or `code` mark, in any rich text of the draft (`RichTextRules.UsesExtendedFormats`).

## 4. Problem codes added

| Code | Raised by | Pointer or field | Stored? |
|---|---|---|---|
| `rich_text_too_many_images` | page and item validators | rich text | no (invalid) |
| `rich_text_too_many_tables` | page and item validators | rich text | no |
| `table_not_rectangular` | page and item validators | row | no |
| `table_header_misplaced` | page and item validators | cell | no |
| `format_not_allowed` | item validation | field, plus `format` | yes, needs attention |
| `image_alt_missing` | item validation | field, plus `path` | yes, needs attention |
| `pages.image_alt_missing` | page publish | `problems[].pointer` | — (publish refused) |
| `pages.admin_outdated`, `cms.admin_outdated` | page and component draft saves, item draft save | — | — (write refused) |

For an item value, the page validator's codes surface as 015's "malformed value" problem with the rule name (015 IT-05), as rule 9 (`rich_text_too_long`) does today.

## 5. Events

No new events. Page publish and item publish events are unchanged. Media re-render and re-index flow through the existing `Media.FileChanged` and `Media.FileDeleted` handlers, because rich text images join the existing reference sets (RT-11).
