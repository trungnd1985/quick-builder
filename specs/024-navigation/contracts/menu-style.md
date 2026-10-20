# Contract addition: Menu style (design tokens v1, and its CSS)

**Feature**: `024-navigation` | **Spec**: FR-017a to FR-017d | **Research**: NV-25 to NV-30

This is the text to merge into [docs/contracts/design-tokens.md](../../../docs/contracts/design-tokens.md)
§2, and into `design-tokens/v1.schema.json`, as an additive change to version 1. It also fixes
the CSS that the token set and the Navigation block produce, which `TokenCss.cs` and
`tokenCss.ts` must emit identically.

## 1. The `menuStyle` field

Optional. Absent: every value below takes its built-in default, and menus look as they did
before this addition.

```json
{
  "menuStyle": {
    "look": "pill",
    "colors": {
      "background": "color-text",
      "text": "color-background",
      "hoverText": "color-background",
      "hoverBackground": "color-brand",
      "currentText": "color-background",
      "currentBackground": "color-brand",
      "focus": "color-brand-light",
      "panelBackground": "color-text"
    }
  }
}
```

| Field | Rule |
|---|---|
| `menuStyle` | Optional object; no other keys than `look` and `colors`. |
| `look` | Optional; `link`, `underline`, `pill` or `button`. Absent: `link`. |
| `colors` | Optional object; only the eight keys above, each optional. |
| a colour value | A token id (`^[a-z0-9-]{1,40}$`). It **may** name a token that does not exist: it then falls back as below. There is never a dangling-reference error (design-tokens.md §2). |

No new check is added to the rule order of design-tokens.md §2; the schema covers it (rule 4,
`editor.invalid_tokens`).

Schema (`$defs/menuStyle`, referenced as an optional top-level property):

```json
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "look": { "enum": ["link", "underline", "pill", "button"] },
    "colors": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "background":        { "$ref": "#/$defs/tokenId" },
        "text":              { "$ref": "#/$defs/tokenId" },
        "hoverText":         { "$ref": "#/$defs/tokenId" },
        "hoverBackground":   { "$ref": "#/$defs/tokenId" },
        "currentText":       { "$ref": "#/$defs/tokenId" },
        "currentBackground": { "$ref": "#/$defs/tokenId" },
        "focus":             { "$ref": "#/$defs/tokenId" },
        "panelBackground":   { "$ref": "#/$defs/tokenId" }
      }
    }
  }
}
```

### Golden examples

- **Valid**: `valid/menu-style.json` (the `pill` look and all eight colours) and
  `valid/menu-style-unknown-token.json` (an id that is not in the set: valid); every existing
  example stays valid unchanged (no `menuStyle`).
- **Invalid** (`editor.invalid_tokens`): `invalid/editor.invalid_tokens--menu-style-look.json`
  (`/menuStyle/look`), `--menu-style-color-key.json` (`/menuStyle/colors/foreground`),
  `--menu-style-bad-id.json` (`/menuStyle/colors/text`) and `--menu-style-extra-key.json`
  (`/menuStyle/fonts`).

## 2. Token CSS

After the existing custom properties, the token CSS emits a `--qb-menu-*` property **only for
each colour the site chose that names a token of its set**, as `var(--qb-color-<id>)`, in this
order: `--qb-menu-background`, `-text`, `-hover-text`, `-current-text`, `-focus`,
`-panel-background`, `-hover-background`, `-current-background`. A colour naming no token is
left out, as if the key were absent.

The built-in defaults live in the stylesheet, as the last fallback of each `var()` (§3):

| Colour | Built-in default |
|---|---|
| background | `transparent` |
| text | `var(--qb-color-color-text)` |
| hover text | by look: `var(--qb-color-color-text)` for pill and button, `var(--qb-color-color-brand)` for link and underline |
| current text | as hover text |
| focus | the current text colour (the block's, then the site's, then the default) |
| panel background | `var(--qb-color-color-background)` |
| hover and current backgrounds | by look: `var(--qb-color-color-brand-light)` for pill and button, `transparent` for link and underline |

The look-dependent defaults are set on the `nav` by its look class, as `--qb-nav-look-background`
and `--qb-nav-look-text`. Pill and button use dark text because the default brand colour on
the light brand colour is 4.24:1, below the 4.5 minimum of §4: the built-in style of every look
passes its own contrast checks.

So a site without a `menuStyle` gets no new custom property, and every page's token CSS is
byte-identical to before this addition (research NV-27).

A new fixture, `tests/fixtures/rendering/token-css/menu-style.json`, pins this output for both
implementations.

## 3. Block CSS (`blocks.css`, inside `@layer qb-blocks`)

The renderer writes, in the block's own style rule, only the `--qb-nav-*` properties whose
props are present. This table is the one mapping of names between the block, the token set
and the CSS (data-model.md §2.3 repeats it):

| Block prop | Token-set key (`menuStyle.colors`) | Block variable | Site variable |
|---|---|---|---|
| `color` | `text` | `--qb-nav-text` | `--qb-menu-text` |
| `activeColor` | `currentText` | `--qb-nav-current-text` | `--qb-menu-current-text` |
| `background` | `background` | `--qb-nav-background` | `--qb-menu-background` |
| `hoverColor` | `hoverText` | `--qb-nav-hover-text` | `--qb-menu-hover-text` |
| `hoverBackground` | `hoverBackground` | `--qb-nav-hover-background` | `--qb-menu-hover-background` |
| `activeBackground` | `currentBackground` | `--qb-nav-current-background` | `--qb-menu-current-background` |
| `focusColor` | `focus` | `--qb-nav-focus` | `--qb-menu-focus` |
| `panelBackground` | `panelBackground` | `--qb-nav-panel-background` | `--qb-menu-panel-background` |

Each pair shares its suffix, and the stylesheet reads each as
`var(--qb-nav-X, var(--qb-menu-X, <built-in default>))` (§2), so an absent prop follows the
site, and an absent site colour the default. The block keeps the prop names `color` and `activeColor`
it already has; the earlier variables `--qb-nav-color` and `--qb-nav-active` are renamed to
`--qb-nav-text` and `--qb-nav-current-text`. Both exist only in this unreleased feature, so
no stored document changes; only golden HTML does.

**Focus** falls back to the block's current-entry colour before the site's:
`outline: 2px solid var(--qb-nav-focus, var(--qb-nav-current-text, var(--qb-menu-focus, var(--qb-menu-current-text, var(--qb-color-color-brand)))))`. A
block that sets only `activeColor` keeps a focus ring in that colour, as before this addition.

The `nav` carries `qb-nav--look-<look>` (research NV-28). Each look styles the **top-level**
entries (`.qb-nav__list--l1 > .qb-nav__item > :is(.qb-nav__link, .qb-nav__text, .qb-nav__expander--label)`);
submenu entries keep the list look with the same colours.

| Look | Rest | Hover and focus | Current, containing, section |
|---|---|---|---|
| `link` | text colour | hover text, underlined on hover (as before this addition, so hover is not shown by colour alone) | current text, weight 600 on the current entry |
| `underline` | text colour | hover text, underlined | current text, underlined |
| `pill` | text colour, radius 999px | hover text on hover background (default `color-brand-light`) | current text on current background (default as hover) |
| `button` | text colour, 1px `currentColor` border, radius 4px | hover text on hover background (default `color-brand-light`) | current text on current background (default as hover) |

For every look:
- `:visited` uses exactly the colours of the unvisited state (FR-017c);
- `:focus-visible` on links and `summary` draws `outline: 2px solid` the focus colour with
  `outline-offset: 2px`; no prop or token removes it (FR-017d, NV-30);
- the bar (`nav`) has the background colour; horizontal dropdowns and the opened
  small-screen panel have the panel background.

With no `menuStyle` and no style props, a Navigation block renders as before this addition,
apart from the `qb-nav--look-link` class.

## 4. Contrast warnings (admin only, FR-017d, NV-29)

Pairs checked, with WCAG 2.x contrast ratios. Colours with alpha, and `transparent`, are
composited over the bar's background, and the bar's over the site's `color-background`:

The effective colours are resolved before checking: the block's override, else the site
value, else the built-in default **for the effective look**. The hover and current backgrounds
default to `color-brand-light` for pill and button, and to `transparent` (so the bar's
background) for link and underline. Inside a horizontal dropdown or the small-screen panel,
entries sit on the panel background; the hover and current pairs are checked over the bar's
background only, a known limit of the warning (the published colours are unaffected).

| Pair | Minimum |
|---|---|
| text on background | 4.5 |
| hover text on hover background | 4.5 |
| current text on current background | 4.5 |
| text on panel background | 4.5 |
| focus on background | 3 |

A failing pair is named in a warning beside the colour controls, in the Design view's Menus
section (site values) and in the Navigation block's panel (effective values). Warnings never
block saving or publishing.
