// The sub-path installation: under /cms behind Nginx (E2E-07), claimed, with a saved session.
import { test as setup } from '@playwright/test'
import { claim, orCleanUp } from './claim.js'
import { start, waitReady } from './installations.js'

setup('start and claim the sub-path installation', async ({ request }) => {
  setup.setTimeout(5 * 60_000)
  await orCleanUp('subpath', async () => {
    await start('subpath')
    await waitReady('subpath')
    await claim('subpath', request)
  })
})
