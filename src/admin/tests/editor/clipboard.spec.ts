import { beforeEach, describe, expect, it } from 'vitest'
import { clearClipboard, copyBlocks, readBlocks } from '../../src/editor/state/clipboard'
import { paste } from '../../src/editor/ops'
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
})
