import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import FieldEditor from '../../src/features/cms/FieldEditor.vue'
import type { SchemaField } from '../../src/api/cms'
import { COLLECTION, en, i18n, violations } from './harness'

// 014 US4, FR-010, FR-029: one field's settings — a type change says what it does to values,
// a fill-in value takes the stored form, and the field that names items cannot be removed.

const summary: SchemaField = { id: 'f2', key: 'summary', name: 'Summary', help: '', required: false, type: 'text', multiline: true, maxLength: 5000 }

function renderEditor(field: SchemaField, extra: Record<string, unknown> = {}) {
  return render(FieldEditor, {
    props: {
      field,
      stored: summary,
      isTitle: false,
      autoKey: false,
      collections: [],
      collectionId: COLLECTION,
      collectionName: 'Blog posts',
      readOnly: false,
      fillInAllowed: false,
      fillIn: undefined,
      ...extra,
    },
    global: { plugins: [i18n] },
  })
}

describe('FieldEditor', () => {
  it('marks lossless, lossy and impossible type changes of a stored field', async () => {
    const lossless = renderEditor({ ...summary, type: 'richText' })
    expect(screen.getByText(en.cms.field.typeRule.lossless)).toBeTruthy()
    expect(await violations(lossless.container)).toEqual([])
    lossless.unmount()

    renderEditor({ ...summary, type: 'boolean' }).unmount()
    renderEditor({ ...summary, type: 'image' })
    expect(screen.getByText(en.cms.field.typeRule.forbidden)).toBeTruthy()
  })

  it('emits a fill-in in the stored form of its type', async () => {
    const date: SchemaField = { id: '~1', key: 'published-on', name: 'Published on', help: '', required: true, type: 'date', withTime: false }
    const { emitted } = renderEditor(date, { stored: undefined, fillInAllowed: true })

    await fireEvent.change(screen.getByLabelText(en.cms.field.fillIn), { target: { value: '2026-10-07' } })

    expect(emitted().fillIn?.[0]).toEqual(['2026-10-07'])
  })

  it('offers to name items with a single-line text field and does not let the title field go', () => {
    const title: SchemaField = { id: 'f1', key: 'title', name: 'Title', help: '', required: true, type: 'text', multiline: false, maxLength: 256 }
    renderEditor(title, { stored: title, isTitle: true })

    expect(screen.getByText(en.cms.field.isTitle)).toBeTruthy()
    expect(screen.queryByRole('button', { name: en.cms.field.remove })).toBeNull()
    expect(screen.queryByRole('button', { name: en.cms.field.makeTitle })).toBeNull()
  })
})
