# Data Model: End-to-End Test Harness

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

The suite stores nothing durable. These are the objects it creates and passes around during
one run, and the one file it keeps in the repository.

## Installation

A throwaway deployment from the image under test (spec, Key Entities).

| Field | Type | Notes |
|---|---|---|
| `kind` | `'seeded' \| 'fresh' \| 'subpath' \| 'upgrade'` | Decides the overlays and whether setup runs (E2E-03). |
| `project` | string | Compose project: `qb-e2e-<kind>`. It is never the operator's default project, so `down -v` cannot touch a real deployment. |
| `port` | number | Loopback port: 18181 to 18184 by kind. |
| `basePath` | string | `''`, or `'/cms'` for `subpath`. |
| `adminUrl` | string | `http://127.0.0.1:<port><basePath>/admin/` |
| `image` | string | `QB_E2E_IMAGE`; for `upgrade`, first `QB_E2E_PREVIOUS_IMAGE`, then `QB_E2E_IMAGE`. |
| `session` | per test | Each test signs in through the API for a session of its own (E2E-04). None for `fresh` and `upgrade`, whose tests start signed out. |

**Lifecycle**:

```text
not started ──up -d──▶ starting ──/health/ready 200──▶ ready ──claim (all but fresh)──▶ seeded
     ▲                    │ timeout 3 min → run fails with the last 40 log lines
     └──── down -v ◀── logs saved ◀── (teardown, always)
```

`upgrade` adds a step between `seeded` and teardown: `up -d` with the candidate image →
`ready`.

**Rules**:

- An installation is started only if at least one selected test needs it. Its setup project
  runs only when a project depending on it has selected tests (E2E-03).
- Logs are always saved before `down -v`. That happens in the teardown project, or in the
  setup project itself when the start fails (E2E-12).

## Seeded administrator

| Field | Value |
|---|---|
| `email` | `operator@e2e.test` |
| `password` | `e2e test passphrase 2026` (throwaway; spec, Assumptions) |
| `displayName` | `E2E Operator` |

The setup wizard test creates its own administrator on the `fresh` installation with
different values, so the two never collide.

## Test site

Created by the `seed` fixture for one test (FR-005).

| Field | Type | Notes |
|---|---|---|
| `id` | GUID | From `POST /api/v1/sites`. |
| `hostName` | string | `s-<8 hex>.e2e.test`, unique per test (E2E-05). |
| `visitorUrl` | string | `http://<hostName>:<port><basePath>/` |
| `pages` | `{ id, path }[]` | Pages the fixture created (home, plus any the test asks for). |

**Rule**: a test never reads or changes another test's site. The tests share nothing but the
installation and the administrator.

## Screen scan result

Produced by `scan(screen)` (E2E-09).

| Field | Type | Notes |
|---|---|---|
| `screen` | string | Stable screen name, such as `sites`, `page-tree`, `editor`, `media`, `trash`, `sign-in` or `setup`. |
| `failing` | violation[] | Critical or serious violations not on the exceptions list. They fail the test. |
| `excepted` | `{ rule, screen }[]` | Listed exceptions that matched. Written to `test-results/a11y-matched/<worker>.json`. |
| `reported` | violation[] | Moderate or minor violations. Attached to the test as `a11y-<screen>.json`. |

## Accessibility exception (persisted: `tests/e2e/a11y-exceptions.json`)

See [contracts/a11y-exceptions.md](contracts/a11y-exceptions.md).

| Field | Type | Rules |
|---|---|---|
| `rule` | string | An axe rule id, such as `color-contrast`. |
| `screen` | string | One screen name from the list above. |
| `reason` | string | Required and non-empty: why the violation is accepted for now. |
| `issue` | number or null | The issue tracking its fix, if one exists. |

**Rules**:

- The pair (`rule`, `screen`) is unique.
- With `QB_E2E_CHECK_EXCEPTIONS=1` (set in CI), a run fails if a listed pair never matched
  (FR-023b).
- New entries need a reason and are reviewed like code.

## Failure artifacts

Kept for each failed test, and each installation's logs for any failed run (FR-018).

| Artifact | Where | Kept |
|---|---|---|
| Trace | `test-results/<test>/trace.zip` | on failure |
| Video | `test-results/<test>/video.webm` | on failure |
| Screenshot | `test-results/<test>/test-failed-1.png` | on failure |
| Compose logs | `test-results/compose-<project>.log` | always written; uploaded on failure |
| HTML report | `playwright-report/` | uploaded on failure, 7 days |
