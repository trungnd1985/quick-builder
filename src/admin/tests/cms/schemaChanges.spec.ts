import { describe, expect, it } from 'vitest'
import type { FieldType, SchemaDocument, SchemaField } from '../../src/api/cms'
import { needsPreview, typeChangeRule, valueChanges } from '../../src/features/cms/schemaChanges'

// 014 research CS-06/07: the builder's port of the server's diff and change rules, with the
// cases of FieldChangeClassificationTests. Free changes save without a preview (FR-030).

const text = (id: string, extra: Partial<SchemaField> = {}): SchemaField =>
  ({ id, key: `k${id}`, name: 'n', help: '', required: false, type: 'text', multiline: false, maxLength: 256, ...extra })
const doc = (...fields: SchemaField[]): SchemaDocument => ({ schemaVersion: 1, fields })

describe('type change rules', () => {
  const allowed: [FieldType, FieldType, string][] = [
    ['text', 'richText', 'lossless'], ['richText', 'text', 'lossy'], ['number', 'text', 'lossless'], ['text', 'number', 'lossy'],
    ['date', 'text', 'lossless'], ['text', 'date', 'lossy'], ['option', 'text', 'lossless'], ['text', 'option', 'lossy'],
    ['boolean', 'text', 'lossless'], ['image', 'file', 'lossless'], ['file', 'image', 'lossy'],
    ['reference', 'multiReference', 'lossless'], ['multiReference', 'reference', 'lossy'],
  ]

  it.each(allowed)('%s to %s is %s', (from, to, rule) => expect(typeChangeRule(from, to)).toBe(rule))

  it('forbids every other pair', () => {
    expect(typeChangeRule('image', 'boolean')).toBe('forbidden')
    expect(typeChangeRule('number', 'date')).toBe('forbidden')
  })
})

describe('whether a save needs the preview', () => {
  const stored = doc(text('f1', { required: true }), text('f2'), { id: 'f3', key: 'k3', name: 'n', help: '', required: false, type: 'option', options: [{ id: 'o1', label: 'A' }, { id: 'o2', label: 'B' }] })

  it('saves renames, reorders, relabels and loosened settings straight away', () => {
    const proposal = doc(
      { ...stored.fields[2]!, name: 'Kind', options: [{ id: 'o2', label: 'Bee' }, { id: 'o1', label: 'A' }] },
      text('f2', { key: 'summary', maxLength: 500, multiline: true }),
      text('f1', { required: true, name: 'Headline' }),
    )
    expect(needsPreview(stored, proposal)).toBe(false)
  })

  it('previews a removed field, a removed option, a tightened setting, required turned on and a fill-in', () => {
    expect(valueChanges(stored, doc(stored.fields[0]!, stored.fields[2]!)).map((c) => c.rule)).toEqual(['lossy'])
    expect(needsPreview(stored, doc(stored.fields[0]!, stored.fields[1]!, { ...stored.fields[2]!, options: [{ id: 'o1', label: 'A' }] }))).toBe(true)
    expect(valueChanges(stored, doc(stored.fields[0]!, text('f2', { maxLength: 10 }), stored.fields[2]!))[0]?.rule).toBe('reported')
    expect(valueChanges(stored, doc(stored.fields[0]!, text('f2', { required: true }), stored.fields[2]!))[0]?.kind).toBe('requiredChanged')
    expect(needsPreview(stored, doc(...stored.fields, { id: '~1', key: 'flag', name: 'Flag', help: '', required: false, type: 'boolean' }), new Set(['~1']))).toBe(true)
  })

  it('previews narrowing the formats of a rich text field, and saves widening them straight away (035 FR-025)', () => {
    const rich = (formats?: { headings?: boolean; images?: boolean; tables?: boolean }) =>
      ({ id: 'f9', key: 'body', name: 'Body', help: '', required: false, type: 'richText' as const, ...(formats ? { formats } : {}) })
    const before = doc(stored.fields[0]!, rich())

    expect(valueChanges(before, doc(stored.fields[0]!, rich({ tables: false })))[0]?.rule).toBe('reported')
    expect(valueChanges(doc(stored.fields[0]!, rich({ headings: false })), doc(stored.fields[0]!, rich({ headings: false, images: false })))[0]?.rule).toBe('reported')
    expect(needsPreview(doc(stored.fields[0]!, rich({ headings: false })), doc(stored.fields[0]!, rich()))).toBe(false)
    expect(needsPreview(before, doc(stored.fields[0]!, rich({ headings: true, images: true, tables: true })))).toBe(false)
  })

  it('saves an added optional field straight away, and previews an added required one', () => {
    expect(needsPreview(stored, doc(...stored.fields, text('~1')))).toBe(false)
    expect(needsPreview(stored, doc(...stored.fields, text('~1', { required: true })))).toBe(true)
  })
})
