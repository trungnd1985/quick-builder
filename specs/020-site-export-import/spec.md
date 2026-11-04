# Feature Specification: Site Export and Import

**Feature Branch**: `020-site-export-import`

**Created**: 2026-11-01

**Status**: Draft

**Issue**: #20

**Input**: User description: "#20 Site export and import. Problem / goal: Operators need backups and a way to move a site between installations, and agencies want to reuse a site as a starting point. A user can export a site to a single archive and import it into another installation. In: export of site settings, pages, shared components, collections, items and a media manifest with files, import with conflict handling (replace, skip, rename), validation and schema-version checks on import, progress reporting via the background worker, CLI command for scripted backups. Out: continuous sync between installations, partial/incremental export."

## Context

Constitution Principle III promises "built-in export/import of a site (content, schemas,
media manifest) so operators can back up, migrate between servers and recover". Until now
every feature has only made sure its stored data is enough for it and listed what an export
must carry:

- **003 FR-033**: media file details are enough for a media manifest.
- **014, 015**: collection schemas and items, keyed by stable field IDs.
- **017 FR-040**: page and template metadata, site search settings and redirects.
- **018 FR-040**: forms travel inside the pages and components that hold them; submissions
  are **not** exported.
- **023**: copies are ordinary documents and export with their copy origin; editing locks,
  viewer presence and takeover records are not exported.
- **024 FR-036**: menus with their versions and schedules, with entry targets mapped to the
  imported pages and items.

This feature builds the export and the import. Three uses drive it:

- **Backup and recovery**: an operator exports a site on a schedule from the command line
  and can bring it back after a mistake or a lost server.
- **Moving a site**: an operator exports a site from one installation and imports it into
  another, for example when changing servers.
- **Starting point**: an agency imports a finished site as a new site on the same or another
  installation and adapts it for a new customer.

Export always takes the **whole site**; partial or incremental exports and continuous sync
between installations are out of scope.

An archive can be imported in three ways:

- **as a new site**: a new, independent site is created from the archive;
- **merge into an existing site**: the archive's content is added to a site that already has
  content, and conflicts are resolved by one policy (replace, skip or rename); nothing is
  removed;
- **restore**: only into the site the archive was exported from, making that site match the
  archive exactly, including removing content the archive does not hold.

## Clarifications

### Session 2026-11-01

- Q: When an operator restores a backup into the site it came from, should the import be
  able to make the site match the archive exactly, including removing content created after
  the backup? → **A: yes, through a separate restore mode**, offered only into the site the
  archive was exported from, after a preview listing everything that will be removed and a
  typed confirmation. Merge imports into an existing site still never delete.
- Q: When an agency imports a site as a starting point for a new customer, should they be
  able to bring in only the content, with nothing published and no version history? →
  **A: yes, a "start fresh" option when importing as a new site**: each document's latest
  draft only, nothing published, no history, schedules or copy origins. The full import
  stays the default, and the export is unchanged.
- Q: When an archive is merged back into the same site it was exported from, should
  documents be matched to their originals by identity, or only by address and name? →
  **A: by identity first when the archive comes from this same site, then by address or
  name**; archives from any other site or installation match by address or name only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An administrator exports a site to one archive (Priority: P1)

Linh, an Administrator, opens the "Acme" site's settings and chooses "Export site". The
export runs in the background: a progress panel shows the current step (pages, components,
collections, items, menus, media files) and a percentage, and Linh can leave the screen and
come back. When it finishes, Linh downloads a single archive file and sees a summary: 84
pages, 12 shared components, 3 collections with 1,240 items, 2 menus, 214 redirects and 610
media files (1.8 GB). One media file is listed as missing because its bytes were no longer
in storage.

**Why this priority**: Without a complete, trustworthy archive there is nothing to import,
back up or reuse. Export alone already gives operators an off-server copy of a site.

**Independent Test**: Build a site that uses every kind of content (settings, design tokens,
pages with every block type including forms and navigation, shared components, collection
templates, collections with every field type, items with references, menus, redirects,
search settings, media in folders, copies made by 023, drafts that differ from what is live,
version history and pending schedules). Export it and confirm the archive contains every one
of them, the summary counts match the site, and a missing media file is reported rather
than stopping the export.

**Acceptance Scenarios**:

1. **Given** an Administrator on a site,
   **When** they start an export,
   **Then** it runs in the background, they see its step and percentage, and they can leave
   the screen and return to the same progress.
2. **Given** a finished export,
   **When** the Administrator opens it,
   **Then** they can download one archive file and see a summary with the count of each
   kind of content, the total size, and any problems found.
3. **Given** a site with drafts that differ from what is live, older versions and pending
   publish schedules,
   **When** it is exported,
   **Then** the archive carries each document's draft, its live version, its retained
   versions and its pending schedules.
4. **Given** a media file whose bytes are missing from storage,
   **When** the site is exported,
   **Then** the export completes, the file stays in the manifest marked as missing, and the
   summary names it.
5. **Given** a member who is not an Administrator,
   **When** they try to start an export or download an archive,
   **Then** it is refused.
6. **Given** members editing the site while an export runs,
   **When** the export finishes,
   **Then** every document in the archive is whole (each holds either the state before an
   edit or after it, never part of one), and editing was never blocked.

---

### User Story 2 - An administrator imports an archive as a new site (Priority: P1)

Minh runs a second installation for a new customer. They create nothing by hand: they choose
"Import site", upload the archive Linh exported, and the installation checks it first. The
check reports that the archive is complete and readable, comes from an older release whose
content will be upgraded, and that one host name, `www.acme.example`, is already used by
another site here and will not be added. Minh gives the new site a name and confirms. The
import runs in the background with progress; when it finishes, the new site has every page,
component, collection, item, menu, redirect and media file from the archive, what was live
is live, drafts are drafts, and every link, image, reference and menu entry points to the
imported copies.

**Why this priority**: This completes the two main promises of the issue: moving a site to
another installation and reusing a site as a starting point. A backup is only useful if it
can be restored.

**Independent Test**: Export the site from Story 1, import it as a new site into an empty
installation and into the same installation, then compare the two sites: content, publish
state, versions, schedules, media, and that visitors see the same published pages at the new
site's address. Confirm no reference in the new site points to the original site.

**Acceptance Scenarios**:

1. **Given** an Administrator with an archive,
   **When** they upload it to import,
   **Then** it is checked before anything changes, and they see what it contains, the
   release and content versions it came from, every problem found, and what will happen to
   each one.
2. **Given** a checked archive,
   **When** the Administrator confirms the import as a new site,
   **Then** a new site is created holding every document, collection, item, menu, redirect,
   setting and media file from the archive, with drafts, live versions, retained versions
   and pending schedules as they were exported.
3. **Given** an imported site,
   **When** any page, component, item, menu, redirect, form or binding in it refers to
   another page, item, collection, component, menu or media file,
   **Then** the reference points to the imported copy in the new site, never to the original
   site or to a different site on the installation.
4. **Given** an archive whose host names are already used by another site on this
   installation,
   **When** it is imported as a new site,
   **Then** those host names are not added, the check and the final report say so, and the
   rest of the import continues.
5. **Given** the same archive imported twice into the same installation,
   **When** both imports finish,
   **Then** there are two independent sites; editing or publishing one never changes the
   other.
6. **Given** an import running in the background,
   **When** the Administrator follows its progress,
   **Then** they see its step and percentage and, at the end, a report of what was imported,
   what was left out and why.
7. **Given** an archive imported as a new site with **start fresh** chosen,
   **When** the import finishes,
   **Then** every page, component, item and menu holds the archive's latest draft (its live
   version where it had no newer draft), nothing is published, and no version history,
   schedules or copy origins are brought in; collections, settings, design tokens,
   redirects and media are imported as usual.

---

### User Story 3 - Import refuses archives it cannot trust or understand (Priority: P1)

Minh tries to import three files. The first was cut off during download: the check says the
archive is incomplete and nothing is changed. The second was exported by a newer release
than this installation: the check names both versions and tells them to upgrade this
installation first. The third was edited by hand and contains a page that breaks the page
rules and a file claiming to be an image that is not one: the check lists both problems
and the import does not start.

**Why this priority**: An import writes a whole site from a file that may come from anywhere.
Refusing damaged, newer or invalid archives before any change is what makes import safe to
offer at all (Principles III, VI and IX).

**Independent Test**: Import a truncated archive, an archive with a changed file, an archive
from a newer release or content version, an archive with an invalid page, item, collection
schema or menu, an archive with a file whose content does not match its declared type, and
an archive whose paths try to escape its own folder. Confirm each is refused at the check
with a clear reason and that nothing on the installation changed.

**Acceptance Scenarios**:

1. **Given** an archive that is incomplete, unreadable, or whose contents do not match the
   integrity information it carries,
   **When** it is checked,
   **Then** it is refused with a reason and nothing is changed.
2. **Given** an archive from a newer archive format or carrying content in a newer schema
   version than this installation supports,
   **When** it is checked,
   **Then** it is refused, naming the archive's versions and the installation's, and saying
   that the installation must be upgraded first.
3. **Given** an archive from an older release whose content is in an older schema version
   still supported,
   **When** it is imported,
   **Then** the content is accepted and upgraded under the same rules as stored content
   (019), and the check says it will be.
4. **Given** an archive holding a page, component, collection schema, item, menu or redirect
   that breaks the rules a member's save must follow,
   **When** it is checked,
   **Then** it is refused, listing each invalid entry with what is wrong, and nothing is
   changed.
5. **Given** an archive holding a media file whose content is not an allowed type or exceeds
   the installation's size limits,
   **When** it is checked,
   **Then** the file is refused under the same rules as an upload, named in the report, and
   the check states whether the import can continue without it.
6. **Given** an import that fails or is cancelled after it started,
   **When** it stops,
   **Then** the target looks exactly as before the import: no partial site, no partial
   content, and nothing new visible to visitors.

---

### User Story 4 - Importing into an existing site with conflicts (Priority: P2)

An agency keeps a "Starter" site with a set of landing pages, a "Testimonials" collection
and shared header and footer components. Hoa, an Administrator, imports Starter's archive
into the customer's existing site, which already has a home page, an "About" page and a
"Header" component. The check lists the conflicts: three pages with the same address, one
component with the same name, one collection with the same address name. Hoa chooses
"rename" for this import. The imported "About" becomes `/about-2`, the component "Header
(imported)", and every imported page that used Starter's header now uses "Header
(imported)"; the customer's own pages are untouched. On another day Hoa restores a few
pages a customer broke by importing last week's backup of that same site with "replace":
the pages take the archive's content, and the broken versions stay in their history.

**Why this priority**: Importing into a new site covers backups, moves and starting points.
Importing into a site that already has content is what the issue's conflict handling is for,
but it builds on Stories 2 and 3.

**Independent Test**: Import an archive into a site that shares some pages, components,
collections, items, menus and redirects with it, once with each of replace, skip and rename.
Confirm the check lists every conflict, each policy gives the outcome FR-024 to FR-026
describes, references in imported content point to the right document for each policy, and
content of the target site that has no counterpart in the archive is unchanged. Then export
a site, add, change and delete content in it, and restore the archive into it: confirm the
preview lists every change, the site matches the archive afterwards, and restoring the
pre-restore export brings back the state before the restore.

**Acceptance Scenarios**:

1. **Given** an archive and an existing site,
   **When** an Administrator checks it for import into that site,
   **Then** the check lists every conflict (a page with the same address, a shared component
   or menu with the same name, a collection with the same address name, an item with the
   same address name in a matching collection, a redirect with the same source) and asks
   for one policy: replace, skip or rename. When the archive came from this same site,
   documents are matched by identity first, so a page renamed or moved since the export is
   matched to itself, not duplicated (FR-022a).
2. **Given** the policy **replace**,
   **When** the import finishes,
   **Then** each conflicting document keeps its place and identity in the target site but
   takes the archive's draft and published state, its previous versions stay in its history
   so it can be rolled back, and references to it from the rest of the site keep working.
3. **Given** the policy **skip**,
   **When** the import finishes,
   **Then** each conflicting document in the target is unchanged, the archive's version is
   not imported, and imported content that referred to it now refers to the target's
   existing document.
4. **Given** the policy **rename**,
   **When** the import finishes,
   **Then** each conflicting document from the archive is added beside the existing one under
   a unique address or name, and imported content that referred to it refers to the added
   copy.
5. **Given** any policy,
   **When** the import finishes,
   **Then** documents of the target site with no counterpart in the archive are unchanged,
   nothing is deleted, the target site's host names are unchanged, and the report lists
   every conflict and how it was resolved.
6. **Given** an import into an existing site,
   **When** it runs,
   **Then** members cannot change that site's content until it finishes, are told why, and
   visitors keep seeing the site as it was until the import completes, after which the
   imported published state appears at once.
7. **Given** an archive exported from a site that still exists on this installation,
   **When** an Administrator chooses **restore** into that site,
   **Then** the check shows a preview of every document, collection, item, menu, redirect
   and media file that will be removed, changed or brought back, and the restore starts only
   after the Administrator types the site's name to confirm.
8. **Given** a confirmed restore,
   **When** it finishes,
   **Then** the site's content, publish state, versions, schedules, settings, design tokens
   and media match the archive: documents added since the export are removed, documents
   deleted since are back with their original identity, and changed documents are as they
   were; the site's host names and enabled state are unchanged.
9. **Given** a restore about to change a site,
   **When** it starts,
   **Then** an export of the site's current state is made first and kept like an admin
   export, so the restore can itself be undone; if that export fails, the restore does not
   start.
10. **Given** an archive from another site or another installation,
    **When** an Administrator imports it,
    **Then** restore is not offered; only a new site or a merge into an existing site is.

---

### User Story 5 - An operator scripts backups and restores from the command line (Priority: P2)

Tuan, the operator, adds a nightly job on the server that exports every site to a backup
folder with the command line, keeping the last 14 archives. The command prints progress,
writes the archive and a summary, and ends with a non-zero exit code if anything failed so
their monitoring alerts them. When a server is lost, they install the product on a new server
and import the latest archive with the same command line, choosing "new site".

**Why this priority**: Scripted backups are named in the issue and are how operators protect
data in practice, but they reuse the export and import of Stories 1 to 4.

**Independent Test**: From inside the running container, export a site by its identifier
and by its host name to a given path, check an archive without importing it, import it as a
new site, into an existing site with each policy, and as a restore into its source site.
Confirm the outcome equals the admin's,
progress and the summary are printed, and the exit codes distinguish success, refused
archive, invalid arguments and failure.

**Acceptance Scenarios**:

1. **Given** shell access to the installation,
   **When** the operator runs the export command for a site, identified by its identifier or
   one of its host names, with an output path,
   **Then** it writes the same archive the admin would produce, prints progress and the
   summary, and exits with success.
2. **Given** the operator runs the export command for all sites,
   **When** it runs,
   **Then** each site is exported to its own archive, and a failure of one site is reported
   without stopping the others, with a non-zero exit code at the end.
3. **Given** an archive,
   **When** the operator runs the import command with a check-only option,
   **Then** it prints the check's result, including conflicts with a named target site, and
   changes nothing.
4. **Given** an archive,
   **When** the operator runs the import command as a new site, into a named site with a
   conflict policy, or as a restore into its source site with the explicit confirmation
   option,
   **Then** the outcome is the same as an import from the admin, and the report is printed.
5. **Given** any command failure (refused archive, unknown site, invalid arguments, a failed
   run),
   **When** the command ends,
   **Then** it prints the reason and exits with a documented non-zero code that tells these
   cases apart.
6. **Given** export and import from the command line,
   **When** they run,
   **Then** they are recorded like admin exports and imports, as operator actions.

---

### Edge Cases

- **Export from a site with a schema change or publish in progress**: the export includes
  each document in a whole state, before or after the change, never half of it.
- **Two exports of the same site at once**: the second waits for the first, or is refused
  with a message; it never produces a mixed archive.
- **An export started while an import into the same site runs**: it waits for the import
  to finish so that it never captures part of an import.
- **Very large media libraries**: export and import complete without loading the whole
  archive into memory, and the admin download can resume after an interruption.
- **Archives in the admin are not kept forever**: a finished export can be downloaded for
  7 days and is then deleted; an Administrator can delete it earlier. The command line
  writes to the operator's own path and is not subject to this.
- **Pending schedules whose time has passed by import**: they are not carried out at import;
  the report lists them so a member can publish by hand.
- **Authorship of an imported document** (created by, published by, copy origin): members of
  the original installation are not accounts here. Their recorded display names are kept as
  history and shown as such, and no account, role or invitation is created.
- **References to things that are not in the archive** (for example a media file deleted
  while the export ran, or a broken reference already present in the site): the import
  keeps them as broken references, handled like any broken reference in the editor and
  renderer, and the report lists them.
- **Item references across collections with mixed policies**: an item in an imported
  collection that refers to an item in a skipped collection refers to the target's item
  with the same address name, or becomes a broken reference that the report lists.
- **Replacing a collection whose fields differ**: the archive's fields are applied to the
  existing collection under the same rules, warnings and value conversions as a schema
  change made by a member (014); the check shows what will be converted or lost before the
  Administrator confirms.
- **Identical media file already in the target site**: it is reused, not stored twice,
  whatever the policy.
- **Collection templates** (016): a page that is the template of a collection stays its
  template after import; if the template is skipped, the collection keeps the target's
  template.
- **Design tokens of an existing site**: replace applies the archive's tokens; skip and
  rename keep the target's tokens (tokens with the archive's names not in the target are
  added).
- **Site settings of an existing site**: kept, except with replace, which applies the
  archive's name, language and search settings; host names and the enabled state are never
  changed by an import into an existing site.
- **Disabled source site**: it can be exported; a site imported from it as a new site
  starts disabled.
- **Hand-edited archive that is still valid**: accepted if it passes every check; integrity
  information that does not match is a refusal, so edits must keep it consistent.
- **Archive bigger than the admin upload limit**: the upload is refused in the admin, and the
  message points to the command line, which has no upload limit.
- **Server restart during an export or import**: the job is marked failed (interrupted); an
  import never ends half applied, and the Administrator sees what happened.

## Requirements *(mandatory)*

### Functional Requirements

**What an archive holds**

- **FR-001**: An export MUST produce one archive file holding the whole site:
  - site settings (name, scheme, default language, host names, enabled state) and design
    tokens;
  - every page with its place in the tree, its settings and search metadata, and its forms
    and navigation blocks;
  - every shared component;
  - every collection with its schema, and every item;
  - every menu (024 FR-036);
  - redirects and site search settings (017 FR-040);
  - a media manifest describing every media file of the site (folders, display names, types,
    sizes, alternative text and the other details 003 and 013 record) and the bytes of each
    original file.
- **FR-002**: For each page, shared component, item and menu the archive MUST carry its
  draft, its live version, every retained version with who published it and when, its
  pending publish and unpublish schedules, and its copy origin where it has one (023).
- **FR-003**: The archive MUST NOT carry form submissions, the submission log or spam
  counters (018 FR-040), editing locks, viewer presence and takeover records (023), member
  accounts, passwords, role assignments, sign-in sessions, installation configuration or
  secrets. Image variants and other data the installation derives MUST NOT be carried; they
  are rebuilt on import.
- **FR-004**: The archive MUST record its archive format version, the release that produced
  it, the content schema version of every document, when it was made and from which site,
  and integrity information that lets import detect any missing or changed part.
- **FR-005**: Every document and file in the archive MUST be in a whole state: the state
  before or after any concurrent change, never part of one. References inside the archive
  MUST either resolve to something in the archive or be listed in the export summary as
  broken.

**Export**

- **FR-006**: Administrators MUST be able to start an export of any site from the admin. It
  MUST run as a background job that does not block editing, publishing or visitors.
- **FR-007**: The admin MUST show a running export's current step and percentage, keep
  showing it after the Administrator leaves and returns, and let them cancel it.
- **FR-008**: A finished export MUST be downloadable from the admin as one file by
  Administrators for 7 days, after which it is deleted; Administrators MUST be able to
  delete it earlier. The download MUST be resumable.
- **FR-009**: A finished export MUST show a summary: counts of each kind of content, the
  archive size, and every problem found (for example missing media bytes), which do not stop
  the export.
- **FR-010**: At most one export of a site MUST run at a time, and an export MUST NOT start
  while an import into the same site runs.

**Checking an archive**

- **FR-011**: Every import MUST first check the archive and MUST change nothing until the
  check passes and the Administrator (or the command) confirms.
- **FR-012**: The check MUST refuse an archive that is incomplete, unreadable, does not
  match its integrity information, or whose internal paths point outside the archive.
- **FR-013**: The check MUST refuse an archive whose format version, or any of whose content
  schema versions, is newer than this installation supports, naming the archive's and the
  installation's versions and saying the installation must be upgraded first. Older
  versions still supported MUST be accepted and upgraded under the same rules as stored
  content (019).
- **FR-014**: The check MUST validate every page, component, collection schema, item, menu,
  redirect and setting against the same rules a member's save must pass, and refuse the
  archive listing every invalid entry with its reason.
- **FR-015**: The check MUST validate every media file by its content type and size under
  the same rules as an upload (Principle IX), never executing any of it. A refused or
  missing file MUST be listed; the import MAY continue without it, keeping its manifest
  entry as a missing file, once the Administrator confirms.
- **FR-016**: The check MUST report what the archive contains, the versions it came from,
  every problem, every conflict with the target (FR-022), host names that will not be added
  (FR-020), and pending schedules whose time has passed (FR-029), before the import is
  confirmed.

**Import**

- **FR-017**: Administrators MUST be able to import an archive as a new site, as a merge
  into an existing site, or as a restore into the site it was exported from (FR-027a).
  Import MUST run as a background job with the same progress and cancel as export (FR-007).
- **FR-018**: An import MUST be all or nothing: if it fails, is cancelled or the server
  stops, the target MUST be as it was before the import, with no partial content and
  nothing new visible to visitors.
- **FR-019**: Imported content MUST belong only to the target site. Every reference between
  imported things (page links, item and collection references, component uses, menu
  entries, Navigation blocks, form redirects and consent links, collection templates and
  bindings, redirect targets, media uses) MUST point to the imported or matched document in
  the target site, never to the source site or another site on the installation.
- **FR-020**: Importing as a new site MUST create the site with the archive's settings and
  a name the Administrator gives. Host names already used on this installation MUST be left
  out and listed. A site exported while disabled MUST be created disabled.
- **FR-021**: Imported documents MUST keep their drafts, live state, retained versions and
  copy origins; what was live MUST be live and visible to visitors at the target site's
  address once the import completes. Recorded authorship MUST be kept as history only; no
  account, role or invitation is created.
- **FR-021a**: When importing as a new site, the Administrator MUST be able to choose
  **start fresh** instead of the default full import. Start fresh MUST give each page,
  shared component, item and menu one draft equal to its latest draft in the archive (its
  live version where it had no newer draft), publish nothing, and bring in no retained
  versions, schedules or copy origins. Collections, settings, design tokens, redirects and
  media MUST be imported as in a full import. Start fresh MUST NOT be offered for a merge or
  a restore.

**Conflicts when importing into an existing site**

- **FR-022**: When merging into an existing site, a conflict MUST be detected for a page
  with the same address, a shared component with the same name, a collection with the same
  address name, an item with the same address name in a matched collection, a menu with the
  same name, and a redirect with the same source. A media file identical to one in the
  target MUST be reused, not reported as a conflict.
- **FR-022a**: When the archive was exported from the target site itself, each document in
  the archive MUST first be matched to the target's document with the same identity, even
  if its address or name has changed since, and only otherwise by address or name as in
  FR-022. When a document matches one target document by identity and a different one holds
  its address or name, the check MUST report both, and the policy applies to the identity
  match while the address or name clash is resolved as under rename. Archives from any
  other site or installation MUST be matched by address or name only.
- **FR-023**: The Administrator MUST choose one policy for all conflicts of an import:
  replace, skip or rename.
- **FR-024**: **Replace**: the existing document MUST keep its identity and place and take
  the archive's draft and live state; its earlier versions MUST stay in its history. A
  replaced collection takes the archive's fields under the rules of a member's schema change
  (014), shown in the check before confirming.
- **FR-025**: **Skip**: the existing document MUST stay unchanged and the archive's version
  MUST NOT be imported; imported content that referred to it MUST refer to the existing
  document. Items of a skipped collection are matched to the target's items by address
  name; unmatched references become broken references listed in the report.
- **FR-026**: **Rename**: the archive's document MUST be added beside the existing one with
  a unique address or name under the rules of the feature that owns it (010, 014, 015, 024);
  imported content that referred to it MUST refer to the added document.
- **FR-027**: A merge into an existing site MUST NOT delete anything, MUST NOT change the
  site's host names or enabled state, and MUST NOT change documents that have no
  counterpart in the archive. Site settings and design tokens MUST change only under
  replace, as the edge cases describe.

**Restoring a site from its own archive**

- **FR-027a**: Restore MUST be offered only when the archive's recorded source site exists on
  this installation, and only into that site. Documents MUST be matched by their identity,
  not by address or name, and no conflict policy applies.
- **FR-027b**: Before a restore is confirmed, the check MUST preview every document,
  collection, item, menu, redirect and media file that will be removed, changed or brought
  back, and any collection field changes. The restore MUST start only after the
  Administrator types the site's name, or the command is given an explicit confirmation
  option.
- **FR-027c**: A restore MUST make the site's content, publish state, versions, schedules,
  settings, design tokens, redirects and media match the archive: what the archive does not
  hold is removed, what it holds but the site lost is brought back with its original
  identity, and the rest takes the archive's state. Host names and the enabled state MUST
  NOT change. Past-due schedules follow FR-029.
- **FR-027d**: Before changing anything, a restore MUST export the site's current state and
  keep it as an admin export (FR-008), named in the report; if that export fails, the
  restore MUST NOT start.
- **FR-028**: While an import into an existing site (merge or restore) runs, members MUST NOT be able to change
  that site's content and MUST be told why; visitors MUST keep seeing the site as it was,
  and the imported published state MUST appear at once when the import completes, with
  every affected cached page refreshed (Principle VIII).
- **FR-029**: Pending schedules whose time has passed by the import MUST NOT be carried out
  and MUST be listed in the report; future schedules MUST be kept.

**Report and audit**

- **FR-030**: A finished import MUST show a report: counts imported per kind, every conflict
  and how it was resolved, host names left out, files left out, broken references, past
  schedules dropped, and the new or target site with a link to it.
- **FR-031**: Every export, download, import, cancel and deletion of an archive MUST be
  recorded with who did it (member, or operator for the command line), the site, the time
  and the outcome, without any content of the site (Principle IX).

**Command line**

- **FR-032**: The operator MUST be able, from inside the installation's container, to export
  one site (by identifier or host name) or every site to a path they choose; to check an
  archive without importing it; and to import an archive as a new site (full or start fresh), as a merge into a
  named site with a conflict policy, or as a restore into its source site. The outcome MUST
  equal the admin's.
- **FR-033**: The commands MUST print progress and the summary or report, and MUST end with
  documented exit codes that tell apart success, a refused archive, an unknown site or
  invalid arguments, and a failed run. Exporting every site MUST continue past a failed
  site and report it at the end.
- **FR-034**: The commands MUST NOT be reachable over the network, as for the existing
  operator commands.

**Security, operations and quality**

- **FR-035**: Only Administrators MUST be able to export, download, check and import, and
  this MUST be enforced server-side on every operation (Principle IX).
- **FR-036**: Export and import MUST work with every configured media storage provider and
  with several app instances, and MUST NOT require any external service (Principles II and
  IV). Export and import MUST handle archives far larger than the available memory.
- **FR-037**: An archive exported by this installation and imported back into it, or into
  another installation of the same or a later release, MUST give a site whose content,
  publish state, versions, schedules and media are equal to the source's, apart from the
  identifiers and the host names FR-020 leaves out.
- **FR-038**: The archive format MUST be documented as a versioned contract with the other
  shared contracts (Principle VI), so that a later release can read every archive format
  version it claims to support.
- **FR-039**: All new screens, progress panels and reports MUST meet the admin accessibility
  baseline (`docs/admin-accessibility-baseline.md`).

### Key Entities *(include if feature involves data)*

- **Site Archive**: one file holding a whole site at one point in time; records its archive
  format version, the release and site it came from, when it was made, the content schema
  version of each document, and integrity information.
- **Media Manifest**: the part of an archive that lists every media file with its details,
  folder and whether its bytes are included or missing.
- **Export Job**: one export of a site; who started it (member or operator), its state
  (running, finished, failed, cancelled), step and percentage, summary, and, for the admin,
  the archive kept for download until it expires.
- **Import Job**: one import of an archive; who started it, its mode (new site, merge or
  restore), for a new site whether it is a full import or start fresh, the target site, the conflict policy (merge only), the pre-restore export
  (restore only), its state, step and percentage, and its report.
- **Import Check Result**: what the archive contains, its versions, every problem, every
  conflict with the target, and what the import will do about each.
- **Import Report**: counts imported, conflicts and their resolution, what was left out and
  why, broken references and dropped schedules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A site exported and imported back as a new site is equal to the original in
  100% of documents, items, menus, redirects, settings and media files in the round-trip
  test suite, apart from identifiers and host names left out.
- **SC-002**: On the reference setup (2 vCPU, 4 GB RAM), exporting a site with 500 pages,
  10,000 items and 2 GB of media completes within 15 minutes, and importing it as a new site
  within 20 minutes, without affecting visitors' page times beyond the budgets of
  Principle VIII.
- **SC-003**: In the refusal test suite (truncated, altered, newer-version, invalid-content
  and unsafe archives), 100% of bad archives are refused at the check with a stated reason,
  and nothing on the installation changes.
- **SC-004**: In the failure test suite (failure, cancel and restart at each step of an
  import), the target is unchanged in 100% of runs.
- **SC-005**: Merging into an existing site with each policy gives exactly the outcome the
  check announced, with zero changed or deleted documents that have no counterpart in the
  archive. Restoring a site that was changed after its export makes it equal to the archive
  in 100% of documents, items, menus, redirects, settings and media files, and restoring the
  pre-restore export brings back the state before the restore.
- **SC-006**: An operator can set up a nightly scripted backup of every site in under 10
  minutes using the operations guide, and a failed backup is visible from the command's exit
  code alone.
- **SC-007**: An Administrator can export a site, and import it into another installation
  as a new site, without help, in a usability check with at least three participants.
- **SC-008**: An archive from each supported earlier release imports and renders correctly
  on the current release in the upgrade test suite.

## Assumptions

- **Administrators only**: sites are created and managed by Administrators (009), and an
  archive holds every draft and setting of a site, so export and import are Administrator
  actions. Designers and Editors cannot export or import. Per-site Administrators do not
  exist (009).
- **Whole site, one policy**: export always takes the whole site (partial and incremental
  exports are out of scope). An import applies one conflict policy to every conflict;
  choosing per conflict or per kind is left for later.
- **Only restore deletes**: a merge into an existing site adds and replaces but never
  removes. Removing content happens only in a restore, into the archive's own source site,
  after a preview, a typed confirmation and an automatic pre-restore export (clarified
  2026-11-01). Moving a site to another installation and restoring it there is an import
  as a new site.
- **No encryption of the archive**: the archive is protected by whoever stores it, as the
  operator's database and media backups already are (operations guide). It holds no secrets
  or passwords (FR-003) but does hold unpublished content and members' display names as
  authorship history.
- **Supported versions**: an archive from any earlier release of the same major version is
  accepted; archives from a newer release are refused (019 rules for the database apply in
  the same spirit). Archives from an earlier major version are accepted only if that
  release's upgrade notes say so.
- **Retention**: 7 days for archives kept in the admin is a fixed default here; the plan may
  make it an operator setting with this default.
- **Command line**: the commands sit beside the existing operator commands
  (`identity`, `media check-storage`) and run inside the container. Rotating old backup
  archives is the operator's script's job, as the operations guide will show.
- **Background work** uses the installation's built-in background jobs (Principle IV); no
  external queue is needed.
- **Out of scope**: continuous sync between installations, partial or incremental export,
  exporting form submissions, exporting members and roles, merging two versions of a
  document, and importing from other products' formats.
- **Dependencies**: builds on #3, #9, #10, #11, #12, #13, #14, #15, #16, #17, #18, #19, #23 and
  #24, all of which define what an export carries.
