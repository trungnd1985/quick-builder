# Contract Change: Block Borders and Radius Tokens

**Feature**: 066 (#66) | **Changes**: [page-schema.md](../../../docs/contracts/page-schema.md),
[design-tokens.md](../../../docs/contracts/design-tokens.md) and their schema files.

Both are **additive within version 1**. Every document and token set valid before stays
valid and renders the same (spec FR-018, FR-023). This file is the text the two contract
documents gain. It becomes normative when merged into them.

---

## 1. Page schema (`page-schema/v1.schema.json`)

### 1.1 New `$defs`

```json
"radiusRef": {
  "description": "A corner radius of the site's design tokens, by id. It may name a token that no longer exists.",
  "type": "object",
  "required": ["token"],
  "additionalProperties": false,
  "properties": { "token": { "$ref": "#/$defs/tokenId" } }
},
"borderWidth": { "enum": ["none", "thin", "medium", "thick"], "default": "none" },
"borderStyle": { "enum": ["solid", "dashed", "dotted"], "default": "solid" },
"border": {
  "description": "A block's border, corners and inner spacing (066). Absent: the block looks as it does without one.",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "width":   { "$ref": "#/$defs/borderWidth" },
    "style":   { "$ref": "#/$defs/borderStyle" },
    "color":   { "$ref": "#/$defs/colorRef", "default": { "token": "color-border" } },
    "radius":  { "$ref": "#/$defs/radiusRef" },
    "padding": { "$ref": "#/$defs/spaceRef" }
  }
},
"borderBox": {
  "description": "The border of a Section or Container: as border, without padding, which they have of their own (066 clarification Q1).",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "width":  { "$ref": "#/$defs/borderWidth" },
    "style":  { "$ref": "#/$defs/borderStyle" },
    "color":  { "$ref": "#/$defs/colorRef", "default": { "token": "color-border" } },
    "radius": { "$ref": "#/$defs/radiusRef" }
  }
}
```

`radius` and `padding` have **no `default`**: absent means the block's own look (research
BB-02).

### 1.2 New prop on 15 types

- `section`, `container`: `"border": { "$ref": "#/$defs/borderBox" }`.
- `heading`, `text`, `rich-text`, `image`, `button`, `spacer`, `divider`, `embed`,
  `navigation`, `form`, `collection-list`, `list-card`, `component-ref`:
  `"border": { "$ref": "#/$defs/border" }`.

`component-ref` keeps `componentId` as its only required prop.

### 1.3 Text for page-schema.md

- **§2 table**: `border` is added to the props of the 15 types.
- **§2a**: a new bullet.
  > **Border** (066): `{ width?, style?, color?, radius?, padding? }`. `radius` is a
  > `radiusRef`, a token reference to the site's corner radii. Section and Container take
  > `borderBox`, which has no `padding`. Absent, or with width `none` and no radius or
  > padding, a block looks as it does without one.
- **§6**: a new paragraph.
  > **Additions in 066 (still version 1)**: `$defs` `radiusRef`, `borderWidth`,
  > `borderStyle`, `border` and `borderBox`, and an optional `border` prop on every content
  > type but `list-empty`. Every document valid before stays valid and renders the same
  > (066 SC-004).
- **§9**: the rendering rules in §3 below.

## 2. Design tokens (`design-tokens/v1.schema.json`)

```json
"radii": {
  "description": "Optional (066). Corner radii; a set without it uses the default set's radii.",
  "type": "array",
  "minItems": 1,
  "maxItems": 12,
  "items": { "$ref": "#/$defs/radius" }
}
```

with

```json
"radius": {
  "type": "object",
  "required": ["id", "name", "rem"],
  "additionalProperties": false,
  "properties": {
    "id": { "$ref": "#/$defs/tokenId" },
    "name": { "$ref": "#/$defs/tokenName" },
    "rem": { "type": "number", "minimum": 0, "maximum": 100 }
  }
}
```

`$defs/tokenId`'s description becomes "Unique across colors, typography.sizes, spacing and
radii".

**Text for design-tokens.md §2**:

| Field | Rule |
|---|---|
| `radii` | Optional (066, still version 1). 1–12 entries of `{ id, name, rem }`, with `rem` from 0 to 100. 100 is fully rounded: CSS reduces a radius that doesn't fit. When it is absent, the default set's radii apply. |

- The `id` row reads "Unique across all four lists".
- Checks 3, 5 and 6 cover `radii`.
- `default.json` gains the five default radii (data-model.md §2).

## 3. Rendering (page-schema.md §9)

- **Tokens**: `--qb-radius-<id>` in `rem` (scaled like spacing), for every default radius id
  and every radius of the site's set.
- **Border classes** on the framed element (research BB-05):

  | Class | When |
  |---|---|
  | `qb-border` | the block has a `border` prop |
  | `qb-border--thin` / `--medium` / `--thick` | width is not `none` |
  | `qb-border--solid` / `--dashed` / `--dotted` | width is not `none` |
  | `qb-border--radius` | `radius` names a token of the design |
  | `qb-border--pad` | `padding` names a token of the design |

  They come after the block's own classes, in this order.
- **Per-node values**, in the node's existing `[data-qb]` rule (published) or `style`
  attribute (canvas):
  - `--qb-border-color` when width is not `none`;
  - `--qb-border-radius` with `qb-border--radius`;
  - `--qb-border-pad` with `qb-border--pad`.
- **Framed element**: the block's own element, except:
  - Image: its `img`;
  - Divider: a `div.qb-divider-frame` around the `hr`, present only when it has a `border`;
  - Component reference: a `div.qb-component-ref` around the component's content, present
    only when it has a `border`.
- **Clipping**: `qb-border--radius` clips content (`overflow: clip`) on Section, Container,
  Rich Text, Embed, List card and Component reference, never on Navigation or Form
  (research BB-06).
- **Size**: `qb-border` is `box-sizing: border-box`; a border never widens a block.

The canvas produces the same classes, wrappers and values.
