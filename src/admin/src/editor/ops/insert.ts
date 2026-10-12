import { canDrop, type ContentNode, type DocumentKind } from '../schema/schema'
import type { Json } from '../schema/subsetValidator'
import { newNodeId } from './ids'
import { checkGrowth, checkSize } from './limits'
import { depthOf, locate, withChildren } from './tree'
import { done, refuse, type OpResult, isListSlot } from './types'

/**
 * Inserts a new block of `type` into `parentId` at `index`, holding only `initialProps`
 * (data-model §1.2: every other property takes its schema default) and `initialChildren`, such
 * as a list's card and empty state (016). Selects it.
 */
export function insert(
  root: ContentNode,
  parentId: string,
  index: number,
  type: string,
  kind: DocumentKind,
  initialProps: Record<string, Json>,
  initialChildren: readonly ContentNode[] = [],
): OpResult {
  return place(root, parentId, index, [{ id: newNodeId(), type, props: { ...initialProps }, children: [...initialChildren] }], kind)
}

/** Places ready-made nodes under a parent, after the nesting and limit checks. Shared by insert and paste. */
export function place(root: ContentNode, parentId: string, index: number, nodes: readonly ContentNode[], kind: DocumentKind): OpResult {
  const parent = locate(root, parentId)
  if (!parent) return refuse('not_found')
  if (nodes.some((node) => isListSlot(node.type))) return refuse('list_slot')
  if (nodes.some((node) => !canDrop(parent.node.type, node.type, kind))) return refuse('nesting_not_allowed')

  const growth = checkGrowth(root, nodes, depthOf(parent.path))
  if (growth) return growth

  const at = Math.max(0, Math.min(index, parent.node.children.length))
  const next = withChildren(root, parent.path, (children) => [...children.slice(0, at), ...nodes, ...children.slice(at)])
  return checkSize(next) ?? done(next, nodes[0]?.id ?? null)
}
