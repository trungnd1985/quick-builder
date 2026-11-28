# Data Model: Border Styles for All Blocks

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

No database table, column or migration changes. Everything below lives inside two JSON
documents that are already stored as `jsonb`: page and component documents (`pages` schema)
and design token sets (`editor` schema). Both stay at `schemaVersion` 1.

## 1. Border setting (page and component documents)

An optional prop `border` on a block's `props`.

| Field | Type | Required | Default | Rule |
|---|---|---|---|---|
| `width` | enum `none` \| `thin` \| `medium` \| `thick` | no | `none` | Fixed widths: 1, 2, 4 px |
| `style` | enum `solid` \| `dashed` \| `dotted` | no | `solid` | Used only when width ≠ `none` |
| `color` | `colorRef` `{ token }` | no | `{ "token": "color-border" }` | Used only when width ≠ `none` |
| `radius` | `radiusRef` `{ token }` | no | none: the block's own corners | Any token id; one that doesn't exist counts as absent |
| `padding` | `spaceRef` `{ token }` | no | none: the block's own spacing | **Not on Section and Container** (`borderBox`) |

No other keys (`additionalProperties: false`). It is never bindable (binding-expression.md §4
is unchanged).

**Which blocks** (`$ref`):

| `$defs/border` | `$defs/borderBox` |
|---|---|
| heading, text, rich-text, image, button, spacer, divider, embed, navigation, form, collection-list, list-card, component-ref | section, container |

`list-empty` and form fields get neither (spec FR-001).

**Lifecycle**: a draft prop like any other. It is set and cleared through the property panel,
which stores `undefined` (removes the prop) for a value that changes nothing (research BB-08).
It reaches visitors on publish. It is copied with its block and carried by export and import.

## 2. Radius token (design token set)

An optional list `radii` on the token set.

| Field | Type | Rule |
|---|---|---|
| `id` | `tokenId` `^[a-z0-9-]{1,40}$` | Unique across `colors`, `typography.sizes`, `spacing` **and `radii`**. Never changes |
| `name` | 1–40 characters | Unique within `radii`, ignoring case |
| `rem` | number, 0 to 100 | Multiples of the base text size. 100 means fully rounded |

The list has 1 to 12 entries when present. When absent, the default list applies:

| `id` | `name` | `rem` |
|---|---|---|
| `radius-none` | None | 0 |
| `radius-sm` | Small | 0.25 |
| `radius-md` | Medium | 0.5 |
| `radius-lg` | Large | 1 |
| `radius-full` | Full | 100 |

The default ids are fixed, like the other default ids, and always emitted as CSS so that a
reference always resolves.

**Lifecycle**: like every token, it is a draft setting.
- Saving needs `editor.design_tokens.manage` (Designer). Publishing the design needs the
  publish capability.
- Deleting a radius token leaves documents valid; blocks that used it show their own corners.

## 3. Relationships

```text
Design token set ──< colours ─────────┐
                 ──< spacing ─────────┤ referred to by id from
                 ──< radii (new) ─────┤
                                      ▼
Page / component document ──< block ── border { width, style, color→colour,
                                                radius→radius, padding→spacing }
```

A reference never fails validation because its token is missing (page-schema.md §2a). Only
the shape is checked.

## 4. Validation summary

| Check | Where | Error |
|---|---|---|
| Unknown `width` or `style`, extra key, non-object ref | page schema (both validators) | `props_invalid` at the key |
| `padding` on Section or Container | page schema (`borderBox`) | `props_invalid` at `…/border/padding` |
| `radii` empty | token check 3 | `editor.token_required` |
| `radii` entry shape, `rem` out of range, more than 12 | token schema | `editor.invalid_tokens` |
| Radius id shared with another list | token check 5 | `editor.duplicate_token_id` |
| Radius name repeated | token check 6 | `editor.duplicate_token_name` |
