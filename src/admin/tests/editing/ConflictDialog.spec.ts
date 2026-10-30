import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import axe from 'axe-core'
import { createMemoryHistory, createRouter } from 'vue-router'
import en from '../../src/i18n/en.json'
import ConflictDialog from '../../src/editing/ConflictDialog.vue'
import { i18n } from '../editor/harness'

// 023 FR-006 to FR-008, research CE-10, contracts/admin-editing.md §3.

const ben = { changedBy: { id: 'u-ben', displayName: 'Ben Hale' }, changedAtUtc: '2026-10-25T10:42:00Z', changeKind: 'member' as const }

function show(props: Record<string, unknown> = {}) {
  return render(ConflictDialog, {
    props: { kind: 'page', conflict: ben, reloading: false, ...props },
    global: { plugins: [i18n] },
  })
}

describe('ConflictDialog', () => {
  it('names who changed it and says nothing was overwritten, with no way to overwrite', async () => {
    const { container } = show()

    const dialog = screen.getByRole('alertdialog')
    expect(dialog.textContent).toContain('Ben Hale')
    expect(dialog.textContent).toContain(en.editing.conflict.nothingOverwritten)
    const actions = screen.getAllByRole('button').map((b) => b.textContent?.trim())
    expect(actions).toEqual([en.editing.conflict.reload, en.editing.conflict.keepViewing])
    expect(actions.join(' ')).not.toMatch(/overwrite|save anyway/i)
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: en.editing.conflict.reload })))
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('names a schema change as a system change', () => {
    show({ kind: 'item', conflict: { changedBy: null, changedAtUtc: '2026-10-25T10:42:00Z', changeKind: 'schema_change' } })

    const dialog = screen.getByRole('alertdialog')
    expect(dialog.textContent).toContain(en.editing.conflict.byFieldChange.split('{')[0].trim())
    expect(dialog.textContent).not.toContain('Ben Hale')
  })

  it('asks before reloading, and keeps viewing on request', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    const { emitted } = show()

    await fireEvent.click(screen.getByRole('button', { name: en.editing.conflict.reload }))
    expect(confirm).toHaveBeenCalledWith(en.editing.conflict.confirmReload)
    expect(emitted().reload).toBeUndefined()

    await fireEvent.click(screen.getByRole('button', { name: en.editing.conflict.reload }))
    expect(emitted().reload).toHaveLength(1)

    await fireEvent.click(screen.getByRole('button', { name: en.editing.conflict.keepViewing }))
    expect(emitted().keepViewing).toHaveLength(1)
    confirm.mockRestore()
  })

  it('lists the fields the other change altered, for an item', () => {
    show({ kind: 'item', changedFields: ['Title', 'Summary'] })

    const list = screen.getByRole('list', { name: en.editing.conflict.changedFields })
    expect(Array.from(list.querySelectorAll('li')).map((li) => li.textContent)).toEqual(['Title', 'Summary'])
  })

  it('offers to save the version as a copy only with the create permission (023 FR-020a)', async () => {
    const { emitted, rerender } = show({ canSaveCopy: true })

    await fireEvent.click(screen.getByRole('button', { name: en.editing.copy.save }))
    expect(emitted().saveCopy).toHaveLength(1)
    expect(screen.queryByText(en.editing.copy.notAllowed)).toBeNull()

    await rerender({ canSaveCopy: false })
    expect(screen.queryByRole('button', { name: en.editing.copy.save })).toBeNull()
    expect(screen.getByText(en.editing.copy.notAllowed)).toBeTruthy()
  })

  it('links to the copy once saved, and names the fields it could not keep', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }] })
    render(ConflictDialog, {
      props: { kind: 'item', conflict: ben, reloading: false, canSaveCopy: true, copy: { to: '/items/copy', label: en.editing.copy.open }, droppedFields: ['Reading time'] },
      global: { plugins: [i18n, router] },
    })

    expect(screen.getByRole('link', { name: en.editing.copy.open }).getAttribute('href')).toBe('/items/copy')
    expect(screen.getByRole('status').textContent).toContain('Reading time')
    expect(screen.queryByRole('button', { name: en.editing.copy.save })).toBeNull()
  })
})
