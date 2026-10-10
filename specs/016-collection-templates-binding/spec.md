# Feature Specification: Collection Templates and Data Binding

**Feature Branch**: `016-collection-templates-binding`

**Created**: 2026-10-10

**Status**: Draft

**Issue**: #16

**Input**: User description: "#16 Collection templates and data binding. Problem / goal: Collection content has to appear on the site without designing a page per item. A designer can turn a page into a template for a collection so every item gets its own URL, bind block properties to item fields, and place list blocks that show items with filtering, sorting and paging. In: collection template pages with a URL pattern such as /blog/{slug}, binding expression format and its contract document, binding UI in the property panel, collection list block with filter/sort/limit and pagination, nested binding scope inside lists, empty and error states, rendering and caching of item pages, invalidation when an item is published, route matching order (static page, then template, then 404). Out: SEO metadata templates (#17), cross-site content reuse."

## Context

Features 010 to 012 give each site a page tree, a visual editor with ten block types, and
publishing with server-side rendering, caching and exact cache refreshes. Features 014 and
015 add collections: a schema of fields, and items that Editors write, publish, unpublish
and schedule, with their draft and published data kept apart. Feature 035 widened rich
text. Nothing yet shows an item to a visitor: every published page holds only content
typed into it by hand.

This feature connects the two. A Designer turns a page into a **template** for a
collection, and every published item of that collection gets its own address built from
the template's place in the page tree and the item's slug, for example `/blog/hello-world`.
Inside the template, block properties are **bound** to the item's fields, so the heading
shows the item's title and the image shows its cover. On any page, a **collection list
block** shows items of a collection, filtered, sorted, limited and split into pages, and
the blocks inside it are bound to each listed item in turn.

Constitution Principle VI names the binding expression format as a shared, versioned
contract between the editor and the renderer; this feature writes it next to the page and
collection schema contracts. Principle VII means visitors see only published items, with
their published data. Principle VIII means item pages and pages with lists are cached and
refreshed exactly when an item, template or schema they depend on changes, using the
dependency graph that 012 began (page to components) extended with page to collections
and template to items.

## Clarifications

### Session 2026-10-10

- Q: Should visitors be able to filter or sort a list themselves, or does only the
  designer set filters and sorting, with visitors only changing pages? → **A: only the
  designer.** Members set filters and sort keys in the editor; visitors only move between
  pages (FR-023, FR-027).
- Q: What does a visitor get at an item's old address after its slug changes or its
  template moves? → **A: the not-found page.** No old slugs are kept; redirects from old
  addresses come with #17 (FR-008).
- Q: May a collection that pages or components use be deleted? → **A: no, refused.** The
  refusal lists every page and component using it in a draft, live version or pending
  scheduled content, matching 014's and 015's delete rules (FR-010).
- Q: Does the template preview in the editor show the sample item's draft or published
  values? → **A: its draft values, marked as a draft.** Any item can be the sample; lists in
  the canvas still show published data (FR-020).
- Q: How far may a binding follow references from an item? → **A: one step, through a
  single reference only.** Multi-reference fields are reached only as a list's source
  (FR-016, FR-025).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A Designer turns a page into a template and every item gets its own page (Priority: P1)

The site has a published "Blog" page at `/blog` and a "Blog posts" collection with 40
published posts. The Designer creates a page under "Blog", chooses "Use as template for a
collection", and picks "Blog posts". The page's address becomes `/blog/{slug}`. In the
editor they choose a sample post to preview, then bind the Heading to the post's Title, the
Image to its Cover image and a Rich text block to its Body. They publish the template. A
visitor opens `/blog/hello-world` and sees that post, and `/blog/autumn-update` shows
another. `/blog/not-a-post` shows the not-found page.

**Why this priority**: This is the core promise of the issue: collection content reaches
visitors without designing a page per item. On its own it delivers a working blog or
product catalogue with one page design.

**Independent Test**: In a collection with published, draft-only and unpublished items,
make a page under a published parent the collection's template, bind three blocks to
three fields, and publish it. Confirm that each published item's address serves complete
HTML with that item's published values, that draft-only, unpublished and unknown slugs get
the not-found response, and that a static page at an address matching the pattern is
served instead of the item.

**Acceptance Scenarios**:

1. **Given** a page with no child pages and a member with the Designer or Administrator
   role,
   **When** they make it the template of a collection of the same site,
   **Then** its address pattern becomes its parent's address followed by `{slug}`, it is
   shown in the page tree as a template of that collection, and it no longer has an address
   of its own.
2. **Given** a published template page and a published item of its collection,
   **When** a visitor requests the template's pattern with the item's slug,
   **Then** they receive a complete page rendered from the template's live version with
   every bound property showing the item's published value.
3. **Given** a template page,
   **When** a visitor requests its pattern with a slug that belongs to no item, to an item
   with no published data, or to an item of another collection or site,
   **Then** they receive the not-found page, which does not reveal whether a draft item
   exists at that address.
4. **Given** a static published page whose address matches a template's pattern (for
   example a page `/blog/archive` next to the template `/blog/{slug}`),
   **When** a visitor requests that address,
   **Then** the static page is served, even when an item has the slug `archive`.
5. **Given** a template page that is not published, or was unpublished,
   **When** a visitor requests any address matching its pattern,
   **Then** they receive the not-found page.
6. **Given** a collection that already has a template,
   **When** a Designer tries to make another page its template,
   **Then** it is refused with a reason naming the existing template.
7. **Given** a template page in the editor,
   **When** the Designer chooses a sample item,
   **Then** the canvas shows every bound property with that item's values, and the choice
   is kept for the member without changing the page's draft.

---

### User Story 2 - Binding block properties to item fields in the property panel (Priority: P1)

On the post template, the Designer selects the Heading and, next to its Text property,
chooses "Bind to field". A list offers the fields that can fill a text property: Title,
Summary, Category, Published on, Reading time. They pick Title. For a byline Text block they
combine fixed text with values: "By {Author → Name}, {Published on}", with the date shown as
"10 October 2026". For the Image they pick Cover image and set Alt text to Title. For an
optional Subtitle field they choose "Hide this block when empty". The panel shows each bound
property with the field's name and a way to unbind it.

**Why this priority**: Without binding, a template shows the same content for every item.
It is the second half of User Story 1 and the mechanism lists reuse.

**Independent Test**: For every block property that can be bound and every field type,
bind it through the property panel, using the keyboard only for one of each. Confirm the
panel offers exactly the compatible fields, that the canvas and the published page show
the expected value for a sample item, including formatted dates and numbers, combined text,
reference values, empty values and fallbacks, and that the stored page refers to fields by
their identifiers.

**Acceptance Scenarios**:

1. **Given** a block inside a binding scope (a template page or a list block),
   **When** the member opens the binding control of a property,
   **Then** they are offered only the fields and built-in item values whose type can fill
   that property (FR-014), grouped by scope when more than one is available.
2. **Given** a bound property,
   **When** the panel shows it,
   **Then** it shows the field's current display name, the scope it comes from, any
   formatting, and controls to change or remove the binding; removing it restores the
   property's previous fixed value.
3. **Given** a text property,
   **When** the member combines fixed text with one or more bound values,
   **Then** the canvas and the published page show the combined text with each value in
   place.
4. **Given** a date or number bound to a text property,
   **When** the member chooses a format,
   **Then** the value is shown in that format in the site's default language.
5. **Given** a single reference field,
   **When** the member binds a property to a field of the referenced item (for example
   Author → Name),
   **Then** the value of the referenced item's published data is shown, or the property is
   treated as empty when the referenced item has no published data.
6. **Given** a bound property whose value is empty for an item,
   **When** the item is shown,
   **Then** the block follows its empty-value choice: show the fallback value the member
   set, or hide the block (FR-019).
7. **Given** a block outside any binding scope,
   **When** the property panel is shown,
   **Then** no binding control is offered.

---

### User Story 3 - Visitors always see the current published item, fast (Priority: P1)

The Editor fixes a typo in "Hello world" and publishes it. Within seconds every visitor
sees the corrected post at `/blog/hello-world` and in the "Latest posts" list on the home
page; the other 39 post pages stay cached. When the Editor unpublishes a post, its address
gives the not-found page and it disappears from every list. When the Designer publishes a
new version of the template, every post page shows the new design.

**Why this priority**: Principle VIII requires rendered output to be cached and refreshed
exactly. A template that shows stale or unpublished items breaks Principle VII.

**Independent Test**: Warm the cache for a template with 100 item pages and five static
pages, two of which list the collection. Publish, unpublish, roll back, schedule and delete
items; publish the template; change the schema. After each, confirm which pages are
refreshed and which stay cached, against FR-033. Repeat with two instances and a shared
cache.

**Acceptance Scenarios**:

1. **Given** a cached item page,
   **When** that item is published, unpublished, rolled back, deleted, or reaches a
   scheduled publish or unpublish,
   **Then** from the refresh on, its address serves the new state, and item pages of other
   items that do not depend on it stay cached.
2. **Given** pages that contain list blocks of a collection,
   **When** any item of that collection changes its published state or published data,
   **Then** those pages are refreshed.
3. **Given** item pages that show values of a referenced item,
   **When** the referenced item's published data changes or it is unpublished,
   **Then** those item pages are refreshed.
4. **Given** a template page,
   **When** it is published, unpublished or rolled back, or a component or the site design
   it uses is published,
   **Then** every item page of its collection is refreshed.
5. **Given** a collection whose schema changes,
   **When** the change is applied,
   **Then** every page that binds to that collection or lists it is refreshed.
6. **Given** an item page that is not cached,
   **When** it is requested,
   **Then** it is served within the same budgets as a static page of the same size (SC-004).

---

### User Story 4 - Showing items with a collection list block (Priority: P2)

On the home page the Designer drops a Collection list block, picks "Blog posts", filters to
items whose Featured switch is on, sorts by Published on, newest first, and limits it to 6.
Inside the list they design one card: an Image bound to Cover image, a Heading bound to
Title, a Text bound to Summary and a Button whose link is the post's page. The canvas shows
six cards. They design the list's empty state: "No featured posts yet."

**Why this priority**: Lists are how visitors find items. Item pages alone are reachable
only by typed links. It builds on binding from User Story 2.

**Independent Test**: In a collection with items covering every filterable field type, add
list blocks with each filter operator, each sort key in both directions and different
limits. Confirm the published page shows exactly the expected published items in the
expected order, that the card blocks show each item's values, that the item link goes to
the item's page, and that an empty result shows the empty state.

**Acceptance Scenarios**:

1. **Given** the editor,
   **When** a member places a Collection list block and chooses a collection of the site,
   **Then** the block repeats its card content once per listed item, and blocks inside the
   card can be bound to that item's fields.
2. **Given** a list block,
   **When** the member adds filter conditions (FR-023),
   **Then** only published items meeting every condition are listed.
3. **Given** a list block,
   **When** the member sets up to three sort keys with a direction each,
   **Then** items are listed in that order, items without a value in a key come last, and
   items equal in every key keep a stable order.
4. **Given** a list block with a limit,
   **When** more items match than the limit,
   **Then** only the first items up to the limit are shown.
5. **Given** a list block whose filters match no published item,
   **When** the page is shown,
   **Then** the list's empty-state content is shown instead of cards.
6. **Given** a Button or link inside a card,
   **When** its link is bound to "Item page",
   **Then** it links to the item's address under the collection's published template, or is
   rendered as plain text when the collection has no published template.

---

### User Story 5 - Paging through a long list (Priority: P2)

The "All posts" page lists the 40 posts 10 at a time. Below the list a visitor sees
"Previous", page numbers and "Next". Page 3 has its own address, `/blog?page=3`, which works
without scripts and can be shared. A second list on the same page, "Popular tags", pages
independently.

**Why this priority**: Long collections cannot be shown on one page. It extends User
Story 4.

**Independent Test**: On a page with two paginated lists, open every page of each list by
following the links and by typing the addresses. Confirm each item appears on exactly one
page, the two lists page independently, out-of-range and malformed page numbers behave as
described, and the page works with scripts turned off.

**Acceptance Scenarios**:

1. **Given** a list block with paging turned on and a page size,
   **When** more items match than the page size,
   **Then** the list shows one page of items and navigation to the previous, next and
   numbered pages, each a plain link with its own address.
2. **Given** a page with several paginated lists,
   **When** a visitor moves to another page of one list,
   **Then** the other lists stay on their current page.
3. **Given** a page number beyond the last page,
   **When** it is requested,
   **Then** the list shows its empty state with a link to its first page, and the rest of
   the page is shown normally.
4. **Given** a page number that is not a whole number, or other unknown address
   parameters,
   **When** the page is requested,
   **Then** the list shows its first page, and the unknown parameters do not create
   separate cached copies of the page.

---

### User Story 6 - Lists inside templates and lists inside lists (Priority: P2)

On the post template the Designer adds "More in this category": a list of Blog posts
filtered to the same Category as the current post, excluding the current post, limited to
3. On the "Team" page, each team member card contains a small list of the posts that member
wrote, newest first, limited to 2. Inside that inner list a binding can still reach the
team member of the outer card.

**Why this priority**: Related content is one of the most common uses of a CMS and needs
binding scopes that nest. It builds on User Stories 1, 2 and 4.

**Independent Test**: Build a template with a list filtered by the current item, and a
list nested in another list's card filtered by the outer item. Confirm that each scope
resolves to the right item, that bindings can reach the outer scope by name, that a
multi-reference field can be the source of a list, and that nesting deeper than allowed is
refused in the editor and by the server.

**Acceptance Scenarios**:

1. **Given** a list block on a template page,
   **When** a filter compares a field with a value of the page's item (for example
   Category equals the current item's Category, or "is not the current item"),
   **Then** each item page lists the matching items for its own item.
2. **Given** a list block inside the card of another list block,
   **When** its filter or a binding inside it refers to the outer card's item,
   **Then** each outer card shows the inner list for its own item.
3. **Given** a block inside nested scopes,
   **When** the member binds a property,
   **Then** they choose the scope (current list item, outer list item, page item) by name,
   and the nearest scope is offered first.
4. **Given** an item with a multi-reference field (Related posts),
   **When** a list block uses that field of the current item as its source,
   **Then** it lists the referenced items that are published, in the order stored in the
   field, after any filters and limit.
5. **Given** a list block nested more deeply than the limit (FR-028),
   **When** the member tries to place it or save the page,
   **Then** it is refused with a reason.

---

### User Story 7 - Changing templates, schemas and addresses safely (Priority: P3)

The Designer moves the "Blog" page under "Resources"; the admin warns that the addresses of
40 published posts will change to `/resources/blog/{slug}`. Later the Designer of the
collection deletes the Summary field; every block bound to it is marked as bound to a
deleted field in the editor, and on the site those blocks behave as if Summary were empty.
Someone tries to delete the "Blog posts" collection and is told which pages use it.

**Why this priority**: Templates and bindings cross three earlier features (pages,
collections, items). Changes in each must never break a published site. It matters less
often than the stories above.

**Independent Test**: With a published template and lists, move the template's parent,
change an item slug, remove and retype bound fields, try to delete the collection, turn the
template back into a normal page, and delete an item used as a sample. Confirm warnings,
refusals, published output and editor marks against FR-008 to FR-012 and FR-031 to FR-032.

**Acceptance Scenarios**:

1. **Given** a published template,
   **When** a move or slug change changes its pattern,
   **Then** the admin warns before saving how many published items will change address,
   and after saving items are served at the new addresses and the old ones give the
   not-found page.
2. **Given** a published item,
   **When** its slug is changed and published,
   **Then** it is served at the new address and its old address gives the not-found page;
   the admin warns about this before publishing (015).
3. **Given** a binding to a field that is removed, or whose type changes so that it can no
   longer fill the bound property,
   **When** the page is opened in the editor,
   **Then** the binding is marked as broken with the reason, and the page can still be
   saved and published; on the site the property behaves as empty.
4. **Given** a collection used as a template's collection or a list block's source by any
   page or component draft or live version,
   **When** someone tries to delete it,
   **Then** it is refused, and the reason lists each page and component that uses it.
5. **Given** a template page,
   **When** a Designer turns it back into a normal page,
   **Then** they are told how many published item addresses will stop working, the
   bindings that refer to the page's item are marked as broken, and the page takes a normal
   slug.

---

### Edge Cases

- **A template at the root** (`/{slug}`): refused; a template's parent MUST be a page, so
  every item address has at least one fixed segment and a template never captures every
  single-segment address.
- **A template's parent that is not published**: item pages are still served, as child
  pages are in 012 (a page's address follows the tree, not its parent's publish state).
- **Template pages cannot have child pages**, and a page with child pages cannot become a
  template, because its address contains the item slug.
- **A static page added at an address an item already uses**: the static page wins (route
  order). The admin warns on both the page and the item that the item's address is hidden
  by a page.
- **Two templates whose patterns could match the same address**: impossible, because every
  template's pattern is its parent's address plus one segment and a parent page may hold at
  most one template (FR-003), whatever its collection.
- **Letter case and accents in addresses**: item addresses are matched ignoring letter
  case, as page addresses are (010).
- **Reserved addresses** (010, 003): unaffected; reserved segments are matched before
  pages and templates.
- **A bound reference whose item has no published data, or was deleted**: treated as
  empty. A list sourced from a multi-reference skips unpublished items.
- **A rich text value bound to a rich text block**: rendered through the same allow-list as
  the block's own content (Principle IX, 035), limited to the formats the block allows.
- **A text field bound into a link**: the address MUST use one of the link schemes 011
  allows; any other value renders the link as plain text.
- **An image field whose media file was deleted**: the image is left out (012 FR-027) and
  the block follows its empty-value choice.
- **Binding loops**: a list cannot be the source of itself, and a template's item cannot be
  a list source except through its multi-reference fields; references followed for a
  binding stop after one step, so no binding can loop.
- **Very large collections**: a list of 100 items per page over a collection of 10,000
  items stays within the render budget (SC-004).
- **A sample item that is deleted**: the editor picks another sample item, or shows the
  template with empty values and a message when the collection has none.
- **Cache-busting**: only the page parameters of the page's own list blocks vary the cached
  copy; other parameters are ignored (FR-027).
- **Items scheduled to publish**: they appear on item pages and in lists when the schedule
  runs, through the same refresh as a manual publish.
- **A component (header or footer) containing a list block**: allowed; every page using
  the component then depends on that collection. Components have no page item scope, so
  blocks in a component cannot bind to the item of the template page they appear on.
- **An item without a name field** (014, 015): the document title of its page is the
  template page's title.

## Requirements *(mandatory)*

### Functional Requirements

**Access**

- **FR-001**: Only members with the Designer or Administrator role on a site MUST be able
  to make a page a template, change its collection, or turn it back into a normal page,
  matching who creates and moves pages in 010. Members who may save page drafts (011,
  including Editors) MUST be able to add, change and remove bindings and list blocks in
  drafts. Publishing templates follows 012 (Designers and Administrators). Authorization
  MUST be enforced by the server on every operation, and users without a role on the site
  MUST receive the same response as for a site that does not exist.

**Templates and addresses**

- **FR-002**: A page MUST be able to be the template of at most one collection of the same
  site, and a collection MUST have at most one template page.
- **FR-003**: A template page's address pattern MUST be its parent page's address followed
  by one item segment that stands for the item's slug. A template page MUST have a parent,
  MUST NOT have child pages, and MUST NOT have an address of its own. A parent page MUST
  hold at most one template page among its children.
- **FR-004**: An item's address MUST be its collection's template pattern with the item's
  published slug, and MUST exist only while the template page is published and the item has
  published data.
- **FR-005**: The public surface MUST match a requested address in this order: reserved
  address spaces (003, 010), then published static pages, then template patterns, then the
  not-found page (012 FR-030). Template matching MUST compare the fixed part of the pattern
  and the slug ignoring letter case.
- **FR-006**: An address that matches a template pattern but no published item MUST receive
  the not-found page, and MUST NOT reveal whether a draft or unpublished item exists with
  that slug.
- **FR-007**: The admin MUST show a template page in the page tree with its pattern and
  collection, and MUST show on an item (015) its live address, or why it has none (no
  template, template not published, item not published, address hidden by a static page).

**Changes that affect addresses or bindings**

- **FR-008**: Before a change that alters a published template's pattern (moving it or an
  ancestor, changing an ancestor's slug), turns a template back into a normal page, or
  unpublishes a template, the admin MUST warn how many published item addresses will
  change or stop working. Addresses MUST follow the change from the next request; redirects
  from old addresses belong to #17.
- **FR-009**: Making a page a template, changing its collection, or turning it back into a
  normal page MUST be refused while the page has child pages, and MUST mark bindings that no
  longer resolve as broken (FR-018).
- **FR-010**: A collection MUST NOT be deletable (extending 014) while any page or component
  draft, live version or pending scheduled content uses it as a template's collection, a
  list block's source, or in a binding. The refusal MUST list each page and component, up
  to 50, with the total.
- **FR-011**: A schema change (014) that removes a bound field or changes its type MUST NOT
  be refused because of bindings; the admin MUST tell the member before applying it how
  many pages and components bind to that field.
- **FR-012**: Changing the collection's address name (014) MUST NOT change item addresses;
  item addresses come only from the template's place in the page tree.

**Binding**

- **FR-013**: The binding expression format MUST be a versioned contract document in
  `docs/contracts/`, alongside the page and collection schema contracts, and both the admin
  and the server MUST validate bindings against the same definition (Principle VI). Stored
  bindings MUST refer to collections and fields by their identifiers, never by display name
  or key, and MUST carry the format version.
- **FR-014**: Each block type's definition (011 FR-010) MUST state which of its properties
  can be bound and which value types each accepts, so that adding a block type does not
  change any other. At least these bindings MUST be available:
  - **Text properties** (heading text, text, button label, image alt text): single-line and
    multi-line text, number, date, option (its label), boolean (a yes and a no text set by
    the member), the item's slug, and a single reference (the referenced item's name).
  - **Rich text content**: rich text and multi-line text fields.
  - **Image**: image fields.
  - **Link** (the button's link; links inside rich text are not bound): the item's page, a single reference's item
    page, a file field, and a text field holding an address.
  - **Built-in item values**: page address, slug, first published time and last published
    time.
- **FR-015**: A text property MUST be able to combine fixed text with any number of bound
  values. Dates MUST offer a choice of formats and numbers a choice of decimal places, shown
  in the site's default language.
- **FR-016**: A binding MUST be able to follow one single-reference field to a field of the
  referenced item. Following more than one step, or following a multi-reference field
  outside a list source (FR-025), MUST NOT be possible.
- **FR-017**: The binding control MUST offer only compatible fields and built-in values
  (FR-014), show bound properties with the field's current display name and scope, and let
  the member remove a binding, which restores the property's last fixed value. Every
  binding action MUST be usable with the keyboard alone and announced to assistive
  technology, matching the 011 accessibility baseline.
- **FR-018**: A binding whose field, collection or scope no longer exists, or whose field
  type can no longer fill the property, MUST be marked as broken in the editor with its
  reason. A page with broken bindings MUST still save and publish; when published, a broken
  binding MUST behave as an empty value.
- **FR-019**: Each bound block MUST have an empty-value choice: show a fallback value set by
  the member (the property's fixed value by default), or hide the block. A value is empty
  when the field has no value, a referenced item or media file is missing or not
  published, or the binding is broken.
- **FR-020**: The editor canvas MUST show bound values: on a template page, those of a
  sample item the member picks from the collection (its draft data, marked as a draft); in
  list blocks, the items and published data the site would show. Picking a sample item MUST
  NOT change the page's draft.
- **FR-021**: Every bound value MUST be encoded for the place it is written to (text,
  attribute, style, address), rich text MUST be produced through the renderer's allow-list
  (Principle IX, 035), and links MUST use only the schemes 011 allows (012 FR-025).

**Collection list block**

- **FR-022**: A Collection list block MUST be a block type (Principle VI) whose source is a
  collection of the same site, or a multi-reference field of an enclosing scope's item
  (FR-025). It MUST hold card content, repeated once per listed item with that item as the
  card's binding scope, and empty-state content shown when no item is listed.
- **FR-023**: A list MUST support up to 10 filter conditions, all of which must hold, with
  operators suited to each field type: equals and not equals (text, number, option,
  boolean, reference), contains (text, multi-reference), greater and less than (number,
  date), and is empty and is not empty (every type). A condition's value MUST be either a
  fixed value or a value from an enclosing scope's item; "is not the current item" MUST be
  available inside a scope of the same collection.
- **FR-024**: A list MUST support up to 3 sort keys (any text, number, date or option field,
  the slug, and the first and last published times) with a direction each, items without a
  value last and a stable order between equal items; a limit from 1 to 100 items when
  paging is off; and, optionally, paging with a page size from 1 to 100, in which case the
  list pages through every matching item and the limit does not apply.
- **FR-025**: A list sourced from a multi-reference field MUST list the referenced items in
  the order stored in the field, unless sort keys are set, before applying filters and
  limit.
- **FR-026**: Lists MUST show only items with published data, using their published data;
  list results MUST NOT include items of another collection or site.

**Paging**

- **FR-027**: A paginated list MUST render plain links to the previous, next and numbered
  pages, each with its own address that works without scripts. Each paginated list on a
  page MUST page independently. Only the page parameters of the page's own paginated lists
  MUST vary the rendered and cached output; other parameters MUST be ignored. A malformed
  page number MUST show the first page; a page number beyond the last page MUST show the
  list's empty state with a link to the first page.

**Scopes and limits**

- **FR-028**: Binding scopes MUST nest: the page's item on a template page, then each
  enclosing list's card item. A binding or filter MUST state which scope it reads, the
  nearest scope being the default. List blocks MUST nest at most 2 deep (a list inside a
  list's card), and a page MUST hold at most 20 list blocks; deeper nesting or more lists
  MUST be refused by the editor and by the server.
- **FR-029**: Blocks in shared components MUST NOT bind to a page's item; list blocks inside
  components MUST be allowed.

**Rendering, caching and invalidation**

- **FR-030**: Item pages MUST be rendered on the server from the template's live version,
  the item's published data, the live versions of the components the template uses and the
  site's published design, meeting 012 FR-022 to FR-029. The document title of an item page
  MUST be the item's name, or the template's title when the item has no name. SEO metadata
  templates are #17.
- **FR-031**: Item pages and pages containing list blocks MUST be cached per site like other
  pages (012 FR-031, FR-034, FR-035), one cached copy per item page and per combination of
  list page numbers.
- **FR-032**: The dependency record (012 FR-032) MUST be extended so that a page or
  component records the collections it lists and binds to, a template records its items, and
  item pages can be refreshed when an item they show through a reference changes.
- **FR-033**: Using that record, the product MUST refresh exactly the affected pages when:
  an item is published, unpublished, rolled back, deleted or reaches a scheduled action
  (its own page, item pages showing it through a reference, and pages listing its
  collection); a template is published, unpublished or rolled back, or its pattern changes
  (all its item pages); a collection's schema changes (all pages binding to or listing it).
  A static page that hides or uncovers an item address takes effect from the next request
  through the route order (FR-005), without a refresh. Refreshes MUST meet 012 FR-033 (every instance within 5 seconds, no unaffected
  page discarded).
- **FR-034**: Every binding and list MUST resolve against the published data at render
  time; nothing rendered for visitors MUST contain draft data, unpublished items or another
  site's content.

**Contracts, events and isolation**

- **FR-035**: The page schema contract MUST be extended for template pages, bindings and the
  Collection list block as an additive, versioned change; every page and component stored
  before this feature MUST stay valid and render unchanged.
- **FR-036**: Other modules MUST learn through integration events when a page becomes or
  stops being a template and when a template's pattern changes, so that #17 (sitemaps,
  redirects) can react. Event handlers MUST be idempotent.
- **FR-037**: Templates, bindings, list results and dependency records MUST be restricted to
  their site and pass the shared isolation suite (009), which MUST prove that no item of one
  site can be shown on another site's pages or addresses.
- **FR-038**: Rendering MUST never fail because of a binding or list problem: unresolvable
  bindings and list sources MUST render as empty values or the list's empty state, and MUST
  be logged without item values (Principle IX).
- **FR-039**: Every refusal MUST state a machine-readable reason and a message through the
  translation lookup, following 012 FR-040.

### Key Entities

- **Template page**: a page of the page tree that is the template of one collection. Has a
  parent, an address pattern (parent address plus the item segment), and the usual draft,
  versions and history of a page (010, 012). Has no child pages.
- **Item address**: the address of one published item, derived from its collection's
  template pattern and its published slug. Not stored as content.
- **Binding**: a link from one block property to a value of an item in a scope: a field
  (by identifier), a built-in item value, or a field of a referenced item, with its
  formatting and empty-value choice. Stored in the page document under the binding contract.
- **Binding scope**: the item a binding reads: the page's item on a template page, or the
  card item of an enclosing list. Scopes nest.
- **Collection list block**: a block type with a source (a collection, or a multi-reference
  field of a scope's item), filters, sort keys, limit, optional paging, card content and
  empty-state content.
- **Sample item**: the item a member picks to preview a template in the editor. Kept per
  member, not part of the page.
- **Dependency record (extended)**: for each published page and component, the collections
  it binds to or lists, the items a template serves, and how item pages learn that a
  referenced item changed, used to refresh exactly the affected pages.
- **Binding contract**: the versioned definition of the binding expression format in
  `docs/contracts/`, shared by the admin and the server.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A Designer who has used the editor but not templates can turn a page into a
  collection template, bind its heading, image and body, publish it and open an item at its
  address in under 5 minutes.
- **SC-002**: A Designer can add a list of the 6 latest featured items with a designed card
  and empty state to a page in under 3 minutes.
- **SC-003**: Across a test covering every bindable property and every field type, 100% of
  published values appear on the published page as defined in the binding contract, and 0
  draft values, unpublished items or values of another site appear.
- **SC-004**: On the reference environment (two cores, four gigabytes of memory), an item
  page of 200 blocks with a list of 20 items, and a page with a paginated list of 100 items
  over a 10,000-item collection, are each served in under 200 milliseconds at the 95th
  percentile when not cached and under 20 milliseconds when cached.
- **SC-005**: After an item is published, unpublished or changed, visitors on every instance
  see the new state on its page and in every list within 5 seconds in 95% of cases and
  within 30 seconds in all cases.
- **SC-006**: Publishing one item of a 1,000-item collection whose template has no list
  block refreshes only that item's page, the item pages that show it through a reference,
  the pages listing its collection, and the pages listing a collection that refers to it;
  every other item page stays cached.
- **SC-007**: Across a test that removes and retypes bound fields, deletes referenced items
  and media, and moves templates, 0 published requests fail with a server error; affected
  values render as empty or fallback.
- **SC-008**: Every binding and list action in the editor can be completed with the
  keyboard alone, and an automated accessibility check of the binding control and list
  settings reports no serious or critical issues.

## Scope

### In Scope

- Making a page the template of a collection, with an address pattern from its place in the
  page tree, and turning it back into a normal page.
- Item addresses and the route matching order: reserved, static page, template, not found.
- The binding expression format and its contract document; bindings stored by identifier.
- The binding control in the property panel, sample items in the editor, broken binding
  marks, empty-value choices and fallbacks.
- The Collection list block with filters, sort keys, limit, paging, card and empty-state
  content, sourced from a collection or a multi-reference field.
- Nested binding scopes on template pages and in lists inside lists.
- Rendering, caching and exact refresh of item pages and pages with lists, extending 012's
  dependency record.
- Safeguards on collection deletion, schema changes, template moves and slug changes.
- Contract, isolation suite, events, upgrade migration and smoke-test extension.

### Out of Scope

- SEO metadata templates, sitemaps, canonical addresses and redirects from old item
  addresses (#17).
- Cross-site content reuse; collections and items stay within their site.
- Visitor-controlled filtering, sorting and search of lists (only paging is driven by the
  visitor); filters are set by the member in the editor.
- Filters relative to the current time (for example "upcoming events"), because they would
  change cached output without any publish.
- Address patterns with placeholders other than the item slug (for example
  `/blog/{category}/{slug}`), more than one template per collection, and templates at the
  root of the site.
- Binding blocks inside shared components to the page's item; binding properties other
  than those in FR-014 (for example colours or spacing).
- Following more than one reference step in a binding; calculations, conditions and custom
  formulas in bindings.
- Rendered draft previews of item pages for visitors and preview links (#22).
- Localisation of item content.

## Assumptions

- **Address pattern from the page tree**: the issue's example `/blog/{slug}` is produced by
  placing the template under the page at `/blog`. Deriving the pattern from the tree keeps
  010's address rules (uniqueness, reserved segments, moves) in one place, and lets the
  collection's address name change without moving items (014 left this to #16). Free-form
  patterns are out of scope.
- **Static pages win over items** at the same address, as the issue states; the admin warns
  when this hides an item rather than refusing either change.
- **Old addresses give the not-found page** (clarified 2026-10-10) after a slug change or a
  template move, as 012 does for pages; this feature keeps no record of old slugs, and
  redirects are #17.
- **Designer-set filters only** (clarified 2026-10-10): "filtering, sorting and paging" in
  the issue means settings the member chooses in the editor. Visitors only move between pages, which keeps
  every list's output cacheable and the published site script-free. Visitor-facing filters
  can be added later as an additive block option.
- **Roles** follow 010 to 012: Designers and Administrators shape page structure (making a
  page a template) and publish pages; everyone who may save page drafts, including Editors,
  may edit bindings and list settings in drafts (011 clarification).
- **The editor previews a template with a sample item's draft data** (clarified 2026-10-10)
  so a Designer can work
  before any item is published; lists in the canvas show what the site would show.
- **Refreshing all list pages on any item change in the collection** is accepted: it is
  correct, simple, and the pages are re-rendered only when next requested.
- **One reference step** (clarified 2026-10-10) covers the common cases (author name,
  category name); deeper
  chains and calculations are left out to keep the binding contract small (Principle XI).
- **Limits** (10 filters, 3 sort keys, 100 items per list or page, 2 levels of nesting, 20
  lists per page) are defaults chosen to keep render times within Principle VIII's budget;
  they can be raised later.
- **Upgrade**: no existing page is a template and no stored document contains a binding or
  list block, so the upgrade only adds the new contract version and dependency records.

## Dependencies

- Feature 003 (media storage) and 013 (media library): the media address space and image
  variants used by bound images.
- Feature 009 (sites and host name resolution): site resolution, the isolation suite.
- Feature 010 (pages, page tree and shared components): page addresses, reserved segments,
  the page schema contract.
- Feature 011 (editor core): block definitions, the property panel, the canvas, allowed link
  schemes, the accessibility baseline.
- Feature 012 (publishing and rendering): publish of pages, the renderer, the not-found
  page, output caching, the dependency record and refresh rules, performance budgets.
- Feature 014 (collections and schema): collections, field identifiers and types, schema
  changes and collection deletion.
- Feature 015 (items): slugs, published data, publish, unpublish, schedules and item change
  announcements.
- Feature 035 (rich text formats): the rich text renderer and allow-list used for bound rich
  text.
- The project constitution, in particular Principles VI (binding format as a versioned
  contract), VII (published data only), VIII (caching, invalidation, budgets), IX (encoded
  output) and X (golden-file tests for binding resolution and rendering).
- GitHub issue #16, which this feature implements and which the pull request closes.
