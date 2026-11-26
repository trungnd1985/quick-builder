// US1 scenario 1, FR-006: a fresh installation is claimed with the setup wizard, using the
// code from the application's logs as an operator reads it.
import { expect, test } from '../fixtures/index.js'
import { SetupScreen } from '../pages/setup.js'
import { SitesScreen } from '../pages/sites.js'
import { setupCode } from '../setup/installations.js'

test('the setup wizard claims a fresh installation @fresh', async ({ page, kind, scan }) => {
  const wizard = new SetupScreen(page, kind, scan)
  await wizard.open()
  await wizard.complete({
    setupCode: await setupCode(kind),
    displayName: 'Wizard Operator',
    email: 'wizard@e2e.test',
    password: 'wizard test passphrase 2026',
  })

  await new SitesScreen(page, kind, scan).waitLoaded()
  await expect(page.getByText('Signed in as Wizard Operator')).toBeVisible()
})
