# Contract: Running the Suite

What a developer, the CI job and the release job may rely on.

## Commands (from `tests/e2e/`)

| Command | Effect |
|---|---|
| `npm ci && npx playwright install chromium` | One-time install. |
| `npm test` | Builds `quickbuilder:e2e` from the repository root if `QB_E2E_IMAGE` is unset, then runs every test except `@upgrade`. |
| `npm test -- --grep @subpath` | Only the tests tagged `@subpath`. Only the installations they need start. |
| `npm run report` | Opens the last HTML report. |
| `npm run check` | `tsc --noEmit` and ESLint. |

## Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `QB_E2E_IMAGE` | unset: build `quickbuilder:e2e` | The image under test, by tag or digest. |
| `QB_E2E_PREVIOUS_IMAGE` | unset: `@upgrade` skips | The release to upgrade from. Set only by `release.yml`. |
| `QB_E2E_REQUIRE_DOCKER` | unset | `1` in CI: a missing Docker is a failure, not a stop (FR-020). |
| `QB_E2E_CHECK_EXCEPTIONS` | unset | `1` in CI: fail the run if an accessibility exception never matched (FR-023b). Set it only for a full run. |
| `CI` | set by GitHub | One retry, `forbidOnly`, the summary reporter. |

## Tags

| Tag | Installation | Runs on |
|---|---|---|
| none | seeded | pull requests and releases |
| `@fresh` | fresh | pull requests and releases |
| `@subpath` | sub-path | pull requests and releases |
| `@upgrade` | upgrade | releases only (skips without `QB_E2E_PREVIOUS_IMAGE`) |

## Exit behaviour

| Situation | Locally | CI (`QB_E2E_REQUIRE_DOCKER=1`) |
|---|---|---|
| Docker unavailable | Prints "Docker is not available: start Docker and run again." and exits 1 before running tests. | Fails. |
| An installation never becomes ready within 3 min | Fails, printing the last 40 log lines. | Same. |
| A test fails, then passes on retry | n/a (no retries) | The run passes; the test is listed as flaky in the job summary. |
| A test fails twice | n/a | The run fails; artifacts are uploaded. |
| An accessibility exception never matched | Fails in global teardown, naming it, only with `QB_E2E_CHECK_EXCEPTIONS=1`. | Fails (the variable is set). |

## Ports and names reserved

- Loopback ports 18181 to 18184.
- Compose projects `qb-e2e-seeded`, `qb-e2e-fresh`, `qb-e2e-subpath` and `qb-e2e-upgrade`.
- These do not collide with the smoke tests, which use 18080 and `quickbuilder-smoke`, so
  the two suites can run on one machine.
