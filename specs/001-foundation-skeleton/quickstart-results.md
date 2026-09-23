# Quickstart Validation Results

**Feature**: `001-foundation-skeleton` | **Run**: 2026-09-23 | **Task**: T132

Every scenario in [quickstart.md](./quickstart.md) executed against the reference
deployment, plus the pipeline timing SC-007 asks for.

**Environment**: Windows 11, Docker 28.5.1, .NET 10.0.401, Node 24.19. This is a developer
workstation, not the reference two-core / four-gigabyte environment, so the timings below
are faster than the budgets assume. They demonstrate headroom rather than certify the
budget — that needs a run on the reference hardware.

## Results

| # | Scenario | Criteria | Result |
|---|---|---|---|
| V1 | Cold start on an empty database | SC-001, SC-002, FR-014a | ✅ **ready in 8s** (budget 60s); live 200, empty site list, admin shell 200 |
| V2 | First site end to end | SC-001a, US1/AC3 | ✅ create 201, public resolve 200 returning the right site, **1s total** (budget 2 min) |
| V3 | Unknown host + zero-site admin | FR-014, FR-014a | ✅ unknown host 404, admin surface unaffected at 200 |
| V4 | Restart preserves data | SC-006, US1/AC4 | ✅ ready 200 after restart, site survived, no manual step |
| V5 | Database outage | SC-010, US1/AC6 | ✅ db down → live **200**, ready **503**; **recovered in 2s** with no restart (budget 60s) |
| V6 | Fail-fast on bad configuration | FR-028, US1/AC5 | ✅ startup aborted naming the key: *"ConnectionStrings:Default is required…"* |
| V7 | Outbox delivery and idempotency | FR-015–FR-018, SC-005 | ✅ 4/4 (`Category=Outbox`) |
| V8 | Concurrency | FR-019, FR-024, SC-012 | ✅ 3/3 (`Category=Concurrency`) |
| V9 | Boundaries fire when broken | SC-004 | ✅ 11/11 architecture tests |
| V10 | Site scoping | FR-013, FR-013a, SC-011 | ✅ 5/5 (`Category=SiteScoping`) |
| V11 | Admin shell quality | SC-013, FR-045a/b | ✅ lint (no raw text) + axe tests pass |
| V12 | Nothing leaves the process | SC-014, FR-031b | ✅ 3/3 (`Category=NoEgress`) |
| V13 | Logs | SC-008, FR-031, FR-032 | ✅ structured JSON, every entry carries a `TraceId`, **zero credential leaks** |

**13 of 13 pass.**

## SC-007 — pipeline duration

| Stage | Time |
|---|---|
| Build (Release) | 6s |
| Tests (unit + architecture + integration) | 18s |
| Admin type-check, lint, test | 6s |
| Container image build | 2s |
| **Total** | **32s** (budget 600s) |

Measured with warm caches on a developer machine. A cold CI runner restoring packages and
pulling base images will be substantially slower, but 32s against a 600s budget leaves
room for that by a wide margin.

## Notes

**The commands in the guide now work as written.** An earlier run would not have got this
far: three scenarios used `--filter Category=…` when no `Category` trait existed, and V12
invoked a Docker flag that does not exist. Both were fixed by adding the traits and
rewriting V12 around the `NoEgress` test. This run is the first time every documented
command has actually been executed.

**V5 is the most reassuring result.** Liveness stayed healthy while readiness went
unhealthy, which is exactly the split FR-027 asks for — an orchestrator would have stopped
sending traffic without killing a process that was fine, and recovery needed no
intervention.

**What this run does not establish**: SC-002's 60s cold-start and SC-010's 60s recovery
budgets are specified against the reference two-core, four-gigabyte environment. The
margins here (8s and 2s) are wide enough to be confident, but a run on that hardware is
what would certify them.
