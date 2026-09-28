import type { ContentNode } from '../schema/schema'
import { locate, withChildren } from './tree'
import { done, refuse, type OpResult } from './types'

/**
 * Removes a block and everything inside it. Selects the next sibling, else the previous one,
 * else the parent — so repeated Delete walks through a list rather than losing the place.
 */
export function remove(root: ContentNode, nodeId: string): OpResult {
  const found = locate(root, nodeId)
  if (!found) return refuse('not_found')
  if (found.path.length === 0) return refuse('cannot_change_root')

  const parent = found.ancestors[found.ancestors.length - 1]
  const index = found.path[found.path.length - 1]
  const siblings = parent.children
  const selection = siblings[index + 1]?.id ?? siblings[index - 1]?.id ?? (parent.type === 'root' ? null : parent.id)

  const next = withChildren(root, found.path.slice(0, -1), (children) => children.filter((_, i) => i !== index))
  return done(next, selection)
}
