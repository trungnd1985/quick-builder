# Quickstart & Validation Guide

**Feature**: `001-foundation-skeleton` | **Date**: 2026-09-22

How to run the skeleton and how to prove each success criterion. This is a validation
guide, not an implementation guide — entity shapes live in [data-model.md](./data-model.md),
HTTP shapes in [contracts/admin-api.yaml](./contracts/admin-api.yaml), and event semantics
in [contracts/integration-events.md](./contracts/integration-events.md).

---

## Prerequisites

- A container runtime (Docker Desktop or equivalent) — that is all an **operator** needs.
- For **development** additionally: .NET 10 SDK, Node 22+, and a container runtime for
  Testcontainers.

---

## Run it (the SC-001 path)

```bash
git clone <repo> && cd quick-builder
docker compose up -d
```

Expected, on a machine that has never run this before:

```bash
curl -fsS http://127.0.0.1:8080/health/live     # 200 immediately
curl -fsS http://127.0.0.1:8080/health/ready    # 200 once migrations finish
open http://127.0.0.1:8080/admin                # admin shell, empty site list
```

The compose file publishes on `127.0.0.1` only — the installation is not reachable from
another machine, by design, because nothing in this feature has a login (FR-037a).
Widening that binding is a deliberate edit.

**Stopwatch check (SC-001)**: clone → loaded admin shell in under 5 minutes, one command,
no manual database work.

---

## Validation scenarios

Each maps to success criteria and user-story acceptance scenarios in [spec.md](./spec.md).

### V1 — Cold start on an empty database (SC-001, SC-002, US1 1–2)

```bash
docker compose down -v          # destroy the volume: genuinely empty
docker compose up -d
time until curl -fsS http://127.0.0.1:8080/health/ready >/dev/null 2>&1; do sleep 1; done
```

**Expect**: ready within 60s; `GET /api/v1/sites` returns `[]`; the admin shell renders
its empty state rather than an error or a blank screen (FR-045).

### V2 — First site, end to end (SC-001a, US1 3)

```bash
curl -fsS -X POST http://127.0.0.1:8080/api/v1/sites \
  -H 'content-type: application/json' \
  -d '{"name":"Example","hostNames":["example.test"]}'

curl -fsS -H 'Host: example.test' http://127.0.0.1:8080/
```

**Expect**: `201` with the created site; the public request resolves to it and returns its
name. Under 2 minutes total, no restart.

### V3 — Unknown host and zero-site installation (FR-014, FR-014a)

```bash
curl -i -H 'Host: nobody.test' http://127.0.0.1:8080/        # 404, discloses nothing
curl -i http://127.0.0.1:8080/admin                          # loads regardless
```

**Expect**: the 404 body is byte-identical whether the installation has zero sites or
many. The admin surface is unaffected — that is the admin/public split (FR-011).

### V4 — Restart preserves data (SC-006, US1 4)

```bash
docker compose restart app
curl -fsS http://127.0.0.1:8080/api/v1/sites    # site from V2 still there
```

**Expect**: no data loss, no manual migration step, readiness healthy again.

### V5 — Database outage (SC-010, US1 6)

```bash
docker compose stop db
curl -i http://127.0.0.1:8080/health/live       # still 200 — process is alive
curl -i http://127.0.0.1:8080/health/ready      # 503, names the failing dependency
docker compose start db
# readiness recovers within 60s, with no app restart
```

### V6 — Fail-fast on bad configuration (US1 5, FR-028)

```bash
docker compose run --rm -e ConnectionStrings__Default= app
```

**Expect**: startup aborts immediately with a message naming the offending key. The server
never begins listening.

### V7 — Outbox delivery and idempotency (SC-005, US3)

Run the integration suite — this is not a curl scenario because it asserts on database
state and redelivery:

```bash
dotnet test tests/QuickBuilder.IntegrationTests --filter Category=Outbox
```

**Expect**: the `Sites.SiteCreated` row and the site row commit together; a rolled-back
creation leaves neither; a restarted dispatcher still delivers a pending event; forced
redelivery produces exactly one effect; an always-failing handler backs off, goes terminal
after `MaxAttempts`, and does not block other events.

### V8 — Concurrency (SC-012, FR-040a)

```bash
dotnet test tests/QuickBuilder.IntegrationTests --filter Category=Concurrency
```

**Expect**: several migrators against one database apply migrations exactly once; several
dispatchers claim each event exactly once. Per the clarification this runs in one test
process against one real database — the pipeline does not orchestrate multiple app
containers.

### V9 — Boundaries actually fire (SC-004)

```bash
dotnet test tests/QuickBuilder.ArchitectureTests
```

**Expect**: every rule in [contracts/module-contract.md](./contracts/module-contract.md)
passes on the real tree, and each deliberately violating fixture fails with a message
naming the rule. A rule with no failing fixture is an untested rule.

### V10 — Site scoping (SC-011, FR-013, FR-013a)

```bash
dotnet test tests/QuickBuilder.IntegrationTests --filter Category=SiteScoping
```

**Expect**: with two sites configured, no public-surface query returns the other site's
host names; the admin cross-site listing works only through the annotated helper; an
attempt to call `IgnoreQueryFilters` elsewhere fails compilation.

### V11 — Admin shell quality (SC-013, FR-045a/b)

```bash
cd admin && npm run lint && npm run test
```

**Expect**: the `no-raw-text` rule reports zero strings written into screens; axe reports
no violations of the recorded baseline; every journey in V2 is completable by keyboard
alone.

### V12 — Nothing leaves the process (SC-014, FR-031b)

```bash
dotnet test tests/QuickBuilder.IntegrationTests --filter Category=NoEgress
```

**Expect**: no telemetry endpoint configured, tracing still instrumented, and zero outbound
HTTP across create → resolve → health.

The test is the primary evidence, and it makes the stronger claim: it asserts no exporter
is *registered*, so there is no code path that could call out — where a traffic capture
only shows that nothing happened to be sent while it was watching.

To confirm at the network level as well, block egress from the app container and run V2
end to end:

```bash
docker compose up -d
docker network disconnect bridge quick-builder-app-1 2>/dev/null || true
# the app keeps its compose network for the database, but has no route out
# then perform V2 — it should succeed unchanged
```

### V13 — Logs (SC-008, FR-031, FR-032)

```bash
docker compose logs app | grep '"TraceId"' | head
```

**Expect**: structured JSON; one request's entries share a `TraceId` that is the trace
identifier, not a separate scheme; the same value appears as `traceId` in an error
response body; no credentials, personal data or content bodies anywhere.

---

## Development loop

```bash
dotnet build                                   # warnings are errors
dotnet test                                    # unit + integration + architecture
cd admin && npm run dev                        # Vite dev server, proxying /api to the host
```

The full pipeline (`.github/workflows/ci.yml`) runs restore, build, all tests, format
check and an image build, and must finish under 10 minutes (SC-007).

---

## Adding a feature (the SC-003 path)

Copy `src/Modules/Sites/QuickBuilder.Sites/Features/ListSites/` to a new folder, rename the
types, register nothing — the module's entry point discovers handlers. Full walkthrough in
`docs/contributing.md`; the rules it follows are in
[contracts/module-contract.md](./contracts/module-contract.md).

**The check that matters**: you changed no file outside your module's folder.
