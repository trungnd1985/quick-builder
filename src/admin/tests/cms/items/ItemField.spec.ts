import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import ItemField from '../../../src/features/cms/items/ItemField.vue'
import type { SchemaField } from '../../../src/api/cms'
import { en, i18n, postSchema, SITE } from './itemHarness'

// 015 FR-009, FR-010: one input per field type, each emitting the contract's value; no value is
// undefined, never an empty string.

const field = (id: string): SchemaField => postSchema().fields.find((f) => f.id === id)!

function renderField(fieldId: string, value: unknown = undefined, problem: { fieldId: string; problem: string; limit?: number } | null = null) {
  return render(ItemField, {
    props: { field: field(fieldId), value, siteId: SITE, itemId: null, problem },
    global: { plugins: [i18n] },
  })
}

const lastValue = (emitted: Record<string, unknown[][]>) => emitted['update:value']!.at(-1)![0]

describe('ItemField', () => {
  it('text: emits the string, and nothing for an empty box; shows the characters left', async () => {
    const { emitted } = renderField('f1', 'Hi')
    expect(screen.getByText('18 characters left')).toBeTruthy()

    await fireEvent.update(screen.getByLabelText(/Title/), 'Hello')
    expect(lastValue(emitted())).toBe('Hello')
    await fireEvent.update(screen.getByLabelText(/Title/), '')
    expect(lastValue(emitted())).toBeUndefined()
  })

  it('multi-line text is a text area', () => {
    renderField('f2')
    expect(screen.getByLabelText('Summary').tagName).toBe('TEXTAREA')
  })

  it('number: emits a number, and what was typed when it is not one, for the checks to report', async () => {
    const { emitted } = renderField('f3')
    await fireEvent.update(screen.getByLabelText(/Reading time/), '7')
    expect(lastValue(emitted())).toBe(7)
    await fireEvent.update(screen.getByLabelText(/Reading time/), 'seven')
    expect(lastValue(emitted())).toBe('seven')
  })

  it('boolean: a switch that toggles', async () => {
    const { emitted } = renderField('f4', false)
    const toggle = screen.getByRole('switch')
    expect(toggle.getAttribute('aria-checked')).toBe('false')
    await fireEvent.click(toggle)
    expect(lastValue(emitted())).toBe(true)
  })

  it('date: emits the calendar date as typed', async () => {
    const { emitted } = renderField('f5')
    await fireEvent.change(screen.getByLabelText('Published on'), { target: { value: '2026-10-08' } })
    expect(lastValue(emitted())).toBe('2026-10-08')
  })

  it('date and time: entered in local time, stored in UTC', async () => {
    const withTime: SchemaField = { ...field('f5'), withTime: true }
    const { emitted } = render(ItemField, { props: { field: withTime, value: undefined, siteId: SITE, itemId: null, problem: null }, global: { plugins: [i18n] } })
    await fireEvent.change(screen.getByLabelText('Published on'), { target: { value: '2026-10-08T09:30' } })
    expect(lastValue(emitted())).toBe(new Date('2026-10-08T09:30').toISOString().replace(/\.\d{3}Z$/, 'Z'))
  })

  it('option: the options in order, by id, or none', async () => {
    const { emitted } = renderField('f6')
    const select = screen.getByRole('combobox', { name: 'Category' }) as HTMLSelectElement
    expect(Array.from(select.options).map((o) => o.textContent?.trim())).toEqual([en.cms.items.inputs.noOption, 'News', 'Guides'])
    await fireEvent.update(select, 'o2')
    expect(lastValue(emitted())).toBe('o2')
    await fireEvent.update(select, '')
    expect(lastValue(emitted())).toBeUndefined()
  })

  it('a problem is shown, announced through aria-describedby, and marks the input invalid', () => {
    renderField('f1', 'x', { fieldId: 'f1', problem: 'missing_required' })
    const input = screen.getByLabelText(/Title/)
    expect(input.getAttribute('aria-invalid')).toBe('true')
    expect(input.getAttribute('aria-describedby')).toContain('item-field-f1-error')
    expect(screen.getByText(en.cms.items.problems.missing_required)).toBeTruthy()
  })

  it('rich text: shows headings and quotes, and keeps them in the value it emits (035 US1)', async () => {
    const body: SchemaField = { id: 'f20', key: 'body', name: 'Body', help: '', required: false, type: 'richText' }
    const value = {
      type: 'doc',
      content: [
        { type: 'heading', attrs: { level: 3 }, content: [{ type: 'text', text: 'Title' }] },
        { type: 'blockquote', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Quoted' }] }] },
      ],
    }
    const { container, emitted } = render(ItemField, {
      props: { field: body, value, siteId: SITE, itemId: null, problem: null },
      global: { plugins: [i18n] },
    })

    expect(container.querySelector('[role="textbox"] h3')?.textContent).toBe('Title')
    expect(container.querySelector('[role="textbox"] blockquote p')?.textContent).toBe('Quoted')

    await fireEvent.update(await screen.findByRole('combobox', { name: en.editor.richText.blockStyle }), 'heading-4')

    expect(lastValue(emitted())).toEqual({ ...value, content: [{ ...value.content[0], attrs: { level: 4 } }, value.content[1]] })
  })

  it('rich text: says when an image needs alternative text (035 FR-014)', async () => {
    const body: SchemaField = { id: 'f20', key: 'body', name: 'Body', help: '', required: false, type: 'richText' }
    render(ItemField, {
      props: { field: body, value: { type: 'doc', content: [] }, siteId: SITE, itemId: null, problem: { fieldId: 'f20', problem: 'image_alt_missing', path: '/content/0' } },
      global: { plugins: [i18n] },
    })

    expect(screen.getByText(en.cms.items.problems.image_alt_missing)).toBeTruthy()
  })
})
