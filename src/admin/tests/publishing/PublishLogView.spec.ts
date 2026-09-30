import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import PublishLogView from '../../src/features/publishing/PublishLogView.vue'
import { createAppRouter } from '../../src/app/router'
import { useAuthStore } from '../../src/app/authStore'
import type { LogEntry } from '../../src/api/publishing'
import en from '../../src/i18n/en.json'
import { i18n, SITE } from '../editor/harness'

// 012 US3, FR-017, clarification Q2: the site's publish log for Administrators — filters,
// paging, and deleted pages still listed and marked.

const dana = { id: 'u', displayName: 'Dana' }

function entry(n: number, overrides: Partial<LogEntry> = {}): LogEntry {
  return {
    id: `e${n}`,
    subjectKind: 'page',
    subjectId: `p${n}`,
    subjectLabel: `Page ${n}`,
    subjectPath: `/page-${n}`,
    subjectExists: true,
    action: 'publish',
    versionNumber: 1,
    restoredFromNumber: null,
    actor: dana,
    scheduleOwner: null,
    reasonCode: null,
    occurredAtUtc: '2026-09-29T09:00:00Z',
    ...overrides,
  }
}

interface Call { url: string }

function stub() {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    calls.push({ url })
    const query = new URL(url, 'http://x').searchParams
    let body: unknown
    if (url === '/api/v1/sites') {
      body = [{ id: SITE, name: 'Main site' }]
    } else if (query.get('subjectKind') === 'design') {
      body = { items: [entry(9, { subjectKind: 'design', subjectId: SITE, subjectLabel: '', subjectPath: null })], nextCursor: null }
    } else if (query.get('cursor') === 'next') {
      body = { items: [entry(3, { subjectExists: false, action: 'deleted', versionNumber: null })], nextCursor: null }
    } else {
      body = { items: [entry(1), entry(2, { actor: null, scheduleOwner: dana, action: 'scheduled_publish' })], nextCursor: 'next' }
    }
    return { ok: true, status: 200, statusText: 'x', headers: { get: () => null }, json: async () => body, text: async () => JSON.stringify(body) }
  }))
  return calls
}

async function renderLog() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/publish-log', name: 'site-publish-log', component: PublishLogView },
      { path: '/sites/:siteId/pages/:pageId/history', name: 'page-history', component: { template: '<div />' } },
      { path: '/sites/:siteId/components/:componentId/history', name: 'component-history', component: { template: '<div />' } },
    ],
  })
  await router.push(`/sites/${SITE}/publish-log`)
  await router.isReady()
  const result = render(PublishLogView, { global: { plugins: [i18n, router, createPinia()] } })
  await waitFor(() => expect(screen.getByRole('table')).toBeTruthy())
  return result
}

describe('PublishLogView (012 US3)', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('lists entries with who acted, and links to the history of subjects that exist', async () => {
    stub()
    await renderLog()

    const rows = within(screen.getByRole('table')).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(within(rows[0]!).getByRole('link', { name: 'Page 1 (/page-1)' }).getAttribute('href')).toBe(`/sites/${SITE}/pages/p1/history`)
    expect(within(rows[1]!).getByText('by schedule, set by Dana')).toBeTruthy()
  })

  it('pages with the cursor and marks deleted subjects without a link', async () => {
    const calls = stub()
    await renderLog()

    await fireEvent.click(screen.getByRole('button', { name: en.publishing.log.more }))

    await waitFor(() => expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(4))
    expect(calls.some((c) => c.url.includes('cursor=next'))).toBe(true)
    const deleted = within(screen.getByRole('table')).getAllByRole('row')[3]!
    expect(within(deleted).getByText(en.publishing.log.deleted)).toBeTruthy()
    expect(within(deleted).queryByRole('link')).toBeNull()
    expect(screen.queryByRole('button', { name: en.publishing.log.more })).toBeNull()
  })

  it('filters by kind', async () => {
    const calls = stub()
    await renderLog()

    await fireEvent.update(screen.getByLabelText(en.publishing.log.filters.kind), 'design')

    await waitFor(() => expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(2))
    expect(within(screen.getByRole('table')).getByText(en.publishing.log.design)).toBeTruthy()
    expect(calls.at(-1)!.url).toContain('subjectKind=design')
  })

  it('has no accessibility violations', async () => {
    stub()
    const { container } = await renderLog()

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'select-name', 'aria-valid-attr-value', 'button-name', 'link-name', 'th-has-data-cells'] },
    })
    expect(results.violations).toEqual([])
  })

  it('is offered only to administrators', async () => {
    stub()
    setActivePinia(createPinia())
    const auth = useAuthStore()
    vi.spyOn(auth, 'bootstrap').mockResolvedValue(undefined)
    auth.signedIn({ id: 'u', email: 'e@x.test', displayName: 'Dana', isAdministrator: false } as never)

    const router = createAppRouter(createMemoryHistory())
    await router.push(`/sites/${SITE}/publish-log`)

    expect(router.currentRoute.value.name).toBe('sites')
  })
})
