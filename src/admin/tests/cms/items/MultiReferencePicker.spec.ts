import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import MultiReferencePicker from '../../../src/features/cms/items/MultiReferencePicker.vue'
import type { SchemaField } from '../../../src/api/cms'
import { COLLECTION, en, i18n, ITEM, SITE, stubFetch, summary, violations, type Call } from './itemHarness'

// 015 US4: an ordered list of items, reordered and removed with buttons or Alt+arrows, each move
// announced; the item itself and items already chosen cannot be picked; at most 100.

const related: SchemaField = { id: 'f8', key: 'related', name: 'Related', help: '', required: false, type: 'multiReference', target: COLLECTION }
const ids = ['aaaaaaaa-0000-0000-0000-000000000001', 'aaaaaaaa-0000-0000-0000-000000000002', 'aaaaaaaa-0000-0000-0000-000000000003']
const names = ['One', 'Two', 'Three']

function server(call: Call) {
  if (call.url.endsWith('/items/lookup')) {
    const asked = (call.body?.ids as string[]) ?? []
    return { status: 200, body: { items: asked.map((id) => ({ id, collectionId: COLLECTION, name: names[ids.indexOf(id)] ?? id, slug: id, status: 'published' })), missing: [] } }
  }
  const items = [summary({ id: ITEM, name: 'Self', slug: 'self' }), ...ids.map((id, i) => summary({ id, name: names[i]!, slug: `s${i}` }))]
  return { status: 200, body: { items, total: items.length, page: 1, pageSize: 25 } }
}

function renderPicker(value: unknown) {
  stubFetch(server)
  return render(MultiReferencePicker, {
    props: { field: related, value, id: 'related', describedBy: '', invalid: false, siteId: SITE, itemId: ITEM, label: 'Related' },
    global: { plugins: [i18n] },
  })
}

const last = (emitted: Record<string, unknown[][]>) => emitted['update:value']!.at(-1)![0]

describe('MultiReferencePicker', () => {
  it('moves items with the buttons and announces the move', async () => {
    const { emitted, container } = renderPicker([ids[0], ids[1], ids[2]])
    await waitFor(() => expect(screen.getByText('Three')).toBeTruthy())
    expect(await violations(container)).toEqual([])

    await fireEvent.click(screen.getByRole('button', { name: 'Move Three up' }))
    expect(last(emitted())).toEqual([ids[0], ids[2], ids[1]])
    expect(container.querySelector('[aria-live]')?.textContent).toContain('Three moved to position 2.')
  })

  it('moves the focused row with Alt and the arrow keys', async () => {
    const { emitted, container } = renderPicker([ids[0], ids[1]])
    await waitFor(() => expect(screen.getByText('Two')).toBeTruthy())

    const rows = container.querySelectorAll<HTMLElement>('.multi-reference__row')
    await fireEvent.keyDown(rows[0]!, { key: 'ArrowDown', altKey: true })
    expect(last(emitted())).toEqual([ids[1], ids[0]])
  })

  it('removes an item, and an empty list is no value', async () => {
    const { emitted } = renderPicker([ids[0]])
    await waitFor(() => expect(screen.getByText('One')).toBeTruthy())
    await fireEvent.click(screen.getByRole('button', { name: 'Remove One' }))
    expect(last(emitted())).toBeUndefined()
  })

  it('adds from the picker, where the item itself and chosen items are disabled', async () => {
    const { emitted } = renderPicker([ids[0]])
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.inputs.pickItems }))
    const list = await within(await screen.findByRole('dialog')).findByRole('listbox')

    expect(within(list).getByRole('option', { name: /Self/ }).getAttribute('aria-disabled')).toBe('true')
    expect(within(list).getByRole('option', { name: /One/ }).getAttribute('aria-disabled')).toBe('true')
    await fireEvent.click(within(list).getByRole('option', { name: /Two/ }))
    expect(last(emitted())).toEqual([ids[0], ids[1]])
  })

  it('stops at 100 items', () => {
    renderPicker(Array.from({ length: 100 }, (_, i) => `aaaaaaaa-0000-0000-0000-${String(i).padStart(12, '0')}`))
    expect((screen.getByRole('button', { name: en.cms.items.inputs.pickItems }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('At most 100 items.')).toBeTruthy()
  })
})
