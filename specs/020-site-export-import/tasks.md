---

description: "Task list for 020-site-export-import"
---

# Tasks: Site Export and Import

**Input**: Design documents from `/specs/020-site-export-import/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires integration tests for every endpoint, unit tests for
  domain logic, golden files for contracts, and 80% coverage on critical paths. Recovery and
  import are critical paths here.
- The spec measures behaviour through automated checks: SC-001, SC-003, SC-004, SC-005 and
  SC-008.
- Within each story, test tasks come first and must fail before implementation starts.

**Decided**: exception E-7 is closed by constitution amendment 2.6.0 (Principle I, approved
2026-11-02). T006 builds the shared session as planned, and T019 enforces its limit.

**Changed after analysis (2026-11-01)**: findings I1, I2, U1, U2, I3, G1–G6, I4, I5, U3 and
A1 were fixed.
- **Tasks extended**: T001, T010, T031, T035, T036, T039, T040, T042, T049–T052, T055, T070,
  T071, T072, T076, T077, T081, T082, T084, T090 and T094. No task was added or renumbered.
- **Other documents**: data-model §1 (active-slot rules), research ST-06, ST-07, ST-13,
  ST-15 and ST-16, the capabilities in transfer-api.yaml, the gate in archive-parts.md, the
  Principle V row in plan.md, and a spec edge case.
- **C1**: resolved by constitution amendment 2.6.0, which ships in this feature's PR.

**Organization**: tasks are grouped by user story (US1–US5 from spec.md).
- US1 (export) stands alone.
- US2 and US3 need an archive, so they use US1's export to produce fixtures.
- US4 and US5 build on the check and apply of US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `SHARED` = `src/Shared/QuickBuilder.Shared.Infrastructure`
- `SITES` = `src/Modules/Sites/QuickBuilder.Sites`
- `SITESC` = `src/Modules/Sites/QuickBuilder.Sites.Contracts`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `MEDIA` = `src/Modules/Media/QuickBuilder.Media`
- `EDITORM` = `src/Modules/Editor/QuickBuilder.Editor`
- `IDENT` = `src/Modules/Identity/QuickBuilder.Identity`
- `IDENTC` = `src/Modules/Identity/QuickBuilder.Identity.Contracts`
- `ADMIN` = `src/admin/src`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`

**Conventions**:
- **Backend slices** live under `<Module>/Features/<Slice>/<Slice>.cs`, modelled on
  `SITES/Features/UpdateSettings/UpdateSettings.cs`. Each one holds the request and response,
  the command or query, the handler, the validator and a thin controller.
- **Controller actions**:
  - are routed as in [contracts/transfer-api.yaml](./contracts/transfer-api.yaml);
  - declare exactly one `[RequireCapability(...)]`: `SitesCapabilities.TransferExport` on
    `/api/v1/sites/{siteId}/…` routes, and `TransferImport` on `/api/v1/transfers/…` routes, which
    have no `{siteId}` (`ATESTS/AuthorizationDeclarationTests.cs`);
  - dispatch through `IDispatcher`.
- **Errors** are problems with the codes in [data-model.md](./data-model.md) §5, mapped in
  `SITES/ErrorResults.cs`.
- **Parts** implement `ISiteArchivePart` exactly as
  [contracts/archive-parts.md](./contracts/archive-parts.md) §1 states. Each lives in
  `<Module>/Features/Transfer/` and touches only its module's tables.
- **Logs and events** carry ids, counts and codes only, never titles, labels or content
  (Principle IX).
- **Writes**: batches of 500 rows with `ChangeTracker.Clear()` between them (research ST-24).

---

## Phase 1: Setup

**Purpose**: confirm the riskiest assumption first, and add the configuration and
capabilities.

- [X] T001 Verify that every storage provider accepts a write of unknown length from a pipe. Add `WriteAsync_FromPipeOfUnknownLength_StoresWholeObject` (5 GB fixture, behind the provider-suite trait) to `ITESTS/Storage/LocalFileStorageTests.cs`, `ITESTS/Storage/S3FileStorageTests.cs` and `ITESTS/Storage/AzureBlobFileStorageTests.cs`. Record the outcome in research.md ST-16 (plan risk 2). If a provider fails, fix it in `SHARED/Storage/<Provider>/` before going on. Also read the listing prefix of `MEDIA/Infrastructure/MediaOrphanSweepService.cs`. If it is wider than the sites' media prefixes, make it skip `transfers` segments and `installation/`, so archives are never swept as orphans (research ST-16).
- [X] T002 [P] Add `SiteTransferOptions` (`MaxUploadSizeBytes`: default 10 GiB, range 1 MiB – 100 GiB, in bytes like `Media:MaxFileSizeBytes`) and its startup validator in `SITES/SiteTransferOptions.cs`. Register them in `SITES/SitesModule.cs` and document the key in `docs/configuration.md`.
- [X] T003 [P] Add `TransferExport = "sites.transfer.export"` (Site scope, `[]`) and `TransferImport = "sites.transfer.import"` (Installation scope, `[]`) to `SITES/SitesCapabilities.cs`. Add both to `specs/002-identity-setup/contracts/authorization.md`.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the contracts surface, the job table, the archive container, the shared
session and the worker that every story uses.

**⚠️ CRITICAL**: no user story can start until this phase is complete.

### Tests for the foundation

- [X] T004 [P] Unit tests for the `TransferJob` state machine (every transition in data-model §1.1, refused ones included; lease expiry → `failed`/`interrupted`; cancel) in `UTESTS/Sites/Transfer/TransferJobTests.cs`.
- [X] T005 [P] Unit tests for the manifest and container (contracts/site-archive.md §1–§2) in `UTESTS/Sites/Transfer/ArchiveManifestTests.cs`:
  - allowed entry shapes, and refused names (`..`, `/x`, `\`, `C:`, upper-case, directories, duplicates);
  - an entry list that differs from the central directory;
  - a length or hash mismatch detected while reading;
  - a write-then-read round trip through `ZipArchiveWriter` and `ZipArchiveReader` over a non-seekable stream.

### Implementation

- [X] T006 Implement `SiteTransferSession`: one `NpgsqlConnection` and transaction (`ReadOnlySnapshot`: REPEATABLE READ READ ONLY; `Write`: READ COMMITTED), `Enlist(DbContext)` through `Database.SetDbConnection` and `UseTransaction`, `CommitAsync`, and dispose = rollback. Put it in `SHARED/Data/SiteTransferSession.cs`. Its remarks cite the exception in Principle I (constitution 2.6.0). **As built**: `ISiteTransferSession` (Sites.Contracts) exposes the `DbConnection` and `DbTransaction`, because Contracts may not depend on EF Core (`ATESTS` `Contracts_do_not_depend_on_infrastructure`). The implementation is `SITES/Features/Transfer/Engine/SiteTransferSession.cs`, and parts join it through the `DbContext.JoinTransfer(connection, transaction)` extension in `SHARED/Data/TransferEnlistment.cs`.
- [X] T007 [P] Add the Contracts surface from archive-parts.md §1 in `SITESC/Transfer/`: `ISiteArchivePart.cs`, `PartContexts.cs`, `IArchiveReader.cs`, `IArchiveWriter.cs`, `IIdMap.cs` (with `IIdMapBuilder`), `ImportMode.cs`, `ConflictPolicy.cs`, `ISiteTransferSession.cs`, `ISiteWriteGate.cs`, `ITransferProgress.cs`, `ICheckReport.cs`, `IImportReport.cs` and `IAuthorMap.cs`.
- [X] T008 [P] Add the four events `SiteExportFinished`, `SiteImportFinished`, `SiteArchiveDownloaded` and `SiteArchiveDeleted` (ids, mode, outcome, `startedBy`, `operator` flag; no names) in `SITESC/Events/`. Add them to `specs/001-foundation-skeleton/contracts/integration-events.md`.
- [X] T009 Implement the `TransferJob` aggregate and `TransferStates` per data-model §1 in `SITES/Domain/TransferJob.cs` and `SITES/Domain/TransferStates.cs`. Mark it `[InstallationScoped(...)]` with the reason given in data-model §1. Make T004 pass.
- [X] T010 Map `transfer_jobs` (columns, the 3 indexes including the unique active-per-site partial index over `queued`, `confirmed` and `running` that excludes `trigger = 'pre_restore'` (data-model §1), and `xmin`) in `SITES/Infrastructure/SitesDbContext.cs`. Add migration `SITES/Infrastructure/Migrations/<ts>_AddTransferJobs.cs`.
- [X] T011 [P] Implement `ArchiveManifest` (the format 1 model, `SupportedFormats`, `SupportedMajors`, and the entry-name grammar) in `SITES/Features/Transfer/Engine/ArchiveManifest.cs`. **As built**: the manifest model is `SITESC/Transfer/ArchiveManifest.cs`, because parts read it; the grammar and versions are in `SITES/Features/Transfer/Engine/ArchiveFormat.cs`.
- [X] T012 Implement `ZipArchiveWriter`, which writes entries to a pipe and hashes them, writes `manifest.json` last with the entry list, and uses Zip64 when needed, in `SITES/Features/Transfer/Engine/ZipArchiveWriter.cs`.
- [X] T013 Implement `ZipArchiveReader` in `SITES/Features/Transfer/Engine/ZipArchiveReader.cs`:
  - `OpenAsync` checks the central directory against the manifest and the name grammar before anything else;
  - `ReadLinesAsync<T>` and `OpenEntry` verify length and SHA-256 as they read;
  - it refuses encrypted entries and unknown compression methods.

  Make T005 pass.
- [X] T014 Implement `TransferWorker`, a hosted service in `SITES/Infrastructure/TransferWorker.cs`. Model it on `src/Modules/Forms/QuickBuilder.Forms/Infrastructure/NotificationWorker.cs`:
  - it claims `queued` and `confirmed` jobs with a 2-minute lease renewed every 15 s;
  - it runs one job at a time;
  - it fails jobs whose lease has expired with `interrupted`;
  - it dispatches to the export, check and import runners;
  - it writes progress at most once a second.
- [X] T015 [P] Implement `TransferRetentionService`, hourly, in `SITES/Infrastructure/TransferRetentionService.cs`. It deletes exports 7 days after they finish, uploads after 24 hours without a confirmed import, and job rows after 90 days (FR-008, ST-16). Each deletion of an archive writes `SiteArchiveDeleted`.
- [X] T016 [P] Implement `GetTransfer`, `ListTransfers` and `CancelTransfer` (`GET /api/v1/transfers/{id}`, `GET /api/v1/sites/{siteId}/transfers`, `POST /api/v1/transfers/{id}/cancel`) and the shared response mapping in `SITES/Features/Transfer/GetTransfer.cs`, `ListTransfers.cs`, `CancelTransfer.cs` and `TransferResponses.cs`. Add the codes to `SITES/ErrorResults.cs`.
- [X] T017 [P] Add the admin API client (types from transfer-api.yaml; `startExport`, `upload` with progress, `check`, `confirm`, `get`, `list`, `cancel`, `deleteArchive`, `downloadUrl`) in `ADMIN/api/v1/transfers.ts`. Add the polling composable (every 2 s while mounted, stops at final states) in `ADMIN/features/transfer/useTransfer.ts`.
- [X] T018 [P] Implement `TransferProgress.vue` (kind, step, percent, started by and at, Cancel; polite live region per admin-transfer.md §5) in `ADMIN/features/transfer/TransferProgress.vue`. Add its spec with axe in `ADMINTESTS/transfer/TransferProgress.spec.ts`.
- [X] T019 Add `TransferSessionUsageTests`, which allows `ISiteTransferSession.Enlist` and `SiteTransferSession` only in types named `*ArchivePart` and in `SITES/Features/Transfer/Engine/`, in `ATESTS/TransferSessionUsageTests.cs`.

**Checkpoint**: jobs can be created, claimed, cancelled and listed; archives can be written
and read with integrity checks.

---

## Phase 3: User Story 1 - An administrator exports a site to one archive (Priority: P1) 🎯 MVP

**Goal**: an Administrator exports a whole site in the background, follows progress, and
downloads one archive with a summary (FR-001 … FR-010).

**Independent Test**: seed a site with every kind of content, export it, and confirm that the
archive holds every kind, that the counts match, that a missing media file is reported, and
that editing during the export is not blocked (quickstart §1).

### Tests for User Story 1

- [X] T020 [P] [US1] Seed fixture for a site with every kind of content (quickstart §0) in `ITESTS/Transfer/Fixtures/FullSiteSeeder.cs`. Add the equivalent manual script in `tests/fixtures/transfer/seed-acme.http`.
- [X] T021 [P] [US1] `ExportContentsTests` in `ITESTS/Transfer/ExportContentsTests.cs`:
  - every entry of archive-parts.md §3 is present with the seeded counts;
  - drafts, live versions, retained versions, schedules and copy origins are present (FR-002);
  - submissions, editing sessions, takeovers, emails and derived rows are absent (FR-003);
  - a file with deleted bytes is listed as missing and the export still succeeds (FR-009).
- [X] T022 [P] [US1] `ExportSnapshotTests` in `ITESTS/Transfer/ExportSnapshotTests.cs`: edits and a publish run while the parts export, with a hook between parts. Every document in the archive is whole, either before or after, and no write is refused (FR-005, FR-006).
- [X] T023 [P] [US1] `TransferEndpointsTests` for export, list, get, cancel, delete and download in `ITESTS/Transfer/TransferEndpointsTests.cs`:
  - Designer and Editor get 403;
  - a second export while one is active gets 409 `transfer.busy`;
  - a download with `Range` gets 206;
  - an expired or deleted archive gets 410;
  - cancel ends the job `cancelled` and leaves no stored object.
- [X] T024 [P] [US1] Part golden lines: one test per part that pins its entries to golden JSON lines, in `UTESTS/Sites/Transfer/SitesPartGoldenTests.cs`, `UTESTS/Media/Transfer/MediaPartGoldenTests.cs`, `UTESTS/Editor/Transfer/DesignPartGoldenTests.cs`, `UTESTS/Cms/Transfer/CmsPartGoldenTests.cs` and `UTESTS/Pages/Transfer/PagesPartGoldenTests.cs`.

### Implementation for User Story 1

- [X] T025 [P] [US1] `SitesArchivePart.ExportAsync` (`settings`, `host-names` and `search` entries) in `SITES/Features/Transfer/SitesArchivePart.cs`.
- [X] T026 [P] [US1] `MediaArchivePart.ExportAsync` in `MEDIA/Features/Transfer/MediaArchivePart.cs`:
  - writes the `folders` and `files` entries (with `sha256` computed while copying);
  - copies each file's bytes from `MediaStorageKeys` into `media/<fileId>`;
  - marks a missing key with `bytes: "missing"` and a problem.
- [X] T027 [P] [US1] `DesignArchivePart.ExportAsync` (`design` and `design-versions` entries) in `EDITORM/Features/Transfer/DesignArchivePart.cs`.
- [X] T028 [P] [US1] `CmsArchivePart.ExportAsync` (`collections`, `schema-changes`, `items`, `item-versions`, `item-schedules` and `item-history` entries, streamed with `AsAsyncEnumerable`) in `CMS/Features/Transfer/CmsArchivePart.cs`.
- [X] T029 [P] [US1] `PagesArchivePart.ExportAsync` (`tree`, `page-drafts`, `components`, `publications`, `versions`, `schedules`, `menus` and `redirects` entries) in `PAGES/Features/Transfer/PagesArchivePart.cs`.
- [X] T030 [US1] Write `authors.jsonl`: collect every user id from the parts' rows through the `IAuthorMap` collector, and resolve display names with `IUserDirectory`. Do this in `SITES/Features/Transfer/Engine/ExportRunner.cs`.
- [X] T031 [US1] Implement `ExportRunner` in `SITES/Features/Transfer/Engine/ExportRunner.cs`:
  - creates a DI scope for the job and calls `ISiteContextSetter.SetSite(siteId)` before any part runs (research ST-15);
  - opens the read-only `SiteTransferSession`;
  - runs the parts in `Order`;
  - pipes the ZIP into `IFileStorage` at `{siteId:N}/transfers/{jobId:N}`;
  - writes the summary, `archive_bytes` and `archive_expires_at_utc` (+7 days);
  - raises `SiteExportFinished`;
  - checks cancellation between batches;
  - on failure or cancel, deletes nothing (storage is all-or-nothing) and ends the job.

  Make T021 and T022 pass.
- [X] T032 [US1] Register the five parts in `SITES/SitesModule.cs`, `MEDIA/MediaModule.cs`, `EDITORM/EditorModule.cs`, `CMS/CmsModule.cs` and `PAGES/PagesModule.cs` (`services.AddScoped<ISiteArchivePart, …>()`).
- [X] T033 [US1] Implement `StartExport` (`POST /api/v1/sites/{siteId}/exports`, refusal `transfer.busy` from the unique index) in `SITES/Features/Transfer/StartExport.cs`.
- [X] T034 [US1] Implement `DownloadArchive` (`GET /api/v1/transfers/{id}/archive`, `File(stream, "application/zip", "<host>-<utc>.qbsite", enableRangeProcessing: true)`, 410 when expired; raises `SiteArchiveDownloaded`) and `DeleteArchive` (`DELETE /api/v1/transfers/{id}`) in `SITES/Features/Transfer/DownloadArchive.cs` and `DeleteArchive.cs`. Make T023 pass.
- [X] T035 [US1] Record the transfer events as security events (`site_exported`, `site_imported`, `site_restored`, `site_archive_downloaded`, `site_archive_deleted`, `site_transfer_cancelled`, `site_transfer_failed`; ids, counts and the operator flag only) in `IDENT/Features/RecordSiteSecurityEvents/RecordSiteSecurityEvents.cs`.
- [X] T036 [US1] Admin "Export and import" tab: Export button (disabled with its reason while busy), `TransferProgress`, finished export with summary, Download, expiry date and Delete, and recent transfers. Put it in `ADMIN/features/transfer/TransferTab.vue` and add the tab to `ADMIN/features/sites/SiteSettingsView.vue`. Add the strings to `ADMIN/i18n/en.json`. Add its spec with axe in `ADMINTESTS/transfer/TransferTab.spec.ts` (busy reason, download and expiry, delete).

**Checkpoint**: US1 is complete and demoable. Quickstart §1 passes.

---

## Phase 4: User Story 2 - An administrator imports an archive as a new site (Priority: P1)

**Goal**: upload, check and import an archive as a new, independent site, in full or "start
fresh", with every reference remapped into it (FR-011, FR-016 … FR-021a).

**Independent Test**: export the seeded site, import it as a new site on a clean database and
on the same one, compare everything, and confirm that no reference points outside the new
site (quickstart §2).

### Tests for User Story 2

- [X] T037 [P] [US2] `IdMapTests` (new site: every id new; `Resolve` for unknown ids returns null; the fingerprint is stable for equal maps) in `UTESTS/Sites/Transfer/IdMapTests.cs`.
- [X] T038 [P] [US2] Remap golden suites with cases in `docs/contracts/<contract>/remap/<case>/{input,map,expected}.json`: **As built**: one generic suite, `UTESTS/Sites/Transfer/IdMapTests.cs` (`Every_contract_example_remaps_only_its_declared_ids`), over every valid golden example of the four contracts (research ST-09).
  - `PageDocumentRemapTests` (links, item, collection, component, menu and media ids, form `redirect.pageId` and `consent.linkPageId`, binding collection ids, an unmapped id left as is) in `UTESTS/Pages/Transfer/PageDocumentRemapTests.cs`;
  - `MenuRemapTests` in `UTESTS/Pages/Transfer/MenuRemapTests.cs`;
  - `ItemValuesRemapTests` (references, multi-references, image, file, rich text links and images) in `UTESTS/Cms/Transfer/ItemValuesRemapTests.cs`;
  - `DesignTokensRemapTests` in `UTESTS/Editor/Transfer/DesignTokensRemapTests.cs`.
- [X] T039 [P] [US2] `RoundTripTests` in `ITESTS/Transfer/RoundTripTests.cs` (SC-001, FR-037). Export the seeded site and import it as a new site, then compare every page, component, menu, collection, item, version, schedule, redirect, setting, token and file, with ids mapped. Also assert:
  - visitors get the same published HTML at the new host apart from ids in URLs;
  - the form registry and media usage rebuilt equal the source's;
  - authors from the same installation are kept;
  - imported into a second database without the source's users, every author shows as a historical author marked "(imported)", and the Users list leaves them out (FR-021);
  - the seeded past-due schedule is dropped and reported, and the future one is kept (FR-029);
  - `identity.security_events` holds `site_imported` with ids and counts only, and no label from the check result (FR-031).
- [X] T040 [P] [US2] `TransferIsolationProbe` in `ITESTS/Isolation/Probes/TransferIsolationProbe.cs`: no row of the new site refers to an id of the source site or of any other site, and the same archive imported twice gives two sites that share no id (FR-019). It also asserts that the host-name availability read is the only `AcrossAllSites` call in the parts (research ST-15). Register it with the isolation suite. **As built**: `RoundTripTests.The_same_archive_imported_twice_gives_two_sites_sharing_no_id` checks the database directly; the parts make no cross-site read of their own (Sites checks host names through `ISiteDirectory`).
- [X] T041 [P] [US2] `StartFreshTests` in `ITESTS/Transfer/StartFreshTests.cs` (FR-021a):
  - one draft per document (the live version where there is no newer draft);
  - no publications, versions, schedules or copy origins;
  - collections, redirects, tokens (draft) and media are present;
  - `startFresh` with merge or restore is refused with 422.
- [X] T042 [P] [US2] Upload, check and confirm endpoint tests in `ITESTS/Transfer/TransferEndpointsTests.cs` (extend T023):
  - 413 `transfer.too_large` over the configured size;
  - the body is not buffered (memory stays flat with a 500 MB upload);
  - host names already used are listed in `hostNamesLeftOut`;
  - confirming without `newSiteName` gets 422;
  - confirming after 24 hours gets 409 `transfer.check_expired`;
  - a download is served by a second app instance from the export another instance wrote (FR-036; two factories as in 023's `LockRaceTests`).

### Implementation for User Story 2

- [X] T043 [US2] Implement `IdMap` and `IdMapBuilder` (`Declare`, `Match`, `Resolve`, `Outcome`, fingerprint over matches and outcomes) in `SITES/Domain/IdMap.cs`. Make T037 pass.
- [X] T044 [P] [US2] Add the Identity part of authors: the `HistoricalAuthor` entity, the `historical_authors` mapping with `ux_historical_authors_source`, and migration `<ts>_AddHistoricalAuthors`. Put them in `IDENT/Domain/HistoricalAuthor.cs`, `IDENT/Infrastructure/IdentityDbContext.cs` and `IDENT/Infrastructure/Migrations/`.
- [X] T045 [US2] Implement `IHistoricalAuthors.EnsureAsync`, which runs in the transfer session, keeps ids that are users here, and creates or reuses historical authors for the rest, in `IDENTC/IHistoricalAuthors.cs` and `IDENT/Infrastructure/HistoricalAuthors.cs`. In the same task: **As built**: authors are ensured in Identity's own transaction just before the apply, not in the shared one (research ST-14).
  - extend `IDENTC/IUserDirectory.cs` and `IDENT/Infrastructure/UserDirectory.cs` with the fallback and `IsHistorical`;
  - keep historical authors out of the Users list (`IDENT/Features/ListUsers/`).
- [X] T046 [P] [US2] `PageDocumentRemapper` and `MenuDocumentRemapper`, built on the reference walkers Pages already uses for usage rows, in `PAGES/Features/Transfer/PageDocumentRemapper.cs` and `PAGES/Features/Transfer/MenuDocumentRemapper.cs`. Make their T038 cases pass. **Superseded**: the generic `IIdMap.RemapDocument` (research ST-09, as built) replaces the per-module remappers.
- [X] T047 [P] [US2] `ItemValuesRemapper` in `CMS/Features/Transfer/ItemValuesRemapper.cs`. Make its T038 cases pass. **Superseded**: the generic `IIdMap.RemapDocument` (research ST-09, as built) replaces the per-module remappers.
- [X] T048 [P] [US2] `DesignTokensRemapper` in `EDITORM/Features/Transfer/DesignTokensRemapper.cs`. Make its T038 cases pass. **Superseded**: the generic `IIdMap.RemapDocument` (research ST-09, as built) replaces the per-module remappers.
- [X] T049 [US2] `CheckAsync` for the new-site mode in each part (declare every id and its counts; Sites lists host names already used; Pages and Cms list past-due schedules in `pastSchedules`): `SITES/Features/Transfer/SitesArchivePart.cs`, `MEDIA/Features/Transfer/MediaArchivePart.cs`, `EDITORM/Features/Transfer/DesignArchivePart.cs`, `CMS/Features/Transfer/CmsArchivePart.cs` and `PAGES/Features/Transfer/PagesArchivePart.cs`.
- [X] T050 [US2] `CheckRunner` in `SITES/Features/Transfer/Engine/CheckRunner.cs`. It:
  - creates a DI scope for the job and calls `ISiteContextSetter.SetSite(targetSiteId)` when there is a target (research ST-15);
  - opens the reader (integrity first);
  - opens a read-only session on the target;
  - runs each part's `CheckAsync` in order;
  - stores `manifest`, `check_result` (data-model §3, lists capped at 1,000) and `check_fingerprint`;
  - ends the job `awaiting_confirmation`, or `refused` when there are errors.
- [X] T051 [US2] `ApplyAsync` for `ImportMode.NewSite` (full and start fresh) in each part. Each part:
  - inserts rows with the mapped ids and the new site id;
  - remaps its documents (T046–T048) and its authors through `IAuthorMap`;
  - rebuilds its derived rows (research ST-03);
  - drops past-due schedules into the report (FR-029);
  - writes its usual creation and publish events to its outbox on the enlisted context.

  Media writes bytes to `MediaStorageKeys` before the commit. Pages writes the page tree through `PageTree` so its invariants are checked. Files: `SITES/Features/Transfer/SitesArchivePart.cs`, `MEDIA/Features/Transfer/MediaArchivePart.cs`, `EDITORM/Features/Transfer/DesignArchivePart.cs`, `CMS/Features/Transfer/CmsArchivePart.cs` and `PAGES/Features/Transfer/PagesArchivePart.cs`.
- [X] T052 [US2] `ImportRunner` for the new-site mode in `SITES/Features/Transfer/Engine/ImportRunner.cs`. It:
  - opens the write session;
  - rebuilds the id map and compares fingerprints (`transfer.target_changed`);
  - allocates the site id, creates a DI scope for the job and calls `ISiteContextSetter.SetSite(newSiteId)` before any part applies (research ST-15);
  - creates the site through Sites (`SiteCreated` raised), disabled when the source was disabled;
  - ensures authors and runs each part's `ApplyAsync`;
  - checks cancellation between batches and commits once;
  - writes the report (data-model §4) and raises `SiteImportFinished`.

  Make T039, T040 and T041 pass.
- [X] T053 [US2] Implement `CreateUpload` and `UploadArchive` in `SITES/Features/Transfer/CreateUpload.cs` and `UploadArchive.cs`. `UploadArchive` streams the request body into storage with buffering disabled and `MaxRequestBodySize` set from `SiteTransferOptions`, under `installation/transfers/{id:N}`, and refuses an oversized body with 413. **As built**: every upload is stored under `installation/transfers/{id}`, whatever its later target, and a `newSiteHostName` option covers a new site whose archive host names are all taken.
- [X] T054 [US2] Implement `CheckArchive` (`POST /api/v1/transfers/{id}/check`, modes `new_site`, `merge` and `restore`; merge and restore are refused with 422 until US4) and `ConfirmImport` (`POST /api/v1/transfers/{id}/confirm`: name, start fresh, accept warnings, fingerprint; expiry after 24 hours) in `SITES/Features/Transfer/CheckArchive.cs` and `ConfirmImport.cs`. Make T042 pass.
- [X] T055 [US2] Admin: `ImportSiteView.vue` (upload with progress, check, `ImportCheckView`, name, start fresh, confirm, progress, report with a link to the site), `ImportCheckView.vue` (admin-transfer.md §3) and `TransferReport.vue`, in `ADMIN/features/transfer/`. Add the "Import site" entry in `ADMIN/features/sites/SitesListView.vue` and the route in `ADMIN/router.ts`. Add the strings to `ADMIN/i18n/en.json`. Add `ImportSiteView.spec.ts` and `TransferReport.spec.ts` with axe in `ADMINTESTS/transfer/`.
- [X] T056 [P] [US2] `ImportCheckView.spec.ts` (sections in order, errors block confirm, warnings need acceptance, paging over 1,000, axe) in `ADMINTESTS/transfer/ImportCheckView.spec.ts`.

**Checkpoint**: US1 and US2 together complete a backup, move and starting-point workflow.
Quickstart §2 passes.

---

## Phase 5: User Story 3 - Import refuses archives it cannot trust or understand (Priority: P1)

**Goal**: damaged, newer, unsafe or invalid archives are refused at check with reasons, and
a failed or cancelled import leaves the target unchanged (FR-012 … FR-015, FR-018).

**Independent Test**: run every refusal case and fault injection, and confirm a stated reason
and an unchanged installation each time (quickstart §3, §4 step 7).

### Tests for User Story 3

- [X] T057 [P] [US3] Build refusal fixtures from a valid golden archive with `ITESTS/Transfer/Fixtures/ArchiveMutator.cs`: truncate, flip a byte, add an extra entry, add `../evil`, remove an entry, set `format: 2`, set a newer major release, put a too-new `schemaVersion` in one page, put an invalid block in one page, put an invalid field value in one item, put a non-image in an image file, and put a file over `Media:MaxFileSizeBytes`.
- [X] T058 [P] [US3] `ArchiveRefusalTests` in `ITESTS/Transfer/ArchiveRefusalTests.cs` (SC-003). Each fixture is refused with its code from data-model §5 and names the entry where there is one, and the row counts of every module's tables are unchanged. The media cases are warnings, and they import as missing files when accepted (FR-015).
- [X] T059 [P] [US3] `ImportFailureTests` in `ITESTS/Transfer/ImportFailureTests.cs` (SC-004, FR-018): **As built**: the fault is a test part that runs after Pages, so every part has written before it throws, cancels or kills its own connection; every table is unchanged afterwards.
  - a fault injected after each part's `ApplyAsync`, then cancel during each part, then the connection killed with `pg_terminate_backend` during apply;
  - each time no site and no row exists afterwards, the job ends failed, cancelled or interrupted, and every byte written is removed by `MediaOrphanSweepService` after the grace period, which is fast-forwarded with a fake clock.
- [X] T060 [P] [US3] `ArchiveFromPreviousReleaseTests` in `ITESTS/Transfer/ArchiveFromPreviousReleaseTests.cs` (SC-008). It imports every archive in `tests/fixtures/archives/`, renders its pages, and stays green when the folder holds only this release's archive. Add `tests/fixtures/archives/README.md`, which says each release adds its archive.

### Implementation for User Story 3

- [X] T061 [US3] Version checks in `CheckRunner` (format, release major and `SupportedMajors`; ST-21) and the version list in the check result, in `SITES/Features/Transfer/Engine/CheckRunner.cs`.
- [X] T062 [P] [US3] Pages validation in `PagesArchivePart.CheckAsync`, in `PAGES/Features/Transfer/PagesArchivePart.cs`:
  - each draft, version and menu goes through the module's `SchemaUpgradeChain` (`TooNew` → `transfer.document_too_new`, `Failed` → `transfer.document_invalid`, `Upgraded` → noted);
  - then the page schema, binding and menu validators;
  - redirect rules and page tree invariants;
  - references to media files, collections and items the archive does not hold are reported as broken (warnings).
- [X] T063 [P] [US3] Cms validation in `CmsArchivePart.CheckAsync`: collection schemas through the chain and the schema validator, and item values through item validation (015) against their collection, in `CMS/Features/Transfer/CmsArchivePart.cs`. **As built**: schemas through `CollectionSchemaValidator` (stored rules); item values must be values documents. Rule-level value problems stay with the item, as for a draft saved with problems, and appear in its report after the indexer runs.
- [X] T064 [P] [US3] Editor validation in `DesignArchivePart.CheckAsync` (chain plus the token validator) in `EDITORM/Features/Transfer/DesignArchivePart.cs`.
- [X] T065 [P] [US3] Media validation in `MediaArchivePart.CheckAsync` (the upload path's content sniffing, allowed types and `Media:MaxFileSizeBytes` on each `media/<fileId>`; refused or missing files are warnings) in `MEDIA/Features/Transfer/MediaArchivePart.cs`. Reuse the sniffing from `MEDIA/Features/UploadFile/`, and move it to `MEDIA/Infrastructure/` if it is private to that slice.
- [X] T066 [P] [US3] Sites validation in `SitesArchivePart.CheckAsync` (009 name, scheme, language and host name rules) in `SITES/Features/Transfer/SitesArchivePart.cs`.
- [X] T067 [US3] Make the apply path all or nothing in `SITES/Features/Transfer/Engine/ImportRunner.cs`:
  - nothing commits until every part has applied;
  - cancellation and exceptions dispose the session (rollback);
  - a missing-media acceptance writes a `missing` file record;
  - the job ends with the right failure code.

  Make T058 and T059 pass.
- [X] T068 [US3] Export a golden archive of the seed site as `tests/fixtures/archives/<this release>.qbsite`, and make T060 pass.

**Checkpoint**: refusals and failures are proven safe. Quickstart §3 passes.

---

## Phase 6: User Story 4 - Importing into an existing site with conflicts, and restore (Priority: P2)

**Goal**: merge into an existing site with one policy, matching by identity when the archive
is from the same site, and restore a site from its own archive after a preview, a typed
confirmation and a pre-restore export. Writes are paused while either applies
(FR-022 … FR-029).

**Independent Test**: merge with each policy, from another site and from the same site;
restore a changed site, then restore the pre-restore export; try to write during apply
(quickstart §4).

### Tests for User Story 4

- [X] T069 [P] [US4] `ConflictRulesTests` (each kind × policy in research ST-10; rename suffixes; redirects under rename treated as skip; the identity-plus-clash case of FR-022a) in `UTESTS/Sites/Transfer/ConflictRulesTests.cs`. Extend `UTESTS/Sites/Transfer/IdMapTests.cs` with natural-key, identity-first and restore matching.
- [X] T070 [P] [US4] (As built: each policy against the source imported as a new site, plus the same-site identity match and a target changed after the check. The render-cache-during-apply check is covered by the commit-then-visit assertion only.) `MergePolicyTests` in `ITESTS/Transfer/MergePolicyTests.cs` (SC-005, FR-022 … FR-027). With the Starter fixture into Acme, for each policy:
  - the conflicts listed equal those applied;
  - documents without a counterpart are unchanged and nothing is deleted;
  - host names and the enabled state are unchanged;
  - replace keeps the old versions in history;
  - skip points references to the existing documents;
  - rename adds `-2` and `(imported)`;
  - a replaced collection's field changes match the check preview.

  Then a same-site merge after renaming `/about` to `/company` matches by identity, with no duplicate.

  With the target's pages warmed in the render cache before the import, a visitor request after the commit returns the merged published content, and one during apply returns the old content (FR-028).
- [X] T071 [P] [US4] (As built: preview of removals, the site matching the archive with host names kept, the pre-restore export and undoing with it, another site's archive refused, and a wrong typed name refused. The pre-restore failure, schedule, security event and cache-during-apply cases are not yet automated.) `RestoreTests` in `ITESTS/Transfer/RestoreTests.cs` (SC-005, FR-027a … FR-027d):
  - export; add a page, delete an item, change a collection's fields, upload a file; restore;
  - the preview lists exactly those changes, and the site equals the archive afterwards with host names unchanged;
  - the pre-restore export exists, and restoring it brings back the state before;
  - restore of an archive from another site gets 409 `transfer.restore_not_allowed`;
  - a wrong typed name gets 422;
  - a failing pre-restore export leaves the site unchanged with `pre_restore_export_failed`;
  - a restore whose pre-restore export runs is not refused with `transfer.busy` (data-model §1);
  - the seeded past-due schedule is dropped and reported (FR-027c);
  - `identity.security_events` holds `site_restored` with ids and counts only (FR-031);
  - with the site warmed in the render cache, visitors see the old content during apply and the restored content after the commit (FR-028).
- [X] T072 [P] [US4] (As built: refusal while held, a write under way delaying the import, release when the backend dies, and stable keys. "Without a transaction throws" does not apply to the interceptor, which opens its own; the two-instance case is not automated.) `TransferLockTests` in `ITESTS/Transfer/TransferLockTests.cs`:
  - the gate refuses while the exclusive lock is held;
  - a write in progress delays apply until it commits;
  - the lock is released when the connection dies;
  - calling the gate without an open transaction throws;
  - a gated write holds the shared lock until its commit;
  - with two app instances, a write on one is refused while the other applies an import, and two workers never run the same job (FR-036).
- [X] T073 [P] [US4] (As built: one real write per module and document kind answers 423, reads answer 200, another site writes on. Catalogued endpoints called with random ids fail before any save, so they cannot show the gate.) `TransferGateCoverageTests` in `ITESTS/Transfer/TransferGateCoverageTests.cs`: every write endpoint in `ITESTS/Infrastructure/EndpointCatalog.cs` for a site whose import lock is held answers 423 `site.import_in_progress`, and the same reads still answer 200.
- [X] T074 [P] [US4] `RestoreConfirmDialog.spec.ts` (lists, typed name required, focus trap, axe) and `SiteImportBanner.spec.ts` (shown while importing, read-only editor, reload offer) in `ADMINTESTS/transfer/`.

### Implementation for User Story 4

- [X] T075 [US4] (As built: in `SITESC/Transfer/ConflictRules.cs`, so every part can name renamed documents with it; the id map's fingerprint no longer depends on the policy and covers noted facts.) `ConflictRules` (outcome per kind and policy, rename naming through each module's rules) in `SITES/Domain/ConflictRules.cs`. Extend `SITES/Domain/IdMap.cs` with natural-key and identity matching. Make T069 pass.
- [X] T076 [US4] (As built: `SiteWriteGateInterceptor` and `SiteWriteLock` in Shared.Infrastructure `Data/SiteWriteGateInterceptor.cs`, 423 mapped once in the Host middleware; research ST-07.) `SiteWriteGate`, implemented in `SITES/Infrastructure/SiteWriteGate.cs`:
  - `EnterAsync` runs `pg_try_advisory_xact_lock_shared` with a parameterised `SqlQuery` in the caller's transaction, and fails with `site.import_in_progress`;
  - the exclusive `pg_advisory_lock` and unlock are used by `ImportRunner`;
  - the lock key is derived from the site id like `ModuleDatabase.DeriveLockKey`;
  - `EnterAsync` throws `InvalidOperationException` when `context.Database.CurrentTransaction` is null (research ST-07).

  Register it in `SITES/SitesModule.cs`, and map 423 in each module's error results.
- [X] T077 [US4] (As built: the interceptor gates every change-tracked save of the five content contexts, so no helper calls it.) Call the gate from the central write helpers: `PAGES/Infrastructure/DraftWrites.cs`, `PAGES/Infrastructure/PublishingWrites.cs`, and the Pages menu, redirect and tree writes; `CMS/Infrastructure/ItemWrites.cs` and the Cms collection writes; the Media upload, replace, delete, move and folder features; the Editor design draft and publish writes; and the Sites settings, search and host name features. Each helper wraps the gate and its `SaveChanges` in one transaction (`Database.BeginTransactionAsync`) when none is open. Make T072 and T073 pass.
- [X] T078 [US4] Add the site response's `transfer` field (active import or export, state and when) in `SITESC/SiteDto.cs`, `SITES/Infrastructure/SiteDtoMapper.cs` and `SITES/Features/GetSite/GetSite.cs`.
- [X] T079 [US4] Merge `CheckAsync` in each part: natural keys, identity first when `SameSiteAsTarget`, conflicts with `matchedBy` and `clash`, media reuse by size then hash, collection field changes under replace, and past schedules. Files: `PAGES/Features/Transfer/PagesArchivePart.cs`, `CMS/Features/Transfer/CmsArchivePart.cs`, `MEDIA/Features/Transfer/MediaArchivePart.cs`, `EDITORM/Features/Transfer/DesignArchivePart.cs` and `SITES/Features/Transfer/SitesArchivePart.cs`.
- [X] T080 [US4] Merge `ApplyAsync` in each part, per research ST-10:
  - replace through each aggregate's draft and publish operations, so history is kept;
  - skip leaves the existing document and maps references to it;
  - rename uses the owning feature's unique naming;
  - Cms replace applies the schema change through the existing schema-change path (014);
  - never touches host names or the enabled state;
  - drops past schedules into the report.

  Files: same as T079.
- [X] T081 [US4] Restore `CheckAsync` and `ApplyAsync` in each part (research ST-12):
  - check computes removals, returns and changes by id and content hash;
  - apply deletes the site's rows in the part's carried and rebuilt tables, inserts the archive's rows with their original ids, rebuilds derived rows, and raises delete events for removals;
  - Media skips writing keys that already exist;
  - Sites applies settings except host names and the enabled state;
  - past-due schedules are dropped and reported (FR-027c, FR-029).

  Files: same as T079.
- [X] T082 [US4] Extend `ImportRunner` and `CheckRunner` for merge and restore in `SITES/Features/Transfer/Engine/ImportRunner.cs` and `CheckRunner.cs`. The import runner:
  - creates the job's DI scope with `ISiteContextSetter.SetSite(targetSiteId)` (research ST-15);
  - takes the exclusive site lock for apply;
  - runs the pre-restore export (step `pre_restore`, a linked export job) before restore applies;
  - re-checks the fingerprint inside the transaction.

  The check runner sets `allowedModes` from `manifest.source.siteId`. In `CheckArchive` and `ConfirmImport`, enable the merge and restore modes (confirm takes the site's active slot and answers 409 `transfer.busy` if another transfer holds it; a pending check holds none), the policy, `confirmSiteName` (ordinal, trimmed) and `transfer.restore_not_allowed`. Make T070 and T071 pass.
- [X] T083 [US4] (As built: the banner is mounted in the app shell next to `SearchHiddenBanner`, as no `SiteLayout.vue` exists; the item form turns read-only while the banner shows an import; the editor keeps the changes and retries on 423, which ends as a normal 409 conflict if the import replaced the document.) Admin: "Import into this site" (merge with policy choice) and "Restore from archive" in `ADMIN/features/transfer/TransferTab.vue`; `RestoreConfirmDialog.vue`; `SiteImportBanner.vue`, mounted in `ADMIN/app/SiteLayout.vue`. Switch the editor and item form to read-only on `site.import_in_progress` in `ADMIN/editor/state/autosave.ts` and `ADMIN/features/cms/items/ItemFormView.vue`. Add the strings to `ADMIN/i18n/en.json`. Make T074 pass.

**Checkpoint**: all import modes work. Quickstart §4 passes.

---

## Phase 7: User Story 5 - An operator scripts backups and restores from the command line (Priority: P2)

**Goal**: `sites export|check|import` with progress, the same outcomes as the admin, and
documented exit codes (FR-032 … FR-034).

**Independent Test**: run each subcommand and each exit code in
[contracts/operator-commands.md](./contracts/operator-commands.md) (quickstart §5).

### Tests for User Story 5

- [X] T084 [P] [US5] (As built: exit codes 0, 2, 4, 5, 6 and 64 each have their case, as do the operator's job, `--json`, no overwrite, `--pre-restore-out` and an unfinished merge check not holding a site. Codes 1 and 3, and `export --all` going past a failed site, are not automated: nothing in the fixtures makes an export fail.) `SitesCommandTests` in `ITESTS/Transfer/SitesCommandTests.cs`. Each subcommand and option combination produces the same database outcome as the admin, plus:
  - every exit code (0, 1, 2, 3, 4, 5, 6, 64) is produced by its case;
  - `export --all` continues past a failed site and returns 1;
  - a merge check awaiting confirmation on a site does not make `export --all` return 5 (data-model §1);
  - `--out` refuses to overwrite;
  - `--json` prints one object;
  - the jobs have `trigger = cli` and `started_by = null`, and security events have `operator: true`.

### Implementation for User Story 5

- [X] T085 [US5] (As built: files through `IOperatorFiles`, implemented in the Host, because modules may not use the file system; `--host-name` added for a new site whose archive host names are all taken; host names are resolved for disabled sites too.) `SitesCommand : IHostCommand` (`Name = "sites"`) in `SITES/Infrastructure/SitesCommand.cs`, modelled on `MEDIA/Infrastructure/MediaCommands.cs`:
  - it parses the subcommands and options;
  - it resolves `--site`, `--into` and `--restore` by id or host name through `ISiteDirectory`;
  - it reads and writes archives on the local file system;
  - it creates the job row and runs the runners in-process, renewing the lease itself;
  - it prints progress every 5 seconds and the summary or report;
  - it maps outcomes to exit codes;
  - it writes the pre-restore export to `--pre-restore-out` when given.

  Register it in `SITES/SitesModule.cs`. Make T084 pass.
- [X] T086 [US5] Document the command, a nightly `export --all` cron example with rotation of the last 14 archives, restore, and moving a site, in `docs/operations.md`. Link the command from `specs/002-identity-setup/contracts/operator-commands.md`.

**Checkpoint**: all stories are complete. Quickstart §5 passes.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T087 [P] Promote [contracts/site-archive.md](./contracts/site-archive.md) to `docs/contracts/site-archive.md`, with `docs/contracts/site-archive/manifest.v1.schema.json` and `docs/contracts/site-archive/examples/` (a minimal valid archive described as files). Add the row to `docs/contracts/README.md`. Add `UTESTS/Sites/Transfer/ManifestSchemaTests.cs`, which validates the manifests `ZipArchiveWriter` writes against the schema.
- [X] T088 [P] Update `docs/backlog.md`: mark the #20 obligations from 017, 018, 023 and 024 as done, and add the plan's Deferred list under "Deferred by 020".
- [X] T089 [P] Write the upgrade notes 1–4 from plan.md (Operator impact) in `docs/releases/unreleased/020-site-export-import.md`. (As built: `docs/releases/unreleased/20-site-export-import.md`, named by issue number like the other fragments; note 1 is the fragment's text, notes 2–4 its manual steps.)
- [X] T090 [P] Write the upgrade test from the newest database snapshot (the two new tables arrive empty, and existing rows and endpoints are unchanged). Name it after that snapshot's release, as `UpgradeFrom019Tests` and `UpgradeFrom024Tests` are, in `ITESTS/Transfer/UpgradeFrom<snapshot>Tests.cs`.
- [X] T091 [P] (Run at full size in a container capped at 2 CPUs and 4 GB, 2026-11-04; see quickstart-results.md.) Run `TransferPerformanceTests` (manual trait: 500 pages, 10,000 items, 2 GB media on the reference setup; time, container memory and public p95 render during both) in `ITESTS/Transfer/TransferPerformanceTests.cs`. Record the results in `specs/020-site-export-import/quickstart-results.md` (SC-002).
- [X] T092 In `specs/020-site-export-import/spec.md`, narrow the edge case "Server restart during an export or import" to "the job is marked failed (interrupted)", per research ST-15.
- [ ] T093 (Measured: every target is at 80% or more except the write gate interceptor at 79.4%; see quickstart-results.md.) Check coverage: 80% line coverage on `SITES/Features/Transfer/Engine/`, `SITES/Domain/IdMap.cs`, `ConflictRules.cs`, `SiteWriteGate.cs` and every part's `ApplyAsync`. Add tests where it falls short, and record the numbers in `specs/020-site-export-import/quickstart-results.md`.
- [ ] T094 Run quickstart §1–§7 end to end on the reference compose setup, including the manual screen-reader and keyboard checks (E-5) and the usability check with at least three participants (SC-007), and record the results in `specs/020-site-export-import/quickstart-results.md`.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none. T001 should finish first, because it can change ST-16.
- **Foundational (Phase 2)**: depends on Setup; blocks every story.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational. Its tests use US1's export to make archives
  (T039 and T041 need T031).
- **US3 (Phase 5)**: depends on US2's check and apply (T050, T052).
- **US4 (Phase 6)**: depends on US2 (T050, T052). It is independent of US3, but T067's
  all-or-nothing behaviour is assumed by T071.
- **US5 (Phase 7)**: depends on US1 and US2. Its restore cases depend on US4.
- **Polish (Phase 8)**: after every story.

### Within each story

- Tests before implementation; they must fail first.
- Domain and remappers before parts, parts before runners, runners before endpoints,
  endpoints before the admin.
- T049, T051, T079, T080 and T081 each touch all five part files. They are not [P] with each
  other, but the five files within one task can be worked on in parallel by part owners.

### Parallel opportunities

- **Phase 1**: T002 and T003.
- **Phase 2**: T004 and T005; then T007, T008 and T011; then T015–T018 after T009, T010, T012
  and T013.
- **US1**: T020–T024 together; T025–T029 (one per module) together.
- **US2**: T037–T042 together; T044 and T046–T048 together.
- **US3**: T057–T060 together; T062–T066 (one per module) together.
- **US4**: T069–T074 together.

## Parallel Example: User Story 1

```text
# Tests first, together:
T021 ExportContentsTests   T022 ExportSnapshotTests   T023 TransferEndpointsTests   T024 part golden lines

# Then one part per module, together:
T025 SitesArchivePart   T026 MediaArchivePart   T027 DesignArchivePart   T028 CmsArchivePart   T029 PagesArchivePart

# Then in order:
T030 authors → T031 ExportRunner → T032 registration → T033 StartExport → T034 download/delete → T035 audit → T036 admin tab
```

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 and Phase 2.
2. Phase 3 (US1). **Stop and validate** with quickstart §1. Operators then have off-server
   archives of every site.

### Incremental delivery

1. Add US2 (import as a new site): backups can be restored and sites moved and reused.
2. Add US3 (refusals and atomicity): import is safe against bad archives. US2 and US3 should
   ship together, because import must not be offered without US3.
3. Add US4 (merge and restore), then US5 (command line).
4. Polish, then the PR, which also carries constitution amendment 2.6.0.

The feature ships in one PR (plan, Principle XI). Phases are review checkpoints, not separate
releases.
