---

description: "Task list for 001-foundation-skeleton"
---

# Tasks: Project Foundation and Skeleton

**Input**: Design documents from `/specs/001-foundation-skeleton/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included. The specification mandates them (FR-025, FR-026, FR-039, FR-040, FR-040a) and measures them (SC-004, SC-005, SC-011, SC-012, SC-013), and constitution Principle X makes them non-optional.

**Organization**: Grouped by user story so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

Modular monolith per [plan.md](./plan.md): `src/QuickBuilder.Host/`, `src/Shared/`, `src/Modules/<Module>/`, `admin/`, `tests/`, `docs/`. Assemblies follow the `QuickBuilder.*` format.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution skeleton, build rules and empty projects. Nothing here has behaviour.

- [X] T001 Create `QuickBuilder.sln` and the directory skeleton (`src/Shared/`, `src/Modules/Sites/`, `admin/`, `tests/`, `docs/`) per plan.md
- [X] T002 [P] Add `Directory.Build.props` at repo root: `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, latest lang version
- [X] T003 [P] Add `Directory.Packages.props` at repo root with central package management, pinning every package in the research.md dependency table
- [X] T004 [P] Add `.editorconfig` at repo root so `dotnet format --verify-no-changes` passes on a clean tree
- [X] T005 [P] Add `BannedSymbols.txt` at repo root banning `M:Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters``1(System.Linq.IQueryable{``0})` and wire `Microsoft.CodeAnalysis.BannedApiAnalyzers` in `Directory.Build.props` (D-07)
- [X] T006 [P] Create `src/QuickBuilder.Host/QuickBuilder.Host.csproj` (ASP.NET Core web project, no other references yet)
- [X] T007 [P] Create `src/Shared/QuickBuilder.Shared.Kernel/QuickBuilder.Shared.Kernel.csproj` (class library, no EF Core or ASP.NET Core references)
- [X] T008 [P] Create `src/Shared/QuickBuilder.Shared.Infrastructure/QuickBuilder.Shared.Infrastructure.csproj` referencing Shared.Kernel
- [X] T009 [P] Create `src/Modules/Sites/QuickBuilder.Sites.Contracts/QuickBuilder.Sites.Contracts.csproj` (class library, no EF Core or ASP.NET Core references)
- [X] T010 [P] Create `src/Modules/Sites/QuickBuilder.Sites/QuickBuilder.Sites.csproj` referencing Shared.Kernel, Shared.Infrastructure and Sites.Contracts
- [X] T011 [P] Create `tests/QuickBuilder.UnitTests/`, `tests/QuickBuilder.IntegrationTests/`, `tests/QuickBuilder.ArchitectureTests/` and `tests/QuickBuilder.SmokeTests/` xUnit projects
- [X] T012 [P] Scaffold the admin app in `admin/` (Vue 3 + TypeScript strict + Vite + Pinia) with `admin/package.json`, `admin/tsconfig.json`, `admin/vite.config.ts`
- [X] T013 [P] Add `.github/workflows/ci.yml` running restore and build on pull requests (tests and checks are added in Phase 6)

**Checkpoint**: `dotnet build` succeeds on an empty solution with warnings as errors.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The abstractions and host composition every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Shared Kernel

- [X] T014 [P] Implement `Result` and `Result<T>` with success / expected failure (error code + message) / unexpected failure in `src/Shared/QuickBuilder.Shared.Kernel/Results/Result.cs` (FR-009)
- [X] T015 [P] Implement `Error` with a stable dotted code in `src/Shared/QuickBuilder.Shared.Kernel/Results/Error.cs`
- [X] T016 [P] Define `ICommandHandler<TCommand, TResult>` and `IQueryHandler<TQuery, TResult>` in `src/Shared/QuickBuilder.Shared.Kernel/Dispatch/Handlers.cs` (FR-006)
- [X] T017 [P] Define `IValidator<T>` and `ValidationResult` in `src/Shared/QuickBuilder.Shared.Kernel/Validation/IValidator.cs` (D-05)
- [X] T018 [P] Define `IIntegrationEvent` (id, occurredAtUtc, eventType) in `src/Shared/QuickBuilder.Shared.Kernel/Events/IIntegrationEvent.cs`
- [X] T019 [P] Define `ISiteOwned` marker carrying `SiteId` in `src/Shared/QuickBuilder.Shared.Kernel/MultiSite/ISiteOwned.cs`

### Shared Infrastructure

- [X] T020 Implement the dispatcher resolving handlers from DI in `src/Shared/QuickBuilder.Shared.Infrastructure/Dispatch/Dispatcher.cs` (depends on T016)
- [X] T021 Implement the validation decorator that runs before the handler and short-circuits on failure in `src/Shared/QuickBuilder.Shared.Infrastructure/Dispatch/ValidationDecorator.cs` (FR-008, depends on T017, T020)
- [X] T022 [P] Implement scoped `ISiteContext` with nullable `SiteId` in `src/Shared/QuickBuilder.Shared.Infrastructure/MultiSite/SiteContext.cs` (D-08)
- [X] T023 Implement the EF convention applying a global query filter to every `ISiteOwned` entity in `src/Shared/QuickBuilder.Shared.Infrastructure/MultiSite/SiteQueryFilterConvention.cs` (FR-013, depends on T019, T022)
- [X] T024 Implement the annotated cross-site read helper requiring a non-empty reason, holding the single `IgnoreQueryFilters` suppression, in `src/Shared/QuickBuilder.Shared.Infrastructure/MultiSite/CrossSiteQuery.cs` (FR-013a, D-07)
- [X] T025 [P] Implement the cache abstraction with its in-memory default and site-namespaced keys in `src/Shared/QuickBuilder.Shared.Infrastructure/Caching/` (D-09, Principle V)
- [X] T026 [P] Implement `StartupState` (`WaitingForDatabase` | `Migrating` | `MigrationsPending` | `Ready` | `Failed`) in `src/Shared/QuickBuilder.Shared.Infrastructure/Startup/StartupState.cs` (D-17; `MigrationsPending` is the state when automatic migration is disabled and migrations have not been applied yet)
- [X] T027 Implement the migration runner: bounded connection retry with backoff, per-module PostgreSQL advisory lock, forward-only apply, gated by the `Database:AutoMigrate` configuration key (default `true`), in `src/Shared/QuickBuilder.Shared.Infrastructure/Migrations/ModuleMigrationRunner.cs` (FR-023, FR-024, FR-030, D-04, depends on T026)
- [X] T112 Implement the `migrate` command-line entry point applying every module's migrations and exiting, so an operator can run migrations as a separate step, in `src/QuickBuilder.Host/Program.cs` and `src/QuickBuilder.Host/Cli/MigrateCommand.cs` (FR-023, constitution Principle II, depends on T027)
- [X] T113 [P] Integration test: with `Database:AutoMigrate=false` startup applies no migrations and readiness reports `MigrationsPending`; the `migrate` command then applies them and readiness turns healthy, in `tests/QuickBuilder.IntegrationTests/Startup/ManualMigrationTests.cs` (FR-023) — **superseded by T125**
- [X] T028 [P] Define the module registration contract `IModule` with its single entry point in `src/Shared/QuickBuilder.Shared.Infrastructure/Modules/IModule.cs` (FR-004)

### Host Composition

- [X] T029 Wire composition root in `src/QuickBuilder.Host/Program.cs`: DI, module registration, middleware order (forwarded headers → base path → routing → site resolution) — composition only, no business logic (FR-001)
- [X] T030 [P] Implement strongly-typed options with `ValidateDataAnnotations().ValidateOnStart()` in `src/QuickBuilder.Host/Configuration/` and author `src/QuickBuilder.Host/appsettings.json` with documented keys and safe defaults (FR-028, FR-029, D-16)
- [X] T031 [P] Add forwarded-headers and configurable base-path middleware in `src/QuickBuilder.Host/Middleware/ProxyMiddleware.cs`, trusting no proxy by default (FR-012, D-12)
- [X] T032 Implement `/health/live` and `/health/ready` reading `StartupState` (including `MigrationsPending`) plus a PostgreSQL check in `src/QuickBuilder.Host/Health/HealthEndpoints.cs` (FR-027, depends on T026, T027)
- [X] T033 [P] Reserve the `/api`, `/health` and `/admin` route prefixes ahead of the public catch-all in `src/QuickBuilder.Host/Routing/RoutePrefixes.cs` (D-11 collision rule)
- [X] T034 [P] Add global exception handling returning RFC 9457 problem details with no internal detail in `src/QuickBuilder.Host/Middleware/ExceptionHandlingMiddleware.cs` (FR-034)
- [X] T035 [P] Configure Serilog structured JSON logging to stdout, with global and per-component minimum levels bound from `src/QuickBuilder.Host/appsettings.json`, in `src/QuickBuilder.Host/Program.cs` (FR-031, FR-033; tracing, correlation and redaction arrive in US5)

### Test Foundations

- [X] T036 Implement the Testcontainers PostgreSQL fixture and `WebApplicationFactory` base in `tests/QuickBuilder.IntegrationTests/Infrastructure/DatabaseFixture.cs` — no in-memory providers (D-14, Principle X) — **marked complete in error during implementation; actually delivered by T117**
- [X] T037 [P] Add unit tests for `Result`, the validation decorator and the dispatcher in `tests/QuickBuilder.UnitTests/Dispatch/` — **superseded by T127**

**Checkpoint**: the host starts, `/health/live` returns 200, `/health/ready` reports `WaitingForDatabase` with no database. User story work can begin.

---

## Phase 3: User Story 1 - Operator brings the product up from nothing (Priority: P1) 🎯 MVP

**Goal**: One command takes an operator from nothing to a loaded admin shell, an empty site list, a created site and a public request that resolves to it.

**Independent Test**: On a clean machine with only a container runtime, run the documented command; containers start, both health endpoints report healthy, the admin shell renders in a browser (quickstart V1–V6).

### Tests for User Story 1 ⚠️

> Write these first and confirm they fail before implementing.

- [X] T038 [P] [US1] Integration test: cold start on an empty database reaches ready within the configured cold-start ceiling, `GET /api/v1/sites` returns `[]`, in `tests/QuickBuilder.IntegrationTests/Sites/FreshInstallTests.cs` (SC-001, SC-002, FR-014a). Assert a generous ceiling in CI (120s) to catch regressions without flaking on shared runners; SC-002's real 60s budget is measured against the reference environment in T109. — **superseded by T118**
- [X] T039 [P] [US1] Integration test: create site → `GET /` with that Host header resolves to it, in `tests/QuickBuilder.IntegrationTests/Sites/CreateAndResolveTests.cs` (SC-001a, US1 scenario 3) — **superseded by T118**
- [X] T040 [P] [US1] Integration test: unknown Host returns an identical 404 body whether zero or many sites exist, in `tests/QuickBuilder.IntegrationTests/Sites/UnknownHostTests.cs` (FR-014) — **superseded by T118**
- [X] T041 [P] [US1] Integration test: duplicate host name is rejected with `sites.host_name_taken`, in `tests/QuickBuilder.IntegrationTests/Sites/HostNameConflictTests.cs` — **superseded by T118**
- [X] T042 [P] [US1] Integration test: readiness turns unhealthy while the database is down and recovers without restart, in `tests/QuickBuilder.IntegrationTests/Health/ReadinessTests.cs` (SC-010, US1 scenario 6) — **superseded by T118**
- [X] T043 [P] [US1] Integration test: missing required configuration aborts startup naming the key, in `tests/QuickBuilder.IntegrationTests/Startup/ConfigurationValidationTests.cs` (FR-028, US1 scenario 5) — **superseded by T118**

### Sites Module — Domain and Data

- [X] T044 [P] [US1] Define `SiteDto` and `ISiteDirectory` in `src/Modules/Sites/QuickBuilder.Sites.Contracts/` (module's public surface, FR-005)
- [X] T045 [P] [US1] Implement the `Site` entity (id, name, timestamps) in `src/Modules/Sites/QuickBuilder.Sites/Domain/Site.cs` per data-model.md
- [X] T046 [P] [US1] Implement the `SiteHostName` entity implementing `ISiteOwned` in `src/Modules/Sites/QuickBuilder.Sites/Domain/SiteHostName.cs` per data-model.md
- [X] T047 [US1] Implement `SitesDbContext` with `HasDefaultSchema("sites")`, its own migrations history table and the site query filter convention, in `src/Modules/Sites/QuickBuilder.Sites/Infrastructure/SitesDbContext.cs` (FR-022, depends on T023, T045, T046)
- [X] T048 [US1] Add the initial migration creating the `citext` extension, `sites.sites` and `sites.site_host_names` with the global unique index on `host_name`, in `src/Modules/Sites/QuickBuilder.Sites/Infrastructure/Migrations/` (depends on T047)

### Sites Module — Feature Slices

- [X] T049 [US1] Implement the `CreateSite` slice (controller at `POST /api/v1/sites`, request/response, command, handler, validator) in `src/Modules/Sites/QuickBuilder.Sites/Features/CreateSite/` per contracts/admin-api.yaml (FR-007, FR-008, FR-010a)
- [X] T050 [P] [US1] Implement the `ListSites` slice at `GET /api/v1/sites` using the annotated cross-site helper with a recorded reason, in `src/Modules/Sites/QuickBuilder.Sites/Features/ListSites/` (FR-013a)
- [X] T051 [P] [US1] Implement the `GetSite` slice at `GET /api/v1/sites/{siteId}` in `src/Modules/Sites/QuickBuilder.Sites/Features/GetSite/` (FR-011b — site named explicitly, never inferred)
- [X] T052 [P] [US1] Implement the `AddHostName` slice at `POST /api/v1/sites/{siteId}/host-names` in `src/Modules/Sites/QuickBuilder.Sites/Features/AddHostName/`
- [X] T053 [US1] Implement the `ResolveCurrentSite` public-surface slice at `GET /` returning the resolved site, in `src/Modules/Sites/QuickBuilder.Sites/Features/ResolveCurrentSite/` (FR-011a)
- [X] T054 [US1] Implement `SitesModule.AddSitesModule()` registering the context, handlers, validators and application part, in `src/Modules/Sites/QuickBuilder.Sites/SitesModule.cs` (FR-004, depends on T047–T053)
- [X] T055 [US1] Implement host-name-to-site resolution middleware on the public surface, backed by the cache and evicting on site or host-name change, in `src/QuickBuilder.Host/Middleware/SiteResolutionMiddleware.cs` (FR-011a, FR-014, D-09, depends on T022, T025, T054)

### Admin Shell

- [X] T056 [P] [US1] Build the shell frame, client-side router and SPA fallback route registration in `admin/src/app/` and `src/QuickBuilder.Host/Routing/` (FR-042, FR-043)
- [X] T057 [P] [US1] Set up `vue-i18n` with one locale and the lookup convention in `admin/src/i18n/` (FR-045a, D-13)
- [X] T058 [US1] Build the sites list and create-site screens with loading, empty and error states, calling `/api/v1/sites`, in `admin/src/features/sites/` (FR-044, FR-045, depends on T056, T057)
- [X] T059 [P] [US1] Add security headers and asset cache policy (`index.html` no-store, hashed assets immutable) in `src/QuickBuilder.Host/Middleware/SecurityHeadersMiddleware.cs` (FR-046, D-11)

### Packaging

- [X] T060 [US1] Write the multi-stage `Dockerfile` (Node builds `admin/` into `wwwroot/admin` → SDK publish → aspnet runtime as non-root `app`) (FR-035, FR-036, D-15)
- [X] T061 [US1] Write the reference `docker-compose.yml` publishing the app on `127.0.0.1:8080` with an inline warning at the binding, PostgreSQL with a named volume, and health probes (FR-037, FR-037a, FR-038)
- [X] T062 [US1] Add a smoke test booting the built image with the reference compose and running health → create site → list → resolve public host, in `tests/QuickBuilder.SmokeTests/` (Principle X, plan exception E-3) — **superseded by T123**

**Checkpoint**: User Story 1 is complete — `docker compose up` to a working admin shell, independently demonstrable.

---

## Phase 4: User Story 2 - Developer adds a feature by following the worked example (Priority: P2)

**Goal**: A developer can add a feature or a whole module by copying the `Sites` example, touching nothing outside their own module.

**Independent Test**: Hand the repository and the contributor document to someone unfamiliar with it; they add one read and one write operation to `Sites` without editing files outside that module (quickstart "Adding a feature").

- [X] T063 [P] [US2] Write `docs/contributing.md` covering local run, adding a feature slice and adding a module, referencing `Sites` as the worked example (FR-047) — **superseded by T128**
- [X] T064 [P] [US2] Link `docs/contributing.md` to [contracts/module-contract.md](./contracts/module-contract.md) as the canonical module contract, and cite that same path from a file header comment in `tests/QuickBuilder.ArchitectureTests/` — do not copy the file — **superseded by T130**
- [X] T065 [US2] Verify every `Sites` feature folder holds its entry point, request/response, command or query, handler, validator and tests together; relocate anything that does not, across `src/Modules/Sites/QuickBuilder.Sites/Features/` (FR-010, US2 scenario 1) — **verified by FeatureSliceTests.Every_feature_keeps_its_parts_in_one_folder**
- [X] T066 [US2] Confirm no `Sites` controller contains a business rule or data access; move any found into its handler, across `src/Modules/Sites/QuickBuilder.Sites/Features/` (FR-007, US2 scenario 3) — **verified by FeatureSliceTests.Controllers_are_given_nothing_but_the_dispatcher**
- [X] T067 [P] [US2] Integration test: a validation failure names the invalid inputs and the handler never runs, in `tests/QuickBuilder.IntegrationTests/Sites/ValidationTests.cs` (FR-008, US2 scenario 4) — **superseded by T126**
- [X] T068 [US2] Test proving a second module registers with one host line and no edit to `Sites`, using a throwaway module fixture in `tests/QuickBuilder.IntegrationTests/Modules/SecondModuleTests.cs` (SC-009, US2 scenario 5) — **superseded by T129**
- [X] T069 [P] [US2] Test proving a new feature folder becomes reachable over HTTP with no edit to host or shared code, in `tests/QuickBuilder.IntegrationTests/Modules/NewFeatureSliceTests.cs` (US2 scenario 2) — **superseded by T126**

**Checkpoint**: the conventions are documented and mechanically demonstrated, not just asserted.

---

## Phase 5: User Story 3 - Work in one module triggers work in another without coupling them (Priority: P3)

**Goal**: Creating a site records an event atomically; a background dispatcher delivers it at least once with effects landing exactly once.

**Independent Test**: Create a site, confirm the event and the site commit together, confirm delivery, force redelivery and confirm one effect (quickstart V7).

### Tests for User Story 3 ⚠️

- [X] T070 [P] [US3] Integration test: site row and outbox row persist together; a failed creation persists neither, in `tests/QuickBuilder.IntegrationTests/Outbox/AtomicityTests.cs` (FR-015, US3 scenarios 1–2) — **superseded by T119**
- [X] T071 [P] [US3] Integration test: a pending event survives restart and is still delivered, in `tests/QuickBuilder.IntegrationTests/Outbox/DurabilityTests.cs` (FR-016, US3 scenario 3) — **superseded by T119**
- [X] T072 [P] [US3] Integration test: forced redelivery produces exactly one effect and is recorded as a no-op, in `tests/QuickBuilder.IntegrationTests/Outbox/IdempotencyTests.cs` (FR-017, SC-005, US3 scenario 4) — **superseded by T119**
- [X] T073 [P] [US3] Integration test: an always-failing handler backs off, goes terminal after `MaxAttempts`, and does not block other events, in `tests/QuickBuilder.IntegrationTests/Outbox/RetryTests.cs` (FR-018, US3 scenario 5) — **superseded by T119**

### Implementation

- [X] T074 [P] [US3] Define `SiteCreated` in `src/Modules/Sites/QuickBuilder.Sites.Contracts/Events/SiteCreated.cs` per contracts/integration-events.md
- [X] T075 [P] [US3] Define `IIntegrationEventHandler<TEvent>` and the outbox store abstraction in `src/Shared/QuickBuilder.Shared.Infrastructure/Outbox/`
- [X] T076 [US3] Map `sites.outbox_messages` and `sites.processed_events` on `SitesDbContext` and add the migration with the partial pending index and the retention index, in `src/Modules/Sites/QuickBuilder.Sites/Infrastructure/` (D-01, D-03, depends on T047)
- [X] T077 [US3] Implement the dispatcher hosted service: `FOR UPDATE SKIP LOCKED` batch claim, handler timeout, completion, backoff on failure, terminal failure after `MaxAttempts`, in `src/Shared/QuickBuilder.Shared.Infrastructure/Outbox/OutboxDispatcher.cs` (FR-016, FR-018, FR-019, D-02, depends on T075, T076)
- [X] T078 [US3] Implement the idempotency wrapper inserting `processed_events` in the handler's transaction and treating a duplicate key as already-done, in `src/Shared/QuickBuilder.Shared.Infrastructure/Outbox/IdempotentDispatch.cs` (FR-017, D-03, depends on T077)
- [X] T079 [US3] Write `SiteCreated` into the outbox inside the `CreateSite` transaction in `src/Modules/Sites/QuickBuilder.Sites/Features/CreateSite/CreateSiteHandler.cs` (FR-015, depends on T049, T076)
- [X] T080 [P] [US3] Implement the retention sweep removing completed events after the configured period, in `src/Shared/QuickBuilder.Shared.Infrastructure/Outbox/OutboxRetentionService.cs` (FR-020)
- [X] T081 [P] [US3] Add outbox configuration keys (poll interval, batch size, handler timeout, max attempts, retention, enabled) to `src/QuickBuilder.Host/appsettings.json` with the defaults in contracts/integration-events.md

**Checkpoint**: cross-module side effects work end to end and are proven idempotent.

---

## Phase 6: User Story 4 - Boundary violations are caught automatically before merge (Priority: P4)

**Goal**: The pipeline fails any pull request that breaks a boundary rule, and names the rule.

**Independent Test**: Introduce one deliberate violation per rule on a throwaway branch; every one fails the build with a message identifying the rule (quickstart V9).

- [X] T082 [P] [US4] Architecture test: a module must not reference another module's implementation, only its `.Contracts`, in `tests/QuickBuilder.ArchitectureTests/ModuleIsolationTests.cs` (FR-025, US4 scenario 1)
- [X] T083 [P] [US4] Architecture test: `*.Contracts` references no EF Core, no ASP.NET Core and no module implementation, in `tests/QuickBuilder.ArchitectureTests/ContractsPurityTests.cs`
- [X] T084 [P] [US4] Architecture test: `QuickBuilder.Host` contains no `DbContext`, handler or domain type, in `tests/QuickBuilder.ArchitectureTests/HostCompositionTests.cs` (US4 scenario 2)
- [X] T085 [P] [US4] Architecture test: `Shared.Kernel` and `Shared.Infrastructure` reference no module, in `tests/QuickBuilder.ArchitectureTests/SharedIndependenceTests.cs`
- [X] T086 [P] [US4] Architecture test: public types in a module implementation are limited to its registration entry point and controllers, in `tests/QuickBuilder.ArchitectureTests/InternalVisibilityTests.cs` (US4 scenario 3)
- [X] T087 [P] [US4] Architecture test: every entity with a `SiteId` implements `ISiteOwned`, in `tests/QuickBuilder.ArchitectureTests/SiteScopingTests.cs` (FR-013)
- [X] T088 [US4] Add one deliberately violating fixture per rule above, proving each rule actually fires, in `tests/QuickBuilder.ArchitectureTests/Fixtures/` (SC-004) — **superseded by T122**
- [X] T089 [US4] Extend `.github/workflows/ci.yml` to run unit, integration (Testcontainers PostgreSQL), architecture and admin test suites, plus `dotnet format --verify-no-changes`, all failing the build (FR-026, FR-039, FR-040, US4 scenarios 4–5) — **already satisfied: `ci.yml` runs all four suites plus the format check**
- [X] T090 [US4] Add the container image build to `.github/workflows/ci.yml` for the runner architecture only, and report status to the pull request (FR-041, D-15) — **already satisfied: the `image` job builds the container**
- [X] T091 [P] [US4] Add the coverlet coverage gate for the critical paths named by Principle X to `.github/workflows/ci.yml` — **superseded by T131**

**Checkpoint**: the rules that hold the architecture together are enforced by the build.

---

## Phase 7: User Story 5 - Operator diagnoses a running instance (Priority: P5)

**Goal**: One identifier follows a request end to end through structured logs, with nothing sensitive in them.

**Independent Test**: Issue a failing request and find every log entry for it by correlation identifier alone; confirm no credentials, personal data or content bodies appear (quickstart V13).

- [X] T092 [US5] Add the site and module log enrichers and confirm no credentials, secrets, personal data or content bodies reach the output, in `src/QuickBuilder.Host/Observability/LogEnrichment.cs` (FR-031, FR-032; the Serilog stack itself is built in T035)
- [X] T093 [US5] Register OpenTelemetry tracing and metrics with ASP.NET Core and Npgsql instrumentation and **no exporter unless configured**, in `src/QuickBuilder.Host/Observability/Telemetry.cs` (FR-031b, D-10)
- [X] T094 [US5] Enrich every log entry with `TraceId` and `SpanId` from `Activity.Current` so the correlation identifier is the trace identifier, in `src/QuickBuilder.Host/Observability/TracingEnricher.cs` (FR-031a, depends on T093)
- [X] T096 [US5] Start an `Activity` per outbox dispatch and carry the originating `trace_id` from the outbox row, in `src/Shared/QuickBuilder.Shared.Infrastructure/Outbox/OutboxDispatcher.cs` (D-10, depends on T077, T093)
- [X] T097 [P] [US5] Return `traceId` in problem-details responses in `src/QuickBuilder.Host/Middleware/ExceptionHandlingMiddleware.cs` (FR-034, contracts/admin-api.yaml)
- [X] T099 [P] [US5] Test: a request's entries share one trace identifier and no credentials, personal data or content bodies appear in output, in `tests/QuickBuilder.IntegrationTests/Observability/LoggingTests.cs` (FR-032, SC-008, US5 scenarios 1–2, 4)
- [X] T100 [P] [US5] Test: a default installation makes zero outbound network requests from startup through site creation, in `tests/QuickBuilder.IntegrationTests/Observability/NoEgressTests.cs` (SC-014, FR-031b)

**Checkpoint**: all five user stories are independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T101 [P] Concurrency test: several migrators against one database apply migrations exactly once, in `tests/QuickBuilder.IntegrationTests/Concurrency/MigrationLockTests.cs` (FR-024, FR-040a, SC-012) — **superseded by T120**
- [X] T102 [P] Concurrency test: several dispatchers claim each event exactly once and a dropped claim becomes eligible again, in `tests/QuickBuilder.IntegrationTests/Concurrency/OutboxClaimTests.cs` (FR-019, FR-040a, SC-012) — **superseded by T120**
- [X] T103 [P] Site-scoping test: with two sites, no public query returns the other site's host names, and each cross-site bypass has its own test, in `tests/QuickBuilder.IntegrationTests/Sites/SiteScopingTests.cs` (SC-011, FR-013a) — **superseded by T121**
- [X] T104 [P] Add the axe-core accessibility checks and the `@intlify/eslint-plugin-vue-i18n` `no-raw-text` rule to `admin/` and its lint/test scripts (FR-045b, SC-013, D-13) — **superseded by T124**
- [X] T105 [P] Write `docs/admin-accessibility-baseline.md` recording the baseline later screens inherit (FR-045b)
- [X] T106 [P] Write `docs/configuration.md` documenting every key, its default and its environment-variable override (FR-029)
- [X] T107 [P] Create the empty `docs/contracts/` directory where later content schemas will live (Principle VI)
- [X] T108 Test: restart against existing data loses nothing and needs no manual step, in `tests/QuickBuilder.IntegrationTests/Startup/RestartTests.cs` (SC-006, US1 scenario 4) — **superseded by T125**
- [X] T111 [P] Integration test: start against an already-migrated database reaches ready within the configured warm-start ceiling, in `tests/QuickBuilder.IntegrationTests/Startup/WarmStartTests.cs` (SC-002) — **superseded by T125**
- [X] T109 Run every scenario in [quickstart.md](./quickstart.md) (V1–V13) against the reference deployment and record the results — **superseded by T132**
- [X] T110 Verify the full pipeline completes under 10 minutes and fails on any warning, formatting deviation or failing test (SC-007) — **superseded by T132**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: depends on Setup — **blocks every user story**
- **US1 (Phase 3)**: depends on Foundational. Delivers the MVP.
- **US2 (Phase 4)**: depends on US1 — the worked example it documents is the `Sites` module built in US1
- **US3 (Phase 5)**: depends on US1 for `CreateSite` (T079 extends it) — otherwise independent
- **US4 (Phase 6)**: depends on Foundational only. Can run in parallel with US1–US3; its fixtures grow as modules appear.
- **US5 (Phase 7)**: depends on Foundational; T096 additionally needs US3's dispatcher
- **Polish (Phase 8)**: depends on the stories whose behaviour it verifies

### Deviation from the template's independence ideal

US2, US3 and US5 each have one genuine dependency on earlier work (the worked example, the `CreateSite` handler, the dispatcher). This is inherent to a foundation feature — the later stories are *about* the thing the earlier story builds — and each remains independently **testable** once its phase completes. Only US4 is fully independent of the others.

### Within Each User Story

- Tests first; confirm they fail before implementing
- Entities → `DbContext` and migration → handlers → controllers → middleware
- Backend before the admin screens that call it

### Parallel Opportunities

- Phase 1: T002–T013 all parallel after T001
- Phase 2: T014–T019 parallel; T022, T025, T026, T028 parallel; T030, T031, T033, T034, T035 parallel; T113 parallel once T112 lands
- Phase 3: all six tests (T038–T043) parallel; T044–T046 parallel; T050–T052 parallel; T056, T057, T059 parallel
- Phase 5: all four tests (T070–T073) parallel; T074, T075 parallel
- Phase 6: all six architecture tests (T082–T087) parallel
- Phase 8: T101–T107 and T111 parallel
- **Across phases**: US4 (Phase 6) can be staffed alongside Phase 3 as soon as Foundational lands

---

## Parallel Example: User Story 1

```bash
# All six User Story 1 tests together (they must fail first):
Task: "Integration test: cold start reaches ready with an empty site list in tests/QuickBuilder.IntegrationTests/Sites/FreshInstallTests.cs"
Task: "Integration test: create site then resolve by Host header in tests/QuickBuilder.IntegrationTests/Sites/CreateAndResolveTests.cs"
Task: "Integration test: unknown Host returns an identical 404 in tests/QuickBuilder.IntegrationTests/Sites/UnknownHostTests.cs"
Task: "Integration test: duplicate host name is rejected in tests/QuickBuilder.IntegrationTests/Sites/HostNameConflictTests.cs"
Task: "Integration test: readiness follows database availability in tests/QuickBuilder.IntegrationTests/Health/ReadinessTests.cs"
Task: "Integration test: bad configuration aborts startup in tests/QuickBuilder.IntegrationTests/Startup/ConfigurationValidationTests.cs"

# All three domain and contract pieces together:
Task: "Define SiteDto and ISiteDirectory in src/Modules/Sites/QuickBuilder.Sites.Contracts/"
Task: "Implement the Site entity in src/Modules/Sites/QuickBuilder.Sites/Domain/Site.cs"
Task: "Implement the SiteHostName entity in src/Modules/Sites/QuickBuilder.Sites/Domain/SiteHostName.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup
2. Phase 2: Foundational — blocks everything
3. Phase 3: User Story 1
4. **STOP and VALIDATE**: run quickstart V1–V6
5. Demo: `docker compose up` to a working admin shell with a created site resolving publicly

### Incremental Delivery

1. Setup + Foundational → the host starts and reports its state
2. + US1 → **MVP**: deployable, demonstrable, the spec's primary promise
3. + US4 → the boundaries are now enforced; safe to add people
4. + US2 → the conventions are documented and provably copyable
5. + US3 → cross-module side effects available to every later module
6. + US5 → diagnosable in production
7. + Polish → concurrency, scoping, accessibility and documentation proofs

Taking US4 second is a deliberate reordering of the spec's priorities: the architecture tests are cheap once the foundation exists, and every later task is safer with them in place.

### Parallel Team Strategy

After Foundational completes:

- Developer A: US1 (Phase 3) — the critical path
- Developer B: US4 (Phase 6) — fully independent, fixtures grow as A's module lands
- Developer C: admin shell tasks T056–T059, coordinating with A on the API shape in contracts/admin-api.yaml

US2, US3 and US5 join once their prerequisites land.

---

## Notes

- `[P]` means a different file and no dependency on incomplete work
- Every task names an exact path; no task requires reading another task to be actionable
- Verify each test fails before implementing against it
- Commit per task or per logical group
- **Prerequisite outside this list**: GitHub issue #1 must exist, or the existing spec-ids check fails the pull request

---

## Phase 9: Convergence

**Purpose**: Close the gap between the specified scope and the current codebase, found by
`/speckit-converge` on 2026-09-22. Ordered CRITICAL first.

**Context**: the backend, admin shell, container image and reference deployment are built
and were verified by hand against a running stack. Almost nothing is verified by an
automated test, and three pipeline gates currently fail on the tree as it stands — so CI
would go red on the first pull request.

### Pipeline gates that currently fail

- [X] T114 CRITICAL Fix the `dotnet format --verify-no-changes` failure per FR-039 (contradicts): IDE1006 fires on the `private static readonly` fields in `tests/QuickBuilder.ArchitectureTests/ModuleBoundaryTests.cs` because the `_`-prefix naming rule in `.editorconfig` applies to them. Scope the rule to instance fields or rename the fields.
- [X] T115 CRITICAL Fix `npm run lint` per FR-039 and SC-013 (contradicts): `admin/eslint.config.js` does not set `parserOptions.parser` to the TypeScript parser, so `admin/src/features/sites/SitesView.vue` fails to parse at the generic on line 14 — which also means `no-raw-text` is not actually inspecting that file. Additionally 33 `no-unused-keys` warnings fail the build under `--max-warnings 0`; resolve the keys or downgrade that specific rule with a recorded reason.
- [X] T116 CRITICAL Make `npm run test` exit zero per FR-039 (contradicts): `vitest run` currently reports "No test files found" and exits non-zero. Satisfied naturally by T124; until then do not paper over it with `passWithNoTests`.

### Test infrastructure

- [X] T117 Build the Testcontainers PostgreSQL fixture and `WebApplicationFactory` base in `tests/QuickBuilder.IntegrationTests/Infrastructure/DatabaseFixture.cs` per Constitution X and plan D-14 (missing). T036 was marked complete in error — no Testcontainers or `WebApplicationFactory` reference exists anywhere in `tests/`. Everything below depends on this.

### Behaviour currently verified only by hand

- [X] T118 [P] Write the six User Story 1 integration tests per US1/AC1–AC6, SC-001, SC-002, SC-010 (missing): cold start reaches ready with an empty site list, create site then resolve by Host header, unknown Host returns an identical 404, duplicate host name is rejected, readiness follows database availability, invalid configuration aborts startup. Files per T038–T043.
- [X] T119 [P] Write the four outbox integration tests per FR-015–FR-018 and SC-005 (missing): atomicity, durability across restart, exactly-once effect on forced redelivery, and backoff to terminal failure. Files per T070–T073.
- [X] T120 [P] Write the concurrency tests per FR-019, FR-024, FR-040a and SC-012 (missing): several migrators apply migrations exactly once and several dispatchers claim each event exactly once, against one real database in one test process. Files per T101–T102.
- [X] T121 [P] Write the site-scoping tests per FR-013, FR-013a and SC-011 (missing): with two sites, no public query returns the other site's host names, and each `AcrossAllSites` call site has its own dedicated test. File per T103.
- [X] T122 Add one deliberately-violating fixture per architecture rule in `tests/QuickBuilder.ArchitectureTests/Fixtures/` per SC-004 (missing): six rules pass today, but none is proven to fail when broken, which is what SC-004 actually measures.
- [X] T123 [P] Write the smoke test booting the built image with the reference compose file per Constitution X and plan exception E-3 (missing): `tests/QuickBuilder.SmokeTests/` contains no test.
- [X] T124 [P] Write the admin accessibility and rendering tests in `admin/tests/` per FR-045b and SC-013 (missing): axe-core checks against the shell and the sites screen, plus loading, empty and error state assertions. Resolves T116.
- [X] T125 [P] Write the startup tests per SC-006, SC-002 and FR-023 (missing): restart preserves data, warm start reaches ready within its ceiling, and `Database:AutoMigrate=false` leaves readiness reporting `MigrationsPending` until the `migrate` command runs. Files per T108, T111, T113.
- [X] T126 [P] Write the User Story 2 verification tests per FR-008 and US2/AC2–AC4 (missing): a validation failure names the invalid inputs and the handler never runs, a new feature folder is reachable with no host edit, and no controller holds a business rule. Files per T067, T069.
- [X] T127 [P] Write unit tests for `Result`, the validation decorator and the dispatcher in `tests/QuickBuilder.UnitTests/Dispatch/` per T037 (missing).

### Documentation and proofs

- [X] T128 Write `docs/contributing.md` per FR-047 (missing): how to run locally, how to add a feature slice, how to add a module, each referencing the `Sites` module as the worked example. Include the administrative/public surface split, which FR-011 requires to be documented and which nothing currently documents.
- [X] T129 Write the test proving a second module registers with one line in the host and no edit to `Sites` per SC-009 and US2/AC5 (missing), in `tests/QuickBuilder.IntegrationTests/Modules/SecondModuleTests.cs`.
- [X] T130 [P] Link `docs/contributing.md` to `specs/001-foundation-skeleton/contracts/module-contract.md` as the canonical module contract per T064 (missing), and cite that path from a header comment in `tests/QuickBuilder.ArchitectureTests/`.

### Pipeline completeness

- [X] T131 Add the coverage threshold gate to `.github/workflows/ci.yml` per Constitution X (partial): coverage is collected and uploaded, but no threshold is enforced, so the 80% critical-path requirement is not actually a gate.
- [X] T132 Run every quickstart scenario V1–V13 against the reference deployment and record the results per SC-007 and quickstart.md (missing), including the measured pipeline duration against the under-10-minutes budget.

**Checkpoint**: CI is green, and every behaviour verified by hand during implementation is
covered by a test that would catch its regression.
