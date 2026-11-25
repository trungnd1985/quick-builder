// The upgrade installation (FR-016a, E2E-14): the previous release, seeded through the API,
// then upgraded in place to the image under test with its volumes kept, as
// docs/operators/upgrade.md has operators do. Runs only at release, where
// QB_E2E_PREVIOUS_IMAGE names the previous release.
import { test as setup } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'
import { headingDocument, Seed } from '../fixtures/api.js'
import { claim, orCleanUp } from './claim.js'
import { imageUnderTest, RESULTS, start, UPGRADE_SEED, upgradeTo, waitReady } from './installations.js'


setup('start the previous release, seed it, and upgrade it', async ({ playwright }) => {
  setup.setTimeout(8 * 60_000)
  const previous = process.env.QB_E2E_PREVIOUS_IMAGE
  if (!previous) {
    throw new Error('QB_E2E_PREVIOUS_IMAGE is not set; the upgrade projects exist only when it is.')
  }

  await orCleanUp('upgrade', async () => {
    await start('upgrade', previous)
    await waitReady('upgrade')

    const request = await playwright.request.newContext()
    await claim('upgrade', request)
    const seed = new Seed(request, 'upgrade')
    const site = await seed.site('Upgrade')
    await seed.publishedPage(site, 'Before upgrade')
    const drafted = await seed.page(site, { title: 'Drafted', slug: 'drafted' })
    await seed.draft(drafted, headingDocument('Draft before upgrade'))
    await request.dispose()

    await mkdir(RESULTS, { recursive: true })
    await writeFile(UPGRADE_SEED, JSON.stringify({ site, drafted }))

    await upgradeTo('upgrade', imageUnderTest())
    await waitReady('upgrade')
  })
})
