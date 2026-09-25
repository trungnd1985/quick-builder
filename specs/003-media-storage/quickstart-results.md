# Quickstart Validation Results

**Feature**: `003-media-storage` | **Run**: 2026-09-25 | **Task**: T087

Each section of [quickstart.md](./quickstart.md) was run against the reference deployment
(`docker compose`, image built from this branch), or through the automated check the
section names.

**Environment**: Windows 11, Docker Desktop 28.5.1 (WSL 2), .NET 10.0.401, Node 24.19. This
is a developer workstation. Only §9 ran under the reference limits (2 CPUs, 4 GB, via
`docker-compose.perf.yml`).

## Results

| § | Scenario | Criteria | Result |
|---|---|---|---|
| 1 | Upload and view with no configuration | SC-001, US1 | ✅ smoke test on the built image: ready in **2.1 s**, setup, a site, a PNG uploaded through the API and fetched byte-for-byte from `/_media/{id}.png` on the site's host name; the image sets `Storage__Local__Path=/var/lib/quickbuilder/media` and compose mounts `media-data` |
| 2 | Validation refuses what it should | SC-002 | ✅ `UploadValidationTests`: all **75** crafted files get their exact status and `errorCode`, and every refusal leaves no row and no stored object; `FileTypeDetectorTests` pass |
| 3 | Site isolation | SC-003 | ✅ `MediaSiteIsolationTests` 9/9: every host-and-file combination, every kind of miss byte-identical apart from `traceId`, traversal attempts never open anything in storage |
| 4 | Files are served inert | SC-004 | ⚠️ **automated half passes, manual half not done.** `MediaServingHeadersTests`: every accepted crafted file (polyglots included) and every sample, on the public address and the preview, has the exact type, `nosniff`, `inline` with `filename*`, the image sandbox CSP (none on PDF), the right `Cache-Control`, a strong `ETag`, `Last-Modified` and `Accept-Ranges`. **The manual Chromium and Firefox check (GIF/JS polyglot, PDF with JavaScript) has not been run: it is the merge gate for PDFs (plan Risks) and must be done before the PR is approved.** |
| 5 | Browse, copy and delete | SC-007, SC-009a, US3 | ✅ `DeleteFileTests` 7/7 (capability matrix; Editors refused on others' files with Identity's own 403 body); `DeleteAcrossInstancesTests` 1/1 (two instances, one database, one volume: `404` on the very next request); `ListFilesTests` 10/10; admin screen tests 10/10 (load more moves focus, the delete dialog traps focus, Escape cancels, copy builds the address) |
| 6 | Operator: configuration, persistence, upgrades | SC-005, SC-010 | ✅ each bad setting stops the **real container** with a message naming the key: `Storage:Provider 'S3'`, a relative path, `/app/media` (after the fix below), `Media:AllowedTypes` with SVG. `StorageStartupValidationTests` 8/8; `UpgradeFrom002Tests` 1/1; the smoke test recreates the app container with the volumes kept and the image still serves identical bytes |
| 7 | Interrupted uploads and clean-up | SC-006 | ✅ `UploadConsistencyTests` 4/4 (storage failure, disk full, dropped connection, failed insert); `MediaOrphanSweepTests` 4/4 (old orphans go, young orphans and stored files stay, per-site logging, concurrent sweeps) |
| 8 | Storage conformance | SC-011 | ✅ `LocalFileStorageConformanceTests` passes every case in the contract, including the concurrent-delete case added by this run |
| 9 | Performance | SC-008, SC-009, SC-009a | ⚠️ **partly met — see below.** 10 MB upload **33 ms** (≤ 5 s ✅); 1 MB image, 50 at once, first byte **p95 32–42 ms** (≤ 100 ms ✅); 10,000-file library, first/middle/last page **8/7/6 ms** (≤ 1 s ✅); other admin requests during 10 × 20 MiB uploads **p95 27–28 ms against 5–6 ms idle** (limit 2×, ❌ on this workstation) |
| 10 | Logs, privacy, offline | SC-012, SC-013 | ✅ `LoggingTests`: no canary file name in any captured event across upload, refusal, list, preview, serve, miss, delete and sweep; `NoEgressTests`: zero outbound calls; admin lint clean, 39/39 screen tests, including the i18n and keyboard checks |

**8 of 10 fully pass; §4 and §9 are open (details below).** Full suite: 228 unit, 20
architecture, 242 integration (3 Unix-only cases skipped on Windows; they run in CI).
Merged line coverage 90.8%; `QuickBuilder.Media` 90.3%, `QuickBuilder.Shared.Infrastructure`
87.6%.

## Deviations found by this run

**The local provider's delete was not idempotent under concurrency on Windows.** Two
concurrent sweeps failed intermittently with "access denied": a second delete of a
"delete pending" file does not succeed on Windows. `DeleteAsync` now waits briefly and
succeeds once the file is gone. The conformance suite gained a concurrent-delete case, so
every future provider is held to it.

**The storage validator created the directory before checking where it was.** Pointing
`Storage:Local:Path` at `/app/media` failed with "cannot be created: access denied" rather
than naming the real problem, and on a writable application directory it would have
created a folder there first. The location is now checked against the application's
directories before anything is created, with a unit test that nothing is left behind.

**Upload bursts took every core from the rest of the admin surface.** Ten 20 MiB uploads
at full speed moved other admin requests from p95 5 ms to 55 ms. An upload gate now allows
`ProcessorCount − 1` uploads to write at once (research M-23), which brought it to
27–28 ms: half, but still above the 2× limit. Two experiments ruled out thread-pool
starvation (a higher minimum changed nothing) and disk contention (no `fsync` was no
better). What remains is CPU and network contention inside Docker Desktop's single VM,
where the load generator, PostgreSQL and the app share cores. **This half of SC-008 must
be re-measured on reference Linux hardware before it can be called met.**

**The first performance numbers measured Docker Desktop, not the app.** Sent from the
Windows host, 50 concurrent 1 MB downloads showed a first-byte p95 of 273 ms. The same
requests from a container on the compose network showed 53 ms. The Docker Desktop port
forwarder was carrying the bulk data. `MediaPerformanceTests` now runs its bulk
measurements from a client container on the compose network, as a reverse proxy would
reach the app.

**NFC normalisation of file names was dropped.** The application runs with invariant
globalization, where `string.Normalize` does nothing. Names are kept as given (research
M-17). This was only ever for tidy display.

**Configuration failures exit with code 139.** Every invalid setting, including 002's
existing ones, stops the container with the right message but exit code 139 rather than
a clean non-zero code. This predates this feature and is worth its own issue.

## What this run does not establish

- **§4 in real browsers.** The manual check of a GIF/JS polyglot and a script-bearing
  PDF in current Chromium and Firefox is a **merge gate** (plan Risks, T087). It needs a
  person with both browsers.
- **The SC-008 interference half on reference hardware** (see above).
- **Linux-only behaviour on this machine**: the read-only and unreadable storage cases
  (`StartupTests`, `StorageStartupValidationTests`) and the `0640` file mode are skipped on
  Windows and run in CI.
- **Issue #3 exists.** The local GitHub token lacks the `repo` scope. CI's `spec-ids.yml`
  check will confirm it.
