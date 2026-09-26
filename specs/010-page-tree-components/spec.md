# Feature Specification: Pages, Page Tree and Shared Components

**Feature Branch**: `010-page-tree-components`

**Created**: 2026-09-26

**Status**: Draft

**Issue**: #10

**Input**: User description: "Pages, page tree and shared components. Problem / goal: Content editors need a structure of pages before there is anything to edit or render. A user can create pages in a hierarchy, set slugs and titles, move and delete pages, and define shared components such as a header and footer that several pages reuse. In: page hierarchy with parent/child, slug uniqueness per site and path computation, page CRUD and move, shared component entities, storage of the page tree JSON with schemaVersion, validation against the page schema contract, the docs/contracts/ page schema document, draft storage. Out: the visual editor (#11), publishing and rendering (#12), collection templates (#16)."

## Context

Features 001 to 009 built everything a site needs except content. An installation hosts
several sites, each resolved from its host names, with per-site roles, a media library
and a shared isolation suite that every module storing site-owned records must pass.
Feature 003 reserved the media address space on the public surface so that no page can
ever shadow it, and left it to this feature to say what pages are.

Nothing can be edited or rendered yet because there are no pages. The visual editor
(#11) needs a page and a stored content document to open. Publishing and rendering (#12)
need a page's address and a draft to publish. Both also need shared pieces, such as a
header and footer, that many pages show but that are maintained in one place.

This feature adds the `Pages` module: a per-site tree of pages with titles, slugs and
computed addresses; shared components; and, for both, a stored draft content document in
the versioned page schema format that the editor and renderer will share. It also writes
that format down as a contract in `docs/contracts/`, as constitution Principle VI
requires.

## Clarifications

### Session 2026-09-26

- Q: Can two pages use the same slug if they sit under different parents, for example
  `/services/faq` and `/support/faq`? → **A: yes.** A slug only has to be unique among
  pages with the same parent. What must be unique across the site is the full address.
- Q: What should happen when someone deletes a page that still has child pages? →
  **A: refuse it.** The delete is refused until the child pages have been moved or
  deleted. Deletion is permanent in this feature, so no single action can remove a whole
  section.
- Q: How should the page and component permissions be split between the Designer and
  Editor roles? → **A: Designer builds, Editor writes.** Designer changes the page
  structure (create, rename, move, delete, choose the home page), manages shared
  components and saves page drafts. Editor can view pages and components and save page
  drafts only. Administrator holds every capability on every site.
- Refined during planning (research P-05): a move names the sibling to place the page
  after, so a change to some other sibling is not a conflict (FR-010).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A site member builds the page structure of a site (Priority: P1)

A designer starting a new client site creates a home page, then top-level pages
"About", "Services" and "Contact", and under "Services" the pages "Web design" and
"Hosting". For each they type a title, and the product suggests a slug from it, which
they may change. The page tree shows the hierarchy, and each page shows its full address,
such as `/services/hosting`.

**Why this priority**: Every later content feature (editor, publishing, rendering, SEO,
navigation) starts from a page with an address. Without the tree there is nothing to
edit or publish.

**Independent Test**: On a site with no pages, create a home page, three top-level pages
and two child pages. Confirm that the tree shows them in the order created, under the
right parents, with the computed addresses. Try a duplicate slug, an invalid slug and a
reserved address, and confirm that each is refused with a reason and nothing is saved.

**Acceptance Scenarios**:

1. **Given** a site with no pages,
   **When** a site member with the page capability creates a page with a title and
   marks it as the home page,
   **Then** the page is created at the site root address `/` and appears in the tree.
2. **Given** a site,
   **When** a site member creates a page with a title, a slug and optionally a parent,
   **Then** the page is created as the last child of that parent (or at the top level),
   its address is the parent's address followed by its slug, and it has an empty draft
   content document.
3. **Given** a page title such as "Dịch vụ & Giá",
   **When** a site member creates the page without typing a slug,
   **Then** the admin shell suggests the slug `dich-vu-gia`, which the member can accept
   or change.
4. **Given** a slug that breaks the uniqueness rule in FR-006, contains characters not
   allowed in slugs, or would give an address that the product reserves (for example the
   media address space),
   **When** a site member tries to use it,
   **Then** it is refused with a reason and nothing is saved.
5. **Given** a site member with a role on site A only,
   **When** they try to list or change pages of site B,
   **Then** the request is answered as though site B's pages did not exist.

---

### User Story 2 - A site member renames, reorders and moves pages (Priority: P1)

The client decides "Hosting" belongs under a new top-level page "Support", and wants
"Contact" to appear before "Services". The designer moves "Hosting" under "Support",
drags "Contact" above "Services", and renames the slug of "Services" to `what-we-do`.
Every affected address, including those of all descendant pages, updates at once.

**Why this priority**: Site structures change constantly during a build. If moving or
renaming a page left stale addresses on its descendants, or could produce two pages at
one address, every later feature would inherit the inconsistency.

**Independent Test**: Build a three-level tree, then move a page with children to a new
parent, reorder siblings and change a slug that has descendants. Confirm that every
affected address is recomputed in the same operation, that sibling order is kept, and
that a move that would create a duplicate address, or put a page under its own
descendant, is refused and changes nothing.

**Acceptance Scenarios**:

1. **Given** a page with descendants,
   **When** a site member changes its slug,
   **Then** its address and the addresses of all its descendants are recomputed in the
   same operation.
2. **Given** a page,
   **When** a site member moves it to a new parent (or to the top level) at a chosen
   position among the new siblings,
   **Then** it and its whole subtree move, the siblings' order is kept apart from the
   inserted page, and all affected addresses are recomputed in the same operation.
3. **Given** a page,
   **When** a site member tries to move it under itself or under one of its descendants,
   **Then** the move is refused with a reason and nothing changes.
4. **Given** a move or slug change that would give any page in the moved subtree an
   address already used by another page, or a reserved address,
   **When** it is submitted,
   **Then** the whole change is refused, the refusal names the first conflicting address,
   and nothing changes.
5. **Given** two site members changing the same page or its siblings at once,
   **When** the second submits a change based on a tree the first has since changed,
   **Then** the second change is refused as out of date and nothing is overwritten
   silently.
6. **Given** a site member changing a page's title,
   **When** they save it,
   **Then** the title changes and the slug and address stay as they were.

---

### User Story 3 - A site member stores and retrieves a page's draft content (Priority: P1)

The visual editor (#11) will save a page's content as a tree of blocks. Before the editor
exists, this feature guarantees that a page's draft content document can be saved and
read back exactly, that it always carries the version of the page schema it was written
in, and that a document that does not follow the page schema contract is refused before
it is stored. A developer building the editor or renderer reads the contract in
`docs/contracts/` and knows exactly what a valid document looks like.

**Why this priority**: The draft document is the hand-off point to both the editor and
the renderer. If invalid documents can be stored, every later feature has to defend
against them; if documents are not versioned, the format can never evolve safely.

**Independent Test**: Save a valid draft document to a page and read it back unchanged,
with its schema version. Submit documents that break each rule in the contract (missing
schema version, unknown version, missing node identifier, duplicate node identifiers,
unknown node type, invalid props, too large, too deep) and confirm that each is refused
with a reason that points at the offending node, and that the previous draft is kept.

**Acceptance Scenarios**:

1. **Given** a page,
   **When** a site member saves a draft document that follows the page schema contract,
   **Then** it is stored as the page's draft, and reading the draft returns the same
   document with its schema version and the time and user of the last change.
2. **Given** a draft document that breaks any rule of the page schema contract,
   **When** it is saved,
   **Then** it is refused with a machine-readable reason and the location of the first
   offending node, and the page's previous draft is kept unchanged.
3. **Given** a draft document written in a schema version that the installation does not
   support,
   **When** it is saved,
   **Then** it is refused as an unsupported version.
4. **Given** two site members saving the same page's draft at once,
   **When** the second saves changes based on a draft the first has since replaced,
   **Then** the second save is refused as out of date.
5. **Given** the page schema contract document in `docs/contracts/`,
   **When** the shared schema definition that the product validates against is compared
   with it,
   **Then** the two agree, and an automated check fails the build if they drift apart.
6. **Given** any change to a page's draft, title, slug or position,
   **When** it is saved,
   **Then** nothing that visitors can see changes, because nothing is published in this
   feature.

---

### User Story 4 - A site member defines shared components used by many pages (Priority: P2)

The designer creates a "Header" and a "Footer" component for the site, each with its own
draft content document. Every page's draft can then include a reference to the header
and footer instead of a copy, so that changing the header once changes it for every page
that uses it once publishing exists. The designer can see which pages use a component,
and cannot delete one that is still in use.

**Why this priority**: Nearly every site has repeated sections, and maintaining copies
per page does not scale. It depends on pages and drafts from stories 1 and 3, so it comes
second.

**Independent Test**: Create a "Header" component with a draft document, reference it
from two pages' drafts, and confirm that the component lists both pages as users.
Try to delete it and confirm that the deletion is refused, naming the number of pages
that use it. Remove the references and confirm that deletion then succeeds. Try to save
a page draft that references a component of another site, or one that does not exist,
and confirm that it is refused.

**Acceptance Scenarios**:

1. **Given** a site,
   **When** a site member with the component capability creates a component with a
   name,
   **Then** it is created with an empty draft content document in the page schema
   format.
2. **Given** a component,
   **When** a site member renames it or saves its draft document,
   **Then** the change is saved under the same validation, versioning and out-of-date
   rules as a page's draft.
3. **Given** a page draft that references a component of the same site,
   **When** it is saved,
   **Then** it is accepted, and the component lists that page among its users.
4. **Given** a page draft that references a component that does not exist or belongs to
   another site,
   **When** it is saved,
   **Then** it is refused as referencing an unknown component, without revealing whether
   the component exists on another site.
5. **Given** a component referenced by at least one page draft,
   **When** a site member tries to delete it,
   **Then** it is refused with a reason that states how many pages use it.
6. **Given** a component draft that references another component,
   **When** it is saved,
   **Then** it is refused, because components do not nest in this feature.

---

### User Story 5 - A site member deletes pages (Priority: P2)

The client drops the "Hosting" service. The designer deletes the "Hosting" page after
confirming. A page with child pages cannot be deleted by accident.

**Why this priority**: Deleting is needed to keep a structure tidy, but it is less
frequent than building and reorganising, and it must be safe.

**Independent Test**: Delete a leaf page after confirming and confirm that it and its
draft are gone and its address is free for reuse. Try to delete a page that has child
pages and confirm that it is refused and nothing changes. Try to delete the home page and confirm the
behaviour in FR-013.

**Acceptance Scenarios**:

1. **Given** a page without child pages,
   **When** a site member deletes it after confirming,
   **Then** the page and its draft are removed, the remaining siblings keep their
   relative order, and its address can be used by another page.
2. **Given** a page with child pages,
   **When** a site member tries to delete it,
   **Then** it is refused with a reason that states how many child pages it has, and
   nothing changes until those pages have been moved or deleted.
3. **Given** the home page,
   **When** a site member tries to delete it,
   **Then** the product behaves as FR-013 describes.
4. **Given** a site member without the page capability on the site,
   **When** they try to create, change, move or delete a page,
   **Then** it is refused as forbidden, and the admin shell does not offer it.
5. **Given** an Editor on a site,
   **When** they open the page tree and the component list,
   **Then** they can see every page and component and save page drafts, but creating,
   renaming, moving or deleting pages, choosing the home page, and any change to
   components are refused as forbidden and not offered.

---

### Edge Cases

- **Slug normalisation**: slugs are stored in lowercase. Letters with diacritics in a
  suggested slug are reduced to their base letters (`ệ` → `e`, `đ` → `d`). Runs of spaces
  and punctuation become a single hyphen, and leading and trailing hyphens are removed.
  A typed slug that still contains characters outside lowercase letters, digits and
  hyphens is refused rather than silently changed.
- **Two pages created at the same address at once**: exactly one succeeds; the other is
  refused as a duplicate address.
- **Reserved addresses**: the media address space (feature 003) and any other address
  space the product reserves on the public surface cannot be the first segment of a page
  address. The list of reserved segments is part of the contract document.
- **Addresses differing only in letter case**: treated as the same address.
- **A move that changes nothing** (same parent, same position): accepted and changes
  nothing.
- **Very deep or very large trees**: page nesting is limited to 10 levels, and a site may
  hold up to 2,000 pages. Beyond these limits, creating or moving is refused with a
  reason.
- **Address length**: a full page address may not exceed 1,024 characters; a slug may not
  exceed 100 characters.
- **Changing an address that visitors or search engines might already know**: nothing is
  published in this feature, so no address is public yet. Redirects from old addresses
  belong to SEO and redirects (#17).
- **Deleting a file from the media library that a draft uses**: no block type that refers
  to media exists in this feature, so no draft can refer to a file yet. Tracking media
  references is left to the feature that adds such blocks (#11).
- **A disabled site** (feature 009): its members can keep building its page tree and
  components in the admin shell.
- **Draft document size**: a single draft document may not exceed 1 MB, 5,000 nodes or
  a nesting depth of 32 nodes. Larger documents are refused with a reason.
- **Node identifiers**: every node in a document has an identifier unique within that
  document. The same identifier may appear in different documents.
- **A component renamed**: pages keep referring to it, because references use its
  identifier, never its name.
- **Two components with the same name on one site**: refused; component names are unique
  per site, ignoring letter case.
- **Upgrade from the previous release**: no page or component exists before this
  feature, so the upgrade adds empty storage for them and changes nothing else.

## Requirements *(mandatory)*

### Functional Requirements

**Page tree**

- **FR-001**: Each site MUST have its own tree of pages. Each page MUST have an
  identifier that never changes, a title, a slug, an optional parent page of the same
  site, a position among its siblings, a computed address, a draft content document and
  the times and users of its creation and last change.
- **FR-002**: Site members with the page capability MUST be able to create a page with a
  title, a slug and an optional parent. A new page MUST be placed last among its
  siblings unless a position is given.
- **FR-003**: A site MUST have at most one home page. The home page MUST be a top-level
  page and its address MUST be the site root `/`; its slug does not appear in any
  address while it is the home page, but it keeps one, so that it has an address again
  if another page becomes the home page. A site member MUST be able to choose which
  top-level page is the home page, provided the rules in FR-006 still hold afterwards.
- **FR-004**: A page's address MUST be computed, never typed: the home page's address is
  `/`, and every other page's address is its parent's address followed by its own slug,
  where the home page contributes nothing to its children's addresses (so a child of the
  home page with slug `news` has address `/news`). Addresses MUST NOT include the site's
  host name or the installation's base path; those are added from the site's base URL
  (feature 009) wherever an absolute address is needed.
- **FR-005**: Slugs MUST consist of 1 to 100 lowercase letters `a`-`z`, digits and single
  hyphens, and MUST NOT start or end with a hyphen. The admin shell MUST suggest a slug
  from the title following the normalisation in Edge Cases.
- **FR-006**: No two pages of a site MUST ever have the same address, ignoring letter
  case, and the check MUST hold when two changes are made at once. The same slug MAY be
  used under different parents (for example `/services/faq` and `/support/faq`). Because
  the home page contributes nothing to its children's addresses, a child of the home page
  and a top-level page MUST NOT share a slug.
- **FR-007**: The first segment of a page address MUST NOT be one that the product
  reserves on the public surface, including the media address space from feature 003.
  The reserved list MUST be documented in the page schema contract document.
- **FR-008**: Site members with the page capability MUST be able to change a page's
  title and slug. Changing a slug MUST recompute the addresses of the page and all its
  descendants in the same operation, or refuse the whole change if any recomputed
  address would break FR-006 or FR-007.
- **FR-009**: Site members with the page capability MUST be able to move a page, with its
  whole subtree, to a new parent or to the top level, at a chosen position among the new
  siblings, and to reorder siblings. A move MUST be refused if it would put a page under
  itself or one of its descendants, exceed the nesting limit, or break FR-006 or FR-007
  for any page in the moved subtree. Accepted moves MUST recompute all affected
  addresses in the same operation.
- **FR-010**: Every change to a page MUST be refused as out of date when the page has
  changed since the caller read it. A move or reorder MUST also be refused as out of date
  when the sibling it is to be placed after is no longer a child of the target parent.
  Changes to other siblings MUST NOT, on their own, make a change out of date.
- **FR-011**: Site members MUST be able to list a site's page tree, showing each page's
  title, slug, address, position and parent, and whether it is the home page, and to
  read a single page with its draft.

**Deleting pages**

- **FR-012**: Deleting a page that has child pages MUST be refused with a reason that
  states how many child pages it has, until they have been moved or deleted. Deleting a
  page without children MUST remove it and its draft after confirmation.
- **FR-013**: Deleting the home page MUST be refused until another top-level page has
  been made the home page, or until it is the only page of the site.
- **FR-014**: Deleting a page in this feature MUST be permanent, and MUST free its
  address for reuse. Trash and restore for pages belongs to #21.

**Draft content documents and the page schema contract**

- **FR-015**: Every page and every component MUST have exactly one draft content
  document. A new page or component MUST start with an empty document in the current
  schema version. Published copies do not exist in this feature (#12), and no change in
  this feature may alter anything visitors can see.
- **FR-016**: Every stored document MUST carry the schema version it was written in.
  This feature introduces schema version 1.
- **FR-017**: A document MUST be a tree of nodes, each with an identifier, a type, a set
  of properties and an ordered list of child nodes, as constitution Principle VI
  defines. Node identifiers MUST be unique within a document.
- **FR-018**: Every document MUST be validated against the page schema contract before it
  is stored: its schema version is supported, its structure is well formed, every node's
  type is a known node type, every node's properties match that type's property
  definition, children appear only where the type allows them, and the size, node-count
  and depth limits in Edge Cases hold. A document that fails MUST be refused with a
  machine-readable reason and the location of the first offending node, and the
  previous draft MUST be kept.
- **FR-019**: The set of known node types MUST be defined in one place that both
  validation and the contract document draw from, so that later features (#11) add
  content block types without changing unrelated ones. This feature MUST define only the
  node types that the structure itself needs: the document root and the reference to a
  shared component.
- **FR-020**: Reading a draft MUST return the stored document equal, as a JSON value, to
  what was saved (property order may differ), with its
  schema version and the time and user of its last change.
- **FR-021**: Saving a draft MUST be refused as out of date when it is based on a draft
  that has since been replaced.
- **FR-022**: The page schema contract MUST be documented in `docs/contracts/`: the
  document envelope and schema version, the node structure, the known node types and
  their properties, the component reference, identifier rules, limits, reserved address
  segments, and how a future schema version and its forward migration are introduced.
  The product MUST validate against a shared schema definition that matches the
  document, and an automated check MUST fail the build when they disagree.
- **FR-023**: Properties in a document MUST NOT carry raw HTML. Rich text, when block
  types that hold it are added later, is stored as structured content (constitution
  Principle IX); the contract document MUST state this rule.

**Shared components**

- **FR-024**: Each site MUST be able to hold shared components. Each component MUST have
  an identifier that never changes, a name unique within the site ignoring letter case,
  a draft content document in the page schema format, and the times and users of its
  creation and last change.
- **FR-025**: Site members with the component capability MUST be able to create,
  rename, list and read components, save a component's draft, and delete a component.
- **FR-026**: A page's draft MAY include references to components of the same site by
  identifier. A reference to a component that does not exist or belongs to another site
  MUST be refused as an unknown component, without revealing which.
- **FR-027**: A component's draft MUST NOT reference components in this feature.
- **FR-028**: For each component, site members MUST be able to see which pages' drafts
  reference it. Deleting a component that any page draft references MUST be refused with
  a reason that states how many pages use it.

**Authorization**

- **FR-029**: The Pages module MUST add named, site-scoped capabilities to feature 002's
  capability model for viewing pages and components, changing the page structure (create,
  rename, move, delete, choose the home page), saving page drafts, and managing shared
  components. Administrator MUST hold all of them on every site. Designer MUST hold all
  of them on the sites where they have the role. Editor MUST hold only viewing pages and
  components and saving page drafts on the sites where they have the role.
- **FR-030**: Every page and component operation MUST declare its capability, and MUST
  answer as not found for sites the user may not see and as forbidden on a permitted
  site where the capability is missing, following feature 002.

**Isolation, events and operation**

- **FR-031**: Pages, components and drafts are site-owned records. They MUST be
  restricted to their site automatically, and the Pages module MUST be registered with
  and pass the shared isolation suite from feature 009.
- **FR-032**: Other modules MUST learn through integration events when a page is
  created, deleted or has its address changed, when the home page changes, and when a
  component is created or deleted, so that later features (rendering, navigation,
  redirects) can react without reading the Pages module's data. Event handlers MUST be
  idempotent.
- **FR-033**: The admin shell MUST provide a page tree screen for a site (create, rename,
  change slug, reorder, move, delete, choose the home page) and a component list (create,
  rename, delete, see users). It MUST show usable empty, in-progress and error states,
  MUST make reordering and moving possible without a pointing device, and MUST meet the
  translation-lookup and accessibility baselines from features 001 and 002. Editing
  draft content visually is the editor's job (#11).
- **FR-034**: Every refusal MUST state a machine-readable reason (for example invalid
  slug, duplicate address, reserved address, move into descendant, nesting too deep,
  page has children, home page, out of date, invalid document with location, unsupported
  schema version, unknown component, component in use, duplicate component name), and
  the admin shell MUST show each as a clear message.
- **FR-035**: The upgrade from the previous release MUST add the page and component
  storage without changing existing data, proven by the automated upgrade test from a
  snapshot of the previous release.
- **FR-036**: The release smoke test MUST be extended to create a page, create a child
  page, save a valid draft and read it back.
- **FR-037**: Log output MUST refer to pages and components by identifier and MUST NOT
  include draft content, titles or other content bodies (constitution Principle IX).

### Key Entities

- **Page**: one page of a site. It has an identifier that never changes, a title, a slug,
  an optional parent page, a position among its siblings, a computed address, a home
  page flag, one draft content document, and the times and users of its creation and
  last change. It belongs to exactly one site.
- **Page Address**: the page's path within its site, computed from its ancestors' slugs
  and its own. Unique per site, ignoring letter case. It never includes the host name or
  base path.
- **Shared Component**: a named, reusable piece of content of a site, such as a header or
  footer, with one draft content document. Pages refer to it by identifier.
- **Draft Content Document**: the editable content of a page or component: a versioned
  tree of nodes in the page schema format. It is never what visitors see; publishing
  (#12) will produce the published copy.
- **Node**: one element of a document: identifier, type, properties and ordered children.
- **Node Type**: a named kind of node with a property definition and a rule for whether
  it has children. This feature defines the document root and the component reference.
- **Page Schema Contract**: the documented, versioned definition of valid documents,
  shared by validation now and by the editor and renderer later.
- **Page Capability / Component Capability**: named, site-scoped permissions added to
  feature 002's capability model.
- **Page Structure Event**: the notice other modules receive when pages or components are
  created, deleted or change address.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A designer can build a site structure of 10 pages across three levels,
  with a home page, in under 5 minutes on a fresh reference installation.
- **SC-002**: Across the automated test suite, zero states are ever stored in which two
  pages of a site share an address, a page is its own ancestor, or a page's address
  disagrees with its ancestors' slugs, including under concurrent changes.
- **SC-003**: 100% of documents that break a contract rule are refused, across a test
  set covering every rule in the contract document, and 100% of valid documents in that
  set are stored and read back equal as a JSON value.
- **SC-004**: Moving or renaming a page with 200 descendants recomputes every address
  and completes in under 2 seconds on the reference environment (two cores, four
  gigabytes of memory).
- **SC-005**: On a site with 2,000 pages, the page tree screen shows the full tree within
  2 seconds on the reference environment.
- **SC-006**: The isolation suite finds zero pages, components or drafts of one site
  reachable through another, on either surface.
- **SC-007**: The contract document and the schema definition the product validates
  against never disagree in a merged build.
- **SC-008**: The page tree and component journeys can be completed with the keyboard
  alone, and 100% of their visible text resolves through the translation lookup.

## Scope

### In Scope

- The Pages module with a per-site page tree: create, read, list, rename, change slug,
  reorder, move and delete pages, and choose the home page.
- Slug rules and suggestions, computed addresses, address uniqueness and reserved
  addresses.
- Shared components: create, read, list, rename, delete, and their use by page drafts.
- One draft content document per page and component, versioned and validated against
  the page schema contract.
- The page schema contract document in `docs/contracts/` and the shared schema
  definition, with a build check that keeps them in step.
- Capabilities, integration events, isolation suite registration, admin shell screens,
  upgrade migration, smoke-test extension.

### Out of Scope

- The visual editor and content block types (#11).
- Publishing, published copies, versions, rollback and rendering (#12).
- Collection templates and pages generated from collection items (#16).
- Redirects from old addresses and other SEO settings (#17).
- Trash and restore of pages and components (#21).
- Site export and import (#20).
- Navigation menus built from the page tree.
- Multi-language pages and per-language addresses.
- Nesting components inside components.
- Tracking which drafts use which media files.

## Assumptions

- **Uniqueness is on the full address, not the slug**: two pages of one site can never
  share an address, because rendering (#12) must map every address to one page, but the
  same slug may appear under different parents (FR-006).
- **One home page per site at `/`**, chosen among top-level pages, whose slug does not
  appear in its children's addresses. This matches the common expectation that
  `/about` is a top-level page even when it sits under the home page in the tree.
- **Slugs are ASCII** (lowercase letters, digits, hyphens). Internationalised slugs are
  left for the localisation work in the backlog.
- **Deletion is permanent** in this feature, consistent with feature 009 leaving trash
  and restore to #21. Pages are not public yet, so nothing visitors see is lost.
- **Limits** (10 levels, 2,000 pages per site, 1 MB / 5,000 nodes / depth 32 per
  document, 100-character slugs, 1,024-character addresses) are generous defaults for
  sites built by hand. They are documented in the contract and can be raised later
  without breaking stored data.
- **Component references are the only cross-document link** in this feature. The editor
  (#11) adds content blocks, and their properties may later reference media or
  collections.
- **Draft saving is whole-document**: a save replaces the entire draft. Finer-grained
  collaborative editing is the editor's concern.

## Dependencies

- Feature 001 (foundation skeleton): the module structure, the outbox and the smoke test.
- Feature 002 (identity and setup): capabilities, per-site grants, the forbidden versus
  not-found rule, and the admin shell baselines.
- Feature 003 (media storage): the reserved media address space.
- Feature 009 (sites and host name resolution): the site context, base URL, disabled
  sites and the shared isolation suite.
- The project constitution, in particular Principles I (rich domain model required for
  Pages), III (upgrades never lose data), V (multi-site), VI (content schemas are
  versioned contracts), VII (draft and published are separate), IX (secure by default)
  and X (contract tests).
- GitHub issue #10, which this feature implements and which the pull request closes.
