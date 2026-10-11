// The binding scopes at a node of the document (016 FR-028, research TB-14): the page's item on
// a template page, then each enclosing Collection list whose card the node sits in. Nearest
// first, as the binding control offers them.

import { isObject } from '../schema/subsetValidator'
import type { ContentNode } from '../schema/schema'
import type { SchemaInfo } from './bindingRules'

export interface ScopeOption {
  /** `page`, or the list's node id. */
  name: string
  /** The collection whose items the scope holds; null when it cannot be told (a broken source). */
  collectionId: string | null
  /** The list's layer label, for the picker; null for the page. */
  label: string | null
}

/** The enclosing list nodes whose card holds `nodeId`, outermost first. */
export function enclosingLists(root: ContentNode, nodeId: string): ContentNode[] {
  const path: ContentNode[] = []
  const walk = (node: ContentNode, lists: ContentNode[]): boolean => {
    if (node.id === nodeId) {
      path.push(...lists)
      return true
    }
    for (const child of node.children) {
      // Only a card opens its list's scope; the empty state has no item.
      const opens = node.type === 'collection-list' && child.type === 'list-card'
      if (walk(child, opens ? [...lists, node] : lists)) return true
    }
    return false
  }
  walk(root, [])
  return path
}

/** The collection a list lists: its own, or the target of the multi-reference field it reads. */
export function listCollection(
  list: ContentNode,
  outer: readonly ScopeOption[],
  schemas: Readonly<Record<string, SchemaInfo>>,
): string | null {
  const source = list.props.source
  if (!isObject(source)) return null
  if (typeof source.collectionId === 'string') return source.collectionId
  if (typeof source.field !== 'string') return null
  const scope = typeof source.scope === 'string'
    ? outer.find((s) => s.name === source.scope)
    : outer[0]
  const field = scope?.collectionId ? schemas[scope.collectionId]?.fields[source.field] : undefined
  return field?.type === 'multiReference' ? field.target : null
}

/**
 * The scopes a binding at `nodeId` can read, nearest first: each enclosing list's card, then the
 * page's item when the page is a template of `pageCollectionId`.
 */
export function scopesAt(
  root: ContentNode,
  nodeId: string,
  pageCollectionId: string | null,
  schemas: Readonly<Record<string, SchemaInfo>>,
): ScopeOption[] {
  const outermostFirst: ScopeOption[] = pageCollectionId ? [{ name: 'page', collectionId: pageCollectionId, label: null }] : []
  for (const list of enclosingLists(root, nodeId)) {
    const nearestFirst = [...outermostFirst].reverse()
    outermostFirst.push({
      name: list.id,
      collectionId: listCollection(list, nearestFirst, schemas),
      label: typeof list.props.layerLabel === 'string' ? list.props.layerLabel : null,
    })
  }
  return outermostFirst.reverse()
}
