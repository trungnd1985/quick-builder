import { describe, expect, it } from 'vitest'
import { changedFields } from '../../src/editing/itemDiff'

// 023 FR-007, research CE-10: which fields another change altered, by field id.

const fields = [
  { id: 'f1', name: 'Title' },
  { id: 'f2', name: 'Summary' },
  { id: 'f3', name: 'Body' },
  { id: 'f4', name: 'Related' },
]

describe('changedFields', () => {
  it('lists added, removed and changed values, in the order of the fields', () => {
    const loaded = { f1: 'Old title', f2: 'Same', f4: ['a'] }
    const latest = { f1: 'New title', f2: 'Same', f3: 'Added' }

    expect(changedFields(loaded, latest, fields)).toEqual([
      { id: 'f1', label: 'Title' },
      { id: 'f3', label: 'Body' },
      { id: 'f4', label: 'Related' },
    ])
  })

  it('compares structured values by content, whatever the key order', () => {
    const richText = { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Hi' }] }] }
    const reordered = { content: [{ content: [{ text: 'Hi', type: 'text' }], type: 'paragraph' }], type: 'doc' }

    expect(changedFields({ f3: richText, f4: ['a', 'b'] }, { f3: reordered, f4: ['a', 'b'] }, fields)).toEqual([])
    expect(changedFields({ f4: ['a', 'b'] }, { f4: ['b', 'a'] }, fields)).toEqual([{ id: 'f4', label: 'Related' }])
  })

  it('names a field that no longer exists by its id', () => {
    expect(changedFields({ f9: 1 }, {}, fields)).toEqual([{ id: 'f9', label: 'f9' }])
  })
})
