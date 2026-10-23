import { describe, expect, it } from 'vitest'
import { join } from 'node:path'
import { readFileSync } from 'node:fs'
import { validateDocument } from '../../src/editor/schema/documentValidator'
import { SchemaSubsetValidator } from '../../src/editor/schema/subsetValidator'
import { contractsDir, expectedFailures, jsonFiles, pageExamples, readJson } from './contractFiles'

// 011 research ED-08, SC-009: the editor's validator gives exactly the server's answer for
// every golden file of the page schema — same rule, same pointer. The files are the shared
// oracle; PageDocumentValidatorTests runs them on the server side.

// Files that are not JSON, or whose defect JSON.parse erases, are the server's alone to
// judge: the editor only ever validates objects (see documentValidator.ts).
const SERVER_ONLY = new Set(['malformed_json--truncated.json', 'duplicate_property--props.json'])

describe('document validator against the golden files', () => {
  it.each(jsonFiles(join(pageExamples, 'valid')))('accepts valid/%s', (file) => {
    const result = validateDocument(readJson(join(pageExamples, 'valid', file)), 'page')

    expect(result).toEqual({ valid: true })
  })

  const expected = expectedFailures(join(pageExamples, 'invalid'))
  const cases = Object.entries(expected).filter(([file]) => !SERVER_ONLY.has(file))

  it.each(cases)('refuses invalid/%s with its rule and pointer', (file, { rule, pointer }) => {
    const result = validateDocument(readJson(join(pageExamples, 'invalid', file)), 'page')

    expect(result.valid).toBe(false)
    if (!result.valid) expect({ rule: result.rule, pointer: result.pointer }).toEqual({ rule, pointer })
    expect(file.startsWith(rule!)).toBe(true)
  })

  it('lists every invalid example in the manifest', () => {
    for (const file of jsonFiles(join(pageExamples, 'invalid'))) expect(expected).toHaveProperty([file])
  })

  it('accepts the component example as a component document', () => {
    expect(validateDocument(readJson(join(pageExamples, 'valid', 'component-page.json')), 'component')).toEqual({ valid: true })
  })

  it('accepts a Navigation block in a component document (024)', () => {
    expect(validateDocument(readJson(join(pageExamples, 'valid', 'component-with-navigation.json')), 'component')).toEqual({ valid: true })
  })

  it('accepts a Form block in a component document (018)', () => {
    expect(validateDocument(readJson(join(pageExamples, 'valid', 'form-in-component.json')), 'component')).toEqual({ valid: true })
  })

  it('accepts an incomplete form, which only publishing refuses (018 rule 28)', () => {
    expect(validateDocument(readJson(join(pageExamples, 'valid', 'form-minimal-draft.json')), 'page')).toEqual({ valid: true })
  })

  it('refuses a component reference inside a component document', () => {
    const result = validateDocument(readJson(join(pageExamples, 'valid', 'header-and-footer.json')), 'component')

    expect(result).toMatchObject({ valid: false, rule: 'component_ref_not_allowed', pointer: '/root/children/0' })
  })

  it('refuses a document nested deeper than the limit before the schema', () => {
    let node: Record<string, unknown> = { id: 'n33', type: 'section', props: {}, children: [] }
    for (let depth = 32; depth >= 2; depth--) node = { id: `n${depth}`, type: 'section', props: {}, children: [node] }
    const document = { schemaVersion: 1, root: { id: 'root', type: 'root', props: {}, children: [node] } }

    expect(validateDocument(document, 'page')).toMatchObject({ valid: false, rule: 'too_deep' })
  })
})

// 016 research TB-06: the binding contract's golden documents, run through the same validator.
// PageSchemaBindingTests runs them on the server side.
const bindingExamples = join(contractsDir, 'binding-expression', 'examples')

describe('document validator against the binding golden files', () => {
  it.each(jsonFiles(join(bindingExamples, 'valid')))('accepts valid/%s', (file) => {
    expect(validateDocument(readJson(join(bindingExamples, 'valid', file)), 'page')).toEqual({ valid: true })
  })

  const expected = expectedFailures(join(bindingExamples, 'invalid'))

  it.each(Object.entries(expected))('refuses invalid/%s with its rule and pointer', (file, { rule, pointer }) => {
    const result = validateDocument(readJson(join(bindingExamples, 'invalid', file)), 'page')

    expect(result.valid).toBe(false)
    if (!result.valid) expect({ rule: result.rule, pointer: result.pointer }).toEqual({ rule, pointer })
    expect(file.startsWith(rule!)).toBe(true)
  })

  it('lists every invalid binding example in the manifest', () => {
    for (const file of jsonFiles(join(bindingExamples, 'invalid'))) expect(expected).toHaveProperty([file])
  })

  it('refuses a binding to the page item in a component (rule 16)', () => {
    const result = validateDocument(readJson(join(bindingExamples, 'valid', 'bindings-every-prop.json')), 'component')

    expect(result).toMatchObject({ valid: false, rule: 'page_scope_not_allowed', pointer: '/root/children/0/children/0/props/bind/text/value' })
  })

  it('refuses a list of a collection the site does not have, last (rule 14)', () => {
    const document = readJson(join(bindingExamples, 'valid', 'collection-list-nested.json'))
    const known = new Set(['0192f5a0-0000-7000-8000-00000000c002'])

    expect(validateDocument(document, 'page', { collectionIds: known })).toMatchObject({
      valid: false,
      rule: 'unknown_collection',
      pointer: '/root/children/0/children/0/props/source',
    })
    expect(validateDocument(document, 'page', { collectionIds: new Set([...known, '0192f5a0-0000-7000-8000-00000000c001']) })).toEqual({ valid: true })
  })
})

describe('subset validator', () => {
  it('refuses a pattern that is not portable, as the server does', () => {
    expect(() => SchemaSubsetValidator.load({ pattern: '(?<=a)b' })).toThrow(/pattern/)
    expect(() => SchemaSubsetValidator.load({ properties: { a: { anyOf: [] } } })).toThrow(/anyOf/)
  })

  it('treats \\w as ASCII, like .NET in ECMAScript mode', () => {
    const validator = SchemaSubsetValidator.load({ pattern: '^\\w+$' })

    expect(validator.validate('abc_1')).toBeNull()
    expect(validator.validate('đồng')).toMatchObject({ keyword: 'pattern' })
  })

  it('never validates a default, even beside a $ref', () => {
    const validator = SchemaSubsetValidator.load({
      $defs: { n: { type: 'integer', default: 'x' } },
      properties: { a: { $ref: '#/$defs/n', default: 3 } },
    })

    expect(validator.validate({ a: 1 })).toBeNull()
    expect(validator.validate({ a: 'x' })).toMatchObject({ location: '/a', keyword: 'type' })
  })

  it('loads the shipped schema file itself', () => {
    const text = readFileSync(join(pageExamples, '..', 'v1.schema.json'), 'utf8')

    expect(() => SchemaSubsetValidator.load(JSON.parse(text))).not.toThrow()
  })
})
