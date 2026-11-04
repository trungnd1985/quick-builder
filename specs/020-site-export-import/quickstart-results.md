# Quickstart results: Site export and import (020)

**Status**: automated part done 2026-11-03. The performance run (T091) was done on 2026-11-04
in a container capped at the reference setup's 2 CPUs and 4 GB; SC-002 is met. The manual
quickstart run with screen-reader, keyboard and usability checks (T094) has not been done.

## Automated evidence

| Quickstart section | Proven by | Result |
|---|---|---|
| §1 Export (US1, SC-001) | `ITESTS/Transfer/ExportContentsTests.cs`, `ExportSnapshotTests.cs`, `TransferEndpointsTests.cs`, `ADMINTESTS/transfer/TransferTab.spec.ts`, `TransferProgress.spec.ts` | pass |
| §2 Import as a new site (US2) | `ITESTS/Transfer/RoundTripTests.cs`, `ImportEndpointsTests.cs`, `ADMINTESTS/transfer/ImportSiteView.spec.ts`, `ImportCheckView.spec.ts` | pass |
| §3 Refusals and failures (US3, SC-003) | `ITESTS/Transfer/ArchiveRefusalTests.cs`, `ImportFailureTests.cs`, `ArchiveFromPreviousReleaseTests.cs` | pass |
| §4 Merge and restore (US4, SC-005) | `ITESTS/Transfer/MergePolicyTests.cs`, `RestoreTests.cs`, `TransferLockTests.cs`, `TransferGateCoverageTests.cs`, `UTESTS/Sites/Transfer/ConflictRulesTests.cs`, `IdMapTests.cs`, `UTESTS/Editor/DesignMergeTests.cs`, `ADMINTESTS/transfer/RestoreConfirmDialog.spec.ts`, `SiteImportBanner.spec.ts` | pass |
| §5 Command line (US5) | `ITESTS/Transfer/SitesCommandTests.cs` | pass |
| §6 Upgrade | `ITESTS/Transfer/UpgradeFrom023Tests.cs` | pass |
| §7 Contract | `UTESTS/Sites/Transfer/ManifestSchemaTests.cs` (writer, golden archive and example against `docs/contracts/site-archive/manifest.v1.schema.json`) | pass |

Whole suites on 2026-11-03, on the final code: integration 1,439 passed and 4 skipped (three
platform-specific startup tests and the manual performance test), unit 2,599 passed,
architecture 60 passed, admin 1,474 passed.

## Coverage (T093)

Line coverage from the transfer unit and integration tests together (coverlet, merged):

| Target | Lines | Covered |
|---|---|---|
| `SITES/Features/Transfer/Engine/` | 84.0% | 844/1005 |
| `SITES/Domain/IdMap.cs` | 94.1% | 96/102 |
| `SITESC/Transfer/ConflictRules.cs` | 97.3% | 36/37 |
| `Shared/…/Data/SiteWriteGateInterceptor.cs` (the write gate as built) | 89.7% | 96/107 |
| `SitesArchivePart.Import.cs` | 92.5% | 124/134 |
| `MediaArchivePart.Import.cs` | 90.2% | 230/255 |
| `DesignArchivePart.Import.cs` | 84.0% | 137/163 |
| `CmsArchivePart.Import.cs` | 80.0% | 395/494 |
| `PagesArchivePart.Import.cs` | 95.5% | 361/378 |
| `PagesArchivePart.Merge.cs` | 92.5% | 356/385 |
| `SitesCommand.cs` | 83.1% | 359/432 |

The write gate was at 79.4%; `ITESTS/Transfer/SiteWriteGateInterceptorTests.cs` (2026-11-05)
covers its synchronous saves, a failed save and the exception, which brings it to 89.7% from
the gate tests alone. Still uncovered: the cancelled-save hook (EF Core stops a cancelled save
before it calls the hook), the guard for an event with no context, and the early return for a
save of no site rows, which the rest of the suite reaches.

## Performance (T091, SC-002)

`ITESTS/Transfer/TransferPerformanceTests.cs` runs with `QB_TRANSFER_PERF=1`. **SC-002 is
met** on the reference setup's limits (2 CPUs, 4 GB): the export takes 4 to 8 s and the import
about 15 s, the process peaks under 450 MB, and visitor p95 stays at 10 to 18 ms during the
export and import jobs (last two sections below). Smoke runs on a developer machine, in-process
with Testcontainers PostgreSQL, prove the test works:

| Scale | Pages | Items | Media | Export | Import | Visitor p95 during export / import |
|---|---|---|---|---|---|---|
| 0.02 | 10 | 200 | 48 MB | 0.5 s | 0.8 s | 207 ms / 8 ms |
| 0.1 | 50 | 1,000 | 208 MB | 0.7 s | 1.8 s | 198 ms / 6 ms |
| 1 (full size), 2026-11-04 | 500 | 10,000 | 2 GB (archive 2,148,449,121 bytes) | 3.9 s | 13.0 s | 15 ms / 7 ms |

**Full size, limited to the reference setup's 2 CPUs and 4 GB, 2026-11-04.** The test ran in a
`mcr.microsoft.com/dotnet/sdk:10.0` container started with `--cpus=2 --memory=4g`, on the working
tree. The PostgreSQL container that Testcontainers starts was outside that limit.

| Step | Time | Limit | Visitor p95 (requests) |
|---|---|---|---|
| Seeding | 5 min 47 s | – | – |
| Export (2,148,457,465 bytes) | 6.1 s | 15 min | 52 ms (49) |
| Import as a new site | 17.6 s | 20 min | 47 ms (130) |

- **Times:** well within SC-002.
- **Memory:** the test process (app and test together) peaked at 428 MB, under the 1 GB target.
  `docker stats` for the container peaked at 3.26 GiB at the end of the import. That figure also
  counts the build's leftover compiler processes (1.5 to 1.7 GiB from the build onwards) and the
  file cache of the 2 GB archive and the media written in the container, so it is not the app's
  memory.
- **Visitor p95:** 47 to 52 ms for a published page while the export and the import run. That is
  under the 200 ms cache-miss budget (Principle VIII), but over the 20 ms cache-hit budget.
  - It is open whether these requests are cache hits.
  - In the run they share two CPUs with the export or import and with the test's own client.
  - Settled by the runs below: the export and import jobs stay within the 20 ms budget.

**Hashing on a low-priority thread, 2026-11-04.** Archive hashing moved off the shared thread pool
to one dedicated low-priority thread (`LowPriorityCpu`; nice 10 on Linux). Same container and
limits; the test now also measures a 10 s idle baseline after a warm-up request:

| Run | Idle p50 / p95 | Export time, p50 / p95 | Import time, p50 / p95 |
|---|---|---|---|
| Before | 7 / 11 ms | 5.4 s, 7 / 53 ms | 19.2 s, 6 / 80 ms |
| After | 7 / 10 ms | 9.3 s, 7 / 62 ms | 17.8 s, 7 / 15 ms |

- **Import:** p95 fell from 80 to 15 ms, inside the 20 ms cache-hit budget.
- **Export:** no better. Hashing was not what slowed visitors there, and the export took 4 s
  longer because its hashing now uses one CPU.
- **Visitor p50:** unchanged at 6 to 7 ms in every run, so the slow requests are a minority.
- One run each; the p95 of about 50 export requests rests on its three slowest.

**Each step timed on its own, two runs, 2026-11-04.** The test now times the export job, the
download, the upload and the import apart, since in the test the archive moves through an
in-memory server at memory speed.

| Step | Run 1: time, p50 / p95 (requests) | Run 2: time, p50 / p95 (requests) |
|---|---|---|
| Idle | 6 / 7 ms (81) | 6 / 8 ms (95) |
| Export job | 6.4 s, 6 / 10 ms (44) | 6.0 s, 6 / **471 ms** (30) |
| Download | 3.8 s, 7 / **1,007 ms** (19) | 2.2 s, 6 / 11 ms (20) |
| Upload | 3.2 s, 7 / 8 ms (31) | 2.8 s, 7 / 26 ms (25) |
| Import | 14.0 s, 6 / 17 ms (123) | 14.6 s, 6 / 17 ms (122) |

- **Import:** p95 17 ms in both runs, inside the 20 ms cache-hit budget.
- **Stalls:** the export job and the download each passed in one run and stalled in the other.
  - A visitor request then waits about half a second to a second; p50 stays at 6 to 7 ms.
  - The export job in run 2 fitted 30 requests into 6 s where about 55 were expected, so it
    stalled more than once.
  - Stalls of whole multiples of about 500 ms match how the .NET thread pool adds threads when
    all of its threads are blocked. They could also be garbage-collection pauses. The next runs
    rule out both.

**Thread pool and GC sampled, two runs, 2026-11-04.** The test now samples the thread pool's
threads and queue and the GC's total pause every 50 ms, and records the samples around every
visitor request over 100 ms.

| Step | Run 3: time, p95 (requests) | Run 4: time, p95 (requests) | Pool threads, queued max | GC paused |
|---|---|---|---|---|
| Idle | 8 ms (94) | 8 ms (73), one request of **507 ms** | 5 to 6, 3 | 3 to 6 ms |
| Export job | 7.6 s, 17 ms (70) | 4.0 s, 10 ms (33) | 6 to 7, 3 | 16 to 17 ms |
| Download | 4.2 s, 40 ms (34), one of 248 ms | 3.9 s, 22 ms (37) | steady, 4 | 7 to 18 ms |
| Upload | 5.2 s, 67 ms (43), one of 112 ms | 5.0 s, 80 ms (40), one of 314 ms | steady, 5 | 6 to 12 ms |
| Import | 15.3 s, 18 ms (124) | 15.4 s, 16 ms (124) | steady, 5 | 430 to 436 ms, 4 gen2 |

- **Not thread-pool starvation:** the pool never added a thread during a stall, and at most
  five work items ever waited.
- **Not GC:** the GC paused for 0 ms during every slow request.
- **Not the transfer:** the longest stall, 507 ms, came in the idle baseline, with nothing
  running. The stalls come from the test environment, for example the Docker Desktop VM or the
  PostgreSQL container outside the CPU limit, still busy after seeding.
- **Export job and import:** visitor p95 is 10 to 18 ms in all four of the runs that time them
  apart, except the export in run 2 (471 ms), which is the same environment stall. That is
  inside the 20 ms cache-hit budget.
- **Download and upload:** in the test these go through an in-memory server at memory speed and
  take two CPUs for 2 GB in about 4 s; over a real network they are bound by the network. The
  test does not measure them for SC-002.

The developer-machine run above (12 CPUs, 16 GB for Docker) passed its limits (export under 15 min, import under 20 min) on a developer
machine with 12 CPUs and 16 GB for Docker, much more than the reference setup, so it shows the
full size works end to end but does not measure SC-002. The test process peaked at 435 MB. It
streams the archive through a temporary file, because a full-size archive is over 2 GB, more
than one array holds.

The first visitor request in each run renders cold, which dominates the p95 of a few requests.
The test process's peak working set (995 MB at 0.1) includes the archive the test holds in
memory, so it says nothing about the application's memory.

## Smoke tests (Docker deployment), 2026-11-04

`tests/QuickBuilder.SmokeTests` on this branch: 13 passed, 4 failed. None of the four is caused by
020:

- `ReleaseUpgradeTests` needs `QB_SMOKE_IMAGE` (a published release image); not set here.
- `SiteResolutionPerformanceTests` expects 404 from every host name, but since #17 a site's other
  host names redirect (301) to its primary one.
- `RenderPerformanceTests` expects no `<script` in a page, but since #17 pages carry structured
  data (`<script type="application/ld+json">`).
- `MediaPerformanceTests` (SC-008, admin p95 during uploads at most 2x idle) fails on `main` too.
  Alternating runs: this branch 23 and 26 ms, `main` 32 and 38 ms, both against about 5 to 7 ms
  idle.

The resolution and render tests have not changed since 2026-10-01, before #17; they need updating
outside 020.

## Not run by hand (T094)

Quickstart §1 to §7 on the reference compose setup, the screen-reader and keyboard checks
(E-5), and the usability check with three participants (SC-007) need people and the reference
setup; they are not done.
