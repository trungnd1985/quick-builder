# Feature Specification: Editor Core

**Feature Branch**: `011-editor-core`

**Created**: 2026-09-28

**Status**: Draft

**Issue**: #11

**Input**: User description: "Editor core. Problem / goal: A user needs to build a page visually instead of writing JSON. In the editor a user can drag blocks onto a canvas, reorder and nest them, edit their properties, undo and redo, preview at desktop/tablet/mobile widths and save the result as a draft. In: editor shell and canvas, block palette with around ten core blocks (section, container, heading, text, rich text, image, button, spacer, divider, embed), property panel driven by each block's props schema, layer/outline view, undo/redo, responsive preview, autosave of drafts, site-level design tokens (colors, typography, spacing) applied in the canvas. Out: collection binding and list blocks (#11), forms (#13), custom code blocks."

## Context

Feature 010 gave every site a tree of pages and shared components, each with one draft
content document in the versioned page schema (`docs/contracts/page-schema.md`). Version 1
of that schema knows only two node types, the document root and the reference to a shared
component, so today a draft can hold nothing a visitor would read. The only way to change
a draft is to send a whole JSON document.

This feature adds the visual editor. A site member opens a page from the page tree, drags
content blocks onto a canvas, arranges and nests them, edits their properties in a panel,
and the draft is saved for them as they work. The canvas looks like the page will look,
using the site's own colours, fonts and spacing, and can be previewed at desktop, tablet
and mobile widths.

It also adds the ten content block types to the page schema contract, as constitution
Principle VI requires: each block type is defined once, with its properties, defaults and
editor controls, and adding one does not change any other. Publishing and rendering for
visitors (#12) will reuse the same definitions.

The issue text names collection binding "#11" and forms "#13"; in the issue list those are
#16 (collection templates and data binding) and #18 (forms and submissions). This spec
uses the latter numbers.

## Clarifications

### Session 2026-09-28

- Q: When a member with the Editor role opens a page in the editor, what may they change?
  → **A: everything in the page's draft.** Editors, like Designers, can insert, move,
  nest, duplicate and delete blocks and edit every property, because both hold "save page
  drafts" (feature 010). Only Designers and Administrators edit shared components and
  design tokens.
- Q: Can a block have different settings at tablet or mobile width? → **A: no.** Every
  block has one set of settings for all widths; layouts adapt automatically through the
  block definitions (Containers stack at mobile width, images scale down). Settings per
  width, including hiding a block at some widths, come in a later feature as optional
  properties, which is an additive change to the schema.
- Q: When a save is refused because someone else saved the same page first, may the member
  overwrite that version with theirs? → **A: no.** The editor never overwrites. It keeps
  the member's version on screen, read-only, so they can copy blocks to the clipboard, and
  offers to reload the latest draft, after which they can paste the copied blocks back.
  Richer concurrent editing is #23.
- Q: What happens when a designer deletes a colour token that drafts still use? → **A:
  allowed after a warning.** The warning states how many blocks use the colour; afterwards
  those blocks use their block type's default colour, and the editor marks the property as
  referring to a deleted colour. No lasting record of colour usage is kept.
- Q: Should unsaved changes survive a browser crash or a closed tab? → **A: yes, offered on
  the next visit.** The editor keeps unsaved changes in the member's browser. When the
  member next opens that page or component, it offers to restore them if the server draft
  is unchanged since they loaded it, or to open them read-only for copying blocks if it
  has changed. The copy is discarded after a successful save or when declined.

### Session 2026-09-29

- Q: How should a member choose how wide a Section's content can be? → **A: a per-section
  choice of Narrow, Normal, Wide or Full.** Narrow, Normal and Wide are site-wide design
  tokens (defaults 720, 1,200 and 1,440 pixels) that a designer can change on the Design
  screen; Full spans the page edge to edge. Sections saved before this as "contained" are
  Normal.
- Q: Should a Container have its own width setting, or always fill its parent? → **A: an
  optional maximum width** of Narrow, Normal or Wide (the same site tokens) or Full, the
  default. A Container narrower than its parent is placed at the start, centre or end by
  its existing alignment setting.
- Q: How should the member set how a horizontal Container's row is split between its
  blocks? → **A: a column pattern on the Container**: Equal (default), 2/3 + 1/3,
  1/3 + 2/3, 3/4 + 1/4, 1/4 + 3/4, or 1/2 + 1/4 + 1/4. Blocks beyond the pattern start a
  new row with the same pattern. It applies at desktop and tablet widths; at mobile width
  the row stacks as before when "stack on mobile" is on.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A site member builds a page by dragging blocks onto a canvas (Priority: P1)

A designer opens the empty "About" page from the page tree. The editor shows a palette of
blocks, a canvas and a property panel. They drag a Section onto the canvas, drop a Heading
and a Text block into it, and type "About us" and a short paragraph directly on the
canvas. They drag an Image below the text and choose a photo from the site's media
library, then add a Button that links to the "Contact" page. They reorder the heading and
image by dragging, and delete a Spacer they added by mistake. Each change appears on the
canvas at once, and the draft is saved without them pressing anything.

**Why this priority**: This is the whole point of the feature. Without placing, arranging
and saving blocks, no other part of the editor has anything to work on, and no site can
have content.

**Independent Test**: Open an empty page, add one block of each of the ten types, nest a
Heading inside a Container inside a Section, reorder two blocks, move a block from one
Section to another and delete one block. Reload the editor and confirm that the page
shows exactly the same blocks, in the same order and nesting, with the same content, and
that the stored draft passes validation against the page schema contract.

**Acceptance Scenarios**:

1. **Given** a site member who may save the page's draft,
   **When** they open the page from the page tree,
   **Then** the editor opens with the page's current draft shown on the canvas, the block
   palette, the property panel and the layer view.
2. **Given** the editor,
   **When** a member drags a block from the palette to a place on the canvas or in the
   layer view where that block is allowed,
   **Then** the block is inserted there with its default properties and becomes the
   selected block.
3. **Given** a drag in progress,
   **When** the pointer is over a place where the dragged block is not allowed (for example
   a Section inside a Section, or anything inside a Heading),
   **Then** the editor shows that it cannot be dropped there, and releasing it changes
   nothing.
4. **Given** a block on the canvas,
   **When** a member drags it to another allowed place, including into or out of a
   Section or Container,
   **Then** it moves there with all the blocks nested inside it.
5. **Given** a selected block,
   **When** a member duplicates or deletes it,
   **Then** the copy is inserted directly after it (with new identifiers for it and all
   its nested blocks), or the block and everything nested inside it is removed.
6. **Given** a member who uses only the keyboard,
   **When** they add, select, move, duplicate and delete blocks,
   **Then** every one of those actions is possible without a pointing device.
7. **Given** a member with a role on site A only,
   **When** they try to open the editor for a page of site B,
   **Then** the editor answers as though the page did not exist.

---

### User Story 2 - A site member edits a block's properties in the property panel (Priority: P1)

The designer selects the Heading and the property panel shows its text, its level and its
alignment. They change it from level 1 to level 2 and centre it. On the Section they pick
the site's "Brand light" colour as the background and a larger vertical padding from the
site's spacing scale. On the Image they write alternative text. On the Button they switch
the link from the "Contact" page to an external address. Every change shows on the canvas
immediately. When they type an address that is not allowed, the panel says why and keeps
the last valid value.

**Why this priority**: Blocks with only their defaults are not a page. Editing properties
is how every block becomes content, and it is how the site's design tokens reach the page.

**Independent Test**: For each block type, select a block and change every property the
panel offers. Confirm that each change appears on the canvas, survives a reload, and that
invalid values (a link with a disallowed scheme, an embed from an unsupported provider,
text longer than the limit) are refused in the panel with a reason and never reach the
saved draft.

**Acceptance Scenarios**:

1. **Given** a selected block,
   **When** the property panel is shown,
   **Then** it shows exactly the properties that block type defines, each with a control
   suited to it (text, choice, number, colour token, spacing token, link, media picker),
   the current values, and the defaults for properties never set.
2. **Given** a property change in the panel,
   **When** the member makes it,
   **Then** the canvas shows the result immediately and the change becomes part of the
   draft.
3. **Given** a Text, Heading, Rich Text or Button block,
   **When** the member edits its text directly on the canvas,
   **Then** the text changes in place, and the same value appears in the property panel.
4. **Given** a Rich Text block,
   **When** the member formats text,
   **Then** they can use paragraphs, bold, italic, links and bulleted and numbered lists,
   and nothing else; text pasted from elsewhere keeps only those formats.
5. **Given** a link property (Button, or a link in Rich Text),
   **When** the member sets it,
   **Then** they can choose a page of the same site, which keeps working when that page
   is moved or renamed, or type an external address beginning with `https://`, `http://`,
   `mailto:` or `tel:`; any other address is refused with a reason.
6. **Given** an Image block,
   **When** the member chooses its image,
   **Then** they pick from, or upload to, the site's own media library, and can set
   alternative text or mark the image as decorative.
7. **Given** an Embed block,
   **When** the member pastes the address of a video on a supported provider,
   **Then** the embed is accepted and the canvas shows a placeholder naming the provider
   and the address; an address from any other provider is refused with a reason that lists
   the supported ones.
8. **Given** a colour, text size or spacing property,
   **When** the member sets it,
   **Then** they choose from the site's design tokens, and the block keeps referring to
   that token rather than to a copy of its value.

---

### User Story 3 - A site member works safely with undo, redo and autosave (Priority: P1)

The designer deletes a Section by accident and presses undo; it comes back with everything
inside it. They press redo, change their mind and undo again. Later their network drops for
a minute: the editor says changes are not saved yet, keeps retrying, and says "Saved" once
it can. A colleague opens the same page in another tab and saves a change first; the
designer's next save is refused as out of date, and the editor tells them so instead of
overwriting the colleague's work.

**Why this priority**: A visual editor that loses work or cannot take back a mistake is
not usable. Autosave and undo together are what make direct manipulation safe.

**Independent Test**: Make a series of at least 20 mixed changes (insert, move, delete,
property edits, text typing), undo all of them and confirm that the page is back to its
starting state, then redo all of them and confirm it matches the final state. Cut the
network, make changes, restore it and confirm that they are saved without action. Save the
same page from a second session and confirm that the first session reports the conflict,
offers no way to overwrite it, and lets the member copy a changed block, reload and paste
it back.

**Acceptance Scenarios**:

1. **Given** any change to the page in the editor,
   **When** the member undoes it,
   **Then** the page returns exactly to its state before the change, including the
   selection, and redo reapplies it.
2. **Given** a member typing in a text field or on the canvas,
   **When** they undo,
   **Then** the typing is undone in sensible chunks (a word or a pause), not one character
   at a time.
3. **Given** unsaved changes,
   **When** the member stops editing for a short while,
   **Then** the draft is saved automatically, and the editor shows whether the page is
   saved, saving, or has unsaved changes.
4. **Given** a save that fails because the connection or the server is unavailable,
   **When** it fails,
   **Then** the editor keeps the changes, says they are not saved, retries on its own and
   lets the member retry now.
5. **Given** a save refused as out of date because someone else saved the draft since
   this session loaded it,
   **When** the refusal arrives,
   **Then** the editor stops autosaving that page, tells the member, keeps their version on
   screen read-only so they can select and copy blocks, and offers to reload the latest
   draft; after reloading they can paste the copied blocks. The editor never offers to
   overwrite the other save.
6. **Given** unsaved changes,
   **When** the member tries to leave the editor or close the tab,
   **Then** they are warned before the changes are lost.
7. **Given** a save refused because the document breaks the page schema contract,
   **When** the refusal arrives,
   **Then** the editor shows the reason and selects the offending block if it can, and
   does not lose the member's changes.
8. **Given** unsaved changes when the browser crashed or the tab was closed,
   **When** the same member opens that page again in the same browser,
   **Then** the editor offers to restore the changes if the saved draft has not changed
   since they loaded it, or to open them read-only so blocks can be copied if it has;
   declining discards them.

---

### User Story 4 - A site member navigates the page in the layer view (Priority: P2)

On a long page, the designer opens the layer view: an outline of every block, nested as on
the page. They select a Button buried three levels deep by clicking its entry, which
selects and scrolls to it on the canvas. They collapse Sections they are not working on,
drag a Container to a different Section in the outline, and rename a Section "Hero" so it
is easy to find.

**Why this priority**: Direct canvas manipulation breaks down on nested and small blocks
(a Spacer, a Divider, a Container with no padding). The outline makes every block
reachable and is the keyboard-friendly way to arrange a page. It builds on stories 1 and 2.

**Independent Test**: On a page with at least 30 blocks nested four levels deep, select
each of three deeply nested blocks through the layer view and confirm the canvas selects
and scrolls to them. Move a block to another parent in the layer view using the pointer and
using the keyboard only, and confirm that the canvas and the saved draft agree.

**Acceptance Scenarios**:

1. **Given** the editor,
   **When** the layer view is open,
   **Then** it lists every block of the page with its type and label, nested as on the
   canvas, and the selection is the same in both.
2. **Given** a block selected in either place,
   **When** it is outside the visible area of the other,
   **Then** the other scrolls to show it.
3. **Given** the layer view,
   **When** a member drags an entry, or moves it with the keyboard, to another allowed
   position,
   **Then** the block moves exactly as it would by dragging on the canvas, under the same
   nesting rules.
4. **Given** a block,
   **When** a member gives it a label,
   **Then** the layer view shows the label instead of the type name; the label is not
   shown to visitors.
5. **Given** a Section or Container with children,
   **When** the member collapses it in the layer view,
   **Then** its children are hidden in the outline only, not on the canvas.

---

### User Story 5 - A site member previews the page at desktop, tablet and mobile widths (Priority: P2)

Before handing the page to the client, the designer switches the canvas to tablet width
and then to mobile width. The two-column Container stacks into one column, the Heading
wraps and the Image scales down. Editing keeps working at every width.

**Why this priority**: Most visitors will see the page on a phone. Checking the layout at
narrower widths is part of building a page, but it depends on there being blocks to look at.

**Independent Test**: Build a page with a horizontal Container of three blocks, a wide
Image and a long Heading. Switch between the three widths and confirm that the canvas
width changes to the defined width for each, that the layout adapts as the block
definitions say, and that selecting and editing work at each width.

**Acceptance Scenarios**:

1. **Given** the editor,
   **When** the member chooses desktop, tablet or mobile,
   **Then** the canvas is shown at that width (see Assumptions for the widths), and the
   chosen width is remembered for the member's next visit to the editor.
2. **Given** a Container laid out horizontally,
   **When** the canvas is at mobile width,
   **Then** its children stack vertically unless the member turned that off for the
   Container.
3. **Given** any width,
   **When** the member selects, edits, moves or adds blocks,
   **Then** it works as at desktop width, and the change applies at every width.

---

### User Story 6 - A designer sets the site's design tokens (Priority: P2)

The designer opens the site's design settings and defines the brand colours ("Brand",
"Brand light", "Text", "Background"), picks a heading font and a body font from the
available fonts, sets the base text size and adjusts the spacing scale. Every block on the
canvas that uses those tokens changes at once. Later they change "Brand" from blue to green
and every Button and Section using it follows.

**Why this priority**: Tokens are what keep a site consistent and make a rebrand one
change instead of hundreds. The editor works with the built-in defaults until a designer
changes them, so this comes after the editing stories.

**Independent Test**: On a page that uses colour, font and spacing tokens, change each
token's value and confirm that every block using it updates on the canvas without editing
the blocks. Delete a colour token that blocks use and confirm that the warning states
how many blocks use it, and that afterwards those blocks show the default colour, are
marked in the property panel, and their pages still save. Confirm that an Editor can see the tokens in effect but cannot change them.

**Acceptance Scenarios**:

1. **Given** a new site,
   **When** a member first opens the editor,
   **Then** the site already has a default set of colour, typography and spacing tokens,
   and the canvas uses them.
2. **Given** a member who may manage design tokens,
   **When** they add, rename or change the value of a colour token, choose the fonts and
   base size, or change a step of the spacing scale,
   **Then** the change is saved and the canvas of any page open in the editor reflects it
   the next time it loads the tokens, without any block being edited.
3. **Given** a colour token,
   **When** its name is changed,
   **Then** blocks that use it keep using it, because blocks refer to tokens by an
   identifier that never changes.
4. **Given** a member without the design token capability,
   **When** they open the design settings,
   **Then** they can see the tokens but any change is refused as forbidden and not
   offered.
5. **Given** a change to the design tokens,
   **When** it is saved,
   **Then** nothing visitors see changes, because nothing is published in this feature.

---

### User Story 7 - A designer edits a shared component in the editor (Priority: P3)

The designer opens the "Header" component from the component list. It opens in the same
editor, where they build it from a Section with a Container holding an Image (the logo) and
three Buttons. On any page that uses the header, the canvas shows the header's current
content in place of the reference, clearly marked as shared and not editable there, with a
way to open the component itself. They can also add a shared component to a page from the
palette.

**Why this priority**: Feature 010 created components but they cannot yet hold content.
Headers and footers matter to nearly every site, but a single page is useful without them.

**Independent Test**: Open a component in the editor, add blocks and confirm the draft is
saved. Insert that component into two pages from the palette and confirm that both show
its content, that it cannot be edited from a page, and that changing the component shows
on both pages the next time they are opened.

**Acceptance Scenarios**:

1. **Given** a member who may manage components,
   **When** they open a component from the component list,
   **Then** it opens in the editor with the same palette, panel, layer view, undo, preview
   and autosave as a page, except that shared components are not offered in the palette.
2. **Given** a page in the editor,
   **When** the member inserts a shared component from the palette,
   **Then** a reference to it is inserted, and the canvas shows the component's current
   draft content in its place.
3. **Given** a component reference on a page's canvas,
   **When** the member selects it,
   **Then** it is selected as one unit, its content cannot be edited there, and the
   property panel offers to open the component in the editor.
4. **Given** an Editor on the site,
   **When** they open the component list,
   **Then** they cannot open a component for editing, consistent with feature 010's role
   split.

---

### Edge Cases

- **Opening a page whose draft is empty**: the canvas shows an empty state that invites
  the member to drag a first block from the palette or add one with the keyboard.
- **A draft that no longer passes validation** (for example because it was written by a
  newer release and the installation was rolled back): the editor does not open it for
  editing; it says why, so no save can corrupt it.
- **A block type the palette does not offer** cannot occur in version 1 documents, because
  the contract only admits known types.
- **The image in an Image block has been deleted from the media library**, or never belonged
  to this site: the canvas shows a clear "image missing" placeholder in its place, the
  property panel offers to choose another, and the draft can still be saved. Stopping
  deletion of files in use is left to the media library feature (#13).
- **A link to a page that has since been deleted**: the canvas and panel mark the link as
  broken; the draft can still be saved. Visitor-facing handling belongs to rendering (#12).
- **A shared component that has since been deleted** cannot occur: feature 010 refuses to
  delete a component that a page draft references.
- **The page is deleted or moved while it is open in the editor**: a move changes nothing
  for the editor; after a delete the next save is refused, and the editor says the page no
  longer exists and does not retry.
- **The member loses the page capability while it is open**: the next save is refused as
  forbidden; the editor says so and stops autosaving.
- **Very large pages**: the editor keeps working within the limits of the page schema
  contract (5,000 nodes, depth 32, 1 MiB). An action that would exceed a limit (for example
  pasting or duplicating a large Section) is refused in the editor with a reason before it
  is applied, so autosave never sends a document that is certain to be refused.
- **Pasting from outside the editor into a Text or Rich Text block**: only plain text, or
  the allowed rich text formats, are kept; scripts, styles, images and any other markup are
  dropped.
- **Copying blocks between pages**: a member can copy a block on one page and paste it on
  another page of the same site; the pasted copy gets new identifiers. Pasting between
  different sites is not supported.
- **Undo after autosave**: undo is not limited by saving; undoing a saved change produces a
  new unsaved change that is autosaved in turn.
- **Undo history across sessions**: it lasts for the editing session and is cleared when
  the editor is reloaded or another page is opened.
- **A token set saved before content widths existed**: it takes the default widths (720,
  1,200 and 1,440 pixels) until a designer saves the set again, so every Section and
  Container width choice always resolves.
- **A deleted colour token**: blocks that used it fall back to their default colour and
  keep the reference, so the draft stays valid; choosing a new colour clears the mark.
  Deleting the last remaining colour is refused (FR-031 requires at least one).
- **A token value that makes text unreadable** (for example text colour equal to
  background): allowed; the editor does not judge design choices in this feature.
- **Fonts**: only fonts that ship with the product or are generic system font stacks can
  be chosen, so the canvas and later published pages never fetch fonts from a third party
  (constitution Principle II: runs fully offline).
- **Embeds in the canvas** do not load the provider's player, so opening the editor never
  contacts a third party; a placeholder shows the provider and the address.
- **Two browser tabs of the same member on the same page**: treated like two members; the
  out-of-date rule in User Story 3 applies.
- **Recovery copies on a shared computer**: copies are kept per member and removed when the
  member signs out, so the next person to sign in on that browser never sees them. A
  recovery copy for a page that has since been deleted, or that the member may no longer
  edit, is discarded without being offered.
- **A disabled site** (feature 009): its members can keep editing drafts, as in feature 010.
- **Upgrade from the previous release**: existing drafts are valid version 1 documents that
  hold only the root and component references, so they stay valid and open in the editor
  unchanged. Sites get the default design tokens.

## Requirements *(mandatory)*

### Functional Requirements

**Editor shell and canvas**

- **FR-001**: The admin shell MUST provide an editor for a page's draft, opened from the
  page tree, and for a shared component's draft, opened from the component list. The
  editor MUST show the canvas, the block palette, the property panel, the layer view, the
  save status, undo and redo, and the preview width selector.
- **FR-002**: The canvas MUST show the draft as it will look to visitors, using the block
  definitions and the site's design tokens, apart from editing aids (selection outline,
  drop indicators, empty-state hints, embed placeholders) that are never part of the page.
- **FR-003**: Members MUST be able to select a block on the canvas or in the layer view;
  exactly one block is selected at a time, or none. The selection MUST be shown on the
  canvas and in the layer view together.
- **FR-004**: Members MUST be able to insert a block from the palette by dragging it to a
  position on the canvas or in the layer view, and by a keyboard action that inserts it
  after the selected block, or inside it if it takes children.
- **FR-005**: Members MUST be able to move, duplicate and delete the selected block, with
  all the blocks nested inside it, by pointer and by keyboard. Duplicates and pasted blocks
  MUST get new identifiers for every node they contain.
- **FR-006**: Every insert, move and paste MUST respect the nesting rules of FR-012; a
  place where the block is not allowed MUST be shown as such during a drag and MUST NOT
  accept the drop.
- **FR-007**: Members MUST be able to copy and paste blocks within a page and between pages
  and components of the same site, subject to FR-006 and FR-038.
- **FR-008**: Every editor action MUST be possible with the keyboard alone, and the editor
  MUST meet the translation-lookup and accessibility baselines of features 001 and 002.

**Block types and the page schema contract**

- **FR-009**: The editor MUST offer these ten content block types: Section, Container,
  Heading, Text, Rich Text, Image, Button, Spacer, Divider and Embed, and, in page
  documents only, the shared component reference from feature 010.
- **FR-010**: Each block type MUST be defined once, with its properties, their types,
  allowed values and defaults, its nesting rule, its editor controls and its label and
  icon in the palette. The palette, the property panel, validation and the contract
  document MUST all draw from that single definition, and adding a block type MUST NOT
  require changing any other (constitution Principle VI).
- **FR-011**: The ten block types MUST be added to the page schema contract
  (`docs/contracts/page-schema.md` and its schema file) as an additive change within
  schema version 1, with valid and invalid example documents for each type, so that every
  document valid before this feature stays valid. The product MUST validate saved drafts
  against the updated contract exactly as feature 010 does.
- **FR-012**: The nesting rules MUST be:
  - Section: only directly under the document root; holds any block except Section.
  - Container: anywhere except directly under the root; holds any block except Section.
  - Heading, Text, Rich Text, Image, Button, Spacer, Divider, Embed and the component
    reference: hold no children.
  - Under the document root: Sections and component references only, so every piece of
    content sits in a Section.
- **FR-013**: The block types MUST provide at least these properties:
  - **Section**: background colour token, vertical padding from the spacing scale, content
    width (Narrow, Normal, Wide or Full; Normal by default). Narrow, Normal and Wide cap the
    content at the site's content-width tokens and centre it; Full spans the page. A
    Section saved as "contained" before this choice existed is Normal.
  - **Container**: direction (vertical or horizontal), gap and padding from the spacing
    scale, alignment, background colour token, whether a horizontal layout stacks at
    mobile width (on by default), and a maximum width (Narrow, Normal or Wide from the
    site's content-width tokens, or Full by default). A Container narrower than its parent
    is placed by its alignment; it never grows wider than its parent. A horizontal
    Container also has a column pattern — Equal (default), 2/3 + 1/3, 1/3 + 2/3,
    3/4 + 1/4, 1/4 + 3/4 or 1/2 + 1/4 + 1/4 — that sets how each row is split between its
    blocks; blocks beyond the pattern start a new row with the same pattern. The pattern
    applies at desktop and tablet widths; at mobile width the row stacks when "stack on
    mobile" is on.
  - **Heading**: text (plain, up to 300 characters), level 1 to 6, alignment, colour token.
  - **Text**: text (plain, up to 5,000 characters, line breaks kept), alignment, colour
    token, size step from the typography scale.
  - **Rich Text**: structured rich text limited to paragraphs, bold, italic, links and
    bulleted and numbered lists, up to 20,000 characters of text; alignment; colour token.
  - **Image**: a media file of the same site, alternative text (up to 300 characters) or a
    decorative flag, width (full or natural), optional link.
  - **Button**: label (up to 100 characters), link, style (primary, secondary or outline),
    alignment, whether the link opens in a new tab.
  - **Spacer**: height from the spacing scale.
  - **Divider**: colour token, thickness (thin or thick), spacing above and below from the
    spacing scale.
  - **Embed**: the address of a video on a supported provider, an accessible title (up to
    200 characters), aspect ratio (16:9, 4:3 or 1:1).
  - **Every block**: an optional label for the layer view (up to 60 characters), never
    shown to visitors.
- **FR-014**: No block property MUST carry raw HTML, CSS or script. Rich text MUST be stored
  as structured content (constitution Principle IX). Values MUST be refused, both in the
  editor and on save, when they are outside the definition (length, allowed values,
  allowed address schemes, supported embed providers).
- **FR-015**: Links MUST point either to a page of the same site by its identifier, so they
  survive the page being moved or renamed, or to an external address with scheme `https`,
  `http`, `mailto` or `tel`.
- **FR-016**: Embeds MUST be limited to an allow-list of video providers, at least YouTube
  and Vimeo. Arbitrary embed code and arbitrary frame addresses MUST NOT be accepted;
  custom code blocks are out of scope (constitution Principle IX).
- **FR-017**: Images MUST be chosen from the site's media library (feature 003), where the
  member can also upload a new file if they hold the upload capability. An Image MUST refer
  to the media file by its identifier.

**Property panel**

- **FR-018**: The property panel MUST be generated from the selected block's definition
  (FR-010): one control per property, grouped as the definition says, showing current
  values and defaults. No block type MUST need a hand-written panel.
- **FR-019**: Text, Heading, Rich Text and Button labels MUST be editable directly on the
  canvas as well as in the panel, and both MUST always show the same value.
- **FR-020**: Colour, text size and spacing properties MUST offer the site's design tokens
  and store a reference to the chosen token, not its value. Fonts are chosen once per site,
  for headings and body text (FR-031), not per block.
- **FR-021**: An invalid value MUST be refused in the panel with a reason, and the block
  MUST keep its last valid value.

**Layer view**

- **FR-022**: The layer view MUST show every block of the document as a nested outline with
  its label or type, stay in step with the canvas selection, and scroll either view to the
  selected block.
- **FR-023**: Members MUST be able to reorder and re-parent blocks in the layer view by
  pointer and by keyboard, under the rules of FR-006, and to collapse and expand blocks with
  children. Collapsing affects the outline only.

**Undo and redo**

- **FR-024**: Every change to the document made in the editor (insert, move, duplicate,
  delete, paste, property change, text edit) MUST be undoable and redoable, restoring the
  document and the selection exactly. Continuous typing MUST be grouped into word- or
  pause-sized steps. A new change after an undo MUST discard the redo steps.
- **FR-025**: The undo history MUST hold at least 100 steps and last for the editing
  session of one document; it MUST be cleared when the editor is reloaded or another
  document is opened. Design token changes are not part of a page's undo history.

**Autosave and saving**

- **FR-026**: The editor MUST save the draft automatically, as a whole document, no more than
  3 seconds after the member stops making changes, and MUST also let the member save now.
  It MUST show whether the document is saved, saving, has unsaved changes, or failed to
  save.
- **FR-027**: A save that fails for a temporary reason MUST keep the changes, retry
  automatically with increasing delays, and be retryable by the member at once.
- **FR-028**: A save refused as out of date (feature 010, FR-021) MUST stop autosave for
  that document and switch the editor to a read-only view of the member's version, in
  which they can select blocks and copy them to the clipboard (FR-007). The editor MUST
  offer to reload the latest draft, after a warning that unsaved changes not copied will
  be lost, and the copied blocks MUST remain pasteable after the reload. The editor MUST
  NOT offer any way to overwrite the newer draft. Finer concurrent editing is left to #23.
- **FR-029**: A save refused as forbidden, not found, or invalid MUST stop autosave, show
  the reason and, for an invalid document, select the offending block when the reported
  location identifies one. The member's changes MUST stay in the editor until they leave.
- **FR-030**: Leaving the editor or closing the tab with unsaved changes MUST warn the
  member first. In addition, the editor MUST keep a copy of unsaved changes in the
  member's browser, per member and per document, updated as they edit. When the member
  next opens that document in the same browser and a copy newer than the saved draft they
  loaded exists, the editor MUST offer to restore it if the saved draft has not changed
  since, or to open it read-only for copying blocks (as in FR-028) if it has; it MUST NOT
  restore without asking and MUST NOT overwrite a newer draft. The copy MUST be discarded
  after a successful save, when the member declines it, and when the member signs out.

**Design tokens**

- **FR-031**: Each site MUST have one set of design tokens:
  - **Colours**: named colours, each with an identifier that never changes, a name unique
    in the site ignoring letter case, and a value; at least 1 and at most 24. Deleting a
    colour MUST be allowed after a warning that states how many blocks in the site's page
    and component drafts use it, counted at the time of the warning. Blocks that refer to
    a deleted colour MUST be shown with their block type's default colour, the property
    panel MUST mark the property as referring to a deleted colour, and such a reference
    MUST NOT make a draft invalid.
  - **Typography**: a heading font and a body font chosen from the fonts available in the
    product, a base text size, and a scale of text size steps.
  - **Spacing**: an ordered scale of named steps (for example none, extra small to extra
    large), each with an identifier that never changes and a size. Deleting a step follows
    the same rule as deleting a colour, with the block type's default spacing as the
    fallback.
  - **Content widths**: three fixed, named widths — Narrow, Normal and Wide — each a size
    in pixels from 320 to 2,560, with Narrow ≤ Normal ≤ Wide. They cannot be added or
    deleted, only resized, so every Section's width choice always resolves.
- **FR-032**: A new site, and every existing site on upgrade, MUST start with a default
  token set that makes all ten block types look complete and readable.
- **FR-033**: Changing a token's value MUST change every block that uses it, on the canvas
  of every page and component, without editing those blocks. Renaming a token MUST NOT
  break blocks that use it.
- **FR-034**: Design tokens MUST be kept as site-level draft settings: changing them MUST
  NOT change anything visitors see. How token changes are published belongs to #12.
- **FR-035**: Fonts offered for selection MUST ship with the product or be generic system
  font stacks, and the editor MUST NOT fetch fonts, scripts or embeds from third parties
  (constitution Principle II).

**Preview**

- **FR-036**: Members MUST be able to switch the canvas between desktop, tablet and mobile
  widths. Editing MUST work at every width, and changes apply at every width. A block MUST
  have one set of property values for all widths, and the panel MUST NOT offer values per
  width. Block layouts MUST adapt to the width as their definitions say (FR-013). The chosen width MUST be
  remembered per member.

**Shared components**

- **FR-037**: Members with the component capability MUST be able to open a shared
  component in the editor, with every editor function a page has, except that shared
  component references are not offered.
- **FR-038**: On a page's canvas, a component reference MUST show the component's current
  draft content in place, marked as shared, selectable and movable only as one unit, not
  editable in place, and with a way to open the component. The palette MUST offer the site's
  shared components for insertion into page documents.

**Authorization**

- **FR-039**: Opening the editor and saving drafts MUST use feature 010's capabilities:
  saving page drafts for pages, managing components for components. Holding "save page
  drafts" MUST allow every editor action on that page's draft; the editor MUST NOT restrict
  Editors to a subset of blocks or properties. Viewing a page in the
  editor without the save capability is out of scope; such members are not offered the
  editor.
- **FR-040**: Managing design tokens MUST be a new named, site-scoped capability held by
  Administrator on every site and by Designer on the sites where they have the role. All
  members who may view pages MUST be able to read the tokens, since the canvas needs them.
- **FR-041**: Every new operation MUST declare its capability, answer as not found for
  sites the user may not see and as forbidden on a permitted site where the capability is
  missing, following feature 002. Design tokens are site-owned records and MUST pass the
  shared isolation suite from feature 009.

**Refusals, operation and logs**

- **FR-042**: Every refusal MUST state a machine-readable reason (for example invalid
  property, disallowed link scheme, unsupported embed provider, nesting not allowed, limit
  exceeded, duplicate token name, too many colours, out of date), and the editor MUST show each
  as a clear message.
- **FR-043**: The upgrade from the previous release MUST keep every stored draft valid and
  unchanged and add the default design tokens to every site, proven by the automated
  upgrade test from a snapshot of the previous release.
- **FR-044**: The release smoke test MUST be extended to open a page in the editor, add a
  Section with a Heading, wait for autosave and confirm that the saved draft holds them.
- **FR-045**: Log output MUST refer to pages, components and tokens by identifier and MUST
  NOT include draft content or token values (constitution Principle IX).

### Key Entities

- **Block Type**: a named kind of content node (Section, Container, Heading, Text, Rich
  Text, Image, Button, Spacer, Divider, Embed) with one definition: its properties, their
  constraints and defaults, its nesting rule, its editor controls and palette entry. It
  extends feature 010's Node Type.
- **Block**: one node of a draft document of a given block type, with an identifier unique
  in its document, property values, an optional layer label and, for Section and
  Container, ordered child blocks.
- **Rich Text Content**: the structured, HTML-free representation of formatted text held by
  a Rich Text block and limited to the allowed formats.
- **Link**: a reference either to a page of the same site by identifier or to an external
  address with an allowed scheme.
- **Design Token Set**: a site's colours, typography, spacing scale and the three content
  widths (Narrow, Normal, Wide). Each colour and spacing step has an identifier that never
  changes, so blocks can refer to it; the content widths are fixed by name.
- **Editing Session**: one member's work on one document in the editor: the loaded draft
  version, unsaved changes, undo and redo history, selection, save status and preview
  width. It is not stored on the server beyond the drafts it saves; its unsaved changes are
  kept as a recovery copy in the member's browser until saved, declined or signed out.
- **Design Token Capability**: a new named, site-scoped permission to change a site's design
  tokens.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A designer who has not used the editor before can build a page with a hero
  Section (heading, text, image, button) and a two-column Section below it in under 10
  minutes on a fresh reference installation, without writing any JSON.
- **SC-002**: 100% of drafts saved by the editor across the automated test suite pass
  validation against the page schema contract, and every document the editor saves reads
  back equal as a JSON value and reopens showing the same page.
- **SC-003**: After any sequence of at least 50 mixed editor changes, undoing all of them
  returns a document equal to the starting one, and redoing all of them returns a document
  equal to the final one.
- **SC-004**: Changes are saved no more than 3 seconds after the member stops editing in
  95% of saves on the reference environment (two cores, four gigabytes of memory); across
  the test suite, zero changes are lost through a temporary network failure or through a
  tab closed with unsaved changes (recoverable on the next visit in the same browser), and
  zero drafts are overwritten by a concurrent save.
- **SC-005**: On a page of 500 blocks, selecting a block, changing a property and
  dropping a dragged block each show their result on the canvas within 100 milliseconds on
  the reference environment, and the editor opens the page within 3 seconds.
- **SC-006**: Every one of the ten block types can be inserted, edited, moved, duplicated
  and deleted with the keyboard alone, and 100% of the editor's visible text resolves
  through the translation lookup.
- **SC-007**: Changing a design token updates 100% of the blocks that use it on the canvas,
  with no block edited.
- **SC-008**: Opening and using the editor makes zero requests to hosts other than the
  installation itself.
- **SC-009**: The contract document and the schema definition the product validates
  against never disagree in a merged build, and every block type has at least one valid
  and one invalid golden example.

## Scope

### In Scope

- The editor shell and canvas for page drafts and shared component drafts.
- The block palette with the ten content block types and shared component references.
- Insert, move, nest, duplicate, delete, copy and paste of blocks by pointer and keyboard.
- The property panel generated from block definitions, and inline text editing.
- The layer view.
- Undo and redo within an editing session.
- Responsive preview at desktop, tablet and mobile widths.
- Autosave and manual save of drafts, with save status, retries and out-of-date handling.
- Site-level design tokens (colours, typography, spacing), their defaults and the screen to
  change them.
- The ten block types added to the page schema contract, with golden examples.
- The design token capability, isolation suite registration, upgrade migration and
  smoke-test extension.

### Out of Scope

- Collection binding, collection list blocks and templates (#16).
- Forms and form blocks (#18).
- Custom code blocks, arbitrary HTML, CSS or script, and embeds from providers outside the
  allow-list.
- Publishing, published copies, rendering for visitors and publishing design tokens (#12).
- Media library improvements such as variants, image cropping, and preventing the deletion
  of files in use (#13).
- Draft preview links for people without an account (#22).
- Real-time collaborative editing and presence (#23); this feature only guarantees that a
  concurrent save is never overwritten.
- Per-width property overrides (for example a different font size on mobile) and hiding
  blocks at certain widths.
- Navigation menu blocks (#24).
- Saved block templates, reusable styles or classes beyond design tokens, animations, and
  custom fonts uploaded by users.
- Viewing a page in the editor without permission to save its draft.
- Version history of drafts.

## Assumptions

- **Who edits what** follows feature 010's role split (see Clarifications): Editors and
  Designers can build and change all of a page's content in the editor, since both hold
  "save page drafts"; only Designers and Administrators edit shared components and design
  tokens. A content-only mode for Editors would be a later feature with its own
  capability.
- **Preview widths**: desktop 1,280 pixels, tablet 768 pixels, mobile 375 pixels. The
  canvas is scaled to fit when the browser window is narrower than the chosen width.
- **Responsive behaviour** comes from the block definitions (fluid widths, Containers that
  stack at mobile width), not from per-width settings; those are a later feature.
- **Save granularity** stays whole-document, as feature 010 defined it. The editor saves the
  entire draft on each autosave.
- **Design tokens have one draft state** in this feature. #12 decides whether tokens are
  published with pages or on their own.
- **Default tokens** are a neutral palette (brand, brand light, text, muted text,
  background, border), a system sans-serif font stack for both headings and body, a 16
  pixel base size, a spacing scale of none, extra small, small, medium, large, extra
  large and double extra large, and content widths of 720 (Narrow), 1,200 (Normal) and
  1,440 (Wide) pixels.
- **Supported embed providers** start with YouTube and Vimeo. Adding a provider is a change
  to the Embed block's definition, not a new block type.
- **Editor chrome is used on desktop-class screens** (at least 1,280 pixels wide). The admin
  shell stays usable on smaller screens, but building pages on a phone is not a goal.
- **Media usage**: an Image refers to a file by identifier; if the file is deleted the page
  shows a placeholder in the editor. Counting and protecting files in use belongs to #13.
- **The editor canvas technology** is chosen in this feature's plan and then recorded in
  the constitution's Technology Constraints, as the constitution requires ("The editor
  canvas library is decided in the editor-core spec").

## Dependencies

- Feature 001 (foundation skeleton): admin shell, translation lookup, smoke test.
- Feature 002 (identity and setup): capabilities, per-site grants, forbidden versus
  not-found rule, admin shell baselines.
- Feature 003 (media storage): the media library, upload and file identifiers for Image
  blocks.
- Feature 009 (sites and host name resolution): the site context and the shared isolation
  suite.
- Feature 010 (pages, page tree and shared components): pages, components, drafts, the
  page schema contract, the out-of-date rule and the page and component capabilities.
- The project constitution, in particular Principles II (runs offline), III (upgrades never
  lose data), VI (content schemas are versioned contracts; a block type is defined once),
  VII (draft and published are separate), IX (no raw HTML, encoded output, no custom code)
  and X (golden-file tests for every block type), and the Technology Constraints entry on
  the editor canvas library.
- GitHub issue #11, which this feature implements and which the pull request closes.
