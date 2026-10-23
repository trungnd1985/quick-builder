# Contract: Release Pipeline

**Feature**: 019 · **Requirements**: FR-001 – FR-014, FR-017, FR-034a, FR-036 ·
**Research**: RL-01 – RL-06, RL-09 – RL-15, RL-18, RL-22

## 1. Triggers and permissions

- `.github/workflows/release.yml`, `on: push: tags: ['v[0-9]+.[0-9]+.[0-9]+',
  'v[0-9]+.[0-9]+.[0-9]+-rc.[0-9]+']`. Nothing else publishes an image (FR-002).
- `permissions: contents: write` (draft releases, assets), `packages: write` (GHCR).
- `concurrency: release-${{ github.ref_name }}`, `cancel-in-progress: false`.
- GitHub environment **`release`**: required reviewer = the maintainer; deployment branches
  and tags restricted to `v*`.

## 2. Jobs

| Job | Runner | Needs | Does | Fails when |
|---|---|---|---|---|
| `prepare` | ubuntu-latest | — | checkout (full history); `gh release list` → available versions; `qb-release version check`; `migrations check --since <previous>`; `patch check` (patch tags); `notes` → `notes.md` artifact; `upgrade-sources` → matrix JSON | tag ≠ `VersionPrefix`; edited migration; patch rule; notes rule; floor not set for a new major |
| `build` | matrix: ubuntu-latest (amd64), ubuntu-24.04-arm (arm64) | prepare | `docker buildx build --platform linux/<arch> --build-arg QB_VERSION_SUFFIX= --build-arg QB_REVISION=<sha> --label …  --output type=image,push-by-digest=true,name-canonical=true,push=true` → digest artifact | build error |
| `manifest` | ubuntu-latest | build | `imagetools create -t ghcr.io/…:sha-<12> <amd64>@… <arm64>@…`; output `index_digest` | — |
| `smoke` | ubuntu-latest | manifest | `QB_SMOKE_IMAGE=ghcr.io/…@<index_digest>`, `QB_SMOKE_REQUIRE_DOCKER=1`; `dotnet test tests/QuickBuilder.SmokeTests --filter Category=Smoke`; upload compose logs | journey fails |
| `arm64-boot` | ubuntu-24.04-arm | manifest | compose up with the candidate; wait `/health/ready` ≤ 3 min; `GET /api/v1/identity/status` is setup mode; upload logs | not ready |
| `upgrade` | ubuntu-latest, matrix over upgrade sources | manifest | `--filter Category=ReleaseUpgrade` with `QB_UPGRADE_FROM=<version or baseline>`, `QB_SMOKE_IMAGE`; downloads the source's snapshot assets (or builds the baseline, RL-15); the leg from the previous release also writes the new snapshot bundle as an artifact | any check of contracts/snapshot.md §4, rollback drill (RL-18) |
| `candidate` | ubuntu-latest | all above, `if: always()` | create/update the **draft** release `vX.Y.Z`: notes (with the failure banner if any gate failed), assets: snapshot bundle, `docker-compose.yml` pinned to `X.Y.Z`, `digests.txt` | — |
| `promote` | ubuntu-latest, `environment: release` | candidate and every gate succeeded | §4 | exact tag exists on another digest |

Every job that starts containers uploads `compose-logs-<job>` on failure, kept 90 days.

## 3. Image

- Name `ghcr.io/trungnd1985/quickbuilder`; one OCI index with `linux/amd64` and
  `linux/arm64`.
- Labels: `org.opencontainers.image.source=https://github.com/trungnd1985/quick-builder`,
  `.version=X.Y.Z`, `.revision=<sha>`, `.licenses=MIT`.
- `docker run … dotnet QuickBuilder.Host.dll` logs `QuickBuilder X.Y.Z+<sha>` first.

## 4. Promote

1. `docker buildx imagetools inspect ghcr.io/…:X.Y.Z` — if it exists and its digest ≠
   `index_digest`, fail: "X.Y.Z is already published as <digest>" (FR-005). If equal, skip to 3.
2. `imagetools create -t …:X.Y.Z [-t …:X.Y -t …:X -t …:latest] ghcr.io/…@<index_digest>`;
   the moving tags only when the tag has no pre-release label **and** `X.Y.Z` is the highest
   available version (a patch to an older minor moves only its own `X.Y`).
3. Replace the `{{CANDIDATE}}` references in the draft notes by the final `X.Y.Z` references
   and digests; publish the release (`draft: false`, `prerelease` for `-rc`, `make_latest`
   per step 2).

## 5. `qb-release` (tools/QuickBuilder.Release)

| Command | Input | Output / exit |
|---|---|---|
| `version check --tag vX.Y.Z[-rc.N] --available <list>` | Directory.Build.props | prints `kind=major|minor|patch|prerelease`, `previous=<v or none>`; 1 if tag ≠ prefix, tag ≤ an available version, or a major without `QbUpgradeFloor` = last minor of the previous major |
| `upgrade-sources --version V --available <list>` | — | JSON array for the matrix (RL-09); `["baseline"]` when the list is empty |
| `migrations check --since <ref>` | git | 1 naming each modified/renamed/deleted migration file |
| `patch check --since <ref>` | git | 1 naming added migrations, schema files or changed version constants |
| `notes --version V --previous <ref> --out <file>` | fragments, git | the notes (contracts/release-notes.md); 1 on any rule |
| `fragment check --base <ref>` | git | 1 if the change needs a fragment and has none (PR CI) |
| `archive --version V` | fragments | moves `unreleased/*` to `docs/releases/V/`, bumps `VersionPrefix` to the next minor |

`<list>` is the versions of published, non-pre-release GitHub releases, space-separated.

## 6. Pull-request CI changes (ci.yml)

- New job `release-checks` (checkout `fetch-depth: 0`): `migrations check --since <latest
  available tag>` (notice and pass when none), `fragment check --base origin/main`, and
  `QuickBuilder.UnitTests` filter `Release` already runs in the main test job.
- `smoke` and `performance` jobs use `-f docker-compose.yml -f docker-compose.build.yml`
  (the reference file no longer builds, RL-10).

## 7. Maintainer runbook (summary; full text in docs/contributing.md)

1. Merge everything for the release; `main` is green.
2. `git tag vX.Y.Z && git push origin vX.Y.Z` (the tag must equal `VersionPrefix`).
3. Wait for the candidate (≤ 60 min, SC-011). Read the draft notes.
4. Approve the `release` environment deployment → the release is available.
5. Open a PR with `qb-release archive --version X.Y.Z` (fragments archived, prefix bumped).

First release only: after step 3, make the GHCR package public (package settings →
visibility); tag `pre-0.1.0-baseline` must exist on 5f549ae before step 2.
