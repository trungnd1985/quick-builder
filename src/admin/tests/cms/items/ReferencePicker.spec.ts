import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import ReferencePicker from '../../../src/features/cms/items/ReferencePicker.vue'
import type { SchemaField } from '../../../src/api/cms'
import { en, i18n, ITEM, PEOPLE, postSchema, SITE, stubFetch, summary, violations, type Call } from './itemHarness'

// 015 US4: a reference chosen in a searchable listbox, replaced or cleared; unpublished items marked.

const author = (): SchemaField => postSchema().fields.find((f) => f.id === 'f7')!
const ANN = 'aaaaaaaa-0000-0000-0000-000000000001'
const BOB = 'aaaaaaaa-0000-0000-0000-000000000002'

function server(call: Call) {
  if (call.url.endsWith('/items/lookup')) {
    return { status: 200, body: { items: [{ id: ANN, collectionId: PEOPLE, name: 'Ann', slug: 'ann', status: 'draft' }], missing: [] } }
  }
  const q = new URL(call.url, 'http://x').searchParams.get('q')
  const people = [summary({ id: ANN, name: 'Ann', slug: 'ann' }), summary({ id: BOB, name: 'Bob', slug: 'bob', status: 'published' })]
  const items = q ? people.filter((p) => p.name!.toLowerCase().includes(q)) : people
  return { status: 200, body: { items, total: items.length, page: 1, pageSize: 25 } }
}

function renderPicker(value: unknown) {
  const calls = stubFetch(server)
  const result = render(ReferencePicker, {
    props: { field: author(), value, id: 'author', describedBy: '', invalid: false, siteId: SITE, itemId: ITEM },
    global: { plugins: [i18n] },
  })
  return { ...result, calls }
}

describe('ReferencePicker', () => {
  it('shows the chosen item by name, marked when not published', async () => {
    renderPicker(ANN)
    await waitFor(() => expect(screen.getByText('Ann')).toBeTruthy())
    expect(screen.getByText(en.cms.items.status.notPublished)).toBeTruthy()
  })

  it('searches the target collection and chooses with the keyboard, replacing the earlier choice', async () => {
    const { emitted, calls, baseElement } = renderPicker(ANN)

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.inputs.pickItem }))
    const dialog = await screen.findByRole('dialog')
    const list = await within(dialog).findByRole('listbox')
    expect(await violations(baseElement)).toEqual([])
    expect(calls.some((c) => c.url.includes(`/collections/${PEOPLE}/items`))).toBe(true)

    // Ann is already chosen, so the active option starts on Bob.
    expect(within(list).getByRole('option', { name: /Ann/ }).getAttribute('aria-disabled')).toBe('true')
    await fireEvent.keyDown(list, { key: 'Enter' })
    expect(emitted()['update:value']![0]).toEqual([BOB])
  })

  it('narrows the list as the member types', async () => {
    renderPicker(undefined)
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.inputs.pickItem }))
    const dialog = await screen.findByRole('dialog')
    await fireEvent.update(within(dialog).getByRole('searchbox'), 'bo')
    await waitFor(() => expect(within(dialog).queryByRole('option', { name: /Ann/ })).toBeNull(), { timeout: 2000 })
    expect(within(dialog).getByRole('option', { name: /Bob/ })).toBeTruthy()
  })

  it('clears the choice', async () => {
    const { emitted } = renderPicker(ANN)
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.form.clear }))
    expect(emitted()['update:value']![0]).toEqual([undefined])
  })
})
