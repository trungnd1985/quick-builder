# Implementation Plan: Media Library and Storage Abstraction

**Branch**: `003-media-storage` | **Date**: 2026-09-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-media-storage/spec.md`

**Issue**: #3. This feature closes exception **E-2** from the
[001 plan](../001-foundation-skeleton/plan.md#complexity-tracking).

## Summary

This feature stores the product's first files. A new `Media` module gives each site a
media library: site members upload images and PDFs, browse them, copy their public
addresses and delete them. Visitors receive the files from the site's own host names.
Underneath, a provider-neutral `IFileStorage` abstraction in `Shared.Infrastructure`
ships with its first provider, the local file system, and with a conformance suite
that the S3 and Azure providers will reuse unchanged.

The approach, detailed in [research.md](./research.md):
- **The abstraction**: five operations (create-only atomic write, seekable read, exists,
  idempotent delete, list by prefix). It never exposes paths or URLs (M-02). Keys are
  system-generated, site-first and grammar-checked, so user input can never reach
  storage (M-03).
- **Local provider**: writes to a temporary file and renames it into place for
  atomicity, with mode `0640` (M-04). Startup fails if the location is relative,
  unwritable, or inside the application's own directories (M-05). The image stores
  media on a named volume (M-06).
- **Validation**: the type is detected from the first bytes with six hand-written
  signature checks, and the extension and declared type must agree with it (M-07,
  M-08). The size limit is enforced while reading (M-09). One file per request, as the
  raw body (M-10).
- **Serving**: `/_media/{id}.{ext}` is a reserved public prefix. Lookups go through the
  site filter. Responses pin the exact type, forbid sniffing, sandbox images and are
  cached as immutable (M-11). Previews are authorized on every view (M-12).
- **Deletion and clean-up**: deletion removes the row, then the bytes, and an hourly
  orphan sweep with a grace period catches anything left behind (M-13, M-15).
- **Permissions**: four capabilities. Editors may delete only their own uploads (M-18).

There are no new NuGet or npm packages and no new third-party files.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged.

**Primary Dependencies**: ASP.NET Core MVC (controllers, `FileStreamResult` range
processing, `IHttpMaxRequestBodySizeFeature`), EF Core 10 + Npgsql, and BCL file I/O
(`FileStream`, `File.Move`, `UnixFileMode`). Vue 3, Pinia, vue-router and vue-i18n are
already present, and upload progress uses the browser's `XMLHttpRequest`. Nothing new is
added.

**Storage**:
- PostgreSQL 16+: a new `media` schema with one table ([data-model.md](./data-model.md)).
- Media bytes: in the configured storage provider, which is the local file system at
  `/var/lib/quickbuilder/media` on a named volume in the reference deployment.

**Testing**: xUnit, `WebApplicationFactory` and Testcontainers PostgreSQL, as before,
plus:
- the storage conformance suite;
- a generated set of at least 50 crafted files;
- header assertions on every served file;
- fault-injection sweep tests;
- the two-instance fixture from 002;
- `UpgradeFrom002Tests`;
- `Performance` smoke tests under `docker-compose.perf.yml`;
- vitest + axe for the media screen.

**Target Platform**: Linux containers (amd64 in CI), unchanged. The local provider also
runs on Windows for development.

**Project Type**: Web application: a modular-monolith backend plus the Vue admin SPA it
serves.

**Performance Goals**:
- **Upload**: 10 MB in ≤ 5 s on a LAN. With 10 × 20 MiB uploads in flight, other admin
  requests stay within 2× their idle p95 (SC-008).
- **Serving**: first byte in ≤ 100 ms at p95 with 50 concurrent 1 MB requests (SC-009).
- **Library**: any page of a 10,000-file library in ≤ 1 s (SC-009a).
- **Memory**: uploads and downloads are streamed, never buffered whole.

**Constraints**:
- A deleted file is not found on the next request on every instance, so there is no
  metadata cache (M-14).
- Zero outbound network traffic (FR-031).
- No file names or contents in logs (FR-030).
- Stored bytes are never modified or executed (FR-005, FR-016).

**Scale/Scope**:
- **Library size**: up to 10,000 files per site with a performance target; larger works
  without one.
- **Code**: 1 new module (2 projects) and 1 new shared folder (`Storage/`).
- **Endpoints**: 4 admin endpoints and 1 public endpoint.
- **Admin shell**: 1 new screen.
- **Capabilities**: 4 new ones.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** `Media` ships as `QuickBuilder.Media.Contracts` + `QuickBuilder.Media`, with slices under `Features/`, its own `media` schema, `DbContext` and migration history, and a single `AddMediaModule()`. It reads other modules only through contracts: `IUserDirectory` for uploader names, and the kernel's `IAccessEvaluator` for the delete-own check. Neither Sites nor Identity learns that Media exists. `site_id` and `uploaded_by` have no cross-schema foreign keys. The storage abstraction goes where the constitution names it, `Shared.Infrastructure` ("storage/cache providers"), and contains no media concept (M-01). It takes no ASP.NET Core dependency: the host passes in the directories the local provider must avoid (M-05). |
| II | Easy to Install, Operate and Upgrade | **Complies.** Upload works on a fresh `docker compose up` with no storage configuration (SC-001, M-06). All five new keys have safe defaults and startup validation that names the key (M-05). The migration is additive and automatic. Nothing needs the network. `/health/ready` gains a storage probe, so an instance whose media volume vanishes or turns read-only after start stops reporting ready (M-22). |
| III | Upgrades Never Lose Data | **Complies.** Media bytes live on a named volume that survives replacing the image (SC-005). `UpgradeFrom002Tests` is this release's snapshot-upgrade test. The row holds everything a future export manifest needs (FR-033: name, type, size, time, uploader, key). `docs/operations.md` covers backing up and restoring media with the database. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies, and E-2 is closed.** Media storage is reached only through `IFileStorage`. The provider is chosen by `Storage:Provider`, with the local file system as the default and only provider (M-05). S3-compatible and Azure Blob providers are follow-up features that implement the same contract and pass the same suite ([contracts/storage-provider.md](./contracts/storage-provider.md)). On multi-instance: the constitution's precondition, "once the operator configures shared cache and storage providers", can't be met until a shared provider exists. Media itself is multi-instance-safe: no metadata cache (M-14), atomic writes (M-04), idempotent sweeps (M-15). E-4 (the cache) still blocks advertising multi-instance, and the docs say so. |
| V | Multi-Site Within One Installation | **Complies.** `MediaFile` is `ISiteOwned` with the standard filter. Public lookups can only return the resolved site's files (FR-014). Admin operations name `{siteId}` in the route. "Media paths namespaced by SiteId" is literal: every key begins with the site id (M-03), and so does every temporary file, which lives under `{siteId}/.incoming/` (M-04). The orphan sweep only ever deletes keys under a site prefix, and reports its results per `SiteId` (M-15). Its cross-site row read uses `AcrossAllSites` with a reason and a dedicated test. No cache keys are added. |
| VI | Content Schemas Are Versioned Contracts | **N/A**: no page-tree, collection or binding schema is touched. Media references from pages arrive with Pages. |
| VII | Draft and Published Are Separate Worlds | **N/A by the principle's own scope**: it covers pages, components and collection items. Clarification Q1 confirmed media has no draft state. The public media path only reads (select by id) and never writes. |
| VIII | Rendering Is Server-Side, Cached, Invalidated | **N/A**: no rendering. Media bytes are cached by browsers and proxies through immutable addresses, so publishing never has to invalidate them. The server-side render cache is untouched. |
| IX | Secure by Default | **Complies.** This feature is where the principle's upload rule is delivered: "validated by content type and size and never executed or served from an executable path". Details: <br>• **Validation**: content-based type detection with extension and declared-type agreement (M-07, M-08), and a size limit enforced while reading (M-09). <br>• **Storage**: bytes live outside every application, content and web root, and startup refuses a location inside one. Files have no execute bits (M-04, M-05). <br>• **Serving**: exact type, `nosniff`, a sandbox CSP on images, and nothing executes or transforms the file on the server (M-11). <br>• **Authorization**: every endpoint declares a capability, and the public endpoint joins the anonymous allow-list through this spec (FR-026). <br>• **Names**: cleaned, and never used in keys or paths (M-17). <br>• **Logs**: no file names (FR-030). The path carries no name, so access logs can't leak one. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** Every endpoint has integration tests against real PostgreSQL. The detector, name cleaning, key grammar and options validation have unit tests. The provider has its conformance suite, which covers "each optional infrastructure provider has its own integration test suite" in advance (M-19). The smoke test gains upload and fetch (FR-032). Upload, serving and site isolation are added to the coverage gate's critical paths. |
| XI | Simplicity and Incremental Delivery | **Complies.** No packages (M-07 rejects imaging and MIME libraries). The abstraction has exactly the operations its one consumer uses. There are no speculative items: no read interface for Pages, no quota, no tombstone table, no S3 stub. The feature ships whole in one PR, so no feature flag is needed. |

**Technology Constraints**:
- MVC controllers only, including the public `/_media` endpoint.
- EF Core only. There is no raw SQL: the orphan sweep's batch lookup is a LINQ `Contains`
  over at most 1,000 keys.
- Read paths use `AsNoTracking` projections.
- Admin assets are built into the image, as before.

**Development Workflow**:
- Issue #3 → `specs/003-media-storage` → branch `003-media-storage` → one PR that closes
  #3.
- The branch was created from `main` at `85c5573`, after 002 merged (#7).
- 001's `spec-ids.yml` check resolves `003` to issue #3, which the 001 plan already names
  as E-2's follow-up.

**Operator impact** (required by the workflow section):
- **New configuration keys**: 5, all optional with safe defaults: `Storage:Provider`,
  `Storage:Local:Path`, `Media:MaxFileSizeBytes`, `Media:AllowedTypes` and
  `Media:OrphanGracePeriod`. They are listed in
  [research.md](./research.md#configuration-keys-introduced) and are to be added to
  `docs/configuration.md`.
- **Migrations**: one, creating the `media` schema. It is additive and applied
  automatically.
- **New dependencies**: none.
- **Upgrade notes**:
  1. The image now declares `VOLUME /var/lib/quickbuilder/media`, and the reference
     `docker-compose.yml` mounts the named volume `media-data` there. **Operators with
     their own compose file or orchestration must mount persistent storage at that
     path**, or uploaded files are lost when the container is replaced.
  2. Back up the media volume together with the database. Restoring one without the
     other leaves library entries whose files are missing (shown as unavailable) or
     bytes without entries (removed after 24 hours). See `docs/operations.md`.
  3. `/_media/` on every site host is now served by the application. A reverse proxy
     that handled `/_media` itself must stop doing so.
  4. Multi-instance deployments still aren't supported (E-4). Media would need every
     instance to mount the same directory.

**Post-Phase-1 re-check**: the data model, the two contracts and the quickstart introduce
no violation. E-2 is closed. E-3 is unchanged, and so is E-4. No new exception is
needed. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/003-media-storage/
├── plan.md              # This file
├── spec.md              # Feature specification (clarified)
├── research.md          # Phase 0: decisions M-01 … M-22
├── data-model.md        # Phase 1: media schema, storage objects, types
├── quickstart.md        # Phase 1: validation guide, one section per success criterion
├── contracts/
│   ├── media-api.yaml          # HTTP contract: /api/v1/media/** and the public /_media/{file}
│   └── storage-provider.md     # IFileStorage, key grammar, provider duties, conformance suite
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
src/
├── QuickBuilder.Host/
│   ~ Program.cs                    # + AddFileStorage(forbidden roots), AddMediaModule(),
│   │                               #   Media application part
│   ~ Health/HealthController.cs    # + storage probe in /health/ready (M-22)
│   ~ Routing/RoutePrefixes.cs      # + PublicMedia "/_media": reserved on the public surface,
│   │                               #   still site-resolved (M-11)
│   ~ appsettings.json              # + Storage and Media sections
├── Shared/
│   └── QuickBuilder.Shared.Infrastructure/
│       + Storage/                  # IFileStorage, StorageKey, StoredFile, StoredFileInfo,
│       │                           # StorageKeyExistsException, StorageOptions,
│       │                           # StorageRegistration (AddFileStorage)
│       + Storage/Local/            # LocalFileStorage, LocalStorageOptionsValidator,
│                                   # LocalIncomingCleanupService
└── Modules/
    └── Media/                      # + new module
        ├── QuickBuilder.Media.Contracts/
        │   └── MediaFileDto.cs
        └── QuickBuilder.Media/
            ├── MediaModule.cs      # AddMediaModule(): DbContext, capabilities, slices, sweep
            ├── MediaCapabilities.cs
            ├── MediaOptions.cs
            ├── Domain/             # MediaFile, MediaFileType, MediaFileName
            ├── Infrastructure/     # MediaDbContext, Migrations/, MediaStorageKeys,
            │                       # FileTypeDetector, MediaResponseHeaders,
            │                       # UploadSizeLimitAttribute, MediaErrorResults,
            │                       # MediaOrphanSweepService
            └── Features/
                ├── UploadFile/  ListFiles/  GetFileContent/  DeleteFile/
                └── ServePublicFile/

src/admin/
├── src/
│   + api/media.ts                  # typed client; XHR upload with progress and the request header
│   ~ app/router.ts                 # + /sites/:siteId/media
│   ~ features/sites/SitesView.vue  # + "Media" link per site
│   + features/media/               # MediaLibraryView, UploadArea, MediaList, DeleteDialog
│   ~ i18n/en.json                  # + all new strings, including the privacy notice
└── tests/
    + media*.spec.ts                # axe + keyboard checks

tests/
~ QuickBuilder.ArchitectureTests/   # + Media boundaries; Shared.Infrastructure/Storage references
│                                   #   no module; the anonymous allow-list gains /_media
~ QuickBuilder.UnitTests/           # + Media/: FileTypeDetector, MediaFileName, MediaFileType;
│                                   #   Storage/: StorageKey grammar, options validation
~ QuickBuilder.IntegrationTests/    # + Storage/: conformance suite + local provider;
│                                   #   Media/: upload, crafted-file set, serving headers,
│                                   #   isolation, delete, paging, orphan sweep;
│                                   #   UpgradeFrom002Tests
~ QuickBuilder.SmokeTests/          # + upload and fetch; Performance: media upload, serving, paging

Dockerfile                          # ~ Media csproj COPY lines; media directory, ENV and VOLUME
docker-compose.yml                  # ~ media-data volume on the app service
docs/
~ configuration.md                  # + Storage and Media sections
~ operations.md                     # + Media storage: location, backup and restore, moving,
│                                   #   deleted files and caches, multi-instance caveat
~ contributing.md                   # + "storing files" pointer to contracts/storage-provider.md
specs/002-identity-setup/contracts/authorization.md   # ~ §4 allow-list gains GET|HEAD /_media/{file}
```

**Structure decision**: `Media` follows the layout that `Sites` and `Identity`
established: `Domain/`, `Features/` and `Infrastructure/`, everything `internal`, and one
`Add<Module>Module()`. Framework plumbing that no single slice owns, such as the
response-header policy, the size-limit filter and the sweep, lives in `Infrastructure/`.
The storage abstraction is the first occupant of `Shared.Infrastructure/Storage/`,
beside `Caching/`, as Principle I describes.

Changing 002's `authorization.md` is required by that document's own rule for extending
the anonymous allow-list. The change is justified by this spec's FR-026.

## Complexity Tracking

Status of exceptions carried over from earlier plans, as required by the Governance
section.

| Exception | Status after this feature |
|---|---|
| **E-1** (no authentication on the admin surface) | Closed by 002. |
| **E-2** (no media storage abstraction) | **Closed.** `IFileStorage` is defined against its first consumer, with the local default provider and a conformance suite (FR-019–FR-024). |
| **E-3** (smoke test is not the constitution's full sequence) | Unchanged. The smoke test gains upload and fetch, but create page, publish and view still wait for Pages and Rendering. Issue #4. |
| **E-4** (in-memory cache can't invalidate across instances) | Unchanged. Media adds no cache dependency (M-14). Multi-instance is still not advertised. |

**New exceptions**: none.

## Risks

- **PDFs aren't sandboxed.** Chromium refuses to render PDFs under a CSP `sandbox`, so
  PDFs rely on the pinned type, `nosniff` and the browsers' isolated PDF viewers
  (M-11).
  - *Mitigation*: the manual browser check in quickstart §4 uses a script-bearing PDF. If
    a supported browser ever runs PDF script with the site's privileges, switch PDFs to
    `Content-Disposition: attachment`. That is a one-line change in
    `MediaResponseHeaders`, and the spec allows it.
  - *Merge gate*: SC-004 for PDF is verified by that manual step, not by the automated
    header test. The step must pass in current Chromium and Firefox before the PR is
    approved (tasks: quickstart run).
- **Operators with custom deployments may not mount the volume.** The image declares
  `VOLUME`, so a plain `docker run` creates an anonymous volume. Recreating the container
  then detaches it and the files seem lost.
  - *Mitigation*: the upgrade note (1), the `docs/operations.md` section, and a comment at
    the volume in `docker-compose.yml`. Startup can't reliably detect an unmounted path,
    so nothing is enforced there.
- **Database and volume backups taken at different times.**
  - *Mitigation*: the damage is bounded and self-healing. Entries without bytes show as
    unavailable and answer `404`. Bytes without entries are swept after 24 hours. It is
    documented, not prevented (spec assumption).
- **The shared-directory multi-instance setup isn't tested.** Atomic rename across NFS
  depends on the mount.
  - *Mitigation*: it isn't advertised (E-4), and the S3 provider is the supported answer
    for multiple instances.
- **Issue #3 must exist.** This is the same prerequisite as in earlier features. The
  local GitHub token lacks the `repo` scope, so this couldn't be checked from here. The
  001 plan names #3 as E-2's follow-up.

## Deferred (recorded, not built)

- **S3-compatible and Azure Blob providers**: follow-up features. Each implements
  [contracts/storage-provider.md](./contracts/storage-provider.md) and passes its suite.
- **A read interface for other modules** (for example `IMediaLibrary.FindAsync`): Pages
  adds it when it references media.
- **Protecting files that pages use from deletion, and narrowing Editor deletion
  further**: Pages.
- **Removing grants, bytes and rows when a site is deleted**: this waits for a
  `SiteDeleted` event. The site-first key prefix already makes removing a site's files
  one prefix delete.
- **Directory fan-out in the local provider**: only if a real library outgrows one
  directory. It changes no keys (M-03).
- **SVG support, image transformation and metadata stripping**: out of scope in the spec.
  They need their own spec.
