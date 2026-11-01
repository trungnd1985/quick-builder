# Feature Specification: Concurrent Editing Safety

**Feature Branch**: `023-concurrency`

**Created**: 2026-10-25

**Status**: Draft

**Issue**: #23

**Input**: User description: "#23 Concurrent editing safety. Problem / goal: Several team members will open the same page in the editor, and with autosave in place the last save silently wins. A user must be told when someone else is editing the same content, and must never overwrite another person's changes without knowing. In: optimistic concurrency on draft saves (version token, conflict detected on save), a soft lock showing who else has the page or item open and since when, takeover of a stale lock, a clear conflict dialog with the option to reload or save a copy, locks released on navigation and expiring automatically. Out: real-time collaborative editing (multiple cursors, CRDT/OT merging)."

## Context

Three earlier features already refuse a save that is out of date. Feature 010 (FR-010,
FR-021) refuses a change to a page or shared component when it has changed since the
member loaded it. Feature 011 (FR-028) stops autosave when that happens, keeps the member's
version on screen read-only so blocks can be copied, and offers to reload. Feature 015
refuses the second of two saves to the same collection item and offers the latest draft.
So nothing is silently overwritten today.

What is missing is everything before and after that refusal. Two members can work on the
same page for half an hour without knowing about each other, and the second finds out only
when their work is refused. The refusal itself offers reload or copy-by-hand; there is no
way to keep a whole version in one step. This feature closes those gaps:

- every draft write to a page, shared component or collection item is checked against the
  version the member loaded, wherever the write comes from;
- a **soft lock**, taken by the first member to change a document, tells everyone who
  opens it that someone else is editing it, who and since when, and tells the person
  editing that someone else has opened it;
- a lock left behind by an idle member can be taken over, and every lock is released when
  its holder leaves and expires on its own when they disappear;
- a conflict dialog explains what happened and lets the member reload or keep their
  version as a copy.

The lock is "soft": it informs and guides, but the version check on save remains what
guarantees that nothing is overwritten. Real-time co-editing (shared cursors, merging two
members' changes) is out of scope.

## Clarifications

### Session 2026-10-25

- Q: What may a member do with a document whose lock is active and held by someone else?
  → **A: view it read-only until it is free or the lock is stale.** They can view and
  copy content but not change it, and cannot take over an active lock; takeover is
  possible only once the lock is stale.
- Q: What does "save my version as a copy" create? → **A: a new page, shared component or
  item next to the original**, unpublished, offered only to members who may create a
  document of that kind. Editors, who cannot create pages (feature 010), are not offered
  it on pages and keep the read-only view to copy blocks from.
- Q: When does a member become the lock holder: on opening a document, or on their first
  change to it? → **A: on their first change.** Opening a document only makes the member
  a viewer, shown to others as "also viewing"; viewers block nobody. The first member to
  make a change takes the lock, and every other open editor switches to read-only.
- Q: Can a member who is editing in one tab or browser move editing to a second one? →
  **A: yes, with "Edit here instead".** Their second session offers it; the lock moves
  there and the first session switches to read-only with a notice.
- Q: Should a waiting member be able to send the holder an "Ask to edit" request that the
  holder can accept to hand the document over? → **A: no.** Waiting members see who is
  editing and since when and wait for the lock to be released, to expire or to become
  stale; there is no request or hand-over inside the admin.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - No draft save ever overwrites someone else's change (Priority: P1)

Anna and Ben both have the "Pricing" page open. Ben's editor autosaves a change. A few
seconds later Anna's editor tries to autosave hers. Her save is refused because the draft
has changed since she loaded it, and a dialog tells her that Ben saved the page at 10:42.
She can reload Ben's version, keep viewing her own to copy blocks from it, or save her
version as a copy. Ben's change is untouched. The same happens on a collection item form,
and when Anna restores unsaved changes from a crashed tab.

**Why this priority**: This is the issue's hard requirement: nobody overwrites another
person's changes without knowing. Locks and presence only make conflicts rarer; the
version check on every write is what makes them harmless.

**Independent Test**: For each document kind (page, shared component, collection item),
open it in two sessions, save in one, then save in the other through every write path
(autosave, manual save, restore after a crash, copying an older version into the draft).
Confirm that every second write is refused, nothing it carried is stored, the first
write's content is intact, and the conflict dialog names who saved and when.

**Acceptance Scenarios**:

1. **Given** a member who loaded a draft at a given version,
   **When** any write to that draft arrives after another write has stored a newer version,
   **Then** it is refused as a conflict, nothing in it is stored, and the newer version is
   unchanged.
2. **Given** a refused write,
   **When** the conflict dialog opens,
   **Then** it says that someone else changed the document, names that member (or says it
   was a system change, such as a collection schema update) and when, states that nothing
   was overwritten, and offers: reload the latest version, keep viewing my version, and,
   to members who may create a document of that kind, save my version as a copy.
3. **Given** the conflict dialog on a page or shared component,
   **When** the member chooses to keep viewing their version,
   **Then** the editor behaves as feature 011 FR-028 describes: read-only, blocks can be
   copied, and the copied blocks remain pasteable after a later reload.
4. **Given** the conflict dialog on a collection item,
   **When** it opens,
   **Then** it also lists which fields the other change altered, so the member can see
   whether their own edits touch the same fields.
5. **Given** the member chooses reload,
   **When** they confirm the warning that their unsaved changes will be discarded unless
   copied,
   **Then** the latest version opens for editing with autosave resumed.
6. **Given** two writes from the same session in quick succession (for example a slow
   autosave followed by the next one),
   **When** both arrive,
   **Then** the member's own earlier save is never reported as someone else's conflict.

---

### User Story 2 - Members see who else is editing before they start (Priority: P1)

Ben opens the "Pricing" page in the editor. Before he changes anything, a banner says
"Anna Tran is editing this page — since 10:15". In the page tree, the page shows a small
marker with the same information. Meanwhile Anna's editor shows "Ben Hale has also opened
this page". When Anna closes the editor, Ben is told that the page is now free and can
start editing without reloading.

**Why this priority**: Telling people before they collide is the main way this feature
saves work. Without it, the version check (Story 1) still protects data but members keep
losing time to refused saves.

**Independent Test**: Open a page in sessions A and B and confirm each sees the other as
viewing and both can edit. Make a change in A and confirm B switches to read-only. Then
open it in session C. Confirm that C sees A's name and start time on opening, that A is
told about C within the stated time, that the page tree and item list show the marker,
and that when A leaves, B and C are told and can edit.
Repeat for a shared component and a collection item.

**Acceptance Scenarios**:

1. **Given** a page, shared component or collection item that nobody is editing,
   **When** a member who may change it opens it,
   **Then** they are a **viewer**: they can edit, nobody is blocked, and other members who
   have it open see them as "also viewing"; when they make their first change, they
   become the **lock holder** for it.
2. **Given** two or more viewers of a free document,
   **When** one of them makes the first change,
   **Then** they become the holder, and every other viewer's editor switches to read-only
   at once with a notice naming the holder.
3. **Given** a document with a lock holder,
   **When** another member opens it,
   **Then** they see, before they can make any change, the holder's display name and since
   when they have been editing, and the document opens read-only for them until it is
   free or its lock becomes stale (Story 3); they can view and copy content but cannot
   take over an active lock.
4. **Given** a lock holder,
   **When** another member opens the same document, or later leaves it,
   **Then** the holder is told who opened it, and later that they left.
5. **Given** the page tree, the shared component list or a collection's item list,
   **When** a member views it,
   **Then** each document with a current lock holder is marked, showing the holder's name
   and since when.
6. **Given** a member who is waiting on a held document,
   **When** the holder leaves or their lock expires,
   **Then** the waiting member is told the document is free and can start editing it,
   after loading the latest version if it changed, without reloading the screen.
7. **Given** a member with a role on site A only,
   **When** documents of site B are locked,
   **Then** they see nothing about site B's locks or the members holding them.
8. **Given** the same member opening the same document in a second tab or browser,
   **When** the second one opens,
   **Then** when the other session holds the lock, the second one is read-only, says the
   document is being edited in another of their sessions, and offers "Edit here instead";
   choosing it moves the lock to this session and switches the other one to read-only
   with a notice. Unsaved changes in the other session are not saved over this one; they
   are handled as after a takeover (Story 3 scenario 3).

---

### User Story 3 - A lock left by an idle or vanished member does not block work (Priority: P2)

Anna opened the "Pricing" page before lunch and left her laptop with the editor open. An
hour later Ben needs to fix a price. The banner says Anna has been editing since 11:50 and
has been inactive for 62 minutes, and offers "Take over". Ben confirms; he becomes the
holder and starts editing. When Anna returns, her editor says Ben took over at 12:52 and is
now read-only; it offers to reload. On another day, Anna's laptop crashes with the page
open: within two minutes the page is free without anyone taking over.

**Why this priority**: Locks that never go away would be worse than no locks. Takeover and
expiry make the soft lock safe to rely on, but they matter only once Story 2 exists.

**Independent Test**: Hold a lock in session A and leave it idle past the stale threshold;
in session B take it over and confirm A switches to read-only with a notice and that the
takeover is recorded. Close A's tab and confirm the lock is released at once; kill A's
browser without closing and confirm the lock expires within the stated time. Navigate away
in-app and sign out and confirm both release the lock.

**Acceptance Scenarios**:

1. **Given** a lock holder who has made no change and no other editing action for the
   stale period,
   **When** another member views the document,
   **Then** the lock is shown as **stale**, with how long the holder has been inactive, and
   the member is offered to take it over.
2. **Given** a stale lock,
   **When** a member takes it over and confirms,
   **Then** they become the holder, they load the latest saved version, the takeover is
   recorded with both members and the time, and the former holder's editor switches to
   read-only with a notice naming who took over and when.
3. **Given** a former holder after a takeover,
   **When** they still had unsaved changes,
   **Then** those changes are not saved over the new holder's work; the former holder can
   view and copy them and save them as a copy, as in the conflict dialog.
4. **Given** a lock holder,
   **When** they leave the document (navigate elsewhere in the admin, close the tab or sign
   out),
   **Then** the lock is released at once; if their sign-in session is ended or revoked
   another way, the lock lapses within the expiry period.
5. **Given** a lock holder whose editor stops responding (crash, lost network, sleeping
   device),
   **When** the expiry period passes without a sign of life,
   **Then** the lock expires on its own and the document is free.
6. **Given** a holder whose network was lost and comes back after their lock expired,
   **When** their editor reconnects,
   **Then** it takes the lock again if it is still free and their version is still the
   latest; otherwise it tells them who holds it now and treats their next save under the
   version check of Story 1.
7. **Given** a member whose permission to change the document is removed, or the document
   is deleted, while they hold its lock,
   **When** that happens,
   **Then** the lock is released and their editor tells them why it stopped.

---

### User Story 4 - A member keeps their version as a copy instead of losing it (Priority: P2)

Anna's save is refused because Ben changed the page. Her version contains a rewritten
section she does not want to lose, and copying blocks one by one would take a while. In the
conflict dialog she chooses "Save my version as a copy". A new page "Pricing (copy)"
holding her whole version appears directly after "Pricing" in the page tree, unpublished,
she is given a link to it, and the editor reloads Ben's version. Later she opens the copy,
takes the section she needs and pastes it into the page, then deletes the copy.

**Why this priority**: Reload and copy-by-hand already prevent data loss (011 FR-028).
Saving a whole copy removes the remaining risk of losing a long session of work and is
what the issue asks for, but it builds on Stories 1 and 3.

**Independent Test**: As a Designer, produce a conflict on a page and a shared component,
and as an Editor on a collection item; choose "save a copy" each time. Confirm a new
document of the same kind holds exactly the member's version next to the original, the
original holds the other member's version, the copy is unpublished and invisible to
visitors, and the member can open it and copy content from it into the original. As an
Editor, produce a conflict on a page and confirm "save a copy" is not offered and the
read-only view still lets them copy blocks.

**Acceptance Scenarios**:

1. **Given** the conflict dialog or the read-only view after a takeover, and a member who
   may create a document of that kind,
   **When** the member chooses to save their version as a copy,
   **Then** a new page, shared component or item holding their complete version is
   created next to the original as FR-020 describes, the original is unchanged, and the
   member is given a link to the copy.
2. **Given** a copy,
   **When** any member who may see it opens it,
   **Then** it shows who made it, when, and from which document and version.
3. **Given** a copy,
   **When** the original or any other document is published,
   **Then** the copy stays unpublished and visitors never see it, until a member who may
   publish it publishes it on purpose like any other document.
4. **Given** a member who may not create a document of that kind (an Editor on a page),
   **When** the conflict dialog or the read-only view after a takeover opens,
   **Then** "save my version as a copy" is not offered, and the dialog says they can copy
   blocks from their version or ask a Designer.
5. **Given** a member's version of a collection item that no longer fits the collection's
   current fields (for example a field was removed in the meantime),
   **When** they save it as a copy,
   **Then** the copy is still stored with every value that can be kept, and the member is
   told which values could not be kept.

---

### Edge Cases

- **Two members making their first change to a free document at the same moment**:
  exactly one becomes the holder; the other's change is not applied and they see the
  first as holder (FR-009a).
- **A viewer who never changes anything**: holds no lock and blocks nobody, however long
  the document stays open; they still appear as "also viewing" until they leave or their
  editor stops giving a sign of life (FR-016).
- **System changes to a draft**: a collection schema change that rewrites item drafts
  (014), an upgrade step that stores a document in a newer schema version (019) or a
  deleted design token (011) counts as a newer version only when it changes the content
  the member sees; when it does, the conflict dialog names it as a system change rather
  than a member.
- **Publishing while someone holds the lock**: allowed; the member publishing is warned
  that the holder may have unsaved changes that will not be included. Publishing does not
  change the draft, so the holder's next save is not refused because of it.
- **Deleting, moving or renaming a locked page**: deleting warns that someone is editing
  it and then ends the holder's session as in Story 3 scenario 7. Moving or renaming a page
  in the tree changes its settings, not its content draft; it does not conflict with the
  holder's content saves.
- **Rolling back or copying a published version into a locked draft** (015 FR-022): goes
  through the same version check; if the member is not the holder they are warned first,
  and the holder's next save is refused as a conflict naming the rollback.
- **Restoring unsaved changes after a crash** (011 FR-030): offered for editing only if
  the saved draft has not changed and the member can take the lock; otherwise they open
  read-only with the option to save them as a copy where FR-020a allows.
- **Clock differences**: "since" and "inactive for" times come from the installation, not
  the member's device, so they are correct for every viewer.
- **Several app instances behind a load balancer**: every member sees the same holder for
  a document, whichever instance serves them.
- **Members without permission to change a document** (for example an Editor viewing a
  shared component): they open it read-only, never take a lock, and still see who is
  editing it.
- **A holder who is only reading** (selecting blocks, scrolling, previewing widths): this
  counts as presence but not as activity for the stale period; only changes and editing
  actions keep a lock from becoming stale.

## Requirements *(mandatory)*

### Functional Requirements

**Version check on every draft write**

- **FR-001**: Every write that changes the draft of a page, shared component or collection
  item MUST state the version of the draft it was based on and MUST be refused as a
  conflict when the stored draft is newer. This covers autosave, manual save, restoring
  unsaved changes, copying a published version into the draft and any other draft write
  added later.
- **FR-002**: A refused write MUST store nothing it carried and MUST leave the newer draft
  unchanged.
- **FR-003**: A conflict refusal MUST report who made the newer version (member display
  name, or "system change" with its kind) and when, so the editor and item form can show
  it.
- **FR-004**: A member's own consecutive writes from one session MUST NOT be reported to
  them as a conflict with someone else.
- **FR-005**: Draft writes that are refused for reasons other than a conflict (forbidden,
  not found, invalid, schema change in progress) keep the behaviour defined in features
  011 and 015.

**Conflict dialog**

- **FR-006**: When a write is refused as a conflict, the editor or item form MUST stop
  autosaving that document and show a conflict dialog that states that nothing was
  overwritten, names who changed the document and when, and offers: reload the latest
  version, keep viewing my version, and save my version as a copy (where FR-020a allows).
  It MUST NOT offer to overwrite the newer version.
- **FR-007**: For a collection item, the conflict dialog MUST list the fields whose values
  differ between the version the member loaded and the newer version.
- **FR-008**: Reloading MUST warn that unsaved changes not copied or saved as a copy will
  be discarded, then open the latest version for editing and resume autosave. Content
  copied to the clipboard from the member's version MUST remain pasteable after the reload.

**Soft lock and presence**

- **FR-009**: Opening a page, shared component or collection item MUST make a member a
  viewer of it, which blocks nobody. A member who may change the document MUST become its
  lock holder when they make their first change while nobody holds the lock; no change is
  saved until they hold it, and if the lock goes to someone else the change is reverted on
  their screen. When someone else holds the lock, the member MUST see,
  before they can change anything, the holder's display name and since when they have
  held it, and the document MUST be read-only for them: they can view it and copy content
  from it, but not change it, until the lock is released, expires (they are then told,
  FR-011, and can start editing) or becomes stale (they may then take it over, FR-017).
  An active lock MUST NOT be taken over by another member; the holder's own other
  sessions may move it (FR-014).
- **FR-009a**: When a viewer becomes the holder, every other open editor of that document
  MUST switch to read-only at once with a notice naming the holder. A change a viewer
  attempted at the same moment that did not win the lock MUST NOT be saved and MUST be
  reverted on their screen, and they MUST be told who is editing.
- **FR-010**: At most one session MUST hold a document's lock at any time, including when
  members open it at the same moment and when the installation runs several instances.
- **FR-011**: Every member with a document open MUST see who else has it open, as holder
  or viewer, and be told when someone opens or leaves it. Members viewing a held document
  MUST be told when it becomes free.
- **FR-012**: The page tree, the shared component list and each collection's item list
  MUST mark documents that currently have a lock holder, with the holder's name and since
  when.
- **FR-013**: Lock and presence information MUST be visible only to members who may see the
  document's site, and MUST show only the holder's display name, never their email
  address or other personal details.
- **FR-014**: When a member opens a document whose lock is held by another of their own
  sessions, it MUST be read-only with a message saying so and MUST offer "Edit here
  instead", which moves the lock to the new session at once, whether the lock is active
  or stale. The former session MUST switch to read-only with a notice, and its unsaved
  changes MUST be handled as after a takeover (FR-018). Moving one's own lock is not
  recorded as a takeover.

**Release, expiry and takeover**

- **FR-015**: A lock MUST be released at once when its holder leaves the document by
  navigating elsewhere in the admin, closing the tab or signing out. When their sign-in
  session ends or is revoked by other means, the lock MUST lapse within the expiry period
  of FR-016 (plan research CE-13).
- **FR-016**: A lock MUST expire on its own when the holder's editor has given no sign of
  life for 2 minutes (crash, lost network, sleeping device). A viewer whose editor gives
  no sign of life for 2 minutes MUST stop being shown as viewing.
- **FR-017**: A lock MUST become stale when its holder has made no change or other editing
  action for 30 minutes. A stale lock MUST be shown as stale with the holder's inactive
  time, and any member who may change the document MUST be able to take it over after
  confirming.
- **FR-018**: A takeover MUST make the member the holder, load the latest saved version
  for them, switch the former holder's editor to read-only with a notice naming who took
  over and when, and be recorded with both members, the document and the time. The former
  holder MUST be able to save their unsaved changes as a copy (FR-020) but never into the
  document.
- **FR-019**: When a holder loses permission to change the document, or the document is
  deleted, the lock MUST be released and the holder told why their editing stopped. When
  a holder reconnects after their lock expired, they MUST get it back only if it is still
  free and their version is still the latest.

**Saving a copy**

- **FR-020**: Choosing "save my version as a copy" MUST create a new document of the same
  kind holding the member's complete version, without changing the original:
  - a page: a new page directly after the original under the same parent, titled
    "<title> (copy)", with an address name made unique as feature 010 requires;
  - a shared component: a new component named "<name> (copy)", used by no page;
  - a collection item: a new item in the same collection, with a slug made unique as
    feature 015 requires.
  The copy MUST be unpublished, and the member MUST be given a link to it.
- **FR-020a**: "Save my version as a copy" MUST be offered only to members who may create
  a document of that kind on that site (feature 010 and 015 permissions), and refused
  server-side for anyone else. Members without that permission (Editors on pages) keep
  the read-only view of their version from which they can copy content.
- **FR-021**: A copy MUST record who made it, when, and from which document and version,
  and show it when opened. After creation it is an ordinary document: it is published
  only when a member publishes it on purpose, and it is renamed, moved, exported and
  deleted like any other.
- **FR-022**: A member's version of a collection item that no longer fits the collection's
  current fields MUST still be saveable as a copy, keeping every value that can be kept and
  telling the member which could not. A page or component copy MUST pass the page schema
  rules like any draft. If it does not, the copy is refused with the reason, and the
  member keeps the read-only view of their version to copy blocks from.

**Operations, security and quality**

- **FR-023**: Locks, takeovers and copies MUST be scoped to the site of their document and
  enforced server-side; the admin hiding a button is not authorisation (Principle IX).
- **FR-024**: Takeovers and conflict refusals MUST be logged with document, site, members
  and time, without any content of the document (Principle IX).
- **FR-025**: Locks MUST work correctly with several app instances behind a load balancer
  once the operator has configured the shared providers, and on a single instance without
  any extra configuration (Principle IV).
- **FR-026**: Losing all locks (for example after a restart) MUST NOT put any draft at
  risk: the version check alone MUST still refuse every out-of-date write.
- **FR-027**: All new messages, banners, markers and dialogs MUST meet the admin
  accessibility baseline (`docs/admin-accessibility-baseline.md`), including announcing
  lock changes and conflicts to screen readers and full keyboard operation.

### Key Entities *(include if feature involves data)*

- **Draft version**: an identifier of the state of a page's, shared component's or
  collection item's draft that every write states and that changes on every stored change;
  carries who made the change and when.
- **Editing lock**: one per document being edited, taken on the holder's first change;
  records the site, the document, the holder (member and session), when it was taken,
  the last sign of life and the last editing activity. Its state is active, stale,
  released or expired. It is not a lasting record and is not exported.
- **Viewer presence**: a member session that has a document open without holding its
  lock, with since when and its last sign of life. Not a lasting record.
- **Takeover record**: who took over a lock from whom, for which document and when; kept
  with the site's audit information.
- **Copy origin**: on a page, shared component or item created by "save my version as a
  copy", who made it, when, and the document and version it came from. The copy itself is
  an ordinary, unpublished document of its kind.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In an automated test running 1,000 pairs of overlapping writes to the same
  pages, components and items, zero writes overwrite a newer version and every refused
  write is reported as a conflict naming the other change.
- **SC-002**: A member opening a document someone else is editing sees the holder's name
  and since when before they can make any change, in 100% of attempts.
- **SC-003**: A lock holder is told that another member opened or left their document
  within 15 seconds, and a waiting member is told the document is free within 15 seconds
  of it becoming free.
- **SC-004**: Leaving a document in the admin, closing the tab or signing out frees it for
  others within 15 seconds; a crashed or disconnected holder's lock expires within 2
  minutes and 15 seconds.
- **SC-005**: In a usability check with at least five team members, every participant who
  meets a conflict or a lock correctly says who else was editing and that nothing was
  overwritten, and completes reload, take over or save a copy without help.
- **SC-006**: In every conflict and takeover, the member's own version stays recoverable
  (viewable and copyable, and saveable as a copy where the member may create one) until
  they choose to discard it: zero cases of lost work in the conflict test suite.
- **SC-007**: Showing locks adds no noticeable delay: the page tree, item lists and editor
  open within the times they meet today on the reference setup.

## Assumptions

- Documents covered are the drafts of pages, shared components and collection items, the
  ones edited in the editor (011) and the item form (015). Site settings, design tokens,
  collection schemas and menus keep their existing out-of-date refusal without locks; they
  can join later.
- The existing out-of-date refusal in 010, 011 and 015 is the version check this feature
  extends; FR-001 makes it cover every draft write path and adds who and when to the
  refusal.
- Lock expiry (2 minutes without a sign of life) and the stale period (30 minutes without
  editing activity) are fixed defaults in this feature; the plan may make them operator
  settings with these defaults.
- "Display name" is the member name already shown in the admin (002). No avatar or
  colour per member is added.
- Takeover records join the existing audit information for the site; no new audit screen
  is added.
- Notifications about presence appear only while the member has the admin open; there is
  no email or push notification.
- There is no way to ask the holder for a document or for the holder to hand it to a
  named member (clarified 2026-10-25); a holder who wants to let someone else edit simply
  leaves the document.
- Real-time co-editing, shared cursors and merging two versions automatically are out of
  scope, as the issue states.
- Depends on #1 (foundation: outbox, multi-instance cache) and #10 (pages, components,
  drafts), and builds on #11 and #15, all merged.
