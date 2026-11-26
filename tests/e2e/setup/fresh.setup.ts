// The fresh installation: started but never claimed, for the setup wizard test (FR-006).
import { test as setup } from '@playwright/test'
import { orCleanUp } from './claim.js'
import { start, waitReady } from './installations.js'

setup('start the fresh installation', async () => {
  setup.setTimeout(5 * 60_000)
  await orCleanUp('fresh', async () => {
    await start('fresh')
    await waitReady('fresh')
  })
})
