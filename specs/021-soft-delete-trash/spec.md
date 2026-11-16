# Feature Specification: Soft Delete, Trash and Restore

**Feature Branch**: `021-soft-delete-trash`

**Created**: 2026-11-05

**Status**: Draft

**Issue**: #21

**Input**: User description: "#21 Soft delete, trash and restore. Problem / goal: Deleting the wrong page or item is the most common mistake end users make, and a self-hosted product cannot rely on us restoring anything for them. Deleted pages, components, collection items and media go to a per-site trash, can be restored, and are purged automatically after a retention period. In: soft delete columns and query filters for pages, shared components, collection items and media; trash view per content type with search and restore; restore rules when the parent or the collection is gone; permanent delete for administrators; scheduled purge job with a configurable retention period; references and cache handled correctly on delete and restore. Out: full audit history of every field change, trash for settings and users."

## Context

Today every delete is permanent. Feature 003 (FR-004) deletes media files for good, feature
010 (FR-014) deletes pages and frees their address, feature 012 (FR-006) removes the content
of a deleted page's or component's versions, and feature 015 (FR-008) deletes an item with
its draft, published data, versions and schedules. Each of those specs says that trash and
restore belong to #21. The only way back from a mistake is the operator's database backup,
which restores the whole installation to an older point in time.

The guards that earlier features put in front of a delete stay as they are. This feature
does not make more things deletable; it changes what a delete does:

- deleting a page, shared component, collection item or media file **moves it to the
  site's trash** instead of removing it;
- for visitors, the editor and every other part of the product, something in the trash
  behaves exactly as if it had been deleted;
- members can **restore** it, with its identity, content and history, for a retention
  period; if the place it came from is gone, the restore says where it will go instead;
- Administrators can **delete permanently** before the retention period ends;
- after the retention period, a background job **purges** it.

Out of scope: a history of every field change, trash for collections, menus, settings,
sites and users.

## Clarifications

### Session 2026-11-05

- Q: Now that a delete can be undone, should deleting a page that has child pages still be
  refused, or move the page and its descendants to the trash together? → **A: still
  refuse.** Feature 010's rule stays: the child pages must be moved or deleted first, and
  every trash entry is a single page. Child pages already in the trash do not count.
- Q: Can a member move a page, component or item to the trash while another member holds
  its edit lock (feature 023)? → **A: no, refused while the lock is active.** The refusal
  names who is editing and since when; the delete is allowed once the lock is released or
  stale. Members who only have it open (viewers) or hold a stale lock are told it moved to
  the trash.
- Q: How does content that was published or scheduled to publish when it was deleted come
  back on restore? → **A: unpublished, with an offer to republish.** The restore shows that
  it was live (or scheduled) when deleted, and members with the publish capability get a
  "Restore and publish" action that, in one confirmed step, restores it and publishes its
  last published version. Schedules are not brought back.
- Q: Who sets how long deleted content stays in the trash? → **A: the operator, once for
  the whole installation**, through configuration (default 30 days, 1 to 3650). There is no
  per-site setting and no admin screen for it in this feature (FR-025).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A member undoes a wrong delete (Priority: P1)

Mai, an Editor, means to delete the draft blog post "Spring sale 2025" but deletes "Spring
sale 2026", which is due to go live next week. The confirmation told her the post would be
kept in the trash for 30 days. She opens the trash, finds the post at the top, and restores
it. It is back in the "Blog posts" collection with its content, its earlier versions and
the same address name, and the item that links to it as "Related post" shows the link again
without anyone touching it. She then republishes it. The same works for a page, a shared
component and a media file.

**Why this priority**: This is the problem the issue states. Without restore, nothing else
in this feature has value.

**Independent Test**: For each content type (page, shared component, collection item,
media file), delete one after confirming, confirm it is gone from its normal list, open the
trash, restore it, and confirm that it is back with the same identity, content, versions
and address (when still free), and that everything that referred to it before the delete
refers to it again.

**Acceptance Scenarios**:

1. **Given** a page, shared component, collection item or media file that a member is
   allowed to delete under the rules of the feature that owns it,
   **When** they delete it and confirm,
   **Then** it moves to the site's trash, the confirmation and the success message say that
   it can be restored until a stated date, and the guards of the owning feature still apply
   (for example, a published page must still be unpublished first).
2. **Given** an entry in the trash,
   **When** a member allowed to restore it chooses restore,
   **Then** it comes back with its original identity, content, versions and history,
   in its original place when that place still exists (Story 4 covers when it does not).
3. **Given** a restored page, component or item,
   **When** it is back,
   **Then** it is **unpublished**: its draft is the draft it had when it was deleted, its
   earlier versions can be viewed and used as before, and it becomes public only through a
   publish.
4. **Given** a trashed item (or other content) that was published or had a scheduled
   publish when it was deleted,
   **When** a member with the publish capability restores it,
   **Then** the restore says it was live (or scheduled) when deleted and, when it has a
   version that was live before, offers "Restore and publish", which restores it and
   publishes that version in one confirmed step; its former schedules are not restored.
5. **Given** a restored media file,
   **When** it is back,
   **Then** it is served again at its original address, its image variants work again, and
   every published page or component that still refers to it shows it again from the next
   request.
6. **Given** something that referred to the deleted content by identity (a menu entry, a
   reference field, a link in a page, an image block, a page that uses a component),
   **When** the content is restored,
   **Then** that reference works again without any change to the referring content.

---

### User Story 2 - Deleted content is gone everywhere until it is restored (Priority: P1)

Once Mai's post is in the trash, the public site no longer shows it: its page answers not
found, it disappears from lists bound to the "Blog posts" collection and from the sitemap,
and the "Latest posts" menu entry that pointed at it is marked as broken in the admin. In the
admin, the post is not in the item list, cannot be chosen in a reference field and is not
counted in the collection's item count. When she restores it, all of that reverses.

**Why this priority**: A trash is only safe if content in it is truly gone for visitors and
for everyday editing. If trashed content leaked into the site, a picker or a count, members
would see deleted things come back and lose trust in delete.

**Independent Test**: Put one of each content type in the trash and check every place that
lists, counts, picks, renders, links to or serves it (public pages and lists, sitemap and
feeds, menus, media addresses, editor pickers, admin lists, counts, search). Confirm each one
treats it as deleted from the next request on every instance, then restore it and confirm
each one shows it again.

**Acceptance Scenarios**:

1. **Given** content in the trash,
   **When** visitors request it or any page that lists, embeds or links to it,
   **Then** they get exactly what the owning features define for deleted content (not found
   for its address, left out of lists and menus, image left out, link rendered as plain
   text), from the next request on every instance.
2. **Given** content in the trash,
   **When** members use the admin or the editor,
   **Then** it does not appear in normal lists, trees, pickers, search results or counts,
   and no new reference to it can be created; a write that names it is refused as if it
   had been deleted.
3. **Given** content that was moved to the trash or restored,
   **When** the change is stored,
   **Then** exactly the cached public pages that depend on it are refreshed on every
   instance, and nothing else.
4. **Given** a page, component or item whose edit lock (feature 023) is held and active by
   another member,
   **When** a member tries to move it to the trash,
   **Then** it is refused with a reason naming who is editing it and since when, and
   nothing changes.
5. **Given** a member who has the page, component or item open as a viewer, or holds a
   stale lock on it,
   **When** someone else moves it to the trash,
   **Then** the editor tells them that it was moved to the trash, by whom and when, stops
   saving, keeps their unsaved work on screen read-only for copying, and offers to open
   the trash if they may restore it.

---

### User Story 3 - Members browse and search the trash (Priority: P2)

A Designer cleaning up after a busy week opens the trash. It has a tab for each content type
(Pages, Components, Items, Media), each showing what was deleted, who deleted it, when, and
when it will be purged. They search for "pricing" and filter Items by the "Plans" collection,
select three entries and restore them in one action.

**Why this priority**: Restore (Story 1) needs a way to find the entry. A few entries can be
found by scrolling; a busy site needs search, filters and bulk restore.

**Independent Test**: Delete 200 mixed entries across all content types and members. Confirm
that each tab lists only its type, newest first; that search by title, name, address or file
name finds an entry; that filters by deleted by, deletion date and collection (or folder)
narrow the list; and that selecting several entries and restoring them restores each one or
reports per entry why it could not be restored.

**Acceptance Scenarios**:

1. **Given** a site with content in the trash,
   **When** a member opens the trash,
   **Then** they see one view per content type they may view, each listing entries newest
   first with title or name, address or file name, where it was (parent page, collection or
   folder), who deleted it, when, and the date it will be purged.
2. **Given** the trash view,
   **When** the member searches or filters,
   **Then** the list narrows by title, name, address or file name, and by deleted by,
   deletion date range and (for items) collection or (for media) folder.
3. **Given** several selected entries,
   **When** the member restores them in one action,
   **Then** each one is restored or left in the trash with a reason, and the result lists
   both groups.
4. **Given** an entry the member may view but not restore,
   **When** they view it,
   **Then** they see the entry without a restore action; the server refuses a restore from
   them either way.
5. **Given** a trashed page, component or item,
   **When** a member opens it from the trash,
   **Then** they can view its last draft read-only before deciding to restore it.

---

### User Story 4 - Restoring when the original place is gone (Priority: P2)

A Designer deleted the page "Team" (under "About"), and a day later someone deleted "About"
itself. Restoring "Team" now tells them that its parent page is in the trash and offers two
choices: restore "About" as well, or put "Team" somewhere else, with the top level of the
tree preselected. In another case, a media file's folder has since been deleted, so the file
is restored to the top level of the library and the restore says so. A blog post whose
collection has gained a new required field is restored as a draft marked as needing
attention.

**Why this priority**: Without explicit rules, restore either fails in confusing ways or puts
content back in a broken place. It matters less often than a plain restore.

**Independent Test**: Create each situation (parent page in the trash, parent page purged,
address taken by a newer page, folder deleted, collection schema changed, collection deleted,
referenced item in the trash) and confirm that the restore states where the content will go
and what will change before it happens, and that the result matches.

**Acceptance Scenarios**:

1. **Given** a trashed page whose parent page is also in the trash,
   **When** a member restores it,
   **Then** they are offered to restore the parent pages as well (when they may restore
   them) or to choose another place in the tree, and nothing is restored until they choose.
2. **Given** a trashed page whose parent page no longer exists,
   **When** a member restores it,
   **Then** they choose another place in the tree, with the top level preselected.
3. **Given** a trashed page or item whose address (slug) is now used by another page or item
   in the same place,
   **When** it is restored,
   **Then** it gets a new free address derived from the old one, and the member is told the
   old and new address before confirming.
4. **Given** a trashed media file whose folder no longer exists,
   **When** it is restored,
   **Then** it goes to the top level of the library (or a folder the member chooses), and
   the member is told.
5. **Given** a trashed item whose collection's schema changed while it was in the trash,
   **When** it is restored,
   **Then** it is restored as a draft under the current schema: values of fields that no
   longer exist are not shown, and if it now misses a required value or breaks a field
   setting it is marked as needing attention (feature 015).
6. **Given** a collection that is deleted while some of its items are in the trash,
   **When** the deletion is confirmed,
   **Then** the confirmation counts those trashed items too, and they are deleted
   permanently with the collection and leave the trash.
7. **Given** a trashed item or page that refers to content that is now in the trash or gone,
   **When** it is restored,
   **Then** the restore is not blocked; those references behave as references to deleted
   content do in the owning features (shown as missing in the admin, left out on the site).

---

### User Story 5 - An Administrator deletes permanently (Priority: P2)

A client asks for a photo to be removed for legal reasons. Deleting it moves it to the trash,
but that is not enough: the file must be gone now. The Administrator opens the media trash,
chooses "Delete permanently", confirms, and the file and its variants are removed. At the end
of a project they also empty the whole trash of a site.

**Why this priority**: Some content must be gone immediately (legal, privacy, storage). It is
needed less often than restore.

**Independent Test**: As an Administrator, permanently delete one entry of each type and
empty a site's trash; confirm everything is gone for good and media storage is freed. As a
Designer and an Editor, confirm that permanent delete and empty trash are not offered and are
refused by the server.

**Acceptance Scenarios**:

1. **Given** an entry in the trash,
   **When** an Administrator of the site permanently deletes it and confirms,
   **Then** it and everything a permanent delete removed before this feature (draft,
   versions, schedules, published data, stored bytes and variants) are removed, it leaves
   the trash, and it cannot be restored.
2. **Given** a site's trash,
   **When** an Administrator empties it and confirms by typing the number of entries shown,
   **Then** every entry in it at that moment is deleted permanently, and entries moved to
   the trash after that moment are kept.
3. **Given** a member who is not an Administrator of the site,
   **When** they try to delete permanently or empty the trash,
   **Then** the server refuses it as forbidden, whatever the admin shows.

---

### User Story 6 - The trash empties itself after the retention period (Priority: P3)

The operator of an installation sets the retention period to 60 days. Every day, entries that
have been in the trash for more than 60 days are purged without anyone doing anything. The
trash shows on each entry the date it will be purged.

**Why this priority**: Without a purge the trash grows forever, keeping storage and personal
data longer than anyone wants. It is invisible until the trash grows, so it can come last.

**Independent Test**: Set a short retention period, put entries in the trash at different
times, run the installation past the period and confirm that exactly the entries older than
the period are purged, on one instance only when several run, and that changing the period
updates the purge dates shown.

**Acceptance Scenarios**:

1. **Given** a retention period configured by the operator (30 days unless set otherwise),
   **When** an entry has been in the trash longer than that period,
   **Then** it is deleted permanently within 24 hours, as if an Administrator had done it,
   and the deletion is recorded as made by the system.
2. **Given** an invalid retention setting,
   **When** the installation starts,
   **Then** it refuses to start with a message naming the setting and the allowed range.
3. **Given** the operator shortens the retention period,
   **When** the installation runs with the new value,
   **Then** the purge dates shown in the trash use the new period, and entries now past it
   are purged on the next run.
4. **Given** several instances of the installation,
   **When** the purge runs,
   **Then** each entry is purged once, and a purge that fails part way leaves every entry
   either fully purged or still restorable, and is retried on the next run.

---

### Edge Cases

- **Deleting a page that has child pages**: still refused, as feature 010 FR-012 says,
  until the child pages have been moved or deleted (see Clarifications). Every trash entry
  is a single page.
- **Trashed child pages do not count as children**: a page whose only children are in the
  trash can be deleted; restoring those children later follows Story 4.
- **The site's only page**: feature 010 allows deleting the home page when it is the only
  page. Restoring it makes it the home page again if the site has no home page at that time.
- **A trashed component used by an old page version**: rolling a page back or publishing a
  version that uses a component in the trash is refused as for a deleted component (feature
  012), with a reason that names the component and says it can be restored from the trash.
- **Media in the trash and folders**: a folder that holds only trashed files counts as empty
  and can be deleted (feature 013 FR-002); its trashed files are restored as in Story 4.
- **Media usage**: a file used only by trashed pages, components or items shows that use as
  "used by content in the trash"; it does not block or warn on delete. Usage is recounted on
  every move to the trash, restore and purge.
- **Restoring a file used by published pages**: it changes what visitors see without a
  publish, so it requires the site's publish capability, as deleting such a file does
  (feature 013 FR-016).
- **The same entry restored or deleted by two members at once**: the first action wins; the
  second is told the entry is no longer in the trash (or no longer exists) and nothing else
  changes.
- **Restore while the collection's schema is being changed** (feature 014 FR-037): the
  restore is refused with the same reason as other item writes, and can be retried.
- **Purge of content still referred to**: a purged page, component, item or file leaves
  references that behave as references to deleted content did before this feature.
- **History after purge**: publish log entries of a purged page or component are kept,
  labelled with its last title or name, as feature 012 FR-006 already does for deletes. An
  item's history goes with the item, as feature 015 FR-008 already does.
- **A collection template page in the trash** (feature 016): while it is in the trash the
  collection has no template. On restore it becomes the template again if the collection
  still exists and has none; otherwise it comes back as a normal page, and the restore says
  so first.
- **Site export and import (feature 020)**: archives never contain trashed content. A restore
  import, which replaces the site, also empties the site's trash. A merge import that brings
  a document whose identity is in the trash adds the imported one beside it, and the trashed
  copy stays in the trash.
- **Upgrade**: content deleted before this feature was installed is gone and cannot appear in
  the trash; upgrading moves nothing to the trash.
- **Storage use**: trashed media files still take storage until purged; the media trash shows
  their total size.

## Requirements *(mandatory)*

### Functional Requirements

**Moving to the trash**

- **FR-001**: Deleting a page, shared component, collection item or media file MUST move it
  to its site's trash instead of removing it. The guards of the owning features (010
  FR-012/FR-013 and FR-028, 012 FR-004/FR-006, 013 FR-015/FR-016, 015 FR-005/FR-008) MUST
  still apply unchanged, except where this feature says otherwise.
- **FR-002**: The delete confirmation and the result MUST state that the content can be
  restored from the trash, and until which date.
- **FR-003**: Moving to the trash MUST keep the content's identity, draft, versions,
  history, stored bytes and image variants. It MUST cancel its pending schedules, and a
  collection item's published data MUST stop being public (the item is unpublished as part
  of the delete).
- **FR-004**: A page or item in the trash MUST free its address for reuse (as 010 FR-014
  does for a delete today), and a shared component in the trash MUST free its name. A media file in the trash MUST keep its address reserved, and
  that address MUST answer not found while the file is in the trash.
- **FR-005**: Each trash entry MUST record who deleted it, when, its content type, its title
  or name, its address or file name, where it was (parent page and position, collection,
  or folder), and whether it was published or had a scheduled publish when deleted.

**Trashed content is treated as deleted**

- **FR-006**: Content in the trash MUST be treated as deleted by every read of the product
  (public rendering, sitemaps and feeds, menus, bindings, admin lists and trees, pickers,
  counts, search, usage checks, exports) except the trash view and restore. This MUST hold
  without each feature having to remember to exclude it.
- **FR-007**: Every write that would create a new reference to trashed content, or change
  it, MUST be refused as for deleted content.
- **FR-008**: Moving a page, component or item to the trash MUST be refused while another
  member holds an active edit lock on it (feature 023), with a reason naming that member and
  since when. A member who has it open as a viewer or holds a stale lock when it is moved to
  the trash MUST be told so with who and when, saving MUST stop, and their unsaved work MUST
  stay on screen read-only for copying.

**Trash view**

- **FR-009**: Each site MUST have a trash with one view per content type (pages, shared
  components, collection items, media files), shown to members who may view that content
  type on the site.
- **FR-010**: Each view MUST list entries newest first with the fields in FR-005 and the date
  each entry will be purged, MUST be paged, and MUST support search by title, name, address
  or file name, and filters by deleted by, deletion date range, collection (items) and folder
  (media).
- **FR-011**: A member MUST be able to open a trashed page, component or item read-only, and
  see a trashed media file's name and who deleted it, before restoring it.
- **FR-012**: The media view MUST show the total storage size of the trashed files.

**Restoring**

- **FR-013**: A member MUST be able to restore an entry when they hold, now, the permission
  the owning feature requires to delete that content (including 013's delete-own rule for
  Editors and its publish capability rule for files used by published pages). Restore MUST
  be refused as forbidden otherwise.
- **FR-014**: Restoring MUST bring back the content with its original identity, draft,
  versions and history, so that every existing reference to it works again without change.
- **FR-015**: A restored page, component or item MUST be unpublished, and its former
  schedules MUST NOT be restored. When it was published or had a scheduled publish when
  deleted, the restore MUST say so. When it has a version that was live before, members with
  the publish capability MUST be offered "Restore and publish", which restores it and
  publishes that last live version in one confirmed step, recorded as both a restore and a
  publish. Content that was only scheduled and never live comes back unpublished. A restored media file MUST be
  served again at its original address.
- **FR-016**: Restoring MUST return content to its original place when it still exists. When
  it does not, the member MUST be told before confirming where the content will go:
  - a page whose parent is in the trash: restore the parent pages too (when the member may),
    or choose another place, top level preselected;
  - a page whose parent no longer exists: choose another place, top level preselected;
  - a media file whose folder no longer exists: top level of the library or a chosen folder.
- **FR-017**: When a restored page's or item's address is now used by another page in the
  same place or another item in the same collection, the restored one MUST get a new free
  address derived from the old one, and the member MUST be told the old and new address
  before confirming. A restored component whose name is now taken MUST get a new name
  derived from the old one, told in the same way.
- **FR-018**: A restored item MUST be validated against its collection's current schema and
  marked as needing attention when it no longer meets it (015), without blocking the restore.
- **FR-019**: Members MUST be able to restore several entries in one action; each entry MUST
  be restored or left in the trash with a reason, and the result MUST list both.
- **FR-020**: Restore and permanent delete of the same entry MUST be safe when requested at
  the same time: exactly one succeeds and the other is told why it failed.

**Permanent deletion and purge**

- **FR-021**: Administrators of the site, and only they, MUST be able to delete trash entries
  permanently (one, several, or the whole trash). Emptying the trash MUST require typing the
  number of entries shown, and MUST NOT remove entries moved to the trash after the
  confirmation was shown.
- **FR-022**: A permanent delete MUST remove everything a delete removed before this feature
  (003 FR-004, 010 FR-014, 012 FR-006, 015 FR-008), including stored bytes and variants.
  Publish log entries of a page or component MUST be kept, labelled with its last title or
  name (012 FR-006). An item's history goes with the item (015 FR-008).
- **FR-023**: Deleting a collection MUST also delete its trashed items permanently, and its
  confirmation MUST count them (extending 014 FR-004).
- **FR-024**: A background job MUST purge, at least once per day, every entry that has been
  in the trash longer than the retention period, as a permanent delete made by the system.
  It MUST purge each entry once across instances, MUST leave every entry either fully purged
  or still restorable if it fails, and MUST retry on its next run.
- **FR-025**: The retention period MUST be configured by the operator for the whole
  installation, in days, with a default of 30 and an allowed range of 1 to 3650. An invalid
  value MUST stop the installation from starting with a message naming the setting and
  range. A change MUST apply to all entries, including those already in the trash.

**References, cache and records**

- **FR-026**: Moving to the trash, restoring and permanent deletion MUST refresh exactly the
  cached public pages that depend on the content, on every instance, from the next request.
- **FR-027**: Media usage (013) and the targets shown for menu entries (024) MUST reflect
  moving to the trash, restoring and purging. Use by trashed content MUST be shown separately
  and MUST NOT block or warn on a delete.
- **FR-028**: Every move to the trash, restore, permanent delete and purge MUST be recorded
  with who (or "system"), when, the content and its site: in the publish log for pages and
  components, in the item's history for an item's move to the trash and restore, and in the
  application log (ids only) for media files and for an item's permanent delete or purge. It
  MUST be announced to the rest of the product as deletes are today
  (010, 012, 015). Records MUST NOT contain content bodies or field values.
- **FR-029**: All trash operations MUST be scoped to one site; no member may see, restore or
  delete permanently another site's trash entries, and a request naming another site's entry
  MUST be answered as if it did not exist.
- **FR-030**: Site exports MUST NOT contain trashed content. A restore import MUST empty the
  site's trash; a merge import of a document whose identity is in the trash MUST add the
  imported one beside it and leave the trashed copy in the trash, with no collision and no
  loss (extending 020).
- **FR-031**: The trash journeys (browse, search, view, restore, permanent delete, empty) MUST
  be completable with the keyboard alone and MUST show usable empty, loading and error states.

### Key Entities

- **Trash entry**: a page, shared component, collection item or media file that has been
  deleted but not yet purged. Attributes: content type, identity, site, title or name,
  address or file name, original place (parent page and position, collection, or folder),
  deleted by, deleted at, whether it was published or scheduled when deleted, purge date
  (derived from the retention period). Not a copy: the
  content itself, marked as deleted.
- **Retention period**: the installation-wide number of days an entry stays in the trash
  before it is purged.
- **Restore plan**: what a restore will do, shown before confirming: target place, new
  address if the old one is taken, parents restored along with it, schema issues.
- **Trash event**: a record of a move to the trash, restore, permanent delete or purge,
  with actor (member or system), time, content and site.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across the automated test suite, 100% of pages, components, items and media
  files moved to the trash and then restored come back with identical identity, draft,
  versions and (when free) address, and every reference to them works again.
- **SC-002**: A member who deleted something by mistake can find and restore it in under
  1 minute from opening the admin, in a trash holding 1,000 entries.
- **SC-003**: Searching or filtering a trash of 10,000 entries shows results in under
  1 second in 95% of attempts on the reference setup.
- **SC-004**: In a test that checks every public and admin read path, 0 trashed entries
  appear outside the trash view, from the first request after the delete, on every instance.
- **SC-005**: After a move to the trash or a restore, 100% of the affected cached public
  pages reflect it on every instance on their next request, and unaffected cached pages are
  not refreshed.
- **SC-006**: The purge removes 100% of entries older than the retention period within
  24 hours of their purge date, and 0 entries younger than it.
- **SC-007**: Across the automated test suite, 0 members without the Administrator role on
  the site succeed in deleting permanently or emptying the trash, and 0 members succeed in
  seeing or restoring another site's entries.
- **SC-008**: In every "original place is gone" situation in Story 4, the member is told
  where the content will go before confirming, and 0 restores fail without a reason.
- **SC-009**: In a moderated test, at least 9 of 10 members who have never seen the trash
  restore a deleted item and a deleted page without help.

## Assumptions

- **Which deletes are allowed does not change**: the guards of features 010, 012, 013 and 015
  stay (home page, published page, component in use, item referred to, file in use warning
  and publish capability), including refusing to delete a page that has child pages
  (clarified 2026-11-05). Only what a delete does changes.
- **Restore permission mirrors delete permission**: whoever may delete something may restore
  it; there is no separate restore capability. Permanent delete is for Administrators only,
  as the issue states.
- **Restored content comes back unpublished**, because Principle VII makes publish an
  explicit action; "Restore and publish" (clarified 2026-11-05) is that explicit action in
  one step. Media files have no publish state; restoring one that published pages use
  is guarded by the publish capability instead.
- **Addresses are freed on delete** (as today), so members can reuse an address right away;
  a clash is resolved on restore with a new address. Media addresses are not freed because
  they are identities, not chosen names.
- **Retention is one installation-wide setting** chosen by the operator (clarified
  2026-11-05), default 30 days, a common default for trash in content tools. A per-site
  setting can come later.
- **The purge runs every hour** on the built-in background job runner, so an entry goes
  within about an hour of its purge date and always within the 24 hours FR-024 allows.
- **Collections, menus, folders, settings, sites and users are not trashed**: deleting them
  stays permanent. Deleting a collection removes its trashed items too.
- Trashed content counts toward storage use until purged.
- Features 003, 010, 012, 013, 014, 015, 016, 020, 023 and 024 are in place; this feature
  extends their delete behaviour and their histories.

## Out of Scope

- A history of every field change, or restoring an older field value (versions from 012 and
  015 remain the way to go back in time).
- Trash for collections, menus, media folders, design tokens, settings, sites and users.
- A per-site or per-content-type retention period.
- Restoring content from before this feature was installed.
- Undo for actions other than delete (moves, renames, schema changes).

## Dependencies

- Feature 010 (pages, page tree, shared components): delete guards, addresses, tree position.
- Feature 012 (publishing and rendering): versions, history of deleted pages, cache
  invalidation through the dependency graph.
- Features 003 and 013 (media): storage, variants, usage, folders, delete permissions.
- Features 014, 015 and 016 (collections, items, bindings): schema validation, references,
  needs-attention marking, collection deletion.
- Feature 020 (site export and import) and feature 023 (concurrent editing) for the
  interactions above; feature 024 (navigation) for menu entry targets.
