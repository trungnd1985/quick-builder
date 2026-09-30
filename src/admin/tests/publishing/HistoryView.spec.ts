import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import axe from 'axe-core'
import HistoryView from '../../src/features/publishing/HistoryView.vue'
import EditorView from '../../src/editor/EditorView.vue'
import type { LogEntry, Publication, SubjectHistory } from '../../src/api/publishing'
import en from '../../src/i18n/en.json'
import { documentWith, i18n, PAGE, SITE, stubServer } from '../editor/harness'

// 012 US3, FR-013 to FR-016: a page's history — View opens the version read-only in the
// editor, Roll back and Copy to draft ask first — and the refusals in words.

const dana = { id: 'u', displayName: 'Dana' }
const base = `/api/v1/pages/sites/${SITE}/pages/${PAGE}`

function entry(action: LogEntry['action'], versionNumber: number | null, restoredFromNumber: number | null = null): LogEntry {
  return {
    id: `${action}-${versionNumber}-${Math.random()}`,
    subjectKind: 'page',
    subjectId: PAGE,
    subjectLabel: 'About',
    subjectPath: '/about',
    subjectExists: true,
    action,
    versionNumber,
    restoredFromNumber,
    actor: dana,
    scheduleOwner: null,
    reasonCode: null,
    occurredAtUtc: '2026-09-29T09:00:00Z',
  }
}

const live = (number: number): Publication => ({
  state: 'published',
  liveNumber: number,
  liveSinceUtc: '2026-09-29T09:00:00Z',
  liveBy: dana,
  hasUnpublishedChanges: false,
  liveUrl: 'https://example.test/about',
  pendingSchedules: [],
  failedSchedule: null,
})

interface Call { url: string; method: string; body?: Record<string, unknown> }

function stub(options: { canPublish?: boolean; canEditPageDrafts?: boolean; rollbackStatus?: number } = {}) {
  const calls: Call[] = []
  let history: SubjectHistory = {
    publication: live(3),
    retainedVersions: [3, 2],
    entries: [entry('publish', 3), entry('publish', 2), entry('publish', 1), entry('unpublish', null)],
  }

  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ url, method, body })

    let status = 200
    let reply: unknown
    if (url.endsWith('/history')) {
      reply = history
    } else if (url.endsWith('/rollback')) {
      status = options.rollbackStatus ?? 200
      if (status === 200) {
        history = { ...history, publication: live(4), retainedVersions: [4, ...history.retainedVersions], entries: [entry('rollback', 4, body.versionNumber), ...history.entries] }
        reply = history.publication
      } else {
        reply = { title: 'Conflict', status, errorCode: 'pages.version_missing_components', componentIds: ['c1'] }
      }
    } else if (url.endsWith('/copy-to-draft')) {
      reply = { schemaVersion: 1, version: 9, updatedAtUtc: '', updatedBy: dana, document: {} }
    } else if (url.endsWith('/draft')) {
      reply = { schemaVersion: 1, version: 8, updatedAtUtc: '', updatedBy: dana, document: {} }
    } else {
      reply = {
        id: PAGE, title: 'About', path: '/about', publication: history.publication,
        permissions: { canManageStructure: true, canEditPageDrafts: options.canEditPageDrafts ?? true, canManageComponents: true, canPublish: options.canPublish ?? true, canViewPublishLog: true },
      }
    }

    return { ok: status < 300, status, statusText: 'x', headers: { get: () => null }, json: async () => reply, text: async () => JSON.stringify(reply) }
  }))
  return calls
}

async function renderHistory(): Promise<{ router: Router; container: Element }> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/pages', name: 'site-pages', component: { template: '<div />' } },
      { path: '/sites/:siteId/pages/:pageId/history', name: 'page-history', component: HistoryView },
      { path: '/sites/:siteId/pages/:pageId/versions/:number', name: 'page-version', component: { template: '<div />' } },
    ],
  })
  await router.push(`/sites/${SITE}/pages/${PAGE}/history`)
  await router.isReady()
  const { container } = render(HistoryView, { props: { siteId: SITE, kind: 'page', subjectId: PAGE }, global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('table')).toBeTruthy())
  return { router, container }
}

const rowOf = (number: number) => screen.getAllByRole('row').find((r) => within(r).queryByText(`Version ${number}`) && within(r).queryByText('Published'))!

describe('HistoryView (012 US3)', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('lists what happened newest first, with actions only for retained versions', async () => {
    stub()
    await renderHistory()

    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('History of About')
    const rows = within(screen.getByRole('table')).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(4)
    expect(within(rows[0]!).getByText(en.publishing.history.live)).toBeTruthy()
    expect(within(rows[0]!).queryByRole('button', { name: /Roll back/ })).toBeNull()
    expect(within(rows[1]!).getByRole('button', { name: /Roll back/ })).toBeTruthy()
    expect(within(rows[2]!).getByText(en.publishing.history.notKept)).toBeTruthy()
    expect(within(rows[3]!).getByText(en.publishing.actions.unpublish)).toBeTruthy()
  })

  it('opens a version from View', async () => {
    stub()
    const { router } = await renderHistory()

    const view = within(rowOf(2)).getByRole('link', { name: /View/ })
    expect(view.getAttribute('href')).toBe(`/sites/${SITE}/pages/${PAGE}/versions/2`)
    await fireEvent.click(view)
    await waitFor(() => expect(router.currentRoute.value.name).toBe('page-version'))
  })

  it('rolls back only after confirmation', async () => {
    const calls = stub()
    await renderHistory()

    await fireEvent.click(within(rowOf(2)).getByRole('button', { name: /Roll back/ }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Publish version 2 again?')
    expect(calls.some((c) => c.url.endsWith('/rollback'))).toBe(false)

    await fireEvent.click(within(dialog).getByRole('button', { name: en.publishing.confirm.confirm }))

    await waitFor(() => expect(calls.find((c) => c.url.endsWith('/rollback'))?.body).toEqual({ versionNumber: 2 }))
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Version 2 is live again as version 4.'))
    expect(await screen.findByText(en.publishing.actions.rollback.replace('{number}', '2'))).toBeTruthy()
  })

  it('copies a version to the draft against the current draft version', async () => {
    const calls = stub()
    await renderHistory()

    await fireEvent.click(within(rowOf(2)).getByRole('button', { name: /Copy to draft/ }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Replace the draft with version 2?')
    await fireEvent.click(within(dialog).getByRole('button', { name: en.publishing.confirm.confirm }))

    await waitFor(() => expect(calls.find((c) => c.url.endsWith('/copy-to-draft'))).toBeTruthy())
    const copy = calls.find((c) => c.url.endsWith('/copy-to-draft'))!
    expect(copy.url).toBe(`${base}/versions/2/copy-to-draft`)
    expect(copy.body).toEqual({ draftVersion: 8 })
  })

  it('says why a rollback was refused', async () => {
    stub({ rollbackStatus: 409 })
    await renderHistory()

    await fireEvent.click(within(rowOf(2)).getByRole('button', { name: /Roll back/ }))
    await fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: en.publishing.confirm.confirm }))

    expect(await screen.findByText('That version uses 1 component(s) that no longer exist.')).toBeTruthy()
  })

  it('offers neither rollback nor copy without the permissions', async () => {
    stub({ canPublish: false, canEditPageDrafts: false })
    await renderHistory()

    expect(screen.queryByRole('button', { name: /Roll back/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Copy to draft/ })).toBeNull()
    expect(within(rowOf(2)).getByRole('link', { name: /View/ })).toBeTruthy()
  })

  it('works from the keyboard and has no accessibility violations', async () => {
    stub()
    const { container } = await renderHistory()

    for (const control of within(rowOf(2)).getAllByRole('button')) {
      expect(control.tagName).toBe('BUTTON')
    }

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'link-name', 'th-has-data-cells'] },
    })
    expect(results.violations).toEqual([])
  })
})

describe('A published version in the editor (012 FR-013)', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('opens read-only, says which version it is, and never saves', async () => {
    const server = stubServer()
    server.script(`GET ${base}/versions/2`, {
      status: 200,
      body: { number: 2, schemaVersion: 1, document: documentWith({ id: 's', type: 'section', props: {}, children: [] }), title: 'About', kind: 'publish', restoredFromNumber: null, publishedAtUtc: '2026-09-29T09:00:00Z', publishedBy: dana },
    })
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/sites/:siteId/pages/:pageId/history', name: 'page-history', component: { template: '<div />' } },
        { path: '/sites/:siteId/pages/:pageId/versions/:number', name: 'page-version', component: EditorView },
      ],
    })
    await router.push(`/sites/${SITE}/pages/${PAGE}/versions/2`)
    await router.isReady()

    render(EditorView, { props: { siteId: SITE, kind: 'page', documentId: PAGE, versionNumber: 2 }, global: { plugins: [i18n, router, pinia] } })

    const banner = await screen.findByText(/Version 2 of About, published .*Read-only\./)
    expect(banner).toBeTruthy()
    expect(server.calls.some((c) => c.url.endsWith('/draft'))).toBe(false)
    expect(screen.queryByRole('button', { name: en.publishing.menu.publish })).toBeNull()
    expect(screen.getByRole('link', { name: en.publishing.history.back }).getAttribute('href')).toBe(`/sites/${SITE}/pages/${PAGE}/history`)
    expect(server.calls.some((c) => c.method === 'PUT')).toBe(false)
  })
})
