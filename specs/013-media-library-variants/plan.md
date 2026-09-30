# Implementation Plan: Media Library, Image Variants and Remote Storage Providers

**Branch**: `013-media-library-variants` | **Date**: 2026-10-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-media-library-variants/spec.md`

**Issue**: #13. No exception from earlier plans is open. This feature adds none, and it
removes the last documented caveat on multi-instance deployment: shared media storage.

## Summary

Feature 003 gave each site an upload-and-serve library on the local disk. This feature
makes that library usable at a real site's scale, serves images at a sensible size, and
adds shared storage:
- **Library**: folders, search, filters and sorting; alternative text; a picker built on
  the same browser; usage, with warnings before delete; and replace, which keeps the
  file's identity.
- **Variants**: resized copies at fixed widths, produced once, stored and cached. Published
  pages use them through `srcset`, and state `width` and `height`.
- **Providers**: S3-compatible and Azure Blob behind 003's unchanged `IFileStorage`,
  chosen by configuration, plus a consistency check for moving between providers.

The approach, detailed in [research.md](./research.md):
- **Ownership** (MV-01):
  - **Media** owns folders, search, alternative text, replace, variants and the check.
  - **Pages** owns *usage*, updated in the same transaction as draft saves and publishes,
    and exposes it through `IMediaUsageReader` (MV-05).
  - **Rendering** only changes the image markup (MV-15).
  - `IFileStorage` needs no new operation.
- **Folders** (MV-02): a table plus a per-site `folder_trees` row version that serialises
  structural changes. Delete-when-empty is enforced by `RESTRICT` foreign keys. There are
  no owners (clarification).
- **Search** (MV-03): a normalised `search_text` column (no case, no accents) matched with
  literal `LIKE`, and keyset paging per sort. It needs no PostgreSQL extensions at 10,000
  files per site.
- **Usage and authorization** (MV-05, MV-06):
  - draft and version reference tables in Pages, backfilled by the migration;
  - the server refuses to delete a used file without `confirmInUse`;
  - deleting or replacing a file used by a live page needs `pages.publishing.manage`
    (clarifications);
  - the uploader never changes.
- **Replace** (MV-07, MV-08): a `content_version` on the file. Each version has its own
  storage key and a canonical address (`{id}-v{n}.{ext}`, and `{id}-v{n}-w{w}.{ext}` for
  variants), so caches stay immutable. Version-1 addresses from 003 are unchanged.
- **Change event** (MV-17): `Media.FileChanged` (replaced, dimensions, variants) tells
  Pages to bump the render generations of exactly the live pages involved. That makes
  replace, backfilled dimensions and failed variants visible at the next request on every
  instance (012 PR-05).
- **Variants** (MV-09 … MV-14):
  - **Library**: NetVips (MIT), over the libvips natives (LGPL, dynamically loaded),
    hardened to five loaders.
  - **Metadata**: header-only reading of dimensions at upload, with a 100-megapixel limit.
  - **Output**: same format as the original, auto-rotated, sRGB, no metadata.
  - **Production**: on demand, single-flight in process, and exactly once across instances
    through `variant_claims`; bounded and time-limited, with the original served
    `no-store` on failure.
  - **Thumbnails**: produced eagerly (clarification).
- **Providers** (MV-18 … MV-20):
  - AWSSDK.S3 with `If-None-Match` create-only writes (verified at start-up), spooled
    uploads and seekable ranged reads;
  - Azure.Storage.Blobs with conditional commits and managed identity;
  - both validated at start-up with the key named, and never logging secrets.
- **Consistency check** (MV-21): a listing-versus-rows comparison per site. It runs in the
  background after start-up, one run at a time across instances, and on demand from the
  admin shell or the CLI (`media check-storage`) (clarifications).

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged.

**Primary Dependencies** (justified in research MV-22):
- **Backend**: ASP.NET Core MVC, EF Core 10 + Npgsql, as before, plus:
  - **NetVips 3.x** with **NetVips.Native** for `linux-musl-x64`, `linux-musl-arm64`,
    `linux-x64` and `win-x64`;
  - **AWSSDK.S3 4.x**, **Azure.Storage.Blobs 12.x** and **Azure.Identity 1.x**;
  - **Microsoft.Extensions.Configuration.Binder**.
- **Tests**: **Testcontainers.Minio** and **Testcontainers.Azurite**.
- **Admin**: Vue 3, Pinia, vue-router and vue-i18n. **Nothing new.**

**Storage**: PostgreSQL 16+.
- **`media` schema**: `media_files` gains 12 columns and new indexes. New tables:
  `folders`, `folder_trees`, `variant_claims`, `storage_checks` and
  `storage_check_missing`.
- **`pages` schema**: new tables `draft_media_references` and `version_media_references`.
- **Bytes**: the configured provider: Local (default), S3 or AzureBlob. Variants live
  under `{site}/media-variants/`.

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers (PostgreSQL, MinIO, Azurite),
and vitest with Testing Library and axe. The suites are listed in research MV-25. The main
ones:
- **Providers**: `FileStorageConformanceTests`, run unchanged for Local, S3 and Azure
  (SC-010), with `S3StartupValidationTests`, `AzureStartupValidationTests` and
  `SecretRedactionTests` (SC-011).
- **Variants**: `VariantProductionExactlyOnceTests` (SC-005) and `VariantServingTests`.
- **Library**: `MediaUsageTests` (SC-008), `DeleteInUseTests` (SC-007, SC-015),
  `ReplaceFileApiTests` (SC-009) and `StorageCheckTests` (SC-014).
- **Multi-instance and upgrade**: `MediaAcrossInstancesTests` and `UpgradeFrom012Tests`
  (SC-012).
- **Performance**: `MediaPerformanceTests` (SC-001, extended) and
  `VariantPerformanceTests` (SC-004), in the smoke project's Performance category.

**Target Platform**: Linux containers (Alpine, amd64 and arm64). The libvips natives ship
in the image, and nothing is installed with `apk`.

**Project Type**: Web application: the modular-monolith backend, the Vue admin SPA and the
rendered public site.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Library**: every page and search at 10,000 files in under 1 s (SC-001).
- **Variants**: a cached variant's first byte in under 100 ms at p95 with 50 concurrent
  requests; a first-time variant of a 20 MiB image in under 3 s (SC-004).
- **Thumbnails**: ready within 5 s of the upload (FR-028).
- **Usage**: current within 5 s. In practice it is immediate, because it is transactional
  (SC-008).
- **Budgets**: the render budgets from 012 are unchanged. The image markup is attribute
  text only.

**Constraints**:
- **Offline**: a default install still makes zero outbound requests. A remote provider
  contacts only its endpoint, plus the platform's identity endpoint if ambient identity is
  chosen (MV-20).
- **Addresses**: every 003 address stays valid while its file is at version 1. Old versions
  are `not_found` from the commit of a replacement.
- **Logs**: no names, alternative text, folder names or secrets (FR-041).
- **Decoding**: native decoding only after content-type detection and the pixel limit
  (MV-09).
- **API**: all changes are additive, except that deleting a used file now needs
  `confirmInUse=true` (the admin shell is updated in the same PR).

**Scale/Scope**:
- **Code**: 0 new modules and 0 new projects.
- **Data**: 7 new tables and 1 changed, in 2 migrations.
- **Endpoints**: 15 new or changed admin endpoints; 1 changed public endpoint (the address
  grammar).
- **Capabilities**: 1 new (`media.storage.check`). The others are reused.
- **Events**: 1 new (`Media.FileChanged`).
- **Providers**: 2 new.
- **Admin shell**: 4 new components (browser, folder tree, details panel, replace dialog),
  1 new screen (**Settings → Storage**) and 3 changed ones (library, delete dialog, picker).
- **Stated limits**: 10,000 files and 1,000 folders per site, depth 8, 100 files per bulk
  move, and 1–10 variant widths.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** <br>• **No new module**: Media gains the slices `ListFiles` (changed), `GetFile`, `UpdateFile`, `MoveFiles`, `ReplaceFile`, `GetFileUsage`, `GetFileVariant`, `ServePublicFile` (changed), `ListFolders`, `CreateFolder`, `UpdateFolder`, `DeleteFolder`, `GetStorageCheck` and `StartStorageCheck`. Pages gains no endpoint: it gains `IMediaUsageReader` and reference upkeep in existing slices. <br>• **Controllers**: thin, with in-house handlers. <br>• **Cross-module**: Media → `Pages.Contracts` (usage reader, capability names); Pages → `Media.Contracts` (events, as before). The new event goes through the Media outbox. No transaction spans modules. The delete-versus-publish race is accepted, not closed with a cross-module lock (MV-06). <br>• **Data**: each table stays in its owner's schema. |
| II | Easy to Install, Operate and Upgrade | **Complies.** <br>• **Defaults**: no configuration is needed. Local stays the default, and every new key is optional and validated at start-up with the key named. <br>• **Validation**: remote providers fail fast on missing settings, bad credentials, missing buckets or containers, or missing conditional writes (FR-033). <br>• **Migrations**: two, forward-only and automatic, including the usage backfill (MV-26). <br>• **Offline**: the default is unchanged; outbound traffic happens only by operator choice (MV-20). <br>• **Health**: remote providers implement the existing storage readiness probe. |
| III | Upgrades Never Lose Data | **Complies.** <br>• **Upgrade**: `UpgradeFrom012Tests`: every file, address and byte is intact, and dimensions and usage are backfilled. <br>• **Export**: folders, alternative text and versions are recorded for the future media manifest (FR-044). <br>• **Moving providers**: a documented copy plus the consistency check (MV-21), so a move is verifiable before traffic switches. |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** <br>• **Providers**: exactly the constitution's named optional providers, behind the existing abstraction, selected by configuration. PostgreSQL is still the only required dependency. <br>• **Multi-instance**: now supported and documented with S3 or Azure (FR-036). Rendering needs no shared cache (012 closed E-4). `MediaAcrossInstancesTests` proves media works. <br>• **Background work**: the thumbnail worker, backfill, sweeps and check are built in, with no external queue. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Site ownership**: every new site-owned table (`folders`, `folder_trees`, `variant_claims`, `storage_check_missing`, and both Pages reference tables) carries `site_id` with a global filter. `storage_checks` is installation-level by nature. <br>• **Keys**: variant keys start with the site (`{site}/media-variants/…`). <br>• **Filter bypasses**: the report's cross-site read and the backfill use `AcrossAllSites` with a reason, and each has a test. `MediaIsolationProbe` covers folders, variants, usage and the report. <br>• **Public requests**: variants resolve only on the owning site's host names. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• **Schema**: unchanged, and no `schemaVersion` bump: stored documents do not change, because alternative text is copied into blocks, never linked. <br>• **Rendering contract**: page-schema.md §9 gains the image attributes (contracts/public-media.md §2), with golden files regenerated in the same PR. |
| VII | Draft and Published Are Separate Worlds | **Complies.** <br>• **Documents**: no page or component document changes without a publish. <br>• **Media outside the rule**: media files are outside the draft and published separation (003 assumption). Replace and delete affect live pages by the spec's clarified design. They are restricted to members who may publish, so Editors still cannot change what visitors see. <br>• **Read-only path**: the renderer's readers, including `PublicMediaReader`, keep their read-only connections. The public `/_media` endpoint already used the Media write context in 003. It now also writes variant bytes and `variant_claims` rows, which are Media's own derived data, never page, component or item content. 012's `PublicPathReadOnlyTests` still hold. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** <br>• **Invalidation**: the page → media edges already in 012's graph now also fire on replace, dimension backfill and variant failure (`Media.FileChanged`). Exactly the affected pages are bumped, on every instance. <br>• **Budgets**: images get `srcset`, `sizes`, `width` and `height`, with no JavaScript, reducing bytes (SC-003) and layout shift (SC-006). The 012 budgets are unaffected. |
| IX | Secure by Default | **Complies.** <br>• **Decoding**: native decoding is hardened: loaders are restricted, untrusted operations blocked, the pixel limit is checked before any decode, and each production has a timeout (MV-09). <br>• **Serving**: variants get 003's serving headers, and canonical-only addresses prevent arbitrary resize requests. <br>• **Authorization**: server-side on every new action, including the live-use rule. <br>• **Secrets**: from configuration or the environment only, never logged or echoed (`SecretRedactionTests`). <br>• **Logs**: no names or alternative text. <br>• **Privacy**: variants drop GPS and EXIF. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: every new or changed one has integration tests on PostgreSQL. <br>• **Providers**: each has its own suite plus the unchanged conformance suite, on MinIO and Azurite containers in CI. <br>• **Rendering**: golden files for the new image markup. <br>• **Unit tests**: domain rules (folders, addresses, variant planning, metadata). <br>• **Smoke test**: extended with a variant fetch (FR-043). <br>• **Coverage**: Media and Pages stay in the 80 % gate for their critical paths. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No abstraction change**: `IFileStorage` is untouched. <br>• **Not built**: format conversion, arbitrary sizes, trigram search, a copy command, a shared cache and a generic job framework (research "Things deliberately not built"). <br>• **New dependencies**: each is justified with its licence and alternatives (MV-22). <br>• **Delivery**: ships in one PR, so no feature flag. If it is split, the S3 and Azure providers are inert until selected, and the library features stand on their own. |

**Technology Constraints**:
- MVC controllers only, including the public variant action (the existing `/_media`
  catch-all).
- EF Core only:
  - the folder-tree revision and claims use row versions and conditional `ExecuteUpdate`;
  - the one piece of hand-written SQL is the migration's jsonpath backfill of usage
    (MV-05), run once, with no user input, and checked against `DependencyExtractor` by a
    test.
- No Node process. NetVips is an in-process native library.

**Development Workflow**:
- Issue #13 → `specs/013-media-library-variants` → branch `013-media-library-variants` → one
  PR that closes #13.
- Clarify has run: 2 questions in specify and 5 in clarify, all answered. Analyze follows
  tasks.
- **Contract changes in the same PR**:
  - `docs/contracts/page-schema.md` §9 (image markup);
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (`Media.FileChanged`);
  - `specs/002-identity-setup/contracts/authorization.md` §2 (`media.storage.check`);
  - `specs/003-media-storage/contracts/storage-provider.md` (pointer to the new providers).
- **Spec correction during planning**: FR-036, US3 scenario 3 and one assumption said
  multi-instance needs a shared cache. Feature 012 closed E-4, so they now say no shared
  cache is required.

**Operator impact**:
- **New configuration keys** (all optional; full table in research "Configuration keys"):
  - `Storage:Provider` gains `S3` and `AzureBlob`;
  - `Storage:S3:{Bucket, Region, ServiceUrl, ForcePathStyle, KeyPrefix, AccessKeyId,
    SecretAccessKey}`;
  - `Storage:AzureBlob:{Container, ConnectionString, AccountUrl, KeyPrefix}`;
  - `Media:MaxImagePixels` (100,000,000);
  - `Media:Variants:{Widths, MaxConcurrent, Timeout}`.
- **Migrations**: 2, additive and automatic: `AddLibraryAndVariants` (Media) and
  `AddMediaReferences` (Pages, with the usage backfill).
- **New dependencies**: NetVips and its natives, AWSSDK.S3, Azure.Storage.Blobs,
  Azure.Identity and Configuration.Binder. A `THIRD-PARTY-NOTICES.md` is added.
- **Upgrade notes**:
  1. Existing files keep their addresses and appear at the top level of each library.
     Dimensions and thumbnails are filled in by a background job over the first minutes
     after the upgrade. Live pages pick up `width`, `height` and `srcset` as each image is
     processed. Search ignores letter case straight away, and also ignores accents once the
     search-text backfill has run, a few seconds per site.
  2. Deleting a file that pages use now shows a warning. API clients must pass
     `confirmInUse=true`. Editors can no longer delete or replace a file that a live page
     shows; a Designer or Administrator must.
  3. Published images now load resized variants. The first visitor to need each size may
     wait up to about 3 seconds; everyone after that is served from storage. Storage use
     grows by roughly 30–60 % of image bytes as variants accumulate.
  4. Multi-instance deployment is now supported with `Storage:Provider=S3` or `AzureBlob`.
     To move existing files, follow "Moving media between providers" in
     `docs/operations.md`, then check **Settings → Storage**.
  5. The image is about 25 MB larger (libvips).
  6. New CLI command: `media check-storage`.

**Post-Phase-1 re-check**:
- media-api.yaml puts a capability on every admin operation, and the public route stays
  anonymous and site-resolved;
- readers.md adds one contract reference (Media → Pages.Contracts) with no cycle;
- every new site-owned table is filtered;
- events carry ids only;
- `IFileStorage` is unchanged.

No violation and no new exception. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/013-media-library-variants/
├── plan.md                      # This file
├── spec.md                      # Feature specification (clarified: 2 + 5 questions)
├── research.md                  # Phase 0: decisions MV-01 … MV-26
├── data-model.md                # Phase 1: tables, rules, state changes, events, error codes
├── quickstart.md                # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── media-api.yaml           # HTTP: listing/search, folders, alt text, move, replace, usage, variants, storage check
│   ├── public-media.md          # Address grammar, headers, responsive image markup (page-schema §9)
│   ├── storage-providers.md     # S3 and Azure providers, key layout, validation, consistency check
│   └── readers.md               # IPublicMediaReader change, IMediaUsageReader, Media.FileChanged, capabilities
├── checklists/
│   └── requirements.md
└── tasks.md                     # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
Directory.Packages.props                  # ~ + NetVips, NetVips.Native.*, AWSSDK.S3, Azure.Storage.Blobs,
                                          #   Azure.Identity, Configuration.Binder, Testcontainers.Minio/Azurite
+ THIRD-PARTY-NOTICES.md                  # libvips (LGPL) and bundled codec notices
Dockerfile                                # ~ runtime: native assets for linux-musl-{x64,arm64} copied with the app
+ docker-compose.storage.yml              # dev/test overlay: MinIO, Azurite, a second app instance
.github/workflows/ci.yml                  # ~ provider suites run in `test` (Docker already present)
src/QuickBuilder.Host/appsettings.json    # ~ + Storage:S3/AzureBlob (empty), Media:MaxImagePixels, Media:Variants

docs/
~ configuration.md                        # every new key, defaults, secrets via environment
~ operations.md                           # providers, permissions, multi-instance (now supported), moving
                                          #   between providers, consistency check, variant storage growth
~ contracts/page-schema.md                # §9 image markup (contracts/public-media.md §2)

src/
├── Shared/QuickBuilder.Shared.Infrastructure/
│   ~ QuickBuilder.Shared.Infrastructure.csproj   # + AWSSDK.S3, Azure.Storage.Blobs, Azure.Identity, Binder
│   ~ Storage/StorageOptions.cs           # SupportedProviders = Local, S3, AzureBlob; S3/AzureBlob settings
│   ~ Storage/StorageRegistration.cs      # switch on provider (MV-20)
│   + Storage/S3/S3FileStorage.cs  S3RangeReadStream.cs  S3UploadSpool.cs  S3StorageOptionsValidator.cs
│   │           S3AbandonedUploadCleanupService.cs
│   + Storage/AzureBlob/AzureBlobFileStorage.cs  AzureBlobStorageOptionsValidator.cs
│   + Storage/RemoteProbe.cs              # shared create/duplicate/read/delete probe for validation and health
├── Modules/Media/
│   ├── QuickBuilder.Media.Contracts/
│   │   ~ IPublicMediaReader.cs           # PublicMediaFile + Width, Height, Variants; + PublicMediaVariant
│   │   + Events/FileChanged.cs
│   └── QuickBuilder.Media/
│       ~ QuickBuilder.Media.csproj       # + NetVips(.Native.*); + ref Pages.Contracts
│       ~ MediaModule.cs  MediaOptions.cs  MediaCapabilities.cs (+ media.storage.check)
│       ~ Domain/MediaFile.cs             # mutable operations (data-model §3.1)
│       + Domain/MediaFolder.cs  FolderTree.cs  MediaAddress.cs  VariantPlan.cs  SearchText.cs
│       + Imaging/ImageProcessor.cs  ImageMetadataReader.cs  VariantEncoder.cs  NetVipsSetup.cs
│       + Variants/VariantService.cs  VariantClaims.cs  ThumbnailQueue.cs  ThumbnailWorker.cs
│       + Infrastructure/ImageMetadataBackfillService.cs  StorageConsistencyCheckService.cs
│       │                StorageCheckRunner.cs  MediaCommands.cs (media check-storage)
│       ~ Infrastructure/MediaDbContext.cs  MediaStorageKeys.cs (+ versions, variant keys)
│       │                MediaOrphanSweepService.cs (+ variant pass)  MediaFileMapper.cs  PublicMediaReader.cs
│       │                MediaResponseHeaders.cs (versioned ETag, no-store fallback)
│       + Infrastructure/Migrations/<ts>_AddLibraryAndVariants.cs
│       ~ Features/ListFiles/  UploadFile/  DeleteFile/  ServePublicFile/
│       + Features/GetFile/  UpdateFile/  MoveFiles/  ReplaceFile/  GetFileUsage/  GetFileVariant/
│       + Features/Folders/ListFolders/  CreateFolder/  UpdateFolder/  DeleteFolder/
│       + Features/StorageCheck/GetStorageCheck/  StartStorageCheck/
├── Modules/Pages/
│   ├── QuickBuilder.Pages.Contracts/
│   │   + Media/IMediaUsageReader.cs  MediaUsage.cs
│   │   + PageContentCapabilities.cs
│   └── QuickBuilder.Pages/
│       + Domain/DraftMediaReference.cs  VersionMediaReference.cs
│       + Infrastructure/MediaUsageReader.cs
│       + Infrastructure/Migrations/<ts>_AddMediaReferences.cs      # + jsonpath backfill
│       ~ Infrastructure/PagesDbContext.cs  DraftWrites.cs          # draft references
│       ~ Features/Publish/ Rollback/ RunDueSchedule/ (version references)  DeletePage/ DeleteComponent/
│       ~ Features/RefreshPagesShowingMedia/   # + IIntegrationEventHandler<FileChanged>
│       ~ PagesCapabilities.cs  PagesModule.cs
└── Modules/Rendering/QuickBuilder.Rendering/
    ~ Rendering/Blocks/MediaRenderers.cs  # ImageRenderer: srcset, sizes, width, height
    ~ Domain/RenderContext.cs  Rendering/Blocks/LayoutRenderers.cs   # track the Section content width

src/admin/
├── src/
│   ~ api/media.ts                        # client for contracts/media-api.yaml
│   + features/media/MediaBrowser.vue  FolderTree.vue  MediaDetailsPanel.vue  ReplaceDialog.vue
│   │               UsageList.vue  AltTextEditor.vue  lastFolder.ts
│   ~ features/media/MediaLibraryView.vue  MediaList.vue  UploadArea.vue  DeleteDialog.vue
│   + features/settings/StorageSettingsView.vue
│   ~ editor/panel/controls/MediaControl.vue    # picker dialog, alt-text copy, last folder
│   ~ editor/blocks/image/ImageBlock.vue        # srcset/sizes/width/height with admin variant URLs
│   ~ app/router.ts  i18n/en.json
└── tests/
    + media/MediaBrowser.spec.ts  FolderTree.spec.ts  MediaDetailsPanel.spec.ts  ReplaceDialog.spec.ts
    │       DeleteDialog.spec.ts  StorageSettingsView.spec.ts
    ~ mediaLibraryView.spec.ts  editor/MediaControl.spec.ts  editor/CanvasMarkupParity.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/         # Media → Pages.Contracts allowed; NetVips only in Media
+ QuickBuilder.UnitTests/Media/           # MediaFolderTreeTests, SearchTextTests, ListingCursorTests, MediaAddressTests,
│                                         #   VariantPlanTests, ImageMetadataTests, VariantEncodingGoldenTests
+ QuickBuilder.UnitTests/Storage/S3RangeReadStreamTests.cs
~ QuickBuilder.UnitTests/Rendering/       # ImageRendererTests; golden files regenerated
~ QuickBuilder.IntegrationTests/
│   (Storage/FileStorageConformanceTests.cs is already the abstract base; LocalFileStorageTests derives from it)
│   + Storage/S3FileStorageConformanceTests.cs
│   │         AzureBlobFileStorageConformanceTests.cs  S3StartupValidationTests.cs
│   │         AzureStartupValidationTests.cs  SecretRedactionTests.cs
│   + Media/MediaFolderApiTests.cs  MediaListingApiTests.cs  AltTextApiTests.cs  MediaUsageTests.cs
│   │       DeleteInUseTests.cs  ReplaceFileApiTests.cs  VariantServingTests.cs
│   │       VariantProductionExactlyOnceTests.cs  ThumbnailLatencyTests.cs  MediaFileChangedRefreshTests.cs
│   │       StorageCheckTests.cs  MediaAcrossInstancesTests.cs  UpgradeFrom012Tests.cs
│   + Pages/MediaReferenceBackfillTests.cs
│   ~ Isolation/Probes/MediaIsolationProbe.cs  Observability/LoggingTests.cs
│   ~ Authorization/AuthorizationDeclarationTests.cs
+ tests/fixtures/media/                   # photo-6000.jpg, logo.png, logo-v2.png, anim.gif, anim.webp,
                                          #   huge-pixels.png, brochure-été.pdf, photo.avif
~ QuickBuilder.SmokeTests/                # journey: + variant fetch; + VariantPerformanceTests;
                                          #   MediaPerformanceTests: + name sort and search
specs/001-foundation-skeleton/contracts/integration-events.md   # ~ + Media.FileChanged
specs/002-identity-setup/contracts/authorization.md             # ~ §2 + media.storage.check
specs/003-media-storage/contracts/storage-provider.md           # ~ pointer to 013 providers and key layout
```

**Structure decision**:
- **Media** gains `Imaging/` (NetVips wrapped behind three small internal classes, so
  nothing else touches the native library) and `Variants/` (production, claims and the
  thumbnail queue), beside its existing `Domain/`, `Features/` and `Infrastructure/`.
- **Storage providers** sit in Shared.Infrastructure next to the local provider, as the
  constitution places them.
- **Pages** gains no slice, only reference upkeep and the reader.
- **Admin**: `MediaBrowser.vue` is shared by the library screen and the picker.

## Complexity Tracking

Status of exceptions carried over from earlier plans, as the Governance section requires.

| Exception | Status after this feature |
|---|---|
| **E-1**, **E-2**, **E-3**, **E-4** | Closed earlier. With remote providers, the "media still needs shared storage" note left by 003 and 012 is resolved: multi-instance is now documented as supported. |
| **E-5** (no browser in the release smoke test) | Unchanged. The smoke test reads HTML and fetches a variant. The media UI is covered by vitest and quickstart §1–§5, until #25. |

**New exceptions**: none.

## Risks

- **AVIF support or speed in the shipped natives**: AVIF encoding is slow on 2 vCPU, and a
  native build might lack the codec.
  - *Mitigation*: effort 2 (MV-11) and the 10 s timeout with the original as fallback. The
    start-up self-check disables AVIF variants gracefully (MV-09). The first task in tasks.md, a spike,
    measures a 20 MiB AVIF on the reference setup before the rest is built.
- **Native decoder vulnerabilities** in libvips or its codecs.
  - *Mitigation*: only five loaders, untrusted operations blocked, decoding only after
    content detection and the pixel limit, time limits, and NetVips.Native pinned in
    central package management and updated with security releases (noted in
    `docs/contributing.md`).
- **S3-compatible services without conditional writes** (older MinIO, some appliances).
  - *Mitigation*: start-up refuses them with a clear message, rather than running with a
    silently weaker guarantee. The documentation lists the tested services (AWS, MinIO,
    R2, Azurite for Azure).
- **Delete racing a publish** (MV-06): a page may be published using a file at the moment
  it is deleted.
  - *Mitigation*: the renderer already omits missing images (012). Documented, not locked.
- **`sizes` overestimates inside multi-column containers** (MV-15): browsers may fetch one
  size larger than needed.
  - *Mitigation*: bounded by the Section width, and never the original. Per-column
    arithmetic is recorded as a possible refinement.
- **Variant storage growth**: up to six extra objects per image.
  - *Mitigation*: produced only on demand (except thumbnails), swept when stale, and the
    growth is stated in the upgrade notes.
- **Backfill load after upgrade** on large installations (reading every image header and
  producing thumbnails).
  - *Mitigation*: batches of 50, bounded by `Media:Variants:MaxConcurrent`, running after
    readiness. The site stays fully usable meanwhile; pages simply lack `srcset` until
    processed.
- **Listing availability checks on remote storage**: the listing issues up to 100 `HEAD`
  requests per page (003's 8-way parallel check).
  - *Mitigation*: measured by `MediaPerformanceTests` against MinIO. If needed, fall back to
    the latest consistency check's results for remote providers. That fallback is recorded,
    not built.

## Deferred (recorded, not built)

- **Format conversion** (WebP or AVIF for JPEG and PNG originals) with `Accept`
  negotiation, cropping and focal points.
- **A built-in provider-to-provider copy**, CDN integrations, and signed or expiring URLs.
- **Trigram search** beyond 10,000 files per site.
- **Separate storage-provider projects** to keep the cloud SDKs out of
  Shared.Infrastructure's dependency graph. This needs a constitution amendment (MV-01).
- **File renaming, tags, quotas, a recycle bin**, and moving files between sites.
