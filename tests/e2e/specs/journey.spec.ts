// US1, FR-007 to FR-011: the journey a new installation's first member takes, in Chromium
// against the image. One test in named steps, so a failure says which step broke (SC-004).
import { expect, test } from '../fixtures/index.js'
import { PageTreeScreen } from '../pages/page-tree.js'
import { SignInScreen } from '../pages/sign-in.js'
import { SitesScreen } from '../pages/sites.js'

test('a member builds, publishes and republishes a page', async ({ page, kind, scan, seed, visitor }) => {
  const site = await seed.site('Journey')
  await seed.publishedPage(site, 'Welcome')

  await test.step('sign out and sign in again', async () => {
    const sites = new SitesScreen(page, kind, scan)
    await sites.open()
    const signIn = new SignInScreen(page, kind, scan)
    await signIn.signOut()
    await signIn.signIn()
    await sites.waitLoaded()
  })

  const tree = new PageTreeScreen(page, kind, scan)

  await test.step('create a page', async () => {
    await tree.open(site)
    await tree.createPage('About', 'about')
  })

  const editor = await test.step('drag a Section and a Heading', async () => {
    const editor = await tree.openInEditor('About')
    await editor.addBlock('Section', editor.emptyPage())
    await editor.addBlock('Heading', editor.emptySlot())
    await expect(editor.layer(/Section/)).toBeVisible()
    await expect(editor.layer(/Heading/)).toBeVisible()
    return editor
  })

  await test.step('edit properties and the draft saves', async () => {
    await editor.select(/Heading/)
    await editor.property('Text').fill('Hello from the browser')
    await editor.property('Heading level').selectOption('2')
    await editor.expectSaved()

    await page.reload()
    await editor.waitLoaded()
    await editor.expectOnCanvas('Hello from the browser')
    await editor.select(/Heading/)
    await expect(editor.property('Heading level')).toHaveValue('2')
  })

  await test.step('the draft is not public', async () => {
    const visit = await visitor(site, 'about')
    await expect(visit.getByRole('heading', { name: 'Hello from the browser' })).toHaveCount(0)
  })

  await test.step('publish and view', async () => {
    await editor.publish()
    const visit = await visitor(site, 'about')
    await expect(visit.getByRole('heading', { level: 2, name: 'Hello from the browser' })).toBeVisible()
  })

  await test.step('edit and republish', async () => {
    await editor.select(/Heading/)
    await editor.property('Text').fill('Hello again')
    await editor.expectSaved()
    await editor.publish()

    const visit = await visitor(site, 'about')
    await expect(visit.getByRole('heading', { level: 2, name: 'Hello again' })).toBeVisible()
    await expect(visit.getByText('Hello from the browser')).toHaveCount(0)
  })
})
