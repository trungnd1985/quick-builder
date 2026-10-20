import { describe, expect, it } from 'vitest'
import { join } from 'node:path'
import { entryProblems, validateMenu, cleanAnchor, type MenuDocument } from '../../src/editor/schema/menuValidator'
import { contractsDir, expectedFailures, jsonFiles, readJson } from './contractFiles'

// 024 research NV-03: the admin's menu validator gives exactly the server's answer for every
// golden file of the menu contract — same rule, same pointer. MenuSchemaGoldenTests runs the
// same files on the server side.

const menuExamples = join(contractsDir, 'menu', 'examples')

// Files whose defect JSON.parse erases, or that are not JSON at all, are the server's alone.
const SERVER_ONLY = new Set(['malformed_json--truncated.json', 'duplicate_property--label.json'])

describe('menu validator against the golden files', () => {
  it.each(jsonFiles(join(menuExamples, 'valid')))('accepts valid/%s', (file) => {
    expect(validateMenu(readJson(join(menuExamples, 'valid', file)))).toEqual({ valid: true })
  })

  const expected = expectedFailures(join(menuExamples, 'invalid'))
  const cases = Object.entries(expected).filter(([file]) => !SERVER_ONLY.has(file))

  it.each(cases)('refuses invalid/%s with its rule and pointer', (file, { rule, pointer }) => {
    const result = validateMenu(readJson(join(menuExamples, 'invalid', file)))

    expect(result.valid).toBe(false)
    if (!result.valid) expect({ rule: result.rule, pointer: result.pointer }).toEqual({ rule, pointer })
    expect(file.startsWith(rule!)).toBe(true)
  })

  it('lists every invalid example in the manifest', () => {
    for (const file of jsonFiles(join(menuExamples, 'invalid'))) expect(expected).toHaveProperty([file])
  })

  it('refuses the 301st entry where it is', () => {
    const entries = Array.from({ length: 301 }, (_, i) => ({ id: `e${i}`, label: 'L', target: { type: 'none' }, children: [] }))

    expect(validateMenu({ schemaVersion: 1, entries })).toMatchObject({ valid: false, rule: 'too_many_entries', pointer: '/entries/300' })
  })
})

describe('entry problems before saving (FR-011)', () => {
  it('marks every invalid entry, not only the first', () => {
    const menu: MenuDocument = {
      schemaVersion: 1,
      entries: [
        { id: 'a', target: { type: 'url', href: 'https://example.com' }, children: [] },
        { id: 'b', label: 'Fine', target: { type: 'none' }, children: [] },
        { id: 'c', label: 'Bad', target: { type: 'url', href: 'javascript:alert(1)' }, children: [] },
        { id: 'd', label: 'Group', target: { type: 'none' }, newTab: true, children: [] },
      ],
    }

    expect(Object.fromEntries(entryProblems(menu))).toEqual({ a: 'label_required', c: 'entry_invalid', d: 'new_tab_not_allowed' })
  })

  it('marks entries past the third level', () => {
    const leaf = { id: 'l4', target: { type: 'page' as const, pageId: '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10' }, children: [] }
    const menu: MenuDocument = {
      schemaVersion: 1,
      entries: [{ id: 'l1', label: 'A', target: { type: 'none' }, children: [{ id: 'l2', label: 'B', target: { type: 'none' }, children: [{ id: 'l3', label: 'C', target: { type: 'none' }, children: [leaf] }] }] }],
    }

    expect(entryProblems(menu).get('l4')).toBe('too_deep')
    expect(entryProblems(menu).has('l3')).toBe(false)
  })
})

describe('anchors (FR-007a)', () => {
  it('drops a leading # and surrounding spaces', () => {
    expect(cleanAnchor('  #form ')).toBe('form')
    expect(cleanAnchor('pricing')).toBe('pricing')
  })
})
