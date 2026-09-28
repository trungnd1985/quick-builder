import { canDrop, type ContentNode, type DocumentKind } from '../schema/schema'
import { MAX_NODE_DEPTH } from '../schema/limits'
import { contains, depthOf, heightOf, locate, withChildren } from './tree'
import { done, refuse, type OpResult } from './types'

/**
 * Moves a block, with everything inside it, to `index` among `parentId`'s children as they are
 * before the move — the gap a drop indicator shows. Moving a block to where it already is
 * returns the same root, so no history step is recorded.
 */
export function move(root: ContentNode, nodeId: string, parentId: string, index: number, kind: DocumentKind): OpResult {
  const moving = locate(root, nodeId)
  if (!moving) return refuse('not_found')
  if (moving.path.length === 0) return refuse('cannot_change_root')

  const target = locate(root, parentId)
  if (!target) return refuse('not_found')
  if (contains(moving.node, parentId)) return refuse('into_itself')
  if (!canDrop(target.node.type, moving.node.type, kind)) return refuse('nesting_not_allowed')

  const oldParent = moving.ancestors[moving.ancestors.length - 1]
  const oldIndex = moving.path[moving.path.length - 1]
  const sameParent = oldParent.id === parentId
  const at = Math.max(0, Math.min(sameParent && index > oldIndex ? index - 1 : index, target.node.children.length - (sameParent ? 1 : 0)))

  if (sameParent && at === oldIndex) return done(root, nodeId)
  if (!sameParent && depthOf(target.path) + heightOf(moving.node) > MAX_NODE_DEPTH) return refuse('too_deep')

  // Take it out first, then find the target again: removing may have shifted its path.
  const without = withChildren(root, moving.path.slice(0, -1), (children) => children.filter((_, i) => i !== oldIndex))
  const targetNow = locate(without, parentId)!
  const next = withChildren(without, targetNow.path, (children) => [...children.slice(0, at), moving.node, ...children.slice(at)])
  return done(next, nodeId)
}
