import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import PageTreeView from '../src/features/pages/PageTreeView.vue'
import type { PageNode, PagePermissions, PageTree } from '../src/api/pages'
import en from '../src/i18n/en.json'

// 010 US1, US2, US5, FR-033, SC-005, SC-008: the page tree. Every action is reachable
// from the keyboard, refusals speak in the lookup's words, and what is offered follows
// the permissions the server returns.

const SITE = '33333333-3333-3333-3333-333333333333'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric' } } },
})

const designer: PagePermissions = { canManageStructure: true, canEditPageDrafts: true, canManageComponents: true }
const editor: PagePermissions = { canManageStructure: false, canEditPageDrafts: true, canManageComponents: false }

function node(id: string, path: string, overrides: Partial<PageNode> = {}): PageNode {
  const segments = path.split('/').filter(Boolean)
  return {
    id,
    parentId: null,
    position: 0,
    depth: Math.max(segments.length, 1),
    title: segments.at(-1) ?? 'Home',
    slug: segments.at(-1) ?? 'home',
    path,
    isHome: path === '/',
    childCount: 0,
    version: 1,
    ...overrides,
  }
}

function sampleTree(permissions: PagePermissions = designer): PageTree {
  return {
    permissions,
    pages: [
      node('home', '/', { position: 0 }),
      node('about', '/about', { position: 1, childCount: 1 }),
      node('team', '/about/team', { parentId: 'about', depth: 2 }),
      node('contact', '/contact', { position: 2 }),
    ],
  }
}

type Reply = { status: number; body?: unknown }
type Call = { url: string; method: string; body?: unknown }

function stubFetch(tree: PageTree, replies: Record<string, Reply[]> = {}) {
  const calls: Call[] = []

  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ url, method, body })

    const key = `${method} ${url.split('?')[0]}`
    const scripted = replies[key]?.shift()
    let reply: Reply
    if (scripted) {
      reply = scripted
    } else if (url === '/api/v1/sites') {
      reply = { status: 200, body: [] }
    } else if (method === 'GET' && url.endsWith('/pages')) {
      reply = { status: 200, body: tree }
    } else if (method === 'GET') {
      const id = url.split('/').at(-1)
      const page = tree.pages.find((p) => p.id === id)
      reply = {
        status: 200,
        body: {
          ...page,
          createdAtUtc: '2026-09-27T08:00:00Z',
          updatedAtUtc: '2026-09-27T08:00:00Z',
          permissions: tree.permissions,
          draft: { schemaVersion: 1, version: 5, updatedAtUtc: '2026-09-27T08:00:00Z', updatedBy: { id: 'u', displayName: 'Dana' } },
        },
      }
    } else if (method === 'DELETE') {
      reply = { status: 204 }
    } else {
      reply = { status: 200, body: method === 'POST' && url.endsWith('/pages') ? { ...tree.pages[0], id: 'new' } : tree }
    }

    return {
      ok: reply.status < 300,
      status: reply.status,
      statusText: 'x',
      json: async () => reply.body,
      text: async () => JSON.stringify(reply.body ?? {}),
    }
  }))

  return calls
}

async function renderView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/pages', name: 'site-pages', component: PageTreeView },
      { path: '/sites/:siteId/pages/:pageId/edit', name: 'page-editor', component: { template: '<div />' } },
    ],
  })
  await router.push(`/sites/${SITE}/pages`)
  await router.isReady()

  const result = render(PageTreeView, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: en.pages.heading })).toBeTruthy())
  await waitFor(() => expect(screen.queryByText(en.pages.loading)).toBeNull())
  return result
}

async function violations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: {
      type: 'rule',
      values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order', 'aria-allowed-attr'],
    },
  })
  return results.violations
}

function selectButton(title: string): HTMLElement {
  return screen.getByRole('button', { name: en.pages.table.select.replace('{title}', title) })
}

describe('PageTreeView — the structure (US1)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('lists the pages in site order with their addresses and the home badge', async () => {
    stubFetch(sampleTree())
    const { container } = await renderView()

    await waitFor(() => expect(screen.getByText('/about/team')).toBeTruthy())
    const rows = within(container.querySelector('tbody')!).getAllByRole('row')
    expect(rows.map((r) => r.querySelector('td')?.textContent?.trim())).toEqual(['/', '/about', '/about/team', '/contact'])
    expect(container.querySelector('tbody .badge')?.textContent).toBe(en.pages.table.home)
    expect(await violations(container)).toEqual([])
  })

  it('shows the empty state for a site without pages', async () => {
    stubFetch({ pages: [], permissions: designer })
    await renderView()

    await waitFor(() => expect(screen.getByText(en.pages.empty)).toBeTruthy())
  })

  it('shows an error with a way to try again', async () => {
    stubFetch(sampleTree(), { [`GET /api/v1/pages/sites/${SITE}/pages`]: [{ status: 500, body: { errorCode: 'x', title: 'x', status: 500 } }] })
    await renderView()

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
    expect(screen.getByRole('button', { name: en.pages.retry })).toBeTruthy()
  })

  it('suggests a slug from the title until the slug is edited, and offers home only when there is none', async () => {
    const calls = stubFetch({ pages: [], permissions: designer })
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.addPage }))
    expect(screen.getByLabelText(/This is the home page/)).toBeTruthy()

    await fireEvent.update(screen.getByLabelText(en.pages.form.title), 'Dịch vụ & Giá')
    expect((screen.getByLabelText(en.pages.form.slug) as HTMLInputElement).value).toBe('dich-vu-gia')

    await fireEvent.update(screen.getByLabelText(en.pages.form.slug), 'services')
    await fireEvent.input(screen.getByLabelText(en.pages.form.slug))
    await fireEvent.update(screen.getByLabelText(en.pages.form.title), 'Dịch vụ')
    expect((screen.getByLabelText(en.pages.form.slug) as HTMLInputElement).value).toBe('services')

    await fireEvent.click(screen.getByRole('button', { name: en.pages.form.create }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({ title: 'Dịch vụ', slug: 'services', parentId: null })
  })

  it('shows a refusal in the lookup\'s words and keeps the dialog open', async () => {
    stubFetch(sampleTree(), {
      [`POST /api/v1/pages/sites/${SITE}/pages`]: [{ status: 409, body: { errorCode: 'pages.reserved_address', title: 'x', status: 409 } }],
    })
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.addPage }))
    expect(screen.queryByLabelText(/This is the home page/)).toBeNull()
    await fireEvent.update(screen.getByLabelText(en.pages.form.title), 'Admin')
    await fireEvent.click(screen.getByRole('button', { name: en.pages.form.create }))

    await waitFor(() => expect(screen.getByText(en.pages.errors.reserved_address)).toBeTruthy())
    expect(screen.getByRole('dialog')).toBeTruthy()
  })

  it('is read-only for an editor', async () => {
    stubFetch(sampleTree(editor))
    await renderView()

    await waitFor(() => expect(screen.getByText(en.pages.readOnly)).toBeTruthy())
    expect(screen.queryByRole('button', { name: en.pages.actions.addPage })).toBeNull()
    await fireEvent.click(selectButton('about'))
    expect(screen.queryByRole('button', { name: en.pages.actions.rename })).toBeNull()
  })
})

describe('PageTreeView — reorganising (US2)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('moves a page up by naming the sibling to follow', async () => {
    const calls = stubFetch(sampleTree())
    await renderView()

    await fireEvent.click(selectButton('contact'))
    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.moveUp }))

    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/contact/move'))).toBe(true))
    expect(calls.find((c) => c.url.endsWith('/contact/move'))?.body).toEqual({ parentId: null, afterPageId: 'home', version: 1 })
  })

  it('says what a change did to item addresses (016 TB-12)', async () => {
    const tree = sampleTree()
    stubFetch(tree, {
      [`POST /api/v1/pages/sites/${SITE}/pages/contact/move`]: [{ status: 200, body: { ...tree, warnings: [{ code: 'items_change_address', count: 12 }, { code: 'item_address_hidden', itemId: 'i1' }] } }],
    })
    await renderView()

    await fireEvent.click(selectButton('contact'))
    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.moveUp }))

    expect(await screen.findByText('12 published items now have new addresses.')).toBeTruthy()
    expect(screen.getByText(en.pages.addresses.hidden)).toBeTruthy()
  })

  it('disables moving past the ends of a sibling group', async () => {
    stubFetch(sampleTree())
    await renderView()

    await fireEvent.click(selectButton('team'))

    expect((screen.getByRole('button', { name: en.pages.actions.moveUp }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: en.pages.actions.moveDown }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('moves to another parent with the keyboard only, never offering the page itself or its children', async () => {
    const calls = stubFetch(sampleTree())
    const { container } = await renderView()

    await fireEvent.click(selectButton('about'))
    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.moveTo }))

    const parent = screen.getByLabelText(en.pages.move.parent) as HTMLSelectElement
    const offered = Array.from(parent.options).map((o) => o.value)
    expect(offered).not.toContain('about')
    expect(offered).not.toContain('team')
    expect(document.activeElement).toBe(parent)
    expect(await violations(container)).toEqual([])

    await fireEvent.update(parent, 'contact')
    await fireEvent.click(screen.getByRole('button', { name: en.pages.move.move }))

    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/about/move'))).toBe(true))
    expect(calls.find((c) => c.url.endsWith('/about/move'))?.body).toEqual({ parentId: 'contact', afterPageId: null, version: 1, redirectOldAddresses: false })
  })

  it('offers make-home only on a top-level page that is not already home', async () => {
    stubFetch(sampleTree())
    await renderView()

    await fireEvent.click(selectButton('team'))
    expect(screen.queryByRole('button', { name: en.pages.actions.makeHome })).toBeNull()
    await fireEvent.click(selectButton('contact'))
    expect(screen.getByRole('button', { name: en.pages.actions.makeHome })).toBeTruthy()
  })

  it('offers to reload when the page changed meanwhile', async () => {
    stubFetch(sampleTree(), {
      [`PUT /api/v1/pages/sites/${SITE}/pages/about`]: [{ status: 409, body: { errorCode: 'pages.out_of_date', title: 'x', status: 409 } }],
    })
    await renderView()

    await fireEvent.click(selectButton('about'))
    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.rename }))
    await fireEvent.update(screen.getByLabelText(en.pages.form.title), 'About us')
    await fireEvent.click(screen.getByRole('button', { name: en.pages.form.save }))

    await waitFor(() => expect(screen.getByText(en.pages.outOfDate)).toBeTruthy())
    expect(screen.getByRole('button', { name: en.pages.reload })).toBeTruthy()
  })
})

describe('PageTreeView — deleting (US5)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('confirms, deletes, and says how many children block a delete', async () => {
    const calls = stubFetch(sampleTree(), {
      [`DELETE /api/v1/pages/sites/${SITE}/pages/about`]: [{ status: 409, body: { errorCode: 'pages.has_children', title: 'x', status: 409, childCount: 1 } }],
    })
    const { container } = await renderView()

    await fireEvent.click(selectButton('about'))
    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.delete }))
    expect(screen.getByRole('alertdialog')).toBeTruthy()
    expect(await violations(container)).toEqual([])

    await fireEvent.click(screen.getByRole('button', { name: en.pages.confirm.delete }))
    await waitFor(() => expect(screen.getByText(en.pages.errors.has_children.replace('{count}', '1'))).toBeTruthy())

    await fireEvent.click(screen.getByRole('button', { name: en.pages.confirm.cancel }))
    await fireEvent.click(selectButton('contact'))
    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.delete }))
    await fireEvent.click(screen.getByRole('button', { name: en.pages.confirm.delete }))

    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.url.includes('/contact?version=1'))).toBe(true))
  })
})

describe('PageTreeView — scale (SC-005)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('shows all 2,000 pages of a full site', async () => {
    const pages: PageNode[] = Array.from({ length: 2000 }, (_, i) => node(`p${i}`, `/p${i}`, { position: i }))
    stubFetch({ pages, permissions: designer })

    const started = performance.now()
    const { container } = await renderView()
    await waitFor(() => expect(container.querySelectorAll('tbody tr').length).toBe(2000))
    const elapsed = performance.now() - started

    // A guard against accidental quadratic rendering; the real 2 s budget is timed in the
    // browser (quickstart §7). jsdom is far slower than a browser, hence the headroom.
    expect(elapsed).toBeLessThan(5000)
  })
})
