// Every kind's teardown project (`<kind>-teardown`): keep the logs, then remove the
// installation and its volumes (FR-003, E2E-12).
import { test as teardown } from '@playwright/test'
import { type Kind, saveLogs, stop } from './installations.js'

teardown('save logs and remove the installation', async () => {
  teardown.setTimeout(2 * 60_000)
  const kind = teardown.info().project.name.replace(/-teardown$/, '') as Kind
  await saveLogs(kind)
  await stop(kind)
})
