# Quickstart Results: Release Pipeline and Upgrade Safety

Runs of [quickstart.md](./quickstart.md) and of the release gate during implementation. Each
entry says what ran, where, and what it showed.

## T028 — the upgrade gate, run locally (2026-10-23)

Machine: Windows 11, Docker Desktop 29.8.2, the developer's workstation (not the reference
2 vCPU / 4 GB setup; `docker-compose.gate.yml` was not layered for these runs).

### Leg from the pre-release baseline

`QB_UPGRADE_FROM=baseline QB_BASELINE_REF=5f549ae QB_SMOKE_IMAGE=quickbuilder:local`
(the image built from this branch), `QB_SNAPSHOT_OUT` set.

| Step | Result |
|---|---|
| Baseline | image built at 5f549ae, set up, filled by the content journey, dumped |
| 1–2 | restored; the candidate was ready **2 s** after start |
| 3–4 | **11 pages** and **51 tables** as before |
| 5 | 4 members signed in; 7 drafts opened |
| 6 | background rewrites done; pages unchanged |
| 7 | a restart wrote nothing |
| 8 | snapshot of `0.1.0-dev` written: dump 197 KB, media 1.7 MB, expectations 12 KB |
| 9 | the baseline image starts on the upgraded database (it predates the check) |
| 10 | rolled back to the baseline in **15 s** |

Whole leg: 1 min 16 s.

Found and fixed on the way:
- The capture's stale-volatile-list check must only run on the release under test: the
  baseline predates `platform.database_version`.
- A restart writes `media.storage_checks` (the storage probe) and `pages.redirect_usage`
  (redirect hit counters): runtime bookkeeping, now on the volatile list with reasons.
- Menus have no `GET …/draft`; a menu's draft is read at the menu's own address.

### Leg from a stored release snapshot

`QB_UPGRADE_FROM=0.1.0-dev QB_SNAPSHOT_DIR=<the bundle above> QB_PREVIOUS_IMAGE=quickbuilder:local`.

| Step | Result |
|---|---|
| 1–2 | restored from the bundle; ready in 2 s |
| 3–4 | **22 pages** and 51 tables as before — the baseline's site and 0.1.0-dev's own, merged |
| 5 | 7 members signed in; 14 drafts opened |
| 6–7 | unchanged; a restart wrote nothing |

This proves the bundle format round-trips and that snapshots accumulate (FR-014).

## Quickstart §2 — the startup refusal on a real container (2026-10-23)

Image built from this branch (`0.1.0-dev`), reference compose file, project `qb-refusal`.

1. Fresh install: ready.
2. `UPDATE platform.database_version SET recorded_version = '0.9.0'`, then `restart app`.
3. `/health/ready` → `503 {"status":"Unhealthy","state":"Incompatible","detail":"This database was
   upgraded by a newer QuickBuilder release. Start that release, or restore the backup taken
   before the upgrade. See the logs.","checks":[]}` — no version numbers.
4. `/` → `503` plain text, "QuickBuilder cannot start on this database. …".
5. One Fatal line: "Database was migrated by QuickBuilder 0.9.0; this is 0.1.0-dev, which cannot
   run on it. Start 0.9.0 or newer, or restore the backup taken before upgrading: …#rolling-back".
6. The container stays up (no restart loop); compose reports it unhealthy.

Found on the way: ASP.NET Core inserts authentication and authorization middleware by itself
when their services exist, and Identity's fallback policy answered 401 before the refusal
pipeline; the refusal pipeline now switches that off.

## T085 — the rest of the quickstart (2026-10-23)

| § | What ran | Result |
|---|---|---|
| 1 | Unit, architecture and the 019 integration suites; `qb-release migrations check --since 5f549ae` and `notes --version 0.1.0 --previous 5f549ae` on this repository | All green. No shipped migration changed since the pre-019 commit. The 0.1.0 notes have every section, 16 fragments, the `platform` migration listed, "None." where empty. |
| 2 | The refusal on a real container | See above. |
| 3, 4 | A release-candidate run and the negative runs | **Not run**: they need a tag pushed to GitHub, the `release` environment and GHCR (T004, T044). |
| 5 | The guide's backup and rollback commands, by hand on a real stack: set up, back up (`pg_dump` to stdout, media tar), remove the volumes, restore (`pg_restore` from stdin, untar, `chown 1654`), start | Ready (200); the pre-backup administrator signs in; the media file comes back owned by the image's user. The gate drills the same procedure on every release (T028: 15 s). Found: Git Bash on Windows rewrites `/out`-style paths; the guide now says to use WSL or `MSYS_NO_PATHCONV=1`. |
| 6 | `migrate` twice; restart on an upgraded database | Exit 0 both times and zero writes the second time (`MigrateCommandTests`, `MigrationRerunTests`); a restart wrote nothing (T028 step 7). |

Timings: upgrade from the baseline to ready in 2 s, rollback in 15 s, whole upgrade leg about a
minute — on a workstation, not the reference shape. SC-001's five minutes and SC-011's 60 minutes
are measured in the first release-candidate run (T044).

## T082 — coverage (2026-10-23)

Full non-smoke run with coverage: 59 architecture, 2,420 unit and 1,340 integration tests
passed (3 skipped: Unix-only storage permission tests). Line coverage of the 019 critical paths:

| Code | Lines |
|---|---|
| `InstallationGate`, `PlatformRecord`, `InstallationVersion`, `SchemaUpgradeChain`, `ApplicationVersion`, `StoredDocuments`, `RefusalHost`, `GetInstallation` | 100 % |
| `SemanticVersion` 97.6 %, `CollectionSchemaUpgrades` 95.8 %, `DesignTokenUpgrades` 95.5 %, `SchemaRead` 92.3 % | |
| `ModuleMigrationRunner` | 83.2 % |
| `InstallationPreflight` | 63.4 % → its "cannot decide" paths covered by `InstallationPreflightTests` (added) |
| Packages: QuickBuilder.Shared.Infrastructure 89.6 %, QuickBuilder.Host 92.6 % | above the 80 % gate |

`PlatformDbContextFactory` (design time only) is not run by tests, as for every module.

## T044 — the first release candidates on GitHub (2026-10-24)

Quickstart §3. Each run built on `ubuntu-latest`, arm64 under QEMU (no `ARM64_RUNNER`).

| Tag | Run | Result |
|---|---|---|
| `v0.1.0-rc.1` | [40006932794](https://github.com/trungnd1985/quick-builder/actions/runs/40006932794) | Failed: the upgrade gate parsed the image's user from `docker run` output that also held the pull progress; the arm64 boot set `DOCKER_DEFAULT_PLATFORM` for the whole job, so setup-qemu pulled an arm64 helper it could not run. Fixed in #42. |
| `v0.1.0-rc.2` | [40048536868](https://github.com/trungnd1985/quick-builder/actions/runs/40048536868) | Failed at upgrade step 8: `pg_dump` could not overwrite the restored `/tmp/snapshot.dump`, which `compose cp` left owned by the runner on Linux (Docker Desktop hid it). Steps 2–7 passed; ready 4 s after start. Fixed in #43. |
| `v0.1.0-rc.3` | [40054875621](https://github.com/trungnd1985/quick-builder/actions/runs/40054875621) | **Every job passed**; promoted by [40056884687](https://github.com/trungnd1985/quick-builder/actions/runs/40056884687). |

The failed drafts carried "Not available: …" banners, so Promote would have refused them.
None of the three failures was in QuickBuilder itself; all were in the gates, and all were
Linux-runner differences a Windows workstation could not show.

`v0.1.0-rc.3`:

- Digest `sha256:ec197eb0a3499b386ccb8fffce2befd524222ec87e3ac5b202c0431f3ee840ce`, linux/amd64 and linux/arm64.
- Tag → candidate: 28 min (02:36:28 → 03:04:35 UTC), within SC-011's 60 minutes. The arm64
  build under QEMU took 23 of them (amd64: 2).
- Smoke journey against the published digest: passed (1 min 30 s).
- arm64 boot: `uname -m` = `aarch64`, ready after 168 s under emulation (budget 3 min, FR-010).
- Upgrade from the baseline: all 10 steps passed in 3 min 11 s, and the draft carries the
  0.1.0-rc.3 snapshot (dump 197 KB, media 1.7 MB, expectations). Up → ready was 4 s in the
  rc.2 run (xUnit prints the step lines only on failure), within SC-001's five minutes.
- Notes: every section present, no unreplaced markers, "Tested in this release from: `baseline`".
- Promote: the release is a published pre-release; the registry has `0.1.0-rc.3` on the
  tested digest and no `latest`, `0.1` or `0`, as a release candidate should.

- Public image (FR-003), 2026-10-24: the maintainer made the GHCR package public (Package
  settings → Change visibility; there is no API for it). After `docker logout ghcr.io`, the
  pull of `0.1.0-rc.3` succeeded and the index lists linux/amd64 and linux/arm64. The release's
  own `docker-compose.yml`, downloaded from the release, started with no credentials: ready
  after 6 s, `setupRequired: true`, first log line `QuickBuilder 0.1.0-rc.3 (65aef83e4543)`.

Still to do for T044:

- The negative runs of quickstart §4 (setup wizard returning 500; a migration deleting a page
  row) on a scratch branch.
