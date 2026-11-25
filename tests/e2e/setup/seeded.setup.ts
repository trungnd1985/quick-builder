// The seeded installation: claimed, with a saved session, shared by the untagged tests.
import { test as setup } from '@playwright/test'
import { claim, orCleanUp } from './claim.js'
import { start, waitReady } from './installations.js'

setup('start and claim the seeded installation', async ({ request }) => {
  setup.setTimeout(5 * 60_000)
  await orCleanUp('seeded', async () => {
    await start('seeded')
    await waitReady('seeded')
    await claim('seeded', request)
  })
})
