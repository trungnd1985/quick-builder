# Feature Specification: Navigation and Menus

**Feature Branch**: `024-navigation`

**Created**: 2026-10-14

**Status**: Draft

**Issue**: #24

**Input**: User description: "#24 Navigation and menus. Problem / goal: Sites need menus that are managed in one place and reused across pages, and menu entries must be able to point at pages, collection items or external URLs. A user can build a multi-level menu, reorder it and place it in a block. In: named menus per site, hierarchical entries (up to three levels), entry targets (page reference, collection item reference, external URL, anchor), labels and attributes such as open-in-new-tab, drag-and-drop ordering, navigation block that renders a menu, link integrity when a target is unpublished or deleted, active-state detection during rendering, cache invalidation when a menu or a target changes. Out: mega-menu visual design beyond the standard block, per-role menus."

## Context

Features 009 to 017 give each site a page tree, a visual editor with ten block types, shared
components such as a header and footer, publishing with server-side rendering and exact
cache refreshes, collections whose items have their own addresses through templates, and
canonical addresses.

Today a site's navigation is built by hand from Button blocks inside the header component.
That has three problems:

- the same links must be rebuilt in the footer, a sidebar or a second header;
- there is no way to show a second or third level, such as "Services › Hosting › Managed";
- nothing tells a visitor which page they are on, because each button is a fixed link.

Specs 010 and 011 left navigation menus to this feature.

This feature adds:

- **menus**: named, ordered trees of entries of up to three levels, managed once per site;
- **entry targets**: a page, a collection item, an external address, an anchor, or no link
  (a label that only groups its children);
- a **Navigation block** that shows a menu anywhere a block can go, usually in the header
  component;
- **link integrity**: entries follow their targets when they move, and entries whose
  targets are not live are never shown as links;
- **active state**: the published page marks the entry for the current page and the
  entries that lead to it;
- **exact refreshes** of published pages when a menu or one of its targets changes.

Constitution Principle V applies because menus belong to one site. Principle VI applies
because the Navigation block is defined once like every other block and menus are stored in
a versioned format. Principle VII applies because published pages read only published menus
and live targets. Principle VIII applies because every page that shows a menu must be
refreshed exactly when the menu or a target changes. Principle IX applies because labels and
addresses are encoded for their context and external addresses are limited to allowed
schemes.

## Clarifications

### Session 2026-10-14

- Q: Do menu changes need an explicit publish, or do they go live when saved? → **A: draft
  and publish, like components**, with versions and rollback. Visitors see a menu change
  only after a member with the publish capability publishes the menu (FR-004, FR-004a).
- Q: What can an anchor target point at, given that blocks have no anchor names? → **A: a
  name the member types**, checked only for allowed characters. Blocks gain no anchor
  setting in this feature (FR-007a).
- Q: What happens when someone unpublishes or deletes a menu that live pages show, or
  publishes a page whose Navigation block points at a never-published menu? → **A: refused,
  as for components** (012 FR-003, FR-004), naming the menu or how many pages use it
  (FR-003, FR-003a).
- Q: How does a visitor open a submenu on a wide screen? → **A: with a separate open/close
  button beside the parent entry** (click, tap, Enter or Space). Pointing opens nothing, and
  the parent's label stays a link to its own page (FR-019).
- Q: Below what width does a collapsing Navigation block hide behind a menu button? → **A: a
  block setting, "collapse below"**: the editor's mobile preview width (default) or tablet
  preview width (FR-017).
- Q: Should submenus leading to the current page start open? → **A: yes, in the vertical
  layout and in an opened collapsed menu**; horizontal dropdowns always start closed
  (FR-019a).

### Session 2026-10-20

- Q: Where should menu styling be set: on each Navigation block, as site-wide menu styles in
  the design tokens, or both? → **A: both**: site-wide menu style defaults in the design
  tokens, which each Navigation block can override (FR-017, FR-017a).
- Q: Which looks should a menu entry be able to take? → **A: four**: plain link, underline,
  pill (rounded background on hover and current) and button (filled background with a
  border) (FR-017b).
- Q: Which colours should a menu style let designers set? → **A: bar background, text, hover
  text and background, current-entry text and background, focus ring, and the dropdown
  panel's background** (FR-017c).
- Q: Should visited links in a menu look different from unvisited ones? → **A: no**: visited
  entries look exactly like unvisited ones (FR-017c).
- Q: What should happen when a designer picks menu colours with too little contrast? → **A:
  warn, never refuse**: the editor names each failing pair, saving and publishing still
  work, and the focus ring cannot be removed (FR-017d).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Building a multi-level menu once (Priority: P1)

A Designer opens **Menus** for the client's site and creates a menu named "Main". They add
"Home" (the home page), "Services" (the Services page), and under Services, "Web design" and
"Hosting" (pages). Under Hosting they add "Managed hosting" (a page) and "Status" (an
external address, opening in a new tab). They add "Latest post" (a blog collection item),
"Contact" (the Contact page, at its "#form" anchor), and a label "Company" with no link that
groups "About" and "Careers". They leave the labels of page entries empty, so each one shows
its page's title. They create a second menu named "Footer" with four entries.

**Why this priority**: Menus are the reason for the feature. Without them there is nothing
to place in a block or to keep correct.

**Independent Test**: Create a menu with entries of every target type across three levels,
with and without own labels and with "open in new tab" on and off. Reload the admin and
confirm that the menu is stored exactly. Try to add a fourth level, an external address with
a disallowed scheme, an empty label on an entry that needs one, and a duplicate menu name,
and confirm each is refused with a reason.

**Acceptance Scenarios**:

1. **Given** a member with the Designer or Administrator role on a site,
   **When** they create a menu with a name,
   **Then** the menu is added to the site's list of menus, empty, and the name is unique
   within the site.
2. **Given** a menu,
   **When** the member adds an entry and chooses its target (a page, a collection item, an
   external address, an anchor, or no link),
   **Then** the entry is saved with that target, its label, and whether it opens in a new tab.
3. **Given** an entry whose target is a page or an item and whose label is empty,
   **When** the menu is shown,
   **Then** the entry's label is the page's title or the item's name, and follows changes to it.
4. **Given** an entry at the third level,
   **When** the member tries to add an entry below it,
   **Then** the action is not offered, and a save that contains a fourth level is refused.
5. **Given** a member with the Editor role,
   **When** they open Menus,
   **Then** they can see the menus but cannot create, change or delete them.

---

### User Story 2 - Placing a menu in a block and seeing it on every page (Priority: P1)

The Designer opens the header component in the editor, removes the old buttons and adds a
**Navigation** block. They pick "Main", choose a horizontal layout, show all three levels,
and turn on "Collapse on small screens". The canvas shows the menu; at the mobile preview
width it becomes a menu button. They publish the "Main" menu and the header. Every
published page that uses the header now shows "Main". A visitor on a phone opens the menu
button and the submenus; a visitor using only the keyboard reaches every entry.

**Why this priority**: A menu nobody sees has no value. Together with User Story 1 this is
the smallest useful feature: one menu, managed once, on every page.

**Independent Test**: Place Navigation blocks for two different menus in a component and in
a page, in each layout and level setting. Publish, then request the pages at desktop and
phone widths, with scripts on and off, and with the keyboard only. Compare the rendered
output with the expected golden output.

**Acceptance Scenarios**:

1. **Given** a page or component draft,
   **When** a member who may edit it adds a Navigation block,
   **Then** they can choose one of the site's menus, the layout (horizontal or vertical),
   how many levels to show (one to three), whether to collapse on small screens, and the
   alignment, and the canvas shows the chosen menu.
2. **Given** a published page that shows a Navigation block,
   **When** a visitor requests it,
   **Then** the menu is part of the server-rendered page as a labelled navigation region,
   with each live entry as a link to its target's current address.
3. **Given** a Navigation block showing more than one level,
   **When** a visitor clicks, taps or presses Enter or Space on the open/close button beside
   an entry that has children,
   **Then** its children are shown or hidden, the entry's label still links to its own
   target, and pointing at the entry without clicking opens nothing.
4. **Given** a Navigation block with "Collapse on small screens" on,
   **When** the page is shown in a viewport narrower than the block's "collapse below"
   width (mobile or tablet preview width),
   **Then** the menu is hidden behind a menu button that opens and closes it.
5. **Given** a visitor whose browser runs no scripts,
   **When** they view a page with a Navigation block,
   **Then** every entry of the shown levels is still reachable.
6. **Given** a Navigation block whose menu draft is changed,
   **When** a member with the publish capability publishes the menu,
   **Then** every published page that shows the menu, directly or through a component, shows
   the change without being republished, and no page changes before that publish.
7. **Given** a published menu with earlier versions,
   **When** a member with the publish capability rolls it back to one of them,
   **Then** every published page that shows the menu shows that version.
8. **Given** a page or component draft with a Navigation block for a menu that has never
   been published,
   **When** a member tries to publish it,
   **Then** the publish is refused, naming the menu, and the editor shows the block as
   showing an unpublished menu.
9. **Given** a menu that the live version of a page or component shows,
   **When** a member tries to unpublish or delete the menu,
   **Then** it is refused, stating how many published pages and components use it.
10. **Given** a site-wide menu style (pill look, a dark bar background and light text) and a
    footer Navigation block that overrides only the bar background,
    **When** the design and the pages are published,
    **Then** every Navigation block shows the pill look with the site-wide colours on hover,
    focus and the current entry, the footer shows its own background, visited entries look
    like the others, and a designer who picks text too close to its background is warned
    but can still publish (FR-017a to FR-017d).

---

### User Story 3 - Reordering and moving entries by drag and drop (Priority: P2)

The client wants "Contact" before "Company" and "Hosting" moved from "Services" to the top
level. The Designer drags "Contact" above "Company", drags "Hosting" out of "Services" with
its child, and drops "Careers" under "About". A drop that would make a fourth level is
shown as not allowed. A colleague who uses only the keyboard makes the same changes with
move keys.

**Why this priority**: Menus change as a site grows. Without reordering, every change means
deleting and recreating entries. It builds on User Story 1.

**Independent Test**: Reorder entries within a level, move entries with children between
levels and parents, and try drops that would exceed three levels or place an entry inside
itself. Repeat each with the keyboard only. Confirm the stored order after each step and
that refused moves change nothing.

**Acceptance Scenarios**:

1. **Given** a menu,
   **When** the member drags an entry to another position, under another entry, or out to
   a higher level,
   **Then** the entry moves with all its children, and the new order is saved.
2. **Given** a drag whose drop would place an entry deeper than the third level, counting
   its children, or inside itself,
   **When** the member hovers that drop position,
   **Then** the position is shown as not allowed, and dropping there changes nothing.
3. **Given** a member who uses only the keyboard,
   **When** they select an entry,
   **Then** they can move it up, down, into the previous entry and out to its parent's
   level with keys, and every move is announced to assistive technology.
4. **Given** an unsaved change to a menu,
   **When** the member undoes it,
   **Then** the menu returns to its previous order.

---

### User Story 4 - Links that stay correct when content changes (Priority: P2)

The Designer renames the slug of "Web design" to "websites" and moves "Hosting" under
"Support". The menu entries follow without being touched. An Editor unpublishes the
"Careers" page: on the next request, "Careers" disappears from the menu on every page. The
blog post behind "Latest post" is deleted: the entry disappears from the site, and in
Menus it is marked as broken, with the reason, so the Designer can point it at another post.

**Why this priority**: Visitors should never follow a menu link into a not-found page. It
builds on User Stories 1 and 2 and on 012's link rules.

**Independent Test**: For each target type, make the target live, change its address,
unpublish it, schedule it, delete it, and (for items) remove the collection's template.
After each step, request pages that show the menu on two instances. Confirm the rendered
links, the hidden entries and the status shown in the admin.

**Acceptance Scenarios**:

1. **Given** an entry whose target is a page or an item,
   **When** the target's address changes,
   **Then** the entry links to the new address from the next request, without any change to
   the menu.
2. **Given** an entry whose target is not live (never published, unpublished, scheduled
   but not yet published, or an item without a live address),
   **When** a page showing the menu is served,
   **Then** the entry is left out; if it has children that are shown, its label is shown
   without a link so that its children stay reachable.
3. **Given** an entry whose target was deleted,
   **When** a member opens the menu,
   **Then** the entry is marked as broken with the reason, and it is left out of the site as
   in scenario 2.
4. **Given** an entry whose target is not live,
   **When** the target is published again,
   **Then** the entry is shown again on every page that shows the menu.
5. **Given** a menu,
   **When** the member opens it,
   **Then** each entry shows whether its target is live, not published, or deleted.

---

### User Story 5 - Visitors see where they are (Priority: P2)

A visitor on "Managed hosting" sees "Managed hosting" marked as the current page, and
"Hosting" and "Services" marked as leading to it. On a blog post page, the "Blog" entry is
marked as the section they are in. Screen reader users hear which entry is the current page.

**Why this priority**: Orientation is a basic expectation of site navigation, and it must be
right without scripts because pages are rendered on the server.

**Independent Test**: For a menu whose entries point at the home page, nested pages, item
pages, external addresses and anchors, request each target page, a descendant page that is
not in the menu, an item page and a paged list. Confirm in the golden output which entries
are marked current, which as leading to it, and which are not marked.

**Acceptance Scenarios**:

1. **Given** a page that is the target of an entry,
   **When** the page is served,
   **Then** that entry is marked as the current page, visibly and for assistive technology,
   and each of its ancestor entries in the menu is marked as containing the current page.
2. **Given** a page that is not the target of any entry but lies below a page that is,
   **When** the page is served,
   **Then** the entry of its nearest ancestor page is marked as the current section, and the
   home page entry is not marked unless the home page itself is served.
3. **Given** an item page,
   **When** it is served,
   **Then** an entry targeting that item is marked as the current page; otherwise the entry
   of the nearest page above the item's address is marked as the current section.
4. **Given** an entry with an external address, an anchor only, or no link,
   **When** any page is served,
   **Then** that entry is never marked as current.
5. **Given** a vertical Navigation block, or a collapsed one whose menu button is opened,
   **When** a page three levels down is served (for a horizontal block, with scripts),
   **Then** the submenus leading to its entry are open and all others are closed; in a
   horizontal block every submenu starts closed.

---

### Edge Cases

- **The same target appears in two entries**: both are marked as current.
- **An entry with "shown" off**: it and all its children are left out of the site, and it
  is never marked as current.
- **An entry's target is a collection's template page** (016): a template page has no
  address of its own, so the entry is never live. It is handled like an unpublished target
  (FR-023). The page picker does not offer template pages; choose an item, or the
  template's parent page.
- **Entry pointing at the home page**: it is current only on the home page, never as a
  section.
- **A label containing quotes, angle brackets or script-like text**: shown as text; never
  breaks out of an attribute or the page.
- **External address with a scheme other than `https`, `http`, `mailto` or `tel`**, or a
  protocol-relative address: refused (011 FR-015).
- **External address on one of the site's own host names**: allowed and kept as typed; it is
  not followed when pages move, and the admin suggests pointing at the page instead.
- **Anchor whose name does not exist on the target page**: the link still goes to the page,
  which opens at its top. Anchor names are typed and not checked (FR-007a).
- **Anchor typed with a leading `#`**: the `#` is removed; other disallowed characters are
  refused.
- **A draft whose Navigation block's menu was deleted** (possible only while no live
  version used the menu, FR-003): the editor marks the block as missing its menu, and the
  draft cannot be published until another menu is chosen or the block is removed.
- **A scheduled publish of a page whose menu is unpublished when the schedule runs**: it
  fails as for a missing component (012), and the failure names the menu.
- **A Navigation block that shows fewer levels than the menu has**: deeper entries are not
  shown, and active state still marks the shown ancestor entry as containing the current
  page.
- **A menu whose entries are all not live**: the Navigation block renders nothing, and no
  empty navigation region is left in the page.
- **A menu on a page that is served while the menu's target pages are unpublished in the
  same publish**: the page never shows a link to a page that is no longer live once the
  refresh has completed.
- **A target page under an unpublished parent**: it is live (012), so it is shown.
- **An item whose collection's template is unpublished or removed**: the item has no live
  address, so its entry is left out, and the admin shows "no page for this item".
- **Two members editing the same menu**: the second save is refused as changed by someone
  else, as for other admin records.
- **Deleting a page that a menu entry targets**: the deletion warning lists the menus that
  point at it, as it already lists other references.
- **Moving a page into trash and restoring it (#21)**: the entry points at it again, because
  it refers to the page, not its address.
- **The not-found page**: it does not include components (012 FR-030), so it shows no menu.
- **A site-wide menu style default changes while a block overrides some of it**: the block
  keeps its overrides and follows the new defaults for the rest; published pages change only
  when the design is published (FR-017a).

## Requirements *(mandatory)*

### Functional Requirements

**Menus**

- **FR-001**: Members with the Designer or Administrator role on a site MUST be able to list,
  create, rename, duplicate and delete the site's menus. Members with the Editor role MUST be
  able to view menus and choose one in a Navigation block, but not change menus.
- **FR-002**: A menu MUST have a name, unique within its site regardless of letter case, of
  at most 100 characters. A site MUST be able to hold at least 50 menus, and a menu at least
  300 entries.
- **FR-003**: Unpublishing or deleting a menu MUST be refused, stating how many published
  pages and components use it, while the live version of any page or component has a
  Navigation block for it (as 012 FR-004 does for components). Otherwise, deleting MUST first
  list the drafts that still use the menu and ask for confirmation; those drafts' Navigation
  blocks are then marked in the editor as missing their menu.
- **FR-003a**: Publishing a page or component, by hand or by a schedule, MUST be refused,
  naming the menus, when its draft has a Navigation block for a menu that has no published
  version or no longer exists (as 012 FR-003 does for components). Rolling back a page or
  component to a version whose Navigation block refers to a deleted menu MUST be refused,
  naming the menu.
- **FR-004**: A menu MUST keep draft and published state separately, like components (012).
  Saving changes only the draft. Changes MUST reach visitors only when a member with the
  publish capability publishes the menu; until then, published pages MUST keep showing the
  published menu. Publishing MUST be atomic and recorded, and MUST produce a version.
- **FR-004a**: Members with the publish capability (Designers and Administrators, 012) MUST
  be able to unpublish and schedule the publish of a menu, see its publish history (who
  published and when), and roll it back to an earlier version, as for components (012).
  Unpublishing follows FR-003. The menu editor MUST show whether the draft differs from the
  published menu.
- **FR-005**: Every creation, change and deletion of a menu MUST be recorded with who made it
  and when, without labels or addresses in the record (constitution Principle IX).

**Entries and targets**

- **FR-006**: A menu entry MUST have a target, a label, whether it opens in a new tab, and
  whether it is shown (an entry can be hidden without deleting it). Entries form a tree of at
  most three levels.
- **FR-007**: An entry's target MUST be one of:
  - a **page** of the same site, by its identifier, so it survives moves and renames;
  - a **collection item** of the same site, by its identifier, linking to the item's live
    address (016);
  - an **external address** with scheme `https`, `http`, `mailto` or `tel` (011 FR-015);
  - an **anchor**: a name on the current page, or a name combined with a page target
    (FR-007a);
  - **no link**: a label that only groups its children.
- **FR-007a**: An anchor name MUST be typed by the member, without the leading `#`. It MUST
  be 1 to 100 characters, start with a letter, and contain only letters, digits, hyphens
  and underscores. The product MUST NOT check whether the name exists on the page, and the
  admin MUST say so next to the field.
- **FR-008**: A label MUST be at most 100 characters. It is required for external, anchor
  and no-link entries. For page and item entries it is optional, and when empty the entry
  shows the page's title or the item's name as published.
- **FR-009**: "Open in new tab" MUST be available for every entry that links. Such links
  MUST NOT give the opened page access to the site's page, and MUST tell assistive
  technology that they open a new tab (012 FR-025).
- **FR-010**: Choosing a page or item target MUST use the same pickers as other links in the
  admin (011 links, 015 item picker), limited to the menu's site.
- **FR-011**: Saving a menu MUST refuse a fourth level, a missing required label, a
  disallowed address scheme, a target on another site, or more entries than the limit. The
  refusal names the first failure and the entry it concerns, and nothing of a refused save is
  kept. Before saving, the menu editor MUST show the problem of every entry that has one.

**Ordering**

- **FR-012**: Members who may change menus MUST be able to reorder entries and move them
  between parents and levels by drag and drop, with all children moving along.
- **FR-013**: Every drag action MUST have a keyboard equivalent (move up, move down, indent
  into the previous entry, outdent), and every move MUST be announced to assistive
  technology (constitution Technology Constraints, editor canvas).
- **FR-014**: A drop or key move that would exceed three levels, counting the moved entry's
  children, or place an entry inside itself MUST be shown as not allowed and change nothing.
- **FR-015**: Changes in the menu editor MUST be undoable until saved, and the editor MUST
  warn before leaving with unsaved changes.

**Navigation block**

- **FR-016**: A Navigation block type MUST be added to the editor's palette, defined once
  with its properties, defaults, editor controls and renderer like the other block types
  (constitution Principle VI). Adding it MUST NOT change other block types.
- **FR-017**: The Navigation block MUST have these properties: menu, layout (horizontal or
  vertical), levels shown (one to three), collapse on small screens (on or off) with the menu
  button's label and "collapse below" (the editor's mobile preview width, the default, or
  its tablet preview width), alignment, and an accessible name that defaults to the menu's name as of its last publish (a rename
  reaches visitors when the menu is next published). It MUST
  use the site's design tokens for colours, type and spacing, and its style follows FR-017a.
- **FR-017a**: Menu styles MUST be set in two places: site-wide menu style defaults in the
  site's design tokens, published with the design like the other tokens, and per-block
  overrides on the Navigation block. A block property left unset MUST follow the site-wide
  default, so changing the default restyles every Navigation block that has not overridden
  it. *(Clarified 2026-10-20.)*
- **FR-017b**: Entries MUST take one of four looks, chosen per menu style (site-wide default,
  block override): **link** (text only), **underline** (an underline on hover, focus and the
  current entry), **pill** (a rounded background on hover, focus and the current entry) and
  **button** (a filled background with a border). The look applies to every entry of the
  block's top level; submenu entries keep a plain list look. Free per-state styling
  (designers setting their own borders, radii or padding per state) is not offered.
  *(Clarified 2026-10-20.)*
- **FR-017c**: A menu style MUST let designers set these colours, each a colour token of the
  design: the bar's background, the entries' text, hover text, hover background, current
  entry text, current entry background, the focus ring, and the dropdown panel's background
  (used for horizontal submenus and the opened small-screen panel). The button look's border
  MUST use the entries' text colour. The existing `color` and `activeColor` block properties
  are the text and current-entry text colours. Visited entries MUST look exactly like
  unvisited ones: no visited colour is offered, and the browser's visited colour MUST NOT
  show. *(Clarified 2026-10-20.)*
- **FR-017d**: Where menu colours are chosen (the design tokens' menu style and the block's
  panel), the editor MUST warn, naming each pair, when the text, hover, current-entry or
  dropdown colours fall below 4.5:1 contrast against their backgrounds, or the focus ring
  below 3:1 against the bar. The warning MUST NOT block saving or publishing. Every entry
  and open/close button MUST show a visible focus ring when focused by keyboard, whatever
  the style; no setting removes it. *(Clarified 2026-10-20.)*
- **FR-018**: In the editor canvas, the Navigation block MUST show the chosen menu's draft, MUST NOT navigate when an entry is clicked, and MUST offer a way to open the menu in
  Menus for members who may change it.
- **FR-019**: On the site, the Navigation block MUST render a labelled navigation region with
  nested lists of entries. Each entry with shown children MUST have its own open/close
  button beside its label, following the accessible disclosure pattern: click, tap, Enter
  or Space toggles the children, the button tells assistive technology whether they are
  open, and its accessible name includes the entry's label. Pointing at an entry MUST NOT
  open it. The label of a linked entry stays a link to its target. A no-link entry's label
  MUST act as its open/close button. Escape MUST close the open submenu and return focus to
  its button, and opening a submenu MUST close other open submenus at the same level.
- **FR-019a**: In the vertical layout, and in a collapsed menu once its menu button is
  opened, submenus that contain the current page or current section (FR-027, FR-028) MUST
  start open, and all other submenus MUST start closed. In the horizontal layout, every
  submenu MUST start closed. This starting state MUST be produced on the server. The one
  exception is a horizontal block that collapses: its submenus on the current path open when
  the menu button opens the panel, which needs scripts. Without scripts they start closed,
  and every entry stays reachable (FR-020). *(Amended 2026-10-14 after analysis.)*
- **FR-020**: Without scripts, every entry of the shown levels MUST stay reachable. Any
  script the block needs MUST be progressive enhancement, and MUST add at most 10 KB to a
  published page (constitution Principle VIII).
- **FR-021**: The Navigation block and the menu format MUST be documented in the page schema
  contract and the shared schema package, with a schema version, so that the editor and the
  renderer validate them against the same definition.

**Link integrity**

- **FR-022**: A page or item entry MUST link to its target's current live address with the
  site's base URL, normalised as canonical addresses are (017 FR-011).
- **FR-023**: An entry whose target is not live (not published, scheduled but not yet
  published, deleted, or an item with no live address) MUST be left out of the rendered menu.
  If it has children that are shown, its label MUST be shown without a link instead.
- **FR-024**: A no-link entry with no shown children MUST be left out. A Navigation block
  with no shown entries MUST render nothing.
- **FR-025**: In Menus, each entry MUST show the state of its target: live, not published, or
  deleted. Entries whose target was deleted MUST be marked as broken and listed at the top of
  the menu editor, so they can be retargeted or removed.
- **FR-026**: Warnings before deleting or unpublishing a page or item MUST list the menus
  with entries that point at it.

**Active state**

- **FR-027**: When a published page or item page is rendered, an entry MUST be marked as the
  current page when its target is that page or item. Ancestor entries of a current entry
  MUST be marked as containing the current page.
- **FR-028**: When no entry targets the rendered page, the entry whose target page is the
  nearest ancestor of the rendered page's address MUST be marked as the current section. The
  home page entry MUST be current only on the home page.
- **FR-029**: Active state MUST ignore query strings and anchors, so a paged list (016
  FR-027) marks the same entry as its first page. External, anchor-only and no-link entries
  MUST never be current.
- **FR-030**: The current page MUST be marked visibly and for assistive technology, and
  active state MUST be produced on the server, without scripts.

**Caching and refresh**

- **FR-031**: Each published page and item page MUST record the menus it shows, directly or
  through components, and the targets of those menus, in the dependency record of 012 FR-032.
- **FR-032**: Pages that show a menu MUST be refreshed when the menu is published or
  rolled back, including by a schedule, and when a target of the menu is published, unpublished,
  moved, renamed (for entries without their own label), or deleted. Pages that do not show
  the menu MUST NOT be refreshed.
- **FR-033**: Refreshes MUST reach every instance within 5 seconds when a shared cache is
  configured (012 FR-033), and MUST NOT serve a link to a target that is no longer live once
  the refresh has completed. Any event handlers involved MUST be idempotent.
- **FR-034**: Pages that show menus MUST stay within 012's render budgets.

**Isolation and portability**

- **FR-035**: Menus MUST belong to exactly one site, MUST be separated by site as every
  site-owned record (constitution Principle V), and MUST be covered by the site isolation
  suite. An entry MUST NOT target a page or item of another site.
- **FR-036**: Export and import of a site (constitution Principle III) MUST include menus,
  with entries' targets mapped to the imported pages and items. This feature defines what an
  export carries; building export and import is the obligation of #20.

### Key Entities

- **Menu**: belongs to one site. It has a name, an ordered tree of entries, who created and
  changed it and when, a draft, a published state, and its published versions.
- **Menu Entry**: belongs to one menu and has at most one parent entry. It holds a position
  among its siblings, a target, an optional label, "open in new tab" and "shown". Its derived
  state is the target's status: live, not published or deleted.
- **Entry Target**: one of page (by identifier), collection item (by identifier), external
  address, anchor (alone or with a page), or no link.
- **Navigation Block**: a block type placed in pages or components. It refers to one menu and
  holds layout, levels shown, collapse on small screens with its "collapse below" width,
  alignment and accessible name.
- **Menu Dependency**: part of the dependency record of 012. It links a published page to
  the menus it shows and to those menus' targets, so that changes refresh exactly the
  affected pages.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A Designer who has used the page editor can build a menu of 15 entries over
  three levels and place it in the header component in under 5 minutes, without help.
- **SC-002**: After a menu is published or rolled back, or a target is published, unpublished, moved or
  deleted, 100% of the published pages that show the menu reflect it on every instance within
  5 seconds, and no page that does not show the menu is refreshed.
- **SC-003**: Across a test matrix of every target type in every state (live, moved, not
  published, scheduled, deleted, item without a page), no published page contains a menu
  link to a target that is not live.
- **SC-004**: Across a test matrix of current pages, descendant pages, item pages and paged
  lists, 100% of rendered menus mark exactly the expected current, containing and section
  entries.
- **SC-005**: On the reference environment, a page showing two menus of 300 entries each
  stays within 012's render budgets on a cache miss and a cache hit.
- **SC-006**: Every entry of a published menu can be reached and opened with the keyboard
  only, by touch, and with scripts turned off, and the menu editor's ordering can be done
  with the keyboard only, meeting the accessibility baseline of 011.
- **SC-007**: 100% of Navigation block outputs in the golden-file test encode every label
  and address for its context.
- **SC-008**: For each of the four entry looks, golden files show the site-wide colours and
  a block's overrides on hover, focus and the current entry, with no visited style; and the
  editor warns for 100% of colour pairs below the FR-017d contrast minimums in its test
  matrix, while refusing none.

## Scope

### In Scope

- Named menus per site, with entries in up to three levels.
- Entry targets: page, collection item, external address, anchor, and no link.
- Labels (own or taken from the target), "open in new tab", and hiding an entry.
- Ordering by drag and drop with keyboard equivalents.
- A Navigation block with layout, levels, collapse on small screens and alignment.
- Link integrity for moved, unpublished and deleted targets, and target status in the admin.
- Active state produced on the server.
- Exact refreshes of pages when a menu or a target changes.

### Out of Scope

- Mega menus or visual designs beyond the standard Navigation block (images, columns or
  blocks inside a menu).
- Menus that differ by visitor role or sign-in.
- Menus generated automatically from the page tree or from a collection.
- Breadcrumb blocks (017 already adds breadcrumb structured data).
- Localised menus and per-language entries.
- Per-entry icons, badges, custom classes or styles.
- Anchor names on blocks, and checking that a typed anchor exists on its page (clarified
  2026-10-14).

## Assumptions

- **Roles** follow 010 to 017: "Designer builds, Editor writes". Menus shape the site's
  structure, so Designers and Administrators manage them. Editors may place an existing menu
  in a Navigation block in a page draft, as they may place any block.
- **Three levels** count the top level as the first.
- **Hidden over broken**: an entry whose target is not live is left out rather than shown as
  plain text, because a menu of dead labels confuses visitors more than a shorter menu. This
  differs from links in text (012 FR-026), where removing words would change the sentence.
- **Labels from targets**: an entry without its own label shows the published title, so a
  page renamed in a draft does not change the menu until the page is published.
- **Section highlighting** uses the page tree (address ancestry), so a page that is not in
  the menu still shows where it belongs.
- **No automatic menus**: a menu is always built by hand. New pages are not added to menus
  automatically.
- **Limits** (50 menus per site, 300 entries per menu, 100-character labels) are defaults
  that can be raised later.
- **Upgrade**: existing sites have no menus, and existing pages and components are not
  changed. Button-based navigation keeps working until a Designer replaces it.

## Dependencies

- Feature 001 (foundation skeleton): the admin shell, its navigation and translation baseline.
- Feature 009 (sites and host name resolution): the base URL, site isolation and the
  isolation suite.
- Feature 010 (pages, page tree and shared components): page identifiers, addresses,
  titles, the page tree, components and the page schema contract.
- Feature 011 (editor core): block definitions, the palette, the property panel, link rules,
  keyboard equivalents for drag actions, and the accessibility baseline.
- Feature 012 (publishing and rendering): publish, the dependency record, refresh rules,
  render budgets, link rules and the not-found page.
- Features 015 and 016 (collection items and templates): the item picker, item names and
  item addresses.
- Feature 017 (SEO): normalised canonical addresses.
- The project constitution, in particular Principles V (site isolation), VI (block
  definitions and contracts), VII (published data only), VIII (caching and budgets), IX
  (encoding and allowed schemes) and X (golden-file tests for blocks).
- GitHub issue #24, which this feature implements and which the pull request closes.
