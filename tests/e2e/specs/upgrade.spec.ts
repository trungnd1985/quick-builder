// US5 scenario 3, FR-016a: after upgrading from the previous release with the data kept, the
// administrator signs in, the draft opens in the editor with its content, and visitors still
// see the published page. Runs only at release: the upgrade project exists only when
// QB_E2E_PREVIOUS_IMAGE names the previous release (playwright.config.ts).
import { readFile } from 'node:fs/promises'
import { expect, test, type TestPage, type TestSite } from '../fixtures/index.js'
import { EditorScreen } from '../pages/editor.js'
import { SignInScreen } from '../pages/sign-in.js'
import { SitesScreen } from '../pages/sites.js'
import { UPGRADE_SEED } from '../setup/installations.js'

test('content and sign-in survive an upgrade from the previous release @upgrade', async ({ page, kind, scan, visitor }) => {
  const { site, drafted } = JSON.parse(await readFile(UPGRADE_SEED, 'utf8')) as { site: TestSite; drafted: TestPage }

  await test.step('sign in after the upgrade', async () => {
    const signIn = new SignInScreen(page, kind, scan)
    await signIn.open()
    await signIn.signIn()
    await new SitesScreen(page, kind, scan).waitLoaded()
  })

  await test.step('the draft opens with its content', async () => {
    const editor = new EditorScreen(page, kind, scan)
    await editor.open(site, drafted)
    await editor.expectOnCanvas('Draft before upgrade')
  })

  await test.step('visitors still see the published page', async () => {
    const visit = await visitor(site)
    await expect(visit.getByRole('heading', { name: 'Before upgrade' })).toBeVisible()
  })
})
