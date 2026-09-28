import type { ContentNode, DocumentKind } from '../schema/schema'
import { place } from './insert'
import { withNewIds } from './tree'
import type { OpResult } from './types'

/**
 * Pastes copied blocks into `parentId` at `index`, each with new ids throughout (FR-005,
 * FR-007), under the same nesting and limit rules as an insert.
 */
export function paste(root: ContentNode, parentId: string, index: number, nodes: readonly ContentNode[], kind: DocumentKind): OpResult {
  return place(root, parentId, index, nodes.map(withNewIds), kind)
}
