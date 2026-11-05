import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import type { CheckResult, Transfer } from '../../src/api/transfers'
import { i18n } from '../editor/harness'

// 020 US2, contracts/admin-transfer.md §2: upload, check, options, confirm.

const check: CheckResult = {
  archive: { format: 1, release: '1.4.0', createdAtUtc: '2026-11-02T09:00:00Z', source: { siteId: 's-1', siteName: 'Acme', enabled: true }, sameSiteAsTarget: false },
  versions: [], counts: { page: 3 },
  problems: [{ severity: 'warning', code: 'transfer.host_name_taken', subject: { kind: 'host_name', id: 'h', label: 'acme.test' }, detail: 'Used by another site here.' }],
  hostNamesLeftOut: ['acme.test'], hostNamesKept: [], conflicts: [], schemaChanges: [], removals: [], returns: [], changes: [], pastSchedules: [],
  allowedModes: ['new_site', 'merge'], totals: {},
}

const transfer = (extra: Partial<Transfer>): Transfer => ({
  id: 't-1', kind: 'import', trigger: 'admin', mode: 'new_site', startFresh: false, conflictPolicy: null, siteId: null, newSiteName: null,
  state: 'awaiting_confirmation', step: null, percent: 0, cancelRequested: false, archiveBytes: 10, archiveExpiresAtUtc: null, downloadable: false,
  checkResult: check, checkFingerprint: 'fp', checkedAtUtc: '2026-11-02T09:01:00Z', summary: null, report: null, failureCode: null,
  preRestoreTransferId: null, startedBy: null, createdAtUtc: '2026-11-02T09:00:00Z', finishedAtUtc: null, ...extra,
})

const api = vi.hoisted(() => ({
  upload: vi.fn(),
  check: vi.fn(),
  confirm: vi.fn(),
  get: vi.fn(),
  cancel: vi.fn(),
}))

vi.mock('../../src/api/transfers', async (original) => ({
  ...(await original<typeof import('../../src/api/transfers')>()),
  transfersApi: api,
}))

describe('ImportSiteView', () => {
  afterEach(() => vi.clearAllMocks())

  it('uploads and checks, then asks for a name, a host name and accepting the warnings before importing', async () => {
    api.upload.mockResolvedValue({ transfer: transfer({ state: 'uploading' }), abort: vi.fn(), done: Promise.resolve(transfer({ state: 'uploaded' })) })
    api.check.mockResolvedValue(transfer({}))
    api.confirm.mockResolvedValue(transfer({ state: 'confirmed', checkResult: check }))
    api.get.mockResolvedValue(transfer({ state: 'running' }))

    const { default: ImportSiteView } = await import('../../src/features/transfer/ImportSiteView.vue')
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })
    const { container } = render(ImportSiteView, { global: { plugins: [i18n, router] } })

    const input = screen.getByLabelText(en.transfer.import.file) as HTMLInputElement
    await fireEvent.update(input)
    Object.defineProperty(input, 'files', { value: [new File(['zip'], 'acme.qbsite')] })
    await fireEvent.change(input)
    await fireEvent.click(screen.getByRole('button', { name: en.transfer.import.upload }))

    await waitFor(() => expect(screen.getByLabelText(en.transfer.import.name)).toBeTruthy())
    expect((screen.getByLabelText(en.transfer.import.name) as HTMLInputElement).value).toBe('Acme (imported)')
    const submit = screen.getByRole('button', { name: en.transfer.import.confirm }) as HTMLButtonElement
    expect(submit.disabled).toBe(true)

    await fireEvent.update(screen.getByLabelText(en.transfer.import.hostName), 'copy.test')
    expect(submit.disabled).toBe(true)
    await fireEvent.click(screen.getByLabelText(en.transfer.import.acceptWarnings))
    expect(submit.disabled).toBe(false)
    expect((await axe.run(container)).violations).toEqual([])

    await fireEvent.click(submit)
    await waitFor(() => expect(api.confirm).toHaveBeenCalledWith('t-1', expect.objectContaining({
      newSiteName: 'Acme (imported)', newSiteHostName: 'copy.test', acceptWarnings: true, startFresh: false, checkFingerprint: 'fp',
    })))
    expect(api.check).toHaveBeenCalledWith('t-1', 'new_site')
  })
})
