---

description: "Task list for 026-operator-documentation"
---

# Tasks: Operator Documentation

**Input**: Design documents from `/specs/026-operator-documentation/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires tests for all new behaviour, and integration tests use
  WebApplicationFactory + Testcontainers PostgreSQL.
- The spec's success criteria SC-002 to SC-008 are measured by automated checks (plan.md,
  Testing; quickstart Q1–Q8).
- Within each story, test tasks come first and must fail before implementation starts.

**Changed after analysis (2026-11-23)**:
- Added T095 (US9, publishing precondition), T096 (US5, deprecated settings in release
  notes) and T097 (US7, startup messages name their key). They keep their numbers but sit in
  their phases; nothing was renumbered.
- Changed T029 (sixth "Allowed values" column), T035 (`SitesOptions` removed), T042 (real
  storage emulators and command services), T043 (`SitesOptions` example) and T044 (computed
  defaults asserted).
- In the spec: the publishing precondition assumption, Out of Scope exceptions for doc links
  and key-naming wording, and SC-007 aligned with FR-031. Plan Principle XI row, OD-10,
  data-model §3 and setting-attributes.md §4 were updated to match.

**Organization**: tasks are grouped by user story (US1–US9 from spec.md), so each story can
be built and checked on its own.
- US1–US5 are P1; US6–US9 are P2.
- The tooling that several stories share is built in Phase 2:
  - the `qb-docs` tool;
  - the setting attributes;
  - the site skeleton;
  - the lint framework;
  - the snippet runner.
- US2 (reference) and US3 (gate) are tightly linked. US2 produces the reference and US3
  makes it impossible to break.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US9)

## Path Conventions

| What | Where |
|---|---|
| Setting attributes | `src/Shared/QuickBuilder.Shared.Kernel/Settings/` |
| Options classes | `src/QuickBuilder.Host/Configuration/`, `src/Shared/QuickBuilder.Shared.Infrastructure/**`, `src/Modules/<Module>/QuickBuilder.<Module>/` |
| Docs tool | `tools/QuickBuilder.Docs/` (`qb-docs`) |
| Release tool | `tools/QuickBuilder.Release/` (`qb-release`, 019) |
| Operator site | `docs/operators/` |
| Unit tests | `tests/QuickBuilder.UnitTests/Docs/`, `tests/QuickBuilder.UnitTests/Release/` |
| Integration tests | `tests/QuickBuilder.IntegrationTests/Configuration/` |
| Procedure tests | `tests/QuickBuilder.SmokeTests/Procedures/` (`Category=Procedures`) |
| Workflows | `.github/workflows/` |

Research decisions are cited as `OD-NN` ([research.md](./research.md)).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: create the new projects and the site so every later task has a place to land.

- [X] T001 Create console project `tools/QuickBuilder.Docs/QuickBuilder.Docs.csproj` (assembly name `qb-docs`, `ProjectReference` to `src/QuickBuilder.Host/QuickBuilder.Host.csproj` and `tools/QuickBuilder.Release/QuickBuilder.Release.csproj`), add it to `QuickBuilder.slnx`, and add `Program.cs` with a command dispatcher printing usage and returning exit code 2 for unknown commands (contracts/qb-docs-cli.md: exit codes 0/1/2, one finding per line `<where>: <problem> — <fix>`).
- [X] T002 [P] Reference `tools/QuickBuilder.Docs` from `tests/QuickBuilder.UnitTests/QuickBuilder.UnitTests.csproj` (follow the existing `QuickBuilder.Release` reference) and add `InternalsVisibleTo("QuickBuilder.UnitTests")` in `tools/QuickBuilder.Docs/QuickBuilder.Docs.csproj`.
- [X] T003 [P] Create the VitePress project in `docs/operators/`:
  - `package.json` with scripts `dev`, `build` and `preview`, and `vitepress` pinned to an exact 1.x version (OD-07);
  - a committed `package-lock.json` from `npm install`;
  - `.gitignore` covering `node_modules/`, `.vitepress/dist/` and `.vitepress/cache/`.
- [X] T004 [P] Add `QbDocsBaseUrl` (`https://trungnd1985.github.io/quick-builder/`) next to `QbUpgradeFloor` in `Directory.Build.props`, and write it into the Host's assembly metadata the same way `QbUpgradeFloor` is written, in `src/QuickBuilder.Host/QuickBuilder.Host.csproj` (OD-11).
- [X] T005 [P] Add a `THIRD-PARTY-NOTICES.md` entry for the VitePress default theme assets served by the published site, covering name, MIT licence and source URL (OD-07, Principle XI).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the attribute vocabulary, the catalog, the site skeleton, the lint framework
and the snippet runner. Every user story uses at least one of them.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### Tests for the foundation

- [X] T006 [P] Write catalog rule tests in `tests/QuickBuilder.UnitTests/Docs/CatalogRulesTests.cs`.
  - Use small fixture options classes defined in the test file.
  - Cover rules 1–7 of contracts/setting-attributes.md §3:
    - section key ≠ `SectionName`;
    - a property without an attribute;
    - an empty description, a description without a trailing `.`, and a description containing `FR-12`;
    - `[RegularExpression]` without `Format`;
    - an unparseable `AppliesWhen`;
    - a `SecretAccessKey` without `Secret=true`;
    - `KeyPrefix` not treated as a secret;
    - duplicate keys.
  - Each failure produces the documented one-line message.
- [X] T007 [P] Write catalog derivation tests in `tests/QuickBuilder.UnitTests/Docs/CatalogDerivationTests.cs` (data-model §1).
  - Type mapping: bool, int, decimal, TimeSpan, string, enum, `IList<string>` → `:<n>` and `__0`, `IDictionary<string,…>` → `:<name>`. An unknown type fails.
  - Defaults come from binding a fixture `appsettings.json` over the initialisers (OD-02). The bound value wins when they differ.
  - `DefaultText` overrides the bound default.
  - `[Range]`, `[AllowedValues]`, `[Length]` and enum names become allowed values.
  - `[SettingsGroup].AppliesWhen` is inherited.
  - `[Required]` sets the required flag.
- [X] T008 [P] Write `tests/QuickBuilder.UnitTests/Docs/SnippetMarkerTests.cs` (data-model §6, contracts/runnable-snippets.md §1–2).
  - Marker parsing for `qb:run` and `qb:file`.
  - Steps are ordered by `step=`.
  - Lint errors: a marker not followed by a fenced block, a duplicate step, an unknown procedure, an unknown substitution, a `file` marker without `path`, a `path` containing `..`, and a procedure without a `run` step.
  - `snippets write` produces `run.sh` starting with `set -euo pipefail`, writes `file` blocks verbatim, applies supplied substitutions, and fails when a required substitution is not supplied.
- [X] T009 [P] Write the lint framework tests in `tests/QuickBuilder.UnitTests/Docs/LintFrameworkTests.cs`.
  - Checks run over a fixture `docs/operators` tree in a temp directory.
  - Findings are sorted and printed one per line.
  - Exit code is 1 when there are findings and 0 when there are none.
  - Unknown `--check` names exit 2.

### Implementation of the foundation

- [X] T010 Add the five attributes exactly as in contracts/setting-attributes.md §1 to `src/Shared/QuickBuilder.Shared.Kernel/Settings/SettingAttributes.cs` (`SettingsSectionAttribute`, `SettingAttribute`, `SettingsGroupAttribute`, `NotASettingAttribute`, `DeprecatedSettingAttribute`). Give each an XML summary and no behaviour.
- [X] T011 Implement the catalog model (`SettingsSection`, `SettingDefinition`, `ExternalSetting`, `Exclusion` with fields per data-model §1) in `tools/QuickBuilder.Docs/Settings/CatalogModel.cs`, and the area list in fixed order (data-model §2) in `tools/QuickBuilder.Docs/Settings/Areas.cs`.
- [X] T012 Implement `tools/QuickBuilder.Docs/Settings/CatalogBuilder.cs` (makes T006 and T007 pass).
  - Reflect over every loaded `QuickBuilder.*` assembly for `[SettingsSection]` and walk properties recursively through `[SettingsGroup]`.
  - Apply rules 1–7 of contracts/setting-attributes.md §3.
  - Derive types and allowed values.
  - Add `ExternalSettings` (T014) to the catalog.
- [X] T013 Implement `tools/QuickBuilder.Docs/Settings/DefaultsBinder.cs` (OD-02).
  - Load `src/QuickBuilder.Host/appsettings.json` only, never `appsettings.Development.json`.
  - For each section, bind onto a fresh instance with `ConfigurationBinder`, as `AddOptions<T>().Bind()` does, and read the property values.
  - Render values in invariant culture: TimeSpan `hh:mm:ss`, bool lower-case, empty → `*(empty)*`, null with Required → `*(none)*`.
- [X] T014 [P] Create `src/QuickBuilder.Host/Configuration/ExternalSettings.cs` with a static, read-only list of `ExternalSetting` entries for `Serilog:MinimumLevel:Default` and `Serilog:MinimumLevel:Override:<Namespace>`.
  - Each entry has an operator description, type and area `logging`.
  - Copy the descriptions from the Logging table of `docs/configuration.md`.
- [X] T015 Implement `tools/QuickBuilder.Docs/Settings/FrameworkAllowList.cs` with the entries and a reason for each, as in contracts/setting-attributes.md §5.
- [X] T016 Implement `tools/QuickBuilder.Docs/Lint/LintRunner.cs` and the `ILintCheck` interface (name, `Run(context) → findings`), plus the `lint [--check <name>]` command in `tools/QuickBuilder.Docs/Program.cs` (makes T009 pass).
- [X] T017 Implement `tools/QuickBuilder.Docs/Snippets/MarkerParser.cs`, `Snippets/ProcedureWriter.cs` and `Snippets/Substitutions.cs`.
  - The closed substitution list is `release-compose`, `image`, `test-certificates`, `host-names` and `secret` (contracts/runnable-snippets.md §2).
  - Add the commands `snippets list` and `snippets write`, and the lint check `snippet-markers`.
  - Makes T008 pass.
- [X] T018 Create the site skeleton in `docs/operators/.vitepress/config.ts`.
  - Title "QuickBuilder operator documentation", `cleanUrls: true`, `search: { provider: 'local' }`.
  - No `ignoreDeadLinks`, no external fonts or analytics.
  - `base` from env `QB_DOCS_BASE` (default `/`).
  - A sidebar with one group per task: Install, Configure, Reverse proxy and TLS, Back up and restore, Upgrade, Troubleshooting, Security checklist, Operations, Reference.
  - Create a placeholder page for each sidebar entry, each with a one-line "Written in tasks Txxx" note, so the build passes from now on.
- [X] T019 Create `docs/operators/index.md`, the start page.
  - One short paragraph saying who the docs are for.
  - One link per task: install, configure, reverse-proxy, backup-restore, upgrade, troubleshooting, security-checklist, reference/configuration (FR-001, SC-009).
  - A note that the same pages are readable in the repository and in the release's source download (FR-002, FR-041).
- [X] T020 [P] Implement the lint checks `nav-pages` and `start-page` (contracts/qb-docs-cli.md) in `tools/QuickBuilder.Docs/Lint/NavPagesCheck.cs` and `Lint/StartPageCheck.cs`. Read the sidebar by parsing `config.ts` for `link:` entries. Add tests to `tests/QuickBuilder.UnitTests/Docs/NavAndStartPageTests.cs`.
- [X] T021 Add job `docs` to `.github/workflows/ci.yml`. It runs on every PR and push.
  - Steps: setup-dotnet 10, setup-node 24 with npm cache on `docs/operators/package-lock.json`, `dotnet build tools/QuickBuilder.Docs -c Release`, `qb-docs lint`, `npm ci && npm run build` in `docs/operators` with `QB_DOCS_BASE=/quick-builder/dev/`.
  - Then fail if the built HTML contains `<script src="http` or `<link…href="http` (contracts/docs-publishing.md §1).
- [X] T022 Add an "Operator docs" section to `docs/contributing.md`:
  - where pages live;
  - how to regenerate the reference (`qb-docs settings generate`) and the upgrade notes;
  - the `qb:run` / `qb:file` markers and their substitutions;
  - the lint checks;
  - how to preview locally (`npm run dev`).

**Checkpoint**: `qb-docs lint` passes, the site builds in CI with placeholder pages, and the
catalog builds from attributes on test fixtures.

---

## Phase 3: User Story 1 — An operator installs QuickBuilder from the documentation alone (Priority: P1) 🎯 MVP

**Goal**: a stranger goes from a clean server to a signed-in administrator by following
`install.md`, and the pipeline runs the same commands.

**Independent Test**: `QB_SMOKE_IMAGE=<image> dotnet test tests/QuickBuilder.SmokeTests --filter "Category=Procedures&Procedure=install"` passes, and a fresh reader completes quickstart Q9.

### Tests for User Story 1

- [X] T023 [P] [US1] Create the procedure test harness in `tests/QuickBuilder.SmokeTests/Procedures/ProcedureRunner.cs`.
  - Call `qb-docs snippets write` for a procedure into a temp directory.
  - Run `bash run.sh` with a timeout of 10 minutes, `COMPOSE_PROJECT_NAME` unique per run, and `QB_SMOKE_IMAGE` exported as `QUICKBUILDER_IMAGE`.
  - Capture stdout/stderr and compose logs into `QB_SMOKE_LOG_DIR`.
  - Always run `docker compose down -v` on dispose.
  - Reuse `ComposeDeployment`'s Docker checks (`QB_SMOKE_REQUIRE_DOCKER`).
- [X] T024 [US1] Write `tests/QuickBuilder.SmokeTests/Procedures/InstallProcedureTests.cs` (`[Trait("Category","Procedures")]`, `[Trait("Procedure","install")]`).
  - Run the `install` procedure with substitutions `release-compose`, `image` and `secret`.
  - Assert `/health/ready` returns 200.
  - Read the setup code from `docker compose logs app`.
  - Run the reference journey from `ReferenceDeploymentSmokeTests`: setup wizard, create page, publish, view. Extract a shared helper rather than duplicating it.
  - Must fail until T026.

### Implementation for User Story 1

- [X] T025 [US1] Write `docs/operators/install.md`, section **Prerequisites**:
  - supported architectures: linux/amd64 and linux/arm64;
  - minimum resources: 2 vCPU and 4 GB RAM for the reference setup (Principle VIII), plus disk for media;
  - Docker Engine with the Compose v2 plugin, with the minimum versions stated;
  - port 8080 on 127.0.0.1 by default;
  - outbound network needed only to pull images.

  Add a `qb:run procedure=install step=1` block that checks `docker compose version` (FR-006).
- [X] T026 [US1] Write `docs/operators/install.md`, sections **Install** through **Next steps**, each command in a `qb:run procedure=install` block in order:
  1. create a directory;
  2. download the release's `docker-compose.yml` (`substitute=release-compose`, URL pattern with `{version}`);
  3. generate a database password and put it in a `.env` file not committed to version control, replacing the placeholder in both `POSTGRES_PASSWORD` and `ConnectionStrings__Default` (`substitute=secret`);
  4. `docker compose up -d`;
  5. wait for `/health/ready`;
  6. `docker compose logs app | grep` for the setup code.

  Then describe the setup wizard in prose and finish with **Next steps** linking reverse-proxy, backup-restore and security-checklist (FR-007, US1 scenario 3).

  If step 3 needs a compose change (an `env_file` or `${…}` interpolation of the password), make it in `docker-compose.yml`, keeping the 127.0.0.1 binding and the existing comments.
- [X] T027 [US1] Update the header comment of `docker-compose.yml` to point at `docs/operators/install.md` and the published site instead of `docs/operations.md`, and keep the reference journey green (`dotnet test tests/QuickBuilder.SmokeTests --filter Category=Smoke`).
- [X] T028 [P] [US1] Create `.github/workflows/docs-procedures.yml`:
  - `on: pull_request` with `paths: [docs/operators/**, docker-compose.yml, Dockerfile, src/QuickBuilder.Host/appsettings.json]`;
  - `on: workflow_call` with input `image` (a digest reference);
  - job `procedures` with matrix `procedure: [install]`, extended in T052 and T071;
  - on PRs, build the image from the branch as the `ci.yml` `image` job does; otherwise use the input;
  - run `dotnet test tests/QuickBuilder.SmokeTests -c Release --filter "Category=Procedures&Procedure=${{ matrix.procedure }}"`;
  - upload compose logs on failure;
  - a comment saying this workflow must not be made a required check (OD-10).

**Checkpoint**: the install page is complete and executed. The MVP is US1 plus US2 and US3 (the reference and its gate).

---

## Phase 4: User Story 2 — An operator configures the product from a complete, accurate reference (Priority: P1)

**Goal**: every setting the product reads is in a generated reference with name, environment form, type, default, required, allowed values, description and secret flag.

**Independent Test**: quickstart Q1. `qb-docs settings check` exits 0, and the generated `docs/operators/reference/configuration.md` holds every key from today's `docs/configuration.md` with the same spelling.

### Tests for User Story 2

- [X] T029 [P] [US2] Write `tests/QuickBuilder.UnitTests/Docs/ReferenceWriterTests.cs` (data-model §3).
  - Table header `| Key | Environment variable | Type | Default | Allowed values | Description |`.
  - The Allowed values cell for `[Range]` (`16 – 16384`), enum, `[AllowedValues]`, `Format` (verbatim), and `—` when unconstrained (data-model §3).
  - Required prefix **Required.**, the secret notice **Secret.**, and per-group sub-headings with `AppliesWhen`.
  - The deprecated line, the hand-written note inlined after the area heading, and the generated-file comment on line 1.
  - Deterministic output: sorted by declaration order and stable across runs.
  - The key column matches `ConfigurationKeys`' row regex.
- [X] T030 [P] [US2] Write `tests/QuickBuilder.UnitTests/Docs/SettingsReferenceIsCurrentTests.cs`.
  - Build the real catalog from the product assemblies and generate the reference in memory.
  - Assert it equals the committed `docs/operators/reference/configuration.md`, line endings normalised to `\n`.
  - On failure, print a unified diff and `Run: dotnet run --project tools/QuickBuilder.Docs -- settings generate`.
- [X] T031 [P] [US2] Write `tests/QuickBuilder.UnitTests/Docs/KeySpellingContinuityTests.cs`. Every key that `ConfigurationKeys` extracts from `git show v0.1.0:docs/configuration.md` must appear in the generated reference, except keys listed as removed in a fragment under `docs/releases/unreleased/`. This proves OD-05: no spurious renames.

### Implementation for User Story 2

- [X] T032 [US2] Implement `tools/QuickBuilder.Docs/Settings/ReferenceWriter.cs` and the commands `settings generate [--out]` and `settings keys` (makes T029 pass).
- [X] T033 [P] [US2] Annotate the Host options classes:
  - `ConnectionOptions`, `TelemetryOptions`, `HostingOptions`, `OutboxOptions` and `AdminOptions`;
  - files: `src/QuickBuilder.Host/Configuration/ConnectionOptions.cs` and `src/QuickBuilder.Host/Configuration/HostingOptions.cs`;
  - use `[SettingsSection]`, `[Setting]` and `[NotASetting]`;
  - mark `ConnectionStrings:Default` with `Secret=true`;
  - give `Hosting:BasePath` and `Hosting:PublicUrl` a `Format`;
  - write operator text taken from the matching rows of `docs/configuration.md`, reworded so no row restates another.
- [X] T034 [P] [US2] Annotate the Shared.Infrastructure options classes with `[SettingsSection]` and `[Setting]`:
  - `DatabaseOptions` (`Migrations/DatabaseOptions.cs`), `CacheOptions` (`Caching/CacheOptions.cs`), `SmtpOptions` (`Email/SmtpOptions.cs`, password `Secret=true`), `TrashOptions` (`Trash/TrashOptions.cs`);
  - `StorageOptions` and its provider settings types in `Storage/` (`[SettingsGroup]` with `AppliesWhen = "Storage:Provider = S3"` / `"… = AzureBlob"` / `"… = Local"`; `Storage:S3:SecretAccessKey` and `Storage:AzureBlob:ConnectionString` `Secret=true`; `ForbiddenRoots` `[NotASetting]`; `Storage:Local:Path` `DefaultText` "Image: `/var/lib/quickbuilder/media`. Elsewhere: `{LocalApplicationData}/QuickBuilder/media`");
  - all files are under `src/Shared/QuickBuilder.Shared.Infrastructure/`.
- [X] T035 [P] [US2] Annotate the module options classes:
  - `src/Modules/Identity/QuickBuilder.Identity/IdentityOptions.cs`: `IdentityOptions`. Its `Hosting` view class, which declares `Hosting:PublicUrl` again, gets `[NotASetting("Declared by the Host's HostingOptions.")]` on each property, so each key is declared once.
  - `src/Modules/Media/QuickBuilder.Media/MediaOptions.cs`.
  - `src/Modules/Forms/QuickBuilder.Forms/FormsOptions.cs`.
  - `src/Modules/Editor/QuickBuilder.Editor/Infrastructure/PreviewOptions.cs`.
  - `src/Modules/Sites/QuickBuilder.Sites/SiteTransferOptions.cs`.
- [X] T036 [US2] Move every explanatory paragraph of `docs/configuration.md` that is not a table row into `docs/operators/reference/notes/<area>.md`, one file per area in data-model §2.
  - Examples: "Trusting nothing is the default and it matters", the startup/readiness probe text for storage, "The cache never decides what visitors see".
  - Remove sentences that restate a default or range the table now gives (FR-013).
  - Keep the opening "how configuration works" text for `configure.md` (T038).
- [X] T037 [US2] Run `qb-docs settings generate` to create `docs/operators/reference/configuration.md`.
  - Compare it row by row with `docs/configuration.md`. Every key, default and meaning must be preserved or corrected to what the code does, with each correction noted in the PR description.
  - Make T030 and T031 pass.
- [X] T038 [US2] Write `docs/operators/configure.md`:
  - `appsettings.json` versus environment variables and the `:` → `__` rule;
  - list entries `__0`, `__1`;
  - how to pass secrets: a `.env` file outside version control, Docker secrets, or the orchestrator's secret store, never a committed file;
  - startup validation stops the app naming the key;
  - where the reference is, with a link.
- [X] T039 [US2] Delete `docs/configuration.md`, and update `tools/QuickBuilder.Release/ConfigurationKeys.cs` so `Guide` is the ordered list `["docs/operators/reference/configuration.md", "docs/configuration.md"]`: the first path present at the ref (or in the working tree) is read (OD-06).
  - Add tests to `tests/QuickBuilder.UnitTests/Release/ConfigurationKeysTests.cs` for v0.1.0 (old path only), HEAD (new path only) and a missing file.
  - Update `FragmentCheck.cs`, where `path == ConfigurationKeys.Guide`, to match either path.
- [X] T040 [US2] Create `docs/releases/unreleased/26-operator-documentation.md` (kind `added`, issue 26).
  - Body: operator documentation site and generated configuration reference.
  - `config.added` lists exactly the keys present in the generated reference but absent from v0.1.0's `docs/configuration.md` (from T031's output).
  - Run `qb-release notes --version 0.2.0 --previous v0.1.0 …` (quickstart Q3) and confirm the Configuration section matches.

**Checkpoint**: the reference is generated, complete for the declared settings, and the 019 key comparison still works.

---

## Phase 5: User Story 3 — A contributor cannot merge an undocumented or stale setting (Priority: P1)

**Goal**: any change that adds, renames or removes a setting without its documentation fails the checks, naming the setting.

**Independent Test**: quickstart Q2, steps 1–5.

### Tests for User Story 3

- [X] T041 [P] [US3] Write `tests/QuickBuilder.IntegrationTests/Configuration/RecordingConfigurationSource.cs`. The provider returns `false` from `TryGet` and records the key; `GetChildKeys` records the parent path. Use a thread-safe set. Unit-test it with a `ConfigurationBuilder` and `Bind` of a small options class, asserting every property path is recorded.
- [X] T042 [US3] Write `tests/QuickBuilder.IntegrationTests/Configuration/EveryReadSettingIsDeclared.cs` (contracts/setting-attributes.md §4).
  - Boot the host with WebApplicationFactory and Testcontainers PostgreSQL, with the recording source added last.
  - Make one request each to `/health/ready` and `/`.
  - Observe host command configuration paths: resolve the services of each host command (`migrate`, `identity setup-code`, `identity recovery-link`) from the booted provider without executing them.
  - Repeat the boot with `Storage:Provider=AzureBlob` against `AzuriteFixture`, and with `Storage:Provider=S3` against the S3 container fixture `RemoteFileStorageConformanceTests` uses (both in `tests/QuickBuilder.IntegrationTests/Storage/`), so the startup probe succeeds against real emulators.
  - Assert observed ⊆ declared ∪ external ∪ exclusions, and declared ⊆ observed ∪ `ObservedLazily`.
  - On failure, list each key with the fix text.

### Implementation for User Story 3

- [X] T043 [US3] Run T042 and resolve every finding.
  - Undeclared product keys get `[Setting]`. For example, `SitesOptions` (internal, no section) reads `Hosting:AdminHostNames` directly in `src/Modules/Sites/QuickBuilder.Sites/SitesOptions.cs`; the boot test must observe it as the key `HostingOptions` declares, so no annotation is needed there.
  - Framework keys get an allow-list entry with a reason in `tools/QuickBuilder.Docs/Settings/FrameworkAllowList.cs`.
  - Remove allow-list entries never observed.
  - Declarations never observed are removed, or marked `ObservedLazily` with a reason.
  - Regenerate the reference and update the T040 fragment if keys were added.
- [X] T044 [US3] Implement the command `settings check` in `tools/QuickBuilder.Docs/Program.cs`.
  - Run catalog rules 1–7.
  - Compare against the committed reference (rule 8) with a diff.
  - Print the exclusion list, computed defaults and `ObservedLazily` settings under a "For review" heading.
  - Assert each `DefaultText` equals the value resolved after `PostConfigure` with the image's environment (for `Storage:Local:Path`, the container default), and add a unit test for it in `tests/QuickBuilder.UnitTests/Docs/ComputedDefaultsTests.cs` (SC-003).
  - Exit 1 with the generate command on any failure.
- [X] T045 [P] [US3] Implement the lint checks `settings-exist` and `notes-restate` (OD-19, contracts/qb-docs-cli.md) in `tools/QuickBuilder.Docs/Lint/SettingsExistCheck.cs` and `Lint/NotesRestateCheck.cs`.
  - Scan `docs/operators/**/*.md`, `docker-compose.yml` and `README.md`.
  - Match lists and maps by prefix.
  - Add tests to `tests/QuickBuilder.UnitTests/Docs/SettingsExistCheckTests.cs`.
- [X] T046 [P] [US3] Implement the lint check `example-secrets` in `tools/QuickBuilder.Docs/Lint/ExampleSecretsCheck.cs`.
  - In fenced blocks and compose files, a `Secret` setting may only be assigned a placeholder: `change-me…`, `<…>` or `${…}`.
  - Add tests to `tests/QuickBuilder.UnitTests/Docs/ExampleSecretsCheckTests.cs`.
- [X] T047 [US3] Add `qb-docs settings check` to the `docs` job in `.github/workflows/ci.yml`. It runs before `lint`. The unit and integration tests already run in the backend job; confirm `EveryReadSettingIsDeclared` is not filtered out by the existing `--filter`.
- [X] T048 [US3] Walk through quickstart Q2 steps 1–5 on a scratch branch, and record the exact failure output of each step in `specs/026-operator-documentation/quickstart-results.md`.

**Checkpoint**: the reference cannot drift from the product. A missing description, a hand edit, an undeclared read or a dead declaration each fail CI.

---

## Phase 6: User Story 4 — An operator backs up and restores an installation (Priority: P1)

**Goal**: one documented, executed procedure backs up the database, media, configuration and secrets, and restores them onto a clean installation.

**Independent Test**: `Category=Procedures&Procedure=backup-restore` passes against the image under test (quickstart Q5).

### Tests for User Story 4

- [X] T049 [US4] Write `tests/QuickBuilder.SmokeTests/Procedures/BackupRestoreProcedureTests.cs` (contracts/runnable-snippets.md §3).
  - **Seed.** Install via the `install` procedure, then use the API to seed: a published page, a draft change to it, a collection with one item, and an uploaded image. Record the image's SHA-256.
  - **Back up.** Run the `backup` steps.
  - **Tear down.** `docker compose down -v`.
  - **Restore.** Run the `restore` steps in a new compose project directory.
  - **Assert.** The published page renders the same HTML body, the draft is present in the admin API, the item is present, the image bytes hash is equal, and the smoke journey passes.
  - **Older release.** Start an image one minor older (the 019 upgrade fixture's previous image) on the restored database and assert startup is refused with the gate's message.
  - Must fail until T050–T051.

### Implementation for User Story 4

- [X] T050 [US4] Write `docs/operators/backup-restore.md`, **Back up** section. Mark each command `qb:run procedure=backup-restore`, in this order:
  1. `docker compose stop app`;
  2. `pg_dump --format=custom` through `docker compose exec -T db`;
  3. tar of the media volume, finding the volume name with `docker volume ls`;
  4. copy of `.env` and `docker-compose.yml`;
  5. `docker compose start app`.

  Explain:
  - why the app is stopped (consistency between database and media; the effects of restoring from different moments, moved from `docs/operations.md` "Back up and restore");
  - expected downtime;
  - backup frequency guidance;
  - protecting backups: they contain personal data and secrets, so encrypt them and restrict access;
  - S3 and Azure Blob: use the provider's versioning and backup, keep it consistent with the database dump, and note that `media-variants/` needs no backup (FR-022, FR-023).

  Keep the Windows/WSL note from the existing text.
- [X] T051 [US4] Write `docs/operators/backup-restore.md`, sections **Verify a backup**, **Restore**, and **Full backup or site export?**.
  - **Verify a backup**: `pg_restore --list` on the dump, plus a periodic test restore.
  - **Restore** (marked steps): new directory, copy `docker-compose.yml` and `.env`, start `db` only, `pg_restore` into it, extract media into the volume, then `docker compose up -d`. Add the version rules: restoring into a newer release is an upgrade (link `upgrade.md`); restoring into an older release is refused (FR-024).
  - **Full backup or site export?**: when each fits, linking the site export/import page in `operations/` (FR-025).
- [X] T052 [US4] Extend the matrix in `.github/workflows/docs-procedures.yml` with `backup-restore`.

**Checkpoint**: backup and restore is documented and proven on every release and every docs PR.

---

## Phase 7: User Story 5 — An operator upgrades between releases safely (Priority: P1)

**Goal**: a release-based upgrade guide, generated upgrade notes for every release in order, and no milestone-based upgrade text left.

**Independent Test**: `upgrade.md` covers paths, before/during/after and rollback. `upgrade-notes.md` holds 0.1.0's notes. Lint `milestone-reference` passes. The refusal log link resolves (quickstart Q8).

### Tests for User Story 5

- [X] T053 [P] [US5] Write `tests/QuickBuilder.UnitTests/Docs/UpgradeNotesGeneratorTests.cs`.
  - Use a fixture `docs/releases/` with `0.1.0/` and `0.2.0/` fragment folders and a fake git tag list.
  - The output has newest first, an anchor `#<version>` per release, and only the "Upgrade notes" and "Supported upgrade path" sections.
  - `--through 0.1.0` omits 0.2.0.
  - A fragment error from the 019 assembler is passed through with exit 1.
- [X] T054 [P] [US5] Write `tests/QuickBuilder.UnitTests/Docs/MilestoneReferenceCheckTests.cs`. The texts "Upgrading from feature 012", "Upgrading to feature 023" and "from feature 15" are found; "feature flag" is not.
- [X] T055 [P] [US5] Extend the installation gate tests to assert the Critical refusal log line links to `{QbDocsBaseUrl}{major}.{minor}/upgrade#when-quickbuilder-refuses-to-start`. The tests are in `tests/QuickBuilder.UnitTests/` (locate the existing `InstallationGate` tests).

### Implementation for User Story 5

- [X] T056 [US5] Implement `tools/QuickBuilder.Docs/UpgradeNotes/UpgradeNotesGenerator.cs` and the command `upgrade-notes generate [--through]` (OD-16).
  - Use `QuickBuilder.Release`'s notes assembler for each released version folder in `docs/releases/<version>/`.
  - Read released versions from git tags `vX.Y.Z`, excluding pre-releases.
  - Write `docs/operators/upgrade-notes.md` with the generated-file comment.
  - Makes T053 pass.
- [X] T057 [US5] Implement the lint check `milestone-reference` in `tools/QuickBuilder.Docs/Lint/MilestoneReferenceCheck.cs` (makes T054 pass).
- [X] T058 [US5] Write `docs/operators/upgrade.md` from `docs/operations.md` "Upgrading and rolling back".
  - Sections: **Supported upgrade paths** (SemVer rules and the upgrade floor), **Before you upgrade** (find your release, read every release's notes on `upgrade-notes.md`, back up by linking `backup-restore.md`, review configuration changes), **Upgrade**, **After you upgrade**, **Rolling back**, and **When QuickBuilder refuses to start**, with anchor `when-quickbuilder-refuses-to-start` and one sub-entry per gate refusal (newer database, too-old database) giving cause and fix (FR-027, FR-029).
  - Replace links to the GitHub releases page with `upgrade-notes.md`.
- [X] T059 [US5] Apply data-model §5 to `docs/operations.md`.
  - For each "Upgrading from/to feature NNN" section, confirm whether the feature's fragment is in `docs/releases/0.1.0/` or `docs/releases/unreleased/`.
  - For unreleased features (020–023), move every operator step into that fragment's `manual`, `background` or `caches` entries. For example, "Upgrading to feature 023" goes into `docs/releases/unreleased/23-concurrency.md`.
  - Delete every such section.
  - Record the disposition of each section in the PR description (FR-044).
- [X] T060 [US5] Point `InstallationGate.Guide` in `src/Shared/QuickBuilder.Shared.Infrastructure/Migrations/Platform/InstallationGate.cs` at `{QbDocsBaseUrl}{major}.{minor}/upgrade#when-quickbuilder-refuses-to-start`.
  - Read the URL from the Host assembly metadata written in T004, passed in the same way as the upgrade floor.
  - Change only the URL, not the wording (OD-11).
  - Makes T055 pass.
- [X] T061 [US5] Update `docs/releases/TEMPLATE.md` and `tools/QuickBuilder.Release/NotesAssembler.cs`.
  - Add the template variable `{{docs}}` = `{QbDocsBaseUrl}{major}.{minor}`, read from `Directory.Build.props`.
  - The header link becomes `{{docs}}/upgrade`.
  - Configuration key links become `{{docs}}/reference/configuration#<anchor>`. The anchor rule must match VitePress's heading slugs, or explicit `{#…}` anchors emitted by `ReferenceWriter` per key.
  - Update `tests/QuickBuilder.UnitTests/Release/NotesAssemblerTests.cs`.
- [X] T096 [US5] Add `config.deprecated: [Key]` to the release-note fragment format.
  - `tools/QuickBuilder.Release` parses it, and the fragment check validates it.
  - `NotesAssembler.cs` renders it under "Upgrade notes → Configuration" as "Deprecated: `X` (use `Y`; removed in Z)", reading replacement and removal version from the generated reference.
  - The assembler refuses notes when a key newly marked `[DeprecatedSetting]` since the previous release has no fragment entry, using the same mechanism as added and removed keys.
  - Update `specs/019-release-upgrade-safety/contracts/release-notes.md` §2–3 and `tests/QuickBuilder.UnitTests/Release/NotesAssemblerTests.cs` (FR-028).
- [X] T062 [US5] Run `qb-docs upgrade-notes generate` to create `docs/operators/upgrade-notes.md` (0.1.0) and link it from `upgrade.md` and the sidebar.

**Checkpoint**: upgrading is documented by release, with every release's notes in one place.

---

## Phase 8: User Story 6 — An operator puts QuickBuilder behind a reverse proxy with TLS (Priority: P2)

**Goal**: working, executed Nginx, Caddy and Traefik examples with TLS, forwarded headers, several sites, admin host names, upload limits and a sub-path variant.

**Independent Test**: `Category=Procedures` with `Procedure` = `nginx`, `caddy`, `traefik` and `proxy-basepath` passes (quickstart Q5).

### Tests for User Story 6

- [X] T063 [P] [US6] Write `tests/QuickBuilder.SmokeTests/Procedures/TestCertificateAuthority.cs`.
  - Generate a throwaway root CA and a leaf for `example.com`, `shop.example.net`, `admin.example.com` and `www.example.com` using `CertificateRequest` (RSA 2048, 1 day).
  - Write PEM files to a directory.
  - Expose the CA path for `curl --cacert`.
  - No new dependency.
- [X] T064 [US6] Write `tests/QuickBuilder.SmokeTests/Procedures/ReverseProxyProcedureTests.cs`. One test per proxy (`[Trait("Procedure","nginx"|"caddy"|"traefik")]`), each following contracts/runnable-snippets.md §3:
  - **Setup.**
    - Run the `install` procedure, then the proxy procedure, with substitutions `test-certificates` and `host-names`.
    - Through the API, create two sites with host names `example.com` and `shop.example.net`.
    - Set `Hosting__AdminHostNames__0=admin.example.com` and `Identity__AllowInsecureHttp=false` as the page instructs.
  - **Assert.**
    - `curl --resolve <host>:443:127.0.0.1 --cacert` reaches each site over HTTPS with its own content.
    - `/admin` on `example.com` answers like a missing page.
    - Sign-in at `admin.example.com` succeeds.
    - The latest `identity.security_events` sign-in row records the test client's address, not the proxy container's.
    - A 50 MB upload succeeds.
  - **Base-path variant.** A fourth test, `proxy-basepath`, runs with Nginx and `Hosting__BasePath=/cms`. It asserts that the site answers under `/cms/` and that `/robots.txt` reaches the product.

### Implementation for User Story 6

- [X] T065 [US6] Write `docs/operators/reverse-proxy/index.md`, covering what any proxy must do:
  - TLS termination;
  - forwarding `Host`, `X-Forwarded-Proto` and `X-Forwarded-For`;
  - naming the proxy in `Hosting:KnownProxies` / `Hosting:KnownNetworks`, with the security reasons moved from the Hosting notes;
  - upload and import size limits;
  - several domains and certificates per domain;
  - admin host names;
  - **Sub-path installation**: a marked `proxy-basepath` variant using Nginx, including forwarding `/robots.txt`;
  - **Checks and common mistakes**: proxy not trusted gives wrong client address and sign-in refused over HTTP; missing `Host` gives the not-found page; a 413 on upload. Each has how to detect it and how to fix it (FR-018, FR-019, US6 scenario 5).
- [X] T066 [P] [US6] Write `docs/operators/reverse-proxy/nginx.md`.
  - "Checked with nginx <version>".
  - A `qb:file procedure=nginx path=nginx/quickbuilder.conf` server config with one server block per site plus admin, `client_max_body_size`, the forwarded headers, and certbot certificate paths (`substitute=test-certificates`).
  - A `qb:file … path=compose.proxy.yml` compose override adding `nginx:<pinned tag>` on ports 80/443 and the app's `Hosting__KnownProxies__0`.
  - `qb:run` steps to start it.
  - Obtaining certificates with certbot, in prose plus commands, not executed.
- [X] T067 [P] [US6] Write `docs/operators/reverse-proxy/caddy.md`.
  - "Checked with Caddy <version>".
  - A `qb:file procedure=caddy path=caddy/Caddyfile` with automatic HTTPS per site; the test substitution replaces issuance with `tls <cert> <key>` lines.
  - `request_body max_size`.
  - A compose override pinning `caddy:<tag>`, with `KnownNetworks` set to the compose network's CIDR.
  - `qb:run` start steps.
- [X] T068 [P] [US6] Write `docs/operators/reverse-proxy/traefik.md`.
  - "Checked with Traefik <version>".
  - A `qb:file procedure=traefik` static config and a compose override with labels on the app per host name, and an ACME resolver (substituted with a file-provider default certificate).
  - A `buffering` middleware for request size.
  - `forwardedHeaders` trust.
  - `qb:run` start steps.
- [X] T069 [US6] Implement the lint check `proxy-version` in `tools/QuickBuilder.Docs/Lint/ProxyVersionCheck.cs`. The "Checked with" version must equal the pinned image tag in the page's compose block. Add tests to `tests/QuickBuilder.UnitTests/Docs/ProxyVersionCheckTests.cs` (FR-021).
- [X] T070 [US6] Implement the `test-certificates` and `host-names` substitutions per proxy in `tools/QuickBuilder.Docs/Snippets/Substitutions.cs`.
  - Each substitution is limited to lines inside blocks marked `substitute=test-certificates`.
  - Add tests asserting that nothing outside those lines changes.
- [X] T071 [US6] Extend the matrix in `.github/workflows/docs-procedures.yml` with `nginx`, `caddy`, `traefik` and `proxy-basepath`.

**Checkpoint**: three proxies and the sub-path variant are documented and proven.

---

## Phase 9: User Story 7 — An operator troubleshoots a problem without reading source code (Priority: P2)

**Goal**: a symptom-first troubleshooting guide covering FR-030's list, every gate refusal and readiness status, logs, health and what to send when asking for help.

**Independent Test**: lint `troubleshooting-coverage` passes. Each entry links to its reference or guide. A reviewer reproduces three symptoms (config error, not-found host, 413) and the guide leads to the fix.

### Tests for User Story 7

- [X] T072 [P] [US7] Write `tests/QuickBuilder.UnitTests/Docs/TroubleshootingCoverageCheckTests.cs`. A fixture page missing the `migrations-pending` anchor fails, naming it. A complete fixture passes.

- [X] T097 [P] [US7] Write `tests/QuickBuilder.UnitTests/Configuration/ValidationMessagesNameTheirKeyTests.cs`.
  - For each `IValidateOptions<T>` in the product (Hosting, Smtp, Local/S3/AzureBlob storage, and any others found by reflection), feed one invalid value per validated property.
  - For each DataAnnotations-validated setting, feed a value that breaks its attribute.
  - Assert the failure message contains the full `Section:Key`.
  - Reword any message that fails so it names the key, changing wording only, not behaviour (FR-031).

### Implementation for User Story 7

- [X] T073 [US7] Implement the lint check `troubleshooting-coverage` in `tools/QuickBuilder.Docs/Lint/TroubleshootingCoverageCheck.cs` with the required anchor list (OD-18):
  - every readiness status the health endpoint reports, found by reading the readiness status names in `src/QuickBuilder.Host/Health/`;
  - the gate refusals `database-newer` and `database-too-old`;
  - the FR-030 symptoms: `container-does-not-start`, `configuration-error`, `database-unreachable`, `host-shows-not-found`, `admin-unreachable`, `upload-rejected`, `email-not-sent`, `sign-in-throttled`, `sign-in-refused-over-http`, `stale-content`.

  Makes T072 pass.
- [X] T074 [US7] Write `docs/operators/troubleshooting.md`.
  - One `## <symptom as the operator sees it> {#anchor}` per required anchor.
  - Each entry has **What you see**, **Why**, **Fix** and a link to the reference entry or guide.
  - The configuration-error entry explains that the message names the key, how to find it in the reference, and the environment variable form (FR-031).
  - A section **Logs and health** covers `docker compose logs app`, JSON log fields, `TraceId` matching the error response, and `/health/live` and `/health/ready` statuses.
  - A section **Asking for help** covers what to collect (version line, readiness output, trace ids, redacted configuration) and what never to share (secrets, content, personal data) (FR-032).
- [X] T075 [P] [US7] Write `docs/operators/reference/health.md`, covering the two endpoints and every readiness status with its meaning. Write `docs/operators/reference/commands.md`, covering `migrate`, `identity setup-code` and `identity recovery-link --email`, moved from `docs/operations.md` "Getting back in", plus any other `HostCommandRunner` commands with usage and output.

**Checkpoint**: every message the product can stop with is findable and explained.

---

## Phase 10: User Story 8 — An operator hardens an installation with a security checklist (Priority: P2)

**Goal**: a checklist where every item has Check, Verify and Fix, covering FR-033, plus a vulnerability reporting route.

**Independent Test**: lint `security-coverage` passes. Working through the checklist on an installation set up from `install.md` only, each Verify step is observable.

### Tests for User Story 8

- [X] T076 [P] [US8] Write `tests/QuickBuilder.UnitTests/Docs/SecurityCoverageCheckTests.cs`. Two fixtures fail: one missing an item, and one with an item lacking **Verify**.

### Implementation for User Story 8

- [X] T077 [US8] Implement the lint check `security-coverage` in `tools/QuickBuilder.Docs/Lint/SecurityCoverageCheck.cs`. The required anchors are `tls`, `trusted-proxies`, `admin-host-names`, `secrets`, `database-not-exposed`, `media-not-exposed`, `backups`, `updates`, `personal-data-in-logs`, `outbound-features` and `report-a-vulnerability`. Each item except the last must contain **Check**, **Verify** and **Fix**. Makes T076 pass.
- [X] T078 [US8] Write `docs/operators/security-checklist.md`, one item per anchor.
  - The secure value and the risk of the convenient one (FR-034, US8 scenario 2). Examples:
    - `Identity:AllowInsecureHttp` must be `false` once the port is widened;
    - publish the app on 127.0.0.1 until setup finishes;
    - the database port is never published;
    - `Telemetry:OtlpEndpoint` is empty unless needed.
  - A Verify command or observation for each item, with Fix links to the relevant guide or reference entry.
  - The final item links to `SECURITY.md` (FR-035).
- [X] T079 [P] [US8] Create `SECURITY.md` at the repository root.
  - Supported versions: the latest minor of the current major.
  - How to report privately: GitHub private vulnerability reporting for the repository, with no personal contact details.
  - What to include, and the response expectations.

**Checkpoint**: an operator can harden and verify an installation from one page.

---

## Phase 11: User Story 9 — An operator reads the documentation for their release online (Priority: P2)

**Goal**: each promoted release publishes its minor line's docs. Older lines stay unchanged. A failed docs build stops the release first.

**Independent Test**: quickstart Q7, steps 1–7, against a scratch Pages repository.

### Tests for User Story 9

- [X] T080 [P] [US9] Write `tests/QuickBuilder.UnitTests/Docs/VersionsManifestTests.cs` for a `versions.json` upsert helper (`tools/QuickBuilder.Docs/Publishing/VersionsManifest.cs`).
  - Insert a new line.
  - Replace the same line's release.
  - Sort newest first by numeric major.minor (`0.10` > `0.9`).
  - Compute the root redirect target only when `highest`.
  - Reject pre-release versions.

### Implementation for User Story 9

- [X] T081 [US9] Implement `tools/QuickBuilder.Docs/Publishing/VersionsManifest.cs` and the command `publish prepare --site <gh-pages checkout> --build <dist> --version <x.y.z> --highest <bool>`.
  - Replace `/<major>.<minor>/` with the build.
  - Upsert `versions.json`.
  - Rewrite `index.html` only when `highest`.
  - Create `.nojekyll` if absent.
  - Touch nothing else (contracts/docs-publishing.md §3 step 4).
  - Makes T080 pass.
- [X] T082 [P] [US9] Add the version picker and the "Built from" label in `docs/operators/.vitepress/theme/index.ts` and `docs/operators/.vitepress/theme/VersionPicker.vue`.
  - Fetch `../versions.json` relative to the base and render a nav dropdown.
  - Hide it when the fetch fails (offline copy).
  - Show "Built from {QB_DOCS_RELEASE}" from a `define` set in `config.ts` (contracts/docs-publishing.md §4).
  - No external requests.
- [X] T083 [US9] Extend the `prepare` job in `.github/workflows/release.yml`, adding steps before the job ends so failures stop the run before `build`:
  1. Build `qb-docs`.
  2. Run `qb-docs upgrade-notes generate --through <version>` and fail if `git status --porcelain docs/operators` is not empty.
  3. Run `qb-docs settings check` and `qb-docs lint`.
  4. Run `npm ci && npm run build` in `docs/operators` with `QB_DOCS_BASE=/<repo>/<major>.<minor>/` and `QB_DOCS_RELEASE=<version>`.
  5. Upload artifact `docs-site` with 90-day retention.
- [X] T084 [US9] Add job `procedures` to `.github/workflows/release.yml`.
  - `needs: manifest`; `uses: ./.github/workflows/docs-procedures.yml` with `image: ${IMAGE}@${digest}`.
  - Add `procedures` to `candidate.needs` and to the gate loop that writes the **Not available** banner, so `promote.yml` refuses a candidate whose procedures failed (FR-043, OD-09).
- [X] T085 [US9] Add step "Publish the documentation" at the end of `.github/workflows/promote.yml`.
  - Skip when `prerelease == true`.
  - Get the site with `gh run download` of artifact `docs-site` from the tag's successful Release run. Otherwise check out the tag and rebuild, as T083 step 4 does.
  - Check out `gh-pages` of `${{ vars.DOCS_PAGES_REPOSITORY || github.repository }}`, creating an orphan branch if missing. Use `secrets.DOCS_DEPLOY_KEY` when set (ssh), else `GITHUB_TOKEN`.
  - Run `qb-docs publish prepare`, commit `docs: publish <version>`, push, and retry once after `git pull --rebase` on conflict.
  - On a refused push or a missing Pages site, fail with the exact message in contracts/docs-publishing.md §3 step 6.
  - Pass `highest` and `prerelease` from the existing `version` step.
- [X] T086 [US9] Update `README.md`: link the published docs and `docs/operators/`, and remove links to `docs/operations.md` and `docs/configuration.md`.
- [ ] T087 [US9] Run quickstart Q7 steps 1–7 against a throwaway public repository set as `DOCS_PAGES_REPOSITORY`, and record the results, including the `gh-pages` `git diff --stat` for step 4, in `specs/026-operator-documentation/quickstart-results.md`.

- [ ] T095 [US9] Before tagging v0.2.0, confirm the publishing precondition: the repository is public, or `DOCS_PAGES_REPOSITORY` and `DOCS_DEPLOY_KEY` are set and Pages is enabled on the target's `gh-pages` branch. Record the decision in `specs/026-operator-documentation/quickstart-results.md`.

**Checkpoint**: releases publish their docs; history is preserved per minor line.

---

## Phase 12: Polish & Cross-Cutting Concerns

- [X] T088 Move the remaining feature topics of `docs/operations.md` into `docs/operators/operations/`. Group them in this order:
  1. media storage;
  2. sites and host names;
  3. pages, editor and design;
  4. publishing and rendering;
  5. collections and templates;
  6. search engines, sitemaps and redirects;
  7. navigation;
  8. forms and email;
  9. identity, passwords and security events;
  10. site export and import;
  11. trash;
  12. preview links.

  - Keep the text.
  - Remove anything already covered by a task page.
  - Replace configuration tables with links to the reference.
  - Add each page to the sidebar.
- [X] T089 Delete `docs/operations.md` once T050, T058, T059, T075 and T088 have moved its content.
  - Implement the lint check `no-old-paths` in `tools/QuickBuilder.Docs/Lint/NoOldPathsCheck.cs`, with tests. It scans the repository except `specs/`, `docs/releases/0.1.0/` and `.git/`.
  - Fix every remaining link, including code comments in `src/Modules/Editor/QuickBuilder.Editor/Infrastructure/PreviewOptions.cs`, `src/Modules/Identity/QuickBuilder.Identity/IdentityOptions.cs` and `src/Shared/QuickBuilder.Shared.Infrastructure/Storage/S3/S3AbandonedUploadCleanupService.cs` (FR-004, SC-010).
- [X] T090 [P] Check every page against FR-005: commands are given in full, no page asks the reader to look at source code, and placeholders use `<…>`. Fix the findings.
- [X] T091 [P] Check SC-009: from `index.md`, every US1–US8 answer is reachable in at most 3 clicks. Record the click paths in `specs/026-operator-documentation/quickstart-results.md`.
- [ ] T092 Run quickstart Q1–Q6 and Q8 in full and record the outcomes in `specs/026-operator-documentation/quickstart-results.md`.
- [ ] T093 Run quickstart Q9 (fresh reader) and record the time and every point where the reader had to guess. Fix the install page for each guess and re-run the `install` procedure.
- [X] T094 Run `dotnet format --verify-no-changes`, the full unit and integration suites, and the architecture tests. Confirm `Shared.Kernel` gained only `Settings/SettingAttributes.cs` and that no module references another (Principle I).

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies.
- **Phase 2 (Foundational)**: depends on Phase 1 and blocks every story.
- **US2 (Phase 4)**: depends on Phase 2.
- **US3 (Phase 5)**: depends on US2 (T032–T037). It closes US2's gate.
- **US1 (Phase 3)**: depends on Phase 2 (snippet runner T017, site T018). It can run in parallel with US2 and US3.
- **US4 (Phase 6)**: depends on US1 (it reuses the `install` procedure and T023).
- **US5 (Phase 7)**: depends on Phase 2. T061 needs T004. T059 is independent of the other stories.
- **US6 (Phase 8)**: depends on US1 (T023, `install` procedure).
- **US7 and US8 (Phases 9–10)**: depend on Phase 2. US7's links are best written after US2 (reference anchors) and US5 (gate refusals).
- **US9 (Phase 11)**:
  - T083 depends on US2, US3 and US5 (`settings check`, `upgrade-notes`);
  - T084 depends on US1, US4 and US6 (procedures);
  - T085 depends on T081.
- **Polish (Phase 12)**: after all stories. T089 needs T050, T058, T059, T075 and T088.

### Within each story

Tests first and failing, then implementation, then the CI wiring, then the checkpoint.

### Parallel opportunities

- **Setup**: T002, T003, T004 and T005.
- **Foundational tests**: T006, T007, T008 and T009.
- **Annotating options classes**: T033, T034 and T035 touch different projects.
- **US1 and US2/US3**: can proceed side by side after Phase 2.
- **Proxy pages**: T066, T067 and T068.
- **US7 and US8**: entirely independent of each other.

## Parallel Example: User Story 2

```text
# After T032 (ReferenceWriter) lands, annotate all options classes at once:
Task: "T033 [P] [US2] Annotate Host options classes"
Task: "T034 [P] [US2] Annotate Shared.Infrastructure options classes"
Task: "T035 [P] [US2] Annotate module options classes"
# Then T036 (notes) → T037 (generate and compare) → T039 → T040 in sequence.
```

## Parallel Example: User Story 6

```text
Task: "T066 [P] [US6] nginx.md"
Task: "T067 [P] [US6] caddy.md"
Task: "T068 [P] [US6] traefik.md"
```

## Implementation Strategy

### MVP first

1. Phase 1, then Phase 2.
2. **US2 then US3**: the generated reference and its gate. This alone fixes the
   biggest current gap (undocumented and drifting settings) and is mergeable on its own.
3. **US1**: the install page, executed.
4. Stop and validate: quickstart Q1–Q3 and Q5 for `install`.

### Incremental delivery (matches plan.md, Delivery order)

| Step | Stories | Result |
|---|---|---|
| 1 | US2 + US3 | Reference generated and gated |
| 2 | US1 | Install documented and executed |
| 3 | US4 | Backup and restore |
| 4 | US5 | Upgrade guide, upgrade notes, milestone clean-up, links |
| 5 | US6 | Proxies |
| 6 | US7 + US8 | Troubleshooting and security checklist |
| 7 | US9 | Release and publish |
| 8 | Polish | Remaining operations topics moved; old files removed |

Each step leaves `main` releasable. Until step 7, the docs are complete in the repository
but not published.

### Before 0.2.0 ships

The maintainer must choose the publishing target (plan.md, Risks; research OD-14):
- make the repository public; or
- set `DOCS_PAGES_REPOSITORY` and `DOCS_DEPLOY_KEY` to point at a public repository with Pages.

## Notes

- [P] tasks touch different files and depend on no incomplete task.
- Generated files are never hand-edited. Regenerate them, and the tests prove they are
  current.
- Commit after each task or logical group. Each commit message references #26.
