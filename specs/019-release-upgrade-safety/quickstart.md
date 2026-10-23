# Quickstart: Validating Release Pipeline and Upgrade Safety

**Feature**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

How to prove each user story works. Details live in the contracts; this guide only says what
to run and what to look for.

## 0. Prerequisites

- Docker with Buildx; .NET 10 SDK; `gh` signed in with access to the repository.
- Approval is the **Promote release** workflow (no environment needed). For native arm64
  builds on a public repository, set the repository variable `ARM64_RUNNER=ubuntu-24.04-arm`.
- (Was: the GitHub environment `release` with the maintainer as required reviewer, limited
  to `v*` tags (contracts/release-pipeline.md §1).
- Tag `pre-0.1.0-baseline` on commit `5f549ae` is pushed (RL-15).
- For a dry run without publishing a real version, use a release-candidate tag
  (`v0.1.0-rc.1`): it runs every gate and never moves `latest`.

## 1. Local checks (minutes, no registry)

```bash
dotnet test tests/QuickBuilder.UnitTests --filter "FullyQualifiedName~Release|FullyQualifiedName~Installation|FullyQualifiedName~SchemaUpgrade"
dotnet test tests/QuickBuilder.IntegrationTests --filter "FullyQualifiedName~Installation|FullyQualifiedName~RefusalHost|FullyQualifiedName~MigrateCommand|FullyQualifiedName~MigrationRerun|FullyQualifiedName~SchemaRead"
dotnet run --project tools/QuickBuilder.Release -- migrations check --since pre-0.1.0-baseline
dotnet run --project tools/QuickBuilder.Release -- notes --version 0.1.0 --previous pre-0.1.0-baseline --out /tmp/notes.md
```

Expected: all green; `migrations check` reports no edited migration; `/tmp/notes.md` has every
section of contracts/release-notes.md §1, with "None." where empty.

## 2. US4 — startup refuses unsafe combinations (local images)

```bash
docker build -t qb:0.2.0 --build-arg QB_VERSION_SUFFIX= .              # with VersionPrefix 0.2.0
docker build -t qb:0.1.0 --build-arg QB_VERSION_SUFFIX= <0.1.0 checkout>
QUICKBUILDER_IMAGE=qb:0.2.0 docker compose up -d                       # fresh install at 0.2.0
QUICKBUILDER_IMAGE=qb:0.1.0 docker compose up -d app                   # older minor
curl -s 127.0.0.1:8080/health/ready
docker compose logs app | grep -i "cannot run on it"
```

Expected: readiness `503` with `"state":"Incompatible"` and no version numbers; one Critical
log line naming 0.2.0 and 0.1.0 and linking the rollback section; `/` answers 503 plain text;
no restart loop (`docker compose ps` shows the container up, unhealthy). Swap back to
`qb:0.2.0`: ready, no migration applied (log "already up to date"). Then as Administrator,
Settings → About shows `0.2.0` and `Database: 0.2.0`.

## 3. US1, US2, US3 — a release candidate end to end

```bash
git tag v0.1.0-rc.1 && git push origin v0.1.0-rc.1
gh run watch   # the "Release" workflow
```

Expected, in the run:

| Job | Look for |
|---|---|
| prepare | `kind=prerelease previous=none`; sources `["baseline"]`; notes artifact |
| build ×2, manifest | one index digest with amd64 and arm64 |
| smoke | the journey's steps all pass against `ghcr.io/…@sha256:…` (digest printed) |
| arm64-boot | ready in under 3 minutes |
| upgrade (baseline) | steps 1–10 of contracts/snapshot.md §4; "up → ready" under 5 minutes; a snapshot bundle artifact |
| candidate | a draft release `v0.1.0-rc.1` with notes, bundle, pinned compose file |
| promote | waits for approval |

Approve the `release` deployment. Expected: tag `0.1.0-rc.1` on the same digest; **no**
`latest`/`0.1`/`0` tags; the GitHub release is a published pre-release.

## 4. Gates really stop a release (negative runs, on a scratch branch)

| Break | Tag | Expected |
|---|---|---|
| Edit a shipped migration's `Up` (after 0.1.0 exists) | `v0.1.1-rc.1` | `prepare` fails naming the file (FR-017, SC-008); nothing built |
| Add a migration on a patch | `v0.1.1` | `prepare` fails: patch rule (FR-034a) |
| Fragment with `breaking: true` | `v0.2.0` | `prepare` fails: breaking change outside a major (FR-034) |
| Make the setup wizard return 500 | `v0.2.0-rc.1` | `smoke` fails; draft shows "Not available: smoke failed"; `promote` cannot be approved; logs kept (FR-009) |
| A migration that deletes a page row | `v0.2.0-rc.2` | `upgrade` step 4 fails naming `pages.pages` |
| Change a block's visible output | `v0.2.0-rc.3` | `upgrade` step 3 fails with a text diff for the affected pages |

Delete the scratch tags and draft releases afterwards.

## 5. US5 — notes and rollback by hand

Follow docs/operations.md "Upgrading and rolling back" on a test machine: install the previous
release from its pinned compose file, run the content journey (or use the admin), back up
with the documented commands, upgrade, then roll back.

Expected: after rollback the previous release is ready and shows the content as it was at
the backup, in under 15 minutes (SC-010). Skipping the restore and starting the previous
image on the upgraded database gives the refusal of §2.

## 6. Re-running and idempotency

- Re-run the whole workflow for the same rc tag: a new candidate digest; approval refuses only
  if `X.Y.Z` already exists on a *different* digest (FR-005).
- Re-run only `promote` after success: no change (same digest).
- On any upgraded deployment: `docker compose run --rm app dotnet QuickBuilder.Host.dll migrate`
  twice → exit 0, "already up to date" for every module, zero writes (SC-007).

## 7. Record results

Write `quickstart-results.md` with the run links, digests, times (up → ready, tag →
candidate) and any deviation, as 018 did.
