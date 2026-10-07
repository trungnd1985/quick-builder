# Feature Specification: CMS Items

**Feature Branch**: `015-cms-items`

**Created**: 2026-10-07

**Status**: Draft

**Issue**: #15

**Input**: User description: "#15 CMS items. Problem / goal: A defined collection is useless until content can be entered. An editor can create, edit, duplicate and delete items of a collection through a form generated from the schema, save drafts, publish items and schedule publishing. In: item CRUD, form generated from the collection schema, per-field validation including required and uniqueness of the item slug, draft and published data kept separately, publish/unpublish and scheduling, reference pickers and referential integrity checks on delete, item list with search, filter, sort and paging, permissions scoped to Editor role. Out: rendering items on a site (#16), bulk import (#20), workflow/approvals."

## Context

Feature 014 lets Administrators and Designers define collections: a named set of items that
share one schema. It stores items only as data that schema changes migrate, and leaves
several things to this feature: the screens to write items, the item's address name
(**slug**), the item's status and publishing, what happens to references when an item is
deleted, and what to do with items that are missing a required value or that no longer meet
a tightened field setting.

This feature gives each collection an item list and an item form that is built from the
collection's schema, so the people who write content (Editors) can create, edit, duplicate,
delete, publish, unpublish and schedule items. As Principle VII requires, an item's draft
and its published data are kept apart: editing never changes the published data until the
item is published, and every publish produces a version that can be rolled back.

Nothing in this feature shows items to visitors. Feature 016 (#16) renders published items
on pages. This feature makes sure that, when #16 arrives, it finds only published data,
valid references and an announcement for every change it must react to.

## Clarifications

### Session 2026-10-07

- Q: May Editors publish, unpublish, schedule and roll back items, or only prepare drafts?
  → **A: Editors may do every item action.** Editors, Designers and Administrators have the
  same item permissions, including publish, unpublish, schedule, roll back and delete. This
  differs on purpose from pages in 012 (FR-001).
- Q: May a draft be saved with a required field empty or a value breaking its field's
  settings? → **A: yes.** The draft is saved and the item is marked as needing attention;
  publishing is refused until it is fixed. Malformed values are always refused (FR-013,
  FR-014, FR-020).
- Q: When an item that other items refer to is deleted, is the delete refused or are the
  references removed? → **A: refused.** The refusal lists every referring item; the
  references must be changed first. Deletion never edits other items (FR-008).
- Q: May an item be published while it refers to items that are not published? → **A: yes,
  with a warning.** The member is told which references are not published; #16 leaves them
  out on the site until they are published (FR-017, FR-020).
- Q: Does the item list search match only name and slug, or also text field content? →
  **A: name and slug only.** Searching field content is backlog (FR-029).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An Editor writes and saves a new item (Priority: P1)

An Editor opens the "Blog posts" collection and chooses "New item". A form appears with one
input per field, in the order the Designer set: a text box for Title, a multi-line box for
Summary, a rich text editor for Body, an image picker for Cover image, a date picker for
Published on, a switch for Featured, a number box for Reading time and a drop-down for
Category. Each shows its help text. They type the title "Hello world"; the slug
"hello-world" is suggested from it. They fill in some fields, leave others empty and save a
draft. The item appears in the list as a draft. They come back the next day, finish it and
save again.

**Why this priority**: Writing content is the whole purpose of a collection. Without it no
other story has anything to act on. On its own it lets a team fill a collection before
anything is published.

**Independent Test**: In a collection with one field of every type and every setting,
create an item through the form, filling every field, save it, reopen it, and confirm every
value comes back exactly as entered. Then confirm that a malformed value is refused with a
reason against its field, that an empty required field is allowed in a draft but flagged,
and that a slug already used in the collection is refused.

**Acceptance Scenarios**:

1. **Given** a collection and a member with the Editor, Designer or Administrator role on
   its site,
   **When** they choose to create an item,
   **Then** a form shows one input per field of the current schema, in schema order, with
   the field's display name, help text, a mark on required fields and an input suited to
   its type and settings (FR-009).
2. **Given** the item form,
   **When** the member types in the field that names items,
   **Then** a slug is suggested from it until the member edits the slug themselves.
3. **Given** a filled form,
   **When** the member saves a draft,
   **Then** the item is stored with that draft data, is not published, and appears in the
   list as a draft; reopening it returns every value exactly as saved.
4. **Given** a form with a value that is malformed for its field (a number with letters, an
   option that no longer exists, a reference to an item of another collection),
   **When** it is saved,
   **Then** the save is refused, nothing is stored, and each problem is shown next to the
   field it concerns.
5. **Given** a form with a required field left empty, or a value that breaks a field
   setting (longer than the maximum length, outside the number range),
   **When** the member saves a draft,
   **Then** the draft is saved, and the item and each such field are marked as needing
   attention before the item can be published.
6. **Given** a slug that another item of the same collection uses, ignoring letter case,
   **When** the item is saved,
   **Then** it is refused with a reason against the slug field.
7. **Given** a user who holds no role on the site,
   **When** they try any item operation,
   **Then** the response is the same as for a site that does not exist.

---

### User Story 2 - Publishing and unpublishing an item (Priority: P1)

The Editor's post is ready. They choose "Publish". The item's draft becomes its published
data and the list shows it as published, with the time and their name. Later they fix a
typo in the draft and save: the list shows "Published, with unpublished changes" and the
published data still holds the old text until they publish again. A post with an outdated
announcement is unpublished; it keeps its draft and can be published again later.

**Why this priority**: Principle VII requires draft and published data to be separate
worlds, and #16 renders only published data. Without publish, no item can ever reach a
site.

**Independent Test**: Publish an item, change its draft, and confirm that the published data
is unchanged until the next publish. Try to publish an item that is missing a required value
and confirm the refusal names the field. Unpublish an item and confirm it has no published
data but keeps its draft.

**Acceptance Scenarios**:

1. **Given** an item whose draft passes every check (FR-020),
   **When** a member publishes it,
   **Then** in one step its draft becomes its published data, a new version is recorded
   with who and when, and the item's status becomes published.
2. **Given** a published item,
   **When** its draft is changed and saved,
   **Then** its published data does not change, and the item is shown as published with
   unpublished changes.
3. **Given** an item whose draft is missing a required value or holds a value that breaks
   its field's settings,
   **When** a member tries to publish it,
   **Then** it is refused, and the reason lists every field that must be fixed.
4. **Given** an item whose draft refers to items that are not published,
   **When** a member publishes it,
   **Then** they are told which references are not published and will not be shown on the
   site until they are, and the publish goes ahead.
5. **Given** a published item,
   **When** a member unpublishes it,
   **Then** it no longer has published data, its draft is kept, the change is recorded in
   its history, and it is shown as a draft.
6. **Given** a published item that published items refer to,
   **When** a member unpublishes it,
   **Then** they are told how many published items refer to it before confirming.
7. **Given** two members publishing the same item at the same time,
   **When** both publish, the second retrying once if the first changed the item,
   **Then** each produces its own complete version one after the other; no published data
   mixes content from both.

---

### User Story 3 - Finding items in a large collection (Priority: P1)

The product catalogue has 4,000 items. An Editor searches "linen" and sees the matching
products. They filter to items in the "Shirts" category that have unpublished changes, sort
by price, and page through the results 50 at a time. Another filter shows only the items
that need attention after the Designer tightened a field's settings.

**Why this priority**: Every other action on an existing item starts from the list. Past a
few dozen items, a list without search, filters and paging is unusable.

**Independent Test**: In a collection of 10,000 items, search by name and by slug, filter by
status, by "needs attention" and by option, boolean and reference values, sort by each
sortable column in both directions, and page through. Confirm the results are correct,
complete and stable across pages, and appear within the time in SC-003.

**Acceptance Scenarios**:

1. **Given** a collection,
   **When** a member opens its item list,
   **Then** they see each item's name, slug, status, any pending schedule, whether it needs
   attention, when it was last changed and by whom, newest change first, one page at a
   time.
2. **Given** an item list,
   **When** the member types a search term,
   **Then** only items whose name or slug contains it, ignoring letter case and accents,
   are listed.
3. **Given** an item list,
   **When** the member filters by status, by "needs attention", or by the value of an
   option, boolean or reference field,
   **Then** only items matching every chosen filter are listed, and the total count is
   shown.
4. **Given** an item list,
   **When** the member sorts by name, slug, status, created time, last changed time,
   published time, or a number or date field, in either direction,
   **Then** the items are listed in that order, and items without a value in the sorted
   field come last.
5. **Given** a result larger than one page,
   **When** the member moves between pages or changes the page size,
   **Then** each item appears on exactly one page, and the search, filters and sort are
   kept.

---

### User Story 4 - Linking items with reference pickers (Priority: P2)

Each blog post has an Author (a reference to the "Team members" collection) and Related
posts (a multi-reference to "Blog posts" itself). In the form, the Editor clicks Author and
searches "ngu" to pick a team member by name. For Related posts they pick three posts, drag
the third to the top, and remove one. A reference to a draft-only team member is marked as
not published.

**Why this priority**: Reference fields are how a content model ties together. They are
defined in 014 but are unusable without a way to choose items. They build on the form from
User Story 1.

**Independent Test**: In an item form with a reference and a multi-reference field, pick
items through search, reorder and remove them with both the mouse and the keyboard, save,
and confirm the stored references and their order. Confirm that an item of another
collection or site, a deleted item and more than the limit cannot be stored.

**Acceptance Scenarios**:

1. **Given** a reference or multi-reference field,
   **When** the member opens its picker,
   **Then** they can search the target collection's items by name or slug and see each
   one's name and status, one page at a time.
2. **Given** a reference field,
   **When** the member picks an item,
   **Then** it replaces any earlier choice; the member can also clear it.
3. **Given** a multi-reference field,
   **When** the member picks, removes and reorders items, by dragging or by the keyboard
   equivalent,
   **Then** the saved value holds those items in that order, each at most once, up to the
   field's limit of 100.
4. **Given** a reference to an item that is not published,
   **When** the form or the list shows it,
   **Then** it is marked as not published.
5. **Given** a multi-reference field targeting the item's own collection,
   **When** the member picks items,
   **Then** they may pick other items of the collection, and may not pick the item itself.

---

### User Story 5 - Deleting and duplicating items safely (Priority: P2)

An Editor wants to delete a team member who left. They are told that 6 blog posts, 4 of
them published, still name this person as their Author, and the deletion is refused with
a list of those posts. They change the posts' authors, then delete the team member. Later
they duplicate a product to create a similar one: the copy opens as a new draft named
"Linen shirt (copy)" with the slug "linen-shirt-copy".

**Why this priority**: Deletion and duplication complete the item lifecycle. Referential
integrity on delete is what keeps #16 from rendering broken links. It is rarer than
writing and publishing.

**Independent Test**: Delete an item no one refers to, an item referred to by another
item's draft only, by another item's published data only. Confirm only the
first is deleted, and the refusals list every referring item. Duplicate an item
with values in every field and confirm the copy's values, slug and status.

**Acceptance Scenarios**:

1. **Given** an item that no other item refers to in its draft, published data or pending
   scheduled content,
   **When** a member deletes it and confirms,
   **Then** the item, its draft, published data, versions and schedules are deleted
   permanently, and its slug becomes free.
2. **Given** an item that other items refer to,
   **When** a member tries to delete it,
   **Then** it is refused, and the reason lists each referring item with its collection,
   field and whether the reference is in its draft, its published data or both.
3. **Given** a multi-reference field targeting the item's own collection,
   **When** a member tries to save the item referring to itself,
   **Then** it is refused (FR-016), so a self-reference can never block a deletion.
4. **Given** a published item,
   **When** a member deletes it,
   **Then** the confirmation says that it is live and will disappear from the site.
5. **Given** an item,
   **When** a member duplicates it,
   **Then** a new draft item is created with a copy of the original's draft values, its
   name marked as a copy, a unique slug derived from the original's, no published data,
   no versions and no schedules, and it opens for editing.

---

### User Story 6 - Scheduling a publish or an unpublish (Priority: P2)

A seasonal sale post must go live on Monday at 09:00 and come down on Friday at 18:00. The
Editor finishes the post and schedules both. The list shows the pending schedules. They
change the draft afterwards: the scheduled publish still publishes the content they
reviewed when scheduling, and the item shows that its draft differs from what is scheduled.
On Monday at 09:00 the post is published, and its history records a scheduled publish.

**Why this priority**: Scheduling is named in the issue and is common for campaigns, but
items can be published by hand without it.

**Independent Test**: Schedule a publish two minutes ahead, change the draft, and confirm the
content at scheduling goes live, not the later draft. Schedule an unpublish and confirm it
happens. Stop the application across a scheduled time, start it again, and confirm each
scheduled action happens once, promptly after start.

**Acceptance Scenarios**:

1. **Given** an item whose draft passes every publish check,
   **When** a member schedules its publish for a future time,
   **Then** the draft at that moment is kept for the schedule, and at that time it is
   published as a new version recorded as a scheduled publish.
2. **Given** a published item, or one with a scheduled publish,
   **When** a member schedules its unpublish for a future time (after any scheduled
   publish),
   **Then** at that time it is unpublished, and the history records it as scheduled.
3. **Given** a pending schedule,
   **When** a member cancels it or changes its time,
   **Then** the change is recorded, and the cancelled action does not happen.
4. **Given** a pending scheduled publish,
   **When** a member publishes, unpublishes or rolls back the item by hand,
   **Then** they are told first that the pending scheduled publish will be cancelled, and
   after confirming it is cancelled and the history records it.
5. **Given** a scheduled publish whose kept content no longer passes the publish checks
   when its time comes (for example a field was made required in the meantime),
   **When** the time comes,
   **Then** nothing is published, the failure is recorded in the item's history with the
   reason, and the item is shown as needing attention.

---

### User Story 7 - Seeing an item's history and rolling back (Priority: P3)

A product's price was published wrong this morning. The Editor opens the item's history and
sees every published version: number, time, who, and how it came about (publish, scheduled
publish, rollback, unpublish). They roll back to yesterday's version; it becomes the
published data at once, as a new version, and the draft is unchanged.

**Why this priority**: Principle VII requires every publish to produce a version that can be
rolled back. It matters less often than every other story.

**Independent Test**: Publish an item three times with different content, roll back to
version 1, and confirm the published data equals version 1, a new version 4 records the
rollback, the draft is unchanged, and the history shows all four versions.

**Acceptance Scenarios**:

1. **Given** an item with published versions,
   **When** a member opens its history,
   **Then** they see every kept version, newest first, with its number, time, member and
   kind, and which version is live.
2. **Given** an earlier version,
   **When** a member rolls back to it,
   **Then** its content is published again as a new version recorded as a rollback of it,
   and the draft is not changed.
3. **Given** an earlier version,
   **When** a member copies it into the draft,
   **Then** the draft is replaced with that version's content and nothing is published.
4. **Given** an earlier version whose content no longer passes the publish checks (a slug
   now used by another item, a reference to a deleted item, a value the schema no longer
   accepts),
   **When** a member tries to roll back to it,
   **Then** it is refused with the reasons; copying it into the draft is still allowed.

---

### Edge Cases

- **Schema changed while the form is open**: saving a form built from an older schema
  revision is refused; the member is shown the current schema with their entered values
  kept for every field that still exists, and the fields that changed are pointed out.
- **Two members editing the same item**: the second save is refused as a conflict, nothing
  they changed is stored, and they are offered the latest draft.
- **Item write during a schema change** (014 FR-037): saving, publishing, unpublishing,
  scheduling, rolling back and deleting are refused with a reason saying the collection's
  fields are being updated; the form keeps the member's input so they can retry.
- **A collection with no single-line text field**: items have no name field; they are
  listed and picked by slug, and the slug is suggested from the creation time until set.
- **Slugs**: 1–100 characters of lower-case letters, digits and hyphens, starting with a
  letter or digit, unique within the collection ignoring letter case among every other
  item's draft and published slug. A suggested slug that is already used gets a number
  added ("hello-world-2").
- **Changing the slug of a published item**: the published slug changes only at the next
  publish; the member is told that the item's published address will change (#16 decides
  how old addresses keep working).
- **Name field empty in a draft**: the item is listed by its slug and marked as needing
  attention, because the name field is always required (014 FR-011).
- **Media deleted after it was chosen**: the field shows the image or file as missing, the
  item needs attention, and publishing is refused until it is replaced or cleared.
- **Rich text** is stored in the structured format of Principle IX; pasted content is
  reduced to what that format allows, never kept as raw markup.
- **Dates** are entered and shown in the member's own time zone and stored as defined by
  the 014 contract (a date-only field has no time zone; a date-and-time field is an exact
  moment). Scheduled times are exact moments, shown in the member's time zone.
- **Boolean** fields that have never been set count as false (014 FR-010) and are never
  missing a required value.
- **A scheduled time in the past** is refused when set; a schedule whose time passes while
  the application is stopped runs once, promptly after start.
- **Duplicating an item whose name or slug would exceed its maximum length** with the copy
  mark: the name or slug is shortened to fit.
- **Collection deleted while an item form is open** (014): saving is refused as for an item
  that no longer exists; the form treats a missing collection and a missing item the same way.
- **Large collections**: lists, searches and pickers stay within SC-003 for a collection of
  10,000 items.

## Requirements *(mandatory)*

### Functional Requirements

**Access**

- **FR-001**: Members with the Editor, Designer or Administrator role on a site MUST be able
  to perform every item operation in this feature on that site's collections: list, view,
  create, edit, duplicate, delete, publish, unpublish, schedule, cancel schedules, view
  history and roll back. Users without a role on the site MUST receive the same response as
  for a site that does not exist. Authorization MUST be enforced by the server on every
  operation.

**Items**

- **FR-002**: An item MUST have a permanent identifier, belong to exactly one collection and
  site, and have a slug, a draft, optional published data, a status, a revision number,
  who created it and when, who last changed it and when, its published versions and its
  pending schedules.
- **FR-003**: The slug MUST follow the rules in Edge Cases. It MUST be kept with the draft
  and with the published data like the item's values, MUST be suggested from the field that
  names items while the member has not edited it, and MUST NOT equal another item's draft or
  published slug in the same collection, ignoring letter case.
- **FR-004**: An item's status MUST be one of: **draft** (no published data),
  **published** (published data equals the draft) or **changed** (published, and the draft
  differs). A pending scheduled publish or unpublish and "needs attention" MUST be shown
  alongside the status.
- **FR-005**: Every write to an item (save, publish, unpublish, schedule, rollback, delete)
  MUST state the item revision it was based on, and MUST be refused as a conflict if the
  item has changed since. A save MUST also state the schema revision its form was built
  from, and MUST be refused if the collection's schema has changed since.
- **FR-006**: Every write to an item MUST be refused while a schema change is being applied
  to its collection (014 FR-037), with the reason given there.
- **FR-007**: Duplicating an item MUST create a new draft item as described in User Story 5,
  scenario 5.
- **FR-008**: Deleting an item MUST require confirmation and MUST delete it permanently with
  its draft, published data, versions and schedules; there is no trash until #21. It MUST
  be refused while any other item refers to it in its draft, its published data or
  the content of a pending scheduled publish; the refusal MUST
  list each referring item (up to 50, with the total) with its collection, field and where
  the reference is.

**The item form**

- **FR-009**: The item form MUST be generated from the collection's current schema, in
  schema order, with each field's display name, help text and required mark, and an input
  for each type and setting:
  - **Text**: a single-line or multi-line box with the remaining length shown.
  - **Rich text**: the rich text editor already used by the admin, limited to the
    structured format of Principle IX.
  - **Number**: a number box that accepts whole numbers or the field's decimal places, and
    shows the minimum and maximum.
  - **Boolean**: a switch.
  - **Date**: a date picker, or a date and time picker.
  - **Image** and **File**: a picker from the site's media library limited to the field's
    allowed kinds, with a preview or file name and a way to clear it.
  - **Option**: a drop-down of the field's options in their order, with a way to clear it.
  - **Reference** and **Multi-reference**: the pickers in FR-016.
- **FR-010**: Every control in the form MUST be reachable and usable with the keyboard alone
  and announced to assistive technology, and every problem MUST be announced and shown next
  to its field, matching the admin accessibility baseline.
- **FR-011**: Leaving a form with unsaved changes MUST ask for confirmation.

**Validation**

- **FR-012**: The server MUST validate item values against the same definition as the 014
  collection schema contract that the admin uses, and the admin MUST check values as the
  member types, using that same definition.
- **FR-013**: A save MUST be refused, with each problem reported against its field, when a
  value is **malformed**: of the wrong kind for its field, an option or field identifier
  that does not exist, a reference to an item that does not exist or is of another
  collection or site, media of another site, a duplicate in a multi-reference, more than
  100 references, or a slug that breaks FR-003.
- **FR-014**: A draft MUST be saveable while a required field has no value or a value breaks
  its field's settings (maximum length, number range, decimal places, allowed file kinds),
  or a chosen media file has been deleted. Such an item MUST be marked as **needs
  attention** with the list of affected fields. The same applies to items made incomplete
  or invalid by a schema change (014 FR-027, FR-029).
- **FR-015**: Item values MUST be stored keyed by field identifier and option identifier as
  required by the 014 contract, never by key or label.

**References and media**

- **FR-016**: A reference picker MUST let the member search the target collection's items
  by name or slug, page through results, and see each item's name and status. A
  multi-reference picker MUST also let the member reorder and remove chosen items, with a
  keyboard equivalent for every drag action. An item MUST NOT be able to refer to itself.
- **FR-017**: References to items without published data MUST be allowed in drafts and in
  published data, and MUST be marked as not published wherever they are shown.
- **FR-018**: The system MUST keep track of which items refer to which, and which items use
  which media files, in their drafts, published data and scheduled content, so that
  FR-008 can be checked and the media library's usage information (013) includes items.

**Publishing**

- **FR-019**: Publishing MUST copy the item's draft and slug into its published data in one
  atomic step and record a new version with its number, time, member and kind.
- **FR-020**: Publishing MUST be refused while the draft needs attention (FR-014) or holds a
  malformed value, listing every field to fix. It MUST NOT be refused because a referenced
  item is not published; the member MUST be told which references are not published.
- **FR-021**: Unpublishing MUST remove the item's published data, keep its draft and
  versions, and record the unpublish in its history. When published items refer to it, the
  member MUST be told how many before confirming.
- **FR-022**: Rolling back MUST publish a kept version's content as a new version recorded
  as a rollback of it, leaving the draft unchanged, and MUST be refused when that content
  fails the publish checks in force today. Copying a kept version into the draft MUST
  always be possible and MUST NOT publish anything.
- **FR-023**: The last 50 published versions of each item MUST be kept; older versions MUST
  be removed, never the live one.

**Scheduling**

- **FR-024**: Members MUST be able to schedule an item's publish and its unpublish for a
  future time, change that time, and cancel either. An item MUST have at most one pending
  scheduled publish and one pending scheduled unpublish, and a scheduled unpublish MUST be
  later than a scheduled publish.
- **FR-025**: A scheduled publish MUST keep the draft as it was when scheduled, MUST pass the
  publish checks when it is set, and MUST publish that kept content at its time, even if the
  draft has changed since; the item MUST show when its draft differs from what is
  scheduled. Schema changes MUST migrate kept scheduled content as they migrate drafts and
  published data.
- **FR-026**: Scheduled actions MUST run once, at or promptly after their time, including
  after the application was stopped across that time, and on one instance only when several
  run. A scheduled publish whose kept content fails the publish checks at its time MUST NOT
  publish, MUST record the failure with its reason, and MUST mark the item as needing
  attention.
- **FR-027**: A manual publish, unpublish or rollback MUST cancel a pending scheduled
  publish, after telling the member before confirming; the history MUST record the
  cancellation.

**Item list**

- **FR-028**: The item list MUST show the columns in User Story 3, scenario 1, MUST page its
  results with a page size of 25, 50 or 100 (default 25), and MUST show the total count.
- **FR-029**: The list MUST support search by name or slug (contains, ignoring letter case
  and accents); filters by status, pending schedule, needs attention, and the value of
  option, boolean and reference fields, combined with "and"; and sorting by name, slug,
  status, created time, last changed time, published time and any number or date field, in
  both directions, with items lacking a value last and a stable order between equal values.
- **FR-030**: The list's search, filters, sort, page and page size MUST be kept when the
  member opens an item and returns, and MUST be reflected in the page address so a list
  view can be shared and reloaded.

**History and announcements**

- **FR-031**: Each item MUST have a history listing its published versions and its publish,
  unpublish, rollback, schedule, cancellation and scheduled-action failure events with
  time and member. The history MUST NOT be shown to users without a role on the site.
- **FR-032**: Every creation, change, publish, unpublish and deletion of an item MUST be
  announced to the rest of the product (Principle I) with the item, collection, site and
  whether published data changed, so that #16 can refresh what depends on it. Logs and
  announcements MUST NOT contain item values.

### Key Entities

- **Item**: one entry of a collection. Has an identifier, slug, status, revision number,
  draft, optional published data, creation and last change (who, when), versions and
  schedules. Belongs to one collection and site.
- **Item draft / published data**: the item's values keyed by field identifier, plus its
  slug, in the 014 contract format. The draft is what members edit; the published data is
  what #16 will show.
- **Item version**: a kept copy of an item's published data with a number, time, member and
  kind (publish, scheduled publish, rollback). Up to 50 per item.
- **Schedule**: a pending publish (with its kept content) or unpublish of an item at a set
  time, with who set it.
- **Item history entry**: a record of a publish, unpublish, rollback, schedule change,
  cancellation or failed scheduled action, with time and member.
- **Item reference**: the record that one item refers to another through a field, and
  whether in its draft, published data or scheduled content. Used for delete checks and
  "not published" marks.
- **Title field**: the single-line text field that names items (014 FR-011, `titleFieldId`);
  the plan and tasks use this term.
- **Needs-attention record**: the list of an item's fields that are missing a required
  value, break their settings or point at deleted media; derived from the item and its
  schema.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Editor who has not used the item form before can create and publish an item
  in a collection with eight fields of different types, including an image, an option and
  a reference, in under 3 minutes.
- **SC-002**: Across a test covering every field type and setting, 100% of values saved
  through the form are read back exactly as entered, and 100% of malformed values are
  refused against the right field.
- **SC-003**: In a collection of 10,000 items on the reference setup, a list page, a search,
  any filter or sort, and a reference picker search each show results within 1 second for
  95% of requests.
- **SC-004**: Editing and saving an item's draft changes its published data in 0 cases; only
  publish, unpublish, rollback and scheduled actions do.
- **SC-005**: Across a test that deletes, unpublishes and changes items in every
  combination, 0 items are left referring to an item that no longer exists.
- **SC-006**: 100% of scheduled actions run exactly once, within 1 minute of their time, or
  within 1 minute of start-up when the application was stopped across that time.
- **SC-007**: Every action in the item list, item form and pickers can be completed with
  the keyboard alone, and an automated accessibility check of them reports no serious or
  critical issues.

## Assumptions

- **Roles** (clarified 2026-10-07): Editors write and publish items. This differs on purpose from pages (012),
  where only Designers and Administrators publish: the issue makes item content, including
  publishing and scheduling, the Editor's job, and the schema already constrains what
  Editors can change. Designers and Administrators can do everything an Editor can. There
  is no approval workflow (out of scope).
- **Drafts may be incomplete** (clarified 2026-10-07): following common practice in comparable products, required
  values and field settings are enforced at publish, not at every draft save; malformed
  values are always refused. 014 left this decision to this feature.
- **Referential integrity on delete blocks rather than clears** (clarified 2026-10-07): deleting an item that others
  refer to is refused until those references are changed, mirroring 014's rule for deleting
  a referenced collection. Unpublishing is allowed with a warning, because #16 will simply
  not show references to unpublished items.
- **Publishing does not require referenced items to be published** (clarified 2026-10-07),
  because items may refer
  to each other in a cycle and neither could be published first.
- **Versioning and rollback** follow 012 (50 kept versions, rollback as a new version, manual
  actions cancel a pending scheduled publish), because Principle VII requires a version that
  can be rolled back for every publish of pages, components and items alike.
- **Slugs** are unique within a collection, not across the site, because #16's item
  addresses include the collection's address name.
- **Media usage**: items' image and file values are reported in the media library's usage
  information from this feature on, as 014 deferred.
- **Rendering items on a site** (#16), **bulk import and export** (#20), **trash and
  restore** (#21), **draft preview links** (#22), **approvals**, **localisation**,
  **bulk actions in the list** (publish or delete many at once), **searching the content of
  text and rich text fields** (clarified 2026-10-07, backlog) and **unique values for
  fields other than the slug** are out of scope.
- **Limits**: up to 10,000 items per collection is the tested size; it is not a hard limit.
- **Upgrade**: items in existing databases (created only by tests and demos in 014) receive
  a slug derived from their name or identifier and the draft status, or the published
  status if they already hold published data, during the upgrade.
