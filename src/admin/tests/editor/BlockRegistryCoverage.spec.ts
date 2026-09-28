import { describe, expect, it } from 'vitest'
import { registry } from '../../src/editor/blocks/registry'
import { blockTypes, pageSchema, propNames, propSchema, requiredProps } from '../../src/editor/schema/schema'
import en from '../../src/i18n/en.json'

// 011 research ED-08, Principle VI: each block type is defined once. The schema says what it
// is; its editor definition says how to edit and draw it. These must cover each other
// exactly, so a type added to the schema cannot ship without an editor, and an editor cannot
// invent a property the schema does not have.

const t = (key: string) => key.split('.').reduce<unknown>((n, p) => (n as Record<string, unknown>)?.[p], en) as string

describe('block registry against the schema', () => {
  it('has a definition for exactly the schema block types', () => {
    expect(Object.keys(registry).sort()).toEqual([...blockTypes].sort())
  })

  it.each(blockTypes)('covers every property of %s with a control, and invents none', (type) => {
    const definition = registry[type]
    const controlled = definition.groups.flatMap((g) => g.props.map((p) => p.prop)).sort()
    const schemaProps = propNames(type).filter((p) => p !== 'layerLabel').sort()

    expect(controlled).toEqual(schemaProps)
  })

  it.each(blockTypes)('starts a new %s with valid values for its required properties only', (type) => {
    const initial = registry[type].initialProps(t)

    expect(Object.keys(initial).sort()).toEqual(requiredProps(type).sort())
    for (const [prop, value] of Object.entries(initial)) {
      expect(pageSchema.checkAgainst(propSchema(type, prop), value as never), `${type}.${prop}`).toBeNull()
    }
  })

  it.each(blockTypes)('names %s in the lookup', (type) => {
    expect(typeof t(registry[type].labelKey)).toBe('string')
  })
})
