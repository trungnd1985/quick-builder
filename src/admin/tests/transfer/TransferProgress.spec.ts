import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import TransferProgress from '../../src/features/transfer/TransferProgress.vue'
import type { Transfer } from '../../src/api/transfers'
import { i18n } from '../editor/harness'

// 020 contracts/admin-transfer.md §1 and §5.

const transfer = (extra: Partial<Transfer> = {}): Transfer => ({
  id: 't-1', kind: 'export', trigger: 'admin', mode: null, startFresh: false, conflictPolicy: null,
  siteId: 's-1', newSiteName: null, state: 'running', step: 'pages', percent: 41, cancelRequested: false,
  archiveBytes: null, archiveExpiresAtUtc: null, downloadable: false, checkResult: null, checkFingerprint: null,
  checkedAtUtc: null, summary: null, report: null, failureCode: null, preRestoreTransferId: null,
  startedBy: { id: 'u-1', displayName: 'Linh Pham' }, createdAtUtc: '2026-11-02T09:00:00Z', finishedAtUtc: null,
  ...extra,
})

function show(value: Transfer) {
  return render(TransferProgress, { props: { transfer: value }, global: { plugins: [i18n] } })
}

describe('TransferProgress', () => {
  afterEach(() => vi.restoreAllMocks())

  it('shows the step, the percent and who started it, accessibly', async () => {
    const { container } = show(transfer())

    expect(container.textContent).toContain(en.transfer.progress.exportTitle)
    expect(container.textContent).toContain(en.transfer.steps.pages)
    expect(container.textContent).toContain('Linh Pham')
    expect(screen.getByRole('progressbar').getAttribute('value')).toBe('41')
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('names the operator command when no member started it', () => {
    const { container } = show(transfer({ startedBy: null, trigger: 'cli' }))

    expect(container.textContent).toContain('Started from the command line')
  })

  it('cancels only after confirming', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    const { emitted } = show(transfer())

    await fireEvent.click(screen.getByRole('button', { name: en.transfer.progress.cancel }))
    expect(emitted().cancel).toBeUndefined()
    await fireEvent.click(screen.getByRole('button', { name: en.transfer.progress.cancel }))
    expect(emitted().cancel).toHaveLength(1)
    expect(confirm).toHaveBeenCalledTimes(2)
  })

  it('shows that it is stopping once a cancel was asked for', () => {
    show(transfer({ cancelRequested: true }))

    expect(screen.queryByRole('button', { name: en.transfer.progress.cancel })).toBeNull()
    expect(screen.getByText(en.transfer.progress.cancelling)).toBeTruthy()
  })

  it('announces the outcome assertively when it ends', async () => {
    const { rerender } = show(transfer())

    await rerender({ transfer: transfer({ state: 'failed', finishedAtUtc: '2026-11-02T09:05:00Z' }) })

    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(en.transfer.states.failed))
    expect(screen.queryByRole('progressbar')).toBeNull()
  })

  it('announces step changes politely', async () => {
    const { rerender, container } = show(transfer({ step: 'media', percent: 5 }))

    await rerender({ transfer: transfer({ step: 'pages', percent: 6 }) })

    await waitFor(() => expect(container.querySelector('[aria-live="polite"]')!.textContent).toContain(en.transfer.steps.pages))
  })
})
