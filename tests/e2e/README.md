# Browser end-to-end suite

These tests drive the admin and the public site in Chromium against the product's **Docker
image**, started from the reference `docker-compose.yml`. They catch what unit and integration
tests cannot see: the built admin loading and finding its API, the editor's drag and drop and
autosave, a published page as a visitor gets it, and the admin under a base path.

Design and decisions: [specs/025-e2e-harness](../../specs/025-e2e-harness/plan.md).

## Run it

You need Docker (running), bash, Node 24, and about 6 GB of free disk.

```bash
cd tests/e2e
npm ci
npx playwright install chromium
npm test
```

`npm test` builds `quickbuilder:e2e` from your checkout. To test an existing image instead, set
`QB_E2E_IMAGE`:

```bash
QB_E2E_IMAGE=quickbuilder:e2e npm test          # reuse the image from the last run
npm test -- specs/journey.spec.ts               # one file
npm test -- --grep @subpath                     # one tag
npm run report                                  # open the last HTML report
npm run check                                   # type-check and lint
```

All the environment variables are listed in
[contracts/runner.md](../../specs/025-e2e-harness/contracts/runner.md).

## How a run is put together

Each test runs against an **installation**: a throwaway deployment of the image, in its own
compose project, on its own loopback port. Its tag decides which one:

| Tag | Installation | Port | What it is |
|---|---|---|---|
| none | `seeded` | 18181 | Claimed through the API; you are signed in as the seeded administrator. |
| `@fresh` | `fresh` | 18182 | Never claimed, for the setup wizard. |
| `@subpath` | `subpath` | 18183 | Under `/cms` behind Nginx, as the reverse proxy docs describe. |
| `@upgrade` | `upgrade` | 18184 | The previous release, upgraded to the image under test. Release only. |

Each installation is three Playwright projects: `<kind>-setup` starts and claims it,
`<kind>` holds its tests, and `<kind>-teardown` saves its logs to
`test-results/compose-qb-e2e-<kind>.log` and removes it with its volumes. Playwright starts an
installation only when a selected test needs it, so `--grep @subpath` starts only the sub-path
installation.

Tests share an installation but never its data. Each test **creates its own site** through the
API, with a host name like `s-3f9a1c.e2e.test`. Chromium resolves `*.e2e.test` to loopback, so
a visitor reaches the site by its own host name. Tests can therefore run in parallel and in any
order.

## Write a test

Import `test` and `expect` from `fixtures/index.ts`, never from `@playwright/test`:

```ts
import { expect, test } from '../fixtures/index.js'
import { PageTreeScreen } from '../pages/page-tree.js'

test('a page can be renamed', async ({ page, kind, scan, seed }) => {
  const site = await seed.site('Rename')                        // your own site, via the API
  await seed.page(site, { title: 'Old name', slug: 'old' })

  const tree = new PageTreeScreen(page, kind, scan)
  await tree.open(site)                                         // navigates, waits, scans
  await tree.select('Old name')
  await page.getByRole('button', { name: 'Rename' }).click()
  await page.getByRole('dialog').getByLabel('Title').fill('New name')
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('button', { name: 'Select New name', exact: true })).toBeVisible()
})
```

**Fixtures** ([contracts/fixtures.md](../../specs/025-e2e-harness/contracts/fixtures.md)):

| Fixture | Gives |
|---|---|
| `page` | A browser page, signed in (except in `@fresh` and `@upgrade` tests). |
| `kind` | The installation's name, which page objects need to build addresses. |
| `seed` | `site()`, `page()`, `draft()`, `publish()`, `publishedPage()`, all through the admin's API. |
| `scan` | The accessibility scan. Page objects call it, so tests rarely do. |
| `visitor(site, path?)` | A page in a context with no session, opened on the site's host name. |

**Page objects** live in `pages/`. Each has `waitLoaded()`, which waits for the screen and then
scans it, and `open()`, which navigates and calls `waitLoaded()`. A helper that lands on
another screen by clicking ends with that screen's `waitLoaded()`; for example, `openInEditor`
ends with the editor's. Add a page object when you add a screen, and give the screen a name in
`SCREENS` in `fixtures/exceptions.ts`.

**Steps**: wrap each stage of a longer test in `test.step('…')`. A failure then names the
step, as `journey.spec.ts` does.

## Choosing elements

Find elements the way a member using a screen reader would:

1. `getByRole(role, { name })` for buttons, links, headings, dialogs, listboxes, trees and so on;
2. `getByLabel(label)` for form fields;
3. `getByText(text)` for messages and visible text, such as the canvas's "Drop blocks here";
4. `getByTestId(id)` only for elements that have no accessible name. **There are none today.**
   The editor's drop targets are reached by their visible text. If you add one, list it here.

`locator('…')` with a CSS or XPath string fails lint, because such selectors break when the
markup or styling changes and check nothing about accessibility. If an element cannot be
reached by role, label or text, it probably needs a label in the admin. Fix that instead.

Wait on what the member sees (`await expect(locator).toBeVisible()`), never on time
(`waitForTimeout`). The strings are the admin's English ones in `src/admin/src/i18n/en.json`;
when a label changes, the test that relies on it fails, which is intended.

## Accessibility scans

Every admin screen a test reaches is scanned with axe against WCAG 2.1 A and AA
(the admin's floor is [docs/admin-accessibility-baseline.md](../../docs/admin-accessibility-baseline.md)):

- a **critical** or **serious** violation fails the test, unless it is on the exceptions list;
- lesser violations are listed in the report (the `a11y-<screen>.json` attachment) and do not
  fail anything.

The exceptions list is [`a11y-exceptions.json`](a11y-exceptions.json). It is empty, and should
stay as short as possible: fix the screen when you can. An entry needs the axe rule, the screen
and a reason ([contracts/a11y-exceptions.md](../../specs/025-e2e-harness/contracts/a11y-exceptions.md)):

```json
{ "rule": "color-contrast", "screen": "editor", "reason": "Why it is accepted for now", "issue": 123 }
```

In CI (`QB_E2E_CHECK_EXCEPTIONS=1`), an entry that no longer occurs fails the run, so the list
only shrinks. Set the variable locally only when you run the whole suite.

## When a test fails

The report keeps, for each failed test, a **trace**, a **video** and a **screenshot**, and each
installation's logs are in `test-results/compose-qb-e2e-<kind>.log`. In CI, download the
`e2e-report` artifact from the run.

```bash
npm run report                                         # the HTML report, with traces linked
npx playwright show-trace test-results/output/<test>/trace.zip
```

The trace shows every action, the page at each step, the network and the console. In CI a
failed test is retried once; one that passes only on its retry is listed as **flaky** in the
job summary. Fix the cause: it is almost always a wait on time or on the wrong thing.

If Docker is not running, the run stops before any test with "Docker is not available".
