# Feature Specification: Media Library and Storage Abstraction

**Feature Branch**: `003-media-storage`

**Created**: 2026-09-24

**Status**: Draft

**Issue**: #3

**Input**: User description: "Media module and storage abstraction. Constitution Principle IV names media storage among the pluggable providers, with a local filesystem default and S3-compatible and Azure Blob as options. The foundation skeleton deliberately ships no storage abstraction, because nothing stored a file yet and Principle XI forbids abstractions no spec needs. This feature defines that abstraction against its first real consumer. In: the Media module, upload with content-type and size validation, the storage abstraction with a local filesystem default, serving uploaded files from a non-executable path, site-namespaced media paths. Out: image transformation pipelines, CDN integration, the S3 and Azure providers themselves — those follow once the abstraction exists and has one working implementation."

## Context

Feature 001 recorded exception E-2: the constitution names media storage as a pluggable
provider, but the foundation shipped no storage abstraction because nothing stored a
file yet, and building one without a consumer would have meant guessing its shape. This
feature closes E-2. It introduces the first thing that stores files, a per-site media
library, and defines the storage abstraction against that real use. It ships one
provider, the local file system, which is the constitution's default.

Feature 002 left Designer and Editor with few actions until content modules arrive, and
it said that each module adds its own capabilities. Media is the first of those modules.

## Clarifications

### Session 2026-09-25

- Q: Should an uploaded file be publicly reachable as soon as it's stored, or only after
  someone deliberately makes it public? → **A: public immediately.** A file is served
  at its hard-to-guess public address from the moment it is stored. Media has no private
  or draft state in this feature.
- Q: When a site member deletes a file, should it be gone for good right away, or kept
  somewhere it can be restored from for a while? → **A: permanent.** Deletion removes
  the file from the library and its public address at once, and the bytes are purged by
  the background job. There is no recycle bin or restore in this feature; recovery is
  from the operator's backups.
- Q: Should uploads keep embedded photo metadata, such as the GPS location and camera
  details inside a phone photo, when the file becomes public? → **A: keep the file
  unchanged, and warn.** Files are stored and served exactly as uploaded, and the upload
  screen tells users that embedded details such as location are published with the
  file.
- Q: Which site roles should be allowed to permanently delete a media file? → **A:
  Administrator and Designer can delete any file; Editor only files they uploaded
  themselves.**
- Q: How many files should one site's media library be designed to hold while browsing
  stays fast? → **A: up to 10,000 files per site**, with any page of the library
  loading within 1 second on the reference environment.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A site member uploads files to the site's media library (Priority: P1)

A Designer on a site opens that site's media library in the admin shell and adds a few
images and a PDF, by picking them or by dragging them onto the page. Each file shows its
progress. Accepted files appear in the library with their name, type, size, upload time
and uploader, and images show a preview. A file that is too large, or whose contents are
not an allowed type, is refused with a message that says why. The other files are
unaffected.

**Why this priority**: Storing a file is the one thing every other part of this feature
depends on. Without it there is nothing to serve, nothing to manage and no consumer for
the storage abstraction.

**Independent Test**: As a user with a role on one site, upload one allowed file of each
supported type and confirm that each appears in that site's library with correct
details. Then upload an oversized file and a disguised file, such as a script renamed to
end in `.jpg`, and confirm that both are refused with distinct reasons and that neither
appears in the library.

**Acceptance Scenarios**:

1. **Given** a user who holds a role on site A,
   **When** they upload a file whose contents are an allowed type and whose size is
   within the limit,
   **Then** the file is stored, appears in site A's library with its details, and is
   given a public address.
2. **Given** the same user,
   **When** they upload a file larger than the configured limit,
   **Then** it is refused as too large, the server stops receiving it once the limit is
   passed, and nothing is added to the library.
3. **Given** the same user,
   **When** they upload a file whose actual contents are not an allowed type, whatever
   its name or declared type says,
   **Then** it is refused as an unsupported type, and nothing is added to the library.
4. **Given** the same user,
   **When** they upload a file whose contents are an allowed type but whose name or
   declared type claims a different one,
   **Then** it is refused as a mismatch, and nothing is added to the library.
5. **Given** the same user,
   **When** they upload several files in one go and one of them is invalid,
   **Then** every valid file is stored, the invalid one is refused with its reason, and
   the result for each file is shown separately.
6. **Given** an upload that is interrupted partway, for example because the connection
   drops or the process restarts,
   **When** the library is viewed afterwards,
   **Then** the file does not appear in it, and no partial file is ever served.
7. **Given** a user who holds no role on site A and is not an administrator,
   **When** they try to upload to site A or list its library,
   **Then** the response is the same as for a site that does not exist.

---

### User Story 2 - Visitors receive uploaded files safely from the site's own address (Priority: P1)

A site member copies the public address of an image from the library and opens it. The
image loads from the site's own host name. A visitor who requests the same address
receives exactly the file that was uploaded, marked with its validated type. No uploaded
file, however it was crafted, is ever run by the server or treated by a browser as a
page or script belonging to the site.

**Why this priority**: The constitution requires that uploads are never executed or
served from an executable path. Serving is also what makes uploads useful: the page
editor and renderer will point at these addresses. This story shares P1 with upload
because stored files that cannot be delivered safely are no use to anyone.

**Independent Test**: Upload one file of each allowed type to site A. Request each one's
public address on site A's host name and confirm that the bytes and type match the
upload. Request the same addresses on site B's host name and confirm that they are not
found. Then upload files crafted to look like both an allowed type and active content,
and confirm that none of them is ever interpreted as a page or script.

**Acceptance Scenarios**:

1. **Given** a file uploaded to site A,
   **When** a visitor requests its public address on one of site A's host names,
   **Then** they receive the exact uploaded bytes, marked with the type that was
   validated at upload.
2. **Given** the same file,
   **When** its address is requested on site B's host name, or on a host name that
   matches no site,
   **Then** the response is not found, identical to a file that never existed, and
   reveals nothing about site A.
3. **Given** a file uploaded to site A,
   **When** a visitor requests an address with the right shape but an identifier that
   was never issued, or that belongs to a deleted file,
   **Then** the response is not found.
4. **Given** any uploaded file,
   **When** it is served,
   **Then** the browser is told not to guess a different type, and the file cannot run
   script with the site's privileges.
5. **Given** a file's public address,
   **When** it is requested repeatedly,
   **Then** browsers and intermediate caches are allowed to keep it for a long time,
   because the content at an address never changes. Requests for part of a file, for
   example to resume a download or to seek within a PDF, are honoured.
6. **Given** any public request whose path tries to escape the media address space,
   such as by using `..`, encoded separators or absolute paths,
   **When** it is processed,
   **Then** it never reaches a file outside that site's media, and it is answered as not
   found.

---

### User Story 3 - Site members browse and delete media (Priority: P2)

An Editor opens the media library of a site they work on, looks through it page by page,
copies the public address of a file to use elsewhere, and deletes a file they uploaded
by mistake, after confirming. From that moment the file is gone from the
library and its public address stops working.

**Why this priority**: A library that can only grow is usable for a first demo. Real use
soon needs browsing and removing mistakes, but this depends on upload and serving being
in place.

**Independent Test**: With a library of more files than fit on one page, page through it
and confirm that every file appears exactly once, newest first. Copy one file's address
and confirm that it serves the file. Delete that file and confirm that it disappears from
the library and that its address answers not found straight away.

**Acceptance Scenarios**:

1. **Given** a site with more files than one page shows,
   **When** a site member browses the library,
   **Then** files are listed newest first, a page at a time, and every file appears
   exactly once even while others are being uploaded.
2. **Given** a file in the library,
   **When** a site member copies its public address,
   **Then** the copied address serves that file on the site's host name.
3. **Given** a site member who confirms deletion of a file,
   **When** the deletion completes,
   **Then** the file is gone from the library, its public address answers not found on
   every instance, and the stored bytes are removed afterwards without operator action.
4. **Given** a site that has no host name yet,
   **When** a site member views its library,
   **Then** they can still upload and preview files, and the admin shell explains that
   public addresses start working once the site has a host name.
5. **Given** an Editor on a site,
   **When** they try to delete a file that another user uploaded,
   **Then** it is refused as forbidden and the file is unchanged. The admin shell does
   not offer deletion for that file, but the server enforces the rule either way.
6. **Given** a user who can see a site but whose role does not include a media
   capability,
   **When** they call that media operation on the site,
   **Then** it is refused as forbidden, following the forbidden-versus-not-found rule
   from feature 002.

---

### User Story 4 - Operator chooses and relies on where media is stored (Priority: P2)

An operator installs the product with the reference deployment and uploads media without
configuring anything. Later they point media storage at a different directory, back it
up alongside the database, and upgrade to a new image. Every file survives. If they
misconfigure storage, the installation refuses to start and tells them exactly what is
wrong.

**Why this priority**: The constitution requires that a fresh install works without
setup and that upgrades never lose data. Files the operator cannot keep, back up or
move would break both promises. This story also establishes the provider selection that
the S3-compatible and Azure Blob providers will plug into.

**Independent Test**: On the reference deployment, upload files, replace the application
image with a newer build, and confirm that every file and its details are intact. Then
set the storage location to a directory that cannot be written, and confirm that
start-up fails with a message naming the setting and the reason. Set an unknown provider
name, and confirm that start-up fails in the same way.

**Acceptance Scenarios**:

1. **Given** a fresh installation from the reference deployment with no storage
   settings,
   **When** a site member uploads a file,
   **Then** it is stored in the documented default location, which the reference
   deployment keeps on persistent storage.
2. **Given** an installation holding uploaded files,
   **When** the operator replaces the application image and restarts,
   **Then** 100% of the files and their details are still present and served.
3. **Given** a storage location that does not exist and cannot be created, cannot be
   written, or lies inside a location the application serves or runs code from,
   **When** the installation starts,
   **Then** start-up fails with a message that names the setting and the problem.
4. **Given** a storage provider name that is not supported,
   **When** the installation starts,
   **Then** start-up fails with a message that names the setting and lists the
   supported providers.
5. **Given** the documentation,
   **When** an operator looks up how to back up or move an installation,
   **Then** it tells them to include the media storage location together with the
   database, and explains what happens if the two are restored from different
   moments.

---

### Edge Cases

- **Two files with the same name**: both are kept as separate files, each with its own
  identifier and address. Names do not need to be unique.
- **File names with path segments, control characters, very long names or characters
  from any language**: the original name is kept for display after removing path
  segments and control characters and shortening it to a documented maximum. It never
  decides where the bytes are stored.
- **Empty file**: refused as invalid.
- **The storage location fills up during an upload**: the upload fails with a clear
  message, nothing is added to the library, and no partial file remains visible.
- **A stored file goes missing outside the application**, for example because an operator
  removed it by hand: its public address answers not found, the library marks the file
  as unavailable instead of failing to load, and the event is logged.
- **Bytes left behind without a library entry**, for example after a crash between
  storing and recording: a background job removes them after a grace period. It never
  removes bytes that belong to a library entry or to an upload still in progress, and
  it is safe with several instances running.
- **A file deleted while a visitor is downloading it**: the download in progress may
  finish or fail, but no new request is served afterwards.
- **Browser and intermediate caches after deletion**: copies a visitor's browser or an
  intermediate cache already holds may keep showing the file until they expire. The
  product does not control those copies, and the documentation says so.
- **Replacing a file's contents**: not possible. A new version is a new upload with a
  new address. This keeps addresses cacheable forever and means an address never
  starts showing different content.
- **Deleting a file that a page uses**: pages do not exist yet. The Pages feature defines
  what happens when a referenced file is deleted.
- **A future page whose address begins with the media path**: the media path is
  reserved on the public surface, so a page can never shadow it and it can never shadow
  a page.
- **Installation behind a reverse proxy with a base path**: public media addresses
  include the configured base path.
- **Many uploads at once**: each is handled independently. Uploading at the maximum size
  does not make other admin requests unusable (SC-008).
- **Upgrade from feature 002**: every site starts with an empty library, and nothing
  existing changes.
- **Several instances behind a load balancer**: every instance must see the same storage
  location for media to work. With the local provider, that means a location shared by
  all instances. Until a shared provider exists, the documentation does not advertise
  multi-instance media.

## Requirements *(mandatory)*

### Functional Requirements

**Media library**

- **FR-001**: Each site MUST have its own media library. Every media file MUST belong to
  exactly one site, and no operation in this feature may move or share a file between
  sites.
- **FR-002**: For each media file, the system MUST record its site, an identifier that is
  hard to guess, its original file name as cleaned for display, its validated type, its
  size in bytes, its upload time and the user who uploaded it.
- **FR-003**: A site member with the matching capability MUST be able to list a site's
  library, newest first, a page at a time, with each file's details. Listing MUST return
  only that site's files.
- **FR-004**: A site member with the matching capability MUST be able to delete a file
  after confirming. Deletion MUST remove the file from the library and stop it being
  served, on every instance, before the operation reports success. The stored bytes MUST
  then be removed by a background job without operator action, idempotently and safely
  with several instances running. Deletion MUST be permanent: this feature offers no
  restore, and a deleted file can only be recovered from the operator's backups.
- **FR-005**: A file's contents MUST never change after upload. The product MUST NOT
  offer a replace-contents operation in this feature.

**Upload and validation**

- **FR-006**: The system MUST decide a file's type by inspecting its contents. It MUST
  NOT trust the file name or the type declared by the client. The type found MUST be on
  the allowed list, and the file name's extension and the declared type MUST agree with
  it, or the upload MUST be refused.
- **FR-007**: The built-in allowed list MUST contain exactly JPEG, PNG, GIF, WebP and
  AVIF images and PDF documents. Operators MAY narrow the list through configuration but
  MUST NOT be able to add types outside it in this feature. SVG, HTML, script and
  executable types MUST NOT be accepted.
- **FR-008**: The system MUST enforce a maximum file size, configurable by the operator,
  with a default of 20 MiB. It MUST stop receiving an upload as soon as the upload passes
  the limit, and MUST refuse empty files.
- **FR-009**: Refusals MUST state a machine-readable reason (too large, empty,
  unsupported type, type mismatch, storage unavailable), and the admin shell MUST show
  each as a clear message.
- **FR-010**: An upload MAY contain several files. Each file MUST be validated and stored
  independently, and the result for each MUST be reported separately.
- **FR-011**: A file MUST appear in the library, and be served, only after its bytes have
  been completely and durably stored. A failed or interrupted upload MUST leave no
  library entry and nothing that can be served.
- **FR-012**: Bytes left in storage without a library entry MUST be removed by a
  background job after a configurable grace period (default 24 hours), without ever
  removing bytes that belong to an entry or to an upload still in progress.

**Serving**

- **FR-013**: Each media file MUST have a public address on the public surface of its
  site, reachable from the moment the file is stored. Media MUST NOT have a private or
  draft state in this feature. The address MUST contain the file's identifier and MAY
  contain its cleaned name for readability. The address MUST include the configured base path, and it MUST NOT
  depend on where or how the bytes are stored.
- **FR-014**: A public media request MUST be served only when the identifier belongs to
  a file of the site resolved from the request's host name. In every other case,
  including another site's file, a deleted file, an identifier never issued or an extension
  that does not match the file's, the response MUST be the same not-found response. That response
  MUST reveal nothing about other sites.
- **FR-015**: The path prefix used for media addresses MUST be reserved on the public
  surface and documented alongside the administrative prefixes from feature 001, so
  that no page can ever take it.
- **FR-016**: Files MUST be served with the type validated at upload, with an instruction
  that forbids browsers from guessing another type, and in a way that prevents the file
  from running script with the site's privileges. Serving MUST NOT execute, interpret or
  transform the file on the server.
- **FR-017**: Because contents never change (FR-005), served files MUST be marked as
  cacheable for a long period. Conditional requests and requests for part of a file MUST
  be supported.
- **FR-018**: Site members MUST be able to preview files, including those of sites with
  no host name yet, through an authenticated administrative operation. The preview MUST
  follow the same type and safety rules as public serving.

**Storage abstraction and the local provider**

- **FR-019**: Media MUST reach stored bytes only through a storage abstraction. Nothing
  outside a provider may know or depend on where or how bytes are physically kept, so
  that an S3-compatible or Azure Blob provider can be added later without changing the
  media library.
- **FR-020**: The abstraction MUST offer only what this feature needs: store a new
  file's bytes under a key, read them in full or in part, check whether they exist,
  delete them, and list stored keys for the clean-up job in FR-012. Every key MUST begin
  with the owning site's identifier, so that all of a site's bytes can be found, backed
  up or removed together.
- **FR-021**: Keys MUST be generated by the system and MUST NOT contain any part of a
  user-supplied name. No input from a request, including crafted paths or encodings, may
  cause a provider to read, write or delete outside the site's own key space.
- **FR-022**: The storage provider MUST be selected by configuration, and the local file
  system MUST be the default and the only provider shipped in this feature. An unknown
  provider name MUST fail start-up with a message listing the supported names.
- **FR-023**: The local provider MUST keep bytes under one configurable directory. It
  MUST refuse to start if that directory cannot be created or written, or if it lies
  inside the application's own files or inside any location the application serves
  static files or runs code from.
- **FR-024**: The abstraction MUST come with a conformance test suite that exercises
  every operation in FR-020, including partial reads, missing keys, concurrent writes
  and key isolation between sites. The local provider MUST pass it, and future
  providers MUST be able to run it unchanged.

**Authorization**

- **FR-025**: The media module MUST add named capabilities, in the capability model from
  feature 002, for viewing a site's library, uploading to it, deleting any of its files
  and deleting files one uploaded oneself. All four MUST be site-scoped. Administrator
  holds them on every site. Designer holds all four on the sites they are granted.
  Editor holds view, upload and delete-own on the sites they are granted, but not
  delete-any. Deleting a file MUST be allowed only with delete-any, or with delete-own
  when the requesting user is the file's recorded uploader.
- **FR-026**: Every media administrative operation MUST declare its capability, and MUST
  follow feature 002's rules: not found for a site the user may not see, forbidden for a
  permitted site where the capability is missing. Public media serving MUST be listed
  among the explicitly anonymous operations.

**Admin shell**

- **FR-027**: The admin shell MUST provide a media library screen for each site that the
  user can see. It MUST let the user upload by choosing files or dragging them in, with
  progress and a per-file result, and it MUST provide a paged list with image previews,
  file details, a copy-address action and deletion with confirmation. It MUST show
  usable empty, in-progress and error states. The upload area MUST tell users, before
  they upload, that files become public straight away and that embedded details such as
  a photo's location are published unchanged with the file.
- **FR-028**: The media screens MUST meet the translation-lookup and accessibility
  baselines from features 001 and 002. Every control and every per-file upload result
  MUST be usable by keyboard alone and announced to assistive technology.

**Operation, data safety and privacy**

- **FR-029**: The reference deployment MUST keep the default media location on
  persistent storage that survives replacing the application image. The configuration
  documentation MUST list every new setting with its default. The operations
  documentation MUST cover backing up, restoring and moving media together with the
  database.
- **FR-030**: Log output MUST refer to media files by identifier and site only. Original
  file names and file contents MUST NOT appear in logs.
- **FR-031**: A default installation MUST still make zero outbound network requests,
  including while uploading, serving and cleaning up media.
- **FR-032**: The release smoke test MUST be extended to upload an image to a site and
  fetch it from that site's public address.
- **FR-033**: The recorded details of each file MUST be enough for a later site export
  to produce a media manifest (constitution Principle III) without changing what is
  stored.

### Key Entities

- **Media File**: one uploaded file in one site's library. It has a site, a
  hard-to-guess identifier, a display name, a validated type, a size, an upload time, the
  uploader and a storage key. Its contents never change, and it exists from the moment
  its bytes are durably stored until it is deleted.
- **Storage Key**: the system-generated location of a file's bytes within the storage
  provider. It always begins with the site's identifier and never contains user input.
- **Storage Provider**: the configured place where bytes live. The local file system is
  the only provider in this feature. S3-compatible and Azure Blob providers are expected
  later.
- **Allowed Type**: a file type that uploads may have, recognised from the file's
  contents and paired with the extensions and declared types that must agree with it.
- **Media Capability**: a named, site-scoped permission (view, upload, delete any file or
  delete one's own uploads)
  registered in feature 002's capability model.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a fresh reference installation, with no storage configuration, a site
  member uploads an image and opens it at its public address in under 1 minute.
- **SC-002**: Across a test set of at least 50 crafted files, 100% of files whose
  contents are not an allowed type are refused. The set includes scripts and HTML
  renamed as images, SVG, executables, polyglot files, files with a mismatched name or
  declared type, empty files and oversized files. 100% of valid files in the set are
  accepted.
- **SC-003**: Across the automated test suite, zero media requests return a file that
  belongs to a site other than the one resolved from the host name. A request for
  another site's file is indistinguishable from a request for a file that never
  existed.
- **SC-004**: Across the automated test suite, zero uploaded files are served in a way
  that allows a browser to treat them as a page or to run their script on the site.
  This is checked for every allowed type and every crafted file in SC-002.
- **SC-005**: After replacing the application image on the reference deployment, 100% of
  previously uploaded files and their details are present, and every file serves
  byte-for-byte identical content.
- **SC-006**: Across interrupted and failed uploads induced in automated tests, zero
  partial files are ever listed or served, and 100% of leftover bytes are gone after the
  first clean-up run past the grace period.
- **SC-007**: A deleted file's public address answers not found on the very next
  request, on every instance.
- **SC-008**: On the reference environment (two cores, four gigabytes of memory), a 10 MB
  upload completes within 5 seconds over a local network. With 10 uploads at the maximum
  size in progress at once, the 95th-percentile response time of other admin requests is
  no more than twice its value on an idle server.
- **SC-009**: On the reference environment, with 50 requests at once for a 1 MB image,
  95% of responses begin within 100 milliseconds.
- **SC-009a**: On the reference environment, with 10,000 files in one site's library,
  every page of the library, including the last, loads in the admin shell within
  1 second.
- **SC-010**: 100% of storage misconfigurations covered by FR-022 and FR-023 stop
  start-up with a message that names the setting.
- **SC-011**: The local provider passes 100% of the storage conformance suite.
- **SC-012**: No original file name or file content appears in log output across the
  full test suite, verified by an automated check.
- **SC-013**: The media journeys (upload, browse, copy address, delete) are completable
  with the keyboard alone, and 100% of their visible text resolves through the
  translation lookup.

## Scope

### In Scope

- The Media module, with a per-site library and its capabilities.
- Upload with content-based type validation, size limits and per-file results.
- The storage abstraction, its conformance test suite and the local file system
  provider, selected by configuration.
- Serving files on each site's public surface from a reserved, non-executable path, with
  site isolation and safe serving rules.
- Authenticated preview for site members.
- Listing and deleting media, and the background clean-up of orphaned and deleted bytes.
- The admin shell media screens.
- Configuration and operations documentation, the reference-deployment storage volume
  and the smoke-test extension.

### Out of Scope

- Image transformation of any kind: resizing, cropping, format conversion, thumbnails
  generated on the server and removing embedded metadata such as camera location.
  Previews use the original file.
- CDN integration and signed or expiring URLs.
- The S3-compatible and Azure Blob providers. They follow as separate features that
  implement this abstraction and pass its conformance suite.
- SVG, video, audio, fonts and any other type outside the allowed list in FR-007.
- Folders, tags, search, renaming, alternative text and other descriptive details.
  Alternative text belongs with the feature that places images on pages.
- Tracking where files are used, and protecting files that pages use from deletion.
- Replacing the contents of an existing file.
- A recycle bin or restoring deleted files.
- Per-site storage quotas.
- Virus or malware scanning.
- Private or draft media: every stored file is reachable at its public address.
- Site export and import. FR-033 only ensures that the stored details are sufficient
  for it.

## Assumptions

- **Uploaded files are public as soon as they are stored**: the constitution's
  draft-and-published separation covers pages, components and collection items, not
  media files. A file is reachable by anyone who knows its address, which contains a
  hard-to-guess identifier and is never listed publicly. Site members are expected not
  to upload material that must stay confidential.
- **Every site role views and uploads media**: Designer and Editor both view and upload.
  Deletion is limited as decided in Clarifications: Editors can delete only their own
  uploads. The Pages feature may narrow deletion further once files can be referenced.
- **Default allowed types and size** (common web images and PDF, up to 20 MiB) cover
  typical site content. SVG is excluded because it can carry script, and supporting it
  safely needs its own decision.
- **Single server by default**: the local provider serves one instance, or several that
  share one directory. The documentation does not claim multi-instance support for
  media until a shared provider exists, which is consistent with exception E-4 in the
  001 plan.
- **Operators back up the media location with the database**: files and their details
  live in different places. The product documents how to keep them consistent but does
  not snapshot both together.
- **Library size**: a site's library is designed for up to 10,000 files (SC-009a).
  Larger libraries still work but have no performance target in this feature.
- **Upload and delete are content actions, not security events**: they are attributed
  (uploader, time) but not written to feature 002's security event record.

## Dependencies

- Feature 001 (foundation skeleton): site resolution from the host name, the
  administrative and public surface split and its reserved prefixes, site-scoped data,
  background jobs, configuration validation at start-up, the logging rules, the reference
  deployment and the smoke test.
- Feature 002 (identity and setup): authentication, the capability model, per-site role
  grants, the forbidden-versus-not-found rule and the check that every administrative
  operation declares its permission.
- The project constitution, in particular Principles III (upgrades never lose data), IV
  (pluggable infrastructure with a local default), V (media paths namespaced by site),
  IX (uploads validated and never executed) and XI (no speculative abstractions).
- GitHub issue #3, which this feature implements, which removes exception E-2 from the
  001 plan, and which the pull request closes.
