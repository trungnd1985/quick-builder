# Feature Specification: End-to-End Test Harness

**Feature Branch**: `025-e2e-harness`

**Created**: 2026-11-26

**Status**: Draft

**Issue**: #25

**Input**: User description: "End-to-end test harness (issue #25, short name e2e-harness). Problem: the editor and the publish pipeline are hard to cover with unit and integration tests, and a self-hosted product breaking on upgrade damages trust directly. The repository needs an end-to-end suite that exercises the real browser flows against the real Docker image, running in CI on every pull request. In scope: Playwright project and fixtures, a seeded test installation, coverage of setup wizard, sign-in, create page, drag a block, edit properties, save, publish, view the published page, edit and republish, upload media, restore from trash; running against the built image in CI; artifacts (trace, video, screenshots) on failure; guidance and helpers for adding new specs. Also cover the admin served under a base path (Hosting:BasePath=/cms) in a real browser, which in-process tests cannot prove (#51). Out of scope: cross-browser matrix beyond Chromium, visual regression testing, load testing. Depends on #1 #10 #11 (all merged)."

## Context

Today the product is tested in three layers:

- **unit tests** for domain logic and for the admin's components, the latter against a fake
  API;
- **integration tests** for every endpoint, run in-process against a real database;
- **the release smoke test and the documented procedures**, which boot the real image and
  drive it over HTTP, without a browser.

No test opens the admin in a real browser against the real image. So nothing automated proves
that the pieces a member actually touches fit together:

- the built admin application loads, finds its API and signs in;
- the editor drags a block onto a page and saves it;
- a published page is what a visitor sees.

Two recorded gaps wait for this feature:

- **Exception E-5** (011 plan): the release smoke test was to open the editor, add a Section
  with a Heading and wait for autosave (011 FR-044). That was deferred to this harness and
  checked by hand meanwhile.
- **#51**: the admin under a base path (`Hosting:BasePath=/cms`) works out its API address
  from its own script's address. Only a browser runs that code, so the fix is proven up to the
  point where the browser takes over and no further.

This feature adds a browser suite that runs those journeys against the image built from the
change under review, on every pull request and on every release candidate.

## Clarifications

### Session 2026-11-26

- Q: Which pull requests should the browser suite run on? → A: Every pull request, with no
  path filter, in parallel with the other CI jobs, so that it can be made a required check.
- Q: Should the suite check in the browser that the admin and published pages still work after
  upgrading from the previous release? → A: Yes, at release only. A browser test starts the
  previous release, creates content, upgrades to the candidate, then opens the admin and the
  published page. Pull requests do not run it.
- Q: Should the suite check the admin screens it visits for accessibility problems? → A: Yes.
  Every screen visited is scanned. Critical and serious problems fail the check. Problems that
  already exist when the suite is added go on an exceptions list, each with a reason, and are
  fixed over time. Lesser problems are only reported.

## User Scenarios & Testing *(mandatory)*

The users of this feature are the people who change QuickBuilder: maintainers and
contributors. Their "journey" is opening a pull request and learning, before merging, whether
the product still works for a member in a browser.

### User Story 1 - A pull request proves the core member journey still works (Priority: P1)

A contributor opens a pull request. Alongside the existing checks, a browser suite boots the
image built from that pull request and walks the journey a new installation's first member
takes:

1. claim the installation with the setup wizard;
2. sign in;
3. create a site and a page;
4. open the page in the editor and drag a block onto it;
5. edit the block's properties, and the draft saves;
6. publish, and view the published page as a visitor on the site's host name;
7. edit and publish again, and the visitor sees the change.

If any step breaks, the pull request shows a failed check that names the step.

**Why this priority**: this journey is the product. A pull request that breaks it while every
unit and integration test passes is exactly the failure the existing layers cannot see.

**Independent Test**: open a pull request that changes nothing user-visible. The browser
check runs and passes. Then break one step on purpose, for example by renaming the editor's
save request. The check fails and names that step.

**Acceptance Scenarios**:

1. **Given** a fresh installation from the image under test, **When** the suite enters the
   setup code from the application's logs and completes the wizard, **Then** it is signed in
   as the administrator it created.
2. **Given** a signed-in administrator with a site and a page, **When** the suite drags a
   Section from the block palette onto the canvas and a Heading into it, **Then** both appear
   on the canvas and in the layer view.
3. **Given** a Heading on the canvas, **When** the suite changes its text and level in the
   property panel, **Then** the editor shows the draft as saved, and reloading the editor
   shows the change.
4. **Given** a saved draft, **When** the suite publishes the page and opens the site's host
   name as an anonymous visitor, **Then** the page shows the heading with its new text.
5. **Given** a published page, **When** the suite edits the heading and publishes again,
   **Then** the visitor's next request shows the new text and not the old.
6. **Given** any step fails, **When** the check finishes, **Then** its report names the
   failed step and links the trace, video and screenshot of that test.

---

### User Story 2 - Media and trash journeys are covered (Priority: P2)

Two everyday flows that cross several modules are covered in the browser:

- **Upload media**: upload an image in the media library, add it to an Image block on a page,
  publish, and the visitor's page shows the image from the site's own address.
- **Restore from trash**: delete a page, find it in the trash, restore it, and it is back in
  the page tree with its draft intact.

**Why this priority**: both cross module boundaries (media and pages, pages and trash) through
the admin's own screens, where a broken screen hides behind passing endpoint tests. They are
second only because the core journey must exist first.

**Independent Test**: run only the media and trash tests against a seeded installation. They
pass without the setup-wizard test having run.

**Acceptance Scenarios**:

1. **Given** a seeded installation, **When** the suite uploads a PNG in the media library and
   picks it for an Image block, then publishes, **Then** the visitor's page shows an image
   whose file loads from the site's host name.
2. **Given** a page with a saved draft, **When** the suite deletes it and restores it from the
   trash, **Then** the page is back in the tree under its old parent, and its draft holds the
   content it had before deletion.

---

### User Story 3 - The admin works under a base path in a real browser (Priority: P2)

An installation served under `/cms` behind a reverse proxy, as the operator documentation
describes, is opened in the browser at `/cms/admin/`. The admin loads, signs in, lists sites
and saves a draft in the editor. Every request it makes stays under `/cms`.

**Why this priority**: #51 was fixed and proven up to the browser. This closes the last part,
which only a browser can prove. It is P2 because it affects only sub-path installations.

**Independent Test**: run only the base-path test. It starts its own installation behind a
proxy under `/cms`, independent of the other tests.

**Acceptance Scenarios**:

1. **Given** an installation under `/cms`, **When** the suite opens `/cms/admin/` and signs
   in, **Then** the sites list loads, and no request the page makes falls outside `/cms`.
2. **Given** the same installation, **When** the suite opens a deep link such as
   `/cms/admin/sites` directly and reloads it, **Then** the same screen loads.
3. **Given** the same installation, **When** the suite saves a draft in the editor, **Then**
   the save succeeds.

---

### User Story 4 - Adding a new browser test is easy and consistent (Priority: P3)

A contributor building a new feature adds a browser test for it. A short guide and shared
helpers give them:

- a signed-in administrator;
- a site with a published page, created through the API rather than the browser, so each
  test is quick and stands alone;
- ways to reach a screen and a block without depending on layout details.

**Why this priority**: the suite is only worth having if it grows with the product. It is P3
because the suite must exist before it can grow.

**Independent Test**: following only the guide, a contributor who has not seen the suite adds
a test for an existing screen. It runs locally with one command and passes in CI.

**Acceptance Scenarios**:

1. **Given** the guide, **When** a contributor runs the documented command on a machine with
   Docker, **Then** the suite builds or uses the image, runs, and prints where the report is.
2. **Given** the helpers, **When** a new test asks for a seeded installation, **Then** it gets
   one without repeating the setup wizard, and two tests do not see each other's sites.
3. **Given** the guide, **When** a contributor reads how to choose an element, **Then** it
   says to reach elements by their accessible role and name, as a member using a screen
   reader would.

---

### User Story 5 - Release candidates are checked in the browser too (Priority: P3)

The release workflow runs the same suite against the release candidate's exact image, as it
already does for the smoke test and the documented procedures. A failing suite blocks
promotion. The browser half of the release smoke test (011 FR-044) is then met, and exception
E-5 is removed.

**Why this priority**: releases already pass through the pull request check, so this mostly
guards against differences between the branch build and the published image. It closes a
recorded exception, though.

**Independent Test**: run the release workflow's browser step with a candidate image. It
reports which image it ran, and a failure stops promotion.

**Acceptance Scenarios**:

1. **Given** a release candidate, **When** the release workflow runs, **Then** the browser
   suite runs against the candidate by its digest, and the run records that digest.
2. **Given** a failing browser suite on a candidate, **When** promotion is attempted, **Then**
   it is refused, as for any other failed release gate.
3. **Given** an installation of the previous release with a published page and a saved
   draft, **When** it is upgraded to the candidate with its data kept, **Then** the
   administrator signs in, the editor opens the draft with its content, and a visitor sees
   the published page.

---

### Edge Cases

- **Docker is not available** on a contributor's machine: the suite says so and stops,
  instead of reporting test failures. In CI, a missing Docker is a failure, never a skip.
- **The installation is slow to start**: the suite waits for the readiness probe, up to a
  limit. If it never becomes ready, the run fails with the application's recent logs.
- **A test fails intermittently**: a test that passes only on retry is reported as flaky in
  the run summary, so it is visible and is not quietly ignored.
- **Two tests run at once against one installation**: each test works in its own site, so
  neither sees the other's pages, and parallel runs stay independent.
- **Drag and drop is not available** to a member who uses only the keyboard: the editor
  already supports keyboard insertion (011 SC-006). The suite tests dragging, since that is
  what this feature adds, and leaves keyboard insertion to the editor's unit tests.
- **Secrets in failure artifacts**: traces and videos record what the browser saw, including
  the setup code and test passwords. Those belong to a throwaway installation and are never
  real credentials.
- **A pull request from a fork**: the suite needs no repository secrets, so it runs the same
  way for pull requests from forks.

## Requirements *(mandatory)*

### Functional Requirements

**The suite and its installation**

- **FR-001**: The repository MUST contain a browser end-to-end suite that drives the admin and
  the public site in Chromium against the product's Docker image, not against an in-process
  server or a development build.
- **FR-002**: The suite MUST run against an image named by the caller, or, when none is
  named, an image built from the working copy.
- **FR-003**: The suite MUST start each installation from the reference compose file, with
  only the changes the test needs, such as its port, and MUST remove the installation and its
  data when the run ends.
- **FR-004**: The suite MUST provide a seeded installation that is already claimed and has a
  signed-in administrator, for every test except the one that tests the setup wizard itself.
- **FR-005**: Each test MUST create its own site (and its own pages and files) through the
  API, so that tests are independent of each other and of their order.

**Journeys covered**

- **FR-006**: The suite MUST cover the setup wizard: entering the code read from the
  application's logs, creating the administrator, and arriving signed in.
- **FR-007**: The suite MUST cover signing out and signing in again with the administrator's
  credentials.
- **FR-008**: The suite MUST cover creating a page in the page tree and opening it in the
  editor.
- **FR-009**: The suite MUST cover dragging a block from the palette onto the canvas and into
  another block, with the pointer.
- **FR-010**: The suite MUST cover editing a block's properties in the property panel, and
  confirm that the draft is saved by reloading the editor.
- **FR-011**: The suite MUST cover publishing a page and viewing it as an anonymous visitor on
  the site's host name, then editing, publishing again and seeing the change.
- **FR-012**: The suite MUST cover uploading an image in the media library, using it in an
  Image block and seeing it on the published page.
- **FR-013**: The suite MUST cover deleting a page, restoring it from the trash and finding
  it in the tree with its draft intact.
- **FR-014**: The suite MUST cover the admin served under a base path behind the documented
  reverse proxy: signing in, a deep link, and saving a draft. It MUST fail if any request the
  admin makes falls outside the base path.

**Running in CI and at release**

- **FR-015**: The suite MUST run on every pull request against the image built from that pull
  request, as its own check, with no path filter, so that it can be made a required check. It
  MUST NOT depend on repository secrets.
- **FR-016**: The suite MUST run in the release workflow against the candidate image by
  digest, and a failure MUST block promotion as the other release gates do.
- **FR-016a**: At release, the suite MUST also run an upgrade journey in the browser: start
  the previous release, create a site with a published page and a saved draft, upgrade to the
  candidate keeping the data, then sign in, open the draft in the editor, and view the
  published page as a visitor. A failure MUST block promotion. Pull requests MUST NOT run it.
- **FR-017**: The suite MUST run in parallel with the existing CI checks, not after them.
- **FR-018**: When a test fails, the run MUST keep its trace, video and screenshot, and the
  application's logs, as downloadable artifacts for at least 7 days.
- **FR-019**: A test that passes only after a retry MUST be reported as flaky in the run
  summary. Retries MUST be limited to one per test, and only in CI.
- **FR-020**: When Docker is not available, a local run MUST stop with a message saying so.
  In CI, it MUST fail.

**Writing new tests**

- **FR-021**: The repository MUST include a guide to running the suite locally with one
  command and to adding a test, covering the shared helpers and how to choose elements.
- **FR-022**: Shared helpers MUST provide at least: a seeded, signed-in administrator; a
  site with a published page; and a way to open a page in the editor.
- **FR-023**: Tests MUST reach elements by accessible role and name, or by a stable test
  identifier where no accessible name exists. They MUST NOT rely on styling classes or layout
  position.

**Accessibility**

- **FR-023a**: Each admin screen a test visits MUST be scanned automatically for
  accessibility problems. A critical or serious problem MUST fail the test, unless it is on
  the suite's exceptions list. Lesser problems MUST be listed in the run's report without
  failing it.
- **FR-023b**: The exceptions list MUST name each problem, the screen it is on and the reason
  it is accepted. A listed exception that no longer occurs MUST fail the run, so the list only
  shrinks.

**Closing recorded exceptions**

- **FR-024**: Exception E-5 (011 plan) MUST be removed, recording that this suite now opens
  the editor, adds a Section with a Heading and confirms the saved draft (011 FR-044), on
  every pull request and every release candidate.

### Key Entities

- **Test installation**: a throwaway QuickBuilder deployment from the image under test,
  either fresh (for the setup wizard) or seeded (claimed, with an administrator). It lives for
  one run.
- **Seeded administrator**: the account the seeded installation was claimed with. Tests sign
  in as it.
- **Test site**: a site with its own host name, created by one test for itself.
- **Failure artifacts**: the trace, video, screenshot and application logs kept for a failed
  test.
- **Accessibility exception**: a known critical or serious accessibility problem on one
  screen, with the reason it is accepted for now.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every journey listed in FR-006 to FR-014 is covered by at least one browser test
  that runs on every pull request.
- **SC-002**: The browser check finishes within **15 minutes** on CI, including starting its
  installations. Because it runs in parallel, it adds no time to a pull request's wait as long
  as the longest existing check takes longer.
- **SC-003**: Over the first 50 pull request runs after merging, at least **98%** of runs
  that fail do so because the change broke something, not because of a flaky test.
- **SC-004**: A deliberately broken step (the editor's save, the publish action, the admin's
  API address under a base path) makes the check fail, and the failure report names the
  step, in each case.
- **SC-005**: A contributor who has not seen the suite can add a passing test for an existing
  screen within **1 hour**, using only the guide.
- **SC-006**: A failed run's artifacts are enough to see which step failed and what the
  browser showed, without re-running the suite locally.

## Assumptions

- **Chromium only.** Other browsers, visual regression and load testing are out of scope, as
  the issue says.
- **The image under test is built once per run** and shared by all the suite's
  installations. Building it is part of the check's time budget (SC-002).
- **One retry in CI** is the industry default for browser suites. Reporting tests that pass
  only on retry as flaky (FR-019) keeps that retry from hiding real problems.
- **The seeded installation is claimed through the API**, not the browser, so only the
  setup-wizard test pays for the wizard. The API is the one the admin itself uses.
- **The base-path installation uses the documented reverse-proxy procedure** (Nginx, under
  `/cms`), so the browser test also checks what the operator documentation tells operators to
  do.
- **Making the check required** in branch protection is the maintainer's choice and outside
  the repository. Since the check runs on every pull request with no path filter, it can be
  made required.
- **Dependencies #1, #10 and #11** (foundation, page tree, editor core) are merged, and so are
  the features this suite drives: publishing (012), media (003) and trash.
- **Throwaway credentials**: the suite's passwords and setup codes belong to installations
  that exist for one run. Showing them in failure artifacts is acceptable.
