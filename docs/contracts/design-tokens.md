# Contract: Design Tokens, Version 1

**Audience**: anyone who reads or writes a site's design tokens: the editor (#11), which
edits them and draws the canvas with them, and the renderer (#12), which turns them into
CSS for visitors.

**Introduced by**: [specs/011-editor-core](../../specs/011-editor-core/) (research ED-14 and
ED-17).

This document and [`design-tokens/v1.schema.json`](design-tokens/v1.schema.json) together
are the contract. The schema file is the single definition of a valid token set:
- the Editor module embeds this exact file and validates against it;
- the editor validates against it too.

`DesignTokenContractTests` fails the build when this document, the schema and the code
disagree. Changing either needs this document, the schema file and, for a new version, the
forward migration of stored token sets, all in the same pull request (constitution,
Development Workflow).

---

## 1. What a token set is

One JSON document per site: the site's named colours, typography, spacing scale and corner
radii.

Page and component documents refer to tokens by **id**
(`{ "token": "color-brand" }`, [page-schema.md](page-schema.md) §2a), never by value. So
changing a token's value changes every block that uses it, and renaming it breaks nothing.

```json
{
  "schemaVersion": 1,
  "colors": [ { "id": "color-brand", "name": "Brand", "value": "#2563eb" } ],
  "typography": {
    "headingFont": "system-ui",
    "bodyFont": "system-ui",
    "baseSizePx": 16,
    "sizes": [ { "id": "text-md", "name": "Normal", "rem": 1 } ]
  },
  "spacing": [ { "id": "space-md", "name": "Medium", "rem": 1 } ],
  "radii": [ { "id": "radius-md", "name": "Medium", "rem": 0.5 } ],
  "contentWidths": { "narrow": 720, "normal": 1200, "wide": 1440 }
}
```

A site that has never saved a token set uses the **default set**
([`design-tokens/examples/valid/default.json`](design-tokens/examples/valid/default.json)).
Its ids are fixed, because the page schema's property defaults name them (`color-text`,
`space-md` and so on).

Token sets are **draft settings** in this release. Changing them changes nothing visitors
see; publishing (#12) decides how they reach published pages.

## 2. Rules

| Field | Rule |
|---|---|
| `schemaVersion` | `1`. |
| `colors` | 1–24 entries of `{ id, name, value }`. `value` is `#rrggbb` or `#rrggbbaa`, lowercase. |
| `typography.headingFont`, `bodyFont` | A font stack id from §3. |
| `typography.baseSizePx` | An integer from 12 to 24. `1rem` equals this. |
| `typography.sizes` | 1–12 entries of `{ id, name, rem }`, with `rem` from 0.5 to 6. |
| `spacing` | 1–12 entries of `{ id, name, rem }`, with `rem` from 0 to 16. |
| `radii` | Optional (added by 066, still version 1). 1–12 entries of `{ id, name, rem }`, with `rem` from 0 to 100: the corner radii that block borders refer to ([page-schema.md](page-schema.md) §2a). 100 is fully rounded, because CSS reduces a radius that doesn't fit its box. When it is absent, the default set's radii apply. |
| `contentWidths` | Optional. `{ narrow, normal, wide }`, each an integer from 320 to 2560 (CSS pixels), with narrow ≤ normal ≤ wide. All three are always present: none can be added or deleted. When it is absent, the default set's widths apply. |
| `menuStyle` | Optional (added by 024, still version 1). `{ look?, colors? }`: `look` is `link`, `underline`, `pill` or `button`; `colors` holds any of `background`, `text`, `hoverText`, `hoverBackground`, `currentText`, `currentBackground`, `focus` and `panelBackground`, each a colour token **id**. No other keys. An id that names no token is valid and falls back like any token reference (below). Absent keys, or no `menuStyle`, take the built-in defaults, so menus look as they did before it existed. See [specs/024-navigation/contracts/menu-style.md](../../specs/024-navigation/contracts/menu-style.md). |
| `id` | Matches `^[a-z0-9-]{1,40}$`. **Unique across all four lists** (`colors`, `typography.sizes`, `spacing`, `radii`). It never changes; a new entry gets a new id. |
| `name` | 1–40 characters, unique within its list ignoring case. Display only. |
| Size | At most 64 KiB of UTF-8 JSON. |

Checks run in this order. The first failure is reported with an error code and, where
there is one, a JSON `pointer`:

1. size (`editor.tokens_too_large`);
2. `schemaVersion` supported (`editor.unsupported_schema_version`);
3. no empty list (`editor.token_required`), `radii` included when it is present. Checked
   before the schema, so that deleting the last entry of a kind gets its own reason;
4. the schema (`editor.invalid_tokens`);
5. ids unique across lists (`editor.duplicate_token_id`, at the second occurrence);
6. names unique within a list, ignoring case (`editor.duplicate_token_name`, at the second
   occurrence);
7. content widths in order, narrow ≤ normal ≤ wide (`editor.content_widths_order`, at
   `/contentWidths`).

**Deleting** a token means leaving it out. Documents that still refer to it stay valid, and
consumers use the referring property's schema `default` (page-schema.md §2a). There is
never a dangling-reference error.

**Fallbacks always resolve**: consumers always define every id of the default set. An id
missing from the site's set takes its value from the default set. So a property whose token
and default token were both deleted still renders.

**Content widths** give the Narrow, Normal and Wide widths of Sections and Containers their
size ([page-schema.md](page-schema.md) §2). They are not tokens: they have no id, and
documents name them by width (`narrow`, `normal`, `wide`). A token set saved before content
widths existed has no `contentWidths`. It stays valid, and consumers use the default set's
widths: 720, 1200 and 1440.

**Corner radii** (066) work the same way. A token set saved before they existed has no
`radii`. It stays valid, and consumers use the default set's five radii: `radius-none` (0),
`radius-sm` (0.25), `radius-md` (0.5), `radius-lg` (1) and `radius-full` (100). Their ids
are fixed like the other default ids. A radius that a block names but the design lacks is
**not** replaced by a default: the block keeps its own corners (page-schema.md §2a).

## 3. Font stacks

Fonts are named system font stacks, from the Modern Font Stacks project (CC0). The product
downloads nothing (constitution Principle II).

| Id | CSS `font-family` |
|---|---|
| `system-ui` | `system-ui, sans-serif` |
| `transitional` | `Charter, 'Bitstream Charter', 'Sitka Text', Cambria, serif` |
| `old-style` | `'Iowan Old Style', 'Palatino Linotype', 'URW Palladio L', P052, serif` |
| `humanist` | `Seravek, 'Gill Sans Nova', Ubuntu, Calibri, 'DejaVu Sans', source-sans-pro, sans-serif` |
| `geometric-humanist` | `Avenir, Montserrat, Corbel, 'URW Gothic', source-sans-pro, sans-serif` |
| `classical-humanist` | `Optima, Candara, 'Noto Sans', source-sans-pro, sans-serif` |
| `neo-grotesque` | `Inter, Roboto, 'Helvetica Neue', 'Arial Nova', 'Nimbus Sans', Arial, sans-serif` |
| `monospace-slab` | `'Nimbus Mono PS', 'Courier New', monospace` |
| `monospace-code` | `ui-monospace, 'Cascadia Code', 'Source Code Pro', Menlo, Consolas, 'DejaVu Sans Mono', monospace` |
| `industrial` | `Bahnschrift, 'DIN Alternate', 'Franklin Gothic Medium', 'Nimbus Sans Narrow', sans-serif-condensed, sans-serif` |
| `rounded-sans` | `ui-rounded, 'Hiragino Maru Gothic ProN', Quicksand, Comfortaa, Manjari, 'Arial Rounded MT', 'Arial Rounded MT Bold', Calibri, source-sans-pro, sans-serif` |
| `slab-serif` | `Rockwell, 'Rockwell Nova', 'Roboto Slab', 'DejaVu Serif', 'Sitka Small', serif` |
| `antique` | `Superclarendon, 'Bookman Old Style', 'URW Bookman', 'URW Bookman L', 'Georgia Pro', Georgia, serif` |
| `didone` | `Didot, 'Bodoni MT', 'Noto Serif Display', 'URW Palladio L', P052, Sylfaen, serif` |
| `handwritten` | `'Segoe Print', 'Bradley Hand', Chilanka, TSCu_Comic, casual, cursive` |

- **Adding** a stack is additive.
- **Removing** one is breaking (a new version plus a migration), because stored token sets
  name it.

## 4. Versioning

The same policy as the page schema (page-schema.md §6):
- additive changes stay in version 1;
- breaking changes need `v2.schema.json`, this document updated, and a forward migration of
  stored token sets.

The schema uses only the page schema's keyword subset (page-schema.md §7), so both are
validated the same way.

## 5. Examples (golden files)

Every validator of this schema runs the same files:

- `design-tokens/examples/valid/*.json` must pass.
- `design-tokens/examples/invalid/<code>--<case>.json` must fail with the error code its
  name begins with, at the pointer given in `design-tokens/examples/invalid/expected.json`.

| File | Expected |
|---|---|
| `valid/default.json` | passes: the default set |
| `valid/full.json` | passes: 24 colours, 12 sizes, 12 spacing steps, the extreme content widths |
| `valid/menu-style.json` | passes: a menu style with the pill look and all eight colours (024) |
| `valid/menu-style-unknown-token.json` | passes: a menu colour naming no token falls back (024) |
| `valid/with-radii.json` | passes: three custom corner radii (066) |
| `invalid/editor.invalid_tokens--bad-colour.json` | at `/colors/0/value` |
| `invalid/editor.invalid_tokens--uppercase-colour.json` | at `/colors/0/value` |
| `invalid/editor.invalid_tokens--25-colours.json` | at `/colors` |
| `invalid/editor.invalid_tokens--unknown-font.json` | at `/typography/bodyFont` |
| `invalid/editor.invalid_tokens--base-size-30.json` | at `/typography/baseSizePx` |
| `invalid/editor.invalid_tokens--bad-id.json` | at `/spacing/0/id` |
| `invalid/editor.duplicate_token_id--across-lists.json` | at `/spacing/3/id` |
| `invalid/editor.duplicate_token_name--case.json` | at `/colors/1/name` |
| `invalid/editor.token_required--empty-spacing.json` | at `/spacing` |
| `invalid/editor.unsupported_schema_version--version-2.json` | at `/schemaVersion` |
| `invalid/editor.invalid_tokens--wide-3000.json` | at `/contentWidths/wide` |
| `invalid/editor.content_widths_order--narrow-over-normal.json` | at `/contentWidths` |
| `invalid/editor.invalid_tokens--menu-style-look.json` | at `/menuStyle/look` |
| `invalid/editor.invalid_tokens--menu-style-color-key.json` | at `/menuStyle/colors/foreground` |
| `invalid/editor.invalid_tokens--menu-style-bad-id.json` | at `/menuStyle/colors/text` |
| `invalid/editor.invalid_tokens--menu-style-extra-key.json` | at `/menuStyle/fonts` |
| `invalid/editor.token_required--radii-empty.json` | at `/radii` (066) |
| `invalid/editor.duplicate_token_id--radius-shares-spacing-id.json` | at `/radii/5/id` (066) |
| `invalid/editor.duplicate_token_name--radius-case.json` | at `/radii/5/name` (066) |
| `invalid/editor.invalid_tokens--radius-over-100.json` | at `/radii/0/rem` (066) |
| `invalid/editor.invalid_tokens--13-radii.json` | at `/radii` (066) |
