# Phase 0 Research: Media Library and Storage Abstraction

**Feature**: `003-media-storage` | **Date**: 2026-09-25 | **Issue**: #3

The stack is fixed by the constitution and by features 001 and 002. This document settles
the design questions the spec leaves to planning and justifies every dependency choice,
as Principle XI requires. There are no new packages.

Decision numbers are prefixed `M-` so they can't be confused with 001's `D-` or 002's
`R-` decisions, which still apply. 002's R-05 (declaring capabilities) and R-06 (proving
every endpoint declares one) matter most here.

---

## M-01: The storage abstraction lives in `Shared.Infrastructure`, and the keys belong to Media

**Decision**: `IFileStorage` and the local provider live in
`QuickBuilder.Shared.Infrastructure/Storage/`. The abstraction knows nothing about media,
sites or file types: it stores bytes under keys. The Media module decides what the keys
are (M-03) and what may be stored (M-07).

**Rationale**: Principle I puts "storage/cache providers" in `Shared.Infrastructure` by
name, next to the existing `ICacheProvider`. Keeping the interface free of media concepts
keeps the shared project generic, as the same principle requires. It also means a later
consumer, such as site export, can store its files through the same providers without
another abstraction.

**Alternatives considered**:
- *Put the abstraction inside the Media module*: this contradicts the constitution's
  placement. A future S3 provider would then have to live inside Media, and a second
  consumer would have to reference Media's implementation, which Principle I forbids.
- *A separate `QuickBuilder.Storage` project*: it adds a project with no rule that needs
  it. The constitution already names the home.

## M-02: The abstraction offers five operations and no paths or URLs

**Decision**:

```text
IFileStorage
  WriteAsync(key, content stream)      create-only and all-or-nothing; fails if the key exists
  OpenReadAsync(key)                   a seekable read stream plus length and last-modified, or null
  ExistsAsync(key)                     true or false
  DeleteAsync(key)                     idempotent; deleting a missing key succeeds
  ListAsync(prefix)                    async stream of (key, length, last-modified), committed files only
```

Partial reads are served by seeking the returned stream. The conformance suite (M-19)
requires `CanSeek` and checks that reads at arbitrary offsets return the right bytes.
The full contract is in [contracts/storage-provider.md](./contracts/storage-provider.md).

**Rationale**: these are exactly the operations FR-020 lists, and each maps directly to
S3 (`PutObject` with `If-None-Match: *`, `GetObject` with ranges, `HeadObject`,
`DeleteObject`, `ListObjectsV2`) and to Azure Blob storage (conditional upload, ranged
download, properties, delete, list blobs). Nothing in the interface assumes a file
system, so FR-019 holds by construction, not by review.

**Alternatives considered**:
- *Return a file path*: this is the most convenient option for the local provider and
  impossible for the other two. It is exactly the shape E-2 feared a guessed interface
  would have.
- *Return a public or pre-signed URL so the provider serves the bytes*: this is CDN-style
  delivery, which is out of scope. It would also bypass the site check (FR-014) and the
  safety headers (FR-016).
- *An explicit `ReadRangeAsync(key, offset, length)`*: ASP.NET Core's range handling
  already works over a seekable stream, including multi-range negotiation, conditional
  requests and `416` responses. A provider for a remote store can wrap ranged GETs in a
  seekable stream. Re-implementing range parsing in the Media module would duplicate
  framework code.
- *Overwrite on write*: files never change (FR-005). Create-only makes an accidental
  overwrite, for example after an id collision, an error rather than silent data loss.

## M-03: Keys are system-generated, site-first and constrained by a grammar

**Decision**: a media file's key is `{siteId:N}/media/{fileId:N}`, for example
`3f2c…9a1b/media/7d41…e0c2`, with no extension and no part of the user's file name. The
abstraction enforces a key grammar on every call: 1 to 8 segments separated by `/`, each
segment `[a-z0-9-]{1,64}`. A key that breaks the grammar throws. The local provider
also checks that the resolved full path lies under its root directory, as a second,
independent guard.

**Rationale**:
- It meets FR-020's "begins with the site's identifier", so all of a site's bytes are
  one prefix to back up or remove.
- The `media/` segment leaves room for other per-site consumers, such as
  `{siteId}/exports/…`, without collisions.
- The grammar makes FR-021 structural: `..`, separators, drive letters, encoded
  characters and user names cannot appear in a key, whatever arrives in a request.

**Alternatives considered**:
- *Keep the original file name in the key*: this is how path traversal and name
  collisions happen, and it puts user input on disk. It is forbidden by FR-021.
- *Fan out into two-character sub-directories*: 10,000 files in one directory is
  unremarkable for ext4, XFS and NTFS, and object stores have no directories. This can
  be added inside the local provider later without changing keys.

## M-04: The local provider writes to a temporary file and renames it into place, and cleans up its own leftovers

**Decision**:
- **Writing**: `LocalFileStorage.WriteAsync` streams to
  `{root}/{first key segment}/.incoming/{random}.part`, on the same file system and
  inside the site's own directory. It flushes to disk, then moves the file to its final
  path without overwriting, creating the directory if needed. Files are created with
  mode `0640` on Unix (no execute bits for anyone). A failure at any point deletes the
  `.part` file.
- **Listing**: `ListAsync` never returns anything under an `.incoming` directory, at any
  depth.
- **Cleanup**: a small hosted service inside the local provider walks
  `{root}/*/.incoming/`, one first-segment directory at a time, and deletes `.part` files
  older than one hour, at startup and hourly. This isn't part of the abstraction.

**Rationale**:
- A rename within one file system is atomic, so a key is either absent or complete.
  That makes FR-011 hold even if the process dies mid-write.
- Temporary files are namespaced by the key's first segment, which for media is the site
  id. That satisfies Principle V ("media paths are namespaced by SiteId") for bytes in
  flight too, and keeps each temporary file on the same file system as its target.
- Removing incomplete writes is a provider concern, and every back-end has its own
  mechanism: S3 has lifecycle rules that abort incomplete multipart uploads, and Azure
  discards uncommitted blocks after seven days. Exposing it in the abstraction would
  make the S3 and Azure providers implement something they don't need.
- One hour is far longer than any upload at the maximum size takes (SC-008). A `.part`
  file older than that belongs to a dead process.

**Alternatives considered**:
- *Write the final file directly*: a crash leaves a truncated file that the orphan sweep
  might not reach for 24 hours, and a racing reader could serve half a file.
- *Put temporary files in the system temp directory*: that is often a different file
  system (tmpfs in containers), so the move becomes a copy and loses atomicity.

## M-05: The provider is chosen by configuration, and bad storage configuration fails startup

**Decision**:
- **Provider selection**: `Storage:Provider` accepts `Local`, compared case-insensitively,
  and defaults to `Local`. Any other value fails startup with
  `Storage:Provider 'X' is not supported. Supported providers: Local.`
- **Local path checks**: `Storage:Local:Path` is validated by an `IValidateOptions`
  validator registered with `ValidateOnStart`, like every other option since 001. The
  path must be absolute. After symbolic links are resolved, it must not be the same as,
  or inside, the application directory (`AppContext.BaseDirectory`), the content root or
  the web root. The host supplies these three directories to `AddFileStorage`, so
  `Shared.Infrastructure` takes no dependency on ASP.NET Core. The directory is created if it is missing, and a probe file is written to
  `.incoming` and deleted.
- **Failure messages**: every failure names the key and the reason.

**Rationale**: FR-022 and FR-023 require these exact failures. It is also 001's pattern:
invalid configuration stops the process before it listens, and the message names the
offending key. The rule against application directories is what "never served from an
executable path" means for the local provider. It isn't enough that we don't serve that
directory: it must not be a place where the application's own files, the static-file
middleware or any runtime loader looks.

**Alternatives considered**:
- *Allow relative paths*: they resolve against the content root, which is `/app` in the
  image. That is inside the application directory, so the rule above would refuse them
  anyway. Rejecting them outright gives a clearer message.
- *Check writability lazily, at the first upload*: the operator would learn about a
  read-only volume from a user's failed upload rather than from startup.

## M-06: The default location is a persistent volume in the image, and a per-user directory elsewhere

**Decision**:
- **Dockerfile**: creates `/var/lib/quickbuilder/media` owned by the `app` user, sets
  `ENV Storage__Local__Path=/var/lib/quickbuilder/media` and declares
  `VOLUME /var/lib/quickbuilder/media`.
- **Reference compose file**: mounts a named volume, `media-data`, there, next to the
  existing `db-data`.
- **Outside a container**: when no path is configured, the default is
  `{LocalApplicationData}/QuickBuilder/media`, which is `~/.local/share` on Linux and
  `%LOCALAPPDATA%` on Windows.

**Rationale**: SC-001 needs uploads to work with no storage configuration, and SC-005
needs files to survive replacing the image. A named volume does both. A fresh named
volume copies ownership from the image directory on first mount, so the non-root `app`
user can write to it without any operator step. A per-user directory keeps local
development working without configuration and never lands inside the build output.

**Alternatives considered**:
- *Use a directory under `/app`*: it is inside the application directory, which M-05
  refuses and FR-023 forbids.
- *Store bytes in PostgreSQL*: this would work offline with no volume, but the database
  would carry every media byte in its backups and WAL. The constitution names the file
  system as the default provider.

## M-07: File types are detected from content by hand-written signature checks

**Decision**: a `FileTypeDetector` in the Media module reads up to the first 4 KiB of the
upload and recognises exactly six types:

| Type | Signature |
|---|---|
| JPEG `image/jpeg` | `FF D8 FF` |
| PNG `image/png` | `89 50 4E 47 0D 0A 1A 0A` |
| GIF `image/gif` | `GIF87a` or `GIF89a` |
| WebP `image/webp` | `RIFF` + 4 size bytes + `WEBP` |
| AVIF `image/avif` | an ISO-BMFF `ftyp` box at offset 4 whose major brand, or one of whose compatible brands, is `avif` or `avis` |
| PDF `application/pdf` | `%PDF-` at offset 0 |

Anything else is `unsupported_type`. The detector does not decode images or parse PDFs.

**Rationale**: six fixed signatures take a few dozen lines of code with unit tests. A
library would add a dependency (Principle XI) to recognise formats we must refuse anyway.
Decoding a file would be the start of an image pipeline, which is out of scope, and it
would put a complex parser on the upload path for no safety gain. Safety comes from how
files are served (M-11), not from proving that a file is a well-formed image.

**Polyglots**: a file that starts with a valid signature and also contains HTML or
script, such as a GIF/JavaScript polyglot, is accepted as its leading type. It can
never be interpreted as anything else, because serving pins the type and forbids
sniffing (M-11). The SC-002 test set therefore contains two kinds of polyglot:
- files that lead with a non-allowed format, which must be refused;
- files that lead with an allowed signature, which are accepted and are the core cases
  for SC-004.

**Alternatives considered**:
- *A MIME-detection or imaging library (ImageSharp, Magick.NET, a MIME-sniffer
  package)*: rejected. Licensing (ImageSharp's split licence), native binaries
  (Magick.NET) or breadth that brings in formats we must refuse. None adds safety over
  M-11.
- *Also scan the first KiB for `<script` or `<html` and refuse*: this guards against
  browsers that sniff content despite being told not to, and every browser the admin
  shell supports honours `nosniff`. It would also refuse legitimate PDFs and images that
  happen to contain those bytes.

## M-08: The file name and declared type must agree with the detected type

**Decision**:
- **Extension**: the extension of the cleaned file name (M-17) must be one of the
  detected type's extensions:

  | Type | Extensions |
  |---|---|
  | JPEG | `jpg`, `jpeg` |
  | PNG | `png` |
  | GIF | `gif` |
  | WebP | `webp` |
  | AVIF | `avif` |
  | PDF | `pdf` |

  Extensions are compared case-insensitively. A name with no extension is a mismatch.
- **Declared type**: the request's `Content-Type` must equal the detected type's canonical
  value. The only exceptions are an absent `Content-Type` and
  `application/octet-stream`, which count as "not declared". In that case the extension
  check alone decides.
- **Result**: a disagreement is refused with `media.type_mismatch`.

**Rationale**: FR-006 requires the three signals to agree. Browsers set `File.type` from
the extension and leave it empty for extensions the OS doesn't know. AVIF on older
systems is a real case. Treating "not declared" as neutral stops those users being
refused for their OS's ignorance, while a declared *contradiction* is still refused.

## M-09: Size limits are enforced before and while reading

**Decision**:
- **Setting**: `Media:MaxFileSizeBytes` defaults to 20,971,520 (20 MiB) and is validated
  to be between 1,024 bytes and 1 GiB.
- **Declared length**: a `Content-Length` above the limit is refused with `413
  media.too_large` before any body is read.
- **Actual length**: for every upload, the request's `IHttpMaxRequestBodySizeFeature` is
  set to the limit, which also covers chunked bodies that don't declare a length. A
  counting guard in the upload handler stops as soon as the byte count passes the limit,
  so FR-008's "stops receiving" holds with or without a declared length. The partial
  write is discarded (M-04).
- **Empty uploads**: a zero-length body is refused with `400 media.empty`.

**Rationale**: Kestrel's global body limit (30 MB by default) is neither configurable per
feature nor tied to our setting. Setting the per-request feature, through a filter
attribute on the upload action, is the framework's supported way to change the limit
for one endpoint. It keeps the controller thin.

## M-10: Uploads send one file per request, as the raw body

**Decision**: `POST /api/v1/media/sites/{siteId}/files` takes the file as the raw request
body:
- `Content-Type` is the declared type.
- The file name travels in `X-QuickBuilder-File-Name`, percent-encoded UTF-8. It is
  required, and a missing name is refused with `400 media.file_name_required`.

The admin shell uploads the files a user drops as parallel requests, at most three at a
time. It uses `XMLHttpRequest` for upload progress, sending the same
`X-QuickBuilder-Request` header and cookie handling as the rest of the API.

**Rationale**:
- One file per request makes FR-010's "each file validated and stored independently,
  result reported separately" automatic: each file gets its own status code and body.
- Streaming a raw body needs no multipart parser, and the size limit applies to exactly
  one file.
- The name goes in a header, not the query string, because query strings end up in
  proxy and access logs, and FR-030 keeps file names out of logs.
- `fetch` still has no upload-progress events. `XMLHttpRequest` does, and it needs no
  package.

**Alternatives considered**:
- *A multipart form with several files*: it needs streaming multipart parsing to avoid
  buffering 20 MB parts. One failed part also complicates the response, and every file
  counts against one body limit.
- *Resumable, chunked uploads (tus and similar)*: valuable for large video, which is out
  of scope. 20 MB over a LAN takes well under the 5 seconds in SC-008.

## M-11: Public files are served from `/_media/{id}.{ext}` with pinned types and a sandbox

**Decision**:
- **Address**: `GET|HEAD /_media/{fileId:N}.{ext}` on the public surface, for example
  `/_media/7d41…e0c2.jpg`. `/_media` is added to `RoutePrefixes` as a **public reserved
  prefix**: site resolution still runs, unlike the administrative prefixes, but no page
  may ever claim it. The Pages feature must refuse slugs starting with `_media`.
- **Endpoint**: an `[AllowAnonymous]` MVC controller in the Media module.
- **Lookup**: the file is read by id through the site-filtered query (`ISiteOwned`,
  001 FR-013), so the ambient site decides the result. The extension in the path must
  equal the file's stored extension.
- **Not found**: every miss, whether another site's file, a deleted file, an id never
  issued or a wrong extension, returns the same `404` problem body
  (`media.not_found`).
- **Response headers**:
  - `Content-Type`: the validated type, exactly.
  - `X-Content-Type-Options: nosniff`.
  - `Content-Disposition: inline; filename*=UTF-8''<cleaned original name>`.
  - `Cache-Control: public, max-age=31536000, immutable`.
  - `ETag: "<fileId:N>"` (strong) and `Last-Modified: <uploaded at>`.
  - `Accept-Ranges: bytes`. Ranges and conditional requests are handled by
    `File(..., enableRangeProcessing: true)`.
  - For images only: `Content-Security-Policy: default-src 'none'; img-src 'self';
    style-src 'unsafe-inline'; sandbox`.
- **Base path**: `UsePathBase` already prefixes every route with the configured base
  path (001 FR-012), so addresses include it with no extra work.

**Rationale**:
- `_media` is short, obviously system-owned, and unlikely to clash with a real page, a
  risk a plain `/media` would carry. It is reserved now, before Pages exists (FR-015).
- The id is the whole address. The cleaned original name is deliberately left out of the
  path, so the name never appears in access logs (FR-030) and the name-mismatch case
  collapses into the extension check. The name still reaches visitors who download the
  file, through `Content-Disposition`.
- **Why the headers make SC-004 hold**:
  - With the type pinned and `nosniff` set, no browser treats the bytes as HTML or script.
  - The CSP `sandbox` directive makes a directly opened image a unique-origin document,
    so even a hypothetical rendering bug couldn't touch the site's cookies or storage.
- **Why PDFs get no `sandbox`**: Chromium refuses to render PDFs in sandboxed documents,
  which would turn every PDF link into a blocked page. PDF viewers run document
  JavaScript, if at all, inside their own isolated viewer, never with the site's
  privileges. The quickstart includes a browser check with a PDF that contains
  JavaScript (see Risks in the plan).
- Immutable caching is safe because contents never change (FR-005). A new version is a
  new id.

**Alternatives considered**:
- *Serve through the static-file middleware from the storage directory*: this ties
  serving to the local file system (FR-019), bypasses the site check, and makes the
  storage directory a served web root, which is exactly what FR-023 forbids.
- *Always `Content-Disposition: attachment`*: this is the strongest isolation, but
  images opened from the library, and later from pages, would download instead of
  display. The CSP sandbox gives the same isolation for images.

## M-12: Admin preview uses the same safety rules and revalidates on every view

**Decision**: `GET /api/v1/media/sites/{siteId}/files/{fileId}/content` requires
`media.files.view`. It sends the same type, `nosniff`, disposition and sandbox headers
as M-11. The one difference is `Cache-Control: private, no-cache` with the same strong
`ETag`, so the browser revalidates, the server re-checks authorization, and an unchanged
file costs a `304`.

**Rationale**: FR-018 requires previews for sites without a host name, so preview cannot
use the public address. Revalidating keeps 002's rule that permission changes apply on
the next request (002 FR-017), without re-downloading 20 MB images. The admin shell's
existing CSP (`img-src 'self'`) already allows these previews, because they come from
its own origin.

## M-13: Deleting a file removes the row immediately and the bytes straight after, and the sweep catches the rest

**Decision**:
1. Delete the `media_files` row and commit. From this moment every instance answers not
   found, because nothing caches file metadata (M-14).
2. After the commit, call `IFileStorage.DeleteAsync(key)`. A failure is logged at
   warning level and not returned to the user.
3. Bytes left by a failed step 2 have no row. The orphan sweep (M-15) removes them on
   its next run: they are older than the grace period, because they were uploaded long
   before.

**Rationale**: this meets FR-004's order: the file is gone from the library and the
public address "before the operation reports success", and the bytes go "afterwards
without operator action". Deletion is permanent, per the clarification. It needs no
tombstone table or purge queue, because the sweep that FR-012 already requires covers
the failure case.

**Alternatives considered**:
- *Soft delete with a `deleted_at` column*: this was ruled out by the clarification
  that deletion is permanent. It would also leave every query needing another filter.
- *A purge-queue table drained by a worker*: this is more machinery for the same
  guarantee the orphan sweep already gives.

## M-14: File metadata is not cached

**Decision**: every public request and every preview reads its row by primary key. No
metadata cache sits in front of it.

**Rationale**: SC-007 requires a deleted file to disappear on the very next request on
every instance. The only cache abstraction is in-memory and cannot invalidate across
instances (001 exception E-4). One indexed primary-key lookup costs well under a
millisecond on the reference environment, far inside SC-009's 100 ms budget. The bytes
themselves are cached by browsers and intermediaries under M-11's immutable headers,
which is where caching pays.

## M-15: An hourly orphan sweep removes bytes that have no row

**Decision**: `MediaOrphanSweepService` in the Media module runs at startup and then
hourly:
1. Stream `ListAsync("")` once, keeping only keys shaped `{32 hex}/media/{32 hex}`.
2. Check those keys against `media_files.storage_key` in batches of 1,000.
3. Delete every key that has no row and whose last-modified time is older than
   `Media:OrphanGracePeriod` (default 24 hours, validated to be between 1 hour and 30
   days).
4. Total the counts by the key's first segment, and log one entry per site with its
   `SiteId`. File names never appear.

A single pass reads each object once. The abstraction has no prefix-listing operation,
and listing per site would have needed one, or would have read everything twice.

**Rationale**:
- **Correctness**: an upload writes bytes and then inserts its row within seconds. Bytes
  younger than the grace period may belong to an upload in progress, so they are never
  touched. Bytes older than it with no row can only be:
  - a crash between write and insert;
  - a failed post-delete cleanup;
  - a rolled-back insert.
- **Idempotence**: deleting an absent key succeeds (M-02), so two instances sweeping
  together at worst delete the same key twice. This is the same no-lock approach as 002's
  `IdentityRetentionService`.
- **Site namespacing**: every key the sweep deletes lies under one site's prefix, and
  its results are reported per site. That is how the constitution's "background jobs are
  namespaced by SiteId" applies to an installation-wide job.
- **Cross-site read**: the sweep reads rows across sites through `AcrossAllSites`, with a
  written reason and a dedicated test (001 FR-013a).

## M-16: The library pages by cursor, newest first

**Decision**:
- **Paging**: `GET /api/v1/media/sites/{siteId}/files?limit=50&cursor=…` orders by
  `(uploaded_at DESC, id DESC)` and returns up to `limit` items (default 50, maximum
  100), an opaque `nextCursor` (base64url of the last row's pair), and `totalCount`.
- **Index**: `(site_id, uploaded_at DESC, id DESC)` serves both the page and the count.
- **Uploader names**: resolved in one call to `IUserDirectory.FindManyAsync` (002
  contracts).
- **Missing bytes**: availability is checked with `ExistsAsync` for the page's items,
  eight at a time. `available: false` makes the screen show the file as unavailable
  (spec edge case), and the event is logged once per listing with the file id.

**Rationale**:
- Keyset paging makes "every file appears exactly once even while others are uploaded"
  (US3 #1) true by construction, which offset paging can't do.
- It keeps the last page of 10,000 files as cheap as the first (SC-009a).
- A count over one site's index range of 10,000 rows is a few milliseconds.
- 50 local `stat` calls cost less than a millisecond each. A future remote provider may
  need to batch these calls, and its spec can decide how.

**Alternatives considered**: *page numbers with `OFFSET`*: rows shift under concurrent
uploads, which breaks US3 #1, and cost grows with the page number.

## M-17: File names are cleaned for display and never used for storage

**Decision**:
1. Percent-decode the header.
2. Take the part after the last `/` or `\`.
3. Remove control characters (categories Cc and Cf) and trim whitespace and dots at both
   ends. Ill-formed UTF-16 is replaced with U+FFFD.
4. If the result is longer than 255 UTF-16 code units, shorten the stem so the whole name
   fits while the extension is kept.
5. If nothing remains, refuse with `media.file_name_required`.

*Changed during implementation*: the plan also normalised names to Unicode NFC. The
application builds with `InvariantGlobalization=true` (001), where `string.Normalize`
does nothing, and enabling ICU for the whole product (plus `icu-libs` in the Alpine
image) is out of proportion for tidier display. Names are therefore kept as given.
Normalisation was never a safety property: names are not used in keys or paths.

The result is stored as `file_name` and returned for display and in
`Content-Disposition`. It is never used in keys (M-03) or paths (M-11).

**Rationale**: this meets the spec's edge case for odd names, and names in any language
survive. Stripping format characters (Cf) removes right-to-left overrides, which make
`gpj.exe` display as `exe.jpg`.

## M-18: The four capabilities, and the delete-own check

**Decision**:

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `media.files.view` | Site | ✓ | ✓ | ✓ |
| `media.files.upload` | Site | ✓ | ✓ | ✓ |
| `media.files.delete` | Site | ✓ | ✓ | — |
| `media.own_files.delete` | Site | ✓ | ✓ | ✓ |

- **Declaration**: the delete endpoint declares `[RequireCapability("media.own_files.delete")]`,
  the weaker of the two.
- **Handler check**: when the caller isn't the file's `uploaded_by`, the handler also
  requires `media.files.delete` through `IAccessEvaluator.HasCapabilityAsync`. Otherwise
  it returns `403 identity.forbidden`.
- **Response order**: the handler loads the file (to learn its uploader) only after the
  declaration has confirmed the caller can see the site. The order of responses is
  therefore 404 for a site the caller can't see (declaration), 404 for a missing file,
  and 403 for someone else's file without `media.files.delete`.
- **`canDelete` flag**: each listed file carries `canDelete`, computed the same way, so the
  shell can hide the button (US3 #5).

**Rationale**:
- This follows 002's contract: names are `<module>.<noun>.<verb>`, every capability is
  site-scoped with `{siteId}` in the route, and Administrator is never listed because it
  holds everything.
- Declaring the weaker capability keeps "one declaration per action" (002
  authorization.md §1) and still fails closed for anyone without either capability.
- The ownership part can't be a declaration, because it depends on the row.

## M-19: One conformance suite, written against the abstraction

**Decision**:
- **Suite**: `FileStorageConformanceTests` is an abstract xUnit class in
  `QuickBuilder.IntegrationTests/Storage/`. A provider's suite derives from it and
  supplies a factory. `LocalFileStorageConformanceTests` is the first.
- **Coverage**: every operation in M-02, plus:
  - create-only behaviour;
  - all-or-nothing writes under a cancelled or failing input stream;
  - seekable partial reads at the start, middle and end;
  - missing keys;
  - idempotent delete;
  - listing by prefix, which never includes incomplete writes;
  - rejection of keys outside the grammar;
  - 20 concurrent writers on distinct keys;
  - isolation between site prefixes.
- **Local-only tests**: tests specific to the local provider (path containment, file
  mode, per-site `.incoming` placement and cleanup, startup validation) sit next to the
  suite, not in it.

**Rationale**: FR-024 and Principle X ("each optional infrastructure provider has its
own integration test suite"). Writing the suite against the interface, before a second
provider exists, is what stops the interface quietly acquiring local-file-system
assumptions.

## M-20: Tests that prove the success criteria

- **SC-002, the crafted-file set**: generated in test code, not committed as binaries, so
  every case is readable in review. It has at least 50 cases:
  - scripts and HTML renamed as `.jpg`, `.png` and `.pdf`;
  - SVG, including SVG with script;
  - PE (`MZ`), ELF and Mach-O headers;
  - ZIP and Office files;
  - polyglots of both kinds (M-07);
  - every allowed type with a wrong extension and with a wrong declared type;
  - names without extensions;
  - empty files;
  - files at the limit, one byte over, and far over with and without `Content-Length`;
  - one valid minimal file per allowed type.
- **SC-004**: a table-driven integration test fetches every accepted file from the
  crafted-file set, through both the public address and the preview. It asserts the
  exact `Content-Type`, `nosniff`, the disposition and, for images, the sandbox CSP. The
  quickstart adds a manual browser check.
- **SC-003**: two sites with files each, and every cross combination of host and id,
  compared byte for byte with a never-issued id.
- **SC-006**: fault injection through a test-only `IFileStorage` decorator and a
  database interceptor, then one sweep run under a moved `TimeProvider`.
- **SC-007**: the two-instance fixture from 002's revocation tests.
- **SC-005**: an upgrade test (`UpgradeFrom002Tests`) and the smoke test on a replaced
  image with the named volume.
- **SC-008, SC-009, SC-009a**: `Performance` category in `QuickBuilder.SmokeTests`, under
  002's `docker-compose.perf.yml` (2 CPU, 4 GB).
- **SC-012**: 002's log scanner gains canary file names: every test upload in the suite
  uses a name containing a unique marker, and the scanner fails if one appears in log
  output.

## M-21: The admin shell's media screen

**Decision**:
- **Route**: `/admin/sites/:siteId/media`, linked from each site in `SitesView`.
- **Components**: `features/media/` holds `MediaLibraryView`, `UploadArea` (a real
  `<button>` plus a drop zone, so keyboard users are never dependent on dragging),
  `MediaList` (a paged table with previews and a "Load more" button that moves focus to
  the first new row) and `DeleteDialog`.
- **Announcements**: per-file upload results go into a list with `aria-live="polite"`, so
  each success or refusal is announced (FR-028).
- **Copy address**: builds `{location.protocol}//{first host name}{publicPath}`. The
  admin shell and the sites sit behind the same proxy, so they share a scheme.
- **Privacy notice**: shown in the upload area before any upload (FR-027).
- **No new packages**: all strings go through the existing i18n lookup (FR-028), and
  vitest and axe checks are added as in 002.

## M-22: Readiness includes storage, through a provider health check

**Decision**: `/health/ready` gains a storage check through a provider-supplied
`IFileStorageHealthCheck.CheckAsync()`, under a 2-second timeout, reported as
`Storage: Healthy|Unhealthy`. An unhealthy check makes the instance not ready. The local
provider writes, reads back and deletes `{root}/.probe-{Guid:N}`, the same probe its
startup validation uses (M-05). A missing, unreadable or read-only directory therefore
makes the instance not ready.

The check is not part of `IFileStorage`: consumers never need it, and each back-end
proves its health differently. S3 would use `HeadBucket`, and Azure a
container-properties call.

**Rationale**:
- Startup validation (M-05) catches a bad location only at start. A volume that
  disappears or turns read-only later would otherwise leave an instance reporting ready
  while every upload fails. Principle II requires readiness to mean "able to serve", and
  storage is now part of that.
- `ExistsAsync` on a missing key can't serve as the probe. .NET's `File.Exists` returns
  `false` on permission and I/O errors instead of throwing, so an unreadable directory
  looks like "key not found". Nothing about a read-only volume shows until something
  writes.

**Alternatives considered**: *add a health operation to `IFileStorage`*: it would put a
non-storage concern on an interface every consumer depends on (M-02 keeps it to exactly
what consumers use).

## M-23: At most `ProcessorCount − 1` uploads write at once

*Added during implementation, from the performance run (tasks T081, T087).*

**Decision**: the upload handler takes a slot from a process-wide `UploadGate` before it
streams bytes into storage. The gate allows `max(1, ProcessorCount − 1)` uploads to write
at once. A waiting upload has read only the 4 KiB it needed for type detection, so TCP
backpressure holds the client; nothing is buffered on the server beyond the web server's
own per-request limit.

**Rationale**: with ten 20 MiB uploads at full speed on the reference limits (2 CPUs),
every other admin request slowed from p95 5 ms to 55 ms. SC-008 allows 2×. The gate leaves
a core for the rest of the admin surface, the same trade 002 made for password hashing
(002 research R-19). A single upload is unaffected (10 MB in 32 ms).

**What it does not fully fix, measured on the development workstation**: with the gate,
the slowdown during the burst is p95 27–28 ms against an idle 5–6 ms. That is half the
ungated figure, but still above 2×.
- **Not disk contention**: removing the `fsync` made it no better (40 ms).
- **What remains**: CPU and network contention inside Docker Desktop's single VM, where the
  load-generating client, PostgreSQL and the app share cores.

The measurement must be repeated on reference Linux hardware before this half of SC-008
can be called met (quickstart-results §9).

**Alternatives considered**:
- *Throttle each upload's throughput*: it would slow every upload to protect a rare burst.
- *Raise the thread-pool minimum*: measured, and it changed nothing (the cause was not
  starvation).

## New dependencies

None. No NuGet or npm package is added, and no third-party data file.

- **Signature detection**: hand-written (M-07).
- **Upload progress**: `XMLHttpRequest`, part of the browser (M-10).
- **Range handling**: ASP.NET Core (M-02, M-11).

## Configuration keys introduced

| Key | Default | Validation |
|---|---|---|
| `Storage:Provider` | `Local` | One of the supported providers (M-05) |
| `Storage:Local:Path` | Image: `/var/lib/quickbuilder/media`. Elsewhere: `{LocalApplicationData}/QuickBuilder/media` | Absolute; outside the app, content and web roots; creatable and writable (M-05) |
| `Media:MaxFileSizeBytes` | `20971520` (20 MiB) | 1,024 to 1,073,741,824 |
| `Media:AllowedTypes` | all six: `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `image/avif`, `application/pdf` | Non-empty; every entry one of the six (FR-007) |
| `Media:OrphanGracePeriod` | `1.00:00:00` (24 h) | 1 hour to 30 days |

All five go into `docs/configuration.md`, under new **Storage** and **Media** sections.
