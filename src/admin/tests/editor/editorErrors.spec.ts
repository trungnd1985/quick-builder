import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
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

  // 016 analysis E1: a rule the validators return but the lookup does not know falls back to
  // "does not follow the page schema", which hides the reason. Read the codes from the sources.
  it('knows every rule code the editor’s and the server’s validators can return', () => {
    // Vitest runs from src/admin.
    const read = (path: string) => readFileSync(resolve(process.cwd(), path), 'utf8')
    const codes = new Set<string>()
    for (const file of ['src/editor/schema/documentValidator.ts', 'src/editor/schema/bindingDocumentRules.ts']) {
      for (const match of read(file).matchAll(/(?:invalid|fail)\('([a-z_]+)'/g)) codes.add(match[1])
    }
    const server = read('../Modules/Pages/QuickBuilder.Pages/Infrastructure/Schema/BindingDocumentRules.cs')
    for (const match of server.matchAll(/"([a-z]+(?:_[a-z]+)+)"/g)) codes.add(match[1])

    expect(codes.size).toBeGreaterThan(15)
    expect([...codes].filter((code) => !DOCUMENT_RULES.includes(code))).toEqual([])
  })

  it('has a fallback for anything unexpected', () => {
    expect(has(serverKey('something.new'))).toBe(true)
    expect(has(ruleKey('something_new'))).toBe(true)
  })
})
