// Reading and rebuilding an immutable document tree (011 research ED-10).
//
// A change rebuilds only the path from the root to the changed node; every other subtree is
// reused by reference. Canvas and layer components receive their node as a prop, so the ones
// whose node kept its identity are skipped by Vue — which is what keeps a 500-block page fast.

import type { ContentNode } from '../schema/schema'
import { newNodeId } from './ids'

export interface Located {
  node: ContentNode
  /** Child indexes from the root to the node; empty for the root. */
  path: number[]
  /** The nodes from the root down to the node's parent. */
  ancestors: ContentNode[]
}

export function locate(root: ContentNode, id: string): Located | null {
  if (root.id === id) return { node: root, path: [], ancestors: [] }
  for (let i = 0; i < root.children.length; i++) {
    const found = locate(root.children[i], id)
    if (found) return { node: found.node, path: [i, ...found.path], ancestors: [root, ...found.ancestors] }
  }
  return null
}

/** The node's parent and its index there; null for the root or an unknown id. */
export function parentOf(root: ContentNode, id: string): { parent: ContentNode; index: number } | null {
  const found = locate(root, id)
  if (!found || found.path.length === 0) return null
  return { parent: found.ancestors[found.ancestors.length - 1], index: found.path[found.path.length - 1] }
}

/** Rebuilds the path to `path`, replacing the node there with `leaf(node)`. */
export function rebuild(root: ContentNode, path: readonly number[], leaf: (node: ContentNode) => ContentNode): ContentNode {
  if (path.length === 0) return leaf(root)
  const [index, ...rest] = path
  const children = root.children.slice()
  children[index] = rebuild(root.children[index], rest, leaf)
  return { ...root, children }
}

/** Replaces the children of the node at `path`. */
export const withChildren = (root: ContentNode, path: readonly number[], children: (current: readonly ContentNode[]) => ContentNode[]): ContentNode =>
  rebuild(root, path, (node) => ({ ...node, children: children(node.children) }))

/** Every node in a subtree, the subtree's root included. */
export const countNodes = (node: ContentNode): number => 1 + node.children.reduce((sum, child) => sum + countNodes(child), 0)

/** The number of levels in a subtree: 1 for a leaf. */
export const heightOf = (node: ContentNode): number => 1 + Math.max(0, ...node.children.map(heightOf))

/** A node's depth with the root at 1, from its path. */
export const depthOf = (path: readonly number[]): number => path.length + 1

/** Whether `id` is `ancestor` itself or somewhere under it. */
export const contains = (ancestor: ContentNode, id: string): boolean => locate(ancestor, id) !== null

/** A deep copy of a subtree with a new id for every node (FR-005). */
export const withNewIds = (node: ContentNode): ContentNode => ({
  ...node,
  id: newNodeId(),
  children: node.children.map(withNewIds),
})

/** Every node of a subtree, depth first. */
export function* walkNodes(node: ContentNode): Generator<ContentNode> {
  yield node
  for (const child of node.children) yield* walkNodes(child)
}
