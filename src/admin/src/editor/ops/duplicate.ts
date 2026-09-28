import type { ContentNode, DocumentKind } from '../schema/schema'
import { place } from './insert'
import { locate, withNewIds } from './tree'
import { refuse, type OpResult } from './types'

/** Copies a block, with new ids throughout, directly after itself, and selects the copy. */
export function duplicate(root: ContentNode, nodeId: string, kind: DocumentKind = 'page'): OpResult {
  const found = locate(root, nodeId)
  if (!found) return refuse('not_found')
  if (found.path.length === 0) return refuse('cannot_change_root')

  const parent = found.ancestors[found.ancestors.length - 1]
  const index = found.path[found.path.length - 1]
  return place(root, parent.id, index + 1, [withNewIds(found.node)], kind)
}
