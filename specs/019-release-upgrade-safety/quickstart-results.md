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
