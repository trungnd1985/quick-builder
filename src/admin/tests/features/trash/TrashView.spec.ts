import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import axe from 'axe-core'
import TrashView from '../../../src/features/trash/TrashView.vue'
import type { TrashEntry, TrashList } from '../../../src/api/trash'
import en from '../../../src/i18n/en.json'

// 021 US3 and contracts/admin-trash.md §2: one tab per kind the member may view, newest first,
// who deleted each entry and when it is deleted permanently; a restore button only where the
// member may restore; an empty state that says how long things stay.

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric' } } },
})

function entry(overrides: Partial<TrashEntry> = {}): TrashEntry {
  return {
    kind: 'page',
    id: '22222222-2222-2222-2222-222222222222',
    label: 'Team',
    address: '/about/team',
    place: { parentId: '33333333-3333-3333-3333-333333333333', parentLabel: 'About', parentState: 'trashed' },
    trashedAtUtc: '2026-11-01T10:00:00Z',
    trashedBy: { userId: 'u1', displayName: 'Dana' },
    purgeOnUtc: '2026-12-01T10:00:00Z',
    wasLive: false,
    hadSchedule: true,
    canRestore: true,
    canPurge: false,
    ...overrides,
  }
}

/** Answers each trash list by kind; `forbidden` kinds answer 403. */
function serve(lists: Partial<Record<string, TrashList>>, forbidden: string[] = []) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (String(url).endsWith(`/sites/${SITE}`)) {
      return { ok: true, status: 200, json: async () => ({ id: SITE, trashRetentionDays: 60 }), headers: new Headers() }
    }

    const kind = ['pages', 'components', 'items', 'files'].find((k) => String(url).includes(`/trash/${k}`))
    if (kind && forbidden.includes(kind)) {
      return { ok: false, status: 403, json: async () => ({ title: 'Forbidden', status: 403, errorCode: 'identity.forbidden' }), headers: new Headers() }
    }

    const body = (kind && lists[kind]) ?? { entries: [], nextCursor: null, total: 0 }
    return { ok: true, status: 200, json: async () => (String(url).includes('/trash/') ? body : []), headers: new Headers() }
  }))
}

async function renderView(tab = 'page') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/trash', name: 'site-trash', component: TrashView },
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
    ],
  })
  await router.push({ name: 'site-trash', params: { siteId: SITE }, query: { tab } })
  await router.isReady()
  return render(TrashView, { global: { plugins: [i18n, router] } })
}

describe('TrashView', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('lists entries with who deleted them, where they were and the purge date, accessibly', async () => {
    serve({ pages: { entries: [entry()], nextCursor: null, total: 1 } })
    const { container } = await renderView()

    expect(await screen.findByText('Team')).toBeTruthy()
    expect(screen.getByText(en.trash.where.parent.trashed.replace('{parent}', 'About'))).toBeTruthy()
    expect(screen.getByText(en.trash.badge.wasScheduled)).toBeTruthy()
    expect(screen.getByRole('button', { name: en.trash.restoreLabel.replace('{label}', 'Team') })).toBeTruthy()

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'button-name', 'aria-valid-attr-value', 'aria-required-children', 'th-has-data-cells'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('links back to the site', async () => {
    serve({})
    await renderView()

    const back = await screen.findByRole('link', { name: en.trash.back })
    expect(back.getAttribute('href')).toBe(`/sites/${SITE}`)
  })

  it('offers no restore where the member may not restore', async () => {
    serve({ pages: { entries: [entry({ canRestore: false })], nextCursor: null, total: 1 } })
    await renderView()

    await screen.findByText('Team')
    expect(screen.queryByRole('button', { name: en.trash.restoreLabel.replace('{label}', 'Team') })).toBeNull()
  })

  it('leaves out a tab the member may not view', async () => {
    serve({}, ['components'])
    await renderView('component')

    await waitFor(() => expect(screen.queryByRole('tab', { name: en.trash.tab.component })).toBeNull())
    expect(screen.getByRole('tab', { name: en.trash.tab.page })).toBeTruthy()
  })

  it('says how long things stay when the trash is empty', async () => {
    serve({})
    await renderView()

    // The operator's setting, not the default (FR-025).
    expect(await screen.findByText(en.trash.empty.replace('{days}', '60'))).toBeTruthy()
  })

  it('shows the storage the media trash takes', async () => {
    serve({ files: { entries: [entry({ kind: 'file', label: 'poster.png', address: 'poster.png', place: { folderState: 'none' }, sizeBytes: 2048 })], nextCursor: null, total: 1, totalBytes: 2048 } })
    await renderView('file')

    expect(await screen.findByText(en.trash.mediaTotal.replace('{n}', '1').replace('{size}', '2.0 KB'))).toBeTruthy()
    await fireEvent.click(screen.getByRole('tab', { name: en.trash.tab.page }))
  })
})
