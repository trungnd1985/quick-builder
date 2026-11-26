// US2 scenario 1, FR-012: an image uploaded in the media library is placed on a page with an
// Image block and served to visitors from the site's own address.
import path from 'node:path'
import { expect, test } from '../fixtures/index.js'
import { EditorScreen } from '../pages/editor.js'
import { MediaScreen } from '../pages/media.js'

const PIXEL = path.resolve(import.meta.dirname, '..', 'fixtures', 'files', 'pixel.png')

test('an uploaded image is shown on the published page', async ({ page, kind, scan, seed, visitor }) => {
  const site = await seed.site('Media')
  const home = await seed.publishedPage(site, 'Gallery')

  await test.step('upload an image', async () => {
    const media = new MediaScreen(page, kind, scan)
    await media.open(site)
    await media.upload(PIXEL, 'pixel.png')
  })

  const editor = new EditorScreen(page, kind, scan)

  await test.step('place it with an Image block', async () => {
    await editor.open(site, home)
    await editor.addBlock('Image', page.getByRole('region', { name: 'Page' }).getByText('Gallery', { exact: true }))
    await editor.select(/Image/)
    await page.getByRole('complementary').getByRole('button', { name: 'Choose image' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Choose pixel.png' }).click()
    await editor.property('Alternative text').fill('A single pixel')
    await editor.expectSaved()
    await editor.publish()
  })

  await test.step('a visitor sees it, served from the site', async () => {
    const visit = await visitor(site)
    const image = visit.getByRole('img', { name: 'A single pixel' })
    await expect(image).toBeVisible()
    // Loaded by the browser itself, which resolves the site's host name (E2E-05); the API
    // request context does not use Chromium's host resolver rules.
    await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth)).toBe(1)
    const source = new URL(await image.evaluate((img: HTMLImageElement) => img.currentSrc))
    expect(source.hostname).toBe(site.hostName)
  })
})
