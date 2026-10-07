import { describe, expect, it } from 'vitest'
import { SchemaSubsetValidator, type JsonObject } from '../../src/editor/schema/subsetValidator'
import pageSchemaJson from '@contracts/page-schema/v1.schema.json'

// 014 research CS-04: a value checked against one named definition, the same cases as the
// server's SchemaSubsetValidatorDefinitionTests.

const schema = SchemaSubsetValidator.load({
  type: 'object',
  $defs: {
    word: { type: 'string', pattern: '^[a-z]+$' },
    pair: {
      type: 'object',
      required: ['a'],
      additionalProperties: false,
      properties: { a: { $ref: '#/$defs/word' }, b: { type: 'integer' } },
    },
  },
})

describe('validateDefinition', () => {
  it('checks a value against the named definition only', () => {
    expect(schema.validateDefinition('word', 'abc')).toBeNull()
    expect(schema.validateDefinition('word', 'ABC')?.keyword).toBe('pattern')
  })

  it('resolves references inside the definition', () => {
    const failure = schema.validateDefinition('pair', { a: 'X' })
    expect([failure?.location, failure?.keyword]).toEqual(['/a', 'pattern'])
  })

  it('reports locations relative to the value', () => {
    expect(schema.validateDefinition('pair', { a: 'x', b: 1.5 })?.location).toBe('/b')
  })

  it('throws for an unknown definition', () => {
    expect(() => schema.validateDefinition('nope', 1)).toThrow()
  })

  it("accepts rich text and refuses an unknown node with the page schema's rtDoc", () => {
    const page = SchemaSubsetValidator.load(pageSchemaJson as unknown as JsonObject)
    expect(page.validateDefinition('rtDoc', { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Hello' }] }] })).toBeNull()
    expect(page.validateDefinition('rtDoc', { type: 'doc', content: [{ type: 'table' }] })?.location).toBe('/content/0')
  })
})
