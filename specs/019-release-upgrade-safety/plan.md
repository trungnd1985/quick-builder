# Implementation Plan: Release Pipeline and Upgrade Safety

**Branch**: `019-release-upgrade-safety` | **Date**: 2026-10-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/019-release-upgrade-safety/spec.md`

**Issue**: #19.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none.
- **Constitution amendment in this PR**: 2.5.0 → 2.5.1 (PATCH, clarifications only),
  approved by the maintainer on 2026-10-23 (analysis findings C1, C2); it lands first, as
  T002:
  1. Principle I, *Modules*: Shared.Infrastructure also holds "the migration runner and its
     database version record, in a `platform` schema with its own migrations" (RL-07).
  2. Principle VI: "a forward migration of stored data" may be an upgrade step applied when
     the document is read and stored on its next save or publish; stored documents are not
     required to be rewritten in bulk (RL-19, clarification Q4).

## Summary

Operators upgrade by changing one image tag. To make that safe, this feature:

- **Ships releases.** A version tag starts a pipeline that builds one image for amd64 and
  arm64 on native runners, pushes it to GHCR by digest, and tests *that* digest: the
  reference smoke journey, an arm64 boot, and upgrade runs from earlier releases' database
  snapshots. Passing candidates wait as draft GitHub releases until the maintainer approves
  it by running the Promote release workflow (GitHub Free has no required reviewers on a
  private repository); approval tags the tested digest and publishes the notes.
- **Keeps a chain of real snapshots.** Each release's upgrade run, after passing, adds its
  own content journey to the upgraded database and attaches the dump, media and an
  expectations file to the release. The next release restores it, boots, and compares what
  visitors see, what is stored, and that members can sign in. 0.1.0 starts from a baseline
  written by the image at `5f549ae`.
- **Refuses unsafe starts.** A `platform.database_version` row records the highest release
  that migrated the database and an "upgrading to" mark. Before any module migrates, the
  runner checks it: a newer major.minor or a database older than the build's floor is
  refused, and a minimal refusal host answers health probes while nothing else runs.
- **Freezes what shipped.** CI and the pipeline fail on any edited migration since the last
  release, on migrations or schema raises in a patch, and on a breaking note outside a major.
- **Upgrades old content on read.** Each owning module runs stored documents through an
  upgrade chain before they leave the module; saves and publishes store the current version.
  Every contract is still at version 1, so the chains are empty and the mechanism is proven
  with test steps and too-new documents.
- **Tells operators.** Notes are assembled from per-PR fragments plus facts the tool derives
  (migrations, schema versions, digests, upgrade path); operations.md gains the upgrade and
  rollback procedure, which the pipeline itself drills.

Decisions are in [research.md](./research.md) (RL-01 … RL-22).

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin; GitHub Actions YAML.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 + Npgsql. Nothing new.
- **Tooling**: new console project `tools/QuickBuilder.Release` (`qb-release`), no packages
  beyond the BCL (RL-22). Docker Buildx, `docker buildx imagetools`, `gh`, `pg_dump` /
  `pg_restore` from the `postgres:16-alpine` image — all already on GitHub runners or in the
  compose file.
- **Admin**: Vue 3, Pinia, vue-i18n. Nothing new.

**Storage**: PostgreSQL 16+.
- New schema `platform`, migration `Initial`: **+** `database_version` (one row) and its
  history table (data-model §1).
- No module schema changes.

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL; the compose-driven
SmokeTests project for the release gates; vitest for the About panel. Main suites:
- `InstallationGateTests`, `ApplicationVersionTests`, `SchemaUpgradeChainTests`,
  `SchemaUpgradeCoverageTests`, `SchemaUpgradeGoldenTests`, `VisibleFormTests`, `Release/*`
  tool tests (unit);
- `InstallationStartupTests`, `RefusalHostTests`, `MigrateCommandTests`,
  `MigrationRerunTests`, `InstallationEndpointTests`, `SchemaReadSiteTests` (integration);
- `MigrationTransactionTests`, `SchemaReadCoverageTests`, boundary rule for
  `PlatformDbContext` (architecture);
- `ReleaseUpgradeTests` with `ContentJourney` (release gate; `Category=ReleaseUpgrade`).

**Target Platform**: Linux containers (Alpine), linux/amd64 and linux/arm64, published to
`ghcr.io/trungnd1985/quickbuilder`.

**Project Type**: Web application (modular-monolith backend, Vue admin, server-rendered
sites) plus its release tooling.

**Performance Goals**:
- Tag → candidate ready for approval ≤ 60 minutes (SC-011); budget: prepare 3, build 15
  (parallel), smoke 10, upgrade 20 (parallel legs), candidate 2.
- Upgrade on the reference setup: up → ready ≤ 5 minutes with the snapshot (SC-001).
- Refusal visible ≤ 30 s after start (SC-006); the check adds one query to every start.
- Rollback by the guide ≤ 15 minutes (SC-010).

**Constraints**:
- The check runs before any hosted service or module code touches the database.
- The readiness probe never reveals version numbers (anonymous).
- The server makes no outbound request; release-notes links are static.
- Snapshots contain invented data only; no real secret is ever in a dump or asset.
- `platform` changes are additive forever.

**Scale/Scope**:
- **Code**: 1 new tool project; `platform` DbContext, gate, refusal host and schema-upgrade
  chain in Shared.Infrastructure; read-site wiring in Pages, Editor, Cms; 1 Identity endpoint;
  1 admin panel.
- **Migrations**: 1 (`platform` `Initial`). Tables: 1 new, 0 changed.
- **Workflows**: 1 new (`release.yml`), 1 changed (`ci.yml`). Compose: 1 changed, 1 new
  override.
- **Endpoints**: 1 admin (`GET /api/v1/identity/installation`). **Capabilities**: 1.
  **Configuration keys**: 0.
- **Docs**: TEMPLATE plus 16 fragments for 0.1.0 (15 merged features and 019); operations, contributing, configuration
  (none), contracts README, page-schema §6, binding-expression versioning.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies, with a PATCH clarification.** <br>• No new module. The database version record is migration infrastructure in Shared.Infrastructure with its own `platform` schema, `PlatformDbContext` and history table (RL-07); the clarification names it in Principle I's list of what Shared.Infrastructure holds. It has no module concept in it. <br>• **Slices**: Identity `Features/GetInstallation/` (thin controller, query handler). <br>• **Cross-module**: modules read `IInstallationVersion` from Shared.Infrastructure; only the migrations namespace touches `PlatformDbContext` (new architecture rule). Upgrade chains stay inside their owning modules; nothing crosses a boundary with an old document. <br>• **Enforcement**: `ModuleBoundaryTests` (+ `PlatformDbContext` rule), `AuthorizationDeclarationTests` (+ capability). |
| II | Easy to Install, Operate and Upgrade | **Complies — this feature is its upgrade half.** <br>• Single image, now published multi-arch; the reference compose pulls it instead of building; install is still `docker compose up` (smoke gate measures it). <br>• No new configuration key. <br>• Migrations still automatic with the `migrate` alternative; both apply the same check. Forward-only and re-runnable, now tested (RL-20). <br>• Invalid combinations fail fast with a clear message (the refusal) instead of half-migrating. <br>• Offline: no outbound call; release notes link is static. <br>• Health checks: `/health/ready` gains state `Incompatible`. |
| III | Upgrades Never Lose Data | **Complies — this feature enforces it.** <br>• SemVer, stamped into the image (RL-01). <br>• Every release is gated on upgrades from real snapshots of earlier releases (previous, first of major, last minor of previous major) with page, record, sign-in and draft checks (RL-09, RL-17), and a rollback drill (RL-18). <br>• Breaking changes only in a major, enforced by the notes tool; upgrade notes generated per release with every section (RL-16). <br>• Export/import (#20) is unchanged and not required here (spec assumption). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL remains the only runtime dependency. The pipeline uses GitHub-hosted tooling only. |
| V | Multi-Site Within One Installation | **Complies.** `platform.database_version` is installation-wide, not site data; exempted in `SiteFilterCoverageTests` with that reason. Snapshots and the journey create several sites and the gate checks every site's pages on its own host names. |
| VI | Content Schemas Are Versioned Contracts | **Complies, with a PATCH clarification.** <br>• Every stored document still carries `schemaVersion`; chains upgrade older versions on read inside the owning module; Rendering and the admin receive only current versions (RL-19). <br>• "Forward migration of stored data" is applied lazily (read, then stored on save or publish), as clarified; page-schema.md §6 and binding-expression.md updated to match; golden files per step required by `SchemaUpgradeCoverageTests`. <br>• Too-new and failed documents fail one page safely. |
| VII | Draft and Published Are Separate Worlds | **Complies.** Reading never writes; drafts are stored upgraded only when saved; published history keeps its versions; a rollback publishes through the chain. The public rendering path gains no write access. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** Upgrading happens before render and before caching, so a cached page is always rendered from a current document. No change to invalidation. The gate's visible-form comparison checks rendered output across releases. |
| IX | Secure by Default | **Complies.** <br>• Version shown only to Administrators (new capability); probes and the refusal host never reveal versions. <br>• Images non-root as before; labels carry no secret; the pipeline uses `GITHUB_TOKEN` only, scoped to `contents` and `packages`. <br>• Snapshots: invented data, generated passwords, database password not in the dump (FR-013). <br>• Logs: `schema_upgrade_failed` carries ids and versions only, no content. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies — fulfils its release clause.** The release pipeline boots the published image and runs the smoke journey (setup, create page, publish, view). Integration tests for the endpoint, the gate, the refusal host and the read sites; unit tests for every decision rule and the tool; golden files for upgrade steps. Migrations and upgrades are critical paths: Shared.Infrastructure's coverage is already enforced at 80 %. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **No new package**, one tool project justified (RL-22). <br>• **Not built**: a real schema v2, bulk rewrites, signing and SBOMs, a second registry, rolling upgrades, automatic downgrade, an in-app updater. <br>• **Reuse**: the smoke journey and `ComposeDeployment`, the migration runner and its locks, GitHub releases and environments, D-17's health states. <br>• **Delivery**: one PR. If it must split: (1) version stamping, `platform`, the check, refusal host and About panel (US4); (2) schema chains and read sites (US3); (3) the tool, fragments, `ci.yml` checks (US5 notes, FR-017); (4) `release.yml`, compose changes, journey, snapshots and gates (US1, US2). Each is safe alone; (4) needs (1)–(3). |

**Technology Constraints**:
- Multi-arch images built by GitHub Actions and published to a container registry, with
  release notes and upgrade notes — exactly the constitution's distribution clause.
- EF Core migrations only (`platform` `Initial`); MVC controller for the endpoint; no
  Minimal APIs. The refusal host uses MVC-free terminal middleware: it is not an API surface
  but a stand-in for the process when the real host must not start (justified in RL-08); it
  maps two fixed paths and a fallback, with no handlers, data access or business logic.

**Development Workflow**:
- Issue #19 → `specs/019-release-upgrade-safety` → branch `019-release-upgrade-safety` → one
  PR that closes #19.
- Clarify has run (5 questions, all answered). Analyze follows tasks.
- **Contract and registry changes in the same PR**:
  - `.specify/memory/constitution.md` (2.5.1, after approval);
  - `docs/contracts/page-schema.md` §6, `docs/contracts/binding-expression.md` (versioning),
    `docs/contracts/README.md` (upgrade folders);
  - `specs/002-identity-setup/contracts/authorization.md` §2 (capability);
  - `specs/001-foundation-skeleton/contracts/admin-api.yaml` (health state `Incompatible`) and
    `specs/012-publishing-rendering/contracts/public-rendering.md` (Rendering receives current
    versions only);
  - `docs/operations.md` (Upgrading and rolling back), `docs/contributing.md` (Making a
    release, Adding stored data, Raising a schema version, release-note fragments),
    `docs/releases/` (TEMPLATE, 0.1.0 fragments), `README.md` (install from the release
    compose file).

**Operator impact**:
- **New configuration keys**: none.
- **Migrations**: 1, additive and automatic: `platform` `Initial`.
- **New dependencies**: none at run time.
- **Reference compose file**: pulls `ghcr.io/trungnd1985/quickbuilder` instead of building;
  each release attaches a copy pinned to its exact version.
- **Upgrade notes** (become 019's fragment):
  1. QuickBuilder now has release numbers. The running release is in Settings → About this
     installation and in the first log line.
  2. On start, QuickBuilder checks which release last upgraded the database and refuses to run
     on a database from a newer release or one too old to upgrade directly; readiness shows
     `Incompatible` and the log says what to do. Nothing is changed when it refuses.
  3. Back up the database and the media volume before every upgrade; that backup is the only
     way back (docs/operations.md, "Upgrading and rolling back").
  4. Install from the `docker-compose.yml` attached to a release, which names an exact
     version; upgrade by changing that version.

**Post-Phase-1 re-check**:
- the check runs before hosted services and before `AutoMigrate`, and refusal starts no
  module (installation-version.md §1, §3);
- only the migrations namespace references `PlatformDbContext`;
- the endpoint declares its capability; the probe never shows versions;
- every read path of schema-upgrades.md §3 is guarded by `SchemaReadCoverageTests`; reads
  never write;
- the gate compares visible output, records, sign-in and drafts, and drills rollback;
- the pipeline promotes the tested digest without rebuilding;
- no new package; one EF migration.

**Gate: PASS**, with the 2.5.1 clarification (approved 2026-10-23) landing first in this PR.
No exception is recorded.

## Project Structure

### Documentation (this feature)

```text
specs/019-release-upgrade-safety/
├── plan.md                    # This file
├── spec.md                    # Feature specification (clarified: 5 questions)
├── research.md                # Phase 0: decisions RL-01 … RL-22
├── data-model.md              # Phase 1: platform table, compatibility rules, snapshot, fragments, lifecycle
├── quickstart.md              # Phase 1: validation guide per story
├── contracts/
│   ├── installation-version.md  # Startup order, check, refusal host, admin endpoint
│   ├── release-pipeline.md      # Workflow jobs, tags, promote, qb-release commands, CI changes
│   ├── release-notes.md         # Template, fragment format, assembly rules
│   ├── snapshot.md              # Bundle, expectations, comparison, gate steps
│   └── schema-upgrades.md       # Chains, read sites, golden files, raising a version
├── checklists/
│   └── requirements.md
└── tasks.md                   # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
.specify/memory/constitution.md                       # ~ 2.5.1 (after approval)
Directory.Build.props                                 # ~ VersionPrefix 0.1.0, VersionSuffix dev, QbUpgradeFloor 0.0.0
Dockerfile                                            # ~ QB_VERSION_SUFFIX, QB_REVISION args; OCI labels
docker-compose.yml                                    # ~ image from QUICKBUILDER_IMAGE (GHCR), no build
+ docker-compose.build.yml                            # build override for developers and PR CI
+ docker-compose.gate.yml                             # reference-shape limits for the release gates (2 CPUs, 4 GB)
README.md                                             # ~ install from a release
.github/workflows/
+ release.yml
~ ci.yml                                              # release-checks job; build override for smoke/perf
docs/
~ operations.md  contributing.md
~ contracts/README.md  contracts/page-schema.md  contracts/binding-expression.md
+ releases/TEMPLATE.md
+ releases/unreleased/<issue>-<slug>.md               # one per merged feature for 0.1.0, and 019's

tools/
+ QuickBuilder.Release/                               # qb-release (RL-22)
    + Program.cs  BuildProps.cs  Git.cs  ReleaseVersions.cs  UpgradeSources.cs
    + MigrationFreeze.cs  PatchRules.cs  Fragment.cs  FragmentCheck.cs  VisibleChanges.cs
    + NotesAssembler.cs  ConfigurationKeys.cs
    (SemanticVersion.cs linked from Shared.Infrastructure/Versioning)

src/QuickBuilder.Host/
~ Program.cs                                          # version log line; refusal → RefusalHost
+ Startup/RefusalHost.cs
~ QuickBuilder.Host.csproj                            # AssemblyMetadata: QbUpgradeFloor

src/Shared/QuickBuilder.Shared.Infrastructure/
+ Versioning/ApplicationVersion.cs  SemanticVersion.cs  IInstallationVersion.cs  InstallationVersion.cs
+ Migrations/Platform/PlatformDbContext.cs  DatabaseVersionRow.cs  InstallationGate.cs  GateDecision.cs
+ Migrations/Platform/Migrations/<ts>_Initial.cs
~ Migrations/ModuleMigrationRunner.cs                 # platform first, check, mark, record
~ Startup/StartupState.cs                             # StartupPhase.Incompatible
+ Schemas/ISchemaUpgradeStep.cs  SchemaUpgradeChain.cs  SchemaRead.cs

src/Modules/Identity/QuickBuilder.Identity/
+ Features/GetInstallation/GetInstallation.cs        # controller, query, handler
~ IdentityModule.cs                                  # capability identity.installation.view

src/Modules/Pages/QuickBuilder.Pages/
+ Infrastructure/Schema/PageSchemaUpgrades.cs  MenuSchemaUpgrades.cs  BindingUpgrades.cs   # empty chains
~ Infrastructure/PublishedContentReader.cs  PageReads.cs  ComponentReads.cs  MenuWrites.cs  PublishingWrites.cs
~ PagesErrors.cs  PagesErrorResults.cs               # document_too_new, document_upgrade_failed

src/Modules/Editor/QuickBuilder.Editor/
+ Infrastructure/DesignTokenUpgrades.cs
~ Infrastructure/PublishedDesignReader.cs  (draft reads)  EditorErrors.cs

src/Modules/Cms/QuickBuilder.Cms/
+ Infrastructure/Schema/CollectionSchemaUpgrades.cs
~ Infrastructure/ItemReads.cs  (collection reads)  CmsErrors.cs

src/Modules/Rendering/QuickBuilder.Rendering/
~ Rendering/PageRenderer.cs  Rendering/Navigation/MenuModel.cs   # SupportedSchemaVersions = { current }

src/admin/src/
+ features/settings/AboutInstallationPanel.vue
~ features/settings/ (route, Administrators only)  i18n/en.json
+ api/installation.ts
src/admin/tests/
+ features/settings/AboutInstallationPanel.spec.ts

tests/
+ QuickBuilder.UnitTests/Release/                    # tool tests, VisibleFormTests
+ QuickBuilder.UnitTests/Versioning/                 # ApplicationVersionTests, InstallationGateTests
+ QuickBuilder.UnitTests/Schemas/                    # SchemaUpgradeChainTests, SchemaUpgradeCoverageTests, SchemaUpgradeGoldenTests
~ QuickBuilder.ArchitectureTests/                    # PlatformDbContext rule, MigrationTransactionTests, SchemaReadCoverageTests, SiteFilterCoverage exemption
~ QuickBuilder.IntegrationTests/
│   + Versioning/InstallationStartupTests.cs  RefusalHostTests.cs  MigrateCommandTests.cs  MigrationRerunTests.cs
│   + Identity/InstallationEndpointTests.cs  Schemas/SchemaReadSiteTests.cs
│   ~ Infrastructure/EndpointCatalog.cs
~ QuickBuilder.SmokeTests/
│   ~ ComposeDeployment.cs                            # QB_SMOKE_IMAGE pull mode; build override otherwise
│   + Release/ContentJourney.cs  ReleaseExpectations.cs  SnapshotBundle.cs  ReleaseUpgradeTests.cs
│   + Release/VisibleForm.cs  ExpectationComparer.cs  VisibleChangeAllowList.cs   # pure; linked into UnitTests
specs/001-foundation-skeleton/contracts/admin-api.yaml                         # ~
specs/002-identity-setup/contracts/authorization.md                            # ~
specs/012-publishing-rendering/contracts/public-rendering.md                  # ~
```

**Structure decision**:
- **Shared.Infrastructure**: versioning, the `platform` record and gate (beside the runner they
  serve), and the generic upgrade chain.
- **Host**: only the refusal host and the version log line; it composes, nothing more.
- **Modules**: each owns the chains for the contracts it stores and wires them into its own
  read paths; Identity exposes the version to Administrators.
- **tools/**: the release tool, outside `src/` because it never ships in the image.
- **SmokeTests**: the release gate, beside the smoke journey it reuses.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. The release gates drive the API, as the smoke journey does; the About panel is covered by vitest. Until #25. |

**New exceptions**: none. The two constitutional points are handled by the PATCH
clarification rather than recorded as exceptions.

## Risks

- **arm64 runners unavailable or slow** (`ubuntu-24.04-arm` is a GitHub-hosted label for
  public repositories).
  - *As built*: the repository is private on GitHub Free, so arm64 runs under QEMU on
    `ubuntu-latest` by default; the repository variable `ARM64_RUNNER` switches to a native
    runner. Emulation slows the arm64 build and boot, so the 60-minute target (SC-011) is at
    risk until the repository is public or a native runner is available.
- **A private repository hides the releases page from operators**: GHCR images can be public
  while the repository is private, but release notes, the pinned compose file and the upgrade
  notes on GitHub Releases are not visible to anyone outside it.
  - *Mitigation*: make the repository public before announcing 0.1.0, or publish the notes
    and compose file somewhere operators can read them. A decision for the maintainer.
- **Visible-form comparison too strict**: a deliberate change to visible output (a renamed
  default text) fails the gate.
  - *Mitigation*: built in this feature (FR-012a, analysis U3): a reviewed
    `docs/releases/unreleased/<issue>-visible-changes.json` names the pages and the reason;
    the gate accepts only those, fails on unused entries, and the notes list them
    (snapshot.md §3).
- **Time-dependent snapshots**: scheduled content or date-relative text would change a
  snapshot restored months later and fail the gate falsely.
  - *Mitigation*: far-future schedules only, and capture refuses pages showing the current
    date (snapshot.md §3, analysis U1).
- **No published image between merge and v0.1.0**: the reference compose names
  `ghcr.io/…:0`, which exists only after the first approval; `docker compose up` from the
  repository fails until then.
  - *Mitigation*: the maintainer cuts `v0.1.0` right after merge (T086); until then the
    README points to `-f docker-compose.build.yml` (T081, analysis C4).
- **CI runners are not the reference setup**: up → ready on a GitHub runner (4 vCPU, 16 GB)
  is faster than on the reference 2 vCPU / 4 GB.
  - *Mitigation*: the gate's compose project caps the app at `cpus: 2`, `mem_limit: 4g`
    (`docker-compose.gate.yml`), so SC-001 is measured on the reference shape (analysis U5).
- **Snapshot growth**: each release adds one site; after many releases the dump grows.
  - *Mitigation*: a site of the journey is small (≈1 MB with media); at 100 releases still
    well within release-asset limits (2 GB each).
- **A release fails after its tag is public**: operators might pull the `sha-` tag.
  - *Mitigation*: nothing documents `sha-` tags; the release compose file and notes name only
    exact versions, which exist only after approval.
- **First promote with a private package**: GHCR packages start private.
  - *Mitigation*: runbook step (contracts/release-pipeline.md §7); the arm64 and smoke jobs
    authenticate with `GITHUB_TOKEN`, so the gates do not depend on visibility.
- **Baseline drift**: the 0.1.0 journey uses today's API against the 5f549ae image; an API
  added in 019 would not exist there.
  - *Mitigation*: the journey uses only endpoints that existed at 5f549ae (019 adds just
    `GET …/installation`, which the journey does not call).

## Deferred (recorded, not built)

- Image signing, SBOM and provenance attestations; a second registry.
- Bulk rewrite of old documents to retire upgrade steps.
- Row checksums for journey tables (analysis U4, accepted).
- Zero-downtime rolling upgrades; automatic downgrade (out of scope by the spec).
- Driving the editor in a browser during the gates (E-5, #25).
