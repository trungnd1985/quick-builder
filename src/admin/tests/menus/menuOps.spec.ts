import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import type { MenuDocument, MenuEntry } from '../../src/api/menus'
import { canMove, indent, locate, move, moveDown, moveUp, outdent } from '../../src/features/menus/menuOps'
import { useMenuHistory } from '../../src/features/menus/useMenuHistory'

// 024 US3 (tasks T053, FR-012 to FR-015): moves keep children, never go past three levels or
// into the moved entry itself, change nothing when refused, and undo restores the document.

const e = (id: string, ...children: MenuEntry[]): MenuEntry => ({ id, label: id, target: { type: 'none' }, children })
const doc = (...entries: MenuEntry[]): MenuDocument => ({ schemaVersion: 1, entries })

/** The tree as nested ids, for readable expectations: `a(b,c)`. */
const shape = (document: MenuDocument): string => {
  const walk = (entries: MenuEntry[]): string => entries.map((x) => (x.children.length ? `${x.id}(${walk(x.children)})` : x.id)).join(',')
  return walk(document.entries)
}

// a
// b ─ b1 ─ b11
//   └ b2
// c
const sample = () => doc(e('a'), e('b', e('b1', e('b11')), e('b2')), e('c'))

describe('moving menu entries', () => {
  it('moves before, after and inside, carrying children', () => {
    const start = sample()
    expect(shape(move(start, 'c', 'a', 'before'))).toBe('c,a,b(b1(b11),b2)')
    expect(shape(move(start, 'a', 'b2', 'after'))).toBe('b(b1(b11),b2,a),c')
    expect(shape(move(start, 'b1', 'c', 'inside'))).toBe('a,b(b2),c(b1(b11))')
    // The input is never changed.
    expect(shape(start)).toBe('a,b(b1(b11),b2),c')
  })

  it('refuses a fourth level, counting the moved entry\'s own children', () => {
    const start = sample()
    // b1 has a child: inside b2 (level 2) would put b11 at level 4.
    expect(canMove(start, 'b1', 'b2', 'inside')).toBe(false)
    expect(move(start, 'b1', 'b2', 'inside')).toBe(start)
    // A leaf may go to the third level, not below it.
    expect(canMove(start, 'a', 'b1', 'inside')).toBe(true)
    expect(canMove(start, 'a', 'b11', 'inside')).toBe(false)
    expect(canMove(start, 'a', 'b11', 'after')).toBe(true)
  })

  it('refuses a drop into the entry\'s own subtree', () => {
    const start = sample()
    expect(canMove(start, 'b', 'b1', 'inside')).toBe(false)
    expect(canMove(start, 'b', 'b11', 'before')).toBe(false)
    expect(canMove(start, 'b', 'b', 'after')).toBe(false)
    expect(move(start, 'b', 'b2', 'after')).toBe(start)
  })

  it('refuses unknown entries', () => {
    const start = sample()
    expect(canMove(start, 'zz', 'a', 'after')).toBe(false)
    expect(canMove(start, 'a', 'zz', 'after')).toBe(false)
  })
})

describe('moving menu entries by key', () => {
  it('moves up and down among siblings, and stops at the edges', () => {
    const start = sample()
    expect(shape(moveUp(start, 'c'))).toBe('a,c,b(b1(b11),b2)')
    expect(shape(moveDown(start, 'a'))).toBe('b(b1(b11),b2),a,c')
    expect(moveUp(start, 'a')).toBe(start)
    expect(moveDown(start, 'c')).toBe(start)
    expect(moveUp(start, 'b1')).toBe(start)
    expect(shape(moveUp(start, 'b2'))).toBe('a,b(b2,b1(b11)),c')
  })

  it('indents into the previous sibling as its last child, and not past the third level', () => {
    const start = sample()
    expect(shape(indent(start, 'c'))).toBe('a,b(b1(b11),b2,c)')
    // b into a would put b11 at a fourth level.
    expect(indent(start, 'b')).toBe(start)
    // The first entry has no previous sibling.
    expect(indent(start, 'a')).toBe(start)
    // b2 into b1 would be level 3: allowed. b1 into nothing above it: refused.
    expect(shape(indent(start, 'b2'))).toBe('a,b(b1(b11,b2)),c')
    expect(indent(start, 'b1')).toBe(start)
  })

  it('outdents to just after the parent, and not from the top level', () => {
    const start = sample()
    expect(shape(outdent(start, 'b1'))).toBe('a,b(b2),b1(b11),c')
    expect(shape(outdent(start, 'b11'))).toBe('a,b(b1,b11,b2),c')
    expect(outdent(start, 'a')).toBe(start)
  })

  it('keeps ids and fields of moved entries', () => {
    const moved = indent(sample(), 'c')
    expect(locate(moved, 'c')).toMatchObject({ level: 2, parent: { id: 'b' }, entry: { id: 'c', label: 'c' } })
  })
})

describe('undo and redo of menu changes', () => {
  it('restores each earlier document and replays it, and a new change clears redo', () => {
    const document = ref<MenuDocument | null>(sample())
    const history = useMenuHistory(document)

    history.change(moveUp(document.value!, 'c'))
    history.change(outdent(document.value!, 'b1'))
    expect(shape(document.value!)).toBe('a,c,b(b2),b1(b11)')

    expect(history.undo()).toBe(true)
    expect(shape(document.value!)).toBe('a,c,b(b1(b11),b2)')
    expect(history.undo()).toBe(true)
    expect(shape(document.value!)).toBe('a,b(b1(b11),b2),c')
    expect(history.undo()).toBe(false)
    expect(history.canUndo.value).toBe(false)

    expect(history.redo()).toBe(true)
    expect(shape(document.value!)).toBe('a,c,b(b1(b11),b2)')
    expect(history.canRedo.value).toBe(true)

    history.change(moveDown(document.value!, 'a'))
    expect(history.canRedo.value).toBe(false)
    expect(history.redo()).toBe(false)
  })

  it('starts over after a save', () => {
    const document = ref<MenuDocument | null>(sample())
    const history = useMenuHistory(document)
    history.change(moveUp(document.value!, 'c'))
    history.reset()
    expect(history.canUndo.value).toBe(false)
    expect(history.undo()).toBe(false)
  })
})
