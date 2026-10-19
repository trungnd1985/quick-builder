import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { duplicate, insert, move, paste, remove, setProp } from '../../src/editor/ops'
import { walkNodes } from '../../src/editor/ops/tree'
import { contentDocument } from '../../src/editor/schema/bindingDocumentRules'
import { validateDocument } from '../../src/editor/schema/documentValidator'
import { canDrop, type ContentNode } from '../../src/editor/schema/schema'
import { insertableTypes as blockTypes, newBlock } from '../../src/editor/blocks/registry'
import { createHistory } from '../../src/editor/state/history'
import { useEditorStore } from '../../src/editor/state/editorStore'

// 011 SC-003, FR-024, research ED-12: after any sequence of changes, undoing them all gives
// back the starting document, redoing them all gives the final one, every intermediate
// document is valid, and ids stay unique.

/** A small deterministic random generator, so a failing seed can be replayed. */
function random(seed: number) {
  let state = seed >>> 0
  return () => {
    state = (state * 1664525 + 1013904223) >>> 0
    return state / 2 ** 32
  }
}

const t = (key: string) => key

/** A new block's props and children, as the palette makes it; lists take a fixed collection. */
function newProps(type: string): [Record<string, never>, ContentNode[]] {
  const block = newBlock(type, t, { collectionIds: ['0192f5a0-0000-7000-8000-00000000c001'], menuIds: ['0192f5a0-0000-7000-8000-00000000d001'] })
  if ('refused' in block) throw new Error(block.refused)
  return [block.props as Record<string, never>, block.children]
}

function start(): ContentNode {
  return {
    id: 'root', type: 'root', props: {}, children: [
      { id: 's1', type: 'section', props: {}, children: [{ id: 'h1', type: 'heading', props: { text: 'Hello' }, children: [] }] },
    ],
  }
}

function setup() {
  const store = useEditorStore()
  store.root = start()
  store.savedRoot = store.root
  const history = createHistory(store, { now: () => clock })
  return { store, history }
}

let clock = 0

/** One random change that the store accepts, if the draw produces one. */
function randomChange(store: ReturnType<typeof useEditorStore>, next: () => number): void {
  const root = store.root!
  const nodes = [...walkNodes(root)]
  const pick = <T>(list: readonly T[]) => list[Math.floor(next() * list.length)]
  const node = pick(nodes)
  const holders = nodes.filter((n) => n.type === 'root' || n.type === 'section' || n.type === 'container')
  const choice = Math.floor(next() * 6)

  if (choice === 0) {
    const parent = pick(holders)
    const type = pick(blockTypes.filter((b) => canDrop(parent.type, b, 'page')))
    if (type) store.apply(insert(root, parent.id, Math.floor(next() * (parent.children.length + 1)), type, 'page', ...newProps(type)))
  } else if (choice === 1) {
    const parent = pick(holders)
    store.apply(move(root, node.id, parent.id, Math.floor(next() * (parent.children.length + 1)), 'page'))
  } else if (choice === 2 && node.id !== 'root') {
    store.apply(remove(root, node.id))
  } else if (choice === 3 && node.id !== 'root') {
    store.apply(duplicate(root, node.id))
  } else if (choice === 4 && node.id !== 'root') {
    const parent = pick(holders)
    store.apply(paste(root, parent.id, 0, [node], 'page'))
  } else {
    // Typing: several keystrokes into one heading, some merged into one undo step.
    const heading = nodes.find((n) => n.type === 'heading')
    if (!heading) return
    const text = String(heading.props.text ?? '')
    const typed = text + pick(['a', 'b', ' ', '.', 'c'])
    store.apply(setProp(root, heading.id, 'text', typed.slice(-300)), { coalesce: `${heading.id}:text` })
    clock += next() < 0.3 ? 1500 : 100
  }
}

describe('undo and redo over random change sequences (SC-003)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    clock = 0
  })

  it.each([1, 2, 3, 4, 5, 6, 7, 8])('seed %i: undo all gives the start, redo all gives the end', (seed) => {
    const { store, history } = setup()
    const next = random(seed)
    const initial = store.root!
    const steps = 50 + Math.floor(next() * 150)

    for (let i = 0; i < steps; i++) {
      randomChange(store, next)
      const ids = [...walkNodes(store.root!)].map((n) => n.id)
      expect(new Set(ids).size, `duplicate id after step ${i}`).toBe(ids.length)
      expect(validateDocument(contentDocument(store.root!), 'page'), `step ${i}`).toEqual({ valid: true })
    }
    const final = store.root!

    while (history.canUndo.value) history.undo()
    expect(store.root).toEqual(initial)

    while (history.canRedo.value) history.redo()
    expect(store.root).toEqual(final)
  })

  it('discards the redo steps when a new change is made after an undo', () => {
    const { store, history } = setup()
    store.apply(setProp(store.root!, 'h1', 'level', 3))
    history.undo()
    expect(history.canRedo.value).toBe(true)

    store.apply(setProp(store.root!, 'h1', 'level', 4))
    expect(history.canRedo.value).toBe(false)
  })

  it('groups typing into word- and pause-sized steps', () => {
    const { store, history } = setup()
    const type = (text: string, gap = 100) => {
      store.apply(setProp(store.root!, 'h1', 'text', text), { coalesce: 'h1:text' })
      clock += gap
    }

    type('Hello w')
    type('Hello wo')
    type('Hello wor')
    type('Hello word ')
    type('Hello word t')

    history.undo()
    expect(store.root!.children[0].children[0].props.text).toBe('Hello word ')
    history.undo()
    expect(store.root!.children[0].children[0].props.text).toBe('Hello')
  })

  it('restores the selection with the document', () => {
    const { store, history } = setup()
    store.select('h1')
    store.apply(remove(store.root!, 'h1'))
    expect(store.selection).not.toBe('h1')

    history.undo()
    expect(store.selection).toBe('h1')
  })

  it('keeps at least 100 steps (FR-025)', () => {
    const { store, history } = setup()
    const initial = store.root
    for (let i = 0; i < 150; i++) store.apply(setProp(store.root!, 'h1', 'level', (i % 6) + 1))

    let undone = 0
    while (history.canUndo.value) {
      history.undo()
      undone++
    }
    expect(undone).toBeGreaterThanOrEqual(100)
    if (undone === 150) expect(store.root).toEqual(initial)
  })

  it('forgets everything when cleared, as when another document opens', () => {
    const { store, history } = setup()
    store.apply(setProp(store.root!, 'h1', 'level', 3))
    history.clear()

    expect(history.canUndo.value).toBe(false)
  })
})
