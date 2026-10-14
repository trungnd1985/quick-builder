import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import RedirectsView from '../../src/features/seo/RedirectsView.vue'
import RedirectDialog from '../../src/features/seo/RedirectDialog.vue'
import RedirectImportDialog from '../../src/features/seo/RedirectImportDialog.vue'
import type { Redirect } from '../../src/api/redirects'
import en from '../../src/i18n/en.json'

// 017 US2 (tasks T041, T050): the redirects screen, its dialog and the import report. States,
// warnings and refusals come from the server; the screen shows them and never decides them.

const SITE = '22222222-2222-2222-2222-222222222222'
const ROOT = `/api/v1/pages/sites/${SITE}/redirects`
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function redirect(overrides: Partial<Redirect> = {}): Redirect {
  return {
    id: 'r1',
    source: '/old',
    match: 'exact',
    target: { kind: 'path', path: '/new' },
    targetAddress: '/new',
    statusCode: 301,
    enabled: true,
    note: null,
    state: 'active',
    hiddenBy: null,
    origin: 'manual',
    hits: 0,
    lastUsedAt: null,
    createdAt: '2026-10-13T08:00:00Z',
    updatedAt: '2026-10-13T08:00:00Z',
    version: 5,
    ...overrides,
  }
}

type Reply = { status: number; body?: unknown }

function stubFetch(replies: Record<string, Reply[]>) {
  const calls: { url: string; method: string; body?: unknown; contentType?: string }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const headers = (init?.headers ?? {}) as Record<string, string>
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body
    calls.push({ url, method, body, contentType: headers['content-type'] })
    const key = `${method} ${url.split('?')[0]}`
    const reply = replies[key]?.shift() ?? { status: 200, body: { pages: [], permissions: {} } }
    return { ok: reply.status < 300, status: reply.status, statusText: 'x', json: async () => reply.body, text: async () => JSON.stringify(reply.body ?? {}) }
  }))
  return calls
}

async function renderView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/sites/:siteId/redirects', name: 'site-redirects', component: RedirectsView }],
  })
  await router.push(`/sites/${SITE}/redirects`)
  await router.isReady()
  return render(RedirectsView, { global: { plugins: [i18n, router] } })
}

afterEach(() => vi.unstubAllGlobals())

describe('Redirects screen', () => {
  it('lists redirects with their state and use, and filters by state', async () => {
    const calls = stubFetch({
      [`GET ${ROOT}`]: [
        { status: 200, body: { items: [redirect(), redirect({ id: 'r2', source: '/about', state: 'hidden', hits: 12 })], total: 2, limit: 10000 } },
        { status: 200, body: { items: [redirect({ id: 'r2', source: '/about', state: 'hidden' })], total: 1, limit: 10000 } },
      ],
    })
    await renderView()

    const table = await screen.findByRole('table')
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((r) => r.getAttribute('data-state'))).toEqual(['active', 'hidden'])
    expect(rows[1].textContent).toContain(en.redirects.states.hidden)
    expect(rows[1].textContent).toContain('12')

    await fireEvent.update(screen.getByLabelText(en.redirects.list.state), 'hidden')
    await waitFor(() => expect(calls.some((c) => c.url.includes('state=hidden'))).toBe(true))
  })

  it('sorts by use, most used first', async () => {
    const calls = stubFetch({ [`GET ${ROOT}`]: [{ status: 200, body: { items: [], total: 0, limit: 10000 } }, { status: 200, body: { items: [], total: 0, limit: 10000 } }] })
    await renderView()

    await fireEvent.update(await screen.findByLabelText(en.redirects.list.sort), 'hits')

    await waitFor(() => expect(calls.some((c) => c.url.includes('sort=hits') && c.url.includes('dir=desc'))).toBe(true))
  })

  it('offers the export as a download', async () => {
    stubFetch({ [`GET ${ROOT}`]: [{ status: 200, body: { items: [], total: 0, limit: 10000 } }] })
    await renderView()

    const link = await screen.findByRole('link', { name: en.redirects.list.export })
    expect(link.getAttribute('href')).toBe(`${ROOT}/export`)
  })

  it('has no axe violations', async () => {
    stubFetch({ [`GET ${ROOT}`]: [{ status: 200, body: { items: [redirect()], total: 1, limit: 10000 } }] })
    const { container } = await renderView()
    await screen.findByRole('table')

    const results = await axe.run(container, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })
})

describe('Redirect dialog', () => {
  const pages = [{ id: 'p1', parentId: null, position: 0, title: 'About', path: '/about', slug: 'about', depth: 1, isHome: false, childCount: 0, version: 1 }]

  it('creates a redirect to a page and closes', async () => {
    const calls = stubFetch({ [`POST ${ROOT}`]: [{ status: 201, body: redirect({ target: { kind: 'page', pageId: 'p1' }, targetAddress: '/about', warnings: [], shortened: false }) }] })
    const { emitted } = render(RedirectDialog, { props: { siteId: SITE, redirect: null, pages }, global: { plugins: [i18n] } })

    await fireEvent.update(screen.getByLabelText(en.redirects.dialog.source), '/about-us.html')
    await fireEvent.update(screen.getByLabelText(en.redirects.dialog.targetKind), 'page')
    await fireEvent.update(screen.getByLabelText(en.redirects.target.page), 'p1')
    await fireEvent.click(screen.getByRole('button', { name: en.redirects.dialog.save }))

    await waitFor(() => expect(emitted().saved).toBeTruthy())
    expect(calls.at(-1)?.body).toEqual({
      source: '/about-us.html',
      match: 'exact',
      target: { kind: 'page', pageId: 'p1' },
      statusCode: 301,
      enabled: true,
      note: null,
    })
  })

  it('shows warnings and a shortened chain before closing', async () => {
    stubFetch({ [`POST ${ROOT}`]: [{ status: 201, body: redirect({ shortened: true, targetAddress: '/final', warnings: ['source_holds_content'] }) }] })
    const { emitted } = render(RedirectDialog, { props: { siteId: SITE, redirect: null, pages }, global: { plugins: [i18n] } })

    await fireEvent.update(screen.getByLabelText(en.redirects.dialog.source), '/blog?page=2')
    await fireEvent.update(screen.getByLabelText(en.redirects.target.path), '/x')
    await fireEvent.click(screen.getByRole('button', { name: en.redirects.dialog.save }))

    const notes = await screen.findAllByRole('note')
    expect(notes.map((n) => n.textContent)).toEqual([
      expect.stringContaining('/final'),
      expect.stringContaining(en.redirects.warnings.source_holds_content.slice(0, 20)),
    ])
    expect(emitted().saved).toBeUndefined()

    await fireEvent.click(screen.getByRole('button', { name: en.redirects.dialog.done }))
    expect(emitted().saved).toBeTruthy()
  })

  it('does not offer a prefix for a source with a query', async () => {
    stubFetch({})
    render(RedirectDialog, { props: { siteId: SITE, redirect: null, pages }, global: { plugins: [i18n] } })

    await fireEvent.update(screen.getByLabelText(en.redirects.dialog.source), '/x?id=1')

    expect(screen.getByLabelText<HTMLInputElement>(en.redirects.match.prefix).disabled).toBe(true)
  })

  it('shows a refusal by its code', async () => {
    stubFetch({ [`POST ${ROOT}`]: [{ status: 409, body: { status: 409, errorCode: 'redirects.loop', redirectId: 'abcdef12-0000' } }] })
    render(RedirectDialog, { props: { siteId: SITE, redirect: null, pages }, global: { plugins: [i18n] } })

    await fireEvent.update(screen.getByLabelText(en.redirects.dialog.source), '/a')
    await fireEvent.update(screen.getByLabelText(en.redirects.target.path), '/b')
    await fireEvent.click(screen.getByRole('button', { name: en.redirects.dialog.save }))

    expect((await screen.findByRole('alert')).textContent).toContain(en.redirects.errors.loop)
  })

  it('has no axe violations', async () => {
    stubFetch({})
    const { container } = render(RedirectDialog, { props: { siteId: SITE, redirect: redirect(), pages }, global: { plugins: [i18n] } })

    const results = await axe.run(container, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })
})

describe('Redirect import', () => {
  it('sends the file as CSV and reports refused lines by number and reason', async () => {
    const calls = stubFetch({
      [`POST ${ROOT}/import`]: [{ status: 200, body: { added: 297, shortened: 0, refused: [{ line: 152, errorCode: 'redirects.duplicate', detail: null }, { line: 301, errorCode: 'redirects.loop', detail: null }] } }],
    })
    const { emitted } = render(RedirectImportDialog, { props: { siteId: SITE }, global: { plugins: [i18n] } })

    const file = new File(['/a,/b,301\n'], 'redirects.csv', { type: 'text/csv' })
    await fireEvent.change(screen.getByLabelText(en.redirects.import.file), { target: { files: [file] } })
    await fireEvent.click(screen.getByRole('button', { name: en.redirects.import.run }))

    const items = await screen.findAllByRole('listitem')
    expect(items.map((i) => i.textContent)).toEqual([
      `Line 152: ${en.redirects.errors.duplicate}`,
      `Line 301: ${en.redirects.errors.loop}`,
    ])
    expect(screen.getByRole('status').textContent).toContain('297 added, 2 refused')
    expect(calls.at(-1)?.contentType).toBe('text/csv')
    expect(emitted().imported).toBeTruthy()
  })

  it('shows a file refused whole', async () => {
    stubFetch({ [`POST ${ROOT}/import`]: [{ status: 400, body: { status: 400, errorCode: 'redirects.import_encoding' } }] })
    render(RedirectImportDialog, { props: { siteId: SITE }, global: { plugins: [i18n] } })

    await fireEvent.change(screen.getByLabelText(en.redirects.import.file), { target: { files: [new File(['x'], 'r.csv')] } })
    await fireEvent.click(screen.getByRole('button', { name: en.redirects.import.run }))

    expect((await screen.findByRole('alert')).textContent).toContain(en.redirects.errors.import_encoding)
  })
})
