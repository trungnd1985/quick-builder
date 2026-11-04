# Research: Site Export and Import

**Feature**: 020 · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

Decisions are numbered ST-01 … ST-24. Each gives the decision, why, and what else was
considered. "Part" means one module's share of an archive (ST-03).

## ST-01 Owner: the Sites module orchestrates; each content module contributes a part

**Decision**: export, check, import, jobs, the archive container, the admin API and the
`sites` operator command live in the **Sites** module (`Features/Transfer/`). Each module
that owns site content implements `ISiteArchivePart`, declared in **Sites.Contracts**
(contracts/archive-parts.md). Sites discovers parts through DI and never references another
module.

**Rationale**:
- Sites already owns site creation, settings, host names and availability, which import as a
  new site and restore must change.
- Every module already references Sites.Contracts (`ISiteDirectory`, `ICurrentSite`), so the
  interface adds no new module edge, and `ModuleBoundaryTests` already exempts
  Sites.Contracts from the cycle check.
- Each module serialises, validates, remaps and writes only its own tables, with its own
  validators and rules (Principle I).

**Alternatives considered**:
- *A new `Transfer` module*: Principle I lists the modules, so this would need an amendment,
  and the module would still own no data except jobs.
- *One exporter reading every module's tables*: breaks the rule that no module reads
  another's tables.
- *Interface in Shared.Infrastructure*: allowed, but the interface needs site concepts
  (settings, host names) that belong in Sites.Contracts.

## ST-02 Archive container: a ZIP file with a JSON manifest and JSON Lines entries

**Decision**: an archive is a ZIP file (`.qbsite`, `application/zip`), written with
`System.IO.Compression` and Zip64 when needed:

```text
manifest.json                    format version, release, source, parts, entry hashes
authors.jsonl                    id and display name of every member the archive names
parts/<part>/<entry>.jsonl       one JSON object per line, per part (archive-parts.md)
media/<fileId>                   original bytes of each media file, stored uncompressed
```

- **Integrity**: `manifest.json` lists every other entry with its length and SHA-256. Import
  refuses an archive whose entries do not match the list exactly: missing, extra, or with a
  different length or hash (FR-012). A truncated ZIP has no central directory and is refused
  as unreadable.
- **Paths**: only the shapes above are accepted. Names with `..`, a leading `/`, `\`, a drive
  letter or anything else are refused before any entry is read (FR-012). Nothing is ever
  extracted to disk by name.
- **Compression**: JSON entries use Deflate. Media entries are stored as they are, because
  images are already compressed and stored entries are quicker to hash.
- **Documented** as a versioned contract in `docs/contracts/site-archive.md`, with
  `site-archive/manifest.v1.schema.json` and golden archives (FR-038). Draft:
  contracts/site-archive.md.

**Rationale**: ZIP is in the BCL (no new dependency, Principle XI). Operators can open it
with ordinary tools. Reading it needs a seekable stream, which `StoredFile.Content` always is.
It writes to a stream that cannot seek. JSON Lines lets parts stream thousands of rows
without loading a whole collection into memory (FR-036).

**Alternatives considered**:
- *tar.gz*: no random access. Hashing and validating before any change would need two
  passes over a multi-gigabyte stream, or extraction to disk.
- *One large JSON document*: cannot be streamed with bounded memory.
- *A signed archive*: the spec accepts hand edits that keep the archive consistent, and
  there are no keys to manage offline. Not built.

## ST-03 Parts, their order, and what is rebuilt instead of carried

**Decision**: five parts. Each writes its entries and lists its contract versions in the
manifest:

| Part | Module | Carries |
|---|---|---|
| `sites` | Sites | settings (name, scheme, default language, enabled state), host names, site search settings |
| `media` | Media | folders, file records (name, type, size, alt text, dimensions, folder, content version, upload author and time), bytes |
| `design` | Editor | design token draft, live version, retained versions |
| `cms` | Cms | collections with schemas and schema change history, items with draft, live version, retained versions, schedules, history, copy origin |
| `pages` | Pages | page tree, pages, components, drafts, publications, retained versions, schedules, menus with drafts and versions, redirects, copy origins |

Forms has no part. Forms live inside page and component documents (018 FR-040), and the form
registry and placements are rebuilt from the documents by the existing
`RefreshFormRegistry` handler when the imported pages' events arrive. **Rebuilt, never
carried**:
- Pages: component, collection, menu and media usage rows, live dependencies, menu targets,
  sitemap state, redirect usage;
- Cms: item references, collection references, item slugs, item media uses;
- Media: image variants and variant claims.

**Not carried**:
- Pages: publish log, redirect change log and menu change log. These are audit
  records of this installation, and FR-031 records the import itself.
- Editor: editing sessions and takeovers (023).
- Forms: submissions, notification deliveries, spam counters and rate windows (018).
- Identity: everything except author display names (ST-14).
- Shared: outbox and processed events.

Parts run in dependency order: `sites`, `media`, `design`, `cms`, `pages`. Remapping does
not depend on this order (ST-08), but validation does: a page's check needs to know which
media files and collections the archive holds.

**Rationale**: carrying derived rows would mean validating that they agree with the
documents. Rebuilding them uses the code that keeps them correct today.

## ST-04 Consistent export: one read-only snapshot shared by every part

**Decision**: the export opens one connection and starts
`REPEATABLE READ, READ ONLY`. Every part's `DbContext` joins it
(`Database.SetDbConnection` + `UseTransaction`) through `SiteTransferSession` in
Shared.Infrastructure (`Data/`). All parts therefore read the same committed state (FR-005)
while members keep editing (FR-006). Media bytes are read after the rows, by content-version
key (`{site}/media/{file}-v{n}`). Keys are never rewritten, so the bytes belong to the
snapshot's row. A key deleted in the meantime is reported as missing (FR-009).

**Rationale**: this is the only way to get a whole-site point in time without blocking edits.
It is the read half of the shared transaction that Principle I allows since amendment 2.6.0
(formerly exception E-7).

**Alternatives considered**:
- *Per-module transactions sharing an exported snapshot* (`pg_export_snapshot` +
  `SET TRANSACTION SNAPSHOT`): keeps one transaction per module, but needs one open
  connection per module for the whole export and a coordinator for their lifetimes. Kept as
  a fallback. It is no longer needed since amendment 2.6.0.
- *Lock the site during export*: blocks editing for up to 15 minutes (SC-002).

## ST-05 Atomic import: one write transaction across the parts

**Decision**: the apply step runs every part's writes in one `READ COMMITTED` transaction on
one connection, shared the same way as ST-04, and commits once (FR-018). Media bytes are
written to storage before the commit, under keys that no committed row uses. If the import
fails, is cancelled or the process dies:
- PostgreSQL rolls the transaction back;
- the bytes already written have no row, so the existing `MediaOrphanSweepService` removes
  them after `Media:OrphanGracePeriod` (003 research M-15).

Each part writes its integration events to its own outbox table in the same transaction, so
cache invalidation, the form registry and search state run only after the commit
(Principle VIII).

**Rationale**: visitors and members see either the whole import or none of it, including
after a restart. PostgreSQL easily handles 10,000 items, 500 pages and their versions in one
transaction. This is the write half of the shared transaction (Principle I, 2.6.0).

**Alternatives considered**:
- *Staging tables per module, then a roll-forward activation* in one transaction per module:
  needs staging copies of about 30 tables, and leaves a window in which visitors see pages
  activated before their items. Rejected as much larger, and weaker for merge and restore.
- *Compensation from the pre-restore export*: a restore that fails would need another
  restore to undo it.

## ST-06 Two steps: check, then apply

**Decision**: every import first runs **check** (job state `checking`):
1. integrity and paths (ST-02);
2. versions (ST-21);
3. each part validates its entries with its module's validators and reports what it found;
4. the id map and every conflict are computed against the target (ST-08, ST-10).

The result is stored on the job (`check_result`) and the job waits in
`awaiting_confirmation`. **Apply** runs only after the Administrator, or the command's
options, confirm. Apply recomputes the id map inside its transaction and compares a
fingerprint of the conflicts and removals with the confirmed check. If the target changed in
between, apply is refused with `transfer.target_changed`, and a new check must be confirmed.
A check that is not confirmed expires after 24 hours, and its upload is deleted. A pending
check holds no site slot, so it never blocks exports or other imports (data-model §1). The
fingerprint comparison at apply catches anything that changed in the meantime.

**Rationale**: FR-011 and FR-016 promise that what was shown is what happens (SC-005).
Merge and restore previews are computed from the target at check time.

## ST-07 Members cannot write to a site while an import into it applies

**Decision**: a per-site PostgreSQL advisory lock.
- Apply takes `pg_advisory_lock(key(site))` (exclusive) for its whole transaction.
- Every content write takes `pg_try_advisory_xact_lock_shared(key(site))` in its own
  transaction. This
  covers writes to pages, components, menus, redirects, design, collections, items, media and
  site settings. If the try fails, the write is refused with
  `423 site.import_in_progress`.
- **As built**: the gate is `SiteWriteGateInterceptor` (Shared.Infrastructure `Data/`), a
  SaveChanges interceptor registered on the Sites, Media, Editor, Cms and Pages contexts,
  instead of calls in each module's write helpers. Any save that adds, changes or deletes an
  `ISiteOwned` row takes the shared lock for each site it touches; when no transaction is
  open it begins one and commits it with the save. A refusal throws
  `SiteImportInProgressException`, which the Host maps to 423. No write path can forget the
  gate, and `ISiteWriteGate` was removed. Set-based `ExecuteUpdate`/`ExecuteDelete` do
  not pass through it; content writes use the change tracker. The import itself takes
  `pg_advisory_xact_lock` (transaction-scoped, not session-scoped) on the session's
  transaction, so the lock ends with the commit or rollback.
- `TransferGateCoverageTests` runs every write endpoint in `EndpointCatalog` against a site
  whose lock is held, and expects 423.

The site response gains `transfer: { kind, state, startedAtUtc } | null`. The admin shows a
banner on every screen of that site while it is set (FR-028), and the editor stops
autosaving with the same banner.

**Rationale**:
- A write that began before the import holds the shared lock, so apply waits for it to finish
  (milliseconds). This ordering is correct without a flag that could get stuck.
- Advisory locks are released when the connection dies, so a crashed import never leaves a
  site locked.
- Visitors are not affected: reads take no lock, and they see committed data only.

**Alternatives considered**:
- *A `transfer_in_progress` flag that every module reads*: a race between the flag and a
  write that has already started, and a stuck flag after a crash.
- *A SaveChanges interceptor in Shared.Infrastructure*: misses `ExecuteUpdate` writes and
  depends on an ambient site that admin requests do not always set.

## ST-08 Matching and the id map

**Decision**: check builds an **id map** from `(kind, archiveId)` to `(targetId, outcome)`
before any part writes. Each part maps its own kinds: page, component, collection, field
(kept), item, menu, media file, folder, redirect, design. Matching depends on the mode:

| Mode | Match | Unmatched |
|---|---|---|
| New site (full or start fresh) | none | new id |
| Merge, archive from **another** site | natural key (FR-022) | new id |
| Merge, archive from **this** site (`manifest.source.siteId == target.id`) | identity, then natural key (FR-022a) | new id |
| Restore | identity only | original id kept |

Natural keys:
- page: address;
- component: name;
- collection: address name;
- item: address name within its matched collection;
- menu: name;
- redirect: source;
- media file: content hash (SHA-256 from the manifest) against the target's files. A
  matching file is reused in every mode except restore.

The target's hashes are computed during check for files without a stored hash. Field ids are
kept as they are: they are unique only within a collection (014).

**Rationale**: allocating every id first lets each part rewrite references to kinds owned by
other parts (pages ↔ items ↔ media) without a write order. The cycle between pages and items
needs this.

**Note**: media files have no stored content hash today. Check hashes the target's files of
the same size only. That is cheap, because most sizes are distinct. Adding a stored hash is
deferred.

## ST-09 Reference rewriting inside documents

**As built (2026-11-02), replacing the decision below**: one generic rewrite,
`IIdMap.RemapDocument`, replaces every JSON string that equals an id the archive declares with
that id's target, in either spelling (`D` or `N`). Every archive id is a random UUID, unique
across kinds, so a string equal to a declared id can only be a reference to that document. Node
ids, field ids and form field ids are not declared, so they are kept (018 backlog). The rewrite
covers every contract and every future block without per-contract code (Principle XI).
`IdMapTests.Every_contract_example_remaps_only_its_declared_ids` runs it over every valid golden
example of the page schema, menu, design tokens and collection item contracts. Broken references
are still reported by each part's check, from the extractors its module already uses for usage
rows.

**Original decision**: each part rewrites the ids in its own documents through the id map, using the
same walkers its module already uses to extract references for its usage rows:
- page and component documents: page links, item, collection, component, menu and media ids;
  form `redirect.pageId` and `consent.linkPageId`; binding collection ids;
- menu documents: `pageId`, `itemId`, `collectionId`;
- item values: references, multi-references, image and file fields, and rich text links and
  images;
- design tokens: media references;
- redirect targets;
- copy origins.

An id that the map does not hold remains a broken reference (spec edge case) and is listed in
the report. Each contract gets golden files under `docs/contracts/<contract>/remap/`: one input,
one map, one expected output.

**Rationale**: one walker per contract already exists and is tested. A second, generic JSON
id scanner would drift from it.

## ST-10 Conflict policies per kind

**Decision**: one policy per merge (FR-023), applied as follows:

| Kind | Replace | Skip | Rename |
|---|---|---|---|
| Page | the target page keeps its id, place and children, and takes the archive's draft and live state; its old versions stay in history | target kept; references point to it | added under the same parent with `-2`, `-3`… on the slug (010 rules) |
| Component | as page | as page | name `<name> (imported)`, then ` 2`… |
| Collection | the archive's fields are applied as a schema change with 014's conversions, previewed in check | target kept; its items are matched by address name (FR-025) | address name `-2`…; display name `(imported)` |
| Item | as page, within a matched collection | as page | slug `-2`… (015 rules) |
| Menu | as page | as page | name `(imported)` |
| Redirect | the target takes the archive's target and kind | target kept | not renamable: treated as skip, and reported |
| Media file | identical files reused (no conflict); other files always added | | |
| Site settings, design tokens | archive applied; host names and enabled state never | target kept; missing tokens added | as skip |

**Rationale**: these are the existing creation and rename rules of each owning feature, so a
renamed document is valid by construction. Redirect sources are unique by rule (017), so they
cannot be renamed.

## ST-11 Start fresh

**Decision**: `startFresh: true` is accepted only with mode `new_site` (FR-021a). Each part
writes one draft per document:
- the archive's draft, or the live version where there is no newer draft;
- no publication, no retained versions, no schedules, no copy origin;
- the item history keeps only a `created` entry naming the importing Administrator.

Collections (with their schema history), settings, tokens (draft only), redirects and media
are imported as usual. Design tokens are imported as a draft: a new site has no published
design, and renders with the defaults until a Designer publishes it.

## ST-12 Restore: replace the site's rows with the archive's

**Decision**: restore (allowed only when `manifest.source.siteId` names an existing site,
FR-027a) runs each part's `ApplyAsync` in `Restore` mode. Inside the shared transaction,
each part:
1. deletes every row of the site in the tables it carries or rebuilds;
2. inserts the archive's rows with their original ids;
3. rebuilds its derived rows.

This needs no row-by-row comparison. The preview of removed, changed and returned documents
(FR-027b) is computed at check time by comparing ids and content hashes. Sites applies the
archive's settings, apart from host names and the enabled state (FR-027c).

Media bytes:
- a key that already exists (same file, same content version) is not written again;
- bytes of files that the archive no longer holds lose their row, and the orphan sweep removes
  them.

**Rationale**: "match the archive" is exactly "replace the rows". Every rule of each module
holds, because the archive was valid when exported and is validated again at check.

**Alternatives considered**: diff and patch per document through each module's domain
operations (delete page, update draft…). Much more code, and the result would be the same.

## ST-13 The pre-restore export

**Decision**: confirming a restore first runs a normal export of the target (kind `export`,
`trigger: pre_restore`), linked from the restore job. The restore's apply starts only after
that export succeeds (FR-027d). It is kept and expires like any admin export (7 days), and
the report links it. From the command line, the pre-restore archive is also written to
`--pre-restore-out` when it is given. The pre-restore export does not take the site's
active slot, because the restore job already holds it (data-model §1).

## ST-14 Authors: historical names, not accounts

**Decision**: `authors.jsonl` lists every user id the archive names (uploaded by, published
by, created by, copy origin…), with that user's display name only: no email, no roles. On
import:
- an id that exists on this installation is kept. This always happens for restore and for
  merges from the same installation.
- any other id is mapped to a **historical author**: a row in the new
  `identity.historical_authors` table (`id`, `display_name`, `imported_at_utc`), created
  once per id per installation.

`IUserDirectory` resolves display names from users, then from historical authors, which it
marks with `isHistorical: true`. The admin shows "(imported)" after the name. A historical
author cannot sign in, has no email, holds no roles and is not listed under Users (FR-021).

**Rationale**: every author column stays a `Guid`, with no change to existing tables, and
every screen that shows an author keeps working.

**Alternatives considered**:
- *Map every author to the importing Administrator*: loses the names that FR-021 keeps.
- *Store names on every row*: needs a new column on about twelve tables.

## ST-15 Jobs: a database-backed queue in Sites

**Decision**: a new `sites.transfer_jobs` table (data-model §1). `TransferWorker`, a hosted
service in Sites:
- claims queued work with a lease (2 minutes, renewed every 15 seconds), as
  `NotificationWorker` (018) does;
- runs one transfer at a time per instance;
- writes `step`, `percent` and `progress_at_utc` at most once a second.

Further rules:
- **One per site**: a unique partial index on `site_id` where the state is active refuses a
  second export, import or restore of the same site (FR-010). An export waits for an import
  of the same site by being refused with `transfer.busy`, and the admin offers to retry.
- **Lost lease**: an instance that loses its lease is the only failure mode the worker
  cannot report itself. Its connection is gone, so the import has rolled back (ST-05). The
  next sweep marks the job `failed` with `interrupted` (spec edge case: failed, not resumed).
- **Cancel**: sets `cancel_requested`. The running step checks it between batches, rolls
  back, and ends `cancelled` (FR-007, FR-018).
- **Command line**: the operator command creates the same job row and runs it in its own
  process without the worker. It renews the lease itself, so the uniqueness, audit and
  interruption rules are the same.

- **Site context**: background work has no resolved site, and site filters treat that as
  "no site" and match nothing. Each runner therefore works in its own DI scope per job and calls
  `ISiteContextSetter.SetSite(siteId)` before any part runs, as `RefreshFormRegistryHandler`
  does. For a new site, it uses the id it allocates. Parts then read and write through their
  normal filtered contexts. The only cross-site read in the parts is Sites' check of host
  names already in use; it goes through `AcrossAllSites` with a reason, and
  `TransferIsolationProbe` covers it.

**Rationale**: the constitution requires built-in, database-backed background work
(Principle IV). The lease pattern is already in the codebase and tested.

## ST-16 Where archives live, and for how long

**Decision**: admin archives (exports and uploads) are stored through `IFileStorage` under
`{siteId:N}/transfers/{jobId:N}`. Uploads for a new site use
`installation/transfers/{jobId:N}`, which is not a site id, so the media orphan sweep, which
only lists site media prefixes, ignores them.
- **Writing**: a writer task writes the ZIP into a `System.IO.Pipelines` pipe, and
  `IFileStorage.WriteAsync` reads the other end. Memory stays bounded whatever the archive
  size. Storage is create-only and all-or-nothing, so a failed export leaves no object.
  **Verified in T001 (2026-11-02)**: the conformance case
  `A_write_from_a_pipe_of_unknown_length_stores_the_whole_object` (70 MiB from a
  non-seekable pipe, across the S3 multipart threshold) passes on the local, S3 (MinIO) and
  Azure Blob (Azurite) providers with no provider change. The 5 GiB variant is in the
  Performance category.
  **Media orphan sweep**: it lists all keys but only acts on keys shaped like
  `{site}/media/…` or `{site}/media-variants/…` (`MediaStorageKeys.SiteOf`, `ParseVariant`),
  so `transfers` keys are never swept. No change needed.
- **Retention**: `TransferRetentionService` runs hourly. It deletes exports 7 days after they
  finish (FR-008), uploads 24 hours after upload if no import confirmed them, and job rows
  after 90 days. Its own key prefix keeps it away from media. That the media orphan sweep
  never lists `transfers` keys is checked in T001; if its listing is wider, it is narrowed
  there.
- **Download**: `GET …/archive` serves the stored object with range processing, so a
  download can resume (FR-008).

**Rationale**: the configured storage is already shared by every instance (Principle IV),
so any instance can serve a download or run the import of an upload received by another.

## ST-17 Upload of an archive in the admin

**Decision**: `PUT /api/v1/transfers/uploads/{uploadId}` streams the request body into storage
without buffering, up to `SiteTransfer:MaxUploadSizeBytes`, which is the only new configuration
key (default `10GB`, maximum `100GB`). A larger body is refused with
`413 transfer.too_large`, and the admin points to the operator command (spec edge case).
Upload progress is shown by the browser from its own request. The upgrade notes say that
reverse proxies limit request bodies (for example nginx `client_max_body_size`) and must be
raised for `/api/v1/transfers/uploads`.

**Alternatives considered**: chunked, resumable uploads (tus or our own chunks). That means
more endpoints and assembly in storage. Deferred. The operator command covers large archives
without any limit.

## ST-18 The `sites` operator command

**Decision**: a new `IHostCommand` named `sites` with `export`, `check` and `import`
(contracts/operator-commands.md):
- the archive is read from and written to the container's file system path given;
- it prints progress every 5 seconds and the summary or report;
- it reuses the job rows (ST-15);
- exit codes: `0` success, `1` failed, `2` archive refused, `3` pending migrations (existing
  convention), `4` unknown site, `5` site busy, `6` confirmation required and missing,
  `64` usage;
- `export --all` exports each site to `<dir>/<host-or-id>-<utc>.qbsite`, continues past a
  failed site, and returns 1 if any failed (FR-033).

**Rationale**: same shape as `identity` and `media check-storage`, which are not reachable
over the network (FR-034).

## ST-19 Audit

**Decision**: Sites writes these outbox events:
- `SiteExportFinished` (`jobId`, `siteId`, `outcome`, `startedBy`, `trigger`);
- `SiteImportFinished` (`jobId`, `siteId`, `mode`, `outcome`, `startedBy`);
- `SiteArchiveDownloaded`;
- `SiteArchiveDeleted`.

Identity records them as security events: `site_exported`, `site_imported`,
`site_restored`, `site_transfer_cancelled`, `site_transfer_failed`,
`site_archive_downloaded`, `site_archive_deleted`. These carry ids, counts and the operator
flag only, no names or content (FR-031, Principle IX). `SiteCreated` is raised for a new
site, as it is today.

## ST-20 Validation reuses each module's rules

**Decision**: check calls each part's `CheckAsync`. Parts use the validators that guard their
own saves:
- Pages: page schema, binding, menu and redirect validators, and the page tree invariants
  (address uniqueness, depth, reserved segments);
- Cms: the collection schema validator and item value validation (015);
- Editor: the design token validator;
- Media: the upload path's content sniffing and allowed types, and `Media:MaxFileSizeBytes`;
- Sites: the settings and host name rules.

An invalid document refuses the archive (FR-014). A refused or missing media file is a
**warning** that the Administrator confirms (FR-015). Its manifest entry is imported as a
missing file, and references to it then behave as references to a missing file do today
(013).

## ST-21 Versions

**Decision**: the manifest records:
- `format` (archive format, starting at `1`);
- `release` (the product's semantic version);
- each part's `contracts`: the content schema version of each contract it uses, for example
  `page-schema: 1`, `menu: 1`.

Check rules:
1. `format` greater than this release supports → refused with `transfer.format_too_new`
   (FR-013).
2. `release` of a newer **major** → refused with `transfer.release_too_new`. An older major
   is refused with `transfer.release_unsupported` unless listed in
   `SiteArchive.SupportedMajors` (only the current major today).
3. Each document is read through its module's `SchemaUpgradeChain`. `TooNew` →
   `transfer.document_too_new`, `Failed` → invalid entry, `Upgraded` → noted in the check
   ("content from release X will be upgraded").
4. Documents are **stored as exported**, at their stored `schemaVersion`. They are upgraded on
   read and saved at the current version on their next save or publish, as 019 requires for
   every stored document (Principle VI). Validation always runs on the upgraded form.

## ST-22 Admin

**Decision**:
- **Site settings → "Export and import"** tab: start an export, follow it, download, delete,
  and list recent transfers with their reports.
- **Sites list → "Import site"**: upload, then check (as a new site), then options (name,
  start fresh), then confirm, then progress, then report.
- The same tab of an existing site offers "Import into this site" (merge with a policy) and
  "Restore". Restore appears only for an archive whose source is this site. It requires typing
  the site's name.
- Progress polls `GET /api/v1/transfers/{id}` every 2 seconds while the screen is open. A
  transfer keeps running when the screen closes, and the tab shows it again on return.
- Components: `TransferProgress`, `ImportCheckView` (contents, versions, problems,
  conflicts, removals), `TransferReport`, `RestoreConfirmDialog`, and `SiteImportBanner`
  (FR-028), which is mounted in the site layout.

All of these meet the accessibility baseline (FR-039): progress is in an `aria-live="polite"`
region, problems are listed as a list with headings, and the dialogs trap focus.

## ST-23 Tests

**Decision** (FR-037, SC-001 … SC-008, Principle X):

- **Unit**:
  - `ArchiveManifestTests` (hashes, paths, versions);
  - `IdMapTests` (each mode and match rule);
  - `ConflictPolicyTests` (each kind × policy);
  - one remap golden suite per contract (`PageDocumentRemapTests`, `MenuRemapTests`,
    `ItemValuesRemapTests`, `DesignTokensRemapTests`).
- **Integration** (Testcontainers):
  - `ExportContentsTests` (every kind present, FR-001 to FR-003);
  - `ExportSnapshotTests` (edits during the export, FR-005);
  - `RoundTripTests` (export, import as new site, compare everything: SC-001, FR-037);
  - `StartFreshTests`;
  - `MergePolicyTests` (each policy, from another site and from the same site);
  - `RestoreTests` (SC-005, pre-restore export);
  - `ArchiveRefusalTests` (truncated, altered, extra entry, bad paths, newer format, too-new
    document, invalid content, non-image bytes: SC-003);
  - `ImportFailureTests` (fault injection after each part, cancel, killed connection:
    SC-004);
  - `TransferGateCoverageTests` (ST-07);
  - `TransferEndpointsTests` (authorisation, 413, range download);
  - `TransferRetentionTests`;
  - `SitesCommandTests` (exit codes);
  - `TransferIsolationProbe`;
  - `UpgradeFrom023Tests`;
  - `ArchiveFromPreviousReleaseTests` (SC-008: from this release on, each release adds its
    archive to `tests/fixtures/archives/`).
- **Performance**: `TransferPerformanceTests` (manual job on the reference setup, SC-002).
- **Admin**: `ImportCheckView.spec.ts`, `RestoreConfirmDialog.spec.ts`,
  `TransferProgress.spec.ts`, `SiteImportBanner.spec.ts`, with axe.

## ST-24 Performance and memory

**Decision**:
- **Streaming**: parts stream rows with `AsAsyncEnumerable` and write them line by line.
  Import reads JSON Lines line by line.
- **Writes**: in batches of 500 rows with `ChangeTracker.Clear()` between batches, inside the
  shared transaction.
- **Hashing** while writing (export) and in one pass before check (import).
- **Media**: bytes are copied stream to stream.

The 2 GB, 10,000-item reference site needs about 2 GB of storage reads and writes, plus about
40,000 rows. Measured targets: export within 15 minutes and import within 20 (SC-002). Normal
reads are unaffected, because export runs in a snapshot without locks. Import's transaction
holds row locks only on the target site's rows.
