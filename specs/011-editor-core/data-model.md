# Data Model: Editor Core

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

This feature adds four things:
1. **Ten block types** to the page schema version 1 (§1). There is no new table; drafts
   stay in `pages.page_drafts` and `pages.component_drafts` (010).
2. **The design token set** (§2), stored in a new `editor` schema (§3).
3. **One read-only query** over drafts, for token usage (§4).
4. **Client-side editing state** (§5), which is never stored on the server.

Error codes are listed in §6.

---

## 1. Block types (page schema v1, additive)

Every block is a node `{ id, type, props, children }` (010 contract §1). Every block's
`props` may also hold `layerLabel` (string, 1–60 characters): a name for the layer view,
never rendered to visitors. It is not called `label`, because the Button uses `label` for
its visible text. Every property is optional unless marked *(required)*. A missing
property means its `default`, which the schema holds (ED-07).

### 1.1 Shared value shapes (`$defs`)

| Def | Shape | Rules |
|---|---|---|
| `colorRef` | `{ "token": tokenId }` | `tokenId` matches `^[a-z0-9-]{1,40}$`. It may name a token that no longer exists; the fallback is the property's `default` (ED-06). |
| `spaceRef` | `{ "token": tokenId }` | As above, for a spacing step. |
| `textSizeRef` | `{ "token": tokenId }` | As above, for a text-size step. |
| `link` | `{ "type": "page", "pageId": uuid }` or `{ "type": "url", "href": string }` | `href`: at most 2,048 characters, matching `^(https?://\S+\|mailto:\S+\|tel:[+0-9 ().-]+)$`. A `pageId` need not exist. |
| `align` | `"start"`, `"center"`, `"end"` | Defaults to `start`. |
| `richText` | a ProseMirror `doc` (below) | At most 20,000 characters of text in total (`rich_text_too_long`, checked in code). |

**`richText`** (ED-06). Every `oneOf` is told apart by `type`.

```text
doc        { type: "doc", content: block[] }                       # content maxItems 500
block      paragraph | bulletList | orderedList
paragraph  { type: "paragraph", content?: inline[] }               # maxItems 1000
bulletList { type: "bulletList",  content: listItem[] }            # minItems 1, maxItems 200
orderedList{ type: "orderedList", content: listItem[], attrs?: { start: integer 1..9999 } }
listItem   { type: "listItem", content: block[] }                  # minItems 1
inline     text | hardBreak
text       { type: "text", text: string (1..20000), marks?: mark[] }   # maxItems 3
hardBreak  { type: "hardBreak" }
mark       { type: "bold" } | { type: "italic" } | { type: "link", attrs: { to: link } }
```

List nesting is bounded by the document's JSON depth cap and the 1 MiB size limit. The
editor limits it to 4 levels.

### 1.2 Block definitions

| Type | Children | Props (type, allowed values, **default**) |
|---|---|---|
| `section` | `contentChild[]` | `background`: `colorRef`, **none** (transparent). `paddingY`: `spaceRef`, **`space-xl`**. `width`: `"narrow"`, `"contained"` (shown as Normal), `"wide"` or `"full"`, **`contained`**. Narrow, Normal and Wide cap the content at the site's content width and centre it; Full spans the page. |
| `container` | `contentChild[]` | `direction`: `"vertical"` or `"horizontal"`, **`vertical`**. `columns`: `"equal"`, `"2-1"`, `"1-2"`, `"3-1"`, `"1-3"` or `"2-1-1"`, **`equal`**; horizontal only; blocks beyond the pattern start a new row; kept at tablet width, stacked at mobile width when `stackOnMobile`. `gap`: `spaceRef`, **`space-md`**. `padding`: `spaceRef`, **`space-none`**. `align`: `align`, **`start`**. `maxWidth`: `"narrow"`, `"normal"`, `"wide"` or `"full"`, **`full`**; caps the Container itself, placed by `align`, never wider than its parent. `background`: `colorRef`, **none**. `stackOnMobile`: boolean, **true**. |
| `heading` | none | `text` *(required)*: string, 0–300 characters, no line breaks. `level`: integer 1–6, **2**. `align`: `align`. `color`: `colorRef`, **`color-text`**. |
| `text` | none | `text` *(required)*: string, 0–5,000 characters; line breaks allowed. `align`: `align`. `color`: `colorRef`, **`color-text`**. `size`: `textSizeRef`, **`text-md`**. |
| `rich-text` | none | `content` *(required)*: `richText`. `align`: `align`. `color`: `colorRef`, **`color-text`**. |
| `image` | none | `media`: `{ "mediaId": uuid }`, **none** (a placeholder in the canvas). `alt`: string, 0–300 characters. `decorative`: boolean, **false**. `width`: `"full"` or `"natural"`, **`full`**. `link`: `link`, **none**. |
| `button` | none | `label` *(required)*: string, 1–100 characters. `link`: `link`, **none**. `style`: `"primary"`, `"secondary"` or `"outline"`, **`primary`**. `align`: `align`. `newTab`: boolean, **false**. |
| `spacer` | none | `height`: `spaceRef`, **`space-lg`**. |
| `divider` | none | `color`: `colorRef`, **`color-border`**. `thickness`: `"thin"` or `"thick"`, **`thin`**. `spacing`: `spaceRef`, **`space-md`**. |
| `embed` | none | `url`: string at most 2,048 characters, matching a YouTube or Vimeo `https` pattern (ED-06), **none** (the canvas shows "choose a video"; the renderer omits the block). `title`: string, 1–200 characters, the frame's accessible name, **none** (the renderer uses a generic translated name). `aspectRatio`: `"16:9"`, `"4:3"` or `"1:1"`, **`16:9`**. Both `url` and `title` are optional, so that a newly inserted Embed is valid. |

**A newly inserted block** holds only the required properties, with the values its editor
definition's `initialProps` gives. Every other property is missing, so it takes its schema
default. The initial values:
- Heading: `text: ""`.
- Text: `text: ""`.
- Rich Text: `content: { "type": "doc", "content": [] }`.
- Button: `label` is the translated word "Button", in the member's admin language at insert
  time.

The canvas shows a translated placeholder for empty text; it is never stored.

### 1.3 Placement lists (ED-05)

| List | Members | Used by |
|---|---|---|
| `rootChild` | `section`, `component-ref` | `root.children` |
| `contentChild` | `container`, `heading`, `text`, `rich-text`, `image`, `button`, `spacer`, `divider`, `embed` | `section.children`, `container.children` |

Component documents: `root.children` holds only `section`, because `component-ref` in a
component document is already refused by §3 rule 7 (`component_ref_not_allowed`).

### 1.4 Validation order (010 contract §3, extended)

1. Size.
2. Well-formed JSON, with no duplicate properties.
3. `schemaVersion` supported.
4. Structural walk: depth, node count, unique ids. It follows `children` only, so rich
   text `content` is never counted as nodes.
5. Schema, with its rules mapped:
   - `schema_violation`;
   - `unknown_type`;
   - `root_misplaced`;
   - **`nesting_not_allowed`** (new): a known type in a list that doesn't admit it;
   - `props_invalid`: any failure under `/props`, including rich text, whatever the
     keyword. A failed `oneOf` under `/props` (for example an unknown mark) is reported at
     the failing object's `/type`;
   - `children_not_allowed`.
6. Code rules:
   - component rules 6 and 7 (010);
   - **`rich_text_too_long`** (new).

Both new rules are additive. No document that passed before fails now.

---

## 2. Design token set (`docs/contracts/design-tokens.md`, v1)

One document per site. JSON shape:

```json
{
  "schemaVersion": 1,
  "colors": [
    { "id": "color-brand", "name": "Brand", "value": "#2563eb" }
  ],
  "typography": {
    "headingFont": "system-ui",
    "bodyFont": "system-ui",
    "baseSizePx": 16,
    "sizes": [
      { "id": "text-sm", "name": "Small", "rem": 0.875 }
    ]
  },
  "spacing": [
    { "id": "space-md", "name": "Medium", "rem": 1 }
  ],
  "contentWidths": { "narrow": 720, "normal": 1200, "wide": 1440 }
}
```

| Field | Rules |
|---|---|
| `schemaVersion` | `1`. |
| `colors` | 1–24 entries. `id` matches `^[a-z0-9-]{1,40}$`. `name` is 1–40 characters, unique ignoring case. `value` is `#rrggbb` or `#rrggbbaa`, lowercase. |
| `typography.headingFont`, `bodyFont` | One of the font stack ids (ED-17). |
| `typography.baseSizePx` | An integer from 12 to 24. |
| `typography.sizes` | 1–12 entries. `id` and `name` as for colours. `rem` is a number from 0.5 to 6. |
| `spacing` | 1–12 entries. `id` and `name` as for colours. `rem` is a number from 0 to 16. |
| `contentWidths` | Optional. `narrow`, `normal` and `wide`, each an integer from 320 to 2560 (px), with narrow ≤ normal ≤ wide (`editor.content_widths_order`). Always all three; none can be added or deleted. Absent (a set saved before they existed), the default widths apply. |
| **Ids** | Unique across `colors`, `typography.sizes` and `spacing` together (ED-14). Never change once created. |

**Defaults** (the lazy default set, a constant in `Editor.Contracts`, with its copy in the
admin checked by `DefaultTokensParityTests`):

| Kind | Tokens |
|---|---|
| Colours | `color-brand` Brand `#2563eb`; `color-brand-light` Brand light `#dbeafe`; `color-text` Text `#111827`; `color-muted` Muted text `#6b7280`; `color-background` Background `#ffffff`; `color-border` Border `#e5e7eb` |
| Fonts | `system-ui` for both headings and body; base 16 px |
| Text sizes | `text-sm` 0.875; `text-md` 1; `text-lg` 1.25; `text-xl` 1.5; `text-2xl` 2; `text-3xl` 2.5 |
| Content widths | Narrow 720; Normal 1200; Wide 1440 (px) |
| Spacing | `space-none` 0; `space-xs` 0.25; `space-sm` 0.5; `space-md` 1; `space-lg` 2; `space-xl` 4; `space-2xl` 6 |

**Heading sizes**: headings map levels to text sizes by fixed rule (1 → `text-3xl` …
6 → `text-md`). The block definition documents this.

**Deleting a token**: the token set document simply no longer holds it. Blocks keep their
`{ "token": id }` and render with the property's schema default (clarification Q4). Deleting
the last colour, text size or spacing step is refused (`editor.token_required`).

**Fallbacks always resolve**: consumers (the canvas, and later the renderer) always define
every token id of the built-in default set. An id missing from the site's set takes its
built-in default value. So a property whose token *and* default token were both deleted
still renders, with the built-in value. The Heading level-to-size map resolves the same
way.

### `DesignTokenSet` aggregate (Editor module)

| Member | Rule |
|---|---|
| `SiteId` | The owning site. `ISiteOwned`, with a query filter. |
| `Document` | The validated token document above. |
| `Version` | `xmin`. A save carries the version it was based on, and a stale one gives `editor.out_of_date`. |
| `UpdatedAtUtc`, `UpdatedBy` | Set on each save. |
| `Replace(document, expectedVersion, user)` | Validates in this order: size (`editor.tokens_too_large`); version (`editor.unsupported_schema_version`); an empty `colors`, `typography.sizes` or `spacing` list (`editor.token_required`, before the schema so that deleting the last entry gets its own reason); schema (`editor.invalid_tokens`, with a pointer); ids unique across kinds (`editor.duplicate_token_id`); names unique per kind ignoring case (`editor.duplicate_token_name`); content widths in order (`editor.content_widths_order`). Ids are never edited: the admin generates an id only for a new entry and never offers to change one. A changed id is indistinguishable from a delete plus an add, and it is treated as such. |
| *(no row)* | `Read` returns the default set with `version: 0`. The first save inserts, and it succeeds only when `version` is `0`. |

There are no domain events or integration events in this feature (research ED-14).

---

## 3. Storage (`editor` schema)

### `editor.design_token_sets`

| Column | Type | Notes |
|---|---|---|
| `site_id` | `uuid` | PK. One row per site. |
| `schema_version` | `integer` | Equal to the document's `schemaVersion`. |
| `document` | `jsonb` | The token set. At most 64 KiB, checked before parsing. |
| `updated_at_utc` | `timestamptz` | |
| `updated_by` | `uuid` | An Identity user id. No FK (a cross-module reference). |
| `xmin` | system | Concurrency token. |

- **Migration**: `InitialEditor` creates the schema and the table, with its own migrations
  history table (Principle I). No data is written, because defaults are lazy (ED-14).
- **Isolation**: `ISiteOwned`, filtered, and covered by `EditorIsolationProbe`.

There are no other new tables. The `pages` schema is unchanged: block types are values
inside the existing `jsonb` documents.

---

## 4. Token usage query (Pages, read-only)

`TokenUsage(siteId, tokenId)` → `{ blockCount, documentCount }`

- **Source**: `pages.page_drafts` ∪ `pages.component_drafts`, filtered to `site_id =
  @siteId` explicitly (raw SQL through EF `SqlQuery`; Technology Constraints).
- **Counting**: `blockCount` sums, over documents, the number of nodes whose `props`
  contain `{"token": @tokenId}` at any depth. `documentCount` counts documents with at
  least one.
- `@tokenId` is validated against `^[a-z0-9-]{1,40}$` before the query runs.

---

## 5. Client-side editing state (never on the server)

| Entity | Fields | Lifetime |
|---|---|---|
| **EditingSession** (`editorStore`) | `siteId`; `kind` (page or component); `documentId`; `document` (immutable tree); `baseVersion`; `selection` (node id or null); `undo[]` and `redo[]` of `{ document, selection }`, up to 200 each; `saveState` (see below); `readOnlyReason` (`conflict`, `forbidden`, `gone`, `invalid`, `recovery` or null); `tokens` (the site's token set, read-only here) | One open document. Cleared on reload or when another document is opened. |
| **RecoveryCopy** (IndexedDB `qb-editor-recovery`) | key `userId/siteId/kind/documentId`; `document`; `baseVersion`; `savedAtUtc` | Until a save covers it, the member declines it, the member signs out, or the document is gone or no longer editable (ED-19). |
| **ClipboardEntry** (`localStorage` `qb.editor.clipboard`) | `userId`, `siteId`, `nodes[]` | Until replaced or sign-out (ED-18). |
| **PreviewWidth** (`localStorage` `qb.editor.previewWidth.<userId>`) | `desktop`, `tablet` or `mobile` | A per-member preference in that browser (FR-036). |

### Save state transitions (ED-13)

```text
saved ──change──▶ dirty ──2 s idle / Ctrl+S──▶ saving
saving ──200──▶ saved            (or dirty, if changed during the save)
saving ──network/5xx/429──▶ retrying ──backoff / "Retry now"──▶ saving
saving ──409 pages.out_of_date──▶ conflict   (read-only; "Reload latest")
saving ──403──▶ forbidden        (read-only)
saving ──404──▶ gone             (read-only)
saving ──400 invalid_document, unknown_component──▶ invalid (read-only; offending node selected)
dirty  ──pre-check fails──▶ invalid (nothing sent)
conflict / recovery ──"Reload latest"──▶ saved (history cleared, clipboard kept)
```

Every state other than `saved` triggers the leave warning (FR-030).

---

## 6. Error codes

| Code | HTTP | Where | Meaning |
|---|---|---|---|
| `pages.invalid_document` with rule `nesting_not_allowed` | 400 | save draft | A known block type is somewhere it may not be. |
| `pages.invalid_document` with rule `rich_text_too_long` | 400 | save draft | A Rich Text block holds more than 20,000 characters of text. |
| `pages.invalid_token_id` | 400 | token usage | The token id doesn't match the id grammar. |
| `editor.invalid_tokens` | 400 | save tokens | The document breaks the token schema; with `pointer`. |
| `editor.duplicate_token_id` | 400 | save tokens | Two entries share an id. |
| `editor.duplicate_token_name` | 400 | save tokens | Two entries of one kind share a name, ignoring case. |
| `editor.content_widths_order` | 400 | save tokens | The content widths are not narrow ≤ normal ≤ wide; `pointer` is `/contentWidths`. |
| `editor.token_required` | 400 | save tokens | A kind would have no entries left. |
| `editor.tokens_too_large` | 413 | save tokens | The document is over 64 KiB. |
| `editor.unsupported_schema_version` | 400 | save tokens | An unknown `schemaVersion`. |
| `editor.out_of_date` | 409 | save tokens | Another save happened since `version`. |
| `sites.not_found` | 404 | any | The site is unknown or not visible (002 rule, unchanged). |

The existing 010 codes (`pages.out_of_date`, `pages.not_found`,
`pages.component_not_found`, `pages.unknown_component` and the others) are unchanged, and
the editor maps each one to a message (FR-042).
