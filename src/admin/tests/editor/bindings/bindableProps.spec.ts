import { describe, expect, it } from 'vitest'
import { kindOf } from '../../../src/editor/bindings/bindingRules'
import { nodeTypes, propSchema } from '../../../src/editor/schema/schema'
import { isObject } from '../../../src/editor/schema/subsetValidator'

// 016 research TB-05 (tasks T048), Principle VI: the properties a block can bind are defined
// once, by the `bind` keys its schema allows. The editor's rule table must name exactly those,
// so the panel never offers a binding the schema refuses, nor misses one it allows.
// BindingRulesSchemaTests checks the server's table the same way.

function bindKeys(type: string): string[] {
  let bind
  try {
    bind = propSchema(type, 'bind')
  } catch {
    return []
  }
  return isObject(bind) && isObject(bind.properties) ? Object.keys(bind.properties).sort() : []
}

describe('bindable properties', () => {
  it.each(nodeTypes)('%s binds exactly the properties its schema allows', (type) => {
    const candidates = new Set([...bindKeys(type), 'text', 'label', 'alt', 'media', 'link', 'content', 'level', 'url'])
    const bindable = [...candidates].filter((prop) => kindOf(type, prop) !== null).sort()

    expect(bindable).toEqual(bindKeys(type))
  })
})
