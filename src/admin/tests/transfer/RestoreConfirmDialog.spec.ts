import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import RestoreConfirmDialog from '../../src/features/transfer/RestoreConfirmDialog.vue'
import type { CheckResult } from '../../src/api/transfers'
import { i18n } from '../editor/harness'

// 020 FR-027b, contracts/admin-transfer.md §1 and §5: what a restore removes, brings back and
// changes, the export made first, and the site's name typed to confirm.

const result = (): CheckResult => ({
  archive: { format: 1, release: '0.3.0', createdAtUtc: '2026-11-01T08:00:00Z', source: { siteId: 's-1', siteName: 'Acme', enabled: true }, sameSiteAsTarget: true },
  versions: [],
  counts: { page: 4 },
  problems: [],
  hostNamesLeftOut: [],
  hostNamesKept: [],
  conflicts: [],
  schemaChanges: [{ collectionId: 'c-1', field: 'f3', change: 'FieldRemoved', valuesConverted: 0, valuesLost: 12 }],
  removals: [{ kind: 'page', id: 'p-9', label: '/news' }, { kind: 'media_file', id: 'm-9', label: 'photo.png' }],
  returns: [{ kind: 'item', id: 'i-1', label: 'hello' }],
  changes: [],
  pastSchedules: [],
  allowedModes: ['new_site', 'merge', 'restore'],
  totals: {},
})

function show(busy = false) {
  return render(RestoreConfirmDialog, { props: { result: result(), siteName: 'Acme', busy }, global: { plugins: [i18n] } })
}

describe('RestoreConfirmDialog', () => {
  it('lists what goes, comes back and changes, and says the site is exported first, accessibly', async () => {
    const { container } = show()

    expect(screen.getByRole('alertdialog', { name: 'Restore “Acme” from this archive?' })).toBeTruthy()
    expect(container.textContent).toContain('2 documents are removed')
    expect(container.textContent).toContain('/news')
    expect(container.textContent).toContain('photo.png')
    expect(container.textContent).toContain('One document comes back')
    expect(container.textContent).toContain('Nothing changes')
    expect(container.textContent).toContain('Field f3: 12 values will be lost.')
    expect(container.textContent).toContain(en.transfer.restore.backupFirst)
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('needs the site name typed exactly, with a visible error otherwise', async () => {
    const { emitted } = show()
    const field = screen.getByLabelText('Type the site name, Acme, to confirm')
    await waitFor(() => expect(document.activeElement).toBe(field))

    await fireEvent.update(field, 'acme')
    await fireEvent.click(screen.getByRole('button', { name: en.transfer.restore.confirm }))
    expect(emitted().confirm).toBeUndefined()
    expect(screen.getByText(en.transfer.restore.nameMismatch)).toBeTruthy()
    expect(field.getAttribute('aria-invalid')).toBe('true')

    await fireEvent.update(field, 'Acme')
    await fireEvent.click(screen.getByRole('button', { name: en.transfer.restore.confirm }))
    expect(emitted().confirm).toEqual([['Acme']])
  })

  it('keeps focus inside, and Escape cancels', async () => {
    const { emitted } = show()
    const dialog = screen.getByRole('alertdialog')
    const cancel = screen.getByRole('button', { name: en.transfer.restore.cancel })
    const confirm = screen.getByRole('button', { name: en.transfer.restore.confirm })

    confirm.focus()
    await fireEvent.keyDown(dialog, { key: 'Tab' })
    expect(dialog.contains(document.activeElement)).toBe(true)
    expect(document.activeElement).not.toBe(confirm)

    cancel.focus()
    await fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true })
    expect(dialog.contains(document.activeElement)).toBe(true)

    await fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(emitted().cancel).toHaveLength(1)
  })

  it('cannot be confirmed twice while the restore starts', () => {
    show(true)

    expect((screen.getByRole('button', { name: en.transfer.restore.restoring }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: en.transfer.restore.cancel }) as HTMLButtonElement).disabled).toBe(true)
  })
})
