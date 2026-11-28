import { describe, expect, it } from 'vitest'
import { definitionDefault } from '../../src/editor/schema/schema'

// 066 research BB-04: the defaults nested inside a prop come from the schema's shared
// definitions, as BlockDefaults.OfDefinition reads them on the server.

describe('definitionDefault', () => {
  it.each([
    ['border', 'width', 'none'],
    ['border', 'style', 'solid'],
    ['border', 'color', { token: 'color-border' }],
    ['borderBox', 'width', 'none'],
    ['borderBox', 'color', { token: 'color-border' }],
  ])('reads %s.%s from the schema', (definition, prop, expected) => {
    expect(definitionDefault(definition, prop)).toEqual(expected)
  })

  it.each([
    ['border', 'radius'],
    ['border', 'padding'],
    ['borderBox', 'padding'],
    ['no-such-definition', 'width'],
  ])('has no default for %s.%s', (definition, prop) => {
    expect(definitionDefault(definition, prop)).toBeUndefined()
  })
})
