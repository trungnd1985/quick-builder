// What every test imports instead of @playwright/test (specs/025-e2e-harness,
// contracts/fixtures.md): the installation its project runs against, the signed-in page,
// API seeding of its own site, accessibility scans and an anonymous visitor.

import { test as base, type Page } from '@playwright/test'
import { ADMINISTRATOR, type Kind, origin, REQUEST_HEADER } from '../setup/installations.js'
import { scan as scanPage } from './a11y.js'
import { Seed, type TestSite } from './api.js'
import type { ScreenName } from './exceptions.js'

export { expect } from '@playwright/test'
export type { ScreenName } from './exceptions.js'
export type { TestPage, TestSite } from './api.js'

export type Scan = (screen: ScreenName) => Promise<void>

interface Fixtures {
  /** The installation this test's project runs against: the project's name. */
  kind: Kind
  /** API seeding, in this test's own session (none for `fresh` and `upgrade`). */
  seed: Seed
  /** Scans the current page for accessibility problems (FR-023a). Page objects call it. */
  scan: Scan
  /** Opens the site's home page as an anonymous visitor, in a context with no session. */
  visitor: (site: TestSite, path?: string) => Promise<Page>
}

export const test = base.extend<Fixtures>({
  kind: async ({}, use, testInfo) => {
    await use(testInfo.project.name as Kind)
  },

  // Each test signs in through the API for a session of its own, so a test that signs out
  // (the journey, the sub-path test) ends only its own session, never the one a test running
  // beside it is using. Tests on `fresh` and `upgrade` start signed out.
  storageState: async ({ kind, playwright }, use) => {
    if (kind === 'fresh' || kind === 'upgrade') {
      await use(undefined)
      return
    }

    const context = await playwright.request.newContext()
    const response = await context.post(`${origin(kind)}/api/v1/identity/sign-in`, {
      headers: REQUEST_HEADER,
      data: { email: ADMINISTRATOR.email, password: ADMINISTRATOR.password },
    })
    if (!response.ok()) {
      throw new Error(`Signing in to ${kind} failed (${response.status()}): ${await response.text()}`)
    }
    const state = await context.storageState()
    await context.dispose()
    await use(state)
  },

  seed: async ({ request, kind }, use) => {
    await use(new Seed(request, kind))
  },

  scan: async ({ page }, use, testInfo) => {
    await use((screen) => scanPage(page, screen, testInfo))
  },

  visitor: async ({ browser }, use) => {
    const contexts: Awaited<ReturnType<typeof browser.newContext>>[] = []
    await use(async (site, path = '') => {
      const context = await browser.newContext()
      contexts.push(context)
      const page = await context.newPage()
      await page.goto(new URL(path, site.visitorUrl).toString())
      return page
    })
    await Promise.all(contexts.map((c) => c.close()))
  },
})
