# Contract: Design Tokens, Version 1

**Planning copy** of `docs/contracts/design-tokens.md`, a new contract. It ships with
`docs/contracts/design-tokens/v1.schema.json` and golden examples in the same PR.

**Audience**: the editor (#11), which reads and writes token sets, and the renderer (#12),
which turns them into CSS.

**Introduced by**: specs/011-editor-core, research ED-14 and ED-17; data model §2.

---

## 1. What a token set is

One JSON document per site: the site's named colours, typography and spacing scale. Page
and component documents refer to tokens by **id** (`{ "token": "color-brand" }`, page
schema §2a), never by value. So changing a token's value changes every block that uses it,
and renaming it breaks nothing.

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
  "spacing": [ { "id": "space-md", "name": "Medium", "rem": 1 } ]
}
```

## 2. Rules

| Field | Rule |
|---|---|
| `schemaVersion` | `1`. |
| `colors` | 1–24 entries of `{ id, name, value }`. `value` is `#rrggbb` or `#rrggbbaa`, lowercase. |
| `typography.headingFont`, `bodyFont` | A font stack id from §3. |
| `typography.baseSizePx` | An integer from 12 to 24. `1rem` equals this. |
| `typography.sizes` | 1–12 entries of `{ id, name, rem }`, with `rem` from 0.5 to 6. |
| `spacing` | 1–12 entries of `{ id, name, rem }`, with `rem` from 0 to 16. |
| `id` | Matches `^[a-z0-9-]{1,40}$`. **Unique across all three lists.** It never changes; a new entry gets a new id. |
| `name` | 1–40 characters, unique within its list ignoring case. Display only. |
| Size | At most 64 KiB of UTF-8 JSON. |

Checks run in this order:
1. size;
2. `schemaVersion`;
3. no empty list (`token_required`, so that deleting the last entry has its own reason);
4. the schema;
5. unique ids across lists;
6. unique names ignoring case. The first failure is reported with a
`pointer`.

**Deleting** a token means leaving it out. Documents that still refer to it stay valid, and
consumers use the referring property's schema `default` (page schema §2a). There is never a
dangling-reference error.

**Defaults**: a site that has never saved tokens uses the default set, which lives in
`QuickBuilder.Editor.Contracts` and is listed in data-model §2. Its ids are fixed, because
the page schema's property defaults name them (`color-text`, `space-md` and so on).

## 3. Font stacks

Fonts are named system font stacks (Modern Font Stacks, CC0). The product downloads nothing
(Principle II).

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

Adding a stack is additive. Removing one is breaking (a new version plus a migration),
because stored token sets name it.

## 4. Versioning

Same policy as the page schema (§6 there): additive changes stay in v1, breaking changes
need `v2.schema.json`, this document updated, and a forward migration of stored token sets.

## 5. Golden files

`design-tokens/examples/valid/{default,full}.json` and `invalid/<rule>--<case>.json`, with
`expected.json` giving the error code and pointer. They cover:
- a bad colour value;
- 25 colours;
- an id duplicated across lists;
- a name duplicated in a different case;
- an empty `spacing` list;
- an unknown font;
- `baseSizePx` of 30;
- a bad id.

`DesignTokenContractTests` (C#) and `designTokens.spec.ts` (TS) run them all.
