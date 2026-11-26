# Quickstart: Validating the End-to-End Harness

**Feature**: [spec.md](spec.md) | **Contracts**: [runner](contracts/runner.md),
[fixtures](contracts/fixtures.md), [a11y exceptions](contracts/a11y-exceptions.md)

Record the results of these steps in `quickstart-results.md` in this folder.

## Prerequisites

- Docker running, and bash on the path (as for the procedure tests).
- Node 24.
- About 6 GB of free disk space for the image and the containers.

```bash
cd tests/e2e
npm ci
npx playwright install chromium
```

## §1 — The full pull-request suite passes locally (SC-001, US1 to US3)

```bash
npm test
```

**Expect**:

- The image builds, and the seeded, fresh and sub-path installations start.
- Every test passes. `@upgrade` is reported as skipped.
- The run prints where the HTML report is.
- `docker ps -a --filter name=qb-e2e` is empty afterwards.

## §2 — Docker missing (FR-020)

Stop Docker, then run `npm test`.

**Expect**: one line saying Docker is not available, exit code 1, and no test results.

## §3 — Deliberate breaks fail with the step named (SC-004)

Make each change on a scratch branch, run `npm test`, then revert it.

| Break | Expected failing test and step |
|---|---|
| In `src/admin`, change the editor's draft save URL | `journey.spec.ts`: "edit properties and the draft saves" |
| In `src/admin`, remove the Publish button's handler | `journey.spec.ts`: "publish and view" |
| In `src/admin/src/app/base.ts`, set `ADMIN_BASE` to `'/admin/'` | `subpath.spec.ts`: a request outside `/cms` |

For each break, open the HTML report and check that the trace, video and screenshot are
attached, and that `test-results/compose-*.log` exists (SC-006).

## §4 — Accessibility exceptions (FR-023a, FR-023b)

1. Remove one entry from `a11y-exceptions.json`. **Expect**: the test that visits that screen
   fails, naming the rule and the screen.
2. Add an entry for a rule that does not occur, such as `{"rule":"blink","screen":"sites","reason":"x"}`.
   **Expect**: `QB_E2E_CHECK_EXCEPTIONS=1 npm test` fails in global teardown, naming that
   entry. `npm test -- --grep @subpath`, without the variable, does not fail on it.

## §5 — Parallel tests stay apart (FR-005)

```bash
npm test -- --workers=4 --repeat-each=3 --grep-invert @fresh
```

**Expect**: everything passes, and no test sees another test's site. The setup wizard test
(`@fresh`) is left out: an installation can be claimed only once, so repeating it on the
same installation fails by design.

## §6 — CI (FR-015, FR-017, FR-018, FR-019, SC-002)

On the pull request for this feature:

- The `e2e` job starts at the same time as `Build and test (.NET)`.
- Its `npm test` step finishes within 15 minutes, and the whole job within its 25-minute
  timeout.
- The job summary lists the passed, failed and flaky counts.
- On a failing run (from §3, pushed to a scratch branch), the `e2e-report` artifact holds the
  report, the traces and the compose logs.

## §7 — Upgrade journey (FR-016a)

```bash
QB_E2E_PREVIOUS_IMAGE=ghcr.io/trungnd1985/quickbuilder:<latest release> npm test -- --grep @upgrade
```

**Expect**:

- The previous release starts and is seeded, then the image under test replaces it with the
  volumes kept.
- The draft opens in the editor with its content, and the visitor sees the published
  heading.

## §8 — A new test from the guide alone (SC-005, US4)

Someone who has not seen the suite follows `tests/e2e/README.md` to add a test for the
Settings screen.

**Expect**: the test passes locally and in CI within an hour. Record the time taken.
