# Implementation Plan: End-to-End Test Harness

**Branch**: `025-e2e-harness` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md) | **Issue**: #25

**Input**: Feature specification from `specs/025-e2e-harness/spec.md`

## Summary

This feature adds a Playwright suite in its own package, `tests/e2e`. It drives the admin and
the public site in Chromium against the product's Docker image, which is started from the
reference compose file.

**Installations.** Playwright setup projects start up to four throwaway installations, each
as its own compose project, and only those that the selected tests need:

- **seeded**: claimed through the API, and shared by most tests;
- **fresh**: used only by the setup wizard test;
- **sub-path**: behind the documented Nginx location under `/cms`;
- **upgrade**: from the previous release, at release only.

**Isolation.** Each test seeds its own site through the admin's API, under a unique
`*.e2e.test` host name. Chromium maps those names to loopback.

**Accessibility.** Every screen a test visits is scanned with axe. Critical and serious
violations fail the test unless they are on an exceptions list. An exception that no longer
occurs also fails the run, so the list can only shrink.

**CI.**

- Pull requests: a new `e2e` job builds its own image and runs with no `needs`, so it runs
  in parallel with the 42-minute backend job instead of after it.
- Releases: the same suite runs against the candidate digest, plus an upgrade journey in the
  browser. It joins the gates that `candidate` waits for.

The suite closes exception E-5 (011).

## Technical Context

**Language/Version**: TypeScript 6 (strict), Node 24 (as the admin's CI job uses).

**Primary Dependencies** (dev only, in `tests/e2e/package.json`; nothing enters the image):

- `@playwright/test`
- `@axe-core/playwright`
- `typescript`
- `eslint` with `typescript-eslint` and `eslint-plugin-playwright`

**Storage**: none of the suite's own. Each installation has its own compose volumes, removed
with `down -v`.

**Testing**: this feature *is* a test suite. It is checked by:

- `tsc --noEmit` and ESLint in the `e2e` job;
- the suite itself;
- the deliberate-break check of SC-004, run once by hand and recorded in
  `quickstart-results.md`.

**Target Platform**: Chromium (Playwright's bundled build), on `ubuntu-latest` runners and on
developer machines with Docker and bash, as the procedure tests require.

**Project Type**: test harness for a web application (an ASP.NET Core host plus the Vue admin,
shipped as one image).

**Performance Goals**:

- The suite run, including starting its installations, finishes within 15 minutes (SC-002).
- The whole `e2e` job, which adds about 3 minutes for the image build and 1 minute for the
  browser install, has a 25-minute timeout. That is still well inside the backend job's 42
  minutes (E2E-13).

**Constraints**:

- No repository secrets, so pull requests from forks also run the job (FR-015).
- At most one retry, and only in CI (FR-019).
- Chromium only.
- No path filter (clarification, 2026-11-26).

**Scale/Scope**:

| Test file | Tests |
|---|---|
| `setup.spec.ts` | 1 |
| `journey.spec.ts` (core journey) | 1 (a serial test with 7 named steps) |
| `media.spec.ts` | 1 |
| `trash.spec.ts` | 1 |
| `subpath.spec.ts` | 1 |
| `upgrade.spec.ts` (release only) | 1 |

About 6 tests in 6 files, using about 8 screens (for axe), 2 workers in CI and 4
installations at most.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (below).*

| # | Principle | How this plan complies |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Not affected.** No production module changes. The suite needed no `data-testid` (E2E-08); the one admin change is an accessibility fix the suite found (E2E-09). |
| II | Easy to Install, Operate and Upgrade | **Supports it.** The suite runs the reference compose file unchanged, apart from the image and the port, which are already parameterised. It adds a browser check of the upgrade path at release (FR-016a). |
| III | Upgrades Never Lose Data | **Supports it.** At release, the upgrade journey proves that a draft and a published page survive an upgrade as a member sees them (E2E-14). |
| IV | Pluggable Infrastructure | **Not affected.** |
| V | Multi-Site Within One Installation | **Exercised.** Each test owns a site with its own host name, and host-name resolution runs in a real browser (E2E-05). |
| VI | Content Schemas Are Versioned Contracts | **Not affected.** |
| VII | Draft and Published Are Separate Worlds | **Exercised.** The core journey checks that a saved draft is not public until it is published, and that a republish replaces what visitors see. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Exercised.** Republish then view (FR-011) proves the invalidation path end to end. |
| IX | Secure by Default | **Complies.** <br>• The suite uses only loopback-bound throwaway installations and needs no secrets. <br>• Seeding uses the public API with the `X-QuickBuilder-Request` header. <br>• Failure artifacts contain only throwaway credentials. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies, and removes E-5.** This adds the browser layer that the release smoke test's journey (setup wizard, create page, publish, view) lacked. 011 FR-044's browser half is now met on every pull request and every release (E2E-15). |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• One new package, using the issue's named tool. <br>• No custom orchestration beyond `docker compose`. <br>• Helpers are limited to what FR-022 lists. |
| — | Technology Constraints | **Complies.** The tests are TypeScript in strict mode, like the admin. Operators never run Node; the suite is development-only and never shipped. |
| — | Development Workflow | **Complies.** <br>• One issue (#25), one spec folder (`specs/025-e2e-harness`), one branch, one PR that closes #25. <br>• Clarify ran on 2026-11-26. Analyze runs before implement. |

**Gate: PASS.** There are no new exceptions, and one is removed (E-5).

### Impact on operators

- **Configuration keys**: none.
- **Migrations**: none.
- **New dependencies in the image**: none.
- **Upgrade notes**: none.
- **Release-note fragment**: none. Nothing an operator sees changes.

The release workflow gains one gate (`e2e`). That is a maintainer-facing change, recorded in
[docs/contributing.md](../../docs/contributing.md) under *Making a release*.

## Project Structure

### Documentation (this feature)

```text
specs/025-e2e-harness/
├── plan.md              # This file
├── research.md          # Phase 0: decisions E2E-01 to E2E-15
├── data-model.md        # Phase 1: installations, fixtures, exceptions list
├── quickstart.md        # Phase 1: how to validate the feature
├── contracts/
│   ├── runner.md        # Environment variables, commands, tags, exit behaviour
│   ├── fixtures.md      # The helpers a test may use (FR-022)
│   └── a11y-exceptions.md  # The exceptions file's format and rules (FR-023b)
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
tests/e2e/                          # NEW: standalone npm package (E2E-02)
├── package.json                    # @playwright/test, @axe-core/playwright, typescript, eslint
├── package-lock.json
├── tsconfig.json                   # strict
├── eslint.config.js                # forbids CSS/XPath selectors (E2E-08)
├── playwright.config.ts            # Chromium, host resolver rules, retries, reporters, artifacts
├── README.md                       # The guide (FR-021)
├── compose.e2e.yml                 # Overlay: image and port only
├── compose.subpath.yml             # Overlay: Nginx under /cms, BasePath, KnownProxies (E2E-07)
├── nginx/subpath.conf              # The documented location blocks, over HTTP
├── a11y-exceptions.json            # Known critical/serious violations, each with a reason
├── setup/
│   ├── installations.ts            # Start, wait, claim, logs, down -v (E2E-03, E2E-04)
│   ├── global-setup.ts             # Docker check, image, exceptions file validation
│   ├── global-teardown.ts          # Unused-exception check when QB_E2E_CHECK_EXCEPTIONS=1 (E2E-09)
│   ├── seeded.setup.ts  fresh.setup.ts  subpath.setup.ts  upgrade.setup.ts   # Setup projects (E2E-03)
│   └── installation.teardown.ts    # Teardown projects: logs, down -v
├── fixtures/
│   ├── index.ts                    # test = base.extend({ admin, seed, scan, visitor, … })
│   ├── api.ts                      # Seeding calls through /api/v1 (E2E-04)
│   ├── a11y.ts                     # scan(screen) (E2E-09)
│   └── drag.ts                     # dragBlock fallback (E2E-10)
├── pages/                          # Page objects: open() + scan, role-based locators
│   ├── setup.ts  sign-in.ts  sites.ts  page-tree.ts  editor.ts  media.ts  trash.ts
├── reporters/summary.ts            # Job summary with flaky tests (E2E-11)
└── specs/
    ├── setup.spec.ts               # @fresh: setup wizard (FR-006)
    ├── journey.spec.ts             # sign out and in, create page, drag, edit, save, publish, view, republish (FR-007 to FR-011)
    ├── media.spec.ts               # FR-012
    ├── trash.spec.ts               # FR-013
    ├── subpath.spec.ts             # @subpath: FR-014
    └── upgrade.spec.ts             # @upgrade: FR-016a, skipped without QB_E2E_PREVIOUS_IMAGE

src/admin/src/i18n/en.json          # The one accessibility fix the first full run found (E2E-09)
.github/workflows/ci.yml            # NEW job e2e, with no needs (E2E-13)
.github/workflows/release.yml       # NEW job e2e; candidate.needs += e2e (E2E-14)
specs/011-editor-core/plan.md       # Dated note: E-5 closed (E2E-15)
tests/QuickBuilder.SmokeTests/ReferenceDeploymentSmokeTests.cs  # Comments: E-5 → the suite
docs/contributing.md                # Tests section: link to tests/e2e/README.md; release gate list
```

**Structure decision**: a new top-level test package beside the existing .NET test projects
(E2E-02). Production code changes are limited to fixes for critical or serious axe findings
on screens the suite visits (E2E-09). The first full run found one, in the trash, and it is
fixed in this feature.

## Complexity Tracking

No violations. Exception E-5 (011) is removed (E2E-15).

## Post-design Constitution Check

Re-checked after writing [data-model.md](data-model.md) and [contracts/](contracts/):

- The design adds no runtime component, configuration key or dependency to the product.
- The only production edit is an accessibility fix, which the accessibility baseline favours.

**Gate: PASS.**
