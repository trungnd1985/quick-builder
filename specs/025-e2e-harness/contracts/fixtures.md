# Contract: Fixtures and Helpers for Test Authors

Tests import `test` and `expect` from `fixtures/index.ts`, never from `@playwright/test`
directly. That is what gives every test the seeded installation and the accessibility scans.

## Fixtures (FR-022)

| Fixture | Type | Gives |
|---|---|---|
| `installation` | `Installation` | The seeded installation for untagged tests. A tagged test (`@fresh`, `@subpath` or `@upgrade`) gets its own installation instead. |
| `page` | Playwright `Page` | Signed in as the seeded administrator, except in `@fresh` tests. |
| `seed` | `Seed` | API seeding for this test's own data (below). |
| `scan` | `(screen: ScreenName) => Promise<void>` | Runs axe on the current page (E2E-09). Page objects call it from `open()`, so tests rarely call it themselves. |
| `visitor` | `(site: TestSite) => Promise<Page>` | A new page in a context with no session, for viewing a site as an anonymous visitor. |

## `Seed`

All calls go through `/api/v1` with the session and `X-QuickBuilder-Request: 1`.

| Call | Returns |
|---|---|
| `site(name?)` | `TestSite`, with a unique host name. |
| `page(site, { title, slug, parent? })` | `{ id, path }` |
| `publishedPage(site, { heading })` | A home page, already published, showing `heading` in an `<h1>`. |
| `draft(site, page, document)` | The draft version number. |

## Page objects (`pages/*.ts`)

Each page object has:

- `waitLoaded()`: waits for the screen's main heading or landmark, then calls `scan(screen)`;
- `open(...)`: goes to the screen by URL, then calls `waitLoaded()`;
- locators found by role and name.

Every helper that lands on another screen by clicking ends with that screen's
`waitLoaded()`. For example, `PageTree.openInEditor` ends with `EditorPage.waitLoaded()`.
That way, every screen a test reaches is scanned (E2E-09).

Tests may add page objects. The rules are:

- reach elements with `getByRole`, `getByLabel` or `getByText`, or with `getByTestId` only
  for the IDs listed in `tests/e2e/README.md`;
- never use `locator(...)` with a CSS or XPath string. The ESLint rule rejects every string
  argument to `locator`. Never pick an element by its position (`nth`, `first`) when it has a
  name;
- wait on what the member sees (`expect(locator).toBeVisible()`), never on `waitForTimeout`.

## `dragBlock(page, source, target)`

Drags a palette item or a canvas block onto a drop target with the pointer. It tries
`source.dragTo(target)` first, and falls back to dispatched drag events with a shared
`DataTransfer` (E2E-10).
