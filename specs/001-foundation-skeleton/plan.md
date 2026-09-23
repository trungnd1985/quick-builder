# Implementation Plan: Project Foundation and Skeleton

**Branch**: `001-foundation-skeleton` | **Date**: 2026-09-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-foundation-skeleton/spec.md`

**Issue**: #1

## Summary

Stand up a deployable modular-monolith skeleton: a host that only composes, a shared
kernel and shared infrastructure, and one real minimal module (`Sites`) that serves as
the worked example every later feature copies. An operator runs one command and reaches
an admin shell; a developer adds a feature by copying a folder; the boundaries that make
this possible are enforced by the build rather than by review.

The technical approach is fixed by the constitution and refined by
[research.md](./research.md): per-module schemas and outbox tables on the module's own
`DbContext` so events persist atomically with their state change; `SKIP LOCKED` claiming
and a per-module processed-events table to turn at-least-once delivery into
effectively-once effects; advisory-lock-guarded startup migrations; site context resolved
by middleware on the public surface only, with EF global query filters doing the scoping
and a banned-API analyzer making every cross-site read deliberate; OpenTelemetry supplying
the trace id that Serilog uses as the log correlation id, with no exporter registered so
nothing leaves the process.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5.x (strict) for the admin app

**Primary Dependencies**: ASP.NET Core MVC controllers (attribute routing, no Minimal
APIs), EF Core 10 + Npgsql, Serilog, OpenTelemetry (no exporter), NetArchTest.Rules,
BannedApiAnalyzers, Testcontainers, xUnit; Vue 3 (Composition API) + Vite + Pinia +
vue-i18n. Command/query dispatch, validation and the outbox are in-house — see D-05.

**Storage**: PostgreSQL 16+, one database, one schema per module (`sites`), `jsonb`
reserved for later content modules (unused in this feature)

**Testing**: xUnit + `WebApplicationFactory` + `Testcontainers.PostgreSql` (no in-memory
providers); coverage collected by coverlet and enforced by a ReportGenerator merge plus an
explicit threshold assertion — **not** by coverlet's own `Threshold` setting, which the
`XPlat Code Coverage` collector silently ignores; vitest + @testing-library/vue + axe-core for the admin
shell

**Target Platform**: Linux containers (amd64 locally and in CI; multi-arch publishing
deferred to the release spec)

**Project Type**: Web application — modular-monolith backend plus a Vue admin SPA served
by the same process

**Performance Goals**: ready within 60s of cold start on an empty database and 30s on a
migrated one (SC-002); full pipeline under 10 minutes (SC-007). No per-request latency
target in this feature — the constitution's render budgets attach to the Rendering
module, which is out of scope.

**Constraints**: zero outbound network requests by default (SC-014); non-root container;
reference deployment bound to loopback (FR-037a); warnings-as-errors; offline-capable

**Scale/Scope**: 5 backend projects + 4 test projects + 1 admin app; one module; 2 admin
screens; expected site count in the tens, outbox throughput in the low hundreds of events
per minute (sizing deferred from clarification — see Deferred below)

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design (result at the end of this
section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** Host composes only; `Shared.Kernel` + `Shared.Infrastructure`; `Sites` ships as `QuickBuilder.Sites.Contracts` + `QuickBuilder.Sites`; code under `Features/` by slice; in-house `ICommandHandler`/`IQueryHandler`; handlers use the module `DbContext` directly; no repositories; per-module schema and migration history; outbox for cross-module effects; NetArchTest enforces all of it. Rich domain model is required only for `Pages`/`Cms`/`Rendering`, so `Sites` uses simple handlers — explicitly permitted. |
| II | Easy to Install, Operate and Upgrade | **Complies.** One command to a working admin (SC-001); config from `appsettings.json` with env overrides and fail-fast validation (D-16); migrations automatic at startup with a documented opt-out (FR-023); fully offline (SC-014); `/health/live` and `/health/ready` (D-17). No setup wizard — it is out of scope for this feature, so "no default credentials" is satisfied vacuously: there are no credentials at all. See exception E-1. |
| III | Upgrades Never Lose Data | **N/A this feature** — no prior release exists to upgrade from. FR-023 makes migrations forward-only and re-runnable, and SC-006 proves a stop/start loses nothing, which is the part of this principle that can be exercised now. The snapshot-upgrade test arrives with the first release. |
| IV | Pluggable Infrastructure | **Complies with a recorded narrowing.** PostgreSQL is the only required dependency; background jobs are database-backed with no broker; the cache abstraction ships with its in-memory default. The media storage abstraction is *not* built — no consumer exists. See E-2. |
| V | Multi-Site Within One Installation | **Complies.** `SiteId` on every site-owned record, enforced by global query filters (D-08); host-header resolution (FR-011a); proxy-aware (D-12); bypasses require the annotated helper and a dedicated test (D-07). Per-site permissions are out of scope with authentication. Cache keys are namespaced by site. |
| VI | Content Schemas Are Versioned Contracts | **N/A this feature** — no content schemas exist yet. `docs/contracts/` is created by this feature as the location later schemas land in. |
| VII | Draft and Published Are Separate Worlds | **N/A this feature** — no content. |
| VIII | Rendering Is Server-Side, Cached, Invalidated | **N/A this feature** — rendering is out of scope. The public surface carries one placeholder endpoint that proves site resolution and nothing more. |
| IX | Secure by Default | **Partially complies — one accepted exception.** Security headers on admin responses (FR-046), no secrets in image or repo (FR-038), no PII or bodies in logs (FR-032, SC-008), forwarded headers trusted from configured proxies only (D-12), non-root container. But the admin surface has no authentication and no authorization, which this principle otherwise requires on every endpoint. See E-1. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies with a scoped adaptation.** Integration tests via `WebApplicationFactory` + Testcontainers, no in-memory providers (D-14); coverage gate on critical paths; concurrency tests prove FR-019/FR-024 (D-14). The constitution's release smoke test names the setup wizard, page creation and publish — none exist yet, so this feature's smoke test boots the built image with the reference compose and runs health → create site → list site → resolve public host. See E-3. |
| XI | Simplicity and Incremental Delivery | **Complies.** No mediator, no repositories, no validation library, no storage abstraction without a consumer (D-05, D-09). Every new dependency is justified in research.md with its licence. No feature flag needed — the feature is demoable whole. |

**Technology Constraints**: .NET 10 with MVC controllers and no Minimal APIs; EF Core +
Npgsql as the only data access; `AsNoTracking()` and DTO projections on reads; schema
changes only through migrations; Vue 3 + TypeScript strict + Vite + Pinia with assets
served by the ASP.NET Core app; Serilog structured logging to console; OpenTelemetry
present but exporting nothing. All satisfied as planned. The editor canvas library is not
chosen here — that belongs to the editor-core spec.

**Development Workflow**: one issue → one spec folder → one branch → one PR. Spec folder
`001-foundation-skeleton` normalises to issue #1 under the existing
[spec-ids.yml](../../.github/workflows/spec-ids.yml) check, which strips leading zeros.
**Prerequisite**: GitHub issue #1 must exist and must not be a pull request, or that check
fails the PR — see Risks.

**Operator impact** (required by the workflow section): this feature introduces the entire
configuration surface (`docs/configuration.md`), the first migrations, and the reference
`docker-compose.yml`. New dependencies are listed in research.md. There are no upgrade
notes because there is no prior release.

**Post-Phase-1 re-check**: the Phase 1 design introduces no new violations. The three
exceptions below are unchanged in number and scope; the data model, contracts and
quickstart all conform to the principles above.

## Project Structure

### Documentation (this feature)

```text
specs/001-foundation-skeleton/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── admin-api.yaml
│   ├── integration-events.md
│   └── module-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

```text
QuickBuilder.slnx                       # .NET 10 XML solution format
Directory.Build.props              # warnings-as-errors, analyzers, nullable, lang version
Directory.Packages.props           # central package management
BannedSymbols.txt                  # IgnoreQueryFilters ban (D-07)
Dockerfile                         # multi-stage: node -> sdk -> aspnet runtime
docker-compose.yml                 # reference deployment, published on 127.0.0.1
.editorconfig

src/
├── QuickBuilder.Host/               # composition only: DI, middleware, routing, startup
│   ├── Program.cs
│   ├── Configuration/             # options binding + ValidateOnStart (D-16)
│   ├── Middleware/                # forwarded headers, base path, security headers
│   ├── Health/                    # live/ready endpoints, StartupState (D-17)
│   ├── appsettings.json
│   └── wwwroot/admin/             # built admin assets (produced by the Node stage)
├── Shared/
│   ├── QuickBuilder.Shared.Kernel/          # Result, Error, IIntegrationEvent, ICommand/IQuery
│   └── QuickBuilder.Shared.Infrastructure/  # dispatcher, validation decorator, site context,
│                                          # query filters, outbox runtime, cache, migrations
└── Modules/
    └── Sites/
        ├── QuickBuilder.Sites.Contracts/    # ISiteDirectory, SiteDto, SiteCreated
        └── QuickBuilder.Sites/
            ├── SitesModule.cs             # AddSitesModule() — the single entry point
            ├── Domain/                    # Site, SiteHostName
            ├── Infrastructure/            # SitesDbContext, Migrations/, outbox mapping
            └── Features/
                ├── CreateSite/            # controller action, request/response, command,
                │                          # handler, validator, tests — one folder
                ├── ListSites/
                ├── AddHostName/
                └── ResolveCurrentSite/    # the public-surface placeholder endpoint

admin/                             # Vue 3 + TS + Vite source (built into Host/wwwroot/admin)
├── src/
│   ├── app/                       # shell, router, Pinia stores
│   ├── features/sites/            # the two screens
│   ├── i18n/                      # locale files (one locale)
│   └── api/                       # generated/typed client for /api/v1
└── tests/

tests/
├── QuickBuilder.ArchitectureTests/  # NetArchTest rules (FR-025)
├── QuickBuilder.UnitTests/
├── QuickBuilder.IntegrationTests/   # WebApplicationFactory + Testcontainers
└── QuickBuilder.SmokeTests/         # boots the built image with the reference compose

docs/
├── configuration.md
├── contributing.md                # add a feature / add a module (FR-047)
├── admin-accessibility-baseline.md
└── contracts/                     # empty placeholder for later content schemas

.github/workflows/
├── spec-ids.yml                   # existing
└── ci.yml                         # new: build, test, arch, format, image build
```

**Structure Decision**: modular monolith exactly as Principle I prescribes — `src/Host`
composes, `src/Shared` holds the two shared projects, and every module lives under
`src/Modules/<Module>/` as a contracts project plus an implementation project whose
`Features/` folders are vertical slices. The admin SPA is a sibling `admin/` source tree
rather than a subfolder of the host, so the Node build stage has a clean context and the
host ships only built output. Tests are split by kind because the architecture tests and
the smoke tests have different run costs and different failure meanings.

## Complexity Tracking

Exceptions to MUST rules, recorded per the Governance section. Each carries a follow-up
that removes it.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| **E-1**: Principle IX requires server-side authorization on every endpoint; this feature ships an admin surface with no authentication or authorization at all. | Authentication and the setup wizard are explicitly out of scope for this feature (spec Out of Scope), and the skeleton has to be reachable to be demonstrable. Mitigated by binding the reference deployment to loopback only (FR-037a), so a default install is not reachable off the operator's machine. | Building an interim auth mechanism would be thrown away by the very next spec, and a temporary acknowledgement flag would be code to delete (rejected as option B during clarification). **Follow-up**: tracked as issue #2 — the Identity/Setup spec removes this exception; until it merges, the reference deployment stays loopback-bound and the documentation says so at the binding. |
| **E-2**: Principle IV names media storage among the pluggable providers; this feature ships no media storage abstraction, narrowing FR-003's wording. | Principle XI forbids abstractions no spec needs, and nothing in this feature or in `Sites` stores a file. An interface with one unused implementation would be designed against imagined requirements. | Shipping a guessed `IMediaStorage` now means the Media spec either inherits a wrong shape or breaks it. **Follow-up**: tracked as issue #3 — the Media module spec defines the abstraction against its real first consumer. |
| **E-3**: Principle X's release smoke test is specified as setup wizard → create page → publish → view; none of those exist. | Those capabilities belong to later specs. The smoke test still boots the published image with the reference compose, which is the part of the principle that can bite today. | Skipping the smoke test entirely would leave the image and compose file untested until the first release. **Follow-up**: tracked as issue #4 — each later spec extends the smoke test with its own step; the full sequence exists once Rendering merges. |
| **E-4**: Principle IV requires correct multi-instance operation once shared providers are configured. The in-memory cache cannot invalidate host-name lookups across instances, so a site created on one instance may stay unresolvable on another until its entry expires. | The cache abstraction has one real consumer and one real implementation today. A distributed provider has no spec yet, and Principle XI forbids building one speculatively. The reference deployment is single-instance, so the precondition the principle names — "once the operator configures shared cache and storage providers" — cannot be met yet. | Shipping a guessed Redis provider now means the cache spec either inherits a wrong shape or breaks it. **Follow-up**: tracked as issue #TBD — the Redis provider spec must solve cross-instance eviction before multi-instance is advertised to operators. Until then the documentation must not claim multi-instance support. |

## Risks

- **Issue #1 must exist on GitHub.** The existing spec-ids check resolves
  `001-foundation-skeleton` to issue #1 and fails the PR if that issue is missing or is a
  pull request. Nothing in the local workflow creates it.
- **Long outbox handlers hold a transaction open** (D-02). Acceptable while handlers are
  trivial; the handler timeout and batch size are configuration keys so this is
  observable rather than silent. Revisit if a handler ever needs real work.
- *(The single-instance cache limitation moved to Complexity Tracking as exception E-4.
  It is a departure from Principle IV, not merely a risk, and belongs where exceptions
  are recorded and their removal is tracked.)*
- *(Resolved.)* **Coverage gate on a skeleton can be gamed.** The concern was that an 80%
  threshold over code this small is easy to hit without testing what matters. It now sits
  alongside tests that do: the concurrency tests (SC-012), the site-scoping tests
  (SC-011), and fixtures proving each architecture rule fires when broken (SC-004).
  Coverage is at 82.47% and the gate enforces 80%, but those three remain the real gate —
  coverage only stops whole areas going dark.
  <br>A separate lesson worth keeping: the first version of this gate could not fail at
  all, because coverlet's `XPlat Code Coverage` collector ignores `Threshold`. It read as
  covered for two iterations. Gates are now verified in both directions — passing at the
  real number *and* failing at an impossible one.

## Deferred from clarification

Outbox throughput, retention window and expected site count were deferred to planning.
Resolved here as starting values, all configurable and all documented in
`docs/configuration.md`: batch size 50, poll interval 5s, handler timeout 30s, max
attempts 5 with exponential backoff, completed-event retention 7 days. These are sized for
the tens-of-sites, hundreds-of-events-per-minute case; none are load-bearing assumptions,
and the indexes in [data-model.md](./data-model.md) are what actually determine headroom.
