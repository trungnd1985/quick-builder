# Research: End-to-End Test Harness

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Issue**: #25

Each decision has an ID (`E2E-nn`) that the plan, tasks and code comments cite.

## E2E-01 — Test framework: Playwright Test, in TypeScript

- **Decision**: `@playwright/test`, written in TypeScript (strict), Chromium only.
- **Rationale**:
  - The issue names Playwright.
  - Playwright's own runner provides everything FR-018 and FR-019 need: one retry in CI,
    flaky tests marked in the report, and traces, videos and screenshots kept only on
    failure.
  - It drives native HTML5 drag and drop in Chromium (`locator.dragTo`). The editor uses
    native drag and drop (constitution, Editor canvas; 011 ED-02), so tests drag the way a
    member does.
  - TypeScript matches the admin's language, so contributors who build screens write their
    tests in the same language and with the same names.
- **Alternatives considered**:
  - **Playwright for .NET, inside `QuickBuilder.SmokeTests`.** This would reuse
    `ComposeDeployment`. But it has no test runner of its own, so the HTML report, the
    flaky marking and the trace viewer integration would have to be rebuilt on xUnit. It
    would also put front-end tests in a project that admin contributors do not otherwise
    touch.
  - **Cypress.** Native drag and drop needs plugins, it runs inside the page (so the
    sub-path origin check is awkward), and it does not match the issue.

## E2E-02 — Where the suite lives: its own package, `tests/e2e`

- **Decision**: a standalone npm package at `tests/e2e/`, with its own `package.json`,
  lockfile and `tsconfig.json`. It imports nothing from `src/admin`.
- **Rationale**:
  - The suite tests the built image as a black box. Importing the admin's source would let
    it pass against code the image does not contain.
  - Keeping Playwright and its browser out of `src/admin/package.json` keeps the admin's
    install and its CI job as fast as they are (the user's concern about CI time).
  - It sits beside the other test projects under `tests/`.
- **Alternatives considered**: a `src/admin/e2e` folder. This was rejected because it
  shares the admin's dependency install, and its lint and type-check would run the e2e
  code as admin code.

## E2E-03 — Installations: compose projects started once per run

- **Decision**: each installation is a pair of Playwright projects:
  - a **setup project** (`<kind>-setup`, running `setup/<kind>.setup.ts`) that starts it,
    waits for readiness and claims it;
  - a **teardown project** (`<kind>-teardown`, named by the setup project's `teardown`
    option) that saves its logs and removes it with `down -v`.

  The browser project for that kind (`seeded`, `fresh`, `subpath` or `upgrade`) depends on
  its setup project, and picks its tests by tag with `grep`. The untagged project uses
  `grepInvert`.

  Playwright runs a dependency project only when a project that depends on it has tests
  selected. So `--grep @subpath`, or a file argument naming `subpath.spec.ts`, starts only
  the sub-path installation. Global setup cannot do this, because `FullConfig` does not list
  the selected files. T026 verifies this behaviour on the Playwright version installed.

  The one-off work that every run needs stays in `globalSetup`: checking Docker, building or
  naming the image, and validating the exceptions file.

  Each installation is its own compose project, so its volumes are separate, and each
  publishes on its own loopback port:

  | Installation | Compose project | Port | Used by |
  |---|---|---|---|
  | **seeded** | `qb-e2e-seeded` | 18181 | every test except the two below |
  | **fresh** | `qb-e2e-fresh` | 18182 | the setup-wizard test only |
  | **sub-path** | `qb-e2e-subpath` | 18183 | the base-path test only |
  | **upgrade** | `qb-e2e-upgrade` | 18184 | the upgrade test, at release only |

  Setup projects with no dependency between them run in parallel, so the installations
  start at the same time. Each waits on `/health/ready`, as `ComposeDeployment` does.
- **Rationale**:
  - FR-003 requires the reference compose file. The overlay `tests/e2e/compose.e2e.yml`
    changes only the image, which is already parameterised by `QUICKBUILDER_IMAGE`, and
    the port, by `QUICKBUILDER_PORT`.
  - One seeded installation shared by all tests keeps the run inside SC-002's budget.
    Starting one installation per test would cost about 30 seconds each.
- **Alternatives considered**: Testcontainers for Node. It would add a dependency for what
  `docker compose` already does, and it would not use the reference compose file.

## E2E-04 — Seeding: the API the admin uses, never the database

- **Decision**: the `seeded-setup` project claims the seeded installation through `POST
  /api/v1/identity/setup`, using the setup code from `docker compose logs app` as
  `ComposeDeployment.ReadSetupCodeFromLogsAsync` does. Per-test data (a site, pages, a published page) comes from a
  `seed` fixture that calls the same API endpoints `OperatorApi` does in the procedure
  tests.
- **Rationale**:
  - FR-004 and FR-005.
  - Seeding through the API means the suite needs no knowledge of the database and stays
    valid across migrations.
  - **Changed during implementation: a session per test.** A first design shared the setup
    project's session through `storageState`. When the journey signed out, the server ended
    that session for every test running beside it, and they landed on the sign-in screen.
    So the `storageState` fixture now signs in through `POST /api/v1/identity/sign-in` for
    each test, and a test that signs out ends only its own session.
  - The `X-QuickBuilder-Request: 1` header (002 R-11) is sent on every state-changing seed
    call, as the admin sends it.
- **Alternatives considered**:
  - SQL seeding: couples the suite to tables.
  - Seeding through the browser: slow, and it makes every test depend on the screens it is
    not testing.

## E2E-05 — Site host names in the browser: Chromium host resolver rules

- **Decision**: every test site gets a unique host name under `e2e.test`, for example
  `s-3f9a1c.e2e.test`. Chromium is launched with `--host-resolver-rules=MAP *.e2e.test
  127.0.0.1`, so `http://s-3f9a1c.e2e.test:18181/` reaches the seeded installation with
  that `Host` header.
- **Rationale**:
  - Sites are resolved by host name (009), so a visitor test must send the site's host.
  - Host resolver rules need no `/etc/hosts` edits and no DNS, and behave the same on
    developer machines and CI.
  - `.test` is reserved (RFC 2606), so it can never reach a real site.
  - Unique host names keep parallel tests apart (FR-005).
- **Alternatives considered**: setting a `Host` header on page requests. Browsers forbid
  overriding `Host`, and the page's own sub-requests (stylesheet, images) would not carry
  it.

## E2E-06 — The admin's address: the loopback IP, and plain HTTP

- **Decision**: the admin is opened at `http://127.0.0.1:<port>/admin/`. The reference
  compose file already sets `Identity__AllowInsecureHttp: "true"` for its loopback binding,
  and `Hosting:AdminHostNames` is empty, so the admin answers on every host name.
- **Rationale**: this is exactly what an operator does on first install (finish setup from
  the machine itself; compose header comment). No overlay is needed.

## E2E-07 — The sub-path installation: the documented Nginx location over plain HTTP

- **Decision**: the sub-path installation adds an Nginx service whose `location /cms/` and
  `location = /robots.txt` blocks are copied from
  [docs/operators/reverse-proxy/index.md](../../docs/operators/reverse-proxy/index.md#sub-path-installation),
  served over plain HTTP. The app gets `Hosting__BasePath=/cms` and
  `Hosting__KnownProxies__0` set to the Nginx container's address. That address is fixed at
  `10.231.0.10` on a dedicated subnet, `10.231.0.0/24`. The subnet is outside Docker's
  default address pools (`172.17.0.0/16`–`172.31.0.0/16` and `192.168.0.0/16`), so it cannot
  overlap a network Docker assigned to another compose project.
- **Rationale**:
  - The documented procedure test (`ReverseProxyProcedureTests.Sub_path_installation_with_nginx`)
    already proves the TLS half: forwarded scheme, certificates and the HTTP-to-HTTPS
    redirect.
  - What only a browser can prove is that the admin's script works out its API address
    under `/cms` (#51), and the scheme does not affect that.
  - Plain HTTP spares the browser a certificate authority.
- **How "no request outside `/cms`" is checked (FR-014)**: the test records every request
  the page makes (`page.on('request')`) and every response (`page.on('response')`). It
  fails if any same-origin request's path does not start with `/cms/`, or if any
  same-origin response is a 404. Under a base path, a 404 is how a wrong address shows up,
  wherever it points.
- **Alternatives considered**: running the documented procedure itself in the browser test.
  This would duplicate the procedure test and its TLS setup without proving anything more
  about the admin.

## E2E-08 — Choosing elements: roles and names, test IDs only as a fallback

- **Decision**: tests use `getByRole`, `getByLabel` and `getByText` with the admin's
  English strings. Where an element has no accessible name, the admin gains a `data-testid`,
  and the guide lists it.

  **Outcome during implementation**: none was needed. The editor's drop targets already
  show text ("This page is empty.", "Drop blocks here"), the palette is a listbox of named
  options, and the layers panel is a tree of named items.

  An ESLint `no-restricted-syntax` rule in `tests/e2e` rejects string arguments to
  `locator(...)`, so CSS and XPath selectors cannot be used at all (FR-023). The `getBy*`
  methods are the only way to find an element, and `getByTestId` covers the exceptions. A
  rule that tries to recognise "CSS-looking" strings would miss forms such as `div.foo`.
- **Rationale**:
  - The admin already holds about 190 `aria-label`s and follows the accessibility baseline
    (A4: every control has a label).
  - Role-based selectors check that baseline as a side effect, and do not break when styles
    change.
- **Note**: the admin's strings go through vue-i18n with one shipped language (English).
  Tests use the English text directly, so a changed label fails the test that depends on
  it. That is intended.

## E2E-09 — Accessibility scans: axe on every screen a test visits

- **Decision**:
  - A `scan` fixture uses `@axe-core/playwright` with the WCAG 2.1 A and AA tags. The
    admin's unit tests already use `axe-core`.
  - Each page object has a `waitLoaded()` that waits for the screen's main heading or
    landmark and then calls `scan(screenName)`. Both `open()` and every navigation that
    lands on the screen end with it. For example, the page tree's `openInEditor` ends with
    `EditorPage.waitLoaded()`, so a screen reached by clicking is scanned as well as one
    reached by URL.
  - Violations with impact `critical` or `serious` fail the test, unless
    `tests/e2e/a11y-exceptions.json` lists the rule for that screen with a reason.
  - Lesser violations are attached to the test and listed in the report.
  - Every exception that matched is written to `test-results/a11y-matched/` by each worker.
  - When `QB_E2E_CHECK_EXCEPTIONS=1`, `globalTeardown` fails the run if any listed
    exception never matched (FR-023b). Both CI jobs set the variable, because they always
    run the whole suite. A developer sets it to check a full local run.

    The suite cannot tell a full run from a filtered one by itself, because `FullConfig`
    does not list the selected files. A filtered run without the variable skips the check,
    since it did not visit every screen.
- **Rationale**: FR-023a and FR-023b, and the clarification session of 2026-11-26.
- **Starting the list**: the first full run on the implementation branch records the
  existing critical and serious violations. Each one goes on the list with a reason, or is
  fixed in the admin in this PR if the fix is small, such as a missing label.

## E2E-10 — Drag and drop

- **Decision**: drag with `locator.dragTo(target)`. If a palette item's `dragstart` needs a
  `dataTransfer` payload that Playwright's synthetic drag does not carry, a helper
  `dragBlock(page, from, to)` dispatches `dragstart`, `dragover` and `drop` with a shared
  `DataTransfer` created through `page.evaluateHandle(() => new DataTransfer())`. This is
  the standard Playwright fallback for HTML5 drag and drop.
- **Rationale**: FR-009 requires the pointer path. Keyboard insertion is the editor's unit
  tests' concern (spec, Edge Cases).

## E2E-11 — Retries, flaky reporting and the job summary

- **Decision**:
  - `retries: process.env.CI ? 1 : 0`.
  - Reporters in CI: `list`, `html` (kept as an artifact) and a small local reporter,
    `reporters/summary.ts`. It writes passed, failed and flaky counts, and the names of
    flaky tests, to `$GITHUB_STEP_SUMMARY`.
  - `forbidOnly` is set in CI.
- **Rationale**: FR-019 and SC-003. Flaky tests are visible on the run page without
  downloading anything.

## E2E-12 — Failure artifacts

- **Decision**:
  - Playwright settings: `trace: 'retain-on-failure'`, `video: 'retain-on-failure'`,
    `screenshot: 'only-on-failure'`.
  - Each `<kind>-teardown` project writes its installation's `docker compose logs
    --no-color` to `test-results/compose-qb-e2e-<kind>.log` before `down -v`.
  - If a setup project fails, it saves the logs and runs `down -v` itself before rethrowing.
    So a failed start leaves nothing behind, whether or not Playwright runs the teardown
    project afterwards.
  - CI uploads `playwright-report/` and `test-results/` on failure, with `retention-days: 7`.
- **Rationale**: FR-018, SC-006.

## E2E-13 — CI placement: its own job, building its own image, with no `needs`

- **Decision**:
  - A new job, `e2e`, in `.github/workflows/ci.yml`, with no `needs`.
  - It builds the image itself (`docker build -t quickbuilder:e2e .`), installs the
    suite's packages and Chromium (`npx playwright install --with-deps chromium`), and runs
    the suite with `QB_E2E_IMAGE=quickbuilder:e2e` and `QB_E2E_REQUIRE_DOCKER=1`.
  - It has no path filter (clarification, 2026-11-26) and uses no secrets.
- **Rationale**:
  - On the last pull request run (#62), the critical path was `.NET build and test` (42
    min) → `image` (2.5 min) → `performance` (11 min), about 56 minutes in all.
  - A job that waits for `image` would start at minute 45. One that builds its own image
    from minute 0 never lengthens the wait (FR-017).
  - **Time budget**:
    - SC-002's 15 minutes covers the suite run (`npm test`), including starting its
      installations.
    - The whole job adds the image build (about 3 min) and the browser install (about 1
      min), so it has a 25-minute timeout. That is still well inside the backend job's 42
      minutes.
  - The duplicated image build costs runner minutes, not wall-clock time.
- **Alternatives considered**: sharing the `image` job's output as an artifact. That would
  tie the job to the backend tests' 42 minutes.

## E2E-14 — Release: the same suite against the candidate, plus the upgrade journey

- **Decision**:
  - A new job, `e2e`, in `.github/workflows/release.yml`, with `needs: [prepare,
    manifest]`.
  - It runs with `QB_E2E_IMAGE` set to the candidate digest and `QB_E2E_PREVIOUS_IMAGE`
    set to `ghcr.io/trungnd1985/quickbuilder:<prepare.outputs.previous>`.
  - `candidate.needs` gains `e2e`, so a failure blocks promotion as the other gates do.
  - The upgrade test (`@upgrade`) skips itself when `QB_E2E_PREVIOUS_IMAGE` is unset, which
    is the case on pull requests and on a first release with no previous version.
- **Upgrade journey** (FR-016a):
  1. Start `qb-e2e-upgrade` with the previous image (the `upgrade-setup` project).
  2. Claim it and seed through the API: a site, a published page, and a draft with
     different content.
  3. Run `compose up -d` again with `QUICKBUILDER_IMAGE` set to the candidate, keeping the
     volumes. This is the step `docs/operators/upgrade.md` tells operators to take.
  4. Wait for readiness. The setup project saves no session for this kind. Then, in the
     browser, the test signs in through the form (which also proves sign-in survives the
     upgrade), opens the draft in the editor and finds its content, then visits the site
     and finds the published content.
- **Rationale**: the existing upgrade legs compare data over HTTP. This adds what a member
  sees after upgrading. The seed calls use `/api/v1` endpoints that previous releases
  already serve.

## E2E-15 — Closing E-5

- **Decision**: [specs/011-editor-core/plan.md](../011-editor-core/plan.md) gets a dated
  note after its Complexity Tracking table, as 001's plan has for E-3. The note records that
  the browser half of 011 FR-044 is now met:
  - `journey.spec.ts` opens the editor, drags a Section and a Heading, and confirms the
    saved draft by reloading;
  - the suite runs on every pull request (`ci.yml` `e2e`) and every release candidate
    (`release.yml` `e2e`).

  The comments in `ReferenceDeploymentSmokeTests` that mention E-5 are updated to point at
  the suite.
