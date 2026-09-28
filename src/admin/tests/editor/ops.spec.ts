import { describe, expect, it } from 'vitest'
import { duplicate, insert, move, paste, remove, setLabel, setProp, type OpResult } from '../../src/editor/ops'
import { countNodes, locate } from '../../src/editor/ops/tree'
import type { ContentNode } from '../../src/editor/schema/schema'
import { MAX_NODE_DEPTH, MAX_NODES } from '../../src/editor/schema/limits'
import { validateDocument } from '../../src/editor/schema/documentValidator'

// 011 research ED-10: every change is a pure function returning a new root. Unchanged
// subtrees keep their identity (which is what lets Vue skip them, SC-005), a refused change
// returns a reason and changes nothing, and every result is a valid document.

const n = (id: string, type: string, props: Record<string, unknown> = {}, children: ContentNode[] = []): ContentNode =>
  ({ id, type, props, children }) as ContentNode

function sample(): ContentNode {
  return n('root', 'root', {}, [
    n('s1', 'section', {}, [n('h1', 'heading', { text: 'One' }), n('c1', 'container', {}, [n('t1', 'text', { text: 'a' })])]),
    n('s2', 'section', {}, [n('b1', 'button', { label: 'Go' })]),
  ])
}

const ok = (result: OpResult) => {
  if (!result.ok) throw new Error(`Refused: ${JSON.stringify(result.refusal)}`)
  return result
}

const valid = (root: ContentNode, kind: 'page' | 'component' = 'page') =>
  expect(validateDocument({ schemaVersion: 1, root }, kind)).toEqual({ valid: true })

const ids = (root: ContentNode): string[] => [root.id, ...root.children.flatMap(ids)]

describe('insert', () => {
  it('adds a block with a new id, selects it, and shares untouched subtrees', () => {
    const root = sample()
    const result = ok(insert(root, 's1', 1, 'text', 'page', { text: '' }))

    const s1 = locate(result.root, 's1')!.node
    expect(s1.children[1]).toMatchObject({ type: 'text', props: { text: '' }, children: [] })
    expect(result.selection).toBe(s1.children[1].id)
    expect(s1.children[1].id).toMatch(/^[A-Za-z0-9_-]{22}$/)
    expect(result.root).not.toBe(root)
    expect(result.root.children[1]).toBe(root.children[1])
    expect(s1.children[0]).toBe(root.children[0].children[0])
    expect(root.children[0].children).toHaveLength(2)
    valid(result.root)
  })

  it('refuses a place the block may not go, and changes nothing', () => {
    const root = sample()

    expect(insert(root, 's1', 0, 'section', 'page', {})).toMatchObject({ ok: false, refusal: { code: 'nesting_not_allowed' } })
    expect(insert(root, 'root', 0, 'heading', 'page', { text: '' })).toMatchObject({ ok: false, refusal: { code: 'nesting_not_allowed' } })
    expect(insert(root, 'h1', 0, 'text', 'page', { text: '' })).toMatchObject({ ok: false, refusal: { code: 'nesting_not_allowed' } })
    expect(insert(root, 'root', 0, 'component-ref', 'component', { componentId: '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f' }))
      .toMatchObject({ ok: false, refusal: { code: 'nesting_not_allowed' } })
    expect(insert(root, 'nope', 0, 'text', 'page', { text: '' })).toMatchObject({ ok: false, refusal: { code: 'not_found' } })
  })

  it('refuses to pass the node and depth limits', () => {
    let deep = n('leaf', 'container')
    for (let i = 0; i < MAX_NODE_DEPTH - 3; i++) deep = n(`c${i}`, 'container', {}, [deep])
    const tall = n('root', 'root', {}, [n('s', 'section', {}, [deep])])
    expect(insert(tall, 'leaf', 0, 'text', 'page', { text: '' })).toMatchObject({ ok: false, refusal: { code: 'too_deep' } })

    const wide = n('root', 'root', {}, [n('s', 'section', {}, Array.from({ length: MAX_NODES - 2 }, (_, i) => n(`x${i}`, 'spacer')))])
    expect(countNodes(wide)).toBe(MAX_NODES)
    expect(insert(wide, 's', 0, 'spacer', 'page', {})).toMatchObject({ ok: false, refusal: { code: 'too_many_nodes' } })
  })
})

describe('move', () => {
  it('moves a block with its subtree to another parent', () => {
    const root = sample()
    const result = ok(move(root, 'c1', 's2', 0, 'page'))

    expect(locate(result.root, 's2')!.node.children.map((c) => c.id)).toEqual(['c1', 'b1'])
    expect(locate(result.root, 'c1')!.node).toBe(root.children[0].children[1])
    expect(result.selection).toBe('c1')
    valid(result.root)
  })

  it('counts the drop index among the siblings as they were', () => {
    const root = n('root', 'root', {}, [n('s', 'section', {}, [n('a', 'spacer'), n('b', 'spacer'), n('c', 'spacer')])])

    // Dropping "a" at the gap before "c" (index 2) leaves it between b and c.
    expect(locate(ok(move(root, 'a', 's', 2, 'page')).root, 's')!.node.children.map((c) => c.id)).toEqual(['b', 'a', 'c'])
    expect(locate(ok(move(root, 'c', 's', 0, 'page')).root, 's')!.node.children.map((c) => c.id)).toEqual(['c', 'a', 'b'])
  })

  it('changes nothing when the block does not move', () => {
    const root = sample()
    expect(ok(move(root, 'h1', 's1', 0, 'page')).root).toBe(root)
    expect(ok(move(root, 'h1', 's1', 1, 'page')).root).toBe(root)
  })

  it('refuses to move a block into itself or where it may not go', () => {
    const root = sample()
    expect(move(root, 'c1', 'c1', 0, 'page')).toMatchObject({ ok: false, refusal: { code: 'into_itself' } })
    expect(move(root, 's1', 'c1', 0, 'page')).toMatchObject({ ok: false, refusal: { code: 'into_itself' } })
    expect(move(root, 'h1', 'root', 0, 'page')).toMatchObject({ ok: false, refusal: { code: 'nesting_not_allowed' } })
    expect(move(root, 'root', 's1', 0, 'page')).toMatchObject({ ok: false, refusal: { code: 'cannot_change_root' } })
  })
})

describe('remove', () => {
  it('removes a block with everything inside it, and selects a neighbour', () => {
    const root = sample()
    const result = ok(remove(root, 'h1'))

    expect(locate(result.root, 'h1')).toBeNull()
    expect(result.selection).toBe('c1')
    expect(ok(remove(root, 'c1')).selection).toBe('h1')
    expect(ok(remove(root, 'b1')).selection).toBe('s2')
    expect(ok(remove(root, 's1')).root.children.map((c) => c.id)).toEqual(['s2'])
    expect(remove(root, 'root')).toMatchObject({ ok: false, refusal: { code: 'cannot_change_root' } })
  })
})

describe('duplicate and paste', () => {
  it('duplicates a block after itself, with new ids throughout', () => {
    const root = sample()
    const result = ok(duplicate(root, 'c1'))

    const s1 = locate(result.root, 's1')!.node
    expect(s1.children.map((c) => c.type)).toEqual(['heading', 'container', 'container'])
    expect(s1.children[2].id).not.toBe('c1')
    expect(s1.children[2].children[0].id).not.toBe('t1')
    expect(s1.children[2].children[0].props).toEqual({ text: 'a' })
    expect(result.selection).toBe(s1.children[2].id)
    expect(new Set(ids(result.root)).size).toBe(ids(result.root).length)
    valid(result.root)
  })

  it('pastes copied blocks with new ids, under the same rules as insert', () => {
    const root = sample()
    const copied = [root.children[0].children[1]]
    const result = ok(paste(root, 's2', 1, copied, 'page'))

    const s2 = locate(result.root, 's2')!.node
    expect(s2.children[1].type).toBe('container')
    expect(s2.children[1].id).not.toBe('c1')
    expect(new Set(ids(result.root)).size).toBe(ids(result.root).length)
    expect(paste(root, 'root', 0, copied, 'page')).toMatchObject({ ok: false, refusal: { code: 'nesting_not_allowed' } })
  })
})

describe('setProp and setLabel', () => {
  it('sets a valid value and shares everything else', () => {
    const root = sample()
    const result = ok(setProp(root, 'h1', 'level', 3))

    expect(locate(result.root, 'h1')!.node.props).toEqual({ text: 'One', level: 3 })
    expect(result.root.children[1]).toBe(root.children[1])
    valid(result.root)
  })

  it('refuses a value outside the definition, with the reason', () => {
    const root = sample()

    expect(setProp(root, 'h1', 'level', 7)).toMatchObject({ ok: false, refusal: { code: 'invalid_value', keyword: 'maximum' } })
    expect(setProp(root, 'b1', 'link', { type: 'url', href: 'javascript:alert(1)' })).toMatchObject({ ok: false, refusal: { code: 'invalid_value' } })
    expect(setProp(root, 'h1', 'text', 'x'.repeat(301))).toMatchObject({ ok: false, refusal: { code: 'invalid_value', keyword: 'maxLength' } })
    expect(setProp(root, 'h1', 'nope', 1)).toMatchObject({ ok: false, refusal: { code: 'invalid_value' } })
  })

  it('removes an optional value but never a required one', () => {
    const root = ok(setProp(sample(), 'h1', 'level', 3)).root

    expect(locate(ok(setProp(root, 'h1', 'level', undefined)).root, 'h1')!.node.props).toEqual({ text: 'One' })
    expect(setProp(root, 'h1', 'text', undefined)).toMatchObject({ ok: false, refusal: { code: 'required' } })
  })

  it('refuses rich text over the block limit', () => {
    const root = n('root', 'root', {}, [n('s', 'section', {}, [n('r', 'rich-text', { content: { type: 'doc', content: [] } })])])
    const long = { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'a'.repeat(15000) }, { type: 'text', text: 'b'.repeat(5001) }] }] }

    expect(setProp(root, 'r', 'content', long)).toMatchObject({ ok: false, refusal: { code: 'rich_text_too_long' } })
  })

  it('labels a block for the layer view, and clears the label with an empty one', () => {
    const labelled = ok(setLabel(sample(), 's1', 'Hero')).root
    expect(locate(labelled, 's1')!.node.props.layerLabel).toBe('Hero')
    expect(locate(ok(setLabel(labelled, 's1', '')).root, 's1')!.node.props).not.toHaveProperty('layerLabel')
  })
})
