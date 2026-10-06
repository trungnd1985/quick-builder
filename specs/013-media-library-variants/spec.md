# Feature Specification: Media Library, Image Variants and Remote Storage Providers

**Feature Branch**: `013-media-library-variants`

**Created**: 2026-10-01

**Status**: Draft

**Issue**: #13

**Input**: User description: "#13 Media library, variants and remote storage providers. Problem / goal: Issue #3 gives the product uploads and local-filesystem storage. Editors still need to browse and reuse what has been uploaded, pages need correctly sized images, and operators running more than one instance need shared storage. This feature builds on the abstraction from #3 rather than redefining it. In: media list with search and folders, media picker in the editor, replace and delete with usage tracking that warns before deleting a file in use, alt text, image variants/resizing with caching, S3-compatible and Azure Blob providers behind the existing storage abstraction, provider selection by configuration. Out: video transcoding, DAM-style metadata and tagging, per-user quotas."

## Context

Feature 003 (#3) created each site's media library: upload with content-based validation,
safe serving from the site's own host name, paged browsing, deletion, and a storage
abstraction with one provider, the local file system. It deliberately left out folders,
search, alternative text, usage tracking, replacing a file's contents, image
transformation, and the S3-compatible and Azure Blob providers.

Feature 011 (#11) added the Image block, which refers to a media file by its identifier
and carries its own alternative text. Its picker is a plain list of the library. Feature
011 left "variants, image cropping, and preventing the deletion of files in use" to this
feature, and feature 012 (#12) renders a deleted image by leaving it out of the page.

This feature makes the library usable at a real site's scale, makes published pages
deliver images at a sensible size, and adds the two shared storage providers that the
constitution (Principle IV) names. It reuses the storage abstraction and conformance suite
from feature 003 and extends them only where a requirement below needs it.

## Clarifications

### Session 2026-10-01

- Q: When a file used on published pages is replaced, do those pages change straight away,
  and who may do it? → **A: straight away, restricted.** Published pages show the new
  contents from their next request. Only members who may publish on the site (Designers
  and Administrators, per feature 012) may replace a file that any currently published
  page or component uses. Editors may replace files they uploaded only while no currently
  published page or component uses them.
- Q: When an operator switches storage providers on an installation that already holds
  files, how do the files reach the new provider? → **A: operator copy plus built-in
  check.** The operator copies the bytes with the storage service's own tools, following
  the documentation; the storage layout is the same on every provider, so a plain copy
  works. A built-in check reports every file or variant the configured provider is
  missing, at start-up (as a warning, without stopping start-up) and on request.
- Q: Should deleting a file that a currently published page or component uses also
  require permission to publish on the site? → **A: yes.** Deleting such a file requires
  the publish capability (Designer or Administrator), the same rule as replace. Editors
  may delete their own files only while no currently published page or component uses
  them.
- Q: When someone replaces a file, who counts as its uploader afterwards? → **A: the
  original uploader.** The person who first uploaded the file remains its uploader for
  every permission check; the time and person of the most recent replacement are
  recorded and shown separately.
- Q: Can Editors rename, move and delete any folder in the site's library, or only
  folders they created? → **A: any folder.** Every member who holds the upload
  capability on the site may create, rename, move and delete (when empty) any folder of
  that site; folders have no owner for permission purposes.
- Q: Should the start-up consistency check hold the instance back until it finishes, or
  run in the background? → **A: background.** It runs after the instance is ready, never
  delays readiness, runs on one instance at a time, and reports to the logs and an
  Administrator status screen.
- Q: Should image variants be produced as soon as an image is uploaded, or only the first
  time each size is requested? → **A: thumbnail eagerly, the rest on demand.** The
  thumbnail size is produced in the background right after upload or replacement; every
  other width is produced on its first request.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Editors organise and find media in folders and by search (Priority: P1)

An Editor opens a site's media library that has grown to several hundred files. They
create a folder called "Team photos", move twelve portraits into it, and add a subfolder
"2026". Later they type "brochure" into the search box and immediately see the two PDFs
whose names match, wherever they are filed. They open a photo, write its alternative text
("Anna Nguyen, head of support, smiling at her desk"), and save it.

**Why this priority**: Browsing and reusing uploads is the core problem the issue names.
Every other story (picking, usage, replacing) is easier when files can be found. It
delivers value on its own even before variants or new providers exist.

**Independent Test**: In a library with more files than fit on one page, create nested
folders, move files between them, search by part of a name and by words in the
alternative text, and confirm that results are correct, that moving a file does not change
its public address, and that alternative text is saved and shown.

**Acceptance Scenarios**:

1. **Given** a site member who can upload to a site,
   **When** they create a folder, a subfolder inside it, and rename either,
   **Then** the folders appear in the library's folder tree, and sibling folders cannot
   share a name (ignoring letter case).
2. **Given** files in the library,
   **When** a site member moves one or several files into a folder in one action,
   **Then** each file appears in exactly one folder, and every moved file keeps its
   identifier and public address.
3. **Given** a folder that still contains files or subfolders,
   **When** a site member tries to delete it,
   **Then** it is refused with a message saying it is not empty. An empty folder is
   deleted after confirmation.
4. **Given** a library of up to 10,000 files,
   **When** a site member types a search term,
   **Then** files whose name or alternative text contains the term (ignoring letter case
   and accents) are listed, across all folders by default, with an option to limit the
   search to the current folder and its subfolders.
5. **Given** a site member browsing a folder,
   **When** they filter by kind (images or documents) or sort by name, upload time or size,
   **Then** the list reflects the filter and order and still pages correctly.
6. **Given** an image in the library,
   **When** a site member writes its alternative text (up to 300 characters) and saves,
   **Then** the text is stored with the file, shown in the library and offered wherever the
   image is chosen for a page.
7. **Given** a user who holds no role on the site,
   **When** they try any folder, search or alternative-text operation,
   **Then** the response is the same as for a site that does not exist.

---

### User Story 2 - Published pages deliver correctly sized images (Priority: P1)

A Designer places a 6,000-pixel-wide photo from a phone into a page and publishes it. A
visitor on a phone downloads a version of the photo a few hundred pixels wide; a visitor
on a large desktop screen gets a larger one. Neither ever downloads the 8 MB original.
The first visitor to need a given size may wait a moment; everyone after that gets it
straight from the cache. Photos appear the right way up even when the camera stored them
sideways, and the layout does not jump while images load.

**Why this priority**: Oversized images are the biggest avoidable cost on a typical
published page and directly affect the performance budgets in constitution Principle VIII.
It is independent of folders and search and valuable on its own.

**Independent Test**: Publish a page with a large photo. Request the page at mobile,
tablet and desktop widths and confirm that each downloads a variant no wider than needed,
that no request downloads the original, that repeated requests are served from the cache,
and that the page reserves the image's space before it loads.

**Acceptance Scenarios**:

1. **Given** a published page with an Image block whose file is a large photo,
   **When** it is rendered for visitors,
   **Then** the image offers several widths so the browser can choose the smallest one
   that fills the space at the visitor's screen size and pixel density, and states the
   image's proportions so the page layout does not shift while it loads.
2. **Given** a variant width that has never been requested for a file,
   **When** it is first requested,
   **Then** it is produced once, stored, and served; every later request for it, on any
   instance, is served from storage without producing it again.
3. **Given** a request for a variant wider than the original,
   **When** it is served,
   **Then** the original's width is never exceeded (images are never enlarged).
4. **Given** a photo whose embedded orientation says it is rotated,
   **When** any variant is produced,
   **Then** the variant is upright, and it carries no embedded camera details or location.
5. **Given** a request for a size that is not one of the configured variant widths,
   **When** it is received,
   **Then** it is answered as not found, so no one can make the server produce arbitrary
   sizes.
6. **Given** an animated image, or an image whose variant cannot be produced,
   **When** a page uses it,
   **Then** the original is used instead and the page still renders; the failure is logged
   with the file's identifier.
7. **Given** a file that is deleted or replaced,
   **When** its old variant addresses are requested,
   **Then** they answer not found, and the stored variants are removed by the background
   clean-up without operator action.
8. **Given** the editor canvas and the library,
   **When** they show image previews and thumbnails,
   **Then** they use small variants rather than the original.

---

### User Story 3 - Operators run several instances on shared S3-compatible or Azure Blob storage (Priority: P1)

An operator running two instances behind a load balancer sets the storage provider to an
S3-compatible bucket (or an Azure Blob container) in configuration and restarts. Uploads
made through one instance are listed, previewed and served by the other. If the
credentials are wrong or the bucket does not exist, the instance refuses to start and
says which setting is wrong. Nothing about the media library changes for site members.

**Why this priority**: Multi-instance deployment is a constitution promise (Principle IV)
that media currently blocks; feature 003 said the S3 provider is the supported answer.
It is independent of the library improvements and needed by every operator who scales
out.

**Independent Test**: Configure each remote provider against a test service, run two
instances, upload through one and fetch through the other, and run the storage
conformance suite unchanged against each provider. Then supply wrong credentials, a
missing bucket or container, and an unknown provider name, and confirm that start-up
fails with a message naming the setting.

**Acceptance Scenarios**:

1. **Given** configuration that selects the S3-compatible provider with an endpoint,
   bucket, region and credentials,
   **When** the installation starts and site members upload, preview, serve and delete
   files,
   **Then** every media operation works exactly as with the local provider, and files are
   stored in that bucket.
2. **Given** configuration that selects the Azure Blob provider with an account, container
   and credentials,
   **When** the same operations are performed,
   **Then** they work exactly as with the local provider.
3. **Given** two instances sharing a remote provider,
   **When** a file is uploaded, replaced or deleted through one instance,
   **Then** the other instance lists, serves or refuses it accordingly from its very next
   request.
4. **Given** a remote provider whose credentials are rejected, whose bucket or container
   does not exist, or whose settings are incomplete,
   **When** the installation starts,
   **Then** start-up fails with a message that names the setting and the problem and never
   contains the secret itself.
5. **Given** a running installation whose remote provider becomes unreachable,
   **When** a site member uploads or a visitor requests a file,
   **Then** the upload is refused with the "storage unavailable" reason, the request fails
   without exposing provider details, and the readiness health check reports storage as
   unavailable until it recovers.
6. **Given** any remote provider,
   **When** a visitor requests a file,
   **Then** it is served from the site's own media address with the same type, safety and
   caching rules as feature 003; visitors never see the bucket's or container's address.
7. **Given** an installation that already holds files in the local provider,
   **When** the operator switches to a remote provider,
   **Then** the documentation tells them how to copy the stored bytes to the new provider
   with the service's own tools, and a built-in check lists every file the new provider
   is missing, in the background after start-up and on request, so they can confirm the copy is
   complete before directing traffic to it.
8. **Given** a provider switch where some files were not copied,
   **When** those files are requested or listed,
   **Then** they behave as missing files do in feature 003 (not found for visitors,
   marked unavailable in the library), and variants that are missing are produced again
   on demand from the original.

---

### User Story 4 - Choosing an image in the editor from the full library (Priority: P2)

A Designer adds an Image block and opens the media picker. It shows the same folders,
search and filters as the library, opens in the folder they last used, and lets them upload
a new photo straight into the current folder. When they choose a photo that already has
alternative text, the block's alternative text is filled in from it; they can change it
for this page or mark the image decorative.

**Why this priority**: The picker from feature 011 already works for small libraries. This
story makes it scale and reuse the alternative text from User Story 1, so it depends on
that story.

**Independent Test**: In a library with folders and several hundred files, open the picker
from an Image block, find a file by folder and by search, upload a new one into a folder,
choose it, and confirm the block refers to the chosen file and its alternative text is
pre-filled and editable.

**Acceptance Scenarios**:

1. **Given** an Image block in the editor,
   **When** the member opens the picker,
   **Then** it offers the folder tree, search, kind filter and sorting from User Story 1,
   showing only images, with thumbnails from variants.
2. **Given** the picker,
   **When** a member who can upload adds a file,
   **Then** it is uploaded into the folder shown, validated as in feature 003, and can be
   chosen straight away.
3. **Given** an image with alternative text in the library,
   **When** it is chosen for a block that has no alternative text of its own,
   **Then** the block's alternative text is filled in from the library, and the member can
   edit it or mark the image decorative without changing the library's text.
4. **Given** an image with no alternative text,
   **When** it is chosen,
   **Then** the property panel prompts for alternative text or the decorative flag, as in
   feature 011.
5. **Given** the picker is closed without choosing,
   **When** the member returns to the canvas,
   **Then** the block is unchanged.
6. **Given** the picker,
   **When** used with the keyboard alone,
   **Then** every folder, search, filter, upload and choose action is reachable and
   announced to assistive technology.

---

### User Story 5 - Seeing where a file is used before deleting or replacing it (Priority: P2)

An Editor wants to delete an old logo. The library shows that it is used on 3 pages and
1 shared component, and lists them with links. When they choose delete, the confirmation
warns that those pages will lose the image and names them; they cancel and replace the
file instead, so every page shows the new logo.

**Why this priority**: Feature 011 and 012 already handle missing images safely (a
placeholder in the editor, omission for visitors), so deletion is not dangerous to the
system, only to content. The warning prevents costly mistakes but depends on the library
and on page content existing.

**Independent Test**: Use one file in a page draft, a published page and a shared
component. Confirm the library shows each usage, that deleting shows a warning naming
them, that a file with no usage is deleted with the ordinary confirmation, and that usage
updates after the draft stops using the file.

**Acceptance Scenarios**:

1. **Given** a file referenced by page drafts, published page versions or shared
   components of its site,
   **When** a site member views it in the library,
   **Then** they see how many places use it and a list of those pages and components,
   distinguishing draft use from published use, each linking to where it can be edited.
2. **Given** a file that is in use,
   **When** a member with permission to delete it chooses delete,
   **Then** the confirmation warns that it is in use, shows the number of pages and
   components affected and names them, and deletion proceeds only if they confirm again.
3. **Given** a file that is not in use,
   **When** it is deleted,
   **Then** only the ordinary confirmation from feature 003 is shown.
4. **Given** an Editor who uploaded a file that a currently published page or component
   uses,
   **When** they try to delete it,
   **Then** it is refused as forbidden with a reason saying a Designer or Administrator
   must delete it; the admin shell says so before they try, but the server enforces it
   either way. If the file is used only in drafts or page history, the Editor may delete
   it after the usage warning.
5. **Given** a draft that stops using a file (the block is removed or points to another
   file), or a page that is deleted,
   **When** the change is saved,
   **Then** the file's usage reflects it within 5 seconds (FR-013).
6. **Given** a file used only by earlier published versions kept for rollback,
   **When** its usage is shown,
   **Then** that use is shown separately as "used in page history", and deleting it warns
   that rolling back to those versions will show the page without the image.
7. **Given** usage recorded before this feature existed,
   **When** the installation is upgraded,
   **Then** usage is computed for every existing draft, published version and component
   without operator action.

---

### User Story 6 - Replacing a file's contents while keeping everything that points to it (Priority: P2)

A Designer uploads a corrected price list PDF and a new version of the company logo as
replacements for the existing files. Every page that used the old files now uses the new
ones, the files keep their names in the library, their folders and their alternative
text, and no visitor or cache is ever given the old contents at the new address.

**Why this priority**: Without replace, updating a logo used on many pages means editing
every page. It builds on usage tracking (User Story 5), which tells the member what will
change.

**Independent Test**: Use a file on a published page, replace it, and confirm that the
page shows the new contents, that the file keeps its identifier, folder and alternative
text, that its public address changes, that the old address answers not found, and that
replacing an image with a PDF is refused.

**Acceptance Scenarios**:

1. **Given** a file and a replacement that passes all of feature 003's validation,
   **When** a permitted member replaces it,
   **Then** the file keeps its identifier, display name, folder, alternative text, usage
   and original uploader; its type, size and dimensions are updated, the time and person
   of the replacement are recorded; and it receives a new public address
   so that caches never serve old contents at the new address.
2. **Given** a replacement of a different kind (an image replaced by a PDF, or the
   reverse),
   **When** it is submitted,
   **Then** it is refused with a reason, and the file is unchanged.
3. **Given** a replaced file,
   **When** its old address or old variants are requested,
   **Then** they answer not found, and the old bytes are removed by the background
   clean-up.
4. **Given** a file used on published pages,
   **When** it is replaced,
   **Then** only a member who may publish on the site (Designer or Administrator) can
   replace it, and every published page and component using it shows the new contents
   from its next request, on every instance, without being republished.
5. **Given** an Editor who uploaded a file,
   **When** they replace it while no currently published page or component uses it,
   **Then** the replacement succeeds; if a published page or component uses it, the
   replacement is refused as forbidden with a reason saying a Designer or Administrator
   must replace it. The admin shell explains this before the Editor uploads, but the
   server enforces it either way.
6. **Given** the replace confirmation,
   **When** the file is in use,
   **Then** it shows the same usage summary as a delete warning, stating that those places,
   including live published pages, will show the new contents straight away.
7. **Given** a replacement that fails validation or is interrupted,
   **When** it ends,
   **Then** the file and its current contents are unchanged and still served.

---

### Edge Cases

- **Folder depth and names**: folders nest up to 8 levels. Names are 1–100 characters,
  cleaned like file names in feature 003, unique among siblings ignoring letter case.
  Moving a folder into itself or one of its own subfolders is refused.
- **Moving a folder**: moves everything inside it; no file's address changes.
- **Two members move the same file at once**: the last move wins; the file is never in
  two folders or in none.
- **Search with no results**: shows an empty state that offers to clear the search or
  search all folders.
- **Search terms with special characters**: treated as literal text, never as patterns.
- **Alternative text on a PDF**: not offered; alternative text applies to images only.
- **Changing a file's alternative text after it is placed on pages**: pages keep the text
  their block holds; the library does not rewrite page content. Blocks that took the text
  from the library are not changed retroactively.
- **An image larger than the configured pixel limit** (default 100 megapixels): new
  uploads are refused with the reason "dimensions too large", because producing variants
  from them could exhaust the server. Such files uploaded before this feature are kept and
  served as originals without variants.
- **Many visitors request the same new variant at once**: it is produced once; the other
  requests wait for it or receive the original, never a partial image.
- **Variant storage lost** (for example deleted from the bucket by hand): variants are
  produced again on demand; originals are never regenerated and a missing original
  behaves as in feature 003.
- **Existing images after upgrade**: their dimensions are recorded by a background job;
  until then pages render them without stated proportions. Their thumbnails are produced
  by the same background job; other widths are produced on demand.
- **A file used in a component that is used on many pages**: usage lists the component
  once and says it appears on N pages.
- **Usage of a file from another site**: cannot arise; an Image block may only refer to a
  file of its own site (feature 011), and any stray reference is ignored by usage tracking.
- **Deleting a file while a member is editing a draft that uses it**: the editor shows
  feature 011's "image missing" placeholder on its next refresh.
- **Remote storage slow or throttling**: uploads and requests may take longer but never
  produce a partial file; retries are bounded and failures are reported as "storage
  unavailable".
- **Clock or eventual consistency differences in remote storage**: a file is listed only
  after the provider confirms it is fully stored (feature 003 FR-011 still holds).
- **Default installation**: with the local provider and no configuration change, the
  installation still makes zero outbound network requests. A remote provider contacts only
  the endpoint the operator configured.

## Requirements *(mandatory)*

### Functional Requirements

**Folders**

- **FR-001**: Each site's library MUST support folders that nest up to 8 levels. Every
  file MUST be in exactly one folder or in the library's top level. Folders belong to one
  site and MUST NOT be shared or moved between sites.
- **FR-002**: Members who hold the upload capability MUST be able to create, rename and
  move any folder of the site, whoever created it, and move files between folders,
  including several files in one action. Deleting a folder MUST be allowed only when it
  is empty. Folders MUST NOT have an owner for permission purposes; who created each
  folder is recorded for display only.
- **FR-003**: Moving a file or folder MUST NOT change any file's identifier or public
  address.
- **FR-004**: Folder names MUST be 1–100 characters, cleaned of path segments and control
  characters, and unique among siblings ignoring letter case.

**Browsing and search**

- **FR-005**: The library list MUST support browsing one folder at a time, searching,
  filtering by kind (images, documents), and sorting by name, upload time (default,
  newest first) or size, and MUST page results so that every matching file appears
  exactly once.
- **FR-006**: Search MUST match files whose display name or alternative text contains the
  search term, ignoring letter case and accents, treating the term as literal text. It
  MUST search the whole library by default and MAY be limited to the current folder and
  its subfolders.
- **FR-007**: Each listed file MUST show its thumbnail (for images), name, kind, size,
  dimensions (for images), upload time, uploader, folder and how many places use it.

**Alternative text**

- **FR-008**: Each image MUST have optional alternative text of up to 300 characters,
  which members holding the upload capability can edit. Documents MUST NOT have
  alternative text.
- **FR-009**: When an image is chosen for an Image block that has no alternative text of
  its own, the block's alternative text MUST be filled in from the library. Afterwards
  the block's text is independent: editing either one MUST NOT change the other.

**Media picker**

- **FR-010**: The editor's media picker MUST offer the folders, search, filters and sorting
  of the library, show only images for the Image block, open in the folder the member last
  used on that site, and let members holding the upload capability upload into the
  current folder.
- **FR-011**: The picker MUST keep feature 011's rule that an Image block refers only to a
  file of the same site, by identifier.

**Usage tracking**

- **FR-012**: The system MUST track, for each file, every page draft, currently published
  page version, shared component (draft or published) and earlier published version kept
  for rollback that refers to it.
- **FR-013**: Usage MUST reflect a saved, published, rolled back or deleted page or
  component within 5 seconds, MUST be computed for all existing content on upgrade, and
  MUST be correct across instances.
- **FR-014**: Members who can view the library MUST be able to see a file's usage count and
  the list of pages and components using it, distinguishing draft, published and history
  use, each linking to where it can be edited when the member may do so. Pages and
  components the member may not see MUST still be counted but MUST NOT be named.
- **FR-015**: Deleting a file that is in use MUST show a warning naming the affected pages
  and components and require a second, explicit confirmation. The server MUST refuse a
  delete of a file in use unless the request states that the member confirmed the
  warning. Deletion requires feature 003's permissions and, when any currently published
  page or component uses the file, also the site's publish capability (FR-038).
- **FR-016**: Deleting a file used by currently published pages or components MUST require
  the site's publish capability; without it the delete MUST be refused as forbidden with
  a reason, even for the file's uploader. A permitted delete MUST cause those pages to render
  without the image (feature 012 FR-027) from their next request, invalidating exactly
  the affected cached pages on every instance.

**Replace**

- **FR-017**: A member MUST be able to replace a file's contents with a new upload of the
  same kind (image for image, document for document) that passes all of feature 003's
  validation. Replacement MUST keep the file's identifier, display name, folder,
  alternative text, usage, original uploader and upload time, MUST update its type,
  size and dimensions, and MUST record the time and person of the most recent
  replacement separately. The original uploader remains the uploader for every
  permission check (delete-own and replace). This supersedes feature 003 FR-005 ("no replace-contents operation").
- **FR-018**: A replaced file MUST receive a new public address and new variant
  addresses. The old addresses MUST answer not found from the moment the replacement
  succeeds, and the old bytes and variants MUST be removed by the background clean-up.
  An address MUST never serve contents other than those it was issued for.
- **FR-019**: When a file used by currently published pages or components is replaced,
  those pages MUST show the new contents from their next request without being
  republished, and exactly the affected cached pages MUST be invalidated on every
  instance. Replacing such a file MUST require the site's publish capability (feature
  012). Earlier published versions kept for rollback also refer to the file by
  identifier, so rolling back shows the current contents.
- **FR-020**: A failed or interrupted replacement MUST leave the file and its current
  contents unchanged and served.

**Image variants**

- **FR-021**: The system MUST provide resized variants of JPEG, PNG, WebP and AVIF images
  at a configurable set of widths (default 320, 640, 960, 1,280, 1,920 and 2,560 pixels),
  never wider than the original, keeping the original's proportions.
- **FR-022**: Each variant MUST be produced at most once per file and width (even with many
  simultaneous requests and several instances), stored through the storage abstraction
  under the owning site's key space, and served from storage on every later request.
  The thumbnail width (the smallest configured width, default 320 pixels) MUST be
  produced in the background right after an image is uploaded or replaced, without
  delaying the upload's result; every other width MUST be produced on its first request.
  Until the thumbnail exists, it too is produced on first request.
- **FR-023**: Variants MUST be upright according to the original's embedded orientation,
  MUST NOT carry embedded camera details or location, and MUST keep transparency where the
  original has it. Originals MUST remain byte-for-byte unchanged (feature 003).
- **FR-024**: Only configured widths MUST be served. Any other size, or any variant of a
  file of another site, a deleted file or a non-image, MUST get the same not-found
  response as feature 003 FR-014.
- **FR-025**: Variants MUST be served with the same type, safety and long-lived caching
  rules as originals (feature 003 FR-016, FR-017), from the site's reserved media address
  space. The one exception is a variant that cannot be produced (FR-026): the original's
  bytes are then returned at the variant's address marked as not cacheable, so no browser
  or intermediate cache keeps the wrong content there.
- **FR-026**: Rendered pages MUST offer the browser the available variant widths with a
  size hint based on the block's layout, so it downloads the smallest adequate variant,
  and MUST state each image's width and height so the layout does not shift. Animated
  GIFs, and images whose variant cannot be produced, MUST use the original.
- **FR-027**: New uploads MUST record image dimensions. Images above a configurable pixel
  limit (default 100 megapixels) MUST be refused with the reason "dimensions too large".
  Dimensions of images uploaded before this feature MUST be recorded by a background job
  after upgrade.
- **FR-028**: The library, the picker and the editor canvas MUST use variants for previews
  and thumbnails; a newly uploaded image's thumbnail MUST be ready within 5 seconds of the
  upload completing on the reference environment (FR-022).
- **FR-029**: Producing variants MUST NOT make the admin or public surface unresponsive:
  the number produced at the same time on one instance MUST be bounded (configurable),
  and a single variant that takes longer than a configurable time MUST be abandoned and
  the original used.

**Remote storage providers**

- **FR-030**: The product MUST ship an S3-compatible provider and an Azure Blob provider
  that implement feature 003's storage abstraction, extended only by what this feature's
  requirements need, and MUST pass the storage conformance suite unchanged, in addition
  to the local provider.
- **FR-031**: The provider MUST be selected by configuration. The local file system MUST
  remain the default. An unknown provider name MUST fail start-up with a message listing
  the supported names (feature 003 FR-022 now lists three).
- **FR-032**: The S3-compatible provider MUST work with any service implementing the S3
  interface, configured by endpoint, region, bucket, optional key prefix, path-style
  addressing option and credentials. The Azure Blob provider MUST be configured by
  account endpoint or connection string, container, optional key prefix and credentials.
  Both MUST also accept the hosting environment's ambient identity where the service
  supports it, so operators need not store keys.
- **FR-033**: At start-up, a remote provider MUST check that its settings are complete and
  that it can write, read and delete in its bucket or container. Any failure MUST stop
  start-up with a message naming the setting and the problem. Secrets MUST never appear in
  messages or logs.
- **FR-034**: The readiness health check MUST report storage as unavailable while the
  configured provider cannot be reached.
- **FR-035**: Files from every provider MUST be served through the site's own media address
  with feature 003's rules; the application MUST NOT hand visitors the provider's own
  address.
- **FR-036**: With a remote provider configured, media, variants, usage and clean-up MUST
  work correctly with several instances behind a load balancer, and the documentation
  MUST state this as a supported configuration. No shared cache is required (feature 012
  closed exception E-4).
- **FR-037**: Stored keys MUST be laid out identically on every provider, so that
  switching providers is a plain copy of the stored bytes made by the operator with the
  storage service's own tools; the operations documentation MUST describe this copy for
  each pair of providers. The product MUST NOT copy bytes between providers itself.
- **FR-037a**: The product MUST include a storage consistency check that lists every file
  (by site and identifier) whose original the configured provider is missing. It MUST run
  automatically in the background after each start-up, without delaying start-up or the
  readiness health check, and with several instances only one MUST run it at a time.
  Missing files MUST be reported as a warning in the logs and on an admin status screen
  visible to Administrators, with the time of the last completed check. It MUST also be
  available on request to Administrators as an administrative operation and as a
  command-line command. Missing variants are not reported, because they are produced
  again on demand.

**Authorization**

- **FR-038**: Folder, move, alternative-text and replace operations MUST each declare a
  capability in feature 002's model and follow its not-found versus forbidden rule.
  Organising folders, moving files and editing alternative text require the upload
  capability. Deleting or replacing a file requires delete-any, or delete-own for the
  uploader (feature 003), and, when any currently published page or component uses the
  file, also the site's publish capability (FR-016, FR-019). Use only in drafts or in
  page history does not require the publish capability. The storage consistency check
  (FR-037a) requires the Administrator role. Variant serving MUST be listed among the
  explicitly anonymous operations.

**Admin shell, operation and privacy**

- **FR-039**: The library and picker screens MUST meet the translation-lookup and
  accessibility baselines from features 001 and 002, with every action usable by keyboard
  alone and announced to assistive technology.
- **FR-040**: The configuration documentation MUST list every new setting with its
  default. The operations documentation MUST cover choosing and configuring each provider,
  the permissions the bucket or container needs, multi-instance setup, backing up remote
  storage with the database, and moving between providers.
- **FR-041**: Logs MUST refer to files, folders and variants by identifier and site only.
  File names, folder names, alternative text, file contents and provider secrets MUST NOT
  appear in logs.
- **FR-042**: A default installation MUST still make zero outbound network requests. With
  a remote provider, the only outbound requests MUST be to the configured storage
  endpoint.
- **FR-043**: The release smoke test MUST be extended to publish a page with an image and
  fetch one of its variants. The provider conformance and multi-instance tests MUST run
  against an S3-compatible test service and an Azure Blob emulator in CI.
- **FR-044**: Folders, alternative text and usage MUST be included in what feature 003
  FR-033 requires for a future media manifest.
- **FR-045**: The upgrade from the previous release MUST keep every existing file, its
  address and its details; every existing file starts in the top level with no
  alternative text.

### Key Entities

- **Media File** (extended from feature 003): gains a folder, optional alternative text
  (images only), image dimensions, the time and person of its most recent replacement,
  and a content version that changes on replacement and is part of its public address.
  Its identifier and original uploader never change.
- **Media Folder**: a named, nestable container in one site's library. It has a site, a
  name, an optional parent folder and its creation details (display only; folders have
  no owner for permission purposes). It holds files and folders but never affects
  addresses.
- **Image Variant**: a resized copy of one content version of an image at one configured
  width. It is derived, stored through the storage abstraction under the site's key
  space, can be produced again at any time, and is removed when its content version is
  replaced or the file is deleted.
- **Media Usage**: the record that a page draft, a published page version, a shared
  component, or a history version refers to a media file. It is derived from page and
  component content and never edited directly.
- **Storage Provider** (extended from feature 003): now one of local file system (default),
  S3-compatible, or Azure Blob, chosen by configuration, each passing the same conformance
  suite.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a library of 10,000 files on the reference environment (two cores, four
  gigabytes of memory), search results and every page of every folder load in the admin
  shell within 1 second.
- **SC-002**: In a usability check, a member finds a specific file among 1,000 by search or
  folder and places it on a page in under 30 seconds.
- **SC-003**: For a 4,000-pixel-wide photo on a published page, a visitor at mobile width
  (375 pixels, 2× density) downloads at least 80% fewer image bytes than the original, and
  no visitor at any of the three preview widths downloads the original.
- **SC-004**: On the reference environment, a cached variant begins to arrive within 100
  milliseconds for 95% of requests under 50 simultaneous requests, and a first-time
  variant of a 20 MiB image is produced within 3 seconds.
- **SC-005**: With 100 simultaneous first requests for the same new variant across two
  instances, the variant is produced exactly once.
- **SC-006**: Published pages with images show zero layout shift caused by images loading.
- **SC-007**: Across the automated test suite, 100% of deletions of files that are in use
  show the usage warning first, and 0% of deletions of files that are not in use show it.
- **SC-008**: Usage shown in the library matches actual page and component content in 100%
  of automated checks, within 5 seconds of each change.
- **SC-009**: After a replacement, 0 requests to the new address ever return the old
  contents, and the old address answers not found on the very next request on every
  instance.
- **SC-010**: The S3-compatible and Azure Blob providers each pass 100% of the storage
  conformance suite, and a two-instance test with each passes 100% of the media journeys.
- **SC-011**: 100% of remote provider misconfigurations in the test set (missing setting,
  wrong credentials, missing bucket or container, no write permission) stop start-up with
  a message naming the setting, and none of those messages or any log line contains a
  secret.
- **SC-012**: After upgrading from the previous release, 100% of existing files are present
  and served at their existing addresses, and 100% of existing images have dimensions and
  usage recorded after the background jobs finish.
- **SC-013**: The library, folder, search, alternative text, picker, usage, replace and
  delete journeys are completable with the keyboard alone, and 100% of their visible text
  resolves through the translation lookup.
- **SC-014**: After an operator copies a site's files from the local provider to a remote
  provider and deliberately leaves some out, the consistency check reports 100% of the
  missing files and no false positives, and a complete copy serves 100% of files at their
  existing addresses.
- **SC-015**: Across the automated test suite, 0 Editors succeed in replacing or deleting a file used
  by a currently published page or component.

## Scope

### In Scope

- Folders, search, filtering and sorting in each site's media library.
- Alternative text on images and its reuse by the editor's Image block.
- The editor's media picker with folders, search and upload into a folder.
- Usage tracking across drafts, published versions, components and history, with
  warnings before deletion and replacement.
- Replacing a file's contents while keeping its identity.
- Image variants at configured widths, produced once, stored and cached, with responsive
  images and stated dimensions on published pages, and variant thumbnails in the admin.
- S3-compatible and Azure Blob storage providers behind feature 003's abstraction,
  selected by configuration, and supported multi-instance media.
- A storage consistency check and documented copy procedure for switching providers.
- Documentation, upgrade jobs, and smoke and provider tests.

### Out of Scope

- Video transcoding, and video or audio as media types.
- DAM-style metadata: tags, captions, credits, custom fields, collections of assets.
- Per-user or per-site quotas.
- Cropping, focal points, filters or other editing of images; arbitrary sizes on request.
- Converting images to a different format in variants.
- Copying stored bytes between providers from within the product.
- Renaming files' display names.
- CDN integration and signed or expiring addresses.
- Removing embedded metadata from originals (feature 003's decision stands; variants carry
  none).
- A recycle bin or restoring deleted files or earlier contents of replaced files.
- Moving or sharing files between sites.
- SVG and other types outside feature 003's allowed list.

## Assumptions

- **Usage warns; only live use restricts who may act**: the issue asks for a warning
  before deleting a file in use. Features 011 and 012 already handle missing images
  safely, so a confirmed delete by a permitted member proceeds. Use by currently published
  pages or components only narrows who is permitted (see Clarifications).
- **Library alternative text is a default, not a live link**: it is copied into an Image
  block when chosen, so changing it never alters already built or published pages without
  a publish (constitution Principle VII).
- **Variants keep the original's format**: format conversion needs per-browser negotiation
  and is left for a later feature; resizing alone removes most of the waste.
- **Fixed widths, not arbitrary sizes**: a fixed set of widths bounds storage and prevents
  anyone from making the server produce unlimited images.
- **Variants are served through the application**, like originals, so site isolation and
  safe serving rules hold for every provider. A CDN in front of the site's address is the
  operator's choice and out of scope.
- **Shared storage is the last multi-instance gap**: feature 012 closed exception E-4, so
  rendering is correct across instances without a shared cache. Once a remote storage
  provider is configured, multi-instance deployment is supported and documented.
- **Replace and delete are the only media actions that reach published pages without a
  publish**: this is deliberate (see Clarifications), and it is why replacing or deleting
  a file in published use requires the publish capability. Media files themselves remain outside the draft and
  published separation, as feature 003 assumed.
- **Folder and alternative-text changes are organisational**: they are attributed but not
  security events, like uploads in feature 003.
- **Library size**: designed for up to 10,000 files per site, as in feature 003.

## Dependencies

- Feature 003 (media storage): the media library, upload validation, serving rules, the
  storage abstraction, its conformance suite and the clean-up job.
- Feature 010 (page tree and components): page drafts, components and deletion, which
  usage tracking follows.
- Feature 011 (editor core): the Image block, its alternative text and decorative flag,
  and the picker this feature extends.
- Feature 012 (publishing and rendering): published versions, rollback history, the
  renderer's image output and cache invalidation, which variants and usage build on.
- Feature 002 (identity and setup): capabilities, per-site roles and the not-found versus
  forbidden rule.
- Feature 001 (foundation skeleton): configuration validation, health checks, background
  jobs, logging rules, the reference deployment and the smoke test.
- The project constitution, in particular Principles III, IV, V, VII, VIII and IX.
- GitHub issue #13, which this feature implements and which the pull request closes.
