# Research: Media Library, Image Variants and Remote Storage Providers

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #13

Decisions are numbered **MV-01 … MV-26** and referred to by number from the plan, the data
model and the contracts. Each one gives the decision, why, and what else was considered.

---

## MV-01 Where each part lives

**Decision**:
- **Media** owns everything about files: folders (MV-02), listing and search (MV-03),
  alternative text (MV-04), replace (MV-07), the public address grammar (MV-08), image
  metadata and variants (MV-09 … MV-14, MV-16), the change event (MV-17) and the storage
  consistency check (MV-21).
- **Pages** owns media *usage* (MV-05), because usage is a fact about page and component
  documents, which Pages owns. It records which drafts and versions reference which file
  and answers through a new `IMediaUsageReader` in `Pages.Contracts`.
- **Rendering** writes responsive image markup from what `IPublicMediaReader` returns
  (MV-15). It still has no tables.
- **Shared.Infrastructure** gains the S3-compatible and Azure Blob providers beside the
  local one (MV-18 … MV-20). **`IFileStorage` does not change**: the five operations 003
  defined are enough for variants, replace and the consistency check.

**Rationale**:
- Usage lives where the documents live, so it is updated in the same transaction as the
  draft save or publish that changes it. FR-013's "within 5 seconds" is met by being
  immediate rather than by an event pipeline.
- Principle I: Media reads usage through a contract, Pages reads files through a contract,
  and no transaction spans both.
- The constitution names Shared.Infrastructure as the home of storage providers.

**Alternatives considered**:
- **A usage projection in Media fed by Pages events**: a second copy that lags, needs a
  backfill and an idempotent handler for every draft save. It buys nothing Media needs.
- **Media parses page documents itself**: forbidden, since it would read Pages' tables.
- **Separate `QuickBuilder.Storage.S3` and `QuickBuilder.Storage.AzureBlob` projects**:
  would keep the SDKs out of Shared.Infrastructure's dependency graph, but the constitution
  lists exactly two shared projects. Doing it would need an amendment for a packaging
  concern. Recorded under Deferred in the plan.

---

## MV-02 Folders

**Decision**:
- A `media.folders` table: id (v7), site, optional parent, name, a normalised name key,
  creation time and creator (display only).
- Sibling names are unique ignoring case and accents, through a unique index on
  `(site_id, parent_id, name_key)` with `NULLS NOT DISTINCT` (PostgreSQL 15+; we require 16).
- **Structural changes serialise per site** through a `media.folder_trees` row per site,
  whose `xmin` is checked on every create, rename, move and delete of a folder. This is the
  same pattern as 010's `page_trees`. It is what makes "never move a folder into its own
  subfolder" and "at most 8 levels" safe when two members move folders at once. The loser
  of a race gets `media.folder_conflict` and the admin shell reloads the tree.
- **Limits**: depth 8 (FR-001) and a stated limit of **1,000 folders per site**. The
  handlers load a site's whole tree to check depth and cycles, which is cheap at that size.
- **Delete only when empty**: `media_files.folder_id` and `folders.parent_id` reference
  `folders` with `ON DELETE RESTRICT`. A file moved in during the delete makes the database
  refuse it, and the error maps to `media.folder_not_empty`. There is no check-then-act race.
- **Files** hold a nullable `folder_id`; null is the top level. Moving files is one
  `ExecuteUpdate` over at most 100 ids of the route's site.
- **No owner**: any member with `media.files.upload` may change any folder (clarification).

**Alternatives considered**:
- **Materialised paths or `ltree`**: faster subtree queries, but every move rewrites
  descendants, and `ltree` is an extension. At 1,000 folders an in-memory walk is enough.
- **A recursive CTE per request**: raw SQL for something the in-memory tree does.
- **A PostgreSQL advisory lock**: works, but it is raw SQL, and the row version does the
  same job through EF.

---

## MV-03 Listing, search, filters and sorting

**Decision**:
- **One listing endpoint** with `folderId`, `scope` (`folder` or `subtree`), `q`, `kind`
  (`image`, `document`), `sort` (`uploaded`, `name`, `size`) and `direction`.
  - Without `q`, it lists one folder, or the top level when no `folderId` is given.
  - With `q`, it searches the whole library by default (FR-006). `scope=subtree` limits the
    search to `folderId` and its descendants, which are computed from the in-memory tree
    (MV-02) and passed as `folder_id = ANY(@ids)`.
- **Search** matches a stored `search_text` column: the display name and alternative text,
  lower-cased with `ToLowerInvariant` and with accents removed (NFD, then non-spacing marks
  dropped), in .NET. The search term is normalised the same way. The query is
  `search_text LIKE '%' || @term || '%' ESCAPE '\'`, with `%`, `_` and `\` escaped, so the
  term is literal text.
  - At 10,000 files per site this is a filtered scan on the site index, measured well under
    SC-001's 1 second. No trigram index and no `unaccent` extension are needed.
- **Sorting** uses keyset paging per sort, with the file id as tie-breaker:
  - `(uploaded_at_utc, id)` is the existing index;
  - `(sort_name, id)`: `sort_name` is the normalised display name;
  - `(size_bytes, id)`.
  - Each index is prefixed with `(site_id, folder_id)`. The cursor encodes the sort and the
    last row's key, so a cursor from another sort is refused (`media.invalid_cursor`). A
    file uploaded while someone pages lands before their cursor, so every file still appears
    exactly once (003 M-16).
- **Each item** gains `folderId`, `altText`, `width`, `height`, `thumbnailPath`, a
  `usage` summary (MV-05) and `canReplace`. `TotalCount` becomes the count for the query.

**Alternatives considered**:
- **PostgreSQL full-text search**: stems words, so "brochures" finds "brochure" but
  "broch" does not. The spec asks for "contains".
- **`pg_trgm` with `unaccent`**: faster at hundreds of thousands of rows, but it needs two
  extensions and gives no gain at the stated scale. It is the documented next step if the
  limit ever grows.
- **Offset paging**: simpler, but files uploaded during browsing would shift pages.

---

## MV-04 Alternative text

**Decision**:
- A nullable `alt_text` column, at most 300 characters, on images only (FR-008). It is
  trimmed, and control characters are removed as for file names (003 M-17). Empty means
  none. It is edited with `PATCH …/files/{id}` under `media.files.upload`.
- **Copied, not linked** (FR-009, spec assumption): the editor copies it into the Image
  block's `alt` when a file is chosen for a block whose `alt` is empty. The page document
  never refers to library text, so a library edit never changes a draft or published page.
  This is purely an admin shell behaviour. The page schema is unchanged.
- It is part of `search_text` (MV-03). It never appears in logs (FR-041).

**Alternatives considered**:
- **The renderer falls back to library text when a block has none**: this changes
  published pages without a publish (Principle VII), and it adds a page schema change to
  tell "empty" apart from "inherit".

---

## MV-05 Usage tracking in Pages

**Decision**:
- **Two tables in `pages`**:
  - `draft_media_references (site_id, subject_kind, subject_id, media_id)`: maintained by
    `DraftWrites` on every page and component draft save, exactly like the existing
    `component_usages`. The page and component deletes remove them explicitly (PR-15
    pattern).
  - `version_media_references (site_id, version_id, media_id)`: written in the publish
    transaction for the new version, from the `DependencyExtractor` output already computed
    there. They are removed by cascade with the version, including by the 50-version
    retention prune.
- **Live, draft and history** are derived at read time:
  - **live**: a version reference whose version is its publication's `live_version_id`;
  - **draft**: a draft reference;
  - **history**: a version reference that is not live.
  - A component's live use also reports how many live pages show it, from
    `live_dependencies` (page → component).
- **`IMediaUsageReader`** in `Pages.Contracts` (contracts/readers.md §2):
  - `SummariseAsync(siteId, mediaIds)` returns place counts per file and whether any place
    is live. It is used by the listing (one query for a page of up to 100 files), and by
    delete and replace.
  - `ListAsync(siteId, mediaId)` returns the places: kind, id, title, and draft, live and
    history flags.
- **Naming**: the places are named only when the caller holds `pages.content.view` on the
  site (FR-014). Every site role holds it today, but the rule is enforced anyway. The
  capability's name moves into `Pages.Contracts` (as `PageContentCapabilities.View`) so
  that Media can ask for it. The name itself is unchanged.
- **Backfill on upgrade** (FR-013): the `AddMediaReferences` migration fills both tables
  from existing `page_drafts`, `component_drafts` and `published_versions`, using
  `jsonb_path_query(document, 'lax $.**.media.mediaId')`, keeping only values that are
  UUIDs. `MediaReferenceBackfillTests` asserts that on the golden fixtures the migration
  finds exactly what `DependencyExtractor` finds, so the SQL and the C# cannot drift.

**Rationale**:
- The update is in the same transaction as the change, so usage is never stale, which
  satisfies SC-008 without any event.
- Recording history references at publish avoids scanning up to 100,000 version documents
  to answer "is this file in a history version?".

**Alternatives considered**:
- **Reusing `live_dependencies`**: it covers live versions only. Drafts and history would
  still need tables.
- **A C# backfill job at start-up**: the same result, but it needs a marker, a lease and
  idempotency. The migration is atomic and runs where the schema change runs.

---

## MV-06 Delete and replace: warnings and live-use authorization

**Decision**:
- **Warning enforced by the server** (FR-015): `DELETE …/files/{id}` on a file with any
  place (draft, live or history) returns `409 media.in_use` with the usage summary, unless
  the request carries `?confirmInUse=true`. The admin shell shows the named warning, then
  retries with the flag. A file with no place is deleted as in 003.
- **Live use needs publishing** (clarifications): when `SummariseAsync` reports live use,
  delete and replace also require `pages.publishing.manage` (`PublishingCapabilities.Manage`,
  already in `Pages.Contracts`) on the site. Without it the answer is
  `403 media.in_live_use`, even for the uploader. The endpoint still declares
  `media.own_files.delete`. The row-dependent rules (another person's file, live use) are
  checked in the handler, as 003 M-18 does.
- **Uploader rule** (clarification): `uploaded_by` never changes. Replacing records
  `replaced_by` and `replaced_at_utc` separately.
- **Race accepted**: a Designer may publish a page using a file at the moment an Editor
  deletes it. The page then renders without the image (012 FR-027), which is the same safe
  outcome as any deleted image. Closing the race would need a transaction spanning Media and
  Pages (Principle I forbids it) or a distributed lock. It is recorded under Risks.

**Alternatives considered**:
- **Enforcing the warning only in the admin shell**: FR-015 requires the server to refuse.
- **A new capability "delete live media"**: the clarification ties the rule to publishing,
  so reusing the publishing capability keeps "who can change what visitors see" in one
  place.

---

## MV-07 Replace

**Decision**:
- **Content versions**: `media_files.content_version` starts at 1. A replacement increments
  it, and its bytes go to a new storage key (MV-08, MV-14). Storage stays create-only.
- **Sequence** in `ReplaceFileHandler`:
  1. Authorize: MV-06, plus the rule that the replacement must be the same kind (image or
     document) (FR-017).
  2. Validate and store the new bytes through 003's upload pipeline (type detection, size
     gate, `UploadGate`), then read image metadata (MV-10).
  3. In one transaction, update the row: type, extension, size, dimensions,
     `content_version`, `storage_key`, `replaced_at_utc`, `replaced_by`, `search_text`,
     variant state reset. Add `Media.FileChanged(reason: replaced)` to the outbox. The
     row's `xmin` guards against a concurrent replace, move or delete, and the loser gets
     `409 media.conflict`.
  4. Delete the old original's key (best effort). The orphan sweep removes it if that fails
     (003 FR-012), and the variant sweep removes old variants (MV-14). Queue the new
     thumbnail (MV-13).
- **Failure** at steps 1–3 leaves the row, and so the current contents and address,
  untouched (FR-020). New bytes stored before a failed commit are orphans, removed by the
  sweep.
- **Kept**: id, display name, folder, alternative text, uploader and upload time
  (clarification, FR-017). The display name deliberately stays, even if the new file's
  name differs.
- **Addresses**: the new version gets a new address (MV-08). The old one no longer matches
  the row, so it is not found from the commit on, on every instance, because every request
  reads the row.

**Alternatives considered**:
- **Overwriting the bytes under the same key**: breaks create-only storage, lets caches
  serve old contents at the same address for a year (003 FR-017), and is not atomic on S3
  if the process dies mid-write.
- **Replacement as a new file plus rewriting every reference**: would change drafts and
  published documents, which Principle VII forbids without a publish.

---

## MV-08 Public address grammar

**Decision**: one canonical address per content version and width, all under the existing
`/_media` prefix:

| What | Address | Example |
|---|---|---|
| Original, version 1 | `/_media/{id}.{ext}` | `/_media/7d41….jpg` (unchanged from 003) |
| Original, version n ≥ 2 | `/_media/{id}-v{n}.{ext}` | `/_media/7d41…-v2.png` |
| Variant | `/_media/{id}-v{n}-w{width}.{ext}` | `/_media/7d41…-v1-w640.jpg` |

- `{id}` is 32 lower-case hex digits, `{n}` is 1–99,999, `{width}` is one of the configured
  widths, and `{ext}` is the version's stored extension.
- Anything else is the same `media.not_found`, before or after the lookup. That includes a
  non-canonical form such as `-v1` on an original, a stale version, an unconfigured width,
  a variant of a non-image, of a GIF or of an animated image, or a variant of a file whose
  variants are marked unavailable (MV-12).
- **Compatibility**: every address 003 issued stays valid while its file is at version 1
  (FR-045, SC-012).
- **Headers**: variants get exactly the original's headers from 003 M-11: the validated
  type, `nosniff`, the sandboxing CSP for images, `immutable` long caching, ETag and range
  support. The exception is MV-12's failure fallback.

**Alternatives considered**:
- **Query strings (`?w=640&v=2`)**: caches and CDNs often ignore or strip query strings,
  and a free-form parameter invites arbitrary sizes. Path segments are exact.
- **Folders in the address**: FR-003 forbids addresses that depend on folders.

---

## MV-09 Image processing library: NetVips

**Decision**:
- Use **NetVips** (MIT), the .NET binding for **libvips**, with **NetVips.Native**
  runtime packages (libvips and its bundled codecs, LGPL-3.0-or-later, dynamically loaded)
  for `linux-musl-x64`, `linux-musl-arm64` (the Alpine runtime image, both architectures of
  Principle II's multi-arch images), `linux-x64` and `win-x64` (development and CI).
- **Why**:
  - It decodes and encodes all four variant formats, including AVIF through libheif. It
    streams (demand-driven, low memory), and shrinks on load for JPEG, so a 6,000-pixel
    photo resizes in well under a second on 2 vCPU.
  - Its licences are compatible with redistribution in an MIT product: the LGPL libraries
    are shipped unmodified as separate shared objects.
  - It is maintained, and it is the same libvips that powers `sharp`.
- **Hardening**: untrusted input is decoded by native code, so:
  - `NetVips.BlockUntrusted = true`, and every loader except JPEG, PNG, GIF, WebP and HEIF
    is blocked with `Operation.Block`. There is no PDF, SVG, TIFF or `magick` decoding.
  - The libvips operation cache is off (`Cache.Max = 0`), and per-operation threads are
    capped (`NetVips.Concurrency = 1`). Parallelism comes from MV-12's bounded pool.
  - Decoding only ever starts after 003's content-type detection has accepted the file and
    MV-10's pixel limit has passed.
  - Each production runs under a timeout (MV-12).
- **Start-up self-check**: the module asks libvips whether `heifload` and `heifsave` with
  AV1 are available. If not (a trimmed native build), AVIF files are treated like animated
  images: no variants, originals served, one warning logged at start-up. The product works
  either way, and the first task in tasks.md, a spike, confirms the shipped packages include AVIF.

**Alternatives considered**:
- **ImageSharp**: fully managed and pleasant, but the Six Labors Split License requires a
  paid licence for some commercial users. That is not compatible with an MIT product
  redistributed to anyone.
- **SkiaSharp** (MIT): no AVIF decoding, and no shrink-on-load.
- **Magick.NET** (Apache-2.0): complete, but slower, much larger, and ImageMagick's history
  of decoder vulnerabilities is long. Its broad format support is a liability here.
- **Writing decoders ourselves**: no.

**Spike result** (task T004, 2026-10-01): NetVips 3.2.0 with NetVips.Native 8.18.7 (libvips
8.18.7).
- **Loaders and savers**: `jpegload`, `pngload`, `gifload`, `webpload` and `heifload`, plus
  `heifsave` with AV1, `jpegsave`, `webpsave` and `pngsave`, are all present on `win-x64`, on
  `linux-musl-x64` in `sdk:10.0-alpine`, and in the runtime image `aspnet:10.0-alpine`.
  AVIF is therefore available, and the degraded mode of this decision is a safety net, not
  the expected state.
- **Also compiled in**: `svgload` is present, and `pdfload` and `magickload` are absent.
  So blocking loaders is necessary, not theoretical.
- **Timings**: on `linux-musl-x64` in Docker limited to `--cpus 2 --memory 4g` (the
  reference budget), resizing a 6,000 × 4,000 photo-like original to 320 and 1,920 px with
  MV-11's settings took:

  | Format | 320 px | 1,920 px |
  |---|---|---|
  | JPEG (22 MiB) | 166 ms | 312 ms |
  | PNG (65 MiB) | 224 ms | 290 ms |
  | WebP | 582 ms | 784 ms |
  | AVIF | 1,288 ms | 1,427 ms |

  Every format is inside SC-004's 3 s, and AVIF with effort 2 is the slowest.
- **arm64**: not run. This Docker host has no arm64 emulation. The `linux-musl-arm64`
  package is the same libvips build, and the multi-arch image build in CI exercises it.
- **Dockerfile**: the portable publish copied natives for all four RIDs. The build stage now
  keeps only `runtimes/linux-musl-{x64|arm64}` according to `TARGETARCH`, which leaves
  17.8 MB, and fails the build if `libvips` is missing.

---

## MV-10 Image metadata at upload, and the pixel limit

**Decision**:
- After 003's pipeline has stored the bytes and before the row is committed, the upload
  (and replace) handler opens the stored object and reads the **header only** through
  libvips, without decoding pixels. From it the handler records:
  - `width` and `height` **as displayed**: swapped when the EXIF orientation is 5–8;
  - `is_animated`: more than one page or frame (GIF, WebP, AVIF sequences).
- **Pixel limit** (FR-027): if `width × height` exceeds `Media:MaxImagePixels` (default
  100,000,000), the upload is refused with `media.dimensions_too_large`, and the stored
  bytes are deleted straight away. The orphan sweep covers a crash.
- **Unreadable header** (revised during implementation): when libvips cannot read the header
  although 003's content detection accepted the file, the upload is **kept** with
  `metadata_state = unreadable`. It is served as its original and gets no variants. Refusing
  it would have turned files 003 accepts, such as a photo with a damaged EXIF block, into
  new upload failures. Nothing is decoded either way, so this weakens no safety property.
- PDFs get no dimensions.
- **Existing images** (FR-027): `ImageMetadataBackfillService` lists the sites that have
  rows with `metadata_state = pending`, then works **one site at a time** (Principle V:
  background jobs are namespaced by site), in batches of 50. For each row it records the
  dimensions, or marks
  the row as over the limit or unreadable. Over-limit and unreadable files keep being
  served as originals, with no variants. It queues the thumbnail (MV-13) and emits
  `Media.FileChanged(reason: dimensions)` so live pages pick up `width` and `height`
  (MV-17). It is idempotent: two instances may process the same row, with the same result.

**Alternatives considered**:
- **Parsing headers ourselves**: JPEG SOF markers can sit after large EXIF segments, and
  AVIF needs ISO-BMFF box parsing. libvips already does both correctly.
- **Refusing existing over-limit images**: 003 accepted them. Refusing them now would break
  pages that show them.

---

## MV-11 Variant widths, formats and encoding

**Decision**:
- **Widths**: `Media:Variants:Widths`, default `[320, 640, 960, 1280, 1920, 2560]`. It must
  hold 1–10 ascending values from 64 to 4,096, validated at start-up. **The smallest is the
  thumbnail width** (MV-13). Changing the list only affects which addresses are valid.
  Variants for removed widths become unreachable and are swept (MV-14).
- **Eligible**: JPEG, PNG, WebP and AVIF images that are not animated, whose metadata is
  recorded and within the limit, and whose variants are not marked unavailable. GIFs never
  get variants (FR-021).
- **Produced width**: only widths **smaller than** the displayed width. The original serves
  as its own largest size, so nothing is ever enlarged (FR-021).
- **Pipeline** (libvips `thumbnail`, shrink-on-load):
  1. auto-rotate from EXIF (FR-023);
  2. import the embedded ICC profile and convert to sRGB;
  3. resize to the width, keeping the aspect ratio;
  4. keep alpha;
  5. save with **no metadata** (`keep: none`: no EXIF, GPS, XMP or ICC; FR-023).
- **Same format as the original** (spec assumption), with fixed settings:
  - JPEG: quality 80, progressive, optimised Huffman tables;
  - PNG: compression 6, adaptive filtering;
  - WebP: quality 80, effort 4;
  - AVIF: quality 55, effort 2. AVIF encoding is slow, and a low effort keeps SC-004's
    3 seconds.
  - Settings are not configurable (Principle XI). One constant table is covered by golden
    size tests.

**Alternatives considered**:
- **Converting everything to WebP or AVIF with `Accept` negotiation**: explicitly out of
  scope. It also makes caching vary by header.
- **Arbitrary widths requested by the page**: out of scope. A fixed set bounds storage and
  work.

---

## MV-12 Producing variants: on demand, exactly once, bounded

**Decision**:
- **Lookup order** for a variant request, public or admin:
  1. parse the address (MV-08);
  2. read the row, site-filtered, and check version, width and eligibility;
  3. `OpenReadAsync(variantKey)`: on a hit, serve it. This is the whole hot path: one
     indexed row read and one storage read (SC-004).
- **On a miss**, produce:
  - **In-process single-flight**: a `ConcurrentDictionary<(file, version, width), Task>`,
    so concurrent requests on one instance share one production.
  - **Across instances**: a claim row in `media.variant_claims (file_id, content_version,
    width, claimed_at_utc, instance_id)`, primary key on the first three columns.
    - Inserting the claim wins the production. A unique violation means another instance
      is producing it.
    - A claim older than the lease (twice the production timeout) is taken over with a
      conditional `ExecuteUpdate`. This only happens after a crash.
    - The winner writes the bytes with `WriteAsync` (create-only) and then deletes the
      claim.
    - The losers poll `ExistsAsync` every 200 ms until the lease expires, then serve the
      stored variant.
    - Together these give SC-005's "exactly once" for 100 simultaneous requests across two
      instances.
- **Bounded**: a semaphore of `Media:Variants:MaxConcurrent` productions per instance
  (default: half the processor count, at least 1). Each production has
  `Media:Variants:Timeout` (default 10 s) (FR-029). Waiting for the semaphore counts
  against the timeout.
- **On failure or timeout**:
  - the request gets the **original bytes at the variant address with
    `Cache-Control: no-store`**, so the image still shows and nothing caches the wrong
    content;
  - the row is marked `variants_state = unavailable`, and `Media.FileChanged(reason:
    variants)` is emitted. Live pages then re-render without `srcset` (MV-15, MV-17), and
    later requests for any variant of that version are `not_found`;
  - the event is logged with the file id only.
  - A replacement resets the state.
- **Produced bytes** are held in memory. Even the largest variant stays well under 10 MB.
  They are written to storage, then served from memory for that request.

**Alternatives considered**:
- **PostgreSQL advisory locks**: raw SQL, and a session lock would pin a pooled connection
  for the whole production.
- **Relying on create-only writes alone**: correct content, but two instances would both
  do the work (it would still be produced twice), which fails SC-005.
- **Serving the original with long caching on failure**: browsers would keep the original
  at the variant's address forever.

---

## MV-13 Eager thumbnails

**Decision** (clarification: thumbnail eagerly, the rest on demand):
- After an upload or replace commits, the handler enqueues `(site, file, version)` on an
  in-process bounded `Channel` (capacity 1,000; when it is full the item is dropped).
- `ThumbnailWorker`, a hosted service, produces the thumbnail width through MV-12's code
  path, including claims and the semaphore. The upload's response does not wait
  (FR-022). The backfill (MV-10) uses the same queue.
- **Loss is harmless**: if the instance stops or the queue is full, the first request
  produces the thumbnail on demand.
- **Target**: ready within 5 seconds of the upload completing (FR-028). This is measured by
  `ThumbnailLatencyTests` with a 20 MiB JPEG on the reference setup.

**Alternatives considered**:
- **An outbox message per upload**: survives restarts, but the dispatcher's polling
  interval eats into the 5 seconds, and on-demand production already covers the loss.
- **Producing inside the upload request**: slows every upload by up to 3 seconds for
  large photos.

---

## MV-14 Storage keys and clean-up

**Decision**:
- **Originals**: `{site}/media/{id}` for version 1 (unchanged) and `{site}/media/{id}-v{n}`
  for later versions. A sibling key, not a sub-key, because on the local provider
  `{id}` is a file and cannot also be a directory.
- **Variants**: `{site}/media-variants/{id}-v{n}-w{width}`, under their own prefix so the
  original sweep never mistakes them for orphans. They are still under the site's first
  segment (Principle V, FR-022).
- `MediaStorageKeys.SiteOf` accepts the `-v{n}` form, and a new `VariantKeys.Parse` reads
  variant keys.
- **Original sweep** (003 FR-012, unchanged logic): a `media/` key that belongs to no row
  and is older than the grace period is deleted. Old versions after a replace fall under
  this rule when the immediate delete failed.
- **Variant sweep**, a new pass of the same service, runs hourly. It deletes a
  `media-variants/` key older than one hour when any of these holds:
  - no row has that id;
  - the row's `content_version` differs;
  - the width is not configured;
  - the row's variants are unavailable.
  The hour covers productions racing a replace. It is idempotent and safe on several
  instances (003 M-15).

**Alternatives considered**:
- **Deleting variants synchronously on delete or replace**: up to six storage calls in the
  request, for bytes nobody can reach any more (their addresses are already `not_found`).

---

## MV-15 Responsive images on published pages

**Decision**:
- `PublicMediaFile` (in `Media.Contracts`) gains `Width`, `Height` and
  `Variants: [(Width, PublicPath)]`. The list is empty when the file is not eligible
  (MV-11).
- `ImageRenderer` writes:
  - `width` and `height`: the displayed dimensions, so the browser reserves the space
    (SC-006). These are omitted while unknown (edge case: before the backfill).
  - `srcset`:
    - every variant, as `{path} {w}w`;
    - plus the original at its own width, **only when** the original is no wider than the
      largest configured width. So a 6,000-pixel original is never offered (SC-003).
  - `sizes`, from the enclosing Section's content width `W` (720, 1,200 or 1,440 px from
    the design tokens, tracked in `RenderContext` as Sections render):
    - `full` width: `(max-width: Wpx) 100vw, Wpx`;
    - `natural` width: the same with `min(W, displayed width)`.
    - Containers with several columns make this an overestimate. It is bounded and
      recorded in Risks.
  - `src`: the largest variant no wider than 1,280, or the original when there are no
    variants.
- **Contract**: `docs/contracts/page-schema.md` §9 (normative rendering since 012) gains
  the image attributes. The schema file and `schemaVersion` are unchanged, because stored
  data does not change. The golden HTML files are regenerated. `CanvasMarkupParity.spec.ts`
  normalises `srcset`, `sizes` and `src`, whose addresses legitimately differ between the
  canvas (admin endpoints) and the published page, and keeps comparing structure, classes,
  `width`, `height` and `alt`.
- **Budget**: this is attribute text only. There is no JavaScript, and the 100 KB budget is
  unaffected.

**Alternatives considered**:
- **`<picture>` with `<source>` per format**: that is for format negotiation, which is out
  of scope. `srcset` with `sizes` is enough for resolution switching.
- **Per-column `sizes`**: needs container layout arithmetic in the renderer. The gain is
  small, since the browser already never downloads the original.

---

## MV-16 Variants in the admin shell

**Decision**:
- A new authenticated endpoint,
  `GET /api/v1/media/sites/{siteId}/files/{fileId}/variants/{width}`, under
  `media.files.view`, uses MV-12's production path. It works for sites with no host name
  (003 FR-018) and sends `Cache-Control: private, max-age=31536000, immutable`, because its
  path also changes with the version (the query carries `v`).
- **Where it is used**:
  - the library grid and the picker use the thumbnail width, with a `srcset` of the next
    width for high-density screens (FR-028);
  - the editor canvas's Image block uses the same `srcset` and `sizes` logic as the
    renderer, pointing at admin addresses;
  - the details panel uses 960.
- Non-eligible images (GIF, animated, unavailable) fall back to 003's preview endpoint.

---

## MV-17 Telling Pages that a file changed

**Decision**:
- New integration event **`Media.FileChanged (Id, OccurredAtUtc, SiteId, FileId, Reason)`**
  with `Reason` ∈ `replaced | dimensions | variants`. It is published in the same
  transaction as the row change (001 FR-015).
- Pages' existing `RefreshPagesShowingMediaHandler` handles it exactly like
  `Media.FileDeleted`: it bumps the render generation of every live page that shows the
  file, directly or through a component, with deduplication by event id. Rendering then
  re-reads `IPublicMediaReader` at the next request on every instance (012 PR-05).
  - FR-019: a replacement reaches live pages at their next request.
  - FR-016: a deletion does too.
  - MV-10: newly recorded dimensions appear.
  - MV-12: failed variants disappear from `srcset`.
- Alternative-text and folder changes emit nothing, because pages never read them
  (MV-04, FR-003).
- The event carries ids only (FR-041). It is documented in
  `specs/001-foundation-skeleton/contracts/integration-events.md`.

**Alternatives considered**:
- **One event per reason**: three handlers doing the same bump.
- **Relying on the cache's 1-hour sliding expiry**: frequently visited pages would never
  expire, and replace must be visible at the next request.

---

## MV-18 The S3-compatible provider

**Decision** (`Shared.Infrastructure/Storage/S3/S3FileStorage`), with **AWSSDK.S3 4.x**
(Apache-2.0):
- **Settings** (`Storage:S3:*`):
  - `Bucket` (required);
  - `Region` (required; any value for services that ignore it);
  - `ServiceUrl` (empty means AWS; set for MinIO, R2, Ceph and others);
  - `ForcePathStyle` (default `false`; `true` for most self-hosted services);
  - `KeyPrefix` (optional; must satisfy `StorageKey.IsValidPrefix` and end with `/`);
  - `AccessKeyId` and `SecretAccessKey`: both or neither. Neither means the SDK's ambient
    credential chain: environment variables, web identity (IRSA) or the instance profile
    (FR-032).
- **Compatibility settings**: request and response checksums are
  `WHEN_REQUIRED`. The SDK 4 default (always CRC32) breaks several S3-compatible services.
  Retries use the SDK's standard mode, 3 attempts.
- **Create-only writes**: `PutObject` and `CompleteMultipartUpload` with
  `If-None-Match: *`, so a `412` becomes `StorageKeyExistsException`.
  - Supported by AWS S3 (since 2024), MinIO, Cloudflare R2 and current Ceph RGW.
  - Start-up **verifies** it (MV-20): a service that ignores the header fails start-up with
    a message saying the service lacks conditional writes. The conformance suite's
    concurrent-write test then runs unchanged (FR-030).
- **Uploads**:
  - The incoming stream is not seekable and S3 needs a length, so bytes are spooled to a
    temporary file. The file lives under the provider's own temp directory, is opened with
    `DeleteOnClose`, and is bounded by the upload limit.
  - Objects up to 64 MiB go in one `PutObject`. Larger ones use multipart with 16 MiB parts.
  - A failed or cancelled write aborts the multipart upload, so nothing becomes visible
    (all-or-nothing, 003 M-04).
  - `S3AbandonedUploadCleanupService` aborts multipart uploads older than 1 hour under the
    prefix, the S3 twin of `LocalIncomingCleanupService`. The operations guide also
    recommends a bucket lifecycle rule.
- **Reads**:
  - `OpenReadAsync` does a `HEAD` for length and last-modified, and returns
    `S3RangeReadStream`: seekable, reading ranged `GET`s with a 1 MiB read-ahead buffer.
    This is how 003's range requests and seek-then-read contract work on S3.
  - A missing key gives `null`.
- **Other operations**:
  - `ExistsAsync`: `HEAD`.
  - `DeleteAsync`: `DeleteObject`, idempotent by nature.
  - `ListAsync`: `ListObjectsV2` with the prefix, paginated and streamed. It never returns
    in-progress multipart uploads (only committed objects) and strips `KeyPrefix`.
- **Health** (`IFileStorageHealthCheck`): put, get and delete of a probe object under
  `{KeyPrefix}.quickbuilder-probe/`, answering within 2 s (003 contract §3.1). Probe keys are outside the
  `StorageKey` grammar, so no listing or sweep ever sees them.

**Alternatives considered**:
- **The MinIO .NET client**: works against AWS as well, but the AWS SDK is the reference
  implementation and supports IRSA and instance profiles.
- **Allowing services without conditional writes, with a HEAD-then-PUT fallback**: racy.
  It would make the conformance suite's concurrent-write guarantee false on those
  services.

---

## MV-19 The Azure Blob provider

**Decision** (`Shared.Infrastructure/Storage/AzureBlob/AzureBlobFileStorage`), with
**Azure.Storage.Blobs 12.x** and **Azure.Identity 1.x** (MIT):
- **Settings** (`Storage:AzureBlob:*`):
  - `Container` (required);
  - **either** `ConnectionString` (account key or SAS, including Azurite), **or**
    `AccountUrl` with no secret, which authenticates with `DefaultAzureCredential`
    (FR-032): managed identity, workload identity or environment. The
    interactive-browser, Visual Studio and developer-CLI sources are excluded;
  - `KeyPrefix` (optional).
- **Create-only writes**: `BlobClient.UploadAsync(stream, new BlobUploadOptions {
  Conditions = new() { IfNoneMatch = ETag.All } })`. A `409 BlobAlreadyExists` or `412`
  becomes `StorageKeyExistsException`.
  - The SDK stages blocks from the unseekable stream and commits them in one
    `PutBlockList`, so the blob is all-or-nothing.
  - Uncommitted blocks expire on their own after 7 days, so no clean-up service is needed.
- **Reads**:
  - `OpenReadAsync(new BlobOpenReadOptions(allowModifications: false) { BufferSize =
    1 MiB })` returns a seekable stream.
  - Length and last-modified come from `GetPropertiesAsync`.
  - A `404` gives `null`.
- **Other operations**:
  - `ExistsAsync`: `ExistsAsync`.
  - `DeleteAsync`: `DeleteIfExistsAsync`.
  - `ListAsync`: `GetBlobsAsync(prefix: …)`, streamed, with `KeyPrefix` stripped.
- **Health**: a probe blob under `{KeyPrefix}.quickbuilder-probe/`, as for S3.
- **The container is never created by the product**: a missing container fails start-up
  (US3 scenario 4). Creating it would hide a typo in the name.

**Alternatives considered**:
- **Azure Data Lake (hierarchical namespace) APIs**: unnecessary. The flat blob API works
  with or without a hierarchical namespace.

---

## MV-20 Selecting the provider, validating it at start-up, and secrets

**Decision**:
- **Selection**: `Storage:Provider` ∈ `Local` (default), `S3`, `AzureBlob`, compared
  case-insensitively. `StorageRegistration.AddFileStorage` switches on it (the "three lines"
  003 left for this). Exactly one provider's services are registered. Only S3 registers
  `S3AbandonedUploadCleanupService`, and only Local registers
  `LocalIncomingCleanupService`. An unknown name still fails with the list of supported
  names, now three (FR-031).
- **Settings**: each provider binds its own sub-section, with the
  `Microsoft.Extensions.Configuration.Binder` package (the reason 003 read keys by hand no
  longer holds; MV-22 lists the dependency).
- **Start-up validation** (`IValidateOptions` plus `ValidateOnStart`, as for Local) runs in
  two steps, each failing with the key named (FR-033):
  1. **Settings**: required keys, pairs complete, `KeyPrefix` grammar and URL syntax.
  2. **Reachability** (30 s timeout):
     - put a probe with the create-only condition;
     - put it again and expect "exists" (this checks conditional writes, MV-18);
     - get it back and compare;
     - delete it.
     Failures are translated into operator language:
     - credentials rejected → `Storage:S3:AccessKeyId`;
     - bucket or container missing → `Storage:S3:Bucket` or `Storage:AzureBlob:Container`;
     - access denied on write → "needs write permission";
     - unreachable → `Storage:S3:ServiceUrl` or `AccountUrl` or the network.
- **Secrets**:
  - `SecretAccessKey`, `ConnectionString`, and the `AccountKey` or `SharedAccessSignature`
    inside it, never appear in messages or logs. Validation messages name keys, never
    values.
  - The SDKs' own logging is not wired to Serilog. The AWS SDK's `AWSConfigs.LoggingConfig`
    stays off.
  - `SecretRedactionTests` scans start-up failures and logs for the configured secret
    values (SC-011).
- **Readiness** (FR-034): `/health/ready` already calls `IFileStorageHealthCheck`, and the
  remote providers implement it. A failed probe reports `storage: unhealthy` without
  details.
- **Outbound traffic** (FR-042):
  - The default Local provider makes none.
  - With S3 or Azure, the only destinations are the configured endpoint, plus, only when
    the operator chose ambient identity, the platform's identity endpoint (instance
    metadata or the Entra ID token endpoint). The operator opts into that by leaving the
    secrets empty, and the configuration guide says so.
  - `NoEgressTests` (`ITESTS/Observability/NoEgressTests.cs`) keeps asserting zero for the default configuration, and is extended to the new media operations.
- **Multi-instance** (FR-036): with S3 or Azure configured, media is shared, and 012 already
  made rendering correct across instances. `docs/operations.md` states multi-instance
  support for the first time, with the storage requirement. `MediaAcrossInstancesTests`
  runs two app instances against one MinIO container.

---

## MV-21 The storage consistency check

**Decision** (clarifications: operator copy plus a check; background after start-up):
- **Tables**:
  - `media.storage_checks`: installation-level (no site). Holds id, trigger (`startup`,
    `admin`, `cli`), provider name, status (`running`, `completed`, `failed`), start and
    finish times, `lease_until`, files checked and missing count.
  - `media.storage_check_missing (check_id, site_id, file_id)`: at most 1,000 rows per
    check. The count is always exact.
- **One at a time**: a partial unique index on `status = 'running'`.
  - Before claiming, a run whose `lease_until` has passed is marked `failed` (its instance
    died).
  - The runner renews the lease every 30 s.
  - A second start is refused (`409 media.storage_check_running`), and the start-up trigger
    simply skips.
- **Algorithm**, per site:
  1. stream `ListAsync("{site}/media/")` into a set of keys;
  2. stream the site's rows' `storage_key`;
  3. every row whose key is absent is missing.
  - This costs one listing per site rather than one `HEAD` per file: 10,000 files on S3
    cost about 10 list calls.
  - Variants are ignored (FR-037a).
- **Triggers**:
  - **Start-up**: `StorageConsistencyCheckService` waits for `ApplicationStarted`, so
    readiness is never delayed. It starts a run unless one that began after this instance
    started is running or has finished. With N instances starting together, one run
    covers them all (the clarification says one at a time).
  - **Admin**: `POST /api/v1/media/storage-check` (202).
  - **CLI**: `media check-storage`, synchronous. It prints counts and up to 100 site and
    file ids, and exits `0` when complete with nothing missing, `3` when something is
    missing, and `1` on error.
- **Reporting**:
  - a warning log line with counts only;
  - `GET /api/v1/media/storage-check` returns the latest run and its missing list;
  - a new admin screen, **Settings → Storage**, shows the provider, the last check, the
    missing files (site, file id and a library link) and a **Run check** button.
- **Authorization**: a new installation-scope capability, `media.storage.check`, held by
  Administrators only.
- **Site filtering**: `storage_check_missing` is `ISiteOwned` and filtered. The report
  reads across sites with `AcrossAllSites("Installation-level storage report")` and a
  dedicated test, as Principle V requires.
- **Documentation**: `docs/operations.md` gains "Moving media between providers":
  - the key layout is identical everywhere (FR-037), so a plain copy works:
    `aws s3 sync <local-media-dir> s3://bucket/<prefix>`, `azcopy copy … --recursive`, or
    `rclone copy` between any two;
  - excluding `.incoming/` and `.probe-*`;
  - then switch `Storage:Provider`, start one instance and read the check.

**Alternatives considered**:
- **A built-in copy command**: rejected in clarification (option A).
- **A `HEAD` per file**: 10,000 requests per site on remote storage, for the same answer.

---

## MV-22 Dependencies

| Package | Licence | Why | Maintained | Alternative considered |
|---|---|---|---|---|
| NetVips 3.x | MIT | image header reading and variants (MV-09) | yes, active | ImageSharp (licence), SkiaSharp (no AVIF), Magick.NET (size, CVEs) |
| NetVips.Native.* (linux-musl-x64/arm64, linux-x64, win-x64) | LGPL-3.0-or-later (libvips) and bundled permissive licences | native libvips | yes, tracks libvips releases | system `apk add vips`: Alpine's build varies in AVIF support by release |
| AWSSDK.S3 4.x | Apache-2.0 | S3-compatible provider (MV-18) | yes, AWS | MinIO client |
| Azure.Storage.Blobs 12.x | MIT | Azure Blob provider (MV-19) | yes, Microsoft | none reasonable |
| Azure.Identity 1.x | MIT | managed and workload identity (FR-032) | yes, Microsoft | connection strings only (fails FR-032) |
| Microsoft.Extensions.Configuration.Binder 10.x | MIT | binding the provider sub-sections | yes | hand-reading about 12 keys |
| Testcontainers.Minio, Testcontainers.Azurite (tests only) | MIT | provider suites (Principle X) | yes | hand-rolled `docker run` |

- **Redistribution**: the native LGPL libraries are shipped unmodified as separate shared
  objects. `THIRD-PARTY-NOTICES.md` (new) lists them, with the source location required by
  LGPL.
- **Image size**: about 25 MB more (libvips natives), recorded in the upgrade notes.

---

## MV-23 Admin shell

**Decision**:
- **`MediaBrowser.vue`**, one component used both by the library screen and, in a modal
  dialog, by the picker (FR-010). It contains:
  - the folder tree (a keyboard tree, following the `aria` treeview pattern);
  - search, the kind filter, sort and direction;
  - the paged grid of thumbnails;
  - upload into the current folder.
  - In picker mode, only images are shown, and choosing one returns it.
- **Last folder**: kept per site and user in `localStorage` (`qb.media.lastFolder.{userId}.
  {siteId}`), with every access wrapped in `try/catch`. A per-device memory is enough for
  "opens in the folder the member last used". A missing or deleted folder falls back to the
  top level.
- **`MediaDetailsPanel.vue`** shows:
  - the preview at 960 and the details, including replaced by and at;
  - the alternative-text editor;
  - the usage list with links (MV-05);
  - Move, Replace and Delete actions, with `ReplaceDialog.vue` and the extended
    `DeleteDialog.vue`. Both show the usage warning, and explain the live-use rule before
    the member acts (US5 scenario 4, US6 scenario 5).
- **Picker** (`MediaControl.vue`): opens `MediaBrowser` in a dialog. When a file is chosen
  for a block whose `alt` is empty, it copies the file's `altText` (MV-04). When the file
  has no alternative text, it keeps 011's prompt.
- **`StorageSettingsView.vue`** (new route `/admin/settings/storage`, Administrators only):
  MV-21's report.
- **Quality**: every string goes through `vue-i18n` (`en.json`), and axe runs in each
  component's spec. The tree, grid and dialogs follow the 002 accessibility baseline
  (SC-013).

---

## MV-24 Authorization summary

| Operation | Declared capability | Additional rule in the handler |
|---|---|---|
| List, search, get file, usage, admin variants, folder tree | `media.files.view` | none |
| Upload (optionally into a folder) | `media.files.upload` | none |
| Folders: create, rename, move, delete; move files; edit alternative text | `media.files.upload` | none (no folder owners, clarification) |
| Delete file | `media.own_files.delete` | another person's file needs `media.files.delete`; live use needs `pages.publishing.manage`; any use needs `confirmInUse` |
| Replace file | `media.own_files.delete` | same as delete, except for the confirmation |
| Storage check: read or run | `media.storage.check` (new, installation) | Administrator only |
| Public original and variant | anonymous (listed explicitly) | site from the host name |

- `AuthorizationDeclarationTests` covers every new action.
- `specs/002-identity-setup/contracts/authorization.md` §2 gains `media.storage.check`.

---

## MV-25 Testing strategy

- **Unit**:
  - `MediaFolderTreeTests`: depth, cycles, sibling names;
  - `SearchTextTests`: accents, case, escaping;
  - `ListingCursorTests`;
  - `MediaAddressTests`: canonical forms and every refusal;
  - `VariantPlanTests`: widths, eligibility, `srcset` and `sizes`;
  - `ImageMetadataTests`: orientation swap, animated, pixel limit;
  - `VariantEncodingGoldenTests`: size bounds, no metadata, upright, alpha;
  - `S3RangeReadStreamTests`.
- **Integration** (PostgreSQL via Testcontainers):
  - `MediaFolderApiTests`, `MediaListingApiTests` (SC-001 at 10,000 files),
    `AltTextApiTests`;
  - `MediaUsageTests` (SC-008) and `DeleteInUseTests` (SC-007, SC-015);
  - `ReplaceFileApiTests` (SC-009, FR-020), `VariantServingTests` and
    `VariantProductionExactlyOnceTests` (SC-005);
  - `ThumbnailLatencyTests`, `MediaFileChangedRefreshTests` (pages refresh),
    `StorageCheckTests` (SC-014) and `UpgradeFrom012Tests` (SC-012, FR-045);
  - `MediaReferenceBackfillTests` and `MediaIsolationProbe` extensions (folders, variants,
    usage, check report).
- **Provider suites** (Principle X): 003's `FileStorageConformanceTests` is already an
  abstract base. It runs unchanged for:
  - Local, through the existing `LocalFileStorageTests`;
  - `S3FileStorageConformanceTests` (MinIO container);
  - `AzureBlobFileStorageConformanceTests` (Azurite container);
  - plus `S3StartupValidationTests` and `AzureStartupValidationTests` (SC-011),
    `SecretRedactionTests` and `MediaAcrossInstancesTests` (SC-010, two app instances on
    MinIO).
- **Rendering**: regenerated golden files with `srcset`; `ImageRendererTests` (SC-003,
  SC-006: `width` and `height` present, original never offered when too large).
- **Admin (vitest)**: `MediaBrowser.spec.ts`, `FolderTree.spec.ts`,
  `MediaDetailsPanel.spec.ts`, `ReplaceDialog.spec.ts`, `DeleteDialog.spec.ts` (usage
  warning, live-use message), `MediaControl.spec.ts` (alternative-text copy, last folder)
  and `StorageSettingsView.spec.ts`, each with axe.
- **Smoke** (FR-043): the reference journey publishes a page with an uploaded JPEG and
  fetches one `srcset` variant from the site's host name. The performance job gains
  `VariantPerformanceTests` (SC-004).
- **CI**: the MinIO and Azurite suites run in the existing `test` job. Testcontainers
  already needs Docker there.

---

## MV-26 Upgrade and migrations

- **Media** `AddLibraryAndVariants` (additive):
  - `folders` and `folder_trees`;
  - new `media_files` columns, all nullable or defaulted: `folder_id`, `alt_text`, `width`,
    `height`, `is_animated`, `metadata_state` (default `pending` for images and
    `not_applicable` for PDFs, set in the migration), `content_version` (default 1),
    `replaced_at_utc`, `replaced_by`, `variants_state` (default `available`),
    `search_text` and `sort_name`. A migration only emits SQL and cannot read rows into
    C#, so it fills them with `lower(file_name)` and marks them `search_text_state =
    provisional`. `SearchTextBackfillService` then rewrites them with `SearchText.For`
    after start-up, one site at a time and idempotently. Until it finishes, search on an
    upgraded site matches case-insensitively but not accent-insensitively. Rejected: the
    `unaccent` extension, because it would duplicate the .NET normalisation in SQL and
    the two could drift;
  - the new indexes, `variant_claims`, `storage_checks` and `storage_check_missing`.
- **Pages** `AddMediaReferences`: the two reference tables and the backfill (MV-05).
- Both are forward-only and safe to re-run (Principle II).
- `UpgradeFrom012Tests` checks that every existing file:
  - keeps its address and bytes;
  - lands at the top level with no alternative text;
  - gets dimensions and a thumbnail after the backfill;
  - and that usage matches the documents.

---

## Things deliberately not built

- A built-in copy between providers (clarification), CDN integration, and signed or
  expiring addresses.
- Format conversion, cropping, focal points, and arbitrary sizes.
- A shared rendered-page cache. It is not needed for correctness (012 E-4).
- Trigram or full-text search. It is documented as the next step beyond 10,000 files per
  site.
- Renaming files, tags and other DAM metadata, quotas, and a recycle bin (spec Out of Scope).
- Moving files between sites.

## Configuration keys

| Key | Default | Validation |
|---|---|---|
| `Storage:Provider` | `Local` | `Local`, `S3`, `AzureBlob` |
| `Storage:S3:Bucket`, `Region` | — | required when S3 |
| `Storage:S3:ServiceUrl` | empty (AWS) | absolute `https` or `http` URL |
| `Storage:S3:ForcePathStyle` | `false` | bool |
| `Storage:S3:KeyPrefix` | empty | key-prefix grammar, ending in `/` |
| `Storage:S3:AccessKeyId`, `SecretAccessKey` | empty (ambient) | both or neither |
| `Storage:AzureBlob:Container` | — | required when AzureBlob |
| `Storage:AzureBlob:ConnectionString` | empty | this or `AccountUrl` |
| `Storage:AzureBlob:AccountUrl` | empty | absolute `https` URL (ambient identity) |
| `Storage:AzureBlob:KeyPrefix` | empty | key-prefix grammar, ending in `/` |
| `Media:MaxImagePixels` | `100000000` | 1,000,000 – 1,000,000,000 |
| `Media:Variants:Widths` | `[320,640,960,1280,1920,2560]` | 1–10 ascending values, 64–4,096 |
| `Media:Variants:MaxConcurrent` | processors ÷ 2 (at least 1) | 1–64 |
| `Media:Variants:Timeout` | `00:00:10` | 1 s – 60 s |

## Resolved unknowns

- **Image library**: NetVips (MV-09).
- **Where usage lives**: Pages (MV-05).
- **Address grammar**: MV-08.
- **Exactly-once production**: claims (MV-12).
- **S3 without seekable uploads**: spooling (MV-18).
- **Create-only on S3**: `If-None-Match`, verified at start-up (MV-18).
- **Ambient identity versus FR-042**: stated as operator opt-in (MV-20).
- **The spec's shared-cache assumption**: superseded by 012 closing E-4. The spec was
  corrected (FR-036).
- **Last-used folder**: per device, in `localStorage` (MV-23).
- **No `NEEDS CLARIFICATION` remains.**
