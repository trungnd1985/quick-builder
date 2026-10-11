import { describe, expect, it } from 'vitest'
import { newBlock, registry } from '../../src/editor/blocks/registry'
import { blockTypes, nodeTypes, pageSchema, propNames, propSchema, requiredProps } from '../../src/editor/schema/schema'
import en from '../../src/i18n/en.json'

// 011 research ED-08, Principle VI: each block type is defined once. The schema says what it
// is; its editor definition says how to edit and draw it. These must cover each other
// exactly, so a type added to the schema cannot ship without an editor, and an editor cannot
// invent a property the schema does not have.

const t = (key: string) => key.split('.').reduce<unknown>((n, p) => (n as Record<string, unknown>)?.[p], en) as string

/** A collection of the site, for the blocks that need one (016). */
const SITE_COLLECTION = '0192f5a0-0000-7000-8000-00000000c001'

describe('block registry against the schema', () => {
  it('has a definition for exactly the schema node types', () => {
    expect(Object.keys(registry).sort()).toEqual([...nodeTypes].sort())
  })

  it.each(blockTypes)('covers every property of %s with a control, and invents none', (type) => {
    const definition = registry[type]
    const controlled = definition.groups.flatMap((g) => g.props.map((p) => p.prop)).sort()
    // `bind` is edited through the binding control beside each bindable property (016 TB-14).
    const schemaProps = propNames(type).filter((p) => p !== 'layerLabel' && p !== 'bind').sort()

    expect(controlled).toEqual(schemaProps)
  })

  it.each(blockTypes)('starts a new %s with valid values for its required properties only', (type) => {
    const initial = registry[type].initialProps(t, { collectionIds: [SITE_COLLECTION] })!

    expect(Object.keys(initial).sort()).toEqual(requiredProps(type).sort())
    for (const [prop, value] of Object.entries(initial)) {
      expect(pageSchema.checkAgainst(propSchema(type, prop), value as never), `${type}.${prop}`).toBeNull()
    }
  })

  it('refuses a collection list on a site without collections', () => {
    expect(newBlock('collection-list', t, { collectionIds: [] })).toEqual({ refused: 'no_collections' })
  })

  it('starts a collection list with the first collection, its card and an empty state saying so', () => {
    const block = newBlock('collection-list', t, { collectionIds: [SITE_COLLECTION, '0192f5a0-0000-7000-8000-00000000c009'] })

    expect('props' in block && block.props.source).toEqual({ collectionId: SITE_COLLECTION })
    expect('children' in block && block.children.map((c) => c.type)).toEqual(['list-card', 'list-empty'])
    expect('children' in block && block.children[1].children[0].props.text).toBe(t('editor.list.emptyText'))
  })

  it.each(blockTypes)('names %s in the lookup', (type) => {
    expect(typeof t(registry[type].labelKey)).toBe('string')
  })
})
