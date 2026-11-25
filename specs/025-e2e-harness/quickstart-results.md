# Quickstart Results: End-to-End Test Harness

**Run**: 2026-11-26, on the implementation branch `025-e2e-harness`, on a Windows 11 workstation with
Docker Desktop. Image `quickbuilder:e2e` was built from the branch. Playwright 1.65 and Chromium 159.

## §1 — The full pull-request suite (SC-001)

`QB_E2E_CHECK_EXCEPTIONS=1 npm test` started the seeded, fresh and sub-path installations in
parallel. The run passed **11 of 11** (3 setup, 5 tests, 3 teardown) in **33 s**, or 35 s
wall clock including global setup, with the image already built. Afterwards,
`docker ps -a --filter name=qb-e2e` listed nothing.

Each installation took 11 to 14 s from `compose up` to claimed. `@upgrade` did not run,
because its projects exist only when `QB_E2E_PREVIOUS_IMAGE` is set.

**Found and fixed on the first full run**: the tests shared one session through
`storageState`, so when the journey signed out, the server ended that session for the trash and
media tests running beside it. Each test now signs in for a session of its own (research E2E-04,
"Changed during implementation").

## §2 — Docker missing (FR-020)

With `DOCKER_HOST=tcp://127.0.0.1:1`:

- Locally, the run printed `Docker is not available: start Docker and run again.` and exited 1
  before any test.
- With `QB_E2E_REQUIRE_DOCKER=1`, global setup threw the same message, marked as required, and
  the run exited 1.

## §3 — Deliberate breaks (SC-004, SC-006)

| Break | Failing test › step |
|---|---|
| Draft save URL changed to `/draft-broken` in `src/admin/src/api/pages.ts` | `journey.spec.ts` › **edit properties and the draft saves** (the save status never reads "All changes saved") |
| `askPublish` does nothing in `PublishMenu.vue` | `journey.spec.ts` › **publish and view** (no "Published version N.") |
| `ADMIN_BASE` fixed to `'/admin/'` in `src/admin/src/app/base.ts` | `subpath.spec.ts` › **a deep link loads, and loads again on reload** |

Each failure kept a screenshot, a video, a trace and `error-context.md` (the page's
accessibility snapshot), and the installation's log was in `test-results/compose-qb-e2e-*.log`.

A first attempt at the publish break, `@click="() => {}"`, did not compile: `askPublish`
became unused, and the admin's type check refuses that. The break was moved inside the
handler.

## §4 — Accessibility exceptions (FR-023a, FR-023b)

- **A real finding**: the first run of `trash.spec.ts` failed on
  `label-content-name-mismatch` (serious, WCAG 2.5.3). Each entry's "Delete permanently"
  button was named "Delete <name> permanently". It was fixed in the admin
  (`trash.purge.label` is now "Delete permanently: {label}") rather than listed, so
  `a11y-exceptions.json` starts empty. The admin's jsdom axe tests had not caught it.
- **Unused entry**: with `{"rule":"blink","screen":"sites"}` listed,
  `QB_E2E_CHECK_EXCEPTIONS=1 npm test -- --grep @subpath` failed in global teardown, naming
  "blink on sites", and exited 1. Without the variable, the same run passed.
- **Malformed list**: an entry with an unknown screen and an empty reason stopped the run in
  global setup, naming both problems, before any installation started.
- **Scans are real**: every scan is attached to its test. In the journey, the screens passed
  17 (sign-in), 21 (sites), 25 (page tree) and 27 (editor) axe rules with no violations.

## §5 — Parallel tests stay apart (FR-005)

`npm test -- --workers=4 --repeat-each=3` ran 21 tests. Everything passed except the second
and third repeats of the setup wizard test. A fresh installation can be claimed only once, so
those repeats found it already claimed, as expected. The quickstart command now leaves out
`@fresh`.

## §6 — CI (SC-002)

To be recorded after this PR's CI run (T043).

## §7 — Upgrade journey (FR-016a)

`QB_E2E_PREVIOUS_IMAGE=ghcr.io/trungnd1985/quickbuilder:0.1.0 npm test -- --grep @upgrade`:

- The 0.1.0 release started and was claimed and seeded through the API.
- `compose up -d` with the branch image replaced the app container and kept the volumes. The
  new container logged `DatabaseVersion` 0.1.0, then 0.2.0-dev.
- The test signed in through the form, the draft opened in the editor with "Draft before
  upgrade", and a visitor saw "Before upgrade".

The setup took 17.8 s, the test 2.6 s and teardown 1.4 s, all passed.

## §8 — A new test from the guide alone (SC-005)

To be done after merge by someone who has not seen the suite.
