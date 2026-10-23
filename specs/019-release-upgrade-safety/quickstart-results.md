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
