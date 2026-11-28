# Research: Border Styles for All Blocks

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #66

Decisions BB-01 to BB-14. Each gives the decision, why, and what else was considered.

---

## BB-01 One nested `border` prop, not five flat props

**Decision**: every block in FR-001 gains one optional prop, `border`, an object:

```json
{ "width": "thin", "style": "dashed", "color": { "token": "color-brand" }, "radius": { "token": "radius-md" }, "padding": { "token": "space-sm" } }
```

The schema has two shared `$defs`:
- `border` (width, style, color, radius, padding) for every block except Section and Container;
- `borderBox` (the same without `padding`) for Section and Container (clarification Q1).

Each block's `props` refers to one of them with `"border": { "$ref": "#/$defs/border" }`.

**Rationale**:
- One definition covers every block (Principle VI: "a block type is defined once"), so the
  15 block entries each gain one line.
- It is one property for the panel, undo and copy. Changing the border is one operation and
  one undo step (FR-011).
- The token usage count (`GetTokenUsage`) already searches `props.**` for `token` keys, so
  nested references are counted with no change.
- The schema subset has no `allOf`, so `borderBox` repeats four of the properties instead of
  extending `border`. That costs about 20 lines of schema, which is acceptable.

**Alternatives considered**:
- **Flat props** (`borderWidth`, `borderStyle`, …): 5 × 15 schema entries and five panel
  fields per block, and a border change becomes several operations. Rejected.
- **`padding` allowed on Section and Container too**: it would compete with their existing
  `paddingY` and `padding`. Rejected by clarification Q1.

---

## BB-02 Values and defaults

**Decision**:

| Key | Values | Schema `default` | Absent means |
|---|---|---|---|
| `width` | `none`, `thin`, `medium`, `thick` | `none` | no line |
| `style` | `solid`, `dashed`, `dotted` | `solid` | solid |
| `color` | `colorRef` | `{ "token": "color-border" }` | the site's Border colour |
| `radius` | `radiusRef` (new, same shape as `colorRef`) | none | the block's own corners |
| `padding` | `spaceRef` | none | the block's own spacing |

Fixed widths: thin 1 px, medium 2 px, thick 4 px (FR-004). They are the same for every site.

`radius` and `padding` have **no schema default**, on purpose. They follow the Navigation
style props of 024 (research NV-26): absent means "leave the block as it looks today". This
matters for the Button, whose corners are rounded at 6 px today. A default of `radius-none`
would square every button the moment it got a border. A radius or spacing token that was
deleted behaves like absent: the block shows its own look, which for every block but the
Button means no rounding and no spacing (spec FR-016, edge cases).

A `border` object with `width: none` and no `radius` or `padding` changes nothing. The editor
removes the prop instead of storing such an object (BB-08), so documents stay minimal.

**Alternatives considered**:
- **Width as tokens**: the issue names fixed steps, and nobody asked for site-specific
  widths. Fixed steps can become tokens later without breaking documents (spec Assumptions).

---

## BB-03 Radius tokens: a fourth list in the token set

**Decision**: the design token set gains an optional `radii` list of `{ id, name, rem }`,
1 to 12 entries, with `rem` from 0 to 100. It is additive, so the set stays at version 1, as
`contentWidths` and `menuStyle` did (design-tokens.md §2).

- **Ids** follow the existing rules: unique across colours, text sizes, spacing and now
  radii; never reused. New ids use the `radius-` prefix.
- **Default list**, added to `design-tokens/examples/valid/default.json`:

  | Id | Name | rem |
  |---|---|---|
  | `radius-none` | None | 0 |
  | `radius-sm` | Small | 0.25 |
  | `radius-md` | Medium | 0.5 |
  | `radius-lg` | Large | 1 |
  | `radius-full` | Full | 100 |

- **"Full"** is 100 rem (1,600 px at a 16 px base). CSS scales radii down when they don't fit
  a box, so any value at least half the shorter side gives a pill, or a circle on a square
  block (spec US2 scenario 3, FR-015). It needs no special value or flag.
- **A set without `radii`** uses the default list, as a set without `contentWidths` uses the
  default widths. A set with an empty `radii` list fails the existing `token_required` check
  (design-tokens.md §2, check 3).
- **CSS**: `--qb-radius-<id>`, emitted for every default id and every id of the site's own
  set, like the other kinds (page-schema.md §9). `TokenKind` gains `Radius`.

**Rationale**: the shape and the rules are those of spacing, so `ScaleList.vue`, the
validators and both `TokenCss` ports extend with one more kind.

**Alternatives considered**:
- **Pixels instead of rem**: every other size in the set is in rem, scaling with the base text
  size. Rejected for consistency.
- **A `full: true` flag**: it needs a special case in the schema, both ports and the editor.
  100 rem does the same with none.

---

## BB-04 Rendering: classes for width and style, custom properties for tokens

**Decision**: a bordered block's element gets the class `qb-border`, plus:
- `qb-border--{width}` and `qb-border--{style}` when the width is not `none`;
- `qb-border--radius` when a radius token is set and exists;
- `qb-border--pad` when a spacing token is set and exists.

It also gets up to three custom properties in its existing per-node style
(page-schema.md §9):

| Custom property | From | Written when |
|---|---|---|
| `--qb-border-color` | `color` (default `color-border`) | width is not `none` |
| `--qb-border-radius` | `radius` | the token exists (`ValueIfSet` / `tokenValueIfSet`) |
| `--qb-border-pad` | `padding` | the token exists |

`blocks.css` holds the rules, at the end of the block layer, so they win over a block's own
border at equal specificity:

```css
.qb-border { box-sizing: border-box; }
.qb-border--thin   { border: 1px solid var(--qb-border-color); }
.qb-border--medium { border: 2px solid var(--qb-border-color); }
.qb-border--thick  { border: 4px solid var(--qb-border-color); }
.qb-border--dashed { border-style: dashed; }
.qb-border--dotted { border-style: dotted; }
.qb-border--radius { border-radius: var(--qb-border-radius); }
.qb-border--pad    { padding: var(--qb-border-pad); }
```

Radius and spacing have their own classes, so that an unset value never resets a block's
own corners or padding. A bare `var()` of an unset property would compute to the initial
value, squaring a Button's corners. No value from a document reaches CSS except as a token
variable or one of the fixed class names (FR-020).
`context.Style` already writes one `[data-qb]` rule per node, and the canvas writes the same
properties in its `style` attribute.

**Nested defaults**: `BlockDefaults` (server) and `defaultOf` (editor) read the defaults of a
block's own props only. The defaults inside `border` (width `none`, style `solid`, colour
`color-border`) are read from `$defs/border` by a small addition to both:
`BlockDefaults.OfDefinition(name, key)` and `definitionDefault(name, key)` in `schema.ts`.
They are never hard-coded, so the schema stays the one definition (Principle VI).

A block with a radius or spacing but `width: none` gets no width class, so only its corners
or spacing change. A `border` object that produces no class beyond `qb-border` (all tokens
gone, width `none`) renders exactly as a block without one, except for that class.

**One shared helper per side**:
- server: `BorderStyle` in `Rendering/Blocks/`. It returns the classes and declarations for a
  node, and each renderer appends them to its own `class` and `context.Style` call;
- canvas: `useBorder(node)` in `blocks/useBorder.ts`, used by each canvas component the same
  way.

The fixtures keep both helpers in step (BB-12).

**Rationale**: the per-node style rule and the shared stylesheet already exist and are allowed
by the content security policy. Classes keep the fixed widths out of the data.

**Alternatives considered**:
- **A wrapper element around every bordered block, added by `RenderChildren`**: one change
  instead of 15. But it frames the wrong element for the Button (its row, against
  clarification Q2) and the Image (the figure, not the picture), and it adds a grid item
  inside horizontal Containers. Rejected, except where a block needs one (BB-05).

---

## BB-05 Which element each block frames

**Decision**:

| Block | Element framed | Note |
|---|---|---|
| Section | `section.qb-section` | the full-width band |
| Container | `div.qb-container` | |
| Heading, Text | `h2–h4.qb-heading`, `p.qb-text` | |
| Rich Text | `div.qb-rich-text` | |
| Image | `img` inside `figure.qb-image` | the picture itself, so the radius rounds the photo. A missing image draws nothing on the published page, so nothing is framed |
| Button | `a/span.qb-button` | its classes come after the button's own, so a border replaces the button's built-in 2 px frame and colour; a radius replaces its 6 px corners; `padding` replaces its built-in padding |
| Spacer | `div.qb-spacer` | |
| Divider | a new `div.qb-divider-frame` around the `hr`, **only when bordered** | the `hr` draws its line with `border-top`, which a border on the `hr` itself would overwrite |
| Embed | `div.qb-embed` | |
| Navigation | `nav.qb-nav` | the bar only. Drop-down panels keep the menu style (spec Assumptions) |
| Form | the form's outer element | |
| Collection list | `ul.qb-list` | the whole list, when it has items (with none, its empty state shows instead) |
| List card | `li.qb-list__card` | every card. One `[data-qb]` rule serves all of them, because they share the node id |
| Component reference | a new `div.qb-component-ref` around the component's Sections, **only when bordered** | today it has no wrapper (MediaRenderers.cs `ComponentRefRenderer`) |

The two conditional wrappers are allowed markup differences only in the sense that they
appear on both sides at once. The canvas draws the same wrapper, so page-schema.md §9's
"same markup" still holds.

**Rationale**: frame what an Editor sees as "the block". Add an element only where styling the
existing one is impossible (Divider) or there is none (Component reference).

---

## BB-06 Rounded corners and clipping

**Decision**: when a radius is set, `overflow: clip` applies to the Section, Container, Rich
Text, Embed, List card and Component reference, so content doesn't poke past the corners
(spec edge case). It does **not** apply to:
- **Navigation**: its drop-down panels must leave the bar;
- **Form**: focus rings of its fields sit at the edge;
- **Image**: the radius applies to the `img`, which needs no clipping;
- the text blocks, Button, Spacer and Divider: nothing inside can overflow.

`overflow: clip` (not `hidden`) creates no scroll container, so sticky children and the
canvas's drag handles keep working.

**Alternatives considered**:
- **Clip everywhere**: it cuts off menus and focus rings. Rejected.
- **Never clip**: a photo in a rounded card would show square corners. Rejected.

---

## BB-07 Layout: the border never widens a block

**Decision**: `.qb-border` sets `box-sizing: border-box`, so a border and its spacing fit
inside the width the block had before (spec edge case "Border width and layout", Assumption).
Vertical size grows by the border, as any edge would. Blocks that are `width: 100%` today
(Embed, a full Image) stay inside their column.

---

## BB-08 Editor: one automatic "Border" group and one control

**Decision**:
- `ControlKind` gains `border`, and `controls/BorderControl.vue` edits the whole object. It
  shows, in this order:
  1. width (choice);
  2. style (choice);
  3. colour (`TokenControl`, kind colour);
  4. radius (`TokenControl`, new kind radius, with "Block default" for absent);
  5. inner spacing (`TokenControl`, kind space, with "None" for absent), only when the
     schema's `$ref` is `border`, not `borderBox`.

  Style and colour are disabled while width is `none` (FR-008).
- **`PropertyPanel.vue` adds the group itself** when `propNames(type)` includes `border`, after
  the block's own groups and before "Layer", as it already does for `layerLabel`. No block
  definition changes, and every block shows the same group (FR-008, SC-002).
- Every change is one `setProp(root, id, 'border', value)`, so it is one undo step, it is copied
  with the block, and it is saved with the draft (FR-011).
- A value equivalent to "nothing" (width `none`, no radius, no padding, style and colour at
  their defaults) is stored as `undefined`, which removes the prop.
- Style and colour chosen under width `none` are kept, so picking a width again restores them
  (spec edge case).
- `TokenControl` gains the kind `radius`, listing `store.tokens.radii ?? defaultTokens.radii`
  by name, like spacing.
- **Accessibility** (FR-012): every sub-control has a visible `<label>`. The group is a
  `fieldset` with a `legend`, like the others. The choices are native `<select>`s, as in
  `ChoiceControl`, so keyboard use and announced values come from the platform. The e2e suite
  checks it with axe and keyboard-only steps (BB-13).

**Alternatives considered**:
- **A `border` group in every `definition.ts`**: 15 edits that could drift. The panel already
  adds "Layer" this way. Rejected.

---

## BB-09 Design token editor

**Decision**: `DesignTokensView.vue` shows a "Corner radius" list with `ScaleList.vue`, after
Spacing:
- `kind` gains `radii`;
- `min` is 0, `max` 100;
- the step is 0.125 rem;
- at most 12 entries.

`editable()` fills `radii` from the defaults when the set has none, as it does for
`contentWidths`. That way the first save writes the list the site was using anyway.
`newId` gains the prefix `radius`. Deleting asks first with the block count, using the existing
`tokenUsage` call (FR-016).

Previews: each row shows a small square with that radius, like the spacing preview bars.

Permissions are unchanged: saving the set needs `editor.design_tokens.manage` (Designer),
checked by `SaveDesignTokens` on the server (spec US3 scenario 4).

---

## BB-10 Validation on both sides comes from the contract files

**Decision**: there is no new validator code for the schema shapes.
- The page validators (Pages' C# validator and the editor's `subsetValidator.ts`) interpret
  `v1.schema.json`. The new `$defs` use only `type`, `properties`, `additionalProperties`,
  `enum`, `$ref` and `default`, which are already in the subset (page-schema.md §7).
- The token validators interpret `design-tokens/v1.schema.json`. Code changes:
  - the cross-list id uniqueness check (check 5) includes `radii`;
  - the per-list name uniqueness check (check 6) includes `radii`;
  - the empty-list check (check 3) includes `radii` when it is present.

Each change is made on both sides: `tokenValidator.ts` and the Editor module's `TokenSetValidator`
validation.
- Invalid values get the existing errors: `props_invalid` at the faulty key for documents,
  `editor.invalid_tokens` for token sets (FR-019).

---

## BB-11 Publishing, caching, export, import and upgrade need no new code

**Decision**:
- **Publishing and invalidation**: a border is a prop in the document, and a radius token is
  part of the design. Publishing a page, component or design already invalidates exactly what
  it affects (Principle VIII). Nothing new (FR-021).
- **Export and import**: archives carry documents and the token set as JSON, validated by the
  same contracts. A border is carried as a prop, radii as a list. Archives exported before this
  feature have neither, and stay valid (FR-022).
- **Upgrade**: no migration. Every change is additive within version 1 of both contracts, so
  no stored document or token set is rewritten (FR-023).
- **Admin tabs opened before the upgrade**: the old bundle's validator refuses a document that
  holds `border` (`additionalProperties: false`), and a token set that holds `radii`, before
  anything is saved. The Editor sees the existing error and reloads. Nothing is lost silently,
  because `setProp` copies the props the old tab doesn't know, and the old validator refuses
  them first. 035's `X-QB-Rich-Text` header is not needed. That header protects content an old
  bundle *rewrites*; here an old bundle can only refuse.

---

## BB-12 Golden files and parity

**Decision**:
- **Contract examples** (`docs/contracts/page-schema/examples/`):
  - valid `borders-every-block.json`;
  - invalid examples for an unknown width, an unknown style, a raw colour string, `padding`
    on a Section, and an extra key.
- **Token examples**:
  - valid `with-radii.json`;
  - invalid `radii-empty.json`, `radius-duplicate-id.json` (an id shared with spacing) and
    `radius-out-of-range.json`.
- **Token CSS fixtures** (`tests/fixtures/rendering/token-css/`): `radii.json` (custom radii),
  and `no-content-widths.json` also proves that a set without `radii` gets the defaults. Both
  ports run them (`TokenCssParityTests`, `tokenCss.parity.spec.ts`).
- **Rendering golden files** (`tests/fixtures/rendering/golden/`):
  - `borders-every-block.html`: every block type with a border, every width and style;
  - `borders-radius.html`: radius with and without a line, Full, a radius on a Button and on
    an Image;
  - `borders-tokens-missing.html`: a deleted colour token, a deleted radius token and a deleted
    spacing token;
  - `borders-component-ref.html`: a bordered and an unbordered reference to the same
    component;
  - `tests/fixtures/rendering/lists/bordered-card.html`, from `CollectionListRenderTests`
    (lists with items have their golden files there): a bordered card in a list of three items.

  This covers FR-024.
- **Existing golden files**: about 50 embed the page's token CSS. Each gains the five
  `--qb-radius-*` properties of the default set. The `blocks.{hash}.css` address also changes,
  because `blocks.css` changes (as in 035). Nothing else changes. The regenerated files
  (`QB_UPDATE_GOLDEN=1`) are reviewed for exactly that diff, which is the evidence for SC-004: no existing page renders
  differently.
- **Canvas parity**: the existing canvas/renderer parity test
  (`src/admin/tests/editor/CanvasMarkupParity.spec.ts`, page-schema.md §9) runs on the new
  golden inputs, so the canvas and the published page show the same borders (SC-003).
- **Unit tests**:
  - `BorderStyleTests` (server) and `useBorder.spec.ts` (editor): every combination of width,
    style, a present or missing token, and the two wrappers;
  - `BorderControl.spec.ts`: storing `undefined` for "nothing", keeping style and colour under
    `none`, and hiding Inner spacing for `borderBox`.

---

## BB-13 End-to-end coverage

**Decision**: one spec, `tests/e2e/specs/block-borders.spec.ts`, in the harness from 025:
1. sign in as a Designer, add a radius token, save and publish the design;
2. as an Editor, add a Container and a Text block, open the Border group **with the keyboard
   only**, and set width, style, colour, radius and inner spacing;
3. check the canvas element's computed border;
4. publish, open the page as a visitor, and check the same computed border and radius;
5. run the axe check on the panel with the Border group open (SC-006).

It reuses the harness's page objects and accessibility fixture. It adds no test ids, because
it selects by role and label (025 E2E-08). This covers FR-025.

---

## BB-14 Documentation

**Decision**:
- `docs/contracts/page-schema.md`:
  - §2: the table gains `border` on the 15 types;
  - §2a: `radiusRef`, and the `border` and `borderBox` values;
  - §6: "Additions in 066 (still version 1)";
  - §9: the border classes, custom properties and the two conditional wrappers.
- `docs/contracts/design-tokens.md`: §1 example and §2 rules gain `radii`.
- `docs/operators/operations/content.md` mentions design tokens. It gains one sentence: corner
  radii are part of the tokens and travel with exports.

`PageSchemaContractTests` and `DesignTokenContractTests` fail the build while the documents and
the schema files disagree, so these are made in the same change (constitution, Development
Workflow).
