# Phase 0 Research: Project Foundation and Skeleton

**Feature**: `001-foundation-skeleton` | **Date**: 2026-09-22

The technology stack is fixed by the constitution, so this document does not
re-litigate platform choices. It resolves the open design decisions the spec leaves to
planning, plus every new third-party dependency (Principle XI requires each to be
justified here).

---

## D-01: Outbox table placement

**Decision**: Each module owns its own outbox table in its own schema
(`sites.outbox_messages`), mapped on that module's `DbContext`. The dispatcher is
generic over a registry of per-module outbox stores.

**Rationale**: FR-015 requires the event and the state change to persist atomically.
With a per-module table on the same `DbContext`, that is a single `SaveChanges` — no
distributed transaction, no shared-table write, and FR-021 (no transaction spans two
modules) and FR-022 (no module touches another's tables) both hold by construction.

**Alternatives considered**:
- *One shared `outbox` schema*: a module writing to it writes outside its own schema,
  breaking FR-022 and the constitution's data-ownership rule.
- *Separate outbox `DbContext`*: atomicity would need a shared transaction or
  `TransactionScope` across contexts, which is exactly what FR-021 forbids.

---

## D-02: Outbox claim strategy

**Decision**: Claim with `SELECT ... FOR UPDATE SKIP LOCKED` on a bounded batch inside a
short transaction; run the handler; mark the row completed; commit. Handlers are given a
timeout and are documented as "must be short".

**Rationale**: PostgreSQL row locks satisfy FR-019 with no bookkeeping — one instance
claims a row, others skip it, and if an instance dies its transaction is rolled back by
the server so the row becomes eligible again automatically. No lease column, no clock
comparison, no stuck-claim sweeper to write or test.

**Alternatives considered**:
- *Lease column with expiry timestamp*: survives long-running handlers, but requires a
  sweeper, depends on clock agreement between instances, and adds a reclaim path that
  itself needs tests. Rejected for this feature; revisit if a handler ever needs to run
  longer than a transaction should be held open.
- *Advisory lock per message*: same effect, more bookkeeping, no benefit here.

**Consequence to record**: a handler that blocks holds a database transaction open. The
handler timeout and the batch size both become documented configuration keys.

---

## D-03: Idempotency mechanism (FR-017)

**Decision**: A per-module `processed_events` table keyed `(event_id, handler_name)`
with a unique constraint. The dispatch wrapper inserts that row in the same transaction
as the handler's effect; a duplicate key means "already done" and the transaction is
rolled back as a no-op.

**Rationale**: Turns at-least-once delivery into effectively-once effects using the
database's own uniqueness guarantee, in the same transaction as the work — so there is
no window where the effect lands but the marker does not. Satisfies FR-017 and US3
scenario 4 directly.

**Alternatives considered**:
- *Handlers individually responsible for idempotency*: pushes the same logic into every
  future module and cannot be enforced by a test.
- *Shared dedupe table*: breaks module data ownership (see D-01).

---

## D-04: Migration concurrency (FR-024)

**Decision**: A startup hosted service takes a PostgreSQL session-level advisory lock
keyed by a hash of the module name, applies that module's migrations, releases the lock.
Other instances block on the lock and then find nothing to apply.

**Rationale**: Advisory locks are held by the connection, so an instance that dies
releases the lock automatically — no stuck-migration state. Per-module keys let modules
migrate in parallel without contending with each other.

**Alternatives considered**:
- *A migrations lock table*: needs its own cleanup path for crashed holders, and the
  table itself has an ownership problem.
- *Migrate only from a separate job*: contradicts FR-023's "automatically at startup by
  default" and the constitution's five-minute install promise.

---

## D-05: Validation abstraction (FR-008)

**Decision**: An in-house `IValidator<TRequest>` returning a project-owned
`ValidationResult`, resolved from DI and invoked by a decorator around the command/query
dispatcher. No third-party validation library.

**Rationale**: The constitution already mandates in-house dispatch abstractions and
forbids mediator libraries; a validation library would be the same kind of dependency for
the same kind of job. The skeleton needs roughly a hundred lines to cover it, and this
avoids inheriting a dependency whose licence may change — a live risk in this ecosystem,
where several widely used .NET helper libraries moved to commercial licences recently.
The constitution requires a licence compatible with redistribution, so avoiding the
question entirely is the cheaper answer.

**Alternatives considered**:
- *FluentValidation*: excellent library, and the rule syntax is nicer than hand-written
  checks. Rejected on dependency-minimisation and redistribution-licence grounds, not on
  quality. Revisit if validation rules become genuinely complex.
- *DataAnnotations*: attribute-based validation cannot express cross-field or
  database-dependent rules (such as "this host name is not already taken") without
  escaping the model anyway.

---

## D-06: Architecture test tooling (FR-025)

**Decision**: `NetArchTest.Rules` (MIT) for assembly, namespace and visibility rules,
run as ordinary xUnit tests in the same test stage as everything else (FR-026).

**Rationale**: Named in the constitution, MIT licensed, no runtime footprint, and
expresses "module A must not reference module B" and "these types must be internal"
directly.

**Limitation to design around**: NetArchTest reasons about types and references, not
about method call sites. It therefore cannot see the case in FR-013a — code that calls
`IgnoreQueryFilters()` to read across sites. That case is handled by D-07 instead.

**Alternatives considered**:
- *ArchUnitNET*: comparable capability, larger API, no advantage for these rules.
- *Custom Roslyn analyzer for everything*: more power than needed for reference rules,
  and much slower to write.

---

## D-07: Enforcing deliberate cross-site reads (FR-013a)

**Decision**: Ban `IgnoreQueryFilters` project-wide with
`Microsoft.CodeAnalysis.BannedApiAnalyzers` (MIT, build-time only, warnings-as-errors
makes it a hard failure). A single helper in `Shared.Infrastructure` carries the only
suppression and requires a non-empty reason string from every caller. A test asserts each
call site's reason is present and that each has its own dedicated test.

**Rationale**: FR-013a says an implicit or accidental bypass MUST fail the checks. A type
or reference rule cannot express that, but a banned-API rule can: the only way to read
across sites is through one annotated, tested door, and any other attempt fails the
build at compile time rather than in review.

**Alternatives considered**:
- *Source-text scanning test* (regex for the call and its enclosing attribute): brittle,
  breaks on formatting, and reports at test time rather than compile time.
- *Trusting review*: exactly what FR-013a exists to prevent.

---

## D-08: Site context and automatic filtering (FR-011a, FR-013)

**Decision**: A scoped `ISiteContext` with a nullable `SiteId`. Public-surface middleware
resolves the host name to a site and populates it; the administrative surface leaves it
null. EF Core global query filters on an `ISiteOwned` marker interface compare against
`ISiteContext.SiteId`. A null site context means public-surface queries match nothing,
which is the safe direction.

**Rationale**: Satisfies FR-011a (handlers never read headers), FR-013 (no per-query
filtering code) and FR-011b (admin works with no site) with one mechanism. Failing closed
on a null site is the behaviour FR-014 wants anyway.

**Revised during implementation — read this before writing a module's DbContext.**

The decision above says filters "compare against `ISiteContext.SiteId`". Taken literally —
a filter expression closing over the injected `ISiteContext` — **it does not work, and it
fails silently.**

EF Core builds and caches the model once per context type. A filter closing over an
injected service captures whichever instance built the model, which in practice is the one
from startup migration, where no site is resolved. Every later request then filters against
a stale empty site and returns **zero rows**. Nothing throws, nothing logs, and the code
reads correctly.

It survived review and a working demo. It was caught only when a scoping test configured
*two* sites and asserted each could see its own data — with one site the bug is
undetectable, because every query returns the only site's rows and passes.

**The shape that works**: expose the site as a property *on the context* and reference it
through `this`, so EF lifts it into a query parameter re-evaluated per query against the
context actually running it:

```csharp
internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

entity.HasQueryFilter(h => h.SiteId == CurrentSiteId);
```

**Consequences recorded elsewhere:**

- The generic `ApplySiteFilters` helper was removed. A loop cannot express this correctly,
  and a helper that looks right while matching nothing is worse than no helper.
- Filters are now declared per entity, by hand, which means one can be *forgotten* — and a
  forgotten filter returns every site's rows. `SiteFilterCoverageTests` closes that hole by
  asserting every `ISiteOwned` entity in the built model carries a filter.
- `contracts/module-contract.md` and `docs/contributing.md` carry the same instruction.

**Alternatives considered**:
- *Throwing when the site context is empty*: would break the administrative surface,
  which legitimately has no site (FR-011b).
- *Passing `SiteId` explicitly into every query*: this is exactly the per-query code
  FR-013 forbids, and it fails open when someone forgets.

---

## D-09: Host name lookup and the cache abstraction

**Decision**: Ship one narrow cache abstraction in `Shared.Infrastructure`, backed by
`IMemoryCache`, used by host-name-to-site resolution. Do **not** ship a media storage
abstraction in this feature.

**Rationale**: Constitution Principle IV requires pluggable infrastructure behind
abstractions, and Principle XI forbids abstractions no spec needs. Host name resolution
runs on every public request and genuinely needs caching, so the cache abstraction has a
real consumer today and a real second implementation (Redis) later. Media storage has no
consumer until the Media module exists; building its abstraction now would be the
speculative kind. This is a deliberate, recorded narrowing of FR-003 — see the plan's
Complexity Tracking.

**Cache invalidation**: creating a site or adding a host name evicts the resolution entry
for the affected host names in the same request. Cross-instance invalidation is not
solved here (in-memory cache, single-instance reference deployment) and is listed as a
known limitation for the Redis provider spec.

---

## D-10: Correlation identifier and tracing (FR-031a, FR-031b)

**Decision**: OpenTelemetry .NET SDK with ASP.NET Core and Npgsql instrumentation
registered, and **no exporter registered unless configuration names one**. Serilog
enriches every log entry from `Activity.Current` with `TraceId` and `SpanId`; the
correlation identifier in logs *is* the trace id. Background dispatch starts its own
`Activity` so outbox work is correlated the same way.

**Rationale**: FR-031a says do not invent a second correlation scheme, and
`Activity.TraceId` already exists on every request through the W3C trace context header.
FR-031b's "no outbound request by default" falls out of registering no exporter — there
is nothing to call. An offline install behaves identically because no code path attempts
a network call (verified by SC-014).

**Alternatives considered**:
- *Custom `X-Correlation-Id` header and a Serilog `LogContext` property*: works, but is
  precisely the scheme FR-031a says to avoid, and it would be replaced the moment tracing
  is switched on.
- *Register an OTLP exporter pointed at localhost by default*: would attempt outbound
  connections on every install, violating FR-031b and the constitution's offline rule.

---

## D-11: Admin asset serving, deep links and versioning (FR-042, FR-043, FR-046)

**Decision**: Vite builds the admin app into the host's `wwwroot/admin` at image build
time (a Node stage in the Dockerfile; no Node at runtime). Deep links are served by an
SPA fallback mapped to `/admin/{*path}`. Vite's content-hashed filenames give FR-046's
versioning; `index.html` is served `no-store` while hashed assets are served `immutable`.

**Rationale**: One image, no runtime Node process (FR-035). Hashed filenames mean an
upgraded image cannot serve a stale mix: the new `index.html` references new file names,
and old names simply no longer exist.

**Path collision rule (FR-043 edge case)**: `/api`, `/health` and `/admin` are reserved
prefixes matched before the public surface; the public catch-all is registered last and
explicitly excludes them. Order is asserted by a routing test rather than left to
registration sequence.

---

## D-12: Reverse proxy and base path (FR-012)

**Decision**: `ForwardedHeadersMiddleware` for host, scheme and client address, with
known proxies/networks configurable; `UsePathBase` driven by a configuration key. Both
run before site resolution so the resolved host is the client-visible one.

**Rationale**: FR-011a resolves sites from the host name, so getting forwarded-host
handling wrong silently resolves the wrong site. Ordering it before site resolution is the
correctness-critical detail.

**Security note**: forwarded headers are trusted only from configured proxy addresses;
the default configuration trusts none, so a direct-to-app deployment cannot be spoofed.

---

## D-13: Admin text lookup and accessibility (FR-045a, FR-045b)

**Decision**: `vue-i18n` (MIT) for the text lookup, shipping one locale. Accessibility is
enforced by `vitest` + `@testing-library/vue` + `axe-core` component checks, plus the
`no-raw-text` lint rule from `@intlify/eslint-plugin-vue-i18n` to fail the build on text
written into a screen.

**Rationale**: FR-045a's real requirement is that adding a language later must not reopen
every screen — a lookup call at each site is what delivers that, and `vue-i18n` is the
standard, MIT-licensed way to do it, with pluralisation and interpolation already solved.
SC-013 demands an automated check for both halves; the lint rule covers "no inline
strings" and axe covers the accessibility baseline, both in CI.

**Baseline recorded** (FR-045b): every interactive element keyboard reachable and
operable, visible focus indicator, WCAG 2.1 AA contrast (4.5:1 body text, 3:1 large text
and UI boundaries), every form control with an associated label. Written into
`docs/admin-accessibility-baseline.md` so later specs inherit it.

**Alternatives considered**:
- *Hand-rolled lookup function*: avoids a dependency, but re-implements pluralisation and
  number/date formatting badly. The dependency is justified.
- *Playwright for accessibility*: better fidelity, much slower, and heavier than this
  shell warrants. Revisit when real screens exist.

---

## D-14: Test infrastructure

**Decision**: xUnit throughout. Integration tests use `WebApplicationFactory` plus
`Testcontainers.PostgreSql` (MIT) — one container per test collection, database per test
class. Coverage via `coverlet` with a threshold gate on the critical paths named by
Principle X. Frontend uses `vitest`.

**Concurrency tests (FR-040a, SC-012)**: run several migrators and several dispatchers in
parallel *in one test process* against one real container, asserting exactly-once
migration application and exactly-once claim. Per the clarification, the pipeline does not
orchestrate multiple application containers.

**Rationale**: The constitution forbids in-memory database providers, and the behaviours
under test (advisory locks, `SKIP LOCKED`) exist only in a real PostgreSQL — an in-memory
provider would pass while proving nothing.

---

## D-15: Container image and reference deployment (FR-035–FR-038)

**Decision**: Multi-stage Dockerfile — Node stage builds the admin assets, SDK stage
publishes the app, runtime stage is `mcr.microsoft.com/dotnet/aspnet:10.0` running as the
non-root `app` user on port 8080. The reference `docker-compose.yml` at the repository
root publishes the app as `127.0.0.1:8080:8080` (FR-037a) and runs PostgreSQL with a
named volume.

**Rationale**: FR-036 requires non-root; the official aspnet images already ship a
non-root `app` user, so this costs one `USER` line rather than custom user creation. The
loopback publish is one edit in one file, which is exactly the property the clarification
asked for — nothing to remove when authentication lands (FR-037b).

**Multi-arch**: the constitution requires multi-arch *published* images; publishing is out
of scope for this feature, so CI builds for the runner's architecture only to prove the
Dockerfile works (FR-041). Multi-arch build and push belongs to the release spec.

---

## D-16: Configuration and fail-fast startup (FR-028, FR-029)

**Decision**: Strongly-typed options bound from `appsettings.json` with
`ValidateDataAnnotations().ValidateOnStart()`, so invalid configuration throws during
startup before the server listens. Environment variable overrides use the standard
double-underscore convention. Every key is documented in `docs/configuration.md`.

**Rationale**: `ValidateOnStart` is precisely FR-028's "fail immediately", and the
exception names the offending key without custom code. Note this uses DataAnnotations for
*configuration* validation only — D-05's decision concerns request validation, which is a
different problem with different needs.

---

## D-17: Database wait and readiness (FR-027, FR-030)

**Decision**: The migration hosted service retries connecting for a bounded, configurable
window with backoff. A `StartupState` singleton records `WaitingForDatabase`,
`Migrating`, `Ready` or `Failed`. Liveness returns healthy whenever the process responds;
readiness reports healthy only in `Ready` and names the current state otherwise.

**Rationale**: Splits the two health endpoints along the line FR-027 draws, and gives
US1 scenario 6 and SC-010 something concrete to assert. The state enum is also what the
edge case "migration fails partway" needs in order to stay unhealthy rather than crash.

**Revised during implementation — two changes:**

1. **The database check is hand-rolled, not `AspNetCore.HealthChecks.NpgSql`.** That
   package's current release targets an older framework, and the check is a single
   `CanConnectAsync` call. A dependency for one line is the kind Principle XI asks us not
   to take. It is reached through `IModuleDatabaseProvider.CanConnectAsync`, which also
   keeps the host free of any `DbContext` — an architecture test caught the first version
   of this controller taking one directly, which would have been data access in the
   composition root. The dependency has been removed from the table below accordingly.

2. **`StartupState` gained a `MigrationsPending` phase.** FR-023 lets an operator disable
   automatic migration and run the `migrate` command instead. Without a distinct state,
   that installation is indistinguishable from one that is still starting — so readiness
   now reports `MigrationsPending` and says what to run, rather than leaving an operator
   to guess whether to wait.

---

## New dependencies summary

Principle XI requires each to be justified; all are permissively licensed and compatible
with redistribution.

| Dependency | Licence | Why | Alternative rejected |
|---|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL | Mandated by constitution | — |
| `Serilog.AspNetCore` | Apache-2.0 | Mandated by constitution | — |
| `OpenTelemetry.Extensions.Hosting` + ASP.NET Core/Npgsql instrumentation | Apache-2.0 | FR-031a/b; supplies the trace id used as correlation id | Custom correlation header (D-10) |
| `NetArchTest.Rules` | MIT | FR-025, named in constitution | ArchUnitNET (D-06) |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | MIT | FR-013a compile-time enforcement; build-time only | Source scanning test (D-07) |
| `Testcontainers.PostgreSql` | MIT | Principle X forbids in-memory providers | — |
| `vue-i18n` | MIT | FR-045a | Hand-rolled lookup (D-13) |
| `axe-core` / `@intlify/eslint-plugin-vue-i18n` | MPL-2.0 / MIT | SC-013 automated checks; dev-only, not redistributed | Playwright (D-13) |

**Explicitly not added**: any mediator library (constitution), any validation library
(D-05), any generic repository layer (constitution), any object-storage abstraction
(D-09), any message broker (constitution Principle IV).

---

## Resolved unknowns

Every `NEEDS CLARIFICATION` from Technical Context is resolved above. No open questions
remain that block Phase 1.
