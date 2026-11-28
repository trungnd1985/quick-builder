import { beforeEach, describe, expect, it } from 'vitest'
import { clearClipboard, copyBlocks, readBlocks } from '../../src/editor/state/clipboard'
import { duplicate, paste } from '../../src/editor/ops'
import type { ContentNode } from '../../src/editor/schema/schema'

// 011 research ED-18, FR-007: copied blocks paste within the same site and user only, survive
// a reload, and are re-ided on paste.

const heading: ContentNode = { id: 'h1', type: 'heading', props: { text: 'Hello' }, children: [] }
const root: ContentNode = { id: 'root', type: 'root', props: {}, children: [{ id: 's', type: 'section', props: {}, children: [] }] }

describe('clipboard', () => {
  beforeEach(() => clearClipboard())

  it('gives the blocks back to the same user on the same site', () => {
    copyBlocks('user-1', 'site-a', [heading])

    expect(readBlocks('user-1', 'site-a')).toEqual([heading])
  })

  it('finds nothing for another site or another user', () => {
    copyBlocks('user-1', 'site-a', [heading])

    expect(readBlocks('user-1', 'site-b')).toBeNull()
    expect(readBlocks('user-2', 'site-a')).toBeNull()
  })

  it('survives a reload, through storage', () => {
    copyBlocks('user-1', 'site-a', [heading])

    const stored = JSON.parse(localStorage.getItem('qb.editor.clipboard')!)
    expect(stored).toMatchObject({ userId: 'user-1', siteId: 'site-a' })
  })

  it('is emptied on clear', () => {
    copyBlocks('user-1', 'site-a', [heading])
    clearClipboard()

    expect(readBlocks('user-1', 'site-a')).toBeNull()
  })

  it('pastes with new ids', () => {
    copyBlocks('user-1', 'site-a', [heading])
    const result = paste(root, 's', 0, readBlocks('user-1', 'site-a')!, 'page')

    expect(result.ok).toBe(true)
    if (result.ok) expect(result.root.children[0].children[0].id).not.toBe('h1')
  })

  // 066 FR-011 (analysis C2): a border travels with its block, exactly, under a new id.
  const bordered: ContentNode = {
    id: 'framed',
    type: 'text',
    props: { text: 'Framed', border: { width: 'thin', style: 'dashed', color: { token: 'color-brand' }, radius: { token: 'radius-md' }, padding: { token: 'space-sm' } } },
    children: [],
  }

  it('keeps a border exactly when a bordered block is copied and pasted', () => {
    copyBlocks('user-1', 'site-a', [bordered])
    const result = paste(root, 's', 0, readBlocks('user-1', 'site-a')!, 'page')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    const pasted = result.root.children[0].children[0]
    expect(pasted.id).not.toBe('framed')
    expect(pasted.props.border).toEqual(bordered.props.border)
  })

  it('keeps a border exactly when a bordered block is duplicated', () => {
    const withBlock: ContentNode = { ...root, children: [{ id: 's', type: 'section', props: {}, children: [bordered] }] }
    const result = duplicate(withBlock, 'framed')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    const [original, copy] = result.root.children[0].children
    expect(copy.id).not.toBe(original.id)
    expect(copy.props.border).toEqual(bordered.props.border)
  })
})
