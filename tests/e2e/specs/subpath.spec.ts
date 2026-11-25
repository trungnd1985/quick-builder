// US3, FR-014: the admin under /cms behind the documented Nginx location (#51), in a real
// browser. The admin works out its API's address from its own script's address, which only a
// browser runs; every request it makes must stay under /cms.
import { headingDocument } from '../fixtures/api.js'
import { expect, test } from '../fixtures/index.js'
import { EditorScreen } from '../pages/editor.js'
import { SignInScreen } from '../pages/sign-in.js'
import { SitesScreen } from '../pages/sites.js'
import { KINDS } from '../setup/installations.js'

test('the admin works under a base path @subpath', async ({ page, kind, scan, seed }) => {
  const { port, basePath } = KINDS[kind]
  const appOrigin = `http://127.0.0.1:${port}`
  const outside: string[] = []
  const missing: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.origin === appOrigin && !url.pathname.startsWith(`${basePath}/`)) outside.push(`${request.method()} ${url.pathname}`)
  })
  page.on('response', (response) => {
    const url = new URL(response.url())
    if (url.origin === appOrigin && response.status() === 404) missing.push(url.pathname)
  })

  // Seeded first: signing out below ends the session the API seeding shares.
  const site = await seed.site('Sub-path')
  const home = await seed.page(site, { title: 'Home', slug: 'home', isHome: true })
  await seed.draft(home, headingDocument('Under a base path'))

  const sites = new SitesScreen(page, kind, scan)

  await test.step('the admin loads at /cms/admin/', async () => {
    await page.goto(`${appOrigin}${basePath}/admin/`)
    await sites.waitLoaded()
  })

  await test.step('a deep link loads, and loads again on reload', async () => {
    await sites.open()
    await page.reload()
    await sites.waitLoaded()
  })

  await test.step('sign out and sign in', async () => {
    const signIn = new SignInScreen(page, kind, scan)
    await signIn.signOut()
    await signIn.signIn()
    await sites.waitLoaded()
  })

  await test.step('a draft saves in the editor', async () => {
    const editor = new EditorScreen(page, kind, scan)
    await editor.open(site, home)
    await editor.select(/Heading/)
    await editor.property('Text').fill('Saved under /cms')
    await editor.expectSaved()
  })

  expect(outside, 'Requests outside the base path').toEqual([])
  expect(missing, 'Requests answered 404 under the base path').toEqual([])
})
