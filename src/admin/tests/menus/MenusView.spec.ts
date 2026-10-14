import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import MenusView from '../../src/features/menus/MenusView.vue'
import en from '../../src/i18n/en.json'
import { designer, editor, list, menu, MENU, problem, SITE, stubFetch, type Reply } from './menuFixtures'

// 024 US1 (FR-001 to FR-003): the site's menus — create, rename, duplicate, delete with the
// drafts that use it listed — keyboard-usable, and read-only for an Editor.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
const ROOT = `/api/v1/pages/sites/${SITE}/menus`

afterEach(() => vi.unstubAllGlobals())

function fallbackFor(permissions = designer) {
  const main = menu({ schemaVersion: 1, entries: [] }, permissions, { entryCount: 4, brokenCount: 1, usedBy: 2 })
  return (method: string, url: string, body: unknown): Reply => {
    if (url === '/api/v1/sites') return { status: 200, body: [] }
    if (url.endsWith('/usages')) return { status: 200, body: { items: [{ kind: 'page', id: 'p1', label: 'Home', inDraft: true, inLive: false }] } }
    if (method === 'DELETE') return { status: 204 }
    if (method === 'GET') return { status: 200, body: list([main], permissions) }
    return { status: 200, body: menu({ schemaVersion: 1, entries: [] }, permissions, { name: (body as { name?: string })?.name ?? 'x' }) }
  }
}

async function renderView() {
  setActivePinia(createPinia())
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/menus', name: 'site-menus', component: MenusView },
      { path: '/sites/:siteId/menus/:menuId', name: 'site-menu', component: { template: '<div />' } },
    ],
  })
  await router.push(`/sites/${SITE}/menus`)
  await router.isReady()
  const result = render(MenusView, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: en.menus.heading })).toBeTruthy())
  await waitFor(() => expect(screen.queryByText(en.menus.loading)).toBeNull())
  return result
}

describe('the menus list', () => {
  it('lists menus with their counts and links to each editor', async () => {
    stubFetch(fallbackFor())
    await renderView()

    const row = screen.getByRole('row', { name: /Main/ })
    expect(within(row).getByText('4 entries')).toBeTruthy()
    expect(within(row).getByText('1 broken')).toBeTruthy()
    expect(within(row).getByText('2 pages or components')).toBeTruthy()
    expect(within(row).getByRole('link', { name: 'Edit the entries of Main' }).getAttribute('href')).toBe(`/sites/${SITE}/menus/${MENU}`)
  })

  it('creates a menu and refuses a duplicate name with the server\'s reason', async () => {
    const calls = stubFetch(fallbackFor(), { [`POST ${ROOT}`]: [{ status: 201, body: menu({ schemaVersion: 1, entries: [] }) }, problem(409, 'pages.duplicate_menu_name')] })
    await renderView()

    await fireEvent.update(screen.getByLabelText(en.menus.create.name), 'Footer')
    await fireEvent.click(screen.getByRole('button', { name: en.menus.create.create }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && (c.body as { name: string }).name === 'Footer')).toBe(true))

    await fireEvent.update(screen.getByLabelText(en.menus.create.name), 'main')
    await fireEvent.click(await screen.findByRole('button', { name: en.menus.create.create }))
    expect((await screen.findByRole('alert')).textContent).toContain(en.menus.errors.duplicate_menu_name)
  })

  it('renames with the version it saw, and duplicates with a "(copy)" name', async () => {
    const calls = stubFetch(fallbackFor())
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: 'Rename Main' }))
    const input = screen.getByLabelText(en.menus.create.name, { selector: `#menu-rename-${MENU}` })
    expect(document.activeElement).toBe(input)
    await fireEvent.update(input, 'Primary')
    await fireEvent.submit(input.closest('form')!)
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ name: 'Primary', version: 7 }))

    await fireEvent.click(screen.getByRole('button', { name: 'Duplicate Main' }))
    await waitFor(() => expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Main (copy)', duplicateOf: MENU }))
  })

  it('asks before deleting, naming the drafts that still show the menu', async () => {
    const calls = stubFetch(fallbackFor())
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: 'Delete Main' }))
    const dialog = await screen.findByRole('alertdialog')
    await waitFor(() => expect(within(dialog).getByText(/Home/)).toBeTruthy())

    await fireEvent.click(within(dialog).getByRole('button', { name: en.menus.confirm.delete }))
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.url.endsWith(`/menus/${MENU}?version=7`))).toBe(true))
  })

  it('shows a refused delete\'s counts', async () => {
    stubFetch(fallbackFor(), { [`DELETE ${ROOT}/${MENU}`]: [problem(409, 'pages.menu_in_use_by_published_content', { pages: 3, components: 1 })] })
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: 'Delete Main' }))
    await fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: en.menus.confirm.delete }))

    await waitFor(() => expect(screen.getByRole('alertdialog').textContent).toContain('3 published page(s) and 1 published component(s)'))
  })

  it('is read-only for an Editor', async () => {
    stubFetch(fallbackFor(editor))
    await renderView()

    expect(screen.getByText(en.menus.readOnly)).toBeTruthy()
    expect(screen.queryByLabelText(en.menus.create.name)).toBeNull()
    expect(screen.queryByRole('button', { name: 'Delete Main' })).toBeNull()
    expect(screen.getByRole('link', { name: 'Edit the entries of Main' })).toBeTruthy()
  })

  it('has no accessibility violations', async () => {
    stubFetch(fallbackFor())
    const { container } = await renderView()

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order', 'link-name'] } })
    expect(results.violations).toEqual([])
  })
})
