import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import axe from 'axe-core'
import { createMemoryHistory, createRouter } from 'vue-router'
import en from '../../src/i18n/en.json'
import SiteImportBanner from '../../src/features/transfer/SiteImportBanner.vue'
import { isImportInProgress, isImportPaused } from '../../src/features/transfer/importLock'
import { ApiError } from '../../src/api/request'
import { i18n } from '../editor/harness'

// 020 FR-028, contracts/admin-transfer.md §4: shown on the site's screens while an import
// applies to it, the site's forms read-only meanwhile, and a reload offered when it ends.

const SITE = '33333333-3333-3333-3333-333333333333'

function stubSites(transfers: (object | null)[]) {
  const calls: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    calls.push(url)
    const transfer = transfers.length > 1 ? transfers.shift() : transfers[0]
    const body = { id: SITE, name: 'Acme', transfer }
    return {
      ok: true,
      status: 200,
      statusText: '',
      text: async () => JSON.stringify(body),
      json: async () => body,
      headers: new Headers(),
    } as unknown as Response
  }))
  return calls
}

async function show(path = `/sites/${SITE}`) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', component: { template: '<div />' } },
      { path: '/', component: { template: '<div />' } },
    ],
  })
  await router.push(path)
  await router.isReady()
  return render(SiteImportBanner, { props: { pollMs: 10, idlePollMs: 10 }, global: { plugins: [i18n, router] } })
}

const running = { id: 't-1', kind: 'import', mode: 'merge', state: 'running', startedAtUtc: '2026-11-02T09:00:00Z' }

describe('SiteImportBanner', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('says changes are paused while an import applies, and pauses the site for the forms, accessibly', async () => {
    stubSites([running])
    const { container, unmount } = await show()

    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('This site is being imported into'))
    expect(isImportPaused(SITE)).toBe(true)
    expect((await axe.run(container)).violations).toEqual([])
    unmount()
  })

  it('offers a reload once the import has finished, and unpauses the site', async () => {
    stubSites([running, null])
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    const { unmount } = await show()

    await waitFor(() => expect(screen.getByRole('button', { name: en.transfer.banner.reload })).toBeTruthy())
    expect(screen.getByRole('status').textContent).toContain(en.transfer.banner.finished)
    expect(isImportPaused(SITE)).toBe(false)
    await fireEvent.click(screen.getByRole('button', { name: en.transfer.banner.reload }))
    expect(reload).toHaveBeenCalledOnce()
    unmount()
  })

  it('shows nothing for an export, or outside a site', async () => {
    stubSites([{ ...running, kind: 'export' }])
    const first = await show()
    await new Promise((resolve) => setTimeout(resolve, 30))
    expect(first.container.textContent).toBe('')
    first.unmount()

    const calls = stubSites([running])
    const second = await show('/')
    await new Promise((resolve) => setTimeout(resolve, 30))
    expect(second.container.textContent).toBe('')
    expect(calls).toEqual([])
    second.unmount()
  })

  it('recognises a write the server refused because of the import', () => {
    expect(isImportInProgress(new ApiError('Paused', 423, { errorCode: 'site.import_in_progress', title: 'Paused', status: 423 }))).toBe(true)
    expect(isImportInProgress(new ApiError('x', 409, { errorCode: 'site.import_in_progress', title: 'x', status: 409 }))).toBe(false)
  })
})
