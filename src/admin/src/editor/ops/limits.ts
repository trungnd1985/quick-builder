// The contract's document limits, checked before a change is applied, so autosave never sends
// a document that is certain to be refused (spec Edge Cases, "very large pages").

import type { ContentNode } from '../schema/schema'
import { MAX_DOCUMENT_BYTES, MAX_NODE_DEPTH, MAX_NODES } from '../schema/limits'
import { utf8Size } from '../schema/documentValidator'
import { countNodes, heightOf } from './tree'
import { refuse, type OpResult } from './types'

/** Whether `added` nodes, `height` levels tall, fit under a parent at `parentDepth`. */
export function checkGrowth(root: ContentNode, added: readonly ContentNode[], parentDepth: number): OpResult | null {
  const adding = added.reduce((sum, node) => sum + countNodes(node), 0)
  if (countNodes(root) + adding > MAX_NODES) return refuse('too_many_nodes')

  const tallest = Math.max(0, ...added.map(heightOf))
  if (parentDepth + tallest > MAX_NODE_DEPTH) return refuse('too_deep')
  return null
}

/** Whether the document with `root` stays within the size limit. */
export const checkSize = (root: ContentNode): OpResult | null =>
  utf8Size({ schemaVersion: 1, root }) > MAX_DOCUMENT_BYTES ? refuse('too_large') : null
