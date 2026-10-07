# Feature Specification: CMS Collections and Schema Builder

**Feature Branch**: `014-cms-collections-schema`

**Created**: 2026-10-07

**Status**: Draft

**Issue**: #14

**Input**: User description: "#14 CMS collections and schema builder. Problem / goal: Sites need structured, repeatable content such as blog posts or products. An administrator or designer can define a collection, add fields of different types, reorder and rename them, and change the schema safely after items already exist. In: collection CRUD, field types (text, rich text, number, boolean, date, image, file, option/enum, reference, multi-reference), stable immutable field IDs, schema stored as JSON with schemaVersion, schema validation, safe schema changes (rename, reorder, add, remove, type change rules) with data migration, the collection schema contract document. Out: item editing (#15), templates and binding (#16), localisation of fields."

## Context

Features 010 to 013 give each site pages, shared components, a visual editor, publishing
and a media library. Everything on a page today is written by hand into that page. Sites
also need content that repeats with the same shape: blog posts, products, team members,
events. This feature introduces the **collection**: a named, site-owned set of items that
all share one **schema**, the list of fields every item has.

This feature covers defining and changing collections and their schemas. Creating and
editing the items themselves is feature 015 (#15), and showing items on pages through
templates and bindings is feature 016 (#16). Because a schema may change after items
exist, this feature also defines how the values already stored in items are carried
through each change. Items exist here only as the stored data that a schema change must
migrate.

Constitution Principle VI makes the collection schema a versioned contract shared by the
editor and the renderer. This feature writes that contract, alongside the existing page
schema and design token contracts.

## Clarifications

### Session 2026-10-07

- Q: Should a schema change apply straight away to items' published data as well as their
  drafts? → **A: yes, one live schema.** A collection has no draft or published schema.
  Each saved change migrates every item's draft and published data together, at once
  (FR-021).
- Q: Are values removed with a field, or cleared by a type change, kept anywhere for
  restore? → **A: no, gone permanently.** Once the member confirms the preview, those
  values are deleted; the preview and confirmation are the safeguard (FR-023, FR-024).
- Q: While a schema change is being applied, what happens to item saves and publishes in
  that collection? → **A: refused until it ends.** They are refused with a reason saying
  the collection's fields are being updated; reading items, including by the published
  site, keeps working (FR-037).
- Q: Should text and number fields have a "must be unique" setting? → **A: no, not in
  this feature.** It goes to the backlog; it can be added later as a new optional setting
  without breaking the contract.
- Q: When a collection is deleted, is it gone permanently or moved to a trash? → **A: gone
  permanently** after the typed-name confirmation; collections join trash and restore
  with #21 (FR-004).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A Designer defines a new collection and its fields (Priority: P1)

A Designer wants a blog. They open the site's Collections area and create a collection
called "Blog posts", which gets the address name "blog-posts". They add a Title (text,
required), a Summary (text, several lines), a Body (rich text), a Cover image (image), a
Published on (date), a Featured flag (boolean), a Reading time (whole number) and a
Category (option, with "News", "Guides" and "Releases"). They mark Title as the field that
names each item, and save. The collection appears in the list with its eight fields and
no items.

**Why this priority**: Nothing else in the CMS works without a collection and its schema.
Items (#15) and templates (#16) both build on it. On its own it lets a team agree and set
up their content model before anyone writes content.

**Independent Test**: Create a collection with one field of each type, save it, reload it,
and confirm that every field, its settings and its order come back exactly as saved; that
each field has an identifier that the system chose; and that an invalid schema (two fields
with the same key, an option field with no options, a reference to a collection of another
site) is refused with a reason that points at the field.

**Acceptance Scenarios**:

1. **Given** an Administrator or Designer of a site,
   **When** they create a collection with a name, an address name and an optional
   description,
   **Then** it is created empty, belongs to that site only, and appears in the site's
   collection list; the address name is suggested from the name and is unique within the
   site, ignoring letter case.
2. **Given** a collection,
   **When** the member adds a field by choosing a type, a display name and a key,
   **Then** the system gives the field a permanent identifier, the key is suggested from
   the display name, and the type's settings (FR-010) are offered with sensible defaults.
3. **Given** a schema with fields of every type,
   **When** it is saved and read back,
   **Then** it is equal to what was saved, including the order of fields and of each
   option field's options.
4. **Given** a schema that breaks a rule of the collection schema contract (FR-018, FR-019),
   **When** it is saved,
   **Then** it is refused, nothing is stored, and each problem is reported against the
   field and setting it concerns.
5. **Given** a collection,
   **When** the member chooses which single-line text field names each item,
   **Then** that field becomes required and is the label used for the collection's items
   wherever they are listed or picked, for example in a reference field.
6. **Given** an Editor of the site,
   **When** they open the Collections area,
   **Then** they can see every collection and its fields but cannot create, change or
   delete any of them; the server refuses those operations as forbidden.
7. **Given** a user who holds no role on the site,
   **When** they try any collection operation,
   **Then** the response is the same as for a site that does not exist.

---

### User Story 2 - Renaming and reordering fields never touches content (Priority: P1)

The blog has 300 posts. The Designer renames "Summary" to "Excerpt" and changes its key
from "summary" to "excerpt", moves Cover image above Body, and renames the collection from
"Blog posts" to "Articles". Every post still has its excerpt, its cover image and its body;
nothing about the stored posts changed, and anything that refers to these fields by their
identifier keeps working.

**Why this priority**: The issue names renaming and reordering as everyday needs, and they
are the most common schema changes. Making them safe depends only on stable field
identifiers, which is the core promise of Principle VI.

**Independent Test**: In a collection whose items hold values in every field, rename fields
(display name and key), reorder them and rename the collection. Confirm that every stored
item value is unchanged, every field keeps its identifier, and the new names and order are
returned.

**Acceptance Scenarios**:

1. **Given** a collection with items,
   **When** a field's display name or key is changed,
   **Then** the field keeps its identifier, every item keeps its value for that field, and
   the new key must still be unique within the collection and not a reserved key.
2. **Given** a collection with items,
   **When** fields are reordered by dragging or by the keyboard equivalent,
   **Then** the new order is saved and no item value changes.
3. **Given** a collection,
   **When** its name, address name or description is changed,
   **Then** the collection keeps its identifier and all its items; a changed address name
   must still be unique within the site.
4. **Given** a field that was renamed,
   **When** someone later adds a new field with the field's old key,
   **Then** it is accepted as a new field with a new identifier; it never receives the
   old field's values.

---

### User Story 3 - Adding and removing fields after items exist (Priority: P1)

Months later the Designer adds an "Author" reference field to the blog and an optional
"Subtitle" text field. Existing posts simply have no subtitle and no author yet. They
also add a required "Language" option field and choose "English" as the value to fill in
for every existing post. Later they remove the "Reading time" field: before confirming,
they are told that 212 of the 300 posts hold a value in it and that those values will be
removed.

**Why this priority**: A content model always grows after launch. Without safe add and
remove, a team must decide its whole model before writing any content.

**Independent Test**: In a collection with items, add an optional field, add a required
field with a fill-in value, add a required field without one, and remove a field that
holds values. Confirm that the preview before each change states exactly how many items
are affected and how, and that the stored items afterwards match the preview.

**Acceptance Scenarios**:

1. **Given** a collection with items,
   **When** an optional field is added,
   **Then** existing items have no value for it, or the fill-in value if the member gave
   one (FR-029), and no item is otherwise changed.
2. **Given** a collection with items,
   **When** a required field is added, or an existing field is made required, together
   with a fill-in value,
   **Then** every item that has no value for that field receives the fill-in value in its
   draft and its published data, and every other item is unchanged.
3. **Given** a collection with items,
   **When** a required field is added, or a field is made required, without a fill-in
   value (or for a type that takes none, FR-031),
   **Then** the change is allowed, the preview says how many items will be missing a
   required value, and those items are reported as incomplete until someone fills them
   in (#15).
4. **Given** a field that holds values in some items,
   **When** the member removes it,
   **Then** the preview states how many items hold a value in it, removal needs an explicit
   confirmation, and afterwards the field's values are gone from every item's draft and
   published data.
5. **Given** a removed field,
   **When** a new field is added,
   **Then** the new field never receives the removed field's identifier, even if it has
   the same key and type.
6. **Given** the field that names each item (FR-011),
   **When** the member tries to remove it,
   **Then** it is refused with a reason saying what must be changed first.

---

### User Story 4 - Changing a field's type or settings with a clear outcome (Priority: P2)

The Designer changes "Reading time" from text to a whole number, because people have been
typing "5" and "about 7 minutes". The preview says 180 items convert cleanly and 12 cannot
be converted and will be cleared, and lists the first of them. They confirm. Later they
change a single "Author" reference into a multi-reference so posts can have co-authors;
the preview says this keeps every value. They try to change "Cover image" into a boolean
and are told this change is not possible and how to do it instead.

**Why this priority**: Type changes are rarer than renames and adds, but they are where
content is most easily lost. The rules have to be predictable and the outcome known before
it happens. It builds on the preview and migration from User Story 3.

**Independent Test**: For every pair of field types, attempt the change on a field holding
a variety of values. Confirm that changes the rules call lossless keep every value, that
lossy changes show an accurate count before confirmation and clear only the values they
said they would, and that changes the rules forbid are refused without changing anything.

**Acceptance Scenarios**:

1. **Given** a field and a new type that the rules mark lossless (FR-026),
   **When** the change is made,
   **Then** every item's value is converted and none is lost, and the field keeps its
   identifier.
2. **Given** a field and a new type that the rules mark lossy,
   **When** the member asks for the change,
   **Then** they are shown how many items convert, how many lose some content (for
   example formatting) and how many will be cleared, with examples, and the change happens
   only after explicit confirmation.
3. **Given** a field and a new type that the rules forbid,
   **When** the member asks for the change,
   **Then** it is refused with a reason suggesting a new field instead; nothing changes.
4. **Given** a field whose values are empty in every item,
   **When** its type is changed to any other type,
   **Then** the change is allowed without a preview of losses.
5. **Given** an option field,
   **When** an option's label is changed,
   **Then** every item that chose it still chooses it; **When** an option is removed,
   **Then** the preview counts the items that chose it and their value is cleared after
   confirmation.
6. **Given** a field whose settings are tightened (a shorter maximum length, a narrower
   number range, fewer allowed file kinds),
   **When** the change is saved,
   **Then** existing values are kept, the preview counts those that no longer meet the
   settings, and those items are reported as needing attention (#15).
7. **Given** a reference or multi-reference field,
   **When** the member tries to point it at a different collection while any item holds a
   value in it,
   **Then** it is refused; when no item holds a value, it is allowed.

---

### User Story 5 - Deleting a collection (Priority: P2)

A "Job openings" collection is no longer needed. The Designer chooses delete and is told it
holds 14 items and that the "Team members" collection has a reference field pointing at it.
They first remove that reference field, then delete the collection by typing its name to
confirm. The collection and its items are gone.

**Why this priority**: Needed to complete the collection lifecycle, but rarer than every
other story, and safe to deliver after them.

**Independent Test**: Delete an empty collection, a collection with items, and a collection
that another collection's reference field targets. Confirm the confirmation states the item
count, that a referenced collection cannot be deleted until the reference fields are removed
or retargeted, and that a deleted collection and its items no longer appear anywhere.

**Acceptance Scenarios**:

1. **Given** a collection that no reference field of another collection targets,
   **When** an Administrator or Designer deletes it and confirms by typing its name,
   **Then** the collection, its schema and all its items, draft and published, are
   deleted, and its address name becomes free.
2. **Given** a collection that a reference or multi-reference field of another collection
   targets,
   **When** someone tries to delete it,
   **Then** it is refused and the reason names each collection and field that refers to it.
3. **Given** a collection whose own reference fields target itself (for example "Related
   posts"),
   **When** it is deleted,
   **Then** those self-references do not prevent deletion.

---

### User Story 6 - Seeing how a collection's schema has changed (Priority: P3)

Something looks wrong with the products collection. An Administrator opens its schema
history and sees each saved change in order, with the revision number, who made it, when,
and a short summary such as "Renamed field Price to Unit price" or "Changed Stock from
text to number: 3 values cleared".

**Why this priority**: Helps teams understand and audit changes, but the schema works
without it.

**Independent Test**: Make several kinds of schema change and confirm that each appears once
in the history with the right revision number, person, time and summary, including the
number of item values affected.

**Acceptance Scenarios**:

1. **Given** a collection,
   **When** any change to its schema or details is saved,
   **Then** its revision number goes up by one and the change is added to its history with
   who, when and a summary of each change, including the counts from the preview.
2. **Given** two members editing the same collection's schema,
   **When** the second saves after the first,
   **Then** the second save is refused as a conflict, nothing they changed is stored, and
   they are offered the latest schema to apply their change again.

---

### Edge Cases

- **Limits**: a site may have up to 100 collections; a collection up to 100 fields; an
  option field up to 200 options; a multi-reference value up to 100 items. Exceeding one is
  refused with a reason naming the limit.
- **Names and keys**: display names are 1–100 characters. Keys and address names are 1–64
  characters of lower-case letters, digits and hyphens, start with a letter, and are unique
  ignoring letter case (keys within the collection, address names within the site).
  Reserved keys that the item itself uses (for example `id`, `slug`, `status`,
  `created-at`, `updated-at`, `published-at`) are refused.
- **Two fields with the same display name**: allowed, because keys and identifiers tell
  them apart; the builder warns.
- **A reference field targeting its own collection**: allowed.
- **A reference to a collection of another site**: refused; collections are never shared
  between sites.
- **Change applied while another change to the same collection is running**: refused as a
  conflict (User Story 6); the schema and items are never half changed.
- **A change interrupted part-way** (instance stops, database error): the schema and every
  item stay exactly as they were before the change.
- **A preview that is out of date** (items changed between preview and confirmation): the
  change is applied using the current items; if the counts now differ, the member is
  shown the new preview and asked to confirm again.
- **Changing several things in one save** (rename one field, remove another, add a third):
  applied together as one revision with one combined preview; either all of it happens or
  none of it.
- **Rich text values** stay in the structured format required by Principle IX; converting
  text to rich text produces paragraphs from line breaks, never raw markup.
- **Dates**: a date-only field holds a calendar date with no time zone; a date-and-time
  field holds an exact moment. Changing date-and-time to date-only keeps the calendar date
  in UTC, which is lossy and previewed. Sites have no time zone (012 assumption), so
  date conversions use UTC and the preview says so.
- **Number precision**: a whole-number field holds whole numbers; a decimal field states
  its number of decimal places (0–6). Reducing decimal places rounds values, which is
  lossy and previewed.
- **Image and file values** refer to media of the same site by identifier (feature 013).
  A value whose media file has since been deleted is kept as an empty reference and
  counted as missing; it never blocks a schema change.
- **Collections with no items**: every change is allowed without a preview of losses,
  including type changes the rules otherwise forbid.
- **Large collections**: a change that touches 10,000 items still completes within the
  time in SC-003, and members see that it is in progress. Item writes to that collection
  are refused until it ends (FR-037).

## Requirements *(mandatory)*

### Functional Requirements

**Collections**

- **FR-001**: Administrators and Designers of a site MUST be able to create, rename, change
  and delete that site's collections. Editors MUST be able to view collections and their
  schemas but not change them. Users without a role on the site MUST receive the same
  response as for a site that does not exist. Authorization MUST be enforced by the
  server on every operation.
- **FR-002**: A collection MUST have a permanent identifier, a display name (1–100
  characters), an address name (FR-007), an optional description (up to 500 characters),
  its schema, its revision number and its schema history. It MUST belong to exactly one
  site.
- **FR-003**: The collection list MUST show each collection's name, address name, number of
  fields, number of items and when its schema last changed, sorted by name.
- **FR-004**: Deleting a collection MUST require typing its display name to confirm, MUST
  state how many items will be deleted and that this cannot be undone, and MUST delete
  its schema, history and all its items (draft and published) permanently; there is no
  trash until #21. It MUST be refused while any reference or multi-reference field
  of another collection targets it, naming those collections and fields.
- **FR-005**: A site MUST support at least 100 collections.

**Fields**

- **FR-006**: Each field MUST have a permanent identifier chosen by the system when the
  field is created. The identifier MUST never change and MUST never be reused for another
  field of the same collection, even after the field is removed.
- **FR-007**: Each field MUST have a display name (1–100 characters), a key (1–64
  characters, lower-case letters, digits and hyphens, starting with a letter, unique in the
  collection ignoring letter case, not reserved), an optional help text (up to 300
  characters), a required flag and the settings of its type. Collection address names
  follow the same character rules and MUST be unique within the site.
- **FR-008**: Display name, key, help text and position MUST be changeable at any time
  without changing any stored item value.
- **FR-009**: The schema MUST support these field types: text, rich text, number, boolean,
  date, image, file, option, reference and multi-reference.
- **FR-010**: Each type MUST offer these settings:
  - **Text**: single line or several lines; maximum length (1–10,000, default 256 for
    single line and 5,000 for several lines).
  - **Rich text**: none beyond the common ones; values are structured rich text as
    required by Principle IX.
  - **Number**: whole number or decimal with 0–6 decimal places; optional minimum and
    maximum.
  - **Boolean**: none; an item without a value counts as false.
  - **Date**: date only, or date and time.
  - **Image**: a single image from the site's media library.
  - **File**: a single file of any kind the media library accepts, optionally limited to
    documents or to images.
  - **Option**: an ordered list of 1–200 options, each with a permanent identifier and a
    label (1–100 characters, unique in the field ignoring letter case); an item chooses
    at most one.
  - **Reference**: one target collection of the same site; an item refers to at most one
    item of it.
  - **Multi-reference**: one target collection of the same site; an item refers to an
    ordered list of up to 100 distinct items of it.
- **FR-011**: Each collection that has at least one single-line text field MUST have
  exactly one of them designated as the field that names its items; it MUST be required.
  The first single-line text field added MUST be designated automatically; the designation
  MUST be changeable to another single-line text field.
- **FR-012**: A collection MUST support up to 100 fields.

**Stored schema and items**

- **FR-013**: Each collection's schema MUST be stored as a structured document that carries
  the contract version it was written in (`schemaVersion`) and the collection's revision
  number. Both MUST be returned whenever the schema is read.
- **FR-014**: Item data MUST be keyed by field identifier, never by key or display name,
  and option values MUST be stored by option identifier.
- **FR-015**: The system MUST store each item's draft data and published data separately
  (Principle VII), so that schema changes can migrate both. Creating, editing and
  publishing items is feature 015.
- **FR-016**: Image and file values MUST refer to media of the same site by identifier.
  Reference values MUST refer to items of the target collection by identifier.

**Validation and the contract**

- **FR-017**: The collection schema MUST be documented as a versioned contract alongside
  the page schema and design token contracts, with one machine-readable definition and
  golden examples covering every field type and setting.
- **FR-018**: The admin's schema builder and the server MUST validate a schema against that
  same definition. The server MUST refuse any schema that does not conform and report each
  problem against the field and setting it concerns.
- **FR-019**: Beyond the definition, the server MUST refuse a schema that: has duplicate
  keys or option labels; uses a reserved key; changes or reuses a field or option
  identifier; targets a collection of another site or one that does not exist; exceeds a
  limit in this section; or leaves the collection without the field that names its items.
  A field that names items cannot be removed until another text field is designated.
- **FR-020**: Stored schemas and item data MUST be readable in every contract version still
  present in the database. A future breaking change to the contract MUST come with a
  forward migration of stored schemas and item data (Principle VI).

**Safe changes and migration**

- **FR-021**: Every save of a schema change MUST be atomic: the schema, the revision number,
  the history entry and every affected item's draft and published data change together,
  or nothing changes.
- **FR-022**: A save MUST state the revision it was based on; a save based on an older
  revision MUST be refused as a conflict.
- **FR-023**: Before any change that would alter, clear or invalidate stored item values,
  the member MUST be shown a preview stating how many items are affected and how (kept,
  converted, partly lost, cleared, now invalid or missing a required value), with up to 10
  examples identified by each item's name. Changes that would clear or partly lose values
  MUST require explicit confirmation. The server MUST refuse such a change unless the
  request states that the member confirmed the preview for the current item data.
- **FR-024**: Adding a field MUST NOT change any existing item, except to write a fill-in value
  (FR-029). Removing a field MUST remove
  its values from every item's draft and published data. Values removed with a field or
  cleared by a type change or option removal MUST be deleted permanently; the product
  MUST NOT keep a hidden copy for restoring them, and the confirmation MUST say that the
  loss cannot be undone.
- **FR-025**: Changing an option's label MUST NOT change any item; removing an option MUST
  clear that value in the items that chose it; reordering options MUST NOT change any item.
- **FR-026**: Changing a field's type MUST follow these rules. "Lossless" changes keep every
  value. "Lossy" changes convert what they can and clear or shorten the rest, as previewed.
  Every other change MUST be refused unless no item holds a value in the field.

  | From → To | Rule |
  |---|---|
  | Text → Rich text | Lossless; each line becomes a paragraph. |
  | Rich text → Text | Lossy; formatting is dropped, values longer than the maximum length are shortened. |
  | Number → Text | Lossless; written in a fixed, culture-independent format. |
  | Text → Number | Lossy; values that are not a number in a fixed format, or that break the number settings, are cleared. |
  | Date → Text | Lossless; written in a fixed date format. |
  | Text → Date | Lossy; values that are not a date in a fixed format are cleared. |
  | Option → Text | Lossless; the chosen option's label. |
  | Text → Option | Lossy; values matching an option's label (ignoring letter case) choose it, others are cleared. |
  | Boolean → Text | Lossless; "true" or "false". |
  | Image → File | Lossless. |
  | File → Image | Lossy; values that are not images are cleared. |
  | Reference → Multi-reference | Lossless; becomes a list of one. |
  | Multi-reference → Reference | Lossy; keeps the first item. |
  | Whole number → Decimal | Lossless. |
  | Decimal → Whole number, or fewer decimal places | Lossy; rounded. |
  | Date only → Date and time | Lossless; midnight UTC. |
  | Date and time → Date only | Lossy; keeps the calendar date in UTC. |
  | Single line ↔ several lines | Lossless; turning several lines into a single line joins lines with a space. |

- **FR-027**: Tightening a field's settings (shorter maximum length, narrower number range,
  fewer allowed file kinds) MUST keep existing values; the preview MUST count the values
  that no longer meet the settings, and those items MUST be reported as needing attention.
- **FR-028**: Changing a reference or multi-reference field's target collection MUST be
  refused while any item holds a value in it.
- **FR-029**: Making a field required, or adding a required field, MUST be allowed while
  items lack a value for it. When adding a field (required or not) or making a field
  required, the member MAY give a fill-in value, which is written to every item's draft
  and published data that lacks a value. Items still lacking a value MUST be
  reported as missing a required value.
- **FR-030**: Changes to display names, keys, help texts, field order, option labels and
  option order MUST NOT require a preview or confirmation.
- **FR-031**: A fill-in value MUST be offered for text, number, boolean, date and option
  fields. Rich text, image, file, reference and multi-reference fields take no fill-in value.
- **FR-032**: Several changes saved together MUST be applied as one revision with one
  combined preview.

**History and notifications**

- **FR-033**: Every saved change MUST add an entry to the collection's history with the new
  revision number, the member, the time and a summary of each change including the counts
  from the preview. The history MUST NOT contain item values.
- **FR-034**: Every change to a collection or its schema, and every deletion, MUST be
  announced to the rest of the product (Principle I) with the collection, site and new
  revision, so that later features (#15, #16) can refresh what depends on it.

**Admin experience**

- **FR-035**: The schema builder MUST let members add, edit, reorder and remove fields,
  edit options, and see the preview before saving. Every drag action MUST have a keyboard
  equivalent, and every control MUST be reachable and announced to assistive technology,
  matching the admin accessibility baseline.
- **FR-036**: While a change touching many items is being applied, the builder MUST show
  that it is in progress and MUST show the outcome when it ends.
- **FR-037**: While a schema change is being applied to a collection, any write to that
  collection's items (saving, publishing, unpublishing or deleting an item, on any
  instance) MUST be refused with a reason saying the collection's fields are being updated
  and to try again shortly. Reading items MUST keep working and MUST return either the
  data from before the change or the data after it, never a mix.

### Key Entities

- **Collection**: a site-owned set of items sharing one schema. Has an identifier, display
  name, address name, description, schema, revision number and history. Belongs to one
  site.
- **Schema**: the ordered list of a collection's fields and which field names its items,
  stored as a document carrying `schemaVersion` (the contract version) and the collection's
  revision number.
- **Field**: one entry of a schema. Has a permanent identifier, display name, key, help
  text, required flag, type and type settings. Option fields hold options; reference
  fields name a target collection.
- **Option**: one choice of an option field, with a permanent identifier and a label.
- **Item (stored data only)**: one entry of a collection, with a draft and a published set
  of values keyed by field identifier. Its editing is feature 015; this feature migrates its
  values when the schema changes.
- **Schema change preview**: the predicted outcome of a proposed change for the current
  items: counts per outcome and examples. Not stored.
- **Schema history entry**: a record of one saved revision: number, member, time and a
  summary of each change with its counts.
- **Collection schema contract**: the documented, versioned definition of a valid schema
  and of item values, shared by the admin and the server.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A Designer who has not used the builder before can create a collection with
  eight fields of different types, including an option and a reference field, in under 5
  minutes.
- **SC-002**: Across a test covering every field type, renaming fields, changing keys,
  reordering fields and options, and renaming the collection change 0 stored item values
  and 0 field identifiers.
- **SC-003**: A schema change affecting every item of a collection of 10,000 items
  completes within 30 seconds on the reference setup, and a preview for it appears within
  5 seconds.
- **SC-004**: For every type change in FR-026 and every option removal, the stored items
  after the change match the confirmed preview in 100% of cases: no value is lost that the
  preview did not count.
- **SC-005**: A change interrupted at any point leaves the schema and 100% of items exactly
  as they were before it.
- **SC-006**: 100% of the contract's golden examples are judged the same (valid or invalid,
  with the same problems) by the admin's validator and the server's.
- **SC-007**: Every action in the schema builder can be completed with the keyboard alone,
  and an automated accessibility check of the builder reports no serious or critical
  issues.

## Assumptions

- **Schema changes take effect immediately and are not drafted or published**
  (clarified 2026-10-07). A collection
  has one current schema; a change applies at once to both the draft and the published
  data of its items. Nothing renders items on published pages until #16, so this cannot
  change what visitors see in this feature. #16 decides how a schema change reaches
  published pages that use the collection.
- **Roles**: Administrators and Designers manage collections, because the schema is part
  of the site's design; Editors write content and therefore get read access only. This
  follows the role split of features 011 and 012.
- **Items in this feature**: there is no admin screen to create items until #15. Tests and
  demos create items directly as stored data. The item count, "needs attention" and
  "missing a required value" states are recorded here; #15 shows and acts on them,
  including whether incomplete items may be published.
- **Media usage by items**: tracking which items use a media file (feature 013's usage
  tracking) comes with #15, when items can hold values created by people.
- **Reference integrity on item deletion** (what happens to references when an item is
  deleted) belongs to #15.
- **The address name** is the collection's name in later features' addresses (for example
  item pages in #16). Changing it here is allowed; #16 decides how existing addresses are
  kept working.
- **Localisation of fields is out of scope**; every field holds one value per item.
- **Custom field types** for developers are out of scope (backlog).
- **Unique field values** (a "must be unique" setting on text or number fields) are out of
  scope (clarified 2026-10-07, backlog). Two items may hold the same value in any field.
- **Site export and import** (#20) will include collection schemas and items; the stored
  schema and item data here are sufficient for it.
- **Fixed formats** for text conversions (numbers and dates) are culture-independent, so a
  conversion gives the same result on every installation.
- **Limits** (100 collections per site, 100 fields per collection, 200 options per field,
  100 items per multi-reference value, 10,000 items as the tested collection size) are
  starting values based on comparable products and can be raised later without a contract
  change.
- **Upgrade**: this is a new area with no existing data; the upgrade from 013 adds empty
  storage for collections and items.
