# Feature Specification: Border Styles for All Blocks

**Feature Branch**: `066-block-borders`

**Created**: 2026-11-28

**Status**: Draft

**Issue**: #66

**Input**: User description: "#66 Border styles for all blocks. Problem / goal: Editors cannot put a border on blocks. No block in the page schema has border properties. Section and container offer only background and padding, and the only border-like options are the button's outline style and the divider's colour and thickness. So boxed cards, framed images, outlined callouts and bordered columns cannot be built without an embed. Editors need a consistent border style for every block, set in the editor's properties panel and backed by the site's design tokens, like colour and spacing are today. In: a shared, optional border property on every content block: section, container, heading, text, rich text, image, button, spacer, divider, embed, navigation, form, collection list (and its card), and component references; border width (none, thin, medium, thick), style (solid, dashed, dotted) and colour (a colorRef to the site's colour tokens); corner radius, backed by new radius design tokens (for example radius-none, radius-sm, radius-md, radius-lg, radius-full) that operators can edit with the other tokens; one shared "Border" group in the properties panel, so every block shows the same controls; the page schema contract, both validators, the renderer (token-backed CSS, no inline arbitrary values) and the editor canvas preview; export and import, and the upgrade path: existing pages have no border, so nothing changes; golden rendering fixtures and end-to-end coverage for the new controls; accessible controls (labels, keyboard use). Out: separate widths, styles or colours per side; free-form CSS or raw pixel values outside the design tokens; shadows, gradients and other visual effects (possible follow-up); borders inside rich text content (tables, images in rich text); per-breakpoint border settings."

## Context

Blocks on pages and components are styled only through the properties the page schema gives
each block type (feature 011), and those properties name the site's design tokens rather than
raw values. Today:

- Sections and Containers can have a background colour and padding, but no edge.
- The Button's `outline` style draws a border, but its colour and shape are fixed.
- The Divider has its own colour and thickness, but it is a line, not a frame.
- No block can have rounded corners.

So common layouts cannot be built: a card with a thin frame, a photo with rounded corners, a
callout with a dashed outline, columns separated by bordered boxes. The only way out is an
Embed block with hand-written markup, which editors should not need and which is meant for
third-party content, not layout.

This feature gives every content block the same optional border: a width, a line style, a
colour from the site's colours, and a corner radius from a new list of radius tokens that
Designers edit with the other tokens. A block without a border looks exactly as it does
today, so no existing page, component or collection template changes.

## Clarifications

### Session 2026-11-28

- Q: When an Editor borders a block that has no padding of its own (Heading, Text, Rich
  Text, Button and so on), should the Border group offer an inner spacing control? → A: Yes.
  The Border group gains an optional inner spacing, a reference to the site's spacing
  tokens, on every block except Section and Container, which keep their existing padding
  control instead.
- Q: When an Editor adds a border to a Button, what does it frame? → A: The button itself.
  When set, it replaces the button's built-in frame, whatever its style (primary, secondary
  or outline); without one, every style looks as today.
- Q: Should a Component Reference have its own border? → A: Yes, per use. A border on the
  reference frames that one placement; the component and its other uses are unchanged, and
  borders on the blocks inside the component still apply everywhere.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Frame a block with a border (Priority: P1)

An Editor selects a Container on a page, opens the "Border" group in the properties panel,
and picks a width (thin), a style (solid) and a colour from the site's colours. The canvas
shows the frame at once. After publishing, visitors see the same frame.

**Why this priority**: This is the problem in the issue. Without it, none of the boxed or
outlined layouts can be built. It is useful on its own even before rounded corners exist.

**Independent Test**: Add a border to one block of each type, save, reload, publish, and
compare the canvas with the published page: every block shows the chosen width, style and
colour, and blocks without a border look as before.

**Acceptance Scenarios**:

1. **Given** a page with a Container that has no border, **When** the Editor sets the
   border width to "thin", the style to "solid" and the colour to the site's "Border"
   colour, **Then** the canvas draws a thin solid frame in that colour around the
   Container right away.
2. **Given** a block with a border, **When** the Editor saves, reloads the editor and
   publishes the page, **Then** the editor shows the same border settings and the
   published page draws the same border.
3. **Given** a block with a border, **When** the Editor sets the width back to "none",
   **Then** the frame disappears from the canvas, and after publishing, from the page.
4. **Given** any content block type listed in FR-001, **When** the Editor selects it,
   **Then** the properties panel shows the same "Border" group with the same controls in
   the same order.
5. **Given** a block with a border, **When** the Editor undoes the change, **Then** the
   block returns to its previous border, like any other property change.

---

### User Story 2 - Round the corners of a block (Priority: P2)

An Editor picks a corner radius for an Image or a Container, with or without a border. The
corners of the block, including its background and the image itself, become rounded by the
amount the site's radius token defines.

**Why this priority**: Rounded cards and photos are the second most common need. It builds
on the same "Border" group but depends on the new radius tokens.

**Independent Test**: Set a radius on an Image without a border and on a Container with a
background and a border; both show rounded corners on the canvas and the published page,
and nothing pokes out past the corners.

**Acceptance Scenarios**:

1. **Given** an Image block with no border, **When** the Editor picks the radius "Medium",
   **Then** the image's corners are rounded by the site's Medium radius, on the canvas and
   on the published page.
2. **Given** a Container with a background colour and a border, **When** the Editor picks
   a radius, **Then** the border and the background follow the same rounded corners.
3. **Given** a block with the radius "Full", **When** it is shown, **Then** its short sides
   are fully rounded (a pill or a circle for a square block).

---

### User Story 3 - Manage the site's radius tokens (Priority: P2)

A Designer opens the site's design tokens and finds a new "Corner radius" list beside colours and
spacing. They change the value of "Medium" from small to larger, add a new radius, or rename
one. Every block that uses that radius updates, and visitors see the change when the design
is published.

**Why this priority**: Radius values are tokens, so the site keeps one consistent look and
Designers change it in one place, as with colours and spacing. Without this, corners would
need raw values, which the issue rules out.

**Independent Test**: Change the value of a radius token used by several blocks; the canvas
updates every one of them, and after publishing the design, the published pages update too.

**Acceptance Scenarios**:

1. **Given** a site that has never saved radius tokens, **When** a Designer opens the
   design tokens, **Then** the "Corner radius" list shows the default radii (None, Small, Medium,
   Large, Full).
2. **Given** several blocks using the "Medium" radius, **When** the Designer changes its
   value and saves, **Then** every one of those blocks updates on the canvas; visitors see
   the new value only after the design is published.
3. **Given** a block using a radius token, **When** a Designer deletes that token, **Then**
   the block's document stays valid and the block shows its own corners again: square for
   every block except the Button, which keeps its usual rounded corners.
4. **Given** a user without permission to change design tokens (an Editor), **When** they
   open the design tokens, **Then** they cannot change the radius list, and the server
   refuses a change sent anyway.

---

### User Story 4 - Borders travel with the site (Priority: P3)

An operator exports a site and imports it into another installation. Every border and radius
setting, and the site's radius tokens, arrive unchanged. Archives exported before this
feature import as before, with no borders.

**Why this priority**: Required by the constitution (Principle III) but follows from the
other stories, since borders live in the page documents and token set the archive already
carries.

**Independent Test**: Export a site with bordered blocks and custom radius tokens, import it
into a fresh installation, and compare the published pages.

**Acceptance Scenarios**:

1. **Given** a site with bordered and rounded blocks and edited radius tokens, **When** it
   is exported and imported, **Then** the imported pages render the same borders and radii.
2. **Given** an archive exported before this feature, **When** it is imported, **Then** it
   imports without errors and its pages look as they did.

### Edge Cases

- **Colour token deleted**: a border whose colour token no longer exists uses the border
  colour's default (the site's "Border" colour), as other colour properties fall back today.
- **Radius token deleted**: the block falls back to its own corners: none for every block
  except the Button, which keeps its usual rounding (FR-016).
- **Radius without border**: allowed; the corners are rounded and no line is drawn.
- **Bordered text without inner spacing**: allowed; the line sits against the content. The
  Editor adds inner spacing to move it away.
- **Spacing token deleted**: inner spacing falls back to the block's own spacing: none for
  every block except the Button, which keeps its usual padding (FR-016).
- **Style or colour set but width "none"**: no line is drawn; the style and colour are kept,
  so picking a width again restores them.
- **Nested borders**: a bordered Container inside a bordered Section draws both frames; the
  inner one sits inside the outer one's padding as any child does.
- **Content larger than a rounded block** (an image, a wide table in rich text): it is
  clipped to the rounded corners rather than drawn over them.
- **Button styles**: every button style already has a built-in frame. When the block has
  its own border, that border replaces it, whatever the style; without one, every style
  looks as today.
- **Component references**: a border on a component reference frames that use of the
  component on that page only; the component itself and its other uses are unchanged.
  Borders on blocks inside the component show in every use, inside the reference's frame.
- **Collection list cards**: a border on the list's card frames every card the list shows,
  for every item.
- **Collection templates**: borders on blocks bound to item data (feature 016) render the
  same for every item; borders are never bound to item fields.
- **Border width and layout**: adding a border does not make a block wider than its column
  or page width, and it does not shift the blocks around it horizontally.
- **Responsive preview**: borders look the same at every preview width.
- **Older documents**: pages, components and token sets saved before this feature have no
  border or radius properties; they open, validate, save and render exactly as before.
- **Invalid values**: a document with an unknown width or style, or a radius or colour
  reference that is not a token reference, is refused by both validators with the usual
  property error.

## Requirements *(mandatory)*

### Functional Requirements

**Border on every block**

- **FR-001**: Every content block MUST accept one optional border setting: Section,
  Container, Heading, Text, Rich Text, Image, Button, Spacer, Divider, Embed, Navigation,
  Form, Collection List, the Collection List's card, and Component Reference. Form fields
  inside a Form and the Collection List's empty-state block are not content blocks and do
  not get one.
- **FR-002**: The border setting MUST consist of:
  - a **width**: none, thin, medium or thick (default none);
  - a **style**: solid, dashed or dotted (default solid);
  - a **colour**: a reference to one of the site's colour tokens (default the site's
    "Border" colour);
  - a **corner radius**: a reference to one of the site's radius tokens (default: the
    block's own corners, which are square for every block except the Button);
  - an **inner spacing**: a reference to one of the site's spacing tokens, the space between
    the border and the block's content (default: the block's own spacing, which is none for
    every block except the Button). Section and Container do not have it; their existing
    padding serves that purpose.
- **FR-003**: A block with no border setting, or with width none, no radius and no inner
  spacing, MUST look exactly as it does before this feature, on the canvas and on published
  pages. "Look" means what is drawn: such a block may carry a marker class, but no
  property of its appearance changes.
- **FR-004**: Thin, medium and thick MUST each be one fixed, visibly different line width,
  the same for every block and every site.
- **FR-005**: The corner radius MUST round the block's border, its background and its
  content, independently of whether a border line is drawn.
- **FR-006**: The border MUST frame the block's own box. For the Image block that is the
  image; for the Button block that is the button itself, whatever its style, and its border
  replaces the button's built-in frame; for a Component Reference, the referenced component as placed on
  that page; for the Collection List's card, every card.
- **FR-007**: The settings MUST NOT allow raw CSS, pixel values or colours outside the
  site's tokens, nor different settings per side or per screen width.

**Editor**

- **FR-008**: The properties panel MUST show one "Border" group, identical for every block
  in FR-001: width, style, colour, corner radius and inner spacing, in that order, except
  that Section and Container show no inner spacing. Style and colour MUST be available only
  while a width other than none is chosen; radius and inner spacing are always available.
- **FR-009**: The colour control MUST list the site's colour tokens, and the radius control
  the site's radius tokens, by name, as other token controls do today.
- **FR-010**: The canvas MUST show border changes immediately, the same way the published
  page will show them, at every preview width.
- **FR-011**: Border changes MUST behave like every other property change: they are saved
  with the draft, can be undone and redone, are copied with the block when it is copied or
  duplicated, and only reach visitors on publish.
- **FR-012**: All Border controls MUST have visible labels, be reachable and operable by
  keyboard alone, and announce their current value to screen readers.

**Radius tokens**

- **FR-013**: The site's design tokens MUST gain a "Corner radius" list, edited by the people who
  may edit the other tokens (Designers), in the same place and the same way as colours and
  spacing: add, rename, change value, delete.
- **FR-014**: A site that has never saved radius tokens MUST use a default list of five:
  None (no rounding), Small, Medium, Large and Full (fully rounded). Their ids MUST be fixed,
  as the default ids of colours and spacing are today.
- **FR-015**: Each radius token MUST have an id, a name and a size within a bounded range,
  with the same rules on ids, names and number of entries as the other token lists.
  "Full" MUST stay fully rounded whatever the block's size.
- **FR-016**: Deleting a radius, colour or spacing token MUST NOT make any document invalid; blocks
  that use it MUST fall back to the property's default (FR-002).
- **FR-017**: Radius token changes MUST follow the existing token rules: they are draft
  settings until the site's design is published, and they update every block that uses
  them.

**Contracts, validation and rendering**

- **FR-018**: The border setting MUST be part of the shared page schema contract and the
  radius list part of the shared design token contract, as additions: documents and token
  sets saved before this feature MUST stay valid without being rewritten.
- **FR-019**: The editor and the server MUST both validate border settings and radius
  tokens against those same contracts and MUST refuse invalid values with the existing
  error reporting (code and location of the faulty value).
- **FR-020**: Published pages MUST render borders from the site's published tokens only. No
  value from a document may reach the page's styles other than through a token or one of the
  fixed widths and styles. This is the rendering side of FR-007, which limits what can be
  set.
- **FR-021**: Publishing a page, component or design that changes a border or a radius
  token MUST update the affected published pages, as any other property or token change does
  today.

**Transfer and upgrade**

- **FR-022**: Site export and import MUST carry border settings and radius tokens unchanged.
  Archives exported before this feature MUST import unchanged.
- **FR-023**: Upgrading an installation MUST NOT change how any existing page looks, and
  MUST NOT require rewriting stored pages, components or token sets.

**Tests**

- **FR-024**: Golden rendering fixtures MUST cover every block type with a border, every
  width and style, a radius with and without a border, a deleted colour token and a deleted
  radius token.
- **FR-025**: The end-to-end suite MUST cover setting a border and a radius in the editor,
  publishing, and seeing them on the published page, including keyboard-only use of the
  Border group.

### Key Entities

- **Border setting**: an optional part of a block's properties: width, style, colour
  reference, radius reference and (except on Section and Container) inner spacing
  reference. Stored in the page or component document with the block.
- **Radius token**: a named corner size in a site's design tokens, with a stable id, a
  display name and a size. Referred to by id from border settings.
- **Design token set**: the site's existing tokens (colours, typography, spacing, content
  widths, menu style), which gains the radius list.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Editor can give any block a framed, rounded look (width, style, colour and
  radius) in under 30 seconds, without leaving the properties panel.
- **SC-002**: All 15 block types in FR-001 offer the same Border group; none needs an Embed
  for a frame or rounded corners.
- **SC-003**: For every golden fixture, the canvas and the published page show the same
  border and radius.
- **SC-004**: After upgrading, 100% of existing pages look identical to before: every
  existing golden rendering file differs only by the five default corner radius values and
  the stylesheet's file name, neither of which changes how anything is drawn.
- **SC-005**: Changing one radius token updates every block that uses it, on every page of
  the site, with one save and one design publish.
- **SC-006**: Every Border control can be used with the keyboard alone and passes the
  project's automated accessibility checks.
- **SC-007**: Published pages with borders stay within the existing rendering performance
  budgets.

## Assumptions

- Border widths are fixed steps rather than tokens, as the issue names them (none, thin,
  medium, thick); only the radius is a new token list. Fixed widths can become tokens later
  without breaking documents.
- The default border colour is the existing "Border" colour token (`color-border`), which
  the Divider already uses.
- Radius tokens are managed by the role that manages other tokens today (Designer); Editors
  pick from them but cannot change them.
- A token set without a radius list uses the default radii, as a token set without content
  widths uses the default widths today.
- The border is drawn inside the block's existing width, so a bordered block fits the same
  column it fitted before.
- Border colours are decorative and are not checked for contrast; the existing contrast
  warnings for text colours are unchanged.
- Borders on Navigation frame the menu bar only, not its drop-down panels, whose look stays
  with the site's menu style (feature 024).
- This builds on the editor core (#11), publishing and rendering (#12), pages and components
  (#10), collection templates and lists (#16), forms (#18) and navigation (#24), and changes
  no behaviour of those features beyond adding the border.
