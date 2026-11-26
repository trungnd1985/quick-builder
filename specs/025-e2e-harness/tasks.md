# Tasks: End-to-End Test Harness

**Input**: Design documents from `specs/025-e2e-harness/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: this feature *is* a test suite. Its browser tests are the user stories'
implementation. They are not extra test tasks. The suite's own checks are type-check, lint and
the deliberate breaks in quickstart §3.

**Organization**: tasks are grouped by user story, so that each story can be built and checked
on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks).
- **[Story]**: US1 to US5, as in spec.md.

All paths are relative to the repository root. The suite lives in `tests/e2e/`
(E2E-02).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the package, its tooling and its compose overlays.

- [X] T001 Create `tests/e2e/package.json` (private, `"type": "module"`) with devDependencies `@playwright/test`, `@axe-core/playwright`, `typescript`, `eslint`, `typescript-eslint` and `eslint-plugin-playwright`. Add scripts: `test` (`playwright test`), `report` (`playwright show-report`), `check` (`tsc --noEmit && eslint . --max-warnings 0`). Run `npm install` to produce `tests/e2e/package-lock.json`.
- [X] T002 [P] Create `tests/e2e/tsconfig.json`, with strict mode, `"module": "NodeNext"`, `"noEmit": true`, and include `**/*.ts`.
- [X] T003 [P] Create `tests/e2e/eslint.config.js`. It uses typescript-eslint's recommended rules and `eslint-plugin-playwright`'s recommended rules, and adds a `no-restricted-syntax` rule that rejects **every** string-literal or template-literal argument to a `.locator(...)` call, with the message "Find elements with getByRole/getByLabel/getByText, or getByTestId for the IDs in README.md" (FR-023, E2E-08).
- [X] T004 [P] Add `tests/e2e/node_modules/`, `tests/e2e/test-results/` and `tests/e2e/playwright-report/` to the repository's `.gitignore`.
- [X] T005 [P] Create `tests/e2e/compose.e2e.yml`. It is an overlay that sets nothing beyond what `QUICKBUILDER_IMAGE` and `QUICKBUILDER_PORT` already parameterise in `docker-compose.yml`. Its header comment explains that it exists so every installation names the same file list (E2E-03).
- [X] T006 [P] Create `tests/e2e/nginx/subpath.conf`: a `server { listen 80; }` block holding the `location /cms/` and `location = /robots.txt` blocks copied from `docs/operators/reverse-proxy/index.md` (sub-path section), with the `proxy_set_header` lines, over plain HTTP (E2E-07).
- [X] T007 Create `tests/e2e/compose.subpath.yml`. It adds an `nginx` service (`nginx:1.27.3`, mounting `nginx/subpath.conf`, fixed address `10.231.0.10` on a dedicated subnet `10.231.0.0/24`, which is outside Docker's default address pools, published on `127.0.0.1:${QUICKBUILDER_PORT}:80`). It sets the app's `Hosting__BasePath: "/cms"` and `Hosting__KnownProxies__0: "10.231.0.10"`, and removes the app's own published port with `ports: !reset []` (E2E-07). Depends on T006.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: installations, fixtures, accessibility scans and configuration that every story
uses.

**⚠️ CRITICAL**: no story can start until this phase is complete.

- [X] T008 Create `tests/e2e/setup/installations.ts` (data-model *Installation*). It provides:
  - `KINDS`: the per-kind project, port, base path and overlays (`seeded`/`fresh`/`upgrade` use `docker-compose.yml` + `compose.e2e.yml`; `subpath` adds `compose.subpath.yml`);
  - `compose(kind, args, env)`, which runs `docker compose -p qb-e2e-<kind> -f … <args>` from the repository root, with `QUICKBUILDER_IMAGE` and `QUICKBUILDER_PORT` set;
  - `start(kind, image)`, which runs `down -v` then `up -d`;
  - `waitReady(kind)`, which polls `<basePath>/health/ready` every 2 s for up to 3 min, then throws with the last 40 log lines;
  - `setupCode(kind)`, which reads `docker compose logs --no-log-prefix app` and finds the JSON line with `SetupCode`, as `tests/QuickBuilder.SmokeTests/ComposeDeployment.cs` does;
  - `claim(kind, setupCode)`, which calls `POST <basePath>/api/v1/identity/setup` with the seeded administrator and `X-QuickBuilder-Request: 1`, and returns the `qb_session` cookie;
  - `saveLogs(kind)`, which writes `test-results/compose-qb-e2e-<kind>.log`;
  - `stop(kind)`, which runs `down -v`.
- [X] T009 Create `tests/e2e/setup/global-setup.ts`, which does only the one-off work every run needs (E2E-03):
  - check `docker info`. When it fails: if `QB_E2E_REQUIRE_DOCKER=1`, throw "Docker is not available: start Docker and run again.", which fails the job; otherwise print that message and `process.exit(1)` before any test runs (FR-020, contracts/runner.md);
  - resolve the image: `QB_E2E_IMAGE`, or else build `quickbuilder:e2e` from the repository root with `docker build`, then export the result as `process.env.QB_E2E_IMAGE` so setup projects inherit it;
  - validate `a11y-exceptions.json` (contracts/a11y-exceptions.md, rules 1 to 3);
  - empty `test-results/a11y-matched/`.

  Depends on T008.
- [X] T010 Create the setup and teardown projects' files (E2E-03, E2E-04, E2E-12):
  - `tests/e2e/setup/seeded.setup.ts` and `tests/e2e/setup/subpath.setup.ts`: `start`, `waitReady`, `claim`, then write the request context's `storageState()` (cookies with the path the server set, such as `/cms`) to `test-results/.auth/<kind>.json`;
  - `tests/e2e/setup/fresh.setup.ts`: `start` and `waitReady` only (the setup wizard test claims it);
  - each setup file wraps its body so that, on any error, it calls `saveLogs` and `stop` and then rethrows;
  - `tests/e2e/setup/installation.teardown.ts`: one test that reads the kind from `test.info().project.name` (`<kind>-teardown`), then calls `saveLogs(kind)` and `stop(kind)`.

  Depends on T008.
- [X] T011 Create `tests/e2e/setup/global-teardown.ts`. When `QB_E2E_CHECK_EXCEPTIONS=1`, it reads `test-results/a11y-matched/*.json` and fails, naming each unused entry, if any entry in `a11y-exceptions.json` never matched. Without the variable it does nothing (FR-023b, E2E-09). Depends on T008.
- [X] T012 [P] Create `tests/e2e/fixtures/api.ts`. `Seed` takes a request context bound to an installation's base URL with the session cookie and `X-QuickBuilder-Request: 1`, and provides:
  - `site(name?)`, with host name `s-<8 hex>.e2e.test`;
  - `page(site, {title, slug, parent?})`;
  - `draft(site, page, document)`;
  - `publishedPage(site, {heading})`, which builds the Section→Heading document the way `tests/QuickBuilder.SmokeTests/Procedures/OperatorApi.cs` `Document(...)` does.

  Use the same endpoint paths as `OperatorApi.cs` (contracts/fixtures.md, E2E-04).
- [X] T013 [P] Create `tests/e2e/fixtures/a11y.ts`. `scan(page, screen)` runs `AxeBuilder` with tags `wcag2a`, `wcag2aa`, `wcag21a` and `wcag21aa`. It splits the violations into:
  - failing: critical or serious, and not excepted;
  - excepted;
  - reported: moderate or minor.

  It attaches `a11y-<screen>.json`, appends the excepted pairs to `test-results/a11y-matched/<workerIndex>.json`, and fails with each failing rule id, the screen and the node targets (FR-023a, E2E-09).
- [X] T014 [P] Create `tests/e2e/fixtures/drag.ts`. `dragBlock(page, source, target)` tries `source.dragTo(target)` first, and checks success with a caller-supplied `expectDropped()` assertion. If that fails, it dispatches `dragstart`, `dragenter`, `dragover`, `drop` and `dragend` with one `DataTransfer` from `page.evaluateHandle(() => new DataTransfer())` (E2E-10).
- [X] T015 Create `tests/e2e/fixtures/index.ts`, which exports `test`, `expect` and `ScreenName`:
  - `installation` (worker-scoped; picked from the project name: `seeded`, `fresh`, `subpath` or `upgrade`);
  - `page`, using that installation's `storageState` from `test-results/.auth/<kind>.json`, with none for `fresh` and `upgrade`;
  - `seed`;
  - `scan`;
  - `visitor(site)`: a new context with no storage state, opening `site.visitorUrl`.

  Depends on T012 and T013.
- [X] T016 Create `tests/e2e/playwright.config.ts`:
  - `testDir: 'specs'`, `fullyParallel: true`, `workers: process.env.CI ? 2 : undefined`;
  - `retries: process.env.CI ? 1 : 0`, `forbidOnly: !!process.env.CI`;
  - per kind, three projects (E2E-03):
    - `<kind>-setup`, with `testDir: 'setup'`, `testMatch: '<kind>.setup.ts'` and `teardown: '<kind>-teardown'`;
    - `<kind>-teardown`, with `testMatch: 'installation.teardown.ts'`;
    - `<kind>`, the browser project, with `dependencies: ['<kind>-setup']`. It selects tests with `grep: /@fresh/`, `/@subpath/` or `/@upgrade/`, and `seeded` uses `grepInvert: /@fresh|@subpath|@upgrade/`;
  - the `upgrade` trio is added only when `QB_E2E_PREVIOUS_IMAGE` is set;
  - every browser project uses Chromium with `launchOptions.args: ['--host-resolver-rules=MAP *.e2e.test 127.0.0.1']`;
  - `use: { trace: 'retain-on-failure', video: 'retain-on-failure', screenshot: 'only-on-failure' }`;
  - `globalSetup` and `globalTeardown`;
  - reporters `list` and `html` (`open: 'never'`), plus `./reporters/summary.ts` in CI. The summary reporter ignores setup and teardown projects.

  This covers E2E-05, E2E-11 and E2E-12. Depends on T009, T010, T011 and T017.
- [X] T017 [P] Create `tests/e2e/reporters/summary.ts`, a Playwright `Reporter`. On `onEnd` it counts passed, failed, flaky and skipped tests (a test with `outcome() === 'flaky'` counts as flaky) and appends a Markdown table, followed by the names of flaky tests, to `process.env.GITHUB_STEP_SUMMARY` when that is set (FR-019, E2E-11).
- [X] T018 [P] Create `tests/e2e/a11y-exceptions.json` as `{ "exceptions": [] }`. It is filled in during T040.
- [X] T019 [P] Create `tests/e2e/pages/sign-in.ts` (screen `sign-in`) and `tests/e2e/pages/sites.ts` (screen `sites`: open, create a site through the UI, open a site). Each has `waitLoaded()`, which waits for the `h1` and then calls `scan`; `open()`, which navigates and then calls `waitLoaded()`; and role-based locators (contracts/fixtures.md).

**Checkpoint**: `npm run check` passes, and a placeholder spec that opens `sites` against the
seeded installation passes.

---

## Phase 3: User Story 1 — The core member journey (Priority: P1) 🎯 MVP

**Goal**: a fresh installation is claimed through the wizard. In the seeded installation, a
page is created, blocks are dragged onto it and edited, and the draft is saved, published,
viewed, edited and republished, all in Chromium against the image.

**Independent test**: `npm test -- specs/setup.spec.ts specs/journey.spec.ts` passes.
Breaking the editor's save or the publish action fails it, naming the step (quickstart §3).

- [X] T020 [P] [US1] Read `src/admin/src/editor/palette/BlockPalette.vue`, `src/admin/src/editor/canvas/PageCanvas.vue`, `src/admin/src/editor/canvas/CanvasNode.vue`, `src/admin/src/editor/canvas/DropIndicator.vue` and `src/admin/src/editor/layers/LayerView.vue`. List every element a drag needs (a palette item, the empty canvas drop zone, a block's child drop zone) that has no accessible role and name. Add `data-testid` only to those: `canvas-root-dropzone` and `block-dropzone-<blockId>`, or whatever names fit. Update `src/admin/tests/editor/` snapshots if any change (E2E-08).
  **Outcome**: none was needed. The drop targets show text ("This page is empty.", "Drop blocks here"), the palette is a listbox of named options, and the layers panel is a tree of named items (research E2E-08).
- [X] T021 [P] [US1] Create `tests/e2e/pages/page-tree.ts` (screen `page-tree`). It provides `open(site)`, `createPage(title, slug)` through the UI, `openInEditor(title)`, `deletePage(title)` and `expectPage(title, {parent?})`. `openInEditor` ends with `EditorPage.waitLoaded()` (from T022), so the editor is scanned when it is reached by clicking (E2E-09).
- [X] T022 [US1] Create `tests/e2e/pages/editor.ts` (screen `editor`). It provides:
  - `open(site, page)`;
  - `paletteItem(type)`;
  - `canvasRoot()`;
  - `dropZoneOf(blockLabel)`;
  - `addBlock(type, target)` using `dragBlock`;
  - `select(blockLabel)`;
  - `property(label)`;
  - `expectSaved()`, which waits for the editor's saved status text;
  - `publish()`, which confirms the dialog and waits for the published status;
  - `expectLayer(label)`.

  Depends on T020.
- [X] T023 [P] [US1] Create `tests/e2e/pages/setup.ts` (screen `setup`), with `open()`, `complete({setupCode, displayName, email, password})` and `expectSignedIn(displayName)`.
- [X] T024 [US1] Create `tests/e2e/specs/setup.spec.ts`, tagged `@fresh`. Read the code with `setupCode('fresh')`, complete the wizard in the browser, and expect to be signed in on the sites screen (FR-006, US1 scenario 1). Depends on T023.
- [X] T025 [US1] Create `tests/e2e/specs/journey.spec.ts` as one `test.describe.serial` block, with one `test.step` per step so that failures name the step. The steps are:
  1. "sign out and sign in again" (FR-007);
  2. "create a page" in a seeded site through the page tree (FR-008);
  3. "drag a Section and a Heading", which drags a Section onto the canvas and a Heading into it, then expects both on the canvas and in the layers (FR-009, scenario 2);
  4. "edit properties and the draft saves", which changes the Heading's text and level, waits for saved, reloads, and expects both (FR-010, scenario 3);
  5. "the draft is not public": the visitor gets the site's not-found page for the new page's path (Principle VII);
  6. "publish and view": the visitor sees `<h1>` or `<hN>` with the text (FR-011, scenario 4);
  7. "edit and republish": the visitor's next load shows the new text and not the old (scenario 5).

  Depends on T021 and T022.
- [X] T026 [US1] Run `npm test -- specs/setup.spec.ts specs/journey.spec.ts` against a locally built image until it passes. Fix only test code, or the admin's markup where T020 applies.

**Checkpoint**: MVP. The core journey runs in a browser against the image.

---

## Phase 4: User Story 2 — Media and trash (Priority: P2)

**Goal**: upload, use and view an image; delete a page and restore it from the trash with its
draft.

**Independent test**: `npm test -- specs/media.spec.ts specs/trash.spec.ts` passes on its own
(it needs no setup-wizard test).

- [X] T027 [P] [US2] Create `tests/e2e/pages/media.ts` (screen `media`), with `open(site)`, `upload(filePath)` through the file input, and `expectFile(name)`. Create `tests/e2e/fixtures/files/pixel.png`, a small valid PNG (the same bytes as `OperatorApi.Png`).
- [X] T028 [P] [US2] Create `tests/e2e/pages/trash.ts` (screen `trash`), with `open(site)`, `restore(title)` (confirming the restore dialog) and `expectEmpty()`.
- [X] T029 [US2] Create `tests/e2e/specs/media.spec.ts`:
  - seed a site with a home page;
  - upload `pixel.png` in the media library;
  - open the home page in the editor and drag an Image block;
  - pick the uploaded file in the Image block's media picker;
  - fill in the alt text, then publish;
  - as a visitor, expect an `img` with that alt text whose `src` loads with status 200 from the site's host name (FR-012, US2 scenario 1).

  Depends on T022 and T027.
- [X] T030 [US2] Create `tests/e2e/specs/trash.spec.ts`:
  - seed a site, a parent page, and a child page with a draft holding a known heading;
  - delete the child in the page tree;
  - restore it from the trash;
  - expect it under the same parent in the tree;
  - open it in the editor and expect the heading (FR-013, US2 scenario 2).

  Depends on T021 and T028.

**Checkpoint**: US1 and US2 pass together and separately.

---

## Phase 5: User Story 3 — The admin under a base path (Priority: P2)

**Goal**: under `/cms` behind Nginx, the admin signs in, deep-links and saves a draft, and no
request leaves `/cms`.

**Independent test**: `npm test -- --grep @subpath` starts only the sub-path installation and
passes. Setting `ADMIN_BASE` to `'/admin/'` in `src/admin/src/app/base.ts` makes it fail
(quickstart §3).

- [X] T031 [US3] Create `tests/e2e/specs/subpath.spec.ts`, tagged `@subpath`:
  - record every request through `page.on('request')` and every response through `page.on('response')`;
  - open `http://127.0.0.1:18183/cms/admin/` and expect the sites screen (signed in through `storageState`);
  - go to `/cms/admin/sites` directly and reload, then expect the same screen (scenario 2);
  - seed a site and page through the API under `/cms`, open it in the editor, change a Heading and expect saved (scenario 3);
  - finally, assert that every same-origin request path starts with `/cms/` and that no same-origin response had status 404 (FR-014, E2E-07).

  Also sign out and sign in through the form once (scenario 1). Depends on T015 and T022.

**Checkpoint**: #51 is proven in a real browser.

---

## Phase 6: User Story 4 — Adding a test is easy (Priority: P3)

**Goal**: a guide and helpers good enough that a newcomer adds a passing test within an hour.

**Independent test**: quickstart §8.

- [X] T032 [P] [US4] Write `tests/e2e/README.md` (FR-021). It covers:
  - prerequisites;
  - one-command run (`npm ci && npx playwright install chromium && npm test`);
  - tags and installations;
  - environment variables (link to `specs/025-e2e-harness/contracts/runner.md`);
  - fixtures and page objects (link to `contracts/fixtures.md`);
  - choosing elements (roles first; the `data-testid` list from T020; what the lint rule forbids);
  - accessibility scans and how to add an exception (link to `contracts/a11y-exceptions.md`);
  - reading a failure's trace (`npx playwright show-trace`);
  - a worked example that adds a test for an existing screen.
- [X] T033 [P] [US4] Update `docs/contributing.md`: in **Tests**, add a paragraph on the browser suite linking `tests/e2e/README.md`; in **Admin app**, a line saying that new screens get a page object and a browser test.

**Checkpoint**: the guide is complete. It is validated in quickstart §8 after merge, by someone new to the suite.

---

## Phase 7: User Story 5 — Release candidates and upgrade (Priority: P3)

**Goal**: the suite runs against the candidate digest at release, the upgrade journey runs in
the browser, and failures block promotion. E-5 is closed.

**Independent test**: `QB_E2E_PREVIOUS_IMAGE=<latest release> npm test -- --grep @upgrade`
passes locally (quickstart §7).

- [X] T034 [US5] Create `tests/e2e/setup/upgrade.setup.ts` for the `upgrade` kind, with the same error handling as the other setup files (T010):
  - start with `QB_E2E_PREVIOUS_IMAGE`, wait, claim and seed through `Seed`: a site, a published page "Before upgrade", and a draft of a second page with heading "Draft before upgrade";
  - write the seeded ids to `test-results/.upgrade.json`;
  - run `up -d` again with `QUICKBUILDER_IMAGE` set to the image under test, keeping the volumes;
  - wait for readiness again. Save no session: the test signs in through the form, which also proves sign-in survives the upgrade.

  This covers E2E-14 and FR-016a.
- [X] T035 [US5] Create `tests/e2e/specs/upgrade.spec.ts`, tagged `@upgrade`. It skips with a reason when `QB_E2E_PREVIOUS_IMAGE` is unset, then:
  - signs in through the form as the seeded administrator;
  - opens the drafted page in the editor and expects "Draft before upgrade";
  - as a visitor, expects "Before upgrade" on the site (US5 scenario 3).

  Depends on T034.
- [X] T036 [US5] In `.github/workflows/release.yml`, add a job `e2e` (name "Browser suite (candidate)", `needs: [prepare, manifest]`, `ubuntu-latest`). Its steps:
  - checkout;
  - setup-node 24;
  - log in to ghcr.io as the other release jobs do;
  - `npm ci` and `npx playwright install --with-deps chromium` in `tests/e2e`;
  - `npm test` with `QB_E2E_IMAGE` set to the candidate digest (the same `format(...)` expression `procedures` uses), `QB_E2E_PREVIOUS_IMAGE: ghcr.io/trungnd1985/quickbuilder:${{ needs.prepare.outputs.previous }}` only when `previous` is non-empty, `QB_E2E_REQUIRE_DOCKER: '1'`, `QB_E2E_CHECK_EXCEPTIONS: '1'`, and `CI: true`;
  - upload `tests/e2e/playwright-report` and `tests/e2e/test-results` on failure, with `retention-days: 7`.

  Add `e2e` to `candidate.needs` (FR-016, E2E-14).
- [X] T037 [US5] Add a dated note to `specs/011-editor-core/plan.md` after the Complexity Tracking table, in the style of `specs/001-foundation-skeleton/plan.md`'s "2026-09-30 — E-3 and E-4 closed" note. It records that E-5 is closed by #25: `tests/e2e/specs/journey.spec.ts` drags a Section and a Heading, saves and confirms the draft, on every PR (`ci.yml` `e2e`) and every release candidate (`release.yml` `e2e`). Update the E-5 mentions in `tests/QuickBuilder.SmokeTests/ReferenceDeploymentSmokeTests.cs` (class remarks and step 9d) to point at the suite (FR-024, E2E-15).

**Checkpoint**: all stories are done.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T038 In `.github/workflows/ci.yml`, add a job `e2e` (name "Browser suite (e2e)", `ubuntu-latest`, **no `needs`**, no path filter, no secrets). Its steps:
  - checkout;
  - setup-node 24 with npm cache on `tests/e2e/package-lock.json`;
  - `npm ci`;
  - `npm run check`;
  - `npx playwright install --with-deps chromium`;
  - `docker build -t quickbuilder:e2e .`;
  - `npm test` with `QB_E2E_IMAGE: quickbuilder:e2e`, `QB_E2E_REQUIRE_DOCKER: '1'` and `QB_E2E_CHECK_EXCEPTIONS: '1'`;
  - on failure, upload an artifact named `e2e-report` holding `tests/e2e/playwright-report` and `tests/e2e/test-results`, with `retention-days: 7`.

  Set `timeout-minutes: 25` (FR-015, FR-017, FR-018, E2E-13).
- [X] T039 Run the full suite locally with `npm test` (quickstart §1) and the parallel run (§5), and fix any flakiness at its cause (wait on what the member sees, never on timeouts).
- [X] T040 Fill `tests/e2e/a11y-exceptions.json` from the first full run. For each critical or serious violation, either:
  - fix it in `src/admin`, if the fix is small and local (a missing label, a missing name on an icon button), and update the matching unit test's axe assertion; or
  - add an entry with a reason (and an `issue` if one is filed).

  Then run quickstart §4 (FR-023a, FR-023b).
- [X] T041 Run quickstart §3 (the three deliberate breaks) and §7 (upgrade from the latest release). Record the results, timings and anything surprising in `specs/025-e2e-harness/quickstart-results.md`.
- [X] T042 Run the repository's checks:
  - `dotnet build -warnaserror` (for the smoke test comment edits);
  - `npm run lint && npm test` in `src/admin` (for the `data-testid` and accessibility fixes);
  - `npm run check` in `tests/e2e`;
  - `dotnet run --project tools/QuickBuilder.Docs -- lint`;
  - `dotnet run --project tools/QuickBuilder.Release -- fragment check --base origin/main`, after `git add -N`.

  Add a release-note fragment only if the accessibility fixes change what members see. If they do, the fragment is `docs/releases/unreleased/25-e2e-harness.md`, `kind: fixed`.
- [ ] T043 After the PR's CI run, record in `quickstart-results.md` the `e2e` job's duration and start time compared with `Build and test (.NET)` (SC-002, quickstart §6).

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 (Setup) has no dependencies. T007 waits on T006.
- Phase 2 (Foundational) waits on Phase 1 and blocks every story. Within it:
  - T009, T010 and T011 wait on T008;
  - T015 waits on T012 and T013;
  - T016 waits on T009, T010, T011 and T017.
- Phases 3 to 7 wait on Phase 2:
  - US1 has no story dependencies;
  - US2 reuses `editor.ts` (T022) and `page-tree.ts` (T021) from US1;
  - US3 reuses `editor.ts` (T022);
  - US4 can start once Phase 2 ends, and its `data-testid` list waits on T020;
  - US5 waits on US1, whose journey is what closes E-5.
- Phase 8 waits on the stories it checks. T038 (the CI job) can be written at any time after T016.

### User story dependencies

```text
Phase 2 ─▶ US1 (P1) ─┬─▶ US2 (P2)
                     ├─▶ US3 (P2)
                     └─▶ US5 (P3)
Phase 2 ─▶ US4 (P3)   (finishes after T020)
```

### Parallel opportunities

- Phase 1: T002 to T006 together.
- Phase 2: T012, T013, T014, T017, T018 and T019 together, after T008.
- US1: T020, T021 and T023 together.
- US2: T027 and T028 together.
- US4: T032 and T033 together, and alongside US2 and US3.

## Parallel example: User Story 1

```text
# After Phase 2:
T020  data-testid audit on the editor canvas (src/admin/src/editor/…)
T021  pages/page-tree.ts
T023  pages/setup.ts
# Then:
T022  pages/editor.ts  (needs T020)
T024  specs/setup.spec.ts  (needs T023)
T025  specs/journey.spec.ts  (needs T021, T022)
```

## Implementation strategy

1. **MVP**: Phases 1 and 2, then US1 (T001 to T026). This already gives a browser check of the
   core journey against the image.
2. **Then**: US2 and US3 (P2), then US4 and US5 (P3), then Phase 8.
3. **Delivery**: everything goes in **one pull request** that closes #25. That follows the
   constitution's "one feature = one PR" and the maintainer's wish to avoid repeated CI waits.
   The CI job (T038) lands in the same PR, so its first run is that PR's own check.
