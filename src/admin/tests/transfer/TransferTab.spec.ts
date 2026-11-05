import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import TransferTab from '../../src/features/transfer/TransferTab.vue'
import type { Site } from '../../src/api/sites'
import type { Transfer } from '../../src/api/transfers'
import { i18n } from '../editor/harness'

// 020 US1, contracts/admin-transfer.md §1: export, follow, download, delete, recent transfers.

const SITE = '22222222-2222-2222-2222-222222222222'

const site = (canTransfer = true): Site => ({
  id: SITE, name: 'Acme', hostNames: ['acme.test'], createdAtUtc: '2026-11-01T08:00:00Z', primaryHostName: 'acme.test',
  hostNameDetails: [{ hostName: 'acme.test', displayName: 'acme.test', isPrimary: true }], scheme: 'https', baseUrl: 'https://acme.test/',
  defaultLanguage: 'en', status: 'enabled', version: 1, updatedAtUtc: '2026-11-01T08:00:00Z',
  permissions: { canManageSettings: true, canManageHostNames: true, canManageAvailability: true, canTransfer },
})

const transfer = (extra: Partial<Transfer> = {}): Transfer => ({
  id: 't-1', kind: 'export', trigger: 'admin', mode: null, startFresh: false, conflictPolicy: null, siteId: SITE, newSiteName: null,
  state: 'succeeded', step: null, percent: 100, cancelRequested: false, archiveBytes: 2_500_000, archiveExpiresAtUtc: '2026-11-09T09:00:00Z',
  downloadable: true, checkResult: null, checkFingerprint: null, checkedAtUtc: null,
  summary: { counts: { page: 84, item: 1240 }, archiveBytes: 2_500_000, problems: [{ code: 'transfer.media_missing', kind: 'media_file', id: 'f-1' }] },
  report: null, failureCode: null, preRestoreTransferId: null, startedBy: { id: 'u-1', displayName: 'Linh Pham' },
  createdAtUtc: '2026-11-02T09:00:00Z', finishedAtUtc: '2026-11-02T09:05:00Z', ...extra,
})

type Reply = { status: number; body?: unknown }

function stubFetch(replies: Record<string, Reply[]>) {
  const calls: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${(init?.method ?? 'GET').toUpperCase()} ${url}`
    calls.push(key)
    const reply = replies[key]?.shift() ?? { status: 404, body: { errorCode: 'test.unexpected', title: key, status: 404 } }
    return {
      ok: reply.status >= 200 && reply.status < 300,
      status: reply.status,
      statusText: '',
      text: async () => (reply.body === undefined ? '' : JSON.stringify(reply.body)),
      json: async () => reply.body,
      headers: new Headers(),
    } as unknown as Response
  }))
  return calls
}

function show(value = site()) {
  return render(TransferTab, { props: { site: value }, global: { plugins: [i18n] } })
}

describe('TransferTab', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('is not shown to members who cannot transfer the site', () => {
    stubFetch({})
    const { container } = show(site(false))

    expect(container.textContent).toBe('')
  })

  it('shows the latest export with its size, expiry, counts, problems, download and delete, accessibly', async () => {
    stubFetch({ [`GET /api/v1/sites/${SITE}/transfers`]: [{ status: 200, body: [transfer()] }] })
    const { container } = show()

    await waitFor(() => expect(screen.getByRole('link', { name: en.transfer.export.download })).toBeTruthy())
    expect(screen.getByRole('link', { name: en.transfer.export.download }).getAttribute('href')).toBe('/api/v1/transfers/t-1/archive')
    expect(container.textContent).toContain('84 pages')
    expect(container.textContent).toContain('1240 items')
    expect(container.textContent).toContain('f-1')
    expect(container.textContent).toContain('Linh Pham')
    expect(screen.getByRole('button', { name: en.transfer.export.delete })).toBeTruthy()
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('starts an export and follows it', async () => {
    const calls = stubFetch({
      [`GET /api/v1/sites/${SITE}/transfers`]: [{ status: 200, body: [] }],
      [`POST /api/v1/sites/${SITE}/exports`]: [{ status: 202, body: transfer({ state: 'queued', percent: 0, downloadable: false, summary: null }) }],
    })
    show()

    await fireEvent.click(await screen.findByRole('button', { name: en.transfer.export.start }))

    await waitFor(() => expect(screen.getByText(en.transfer.export.started)).toBeTruthy())
    expect(calls).toContain(`POST /api/v1/sites/${SITE}/exports`)
    expect(screen.getByRole('progressbar')).toBeTruthy()
    expect((screen.getByRole('button', { name: en.transfer.export.start }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText(en.transfer.export.busy)).toBeTruthy()
  })

  it('says why an export is refused when the site is busy', async () => {
    stubFetch({
      [`GET /api/v1/sites/${SITE}/transfers`]: [{ status: 200, body: [] }],
      [`POST /api/v1/sites/${SITE}/exports`]: [{ status: 409, body: { errorCode: 'transfer.busy', title: 'Conflict', status: 409 } }],
    })
    show()

    await fireEvent.click(await screen.findByRole('button', { name: en.transfer.export.start }))

    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(en.transfer.errors.transfer_busy))
  })

  it('deletes the archive only after confirming', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    const calls = stubFetch({
      [`GET /api/v1/sites/${SITE}/transfers`]: [{ status: 200, body: [transfer()] }, { status: 200, body: [transfer({ downloadable: false, state: 'expired' })] }],
      [`DELETE /api/v1/transfers/t-1`]: [{ status: 204 }],
    })
    show()

    const remove = await screen.findByRole('button', { name: en.transfer.export.delete })
    await fireEvent.click(remove)
    expect(calls).not.toContain('DELETE /api/v1/transfers/t-1')
    await fireEvent.click(remove)

    await waitFor(() => expect(screen.getByText(en.transfer.export.deleted)).toBeTruthy())
    expect(confirm).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('link', { name: en.transfer.export.download })).toBeNull()
  })
})
