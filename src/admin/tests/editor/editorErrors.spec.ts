import { describe, expect, it } from 'vitest'
import en from '../../src/i18n/en.json'
import { DOCUMENT_RULES, OP_REFUSALS, SERVER_CODES, refusalKey, ruleKey, serverKey } from '../../src/editor/editorErrors'

// 011 FR-042 (analysis C1): every refusal the editor can meet has a message in the lookup.

const has = (path: string): boolean =>
  path.split('.').reduce<unknown>((node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined), en) !== undefined

describe('editor error messages', () => {
  it.each(OP_REFUSALS)('has a message for the refused operation %s', (code) => {
    expect(has(refusalKey(code)), refusalKey(code)).toBe(true)
  })

  it.each(DOCUMENT_RULES)('has a message for the document rule %s', (rule) => {
    expect(has(ruleKey(rule)), ruleKey(rule)).toBe(true)
  })

  it.each(SERVER_CODES)('has a message for the server code %s', (code) => {
    expect(has(serverKey(code)), serverKey(code)).toBe(true)
  })

  it('has a fallback for anything unexpected', () => {
    expect(has(serverKey('something.new'))).toBe(true)
    expect(has(ruleKey('something_new'))).toBe(true)
  })
})
