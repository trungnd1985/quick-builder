# Contract draft: the `navigation` node type (page schema v1, additive)

Draft of the additions to `docs/contracts/page-schema.md` (§2 table and placement, §6
"Additions in 024") and `page-schema/v1.schema.json` (`$defs/navigation`,
`$defs/navCollapse`, `contentChild`). Decisions: research NV-05, NV-12 to NV-15.

## 1. §2 row

| Type | Props | Children | Allowed in |
|---|---|---|---|
| `navigation` | `menuId`\*, `layout`, `levels`, `collapse`, `align`, `ariaLabel`, `color`, `activeColor`, `layerLabel` | none | in `section` or `container` (024), in page and component documents |

## 2. Props

| Prop | Schema | Default | Meaning |
|---|---|---|---|
| `menuId` | UUID | required | A menu of the document's site. **May name a menu that no longer exists**; the editor marks it and publishing refuses it (024 FR-003a). |
| `layout` | `"horizontal"` \| `"vertical"` | `"horizontal"` | Entries in a row with dropdowns, or stacked with inline submenus. |
| `levels` | integer 1–3 | `3` | How many levels are shown. |
| `collapse` | `navCollapse` | `{ "enabled": false, "below": "mobile" }` | Hide behind a menu button below a preview width. |
| `align` | `"start"` \| `"center"` \| `"end"` | `"start"` | Placement of the top level. |
| `ariaLabel` | string ≤ 100, no control characters | the live menu's name (its name at last publish) | The accessible name of the navigation landmark. |
| `color` | `colorRef` | text colour token | Entry text. |
| `activeColor` | `colorRef` | brand colour token | Current, containing and section entries (024 FR-030). |
| `layerLabel` | as every block | — | Editor aid, never rendered. |

`$defs/navCollapse`:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["enabled", "below"],
  "properties": {
    "enabled": { "type": "boolean" },
    "below": { "enum": ["mobile", "tablet"] },
    "buttonLabel": { "type": "string", "maxLength": 40, "pattern": "^[^\\u0000-\\u001F\\u007F]*$" }
  }
}
```

`below` names the editor's preview widths, which are the stylesheet's container breakpoints:
`mobile` = 480 px, `tablet` = 767 px (011 FR-036, research ED-09). `buttonLabel` defaults to the
translated "Menu" of the site's default language.

## 3. Rules

No new document rule. In particular, `menuId` is **not** checked against the site when a draft
is saved, unlike `componentId` (rule 6), so deleting a menu never makes a draft unsaveable.
Publishing checks it (024 data-model §4.2).

`navigation` is not bindable: binding-expression.md §4 is unchanged.

## 4. §6 entry

**Additions in 024 (still version 1)**: the node type `navigation` in `contentChild`, with
`$defs/navigation` and `$defs/navCollapse`. Every document valid before stays valid and renders
the same (024 `UpgradeFrom017Tests`). Menus themselves have their own contract,
[menu.md](menu.md).

## 5. Golden examples

`valid/navigation-minimal.json` (only `menuId`), `valid/navigation-full.json` (every prop),
`valid/component-with-navigation.json`.
`invalid/navigation-levels-4.json`, `invalid/navigation-collapse-below-desktop.json`,
`invalid/navigation-children.json` (`children_not_allowed`),
`invalid/navigation-under-root.json` (`nesting_not_allowed`),
`invalid/navigation-label-control-char.json`.
