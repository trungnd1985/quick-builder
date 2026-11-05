# Implementation Plan: Site Export and Import

**Branch**: `020-site-export-import` | **Date**: 2026-11-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/020-site-export-import/spec.md`

**Issue**: #20.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none. **E-7** (a database transaction shared by the modules' parts of
  one export or import) was raised by this plan and closed by constitution amendment 2.6.0
  (Principle I, approved 2026-11-02). See Complexity Tracking.

## Summary

An Administrator, or the operator on the command line, exports a whole site to one `.qbsite`
archive. The archive holds the site's settings, design, pages, components, menus, redirects,
collections, items, version history, schedules, and every media file with its bytes. It can
be imported in three ways:
- **as a new site**, either in full or with "start fresh" (drafts only);
- **merged into an existing site**, with one conflict policy (replace, skip or rename);
- **restored into the site it came from**, which makes the site match the archive, after a
  preview, a typed confirmation and an automatic export of the current state.

Every import is checked before anything changes, then applied all or nothing.

The approach, detailed in [research.md](./research.md):
- **Owner** (ST-01): the Sites module runs the jobs, the archive, the API and the `sites`
  command. Each content module (Sites, Media, Editor, Cms, Pages) implements
  `ISiteArchivePart` from Sites.Contracts and handles only its own tables, with its own
  validators. There is no new module and no new module edge.
- **Format** (ST-02, ST-21): a ZIP with `manifest.json`, which records the format, release,
  source site and a SHA-256 for every entry. Each part writes JSON Lines entries, and media
  bytes are stored as they are. It becomes a versioned contract in `docs/contracts/`.
  Documents are stored as exported and upgraded on read (019).
- **Consistency and atomicity** (ST-04, ST-05; Principle I as amended in 2.6.0):
  - export reads every part in one read-only snapshot, so members keep editing;
  - import applies every part in one transaction, so a failure, cancel or crash leaves the
    target untouched;
  - bytes written before a failed commit are removed by the existing media orphan sweep.
- **Check, then apply** (ST-06, ST-08 … ST-12):
  - check validates integrity, versions and every entry;
  - it builds an id map (new ids for a new site; natural keys for a merge; identity first for
    an archive of the same site; original ids for a restore) and lists conflicts, removals and
    warnings;
  - apply refuses to run if the target changed since the confirmed check.
- **Writes paused during apply** (ST-07): an advisory lock per site. Every content write takes
  it shared and gets `423 site.import_in_progress` while an import holds it. The admin shows a
  banner.
- **Jobs** (ST-15, ST-16): a `sites.transfer_jobs` queue with leases, run by a hosted worker
  or in-process by the command. Archives go through `IFileStorage` and are kept 7 days.
  Downloads support ranges; uploads stream unbuffered.
- **Authors** (ST-14): member ids from another installation become historical authors in
  Identity, with a display name only. No accounts are created.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 + Npgsql, `System.IO.Compression` (ZIP, Zip64),
  `System.IO.Pipelines`, `System.Security.Cryptography` (SHA-256), the in-house dispatcher,
  outbox and hosted services.
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n.
- **Nothing new**: every server library above is in the BCL; no tus or chunk-upload
  library (ST-17).

**Storage**: PostgreSQL 16+ and the configured `IFileStorage` provider.
- **Sites** migration `AddTransferJobs`: **+** `transfer_jobs`.
- **Identity** migration `AddHistoricalAuthors`: **+** `historical_authors`.
- **No other table changes.** Parts read and write the existing tables (data-model).
- **Storage keys**: `{siteId}/transfers/{jobId}`, and `installation/transfers/{jobId}` for
  uploads made before a site exists (ST-16).

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL, and vitest with
Testing Library and axe. Suites are listed in research ST-23. The main ones:
- `RoundTripTests` (SC-001, FR-037);
- `MergePolicyTests` and `RestoreTests` (SC-005);
- `ArchiveRefusalTests` (SC-003);
- `ImportFailureTests` (SC-004);
- `ExportSnapshotTests`;
- `TransferGateCoverageTests`;
- `SitesCommandTests`;
- remap golden suites per contract;
- `UpgradeFrom023Tests`;
- `ArchiveFromPreviousReleaseTests` (SC-008);
- the manual `TransferPerformanceTests` (SC-002).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend, the Vue admin SPA and the
operator command. The public site is untouched, apart from seeing imported content once it
is committed.

**Performance Goals** (reference setup, 2 vCPU and 4 GB; SC-002):
- Export of 500 pages, 10,000 items and 2 GB of media in at most 15 minutes. Import as a new
  site in at most 20 minutes.
- Server memory stays bounded (under 1 GB in the container) whatever the archive size:
  streaming, batches of 500 rows, and pipes for the ZIP (ST-24).
- The public p95 render stays within the Principle VIII budgets during both. Export takes no
  locks; import locks only the target site's rows.

**Constraints**:
- **All or nothing** for every import, including after a crash (FR-018).
- **Nothing extracted by name**: entry names are validated against fixed shapes (ST-02).
- **Logs and events**: ids, counts and codes only. Titles and labels appear only in the job's
  check result and report, which only Administrators see.
- **Offline**: no outbound requests.

**Scale/Scope**:
- **Code**: 0 new projects. 5 part implementations, 1 orchestrator, 1 worker and 1 retention
  service.
- **Migrations**: 2. Tables: 2 new.
- **Endpoints**: 11 new (export, list, upload ×2, check, confirm, get, delete, cancel,
  download, plus the site response field). Every content write gains the gate (ST-07).
- **Contracts interfaces**: in Sites.Contracts, `ISiteArchivePart`, `ISiteTransferSession`
  and `ISiteWriteGate`; in Identity.Contracts, `IHistoricalAuthors`.
- **Events**: 4 new (Sites); Identity consumes them.
- **Capabilities**: 2 new, Administrator only: `sites.transfer.export` (site) and
  `sites.transfer.import` (installation).
- **Configuration keys**: 1, `SiteTransfer:MaxUploadSizeBytes`.
- **Operator commands**: 1 new, `sites`.
- **Content contracts**: 1 new, `site-archive`. Remap golden files for 4 existing contracts.
- **Admin**: about 6 components and 2 TypeScript modules.

## Constitution Check

*GATE: passed before Phase 0 with exception E-7 recorded, and re-checked after Phase 1
design and after amendment 2.6.0 (result at the end of this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies, under Principle I as amended in 2.6.0.** <br>• **No new module** (ST-01): orchestration in Sites `Features/Transfer/` (StartExport, Upload, Check, Confirm, GetTransfer, Cancel, DeleteArchive, Download, ListTransfers), `TransferWorker`, `TransferRetentionService` and `SitesCommand`. <br>• **Parts**: `Features/Transfer/ArchivePart` in Pages, Cms, Media and Editor, and Sites' own part. Each touches only its module's tables. <br>• **Cross-module**: through Sites.Contracts and Identity.Contracts only; no new edge (archive-parts.md §2). <br>• **Domain model**: `TransferJob` aggregate (state machine, data-model §1.1) and pure `IdMap` and `ConflictRules`, unit-tested. Pages and Cms parts write through their aggregates' factory methods for restore and insert, so invariants are checked. <br>• **Shared transaction** (amendment 2.6.0, formerly E-7): one database transaction is shared by the parts of one export (read-only) or import (ST-04, ST-05). Each module still writes only its own tables with its own `DbContext`. <br>• **Enforcement**: `ModuleBoundaryTests` unchanged; a new `TransferSessionUsageTests` allows `ISiteTransferSession.Enlist` only in `ArchivePart` classes. |
| II | Easy to Install, Operate and Upgrade | **Complies.** One configuration key with a safe default, validated at startup (ST-17). Two forward-only, automatic migrations. The command follows the existing operator commands. Health checks are unchanged. |
| III | Upgrades Never Lose Data | **Complies, and delivers the principle's export/import promise.** <br>• The archive format is versioned, and every release reads the formats it lists (ST-21). <br>• `ArchiveFromPreviousReleaseTests` imports a golden archive from each earlier release. <br>• `UpgradeFrom023Tests`: the two new tables arrive empty and nothing else changes. <br>• Restore always exports the current state first (ST-13). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL plus the configured storage provider. Jobs are database-backed. Locks are PostgreSQL advisory locks, so one instance and many behave the same. Archives in shared storage let any instance serve a download or run a job. |
| V | Multi-Site Within One Installation | **Complies.** <br>• **Isolation**: every reference in an import is remapped into the target site (FR-019). `TransferIsolationProbe` proves that a new site shares no row id with its source or another site. <br>• **Site-owned rows**: every row written carries the target `site_id`. Runners set the site context for each job's scope (ST-15), so parts use their normal filtered contexts. The one cross-site read (host names already in use) uses `AcrossAllSites` with a reason, and `TransferIsolationProbe` covers it. <br>• **`transfer_jobs`** is `[InstallationScoped]` with a reason (data-model §1), and reads filter `site_id` explicitly. <br>• **Keys and jobs** are namespaced by site (`{siteId}/transfers/…`). <br>• **Raw SQL**: none apart from the advisory lock functions, called through EF Core `SqlQuery` with parameters, with a test (Technology Constraints). |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• A new contract `docs/contracts/site-archive.md`, with a manifest schema and golden archives. <br>• Documents inside it keep their own contract versions; they are stored as exported and upgraded on read (ST-21). <br>• Remap golden files for page schema, menu, collection schema and design tokens. <br>• No existing contract changes. |
| VII | Draft and Published Are Separate Worlds | **Complies.** Drafts, live versions and retained versions travel separately and are restored separately. "Start fresh" publishes nothing. An import never changes what visitors see until its single commit. Rollback to imported versions works as for any version. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** Apply writes the usual publish and delete events in the same transaction, so invalidation of exactly the affected entries runs after the commit, on every instance. Derived dependency rows are rebuilt by the owning modules (ST-03). |
| IX | Secure by Default | **Complies.** <br>• **Authorisation**: Administrator only, enforced per operation (FR-035). <br>• **Untrusted archives**: fixed entry shapes, hash checks, no extraction by name, upload validation of every media file, and module validators for every document (ST-02, ST-20). <br>• **Upload size** limited by configuration. <br>• **Archive contents**: no secrets, passwords, emails, sessions or submissions (FR-003). <br>• **Logs and events**: ids only (ST-19). <br>• **Restore** needs the typed name. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** <br>• **Endpoints**: integration tests for every new operation and for the 423 gate on every write. <br>• **Contracts**: golden files for the archive and every remap. <br>• **Domain**: unit tests for `TransferJob`, `IdMap` and `ConflictRules`. <br>• **Coverage**: 80% line coverage on the orchestrator, id map, parts' apply paths and the gate, which become a critical path for recovery and upgrade. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new dependencies.** <br>• **Not built**: chunked uploads, archive encryption or signing, per-conflict choices, stored media hashes, resuming interrupted jobs. <br>• **Reuse**: the lease worker pattern, the media orphan sweep, the existing validators, reference walkers and events. <br>• **Delivery**: the feature ships whole in one PR, so no feature flag. |

**Technology Constraints**:
- MVC controllers only. EF Core only.
- The advisory lock calls (`pg_advisory_lock`, `pg_advisory_unlock`,
  `pg_try_advisory_xact_lock_shared`) are parameterised `SqlQuery` calls inside Sites, which
  owns the lock, and touch no table. `TransferLockTests` covers them.
- No Node process at run time. No new admin libraries.

**Development Workflow**:
- Issue #20 → `specs/020-site-export-import` → branch `020-site-export-import` → one PR that
  closes #20.
- Clarify has run: 3 questions, all answered. Analyze has run (2026-11-01). Its findings are
  fixed, and C1 was resolved by constitution amendment 2.6.0, which is part of this
  feature's PR.
- **Spec alignment**: the edge case "Server restart during an export or import: the job is
  marked failed or resumed" is resolved as **failed** (ST-15). The PR should narrow that edge
  case in spec.md; `/speckit-analyze` should confirm it.
- **Contract and registry changes in the same PR**:
  - `docs/contracts/site-archive.md` and `docs/contracts/site-archive/` (new), and
    `docs/contracts/README.md` (row);
  - `docs/contracts/{page-schema,menu,collection-schema,design-tokens}/remap/` golden files;
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (4 Sites events, Identity
    consumers, and the existing events raised by apply);
  - `specs/002-identity-setup/contracts/authorization.md` (2 capabilities, 11 operations,
    `site.import_in_progress`);
  - `specs/002-identity-setup/contracts/operator-commands.md` (link to the `sites` command);
  - `docs/operations.md` (backups with `sites export --all`, restore, proxy body limits) and
    `docs/configuration.md` (`SiteTransfer:MaxUploadSizeBytes`);
  - `docs/backlog.md`: the #20 obligations from 017, 018, 023 and 024 are marked done, and
    the deferred items are added.

**Operator impact**:
- **New configuration keys**: `SiteTransfer:MaxUploadSizeBytes` (default `10GB`, maximum
  `100GB`; invalid values fail at startup).
- **Migrations**: 2, additive and automatic: Sites `AddTransferJobs` and Identity
  `AddHistoricalAuthors`.
- **New dependencies**: none.
- **New operator command**: `sites export|check|import`.
- **Upgrade notes** (`docs/releases/unreleased/`):
  1. Administrators can export a site to one archive and import it as a new site, into an
     existing site, or as a restore. See the operations guide for nightly backups with
     `sites export --all`.
  2. A reverse proxy limits upload size, so raise its limit for `/api/v1/transfers/uploads` to
     import large archives in the admin, or use the command line, which has no limit.
  3. Exports kept in the admin use media storage space for 7 days.
  4. Archives contain unpublished content and members' display names. Store them as you
     store database backups.

**Post-Phase-1 re-check**:
- `transfer-api.yaml` declares a capability on every operation;
- archive-parts.md adds only Contracts surfaces, with no new edge;
- every row the parts write carries the target `site_id`;
- the job table is `[InstallationScoped]` with a reason;
- no event carries content;
- raw SQL is limited to the advisory locks;
- the public path reads only committed, published data.

**Gate: PASS** with no open exception. E-7 is closed by amendment 2.6.0.

## Project Structure

### Documentation (this feature)

```text
specs/020-site-export-import/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 3 questions)
├── research.md                # Phase 0: decisions ST-01 … ST-24
├── data-model.md              # Phase 1: tables, states, check result, report, codes
├── quickstart.md              # Phase 1: validation guide per story and criterion
├── contracts/
│   ├── transfer-api.yaml      # HTTP: export, upload, check, confirm, progress, download
│   ├── site-archive.md        # The archive format (becomes docs/contracts/site-archive.md)
│   ├── archive-parts.md       # ISiteArchivePart, session, gate, entries per part, remapping
│   ├── operator-commands.md   # `sites export|check|import`, exit codes
│   └── admin-transfer.md      # Admin screens, banner, accessibility
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
src/Shared/QuickBuilder.Shared.Infrastructure/
+ Data/SiteTransferSession.cs                        # shared connection + transaction (Principle I, 2.6.0)

src/Modules/
├── Sites/
│   ├── QuickBuilder.Sites.Contracts/
│   │   + Transfer/ISiteArchivePart.cs  PartContexts.cs  IArchiveReader.cs  IArchiveWriter.cs
│   │   +          IIdMap.cs  ImportMode.cs  ConflictPolicy.cs  ISiteTransferSession.cs  ISiteWriteGate.cs
│   │   + Events/SiteExportFinished.cs  SiteImportFinished.cs  SiteArchiveDownloaded.cs  SiteArchiveDeleted.cs
│   │   ~ SiteDto.cs                                 # transfer state
│   └── QuickBuilder.Sites/
│       + Domain/TransferJob.cs  TransferStates.cs  IdMap.cs  ConflictRules.cs
│       + Features/Transfer/StartExport.cs  ListTransfers.cs  CreateUpload.cs  UploadArchive.cs
│       +                   CheckArchive.cs  ConfirmImport.cs  GetTransfer.cs  CancelTransfer.cs
│       +                   DeleteArchive.cs  DownloadArchive.cs  TransferResponses.cs
│       + Features/Transfer/Engine/ExportRunner.cs  CheckRunner.cs  ImportRunner.cs
│       +                          ZipArchiveWriter.cs  ZipArchiveReader.cs  ArchiveManifest.cs
│       + Features/Transfer/SitesArchivePart.cs
│       + Infrastructure/TransferWorker.cs  TransferRetentionService.cs  SiteWriteGate.cs
│       +                SitesCommand.cs  SiteTransferOptions.cs (+ validator)
│       ~ Infrastructure/SitesDbContext.cs  + Infrastructure/Migrations/<ts>_AddTransferJobs.cs
│       ~ Features/UpdateSettings/  AddHostName/  RemoveHostName/  UpdateSearchSettings/   # gate
│       ~ SitesCapabilities.cs  SitesModule.cs  ErrorResults.cs
├── Pages/QuickBuilder.Pages/
│   + Features/Transfer/PagesArchivePart.cs  PageDocumentRemapper.cs  MenuDocumentRemapper.cs
│   ~ Infrastructure/DraftWrites.cs  PublishingWrites.cs  + menu, redirect and tree writes   # gate
├── Cms/QuickBuilder.Cms/
│   + Features/Transfer/CmsArchivePart.cs  ItemValuesRemapper.cs
│   ~ Infrastructure/ItemWrites.cs  + collection writes                                     # gate
├── Media/QuickBuilder.Media/
│   + Features/Transfer/MediaArchivePart.cs
│   ~ Features/Upload…  ReplaceFile/  DeleteFile/  folder features                          # gate
│   ~ Infrastructure/ContentSniffing (reused by the part)
├── Editor/QuickBuilder.Editor/
│   + Features/Transfer/DesignArchivePart.cs  DesignTokensRemapper.cs
│   ~ design draft and publish writes                                                       # gate
└── Identity/
    ├── QuickBuilder.Identity.Contracts/ + IHistoricalAuthors.cs  ~ IUserDirectory.cs (IsHistorical)
    └── QuickBuilder.Identity/
        + Domain/HistoricalAuthor.cs  Infrastructure/HistoricalAuthors.cs
        ~ Features/RecordSiteSecurityEvents/ (+ the 4 transfer events)
        ~ Infrastructure/IdentityDbContext.cs  UserDirectory.cs
        + Infrastructure/Migrations/<ts>_AddHistoricalAuthors.cs

src/admin/src/
+ api/transfers.ts
+ features/transfer/TransferTab.vue  ImportSiteView.vue  ImportCheckView.vue  TransferProgress.vue
+                   TransferReport.vue  RestoreConfirmDialog.vue  SiteImportBanner.vue  useTransfer.ts
~ features/sites/SiteSettingsView.vue  SitesListView.vue  app/SiteLayout.vue  router.ts
~ editor/state/autosave.ts  features/cms/items/ItemFormView.vue   # read-only on site.import_in_progress
~ i18n/en.json
src/admin/tests/features/transfer/ + ImportCheckView.spec.ts  RestoreConfirmDialog.spec.ts  TransferProgress.spec.ts  SiteImportBanner.spec.ts

tests/
~ QuickBuilder.ArchitectureTests/ + TransferSessionUsageTests.cs  ~ AuthorizationDeclarationTests.cs
+ QuickBuilder.UnitTests/Sites/Transfer/ArchiveManifestTests.cs  IdMapTests.cs  ConflictRulesTests.cs  TransferJobTests.cs
+ QuickBuilder.UnitTests/{Pages,Cms,Editor}/Transfer/*RemapTests.cs
~ QuickBuilder.IntegrationTests/
│   + Transfer/ExportContentsTests.cs  ExportSnapshotTests.cs  RoundTripTests.cs  StartFreshTests.cs
│   +          MergePolicyTests.cs  RestoreTests.cs  ArchiveRefusalTests.cs  ImportFailureTests.cs
│   +          TransferGateCoverageTests.cs  TransferEndpointsTests.cs  TransferRetentionTests.cs
│   +          TransferLockTests.cs  SitesCommandTests.cs  ArchiveFromPreviousReleaseTests.cs
│   +          TransferPerformanceTests.cs (manual)  UpgradeFrom023Tests.cs
│   ~ Isolation/Probes/ (+ TransferIsolationProbe)  Infrastructure/EndpointCatalog.cs
+ tests/fixtures/archives/ (golden archive per release)  tests/fixtures/transfer/seed-acme.http
docs/contracts/site-archive.md  docs/contracts/site-archive/    # +
docs/contracts/{page-schema,menu,collection-schema,design-tokens}/remap/   # +
docs/operations.md  docs/configuration.md  docs/backlog.md  docs/releases/unreleased/   # ~ / +
```

**Structure decision**:
- **Sites** owns everything about a transfer as a whole: jobs, the container, check and apply
  orchestration, the API, the command and the gate. It never sees another module's rows.
- **Each content module** owns its part under `Features/Transfer/`, beside the validators,
  walkers and write helpers it reuses. A change to a module's tables is then made in one
  place, next to its export.
- **Admin**: one `features/transfer/` folder. Other changes are wiring and the read-only
  state on `site.import_in_progress`.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. Admin upload, download and restore flows are checked by hand in quickstart §1, §2 and §4, until #25. |

Exception raised by this plan:

| Exception | Rule | Why it is needed | Simpler alternative rejected because | Follow-up |
|---|---|---|---|---|
| **E-7** Shared transaction for site transfer | Principle I: "No database transaction spans more than one module." | FR-005 (export reflects one point in time while editing continues) and FR-018 (an import is all or nothing, also after a crash) need every part to read one snapshot and to commit together. One PostgreSQL database makes this one transaction on one connection. Each module still writes only its own tables through its own `DbContext`, and only `ArchivePart` classes may enlist (`TransferSessionUsageTests`). | **Staged import with roll-forward** needs staging copies of about 30 tables in five modules and leaves a window where visitors see half an import. **Compensation** (undo by restore) can itself fail. **Per-module transactions sharing an exported snapshot** solve export only. | **Closed** on 2026-11-02 by constitution amendment 2.6.0, which writes this exception into Principle I. `TransferSessionUsageTests` (T019) enforces its limit. |

## Risks

- **Long transactions** (minutes) hold back vacuum and keep row locks on the target site.
  - *Mitigation*: only one transfer runs per instance. The import holds locks only on the
    target's rows, and its writes are paused anyway (ST-07). SC-002 is measured with
    autovacuum on. Exports read without locks.
- **Storage providers and unknown-length writes**: the ZIP is piped into `WriteAsync` without
  a length.
  - *Mitigation*: S3 already spools to disk for unknown lengths (`S3UploadSpool`). Local and
    Azure write streams. The first task verifies all three with a 5 GB fixture in their
    provider suites.
- **Proxy body limits** make large admin uploads fail before they reach the app.
  - *Mitigation*: upgrade note 2, the `413` message pointing to the command, and the command
    itself.
- **Write paths that miss the gate**.
  - *Mitigation*: `TransferGateCoverageTests` walks every write endpoint in `EndpointCatalog`.
    New endpoints are added to the catalog by its existing completeness test.
- **Media hash matching** reads target files of equal size during check (ST-08).
  - *Mitigation*: only equal sizes are hashed. A stored hash is deferred, and is worth adding
    if checks prove slow on large libraries.
- **Archives from hand edits** that pass every check but carry surprising content.
  - *Mitigation*: every document goes through the same validators as a member's save, and
    every file through the upload checks. Nothing in an archive is executable.

## Deferred (recorded, not built)

- Chunked, resumable uploads in the admin (ST-17).
- Encrypted or signed archives.
- Choosing a policy per conflict or per kind (spec assumption).
- Stored content hashes for media files (ST-08).
- Resuming an interrupted export or import instead of failing it (ST-15).
- Scheduled exports configured in the admin. Today they are scripted with the command
  (Story 5).
- Importing other products' formats.
