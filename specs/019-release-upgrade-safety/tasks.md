  *Changed 2026-10-24*: the job moved to `WF/promote.yml`, run by hand with the tag: it refuses unless the Release run for the tag succeeded and the draft has no failure banner, and reads the tested digest from the draft's `digests.txt`.
  *Changed 2026-10-24*: native arm64 runners are free for public repositories only, so arm64 builds under QEMU on `ubuntu-latest` unless the repository variable `ARM64_RUNNER` names a native runner. The `arm64-boot` job likewise, with `DOCKER_DEFAULT_PLATFORM=linux/arm64`.
  *Changed 2026-10-24*: the repository is private on GitHub Free, which offers no required reviewers on environments. Only the `pre-0.1.0-baseline` tag is needed; approval is the manual **Promote release** workflow (`WF/promote.yml`).
---

description: "Task list for 019-release-upgrade-safety"
---

# Tasks: Release Pipeline and Upgrade Safety

**Input**: Design documents from `/specs/019-release-upgrade-safety/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for decision logic (here: the compatibility rules, versions, upgrade sources,
    the release tool);
  - golden files for contracts (here: schema upgrade steps);
  - a release pipeline test that boots the published image (this feature builds it);
  - 80 % coverage on critical paths (here: migrations, upgrades, the startup check).
- The spec measures behaviour through automated checks: SC-001 … SC-011.
- Within each story, test tasks come first and must fail before implementation starts.

**Changed after analysis (2026-10-23)**: T002, T022, T023, T026, T027, T044, T047, T051,
T053, T056, T064, T078, T081 and T086 were amended; T084 was removed (resolved by finding I1
in spec.md FR-030); T087 (US1), T088 (US1) and T089 (US5) were added in their phases. No task
was renumbered.

**Organization**: tasks are grouped by user story (US1–US5 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `SHARED` = `src/Shared/QuickBuilder.Shared.Infrastructure`
- `HOST` = `src/QuickBuilder.Host`
- `IDENTITY` = `src/Modules/Identity/QuickBuilder.Identity`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `EDITORM` = `src/Modules/Editor/QuickBuilder.Editor`
- `CMS` = `src/Modules/Cms/QuickBuilder.Cms`
- `RENDER` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `TOOL` = `tools/QuickBuilder.Release`
- `CONTRACTS` = `docs/contracts`
- `RELEASES` = `docs/releases`
- `ADMIN` = `src/admin/src`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`
- `WF` = `.github/workflows`

**Conventions**:
- **The startup check** runs before the `AutoMigrate` decision, before the HTTP pipeline and
  before any hosted service (installation-version.md §1). Nothing in a refusal path writes to
  the database.
- **Anonymous surfaces never show versions**: readiness, the refusal host and the public site.
  Versions appear in logs and to Administrators only.
- **Reads never write**: an upgrade chain's result is stored only by a save or publish
  (schema-upgrades.md §3). Logs about documents carry contract, subject kind, subject id and
  versions only.
- **The release tool** (`TOOL`) uses the BCL only and shells out to `git`; it never calls
  GitHub. Exit codes: 0 pass, 1 rule failed (message names the file, section or version),
  2 usage error.
- **Release tests** in `STESTS/Release/` are excluded from the normal CI run like every smoke
  test; pure helpers they use (`VisibleForm`, `ExpectationComparer`) are linked into
  `UTESTS` as source files so their unit tests run in CI.
- **Admin text**: every visible string goes through vue-i18n keys in `ADMIN/i18n/en.json` in
  the same task.
- **New endpoints** declare exactly one `[RequireCapability(...)]`, dispatch through
  `IDispatcher`, and are listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.

**References**:
- decisions: `RL-xx` in [research.md](./research.md);
- the record, the compatibility table, snapshot contents, fragments, lifecycle:
  [data-model.md](./data-model.md);
- startup order, refusal host, admin endpoint: [contracts/installation-version.md](./contracts/installation-version.md);
- workflow jobs, promote, `qb-release` commands, CI changes: [contracts/release-pipeline.md](./contracts/release-pipeline.md);
- template, fragment format, assembly rules: [contracts/release-notes.md](./contracts/release-notes.md);
- bundle, expectations, comparison, gate steps: [contracts/snapshot.md](./contracts/snapshot.md);
- chains, read sites, golden files, raising a version: [contracts/schema-upgrades.md](./contracts/schema-upgrades.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, the constitution clarification, the tool project and
the repository settings the pipeline needs.

- [X] T001 Commit `specs/019-release-upgrade-safety/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `019-release-upgrade-safety`, referencing #19.
- [X] T002 Amend `.specify/memory/constitution.md` to 2.5.1 (PATCH): in Principle I, add "the migration runner and its database version record (schema `platform`, own migrations)" to what `QuickBuilder.Shared.Infrastructure` holds; in Principle VI, add that a forward migration of stored data may be an upgrade step applied on read and stored on the next save or publish, with no bulk rewrite required. Update the Sync Impact Report (reason: research RL-07 and RL-19, clarification Q4; affected specs: 019 only) and **Last Amended**. **Precondition**: the maintainer approved the clarification on 2026-10-23 (analysis findings C1, C2); record the approval in the PR description. T010 and T049 must not start before T002 is committed.
- [X] T003 Create `TOOL/QuickBuilder.Release.csproj` (console, `net10.0`, `AssemblyName` `qb-release`, no package references, warnings as errors via `Directory.Build.props`) with an empty `TOOL/Program.cs` returning 2 for unknown commands; add it to `QuickBuilder.slnx`; reference it from `UTESTS/QuickBuilder.UnitTests.csproj`. Confirm `dotnet build QuickBuilder.slnx` and `dotnet format --verify-no-changes` pass.
- [X] T004 Maintainer, outside the code: push tag `pre-0.1.0-baseline` on commit `5f549ae`; create the GitHub environment `release` with the maintainer as required reviewer and deployment tags limited to `v*` (release-pipeline.md §1, §7). Record both in the PR description. T041 and T044 need them.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- one version number stamped into every build (RL-01);
- the `platform` schema and the record written on every successful start (RL-07), without
  refusals yet;
- the reference compose file that pulls a published image, and a build override (RL-10).

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Version stamping (RL-01, FR-001)

- [X] T005 [P] Create `UTESTS/Versioning/ApplicationVersionTests.cs`: `SemanticVersion` parses `0.1.0`, `0.3.0-dev`, `1.2.3-rc.4`, `0.2.0+abc123` (build metadata ignored) and refuses malformed strings; ordering follows SemVer 2.0 §11 (`0.2.0-dev < 0.2.0-rc.1 < 0.2.0 < 0.2.1 < 0.3.0-dev`); `MajorMinor` compares only major.minor; `ApplicationVersion` reads `Version`, `Revision` and `UpgradeFloor` from an assembly's informational version and `AssemblyMetadata("QbUpgradeFloor")`. It must fail first.
- [X] T006 Create `SHARED/Versioning/SemanticVersion.cs` (immutable record struct with `Parse`, `TryParse`, `CompareTo`, `MajorMinor`, `IsPreRelease`, `ToString`) and `SHARED/Versioning/ApplicationVersion.cs` (singleton built from the entry assembly; `Revision` from the `+<sha>` suffix, null when absent). Register `ApplicationVersion` in `SHARED/SharedInfrastructureRegistration.cs`. Make T005 pass.
- [X] T007 Set `<VersionPrefix>0.1.0</VersionPrefix>`, `<VersionSuffix>dev</VersionSuffix>` and `<QbUpgradeFloor>0.0.0</QbUpgradeFloor>` in `Directory.Build.props`, each with a comment pointing to research RL-01; add `<AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute"><_Parameter1>QbUpgradeFloor</_Parameter1><_Parameter2>$(QbUpgradeFloor)</_Parameter2></AssemblyAttribute>` to `HOST/QuickBuilder.Host.csproj`. In `HOST/Program.cs`, log `QuickBuilder {Version} ({Revision})` as the first line after the logger exists (FR-006).
- [X] T008 In `Dockerfile`: add `ARG QB_VERSION_SUFFIX=dev` and `ARG QB_REVISION=` to the build stage and pass `-p:VersionSuffix=${QB_VERSION_SUFFIX} -p:SourceRevisionId=${QB_REVISION}` to `dotnet publish`; add `ARG QB_VERSION` and `LABEL org.opencontainers.image.source=https://github.com/trungnd1985/quick-builder org.opencontainers.image.version=$QB_VERSION org.opencontainers.image.revision=$QB_REVISION org.opencontainers.image.licenses=MIT` to the runtime stage (RL-02). Build once and check the first log line says `0.1.0-dev`.
- [X] T009 [P] Link `SHARED/Versioning/SemanticVersion.cs` into `TOOL/QuickBuilder.Release.csproj` as `<Compile Include="..\..\src\Shared\QuickBuilder.Shared.Infrastructure\Versioning\SemanticVersion.cs" Link="SemanticVersion.cs" />` so the tool and the server order versions identically; add `TOOL/BuildProps.cs` reading `VersionPrefix` and `QbUpgradeFloor` from `Directory.Build.props` (plain XML).

### The `platform` record (RL-07, data-model §1)

- [X] T010 Create `SHARED/Migrations/Platform/PlatformDbContext.cs` (schema `platform`, history table `platform.__ef_migrations_history`), `SHARED/Migrations/Platform/DatabaseVersionRow.cs` (columns of data-model §1, `CHECK (id = 1)`), a design-time factory as the modules have, and generate `SHARED/Migrations/Platform/Migrations/<ts>_Initial.cs`. Depends on T002.
- [X] T011 [P] Create `ITESTS/Versioning/InstallationStartupTests.cs` with the passing cases only: a fresh database ends with `recorded_version = 0.1.0-dev` and `upgrading_to` null; a second start writes nothing (`pg_stat_user_tables` counters unchanged in every schema); a pre-release database (modules migrated, no `platform` schema, as the 5f549ae build leaves it) passes and is recorded; with `Database:AutoMigrate=false` the `platform` schema is still migrated, readiness says `MigrationsPending` and the record is unchanged. It must fail first.
- [X] T012 Change `SHARED/Migrations/ModuleMigrationRunner.cs` to follow installation-version.md §1 steps 2–9 for the passing rows of data-model §2 (1, 2, 5, 8, 9): migrate `platform` under advisory lock `platform` before the `AutoMigrate` decision; write `upgrading_to` before the first module migration when the rules say so; after all modules, set `recorded_version = max(R, V)` and clear `upgrading_to` in one statement; write nothing when nothing changes; log `Database at {R}; upgraded to {V}` or `Database at {V}`. Put the decision in `SHARED/Migrations/Platform/InstallationGate.cs` (pure function of `V`, `F`, the row and `H`) returning `GateDecision` (`SHARED/Migrations/Platform/GateDecision.cs`). Make T011 pass.
- [X] T013 [P] In `ATESTS/ModuleBoundaryTests.cs`, add the rule that only types in `QuickBuilder.Shared.Infrastructure.Migrations` reference `PlatformDbContext`; in `ATESTS/SiteFilterCoverageTests.cs` (or wherever exemptions live), exempt `DatabaseVersionRow` with the reason "installation-wide, not site data (019 RL-07)".

### Compose: pull by default, build on request (RL-10)

- [X] T014 Change `docker-compose.yml`: `image: ${QUICKBUILDER_IMAGE:-ghcr.io/trungnd1985/quickbuilder:0}`, remove `build:`, and update the header comment (install from a release's compose file; exact versions; upgrade by changing the version). Create `docker-compose.build.yml` with `services.app.build` (context `.`, `Dockerfile`) and `image: quickbuilder:local`, with a comment saying it is for developers and pull-request CI.
- [X] T015 Change `STESTS/ComposeDeployment.cs`: when `QB_SMOKE_IMAGE` is set, export `QUICKBUILDER_IMAGE` to compose, run `compose pull app` instead of `build`, and write the pulled image's repo digest to the test output; otherwise layer `docker-compose.build.yml` after `docker-compose.yml` and before other override files and build. Keep `down -v` and the project name. Run `ReferenceDeploymentSmokeTests` locally in build mode; it must pass.
- [X] T016 In `WF/ci.yml`, make the `image` job build with `docker build -t quickbuilder:local .` and confirm the `smoke` and `performance` jobs still pass through `ComposeDeployment`'s build mode (no other change needed); fix any override file (`docker-compose.perf.yml`, `docker-compose.forms-perf.yml`, `docker-compose.storage.yml`) that assumed `build:` in the base file.

**Checkpoint**: every build knows its version; every start records it; compose pulls a
published image by default. User stories can start.

---

## Phase 3: User Story 1 - Upgrade by replacing the image (Priority: P1) 🎯 MVP

**Goal**: a database written by an earlier build, with every kind of stored data, upgrades by
starting the new image, and visitors, members and editors find everything as before;
migrations re-run and resume safely.

**Independent Test**: quickstart §3's `upgrade (baseline)` leg run locally: restore the
baseline bundle, start the branch image, and pass steps 1–7 of snapshot.md §4.

### Tests for User Story 1 ⚠️

- [X] T017 [P] [US1] Create `UTESTS/Release/VisibleFormTests.cs` on fixed HTML strings: title plus collapsed body text; `href`, `src`, `srcset`, `action`, `alt` in document order; `blocks.<hash>.css` and hashed scripts normalised; media query strings kept; markup and class changes give the same form (snapshot.md §3). Link `STESTS/Release/VisibleForm.cs` into `UTESTS/QuickBuilder.UnitTests.csproj` as a source file. It must fail first.
- [X] T018 [P] [US1] Create `UTESTS/Release/ExpectationComparerTests.cs`: page status or fingerprint change → failure naming host, path and a text diff of the first 400 characters; a counted table with fewer rows or missing → failure naming it; more rows → pass; an unknown table at capture → failure; merging snapshot(N-1)'s file with N's keeps every page and member (snapshot.md §2–§3). Link `STESTS/Release/ExpectationComparer.cs`. It must fail first.
- [X] T019 [P] [US1] Create `ITESTS/Versioning/MigrationRerunTests.cs` (FR-016, SC-007): on a database built from the current migrations plus seeded content, run `ModuleMigrationRunner.RunAsync(force: false)` and then the `migrate` command path; record `n_tup_ins + n_tup_upd + n_tup_del` per table before and after; assert zero writes outside `platform` and history tables, and "already up to date" logged per module. It must fail first where the second run writes.
- [X] T020 [P] [US1] Create `ITESTS/Versioning/InterruptedMigrationTests.cs` (FR-018): migrate Pages and Cms down to an earlier migration, write `upgrading_to = V`, apply only half the pending migrations through a context and dispose it, then start the runner: every module completes, `recorded_version = V`, `upgrading_to` null. It must fail first.
- [X] T021 [P] [US1] Create `ATESTS/MigrationTransactionTests.cs`: reflect every `Migration` subclass in module and `platform` assemblies; fail when any operation uses `suppressTransaction: true` without `IF NOT EXISTS`/`IF EXISTS` in its SQL. Add `ATESTS/MigrationRunnerSurfaceTests.cs`: `ModuleMigrationRunner` and `MigrateCommand` never call `MigrateAsync` with a target migration (FR-015).

### Implementation for User Story 1

- [X] T022 [US1] Create `STESTS/Release/VisibleForm.cs` (pure, no Docker; a hand-written tokenizer for start tags, attributes, text and entities, enough for the server's own well-formed output — no new package, research RL-17) and `STESTS/Release/ExpectationComparer.cs` (compare, merge, format diffs). Make T017 and T018 pass.
- [X] T023 [US1] Create `STESTS/Release/ReleaseExpectations.cs`: the JSON model of snapshot.md §2 (`format: 1`); `CaptureAsync(ComposeDeployment, admin credentials)` enumerating every site, its first enabled host name, its published pages and item pages, one 404 path and one redirect per site through the admin API, fetching each page from the public host and computing `VisibleForm` fingerprints; counting rows of every table in every module schema through `docker compose exec db psql`, failing on a table neither counted nor in `VolatileTables` (the list of snapshot.md §3 with one reason per entry), and failing on a page whose visible form contains the current date ("time-dependent page", snapshot.md §3); `ReadAsync`/`WriteAsync`.
- [X] T024 [US1] Create `STESTS/Release/SnapshotBundle.cs`: `DumpAsync` (stop `app`, `pg_dump --format=custom --no-owner --no-acl` from `db`, `tar -czf` of the media volume through a throwaway container), `RestoreAsync` (stop, remove volumes, start `db`, `pg_restore --no-owner --role=quickbuilder`, untar as the image's `app` user), with the asset names of data-model §3.
- [X] T025 [US1] Create `STESTS/Release/ContentJourney.cs`, part 1, through the admin API only: claim setup (or sign in as the journey administrator), create site `release-<V>` with two host names and a disabled second site; members with each role and generated passwords; pages (home, nested child, draft-only, published with unpublished changes); every block type including rich text with every 035 format; a shared component on two pages; published design tokens with menu style; an image with variants and a document file; a page rolled back to an earlier version (data-model §4, rows 009–013 and 024 design).
- [X] T026 [US1] Extend `STESTS/Release/ContentJourney.cs`, part 2: a collection with every field type; items published, draft, and scheduled to publish and unpublish on `2099-01-01` (never sooner: snapshots must not change when restored later, snapshot.md §3); an item template and a collection list with paging and filters; SEO settings, a redirect, a canonical address and structured data; a menu with nested entries shown by a Navigation block; a form with every field type, two submissions through the public host and one caught as spam. Return the expectations' `members` and `drafts` lists. Use invented data only (`example.test`, fixed fake names) (FR-013).
- [X] T027 [US1] Create `STESTS/Release/ReleaseUpgradeTests.cs` (`[Trait("Category","ReleaseUpgrade")]`, in `DeploymentGroup`): reads `QB_UPGRADE_FROM` (`baseline` or a version), `QB_SMOKE_IMAGE`, `QB_PREVIOUS_IMAGE`, `QB_SNAPSHOT_DIR`, `QB_SNAPSHOT_OUT`; in `baseline` mode builds the image at tag `pre-0.1.0-baseline` from a `git worktree`, starts it, runs `ContentJourney`, captures expectations and dumps (RL-15); layers `docker-compose.gate.yml` (T088) so the app runs on the reference shape; then runs snapshot.md §4 steps 1–7 against `QB_SMOKE_IMAGE` (restore, up → ready ≤ 5 min with the time printed, pages with the allow-list of T087, pages, records, sign-in and drafts, background idle and pages again, restart and zero writes) and step 8 (journey of the new version, capture with merge, dump to `QB_SNAPSHOT_OUT`). Each failure names its step.
- [X] T028 [US1] Run `ReleaseUpgradeTests` locally with `QB_UPGRADE_FROM=baseline` against an image built from this branch; fix whatever it finds in migrations or reads (not in the comparison); record the run, the up → ready time and the bundle sizes in `specs/019-release-upgrade-safety/quickstart-results.md`.

*Added after analysis (2026-10-23)*:

- [X] T087 [US1] Implement the visible-change allow-list (FR-012a, snapshot.md §3, analysis U3): create `STESTS/Release/VisibleChangeAllowList.cs` (pure; loads `docs/releases/unreleased/*-visible-changes.json`; `host` exact or `*`, `path` exact or ending in `/*`) and make `STESTS/Release/ExpectationComparer.cs` accept a fingerprint difference only on a matching page, still print its diff, and fail on any entry that matches no differing page ("unused allow-list entry"); status codes, records and sign-in are never allow-listed. Tests first in `UTESTS/Release/VisibleChangeAllowListTests.cs` (link the new file into `UTESTS` like T017): match rules, unused entry, status change not excused, malformed file. `ReleaseUpgradeTests` (T027) passes the loaded list to steps 3 and 6. Depends on T022.
- [X] T088 [US1] Create `docker-compose.gate.yml` limiting `app` to `cpus: 2` and `mem_limit: 4g` (the reference shape of constitution Principle VIII, analysis U5), with a comment that it is for the release gates only; make `STESTS/ComposeDeployment.cs` layer it when `QB_GATE=1`, and set `QB_GATE=1` in `ReleaseUpgradeTests` (T027) and in the release `smoke` job (T040).

**Checkpoint**: an image built from this branch upgrades a database written by the 5f549ae
build with every kind of data intact, and the run produces a snapshot bundle.

---

## Phase 4: User Story 2 - A release cannot ship unless it upgrades and boots (Priority: P1)

**Goal**: a version tag builds one multi-arch image, publishes it by digest, runs the smoke,
arm64 and upgrade gates on that digest, and waits for approval; approval tags the tested
digest; shipped migrations and patch rules are enforced.

**Independent Test**: quickstart §3 with `v0.1.0-rc.1`, and the first three negative runs
of quickstart §4.

### Tests for User Story 2 ⚠️

- [X] T029 [P] [US2] Create `UTESTS/Release/ReleaseVersionsTests.cs` for `version check`: tag equals `VersionPrefix` (with and without `-rc.N`) passes and prints `kind` and `previous`; a different number fails; a tag at or below an available version fails; kinds major/minor/patch/prerelease for 0.x and 1.x (0.x minors are minors, FR-001a); a new major whose `QbUpgradeFloor` is not the last minor of the previous major fails.
- [X] T030 [P] [US2] Create `UTESTS/Release/UpgradeSourcesTests.cs` (RL-09): empty list → `["baseline"]`; `0.4.0` with `0.1.0 0.2.0 0.3.1` → `["0.3.1","0.1.0"]`; `1.0.0` with `0.1.0 … 0.7.2` → `["0.7.2"]` (previous and last minor collapse); `1.3.0` with `1.0.0 1.2.0` → `["1.2.0","1.0.0"]`; `2.0.0` → previous, `2.0.0`'s own major has no first release yet, last minor of 1; pre-releases never appear as sources.
- [X] T031 [P] [US2] Create `UTESTS/Release/MigrationFreezeTests.cs` against temporary git repositories made in the test: a modified, renamed or deleted `<ts>_<Name>.cs` or `.Designer.cs` under any `Infrastructure/Migrations/` (and `SHARED/Migrations/Platform/Migrations/`) since the ref fails naming it; added migrations and changed `*ModelSnapshot.cs` pass; no ref → pass with a notice (FR-017, SC-008).
- [X] T032 [P] [US2] Create `UTESTS/Release/PatchRulesTests.cs` (FR-034a): since `vX.Y.0`, an added migration, an added `CONTRACTS/**/v*.schema.json`, or a changed `CurrentVersion` in `PAGESC/PageSchema.cs`, `PAGESC/MenuSchema.cs`, `src/Modules/Editor/QuickBuilder.Editor.Contracts/DesignTokens.cs`, `src/Modules/Cms/QuickBuilder.Cms.Contracts/CollectionSchema.cs` or the binding version constant fails, naming each; anything else passes.

### Implementation for User Story 2

- [X] T033 [US2] Create `TOOL/Git.cs` (run `git` with arguments, capture output, fail with its stderr) and `TOOL/ReleaseVersions.cs`, and wire `version check --tag --available` in `TOOL/Program.cs` with the exit codes of the conventions. Make T029 pass.
- [X] T034 [P] [US2] Create `TOOL/UpgradeSources.cs` and the `upgrade-sources --version --available` command printing a JSON array. Make T030 pass.
- [X] T035 [P] [US2] Create `TOOL/MigrationFreeze.cs` and the `migrations check --since` command. Make T031 pass.
- [X] T036 [P] [US2] Create `TOOL/PatchRules.cs` and the `patch check --since` command. Make T032 pass.
- [X] T037 [US2] Add the `release-checks` job to `WF/ci.yml` (release-pipeline.md §6): checkout with `fetch-depth: 0`; find the latest available release with `gh release list --exclude-drafts --exclude-pre-releases --limit 1` (`GH_TOKEN: ${{ github.token }}`); run `dotnet run --project tools/QuickBuilder.Release -- migrations check --since <tag>` or print a notice when there is none.
- [X] T038 [US2] Create `WF/release.yml` with the trigger, permissions and concurrency of release-pipeline.md §1 and the `prepare` job: checkout full history; list available versions; `version check`; `migrations check` and, for patches, `patch check` since the previous release (or `pre-0.1.0-baseline`); `upgrade-sources` → job output `sources`. (The notes step is added by T076.)
- [X] T039 [US2] Add to `WF/release.yml` the `build` matrix (amd64 on `ubuntu-latest`, arm64 on `ubuntu-24.04-arm`; `docker/setup-buildx-action`, `docker/login-action` to `ghcr.io` with `GITHUB_TOKEN`; `docker/build-push-action` with `platforms: linux/<arch>`, build args `QB_VERSION_SUFFIX=` (empty, or the `-rc.N` label), `QB_VERSION`, `QB_REVISION`, outputs `type=image,name=ghcr.io/trungnd1985/quickbuilder,push-by-digest=true,name-canonical=true,push=true`; digest uploaded as an artifact) and the `manifest` job (`docker buildx imagetools create -t ghcr.io/trungnd1985/quickbuilder:sha-<12>` from both digests; output `index_digest`) (RL-03).
- [X] T040 [US2] Add to `WF/release.yml` the `smoke` job (`QB_SMOKE_IMAGE=ghcr.io/trungnd1985/quickbuilder@<index_digest>`, `QB_SMOKE_REQUIRE_DOCKER=1`, `dotnet test tests/QuickBuilder.SmokeTests --filter Category=Smoke`, compose logs uploaded on failure) and the `arm64-boot` job on `ubuntu-24.04-arm` (compose up with the candidate, wait for `/health/ready` up to 3 minutes, assert `GET /api/v1/identity/status` reports setup mode, upload logs) (FR-007, FR-010, RL-11).
- [X] T041 [US2] Add to `WF/release.yml` the `upgrade` job, one matrix leg per `prepare.outputs.sources`: for a version, `gh release download v<source> -p 'quickbuilder-<source>-*'` into `QB_SNAPSHOT_DIR` and set `QB_PREVIOUS_IMAGE=ghcr.io/trungnd1985/quickbuilder:<source>`; for `baseline`, nothing to download; run `--filter Category=ReleaseUpgrade` with `QB_SMOKE_IMAGE`; the leg whose source is the previous release (or `baseline`) sets `QB_SNAPSHOT_OUT` and uploads the new bundle as artifact `snapshot`; logs uploaded on failure (FR-008, RL-09, RL-14). Depends on T027 and T004.
- [X] T042 [US2] Add to `WF/release.yml` the `candidate` job (`needs` every gate, `if: always()`): generate `docker-compose.yml` with `QUICKBUILDER_IMAGE` defaulting to `ghcr.io/trungnd1985/quickbuilder:<X.Y.Z>`; write `digests.txt`; create or update the **draft** release for the tag (`gh release create --draft` / `gh release edit`), attaching the snapshot bundle (when produced), the pinned compose file and `digests.txt`; when any gate failed, prefix the body with `Not available: <job> failed — <run URL>` (FR-009, RL-05). The body comes from T076's notes once that lands; until then a placeholder body with the digest.
- [X] T043 [US2] Add to `WF/release.yml` the `promote` job (`environment: release`, `needs` `candidate` and every gate, runs only when all succeeded): the steps of release-pipeline.md §4 — refuse when `X.Y.Z` exists on another digest (FR-005) and skip when it is the same; `imagetools create` the exact tag and, for a non-pre-release that is the highest available version, `X.Y`, `X` and `latest`; publish the draft (`gh release edit --draft=false`, `--prerelease` for `-rc`, `--latest` per the same rule) (FR-004, FR-009a, RL-06).
- [ ] T044 [US2] Run quickstart §3 with tag `v0.1.0-rc.1` and approve it; then the negative runs of quickstart §4 rows "Make the setup wizard return 500" and "A migration that deletes a page row" on a scratch branch with `-rc` tags. Record run links, the candidate digest, tag → candidate time (SC-011) and results in `specs/019-release-upgrade-safety/quickstart-results.md`; delete the scratch tags, images' extra tags and draft releases afterwards. After the first push, make the GHCR package public and check that `docker logout ghcr.io && docker pull ghcr.io/trungnd1985/quickbuilder:0.1.0-rc.1` succeeds without credentials (FR-003, analysis G1).

**Checkpoint**: a tag produces a tested, multi-arch candidate and, after approval, a
published pre-release; broken builds stop before approval.

---

## Phase 5: User Story 3 - Old content keeps rendering (Priority: P1)

**Goal**: every stored document passes through its contract's upgrade chain before it leaves
the owning module; old versions render and edit, saves and publishes store the current
version, too-new and failed documents fail one page safely.

**Independent Test**: `SchemaReadSiteTests` and the chain tests pass; with every contract at
version 1, the gates still render every snapshot page unchanged.

### Tests for User Story 3 ⚠️

- [X] T045 [P] [US3] Create `UTESTS/Schemas/SchemaUpgradeChainTests.cs` with a test-only contract at version 3 and steps 1→2 and 2→3: `Read` returns `Current`, `Upgraded` (both steps applied in order, `schemaVersion` set), `TooNew` and `Failed` (a step that throws); the input document is unchanged; the constructor refuses missing or duplicate steps (schema-upgrades.md §2). It must fail first.
- [X] T046 [P] [US3] Create `UTESTS/Schemas/SchemaUpgradeCoverageTests.cs` and `UTESTS/Schemas/SchemaUpgradeGoldenTests.cs` (schema-upgrades.md §4) over the chains of Pages (page schema, menu, binding), Editor (design tokens) and Cms (collection schema): pass today with every contract at 1; prove each rule fires with a temporary fixture folder in the test (a contract at 2 with no step; a step with no golden pair; a pair whose output differs).
  *Done as*: the golden comparison checks the step output against `*.out.json`; validating the output against `vN+1.schema.json` is left to the validators once a v2 exists.
- [X] T047 [P] [US3] Create `ITESTS/Schemas/SchemaReadSiteTests.cs`. **Too new**: store a page, a component and a menu at `CurrentVersion + 1` in `pages.published_versions`, a design in `editor.design_versions`, and an item's values document in `cms.items` (written by SQL, bypassing validators; none of these columns has a version check): that page answers as an unrenderable page while another page on the site renders; the design falls back to the default tokens; the admin read answers `409 document_too_new` and a save answers the same; each stored row is byte-for-byte unchanged. **Failed**: `pages.page_drafts`, `pages.component_drafts`, `pages.menu_drafts`, `editor.design_token_sets` and `cms.collections` hold `CHECK (schema_version = 1)`, so cover them with a test-registered chain whose step throws: the admin read answers `422 document_upgrade_failed` and the log entry `schema_upgrade_failed` contains contract, subject kind, id and version and no document text. Say why in the test's remarks. It must fail first.
- [X] T048 [P] [US3] Create `ATESTS/SchemaReadCoverageTests.cs`: each read-path type listed in schema-upgrades.md §3 (by name) depends on its module's chain type; the test lists the types so a renamed or new read path must be added deliberately.

### Implementation for User Story 3

- [X] T049 [US3] Create `SHARED/Schemas/ISchemaUpgradeStep.cs`, `SHARED/Schemas/SchemaUpgradeChain.cs` and `SHARED/Schemas/SchemaRead.cs` exactly as schema-upgrades.md §2. Make T045 pass. Depends on T002.
- [X] T050 [US3] Create `PAGES/Infrastructure/Schema/PageSchemaUpgrades.cs`, `MenuSchemaUpgrades.cs` and `BindingUpgrades.cs` (empty step lists, current from `PAGESC`), registered in `PAGES/PagesModule.cs` as replaceable singletons; add `document_too_new` (409) and `document_upgrade_failed` (422) to `PAGES/PagesErrors.cs` and `PagesErrorResults.cs`.
- [X] T051 [US3] Route every Pages read path of schema-upgrades.md §3 through the chains (page chain, then the binding chain on the result): `PAGES/Infrastructure/PublishedContentReader.cs`, `PageReads.cs`, `ComponentReads.cs`, the menu draft read in `MenuWrites.cs`, and the source-content reads in `PublishingWrites.cs`, which also stamp `PageSchema.CurrentVersion` / `MenuSchema.CurrentVersion` on the version they publish. `TooNew`/`Failed` become the existing unrenderable result for Rendering and the new errors for the admin, logged as the conventions say. Extend T047 with: after a republish and a rollback, earlier `published_versions` rows keep their stored `schema_version` (FR-023a, analysis G3).
  *Done as*: one Pages service, `PAGES/Infrastructure/Schema/StoredDocuments.cs`, makes the decision for every read site; a document that cannot be used is handed to Rendering **empty**, because the page renderer reads the version inside the document and an old document that failed to upgrade would still render in its old form.
- [X] T052 [P] [US3] Create `EDITORM/Infrastructure/DesignTokenUpgrades.cs`; route `EDITORM/Infrastructure/PublishedDesignReader.cs` and the design draft reads through it; add the two errors to the Editor module's error types. A `TooNew`/`Failed` published design falls back to the default tokens for rendering, as a missing publication does today, and logs.
- [X] T053 [P] [US3] Create `CMS/Infrastructure/Schema/CollectionSchemaUpgrades.cs`; change `CMS/Infrastructure/ItemReads.cs` `ReadValues` to accept `schemaVersion <= CurrentVersion` and upgrade through the chain; a newer version returns a distinct too-new result (not the "malformed" null), so the admin answers `409 document_too_new` and Rendering treats only that item as unrenderable (schema-upgrades.md §3 exceptions); route the collection schema reads through it; add the two errors to the Cms error types.
  *Done as*: `ItemReads.ReadValues` turned out to check request input, not stored values, so it is unchanged. The Cms read sites are `PublishedItemReader` (schemas and item values; an unusable one is left out), `CollectionSchemaReader`, and the admin `CollectionReads.ReadAsync` / `ItemReads.ReadAsync`, which keep the stored JSON as it is unless it was actually upgraded.
- [X] T054 [US3] Set `RENDER/Rendering/PageRenderer.cs` and `RENDER/Rendering/Navigation/MenuModel.cs` `SupportedSchemaVersions` to `{ current }` with a remark that owning modules upgrade before handing documents over (schema-upgrades.md §3); keep `RendererSchemaVersionsTests` and `BlockRendererCoverageTests` passing. Make T046–T048 pass.
- [X] T055 [US3] Update `CONTRACTS/page-schema.md` §6 (item 3 becomes "an upgrade step from N-1 with golden files, applied on read and stored on the next save or publish"; link schema-upgrades), the matching versioning paragraph in `CONTRACTS/binding-expression.md`, `CONTRACTS/README.md` (the `upgrades/` folders), `specs/012-publishing-rendering/contracts/public-rendering.md` (Rendering receives current versions only), and add "Raising a schema version" to `docs/contributing.md` from schema-upgrades.md §5.

**Checkpoint**: every read path upgrades; nothing reads differently today because every
contract is at version 1; the next breaking change has a tested place to go.

---

## Phase 6: User Story 4 - Startup refuses unsafe version combinations (Priority: P2)

**Goal**: a newer-minor or too-old database is refused before anything touches it, with a
clear message in the logs, `Incompatible` on readiness and no restart loop; Administrators
see the running version.

**Independent Test**: quickstart §2.

### Tests for User Story 4 ⚠️

- [X] T056 [P] [US4] Create `UTESTS/Versioning/InstallationGateTests.cs`: every row of data-model §2 with its writes, including `-dev` and `-rc` versions, `F` above `0.0.0`, rule 5 before rule 7, an `M` older than `V`, and "1.4.2 on 1.4.3"; the refusal messages of data-model §2 contain both versions in the log text and none in the anonymous detail. The refusal rows must fail first; the passing rows already pass after T012.
- [X] T057 [P] [US4] Extend `ITESTS/Versioning/InstallationStartupTests.cs` with refusals: a database recorded at a newer minor; one with `upgrading_to` at a newer minor; one recorded below a raised floor; a pre-release database with a floor above `0.0.0`. Each: readiness `503` with `state: "Incompatible"`, one Critical log line, no module migration attempted, zero writes in every schema; and a patch-down case that starts. It must fail first.
  *Done as*: rules 3 and 7 depend on the build's floor, which an integration test cannot lower; `InstallationGateTests` covers them. Zero writes are checked by a fingerprint of every row with its `xmin`, not `pg_stat_user_tables`, which PostgreSQL 15+ flushes late.
- [X] T058 [P] [US4] Create `ITESTS/Versioning/RefusalHostTests.cs`: `/health/live` 200, `/health/ready` 503 with the report shape and no version, any other path 503 plain text with `Cache-Control: no-store`; under `Hosting:BasePath`; no `IHostedService` from any module is registered (installation-version.md §3). It must fail first.
- [X] T059 [P] [US4] Create `ITESTS/Versioning/MigrateCommandTests.cs`: the `migrate` command exits 3 on refusal with the Critical line, 0 on success, and 0 with zero writes when run a second time. It must fail first.
- [X] T060 [P] [US4] Create `ITESTS/Identity/InstallationEndpointTests.cs`: `GET /api/v1/identity/installation` answers 200 with `version`, `revision`, `databaseVersion`, `upgradingTo` and `releaseNotesUrl` (null for `-dev`) for an Administrator, 403 for Designer and Editor, 401 anonymous (installation-version.md §4). It must fail first.
- [X] T061 [P] [US4] Create `ADMINTESTS/features/settings/AboutInstallationPanel.spec.ts`: shows version, revision and database version; the release-notes link only when the URL is present; the "upgrade in progress" line when `upgradingTo` is set; hidden from non-Administrators; axe passes. It must fail first.
  *Done as*: in `ADMINTESTS/settings/AboutInstallation.spec.ts`, beside the other settings specs.

### Implementation for User Story 4

- [X] T062 [US4] Complete `SHARED/Migrations/Platform/InstallationGate.cs` with the refusal rows (3, 4, 6, 7) and the messages of data-model §2 (log text with versions and the operations-guide anchors `#rolling-back` and `#upgrade-path`; anonymous detail without); add `StartupPhase.Incompatible` to `SHARED/Startup/StartupState.cs`. Make T056 pass.
- [X] T063 [US4] In `SHARED/Migrations/ModuleMigrationRunner.cs`, on a refusal: log the Critical line once, set `Incompatible`, and throw `InstallationIncompatibleException` (new, `SHARED/Migrations/Platform/`) carrying the anonymous detail; in `HOST/Startup/StartupTasks.cs`, `MigrateCommand` returns 3 for it.
- [X] T064 [US4] Create `HOST/Startup/RefusalHost.cs` (a minimal `WebApplication` on the same URLs and base path with the three responses of installation-version.md §3, no modules, built from terminal middleware (`app.Run`) only — no `Map*` endpoints, so no Minimal API is introduced) and, in `HOST/Program.cs`, catch `InstallationIncompatibleException` from `RunMigrationsAsync` and run the refusal host instead of building the pipeline. Make T057–T059 pass.
  *Done as*: not a separate host: the check also runs before the host is built (`InstallationPreflight`), QuickBuilder's own hosted services are removed from the services, and the same app gets one terminal middleware (`HOST/Startup/RefusalHost.cs`). A separate host would not be the app `WebApplicationFactory` starts. ASP.NET Core's self-inserted authentication and authorization middleware is switched off for that pipeline.
- [X] T065 [US4] Create `SHARED/Versioning/IInstallationVersion.cs` and `SHARED/Versioning/InstallationVersion.cs` (reads the row through `PlatformDbContext`; `DatabaseVersion` null when absent or `0.0.0`); register it.
- [X] T066 [US4] Create `IDENTITY/Features/GetInstallation/GetInstallation.cs` (controller at `api/v1/identity/installation`, query, handler using `IInstallationVersion` and `ApplicationVersion`; `releaseNotesUrl` = `https://github.com/trungnd1985/quick-builder/releases/tag/v<version>` unless pre-release `dev`); register capability `identity.installation.view` (Installation scope, Administrator only) in `IDENTITY/IdentityModule.cs`; add it to `specs/002-identity-setup/contracts/authorization.md` §2 under "Registered by 019"; list the endpoint in `ITESTS/Infrastructure/EndpointCatalog.cs`. Make T060 pass.
  *Done as*: the endpoint catalogue reads endpoints from the running app, so nothing is listed by hand.
- [X] T067 [P] [US4] Create `ADMIN/api/installation.ts` and `ADMIN/features/settings/AboutInstallationPanel.vue`, add it to the settings route for Administrators with i18n keys in `ADMIN/i18n/en.json`. Make T061 pass.
- [X] T068 [P] [US4] Add state `Incompatible` with its meaning to the health schema in `specs/001-foundation-skeleton/contracts/admin-api.yaml`.
- [X] T069 [US4] Add snapshot.md §4 step 9 to `STESTS/Release/ReleaseUpgradeTests.cs`: start `QB_PREVIOUS_IMAGE` on the upgraded database; for a different major.minor assert readiness `Incompatible` within 30 s, the Critical line naming both versions, and unchanged write counters (SC-006); for the same major.minor (a patch) assert it becomes ready; in `baseline` mode assert the baseline image starts (it predates the check).

**Checkpoint**: unsafe combinations are refused without touching data; the version is visible
to Administrators and in the first log line.

---

## Phase 7: User Story 5 - Operators know what changes and how to go back (Priority: P2)

**Goal**: every release has assembled release and upgrade notes with every section; PRs that
change operator-visible things carry a fragment; the operations guide has the upgrade and
rollback procedure, and the pipeline drills the rollback.

**Independent Test**: `qb-release notes --version 0.1.0 --previous pre-0.1.0-baseline`
produces complete notes; quickstart §5 by hand; the rollback step of the upgrade gate passes.

### Tests for User Story 5 ⚠️

- [X] T070 [P] [US5] Create `UTESTS/Release/FragmentTests.cs` (release-notes.md §2): valid fragments parse; unknown keys, block lists, a missing required key, a body with a heading, more than five sentences, or a file name whose prefix differs from `issue` fail with file and line. It must fail first.
- [X] T071 [P] [US5] Create `UTESTS/Release/NotesAssemblerTests.cs` against a temporary git repository: every section of release-notes.md §1 present in order with "None." when empty; derived "Database migrations" (per module, names added since the previous ref) and "Content schema versions"; each failing rule of release-notes.md §3 (malformed fragment, breaking outside a new major — 0.x minors included, configuration keys not matching `docs/configuration.md`'s tables, migrations with no fragment, patch rules, template missing a section). It must fail first.
- [X] T072 [P] [US5] Create `UTESTS/Release/FragmentCheckTests.cs` (release-notes.md §4): a change adding a migration, editing `docs/configuration.md`, a contract file other than examples, or `docker-compose.yml` without a fragment fails; with one passes; `archive` moves fragments to `RELEASES/<V>/` and bumps `VersionPrefix` to the next minor. It must fail first.

### Implementation for User Story 5

- [X] T073 [P] [US5] Create `RELEASES/TEMPLATE.md` exactly as release-notes.md §1 and `RELEASES/unreleased/.gitkeep`.
- [X] T074 [US5] Create `TOOL/Fragment.cs`, `TOOL/ConfigurationKeys.cs` (keys from the tables of `docs/configuration.md` at a git ref) and `TOOL/NotesAssembler.cs`, and the `notes --version --previous --out` command. Make T070 and T071 pass.
- [X] T075 [US5] Add the `fragment check --base` and `archive --version` commands to `TOOL/Program.cs` (with `TOOL/FragmentCheck.cs`). Make T072 pass.
- [X] T076 [US5] Wire notes into the pipeline: `WF/ci.yml` `release-checks` runs `fragment check --base origin/${{ github.base_ref }}` on pull requests; `WF/release.yml` `prepare` runs `notes` (previous = the previous release or `pre-0.1.0-baseline`) and uploads `notes.md`; `candidate` uses it as the draft body with `{{CANDIDATE}}` replaced by the digest reference; `promote` replaces it with the final `X.Y.Z` references before publishing (release-pipeline.md §4 step 3).
- [X] T077 [US5] Write the 0.1.0 fragments in `RELEASES/unreleased/`: one per merged feature (001, 002, 003, 009, 010, 011, 012, 013, 014, 015, 016, 017, 018, 024, 035) from each plan's "Operator impact" and the "Upgrading from feature 0NN" sections of `docs/operations.md`, and one for 019 from plan.md's upgrade notes; each with its configuration keys so `notes` passes the configuration check. Run `qb-release notes --version 0.1.0 --previous pre-0.1.0-baseline` and review the output.
- [X] T078 [US5] Add snapshot.md §4 step 10 (rollback drill) to `STESTS/Release/ReleaseUpgradeTests.cs`: restore the source bundle with `SnapshotBundle.RestoreAsync`, start `QB_PREVIOUS_IMAGE` (the baseline image in `baseline` mode), wait ready, compare against the source expectations; assert the restore-to-ready time is at most 15 minutes and print it (SC-010) (FR-036, RL-18).
- [X] T079 [US5] Add the chapter **Upgrading and rolling back** to `docs/operations.md` (RL-21; anchors `#upgrade-path` and `#rolling-back` used by T062's messages): before (read every release's notes between yours and the target; back up with `docker compose exec db pg_dump --format=custom …` and the media volume with the commands snapshot.md §1 uses), upgrade (change `QUICKBUILDER_IMAGE` to the exact version, `docker compose up -d`, watch readiness and the version), after (background work, caches), the supported upgrade path and floors, what `Incompatible` means and what to do, rollback (stop, restore both, start the previous exact version, check), and "without the backup there is no rollback; data written since the upgrade is lost". Mark the existing "Upgrading from feature 0NN" sections as history of pre-release builds.
- [X] T080 [P] [US5] Add to `docs/contributing.md`: "Making a release" (release-pipeline.md §7), "Release-note fragments" (release-notes.md §2 and §4), and "Adding stored data" (add a step to `STESTS/Release/ContentJourney.cs` and a row to data-model §4's list, kept in the contributing guide from now on).
- [X] T081 [P] [US5] Update `README.md`: install from the `docker-compose.yml` attached to a release (exact version), upgrade by changing that version with a link to the operations chapter, and build from source with `-f docker-compose.build.yml`. Until `v0.1.0` is approved, the install section says to use the build override (no published image exists yet; plan Risks, analysis C4).

*Added after analysis (2026-10-23)*:

- [X] T089 [US5] Add the "Visible changes" section to `RELEASES/TEMPLATE.md` and `TOOL/NotesAssembler.cs` (every `*-visible-changes.json` entry: reason and pages, "None." when empty) with assembly rule 7 of release-notes.md §3 (a file that does not parse, or has no `reason` or `pages`, fails), in `TOOL/VisibleChanges.cs`; make `archive` move these files with the fragments. Tests first in `UTESTS/Release/NotesAssemblerTests.cs` and `FragmentCheckTests.cs`. Depends on T074, T075.

**Checkpoint**: notes are complete for 0.1.0; the guide's procedure is the one the gate runs.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T082 Run coverage and confirm at least 80 % line coverage on `SHARED/Migrations/` (runner and `Platform/`), `SHARED/Versioning/`, `SHARED/Schemas/` and `HOST/Startup/RefusalHost.cs`; add tests where it falls short. Shared.Infrastructure and Host are already in `WF/ci.yml`'s per-package gate.
- [X] T083 [P] Update `docs/backlog.md` with the deferred items of plan.md (signing and SBOM, a second registry, bulk rewrite, the visible-change allow-list, rolling upgrades, automatic downgrade).
- [X] T084 Removed: resolved during analysis (finding I1); spec.md FR-030 now matches research RL-08. Nothing to do.
- [X] T085 Run quickstart.md §1–§6 end to end, including the remaining negative runs of §4 and the rollback by hand of §5; record results, times (SC-001, SC-006, SC-010, SC-011) and deviations in `specs/019-release-upgrade-safety/quickstart-results.md`.
  *Done as*: §1, §2, §5 and §6 ran (quickstart-results.md); §3 and §4 need GitHub and wait for T004 and T044.
- [X] T086 Open the pull request for branch `019-release-upgrade-safety`, closing #19, listing the constitution clarification and its approval, the manual steps of T004, the operator impact and upgrade notes from plan.md, and the plan for cutting `v0.1.0` immediately after merge (release-pipeline.md §7): until it is approved, the reference compose names an image that does not exist yet (plan Risks).
  *Done as*: https://github.com/trungnd1985/quick-builder/pull/41 (2026-10-24).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 (the constitution clarification) blocks T010 and
  T049. T004 (baseline tag, environment) is needed by T041, T044 and T028's baseline build.
- **Foundational (Phase 2)**: depends on Setup; blocks every story.
- **US1 (Phase 3)**: depends on Phase 2.
- **US2 (Phase 4)**: tool and CI tasks (T029–T037) depend only on Phase 2; the workflow's
  `upgrade` job (T041) depends on US1's `ReleaseUpgradeTests` (T027). T044 should run after
  US4 (T062–T064), so no image without the startup check is ever approved.
- **US3 (Phase 5)**: depends on Phase 2 and T002; independent of US1, US2 and US4.
- **US4 (Phase 6)**: depends on Phase 2 (T012's gate and record). T069 depends on T027.
- **US5 (Phase 7)**: T070–T075 and T079–T081 depend only on Phase 2; T076 depends on T038,
  T042 and T043; T078 depends on T027.
- **Polish (Phase 8)**: after the stories it covers.
- **Added after analysis**: T087 runs after T022 and before T028; T088 before T028 and T040;
  T089 after T074 and T075, before T077.

### Within Each User Story

- Tests first, failing; then shared infrastructure; then module wiring; then endpoints and
  admin; then workflows and documentation.
- `STESTS/Release/` files build on each other: T022 → T023, T024 → T025, T026 → T027.
- `WF/release.yml` is edited by T038, T039, T040, T041, T042, T043 and T076 in that order;
  never in parallel.

### Parallel Opportunities

- Phase 2: T005 with T009 and T011; T013 beside T012; T014 beside the versioning tasks.
- US1: T017–T021 together; T024 beside T023.
- US2: T029–T032 together; T034, T035, T036 together after T033.
- US3: T045–T048 together; T052 and T053 beside T051.
- US4: T056–T061 together; T067 and T068 beside T066.
- US5: T070–T073 together; T080 and T081 beside T079.
- Across stories after Phase 2: US3 and US4 can proceed beside US1.

---

## Parallel Example: Foundational

```text
Task: "T005 Create UTESTS/Versioning/ApplicationVersionTests.cs"
Task: "T009 Link SemanticVersion.cs into TOOL and add TOOL/BuildProps.cs"
Task: "T011 Create ITESTS/Versioning/InstallationStartupTests.cs (passing cases)"
Task: "T014 Change docker-compose.yml and add docker-compose.build.yml"
```

## Parallel Example: User Story 4

```text
Task: "T056 Create UTESTS/Versioning/InstallationGateTests.cs"
Task: "T057 Extend ITESTS/Versioning/InstallationStartupTests.cs with refusals"
Task: "T058 Create ITESTS/Versioning/RefusalHostTests.cs"
Task: "T059 Create ITESTS/Versioning/MigrateCommandTests.cs"
Task: "T060 Create ITESTS/Identity/InstallationEndpointTests.cs"
Task: "T061 Create ADMINTESTS/features/settings/AboutInstallationPanel.spec.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phases 1 and 2.
2. US1: an image from this branch upgrades a database written by the 5f549ae build with
   everything intact, and produces the first snapshot bundle (demoable locally, nothing
   published).
3. **Stop and validate** with T028.

### Incremental Delivery

1. MVP (US1).
2. US3 and US4 in parallel: old content has its upgrade path; unsafe starts are refused.
   From here a published image is safe to run.
3. US2: the pipeline, the gates and approval; first `v0.1.0-rc.1` (T044).
4. US5: notes, fragments, the guide and the rollback drill; wire notes into the pipeline.
5. Polish, then the PR. After merge, the maintainer cuts `v0.1.0` (release-pipeline.md §7).

If the PR must be split (plan.md, Principle XI): (1) Phases 1–2 and US4; (2) US3; (3) the
tool, fragments and CI checks (T029–T037, T070–T077); (4) US1's gate code and the release
workflow (the rest of US1, US2, T076, T078). Each is safe alone; (4) needs (1)–(3).

---

## Notes

- [P] tasks touch different files and depend on no incomplete task.
- Verify each test fails before implementing; commit after each task or logical group,
  referencing #19.
- Never push a `vX.Y.Z` tag without `-rc` while working on this branch: only T044's `-rc`
  tags, and the maintainer's `v0.1.0` after merge.
