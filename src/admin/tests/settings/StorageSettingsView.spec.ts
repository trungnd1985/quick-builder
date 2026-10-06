import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import StorageSettingsView from '../../src/features/settings/StorageSettingsView.vue'
import en from '../../src/i18n/en.json'

// 013 MV-21 (tasks T079): the provider, the latest consistency check, the files it found
// missing with links into their libraries, and a Run check button that waits while one runs.

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' } } },
})

const SITE = '11111111-1111-1111-1111-111111111111'
const FILE = '22222222-2222-2222-2222-222222222222'

const completed = {
  id: 'c1',
  trigger: 'startup',
  provider: 'S3',
  status: 'completed',
  startedAtUtc: '2026-10-01T10:00:00Z',
  finishedAtUtc: '2026-10-01T10:01:00Z',
  filesChecked: 120,
  missingCount: 1,
  missing: [{ siteId: SITE, fileId: FILE }],
}

type Route = { status?: number; body: unknown }

function respond(routes: Record<string, Route | Route[]>) {
  const calls: string[] = []
  const fetch = vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${url}`
    calls.push(key)
    const entry = routes[key]
    if (entry === undefined) throw new Error(`Unexpected request: ${key}`)
    const reply = Array.isArray(entry) ? (entry.length > 1 ? entry.shift()! : entry[0]) : entry
    const status = reply.status ?? 200
    return {
      ok: status < 400,
      status,
      statusText: '',
      headers: new Headers({ 'Content-Type': status < 400 ? 'application/json' : 'application/problem+json' }),
      json: async () => reply.body,
      text: async () => JSON.stringify(reply.body),
    }
  })
  return { fetch, calls }
}

async function renderView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/settings/storage', name: 'settings-storage', component: StorageSettingsView },
      { path: '/sites/:siteId/media', name: 'site-media', component: { template: '<p />' } },
    ],
  })
  await router.push('/settings/storage')
  await router.isReady()
  return render(StorageSettingsView, { global: { plugins: [i18n, router] } })
}

describe('StorageSettingsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.unstubAllGlobals()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows the provider, the latest check and the missing files linked to their library, accessibly', async () => {
    const { fetch } = respond({
      'GET /api/v1/sites': { body: [{ id: SITE, name: 'Main site', hostNames: [] }] },
      'GET /api/v1/media/storage-check': { body: { provider: 'S3', latestCheck: completed } },
    })
    vi.stubGlobal('fetch', fetch)
    const { container } = await renderView()

    await waitFor(() => expect(screen.getByText(en.storageSettings.providers.S3)).toBeTruthy())
    expect(screen.getByText('120')).toBeTruthy()
    expect(screen.getByText(en.storageSettings.statuses.completed)).toBeTruthy()

    const link = screen.getByRole('link', { name: FILE })
    expect(link.getAttribute('href')).toBe(`/sites/${SITE}/media?file=${FILE}`)
    expect(screen.getByRole('rowheader', { name: 'Main site' })).toBeTruthy()

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'button-name', 'heading-order', 'link-name', 'th-has-data-cells'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('says so when no check has run yet', async () => {
    const { fetch } = respond({
      'GET /api/v1/sites': { body: [] },
      'GET /api/v1/media/storage-check': { body: { provider: 'Local', latestCheck: null } },
    })
    vi.stubGlobal('fetch', fetch)
    await renderView()

    await waitFor(() => expect(screen.getByText(en.storageSettings.empty)).toBeTruthy())
    expect(screen.getByText(en.storageSettings.providers.Local)).toBeTruthy()
    expect((screen.getByRole('button', { name: en.storageSettings.run }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('starts a check, disables the button while it runs, and polls every five seconds until it finishes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const running = { ...completed, id: 'c2', trigger: 'admin', status: 'running', finishedAtUtc: null, filesChecked: 0, missingCount: 0, missing: [] }
    const { fetch, calls } = respond({
      'GET /api/v1/sites': { body: [] },
      'GET /api/v1/media/storage-check': [
        { body: { provider: 'S3', latestCheck: null } },
        { body: { provider: 'S3', latestCheck: running } },
        { body: { provider: 'S3', latestCheck: { ...completed, id: 'c2', trigger: 'admin', missingCount: 0, missing: [] } } },
      ],
      'POST /api/v1/media/storage-check': { status: 202, body: running },
    })
    vi.stubGlobal('fetch', fetch)
    await renderView()

    await waitFor(() => expect(screen.getByText(en.storageSettings.empty)).toBeTruthy())
    await fireEvent.click(screen.getByRole('button', { name: en.storageSettings.run }))

    const busy = await screen.findByRole('button', { name: en.storageSettings.running })
    expect((busy as HTMLButtonElement).disabled).toBe(true)

    const gets = () => calls.filter((c) => c === 'GET /api/v1/media/storage-check').length
    expect(gets()).toBe(1)
    await vi.advanceTimersByTimeAsync(5000)
    await waitFor(() => expect(gets()).toBe(2))
    await vi.advanceTimersByTimeAsync(5000)
    await waitFor(() => expect(screen.getByText(en.storageSettings.allPresent)).toBeTruthy())
    expect((screen.getByRole('button', { name: en.storageSettings.run }) as HTMLButtonElement).disabled).toBe(false)

    // Finished: no more polling.
    await vi.advanceTimersByTimeAsync(15000)
    expect(gets()).toBe(3)
  })

  it('explains a refusal because a check is already running', async () => {
    const { fetch } = respond({
      'GET /api/v1/sites': { body: [] },
      'GET /api/v1/media/storage-check': { body: { provider: 'S3', latestCheck: completed } },
      'POST /api/v1/media/storage-check': {
        status: 409,
        body: { title: 'Conflict', status: 409, errorCode: 'media.storage_check_running' },
      },
    })
    vi.stubGlobal('fetch', fetch)
    await renderView()

    await fireEvent.click(await screen.findByRole('button', { name: en.storageSettings.run }))

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain(en.storageSettings.errors.running))
  })
})
