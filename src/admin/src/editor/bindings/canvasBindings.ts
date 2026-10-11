// Bound values on the canvas (016 research TB-14, FR-020).
//
// A block on the canvas is drawn from a copy of its node whose bound properties hold what the
// binding resolves to — the sample item's values on a template page, a listed item's inside a
// card — so every block component shows bound values without knowing about bindings. Broken
// bindings are reported for the panel and the layer view; a block whose binding hides it is
// drawn dimmed rather than removed, so it can still be selected.

import type { InjectionKey } from 'vue'
import { isObject, type Json } from '../schema/subsetValidator'
import type { ContentNode } from '../schema/schema'
import { kindOf } from './bindingRules'
import { resolveBinding, type Bound, type OpenScope, type ResolveData } from './resolve'

/** The list cards open around a node on the canvas, outermost first (US4, US6). */
export const CANVAS_SCOPES: InjectionKey<() => readonly OpenScope[]> = Symbol('canvas-scopes')

export interface DisplayedNode {
  /** The node with each bound property holding its resolved value, or its own when empty. */
  node: ContentNode
  /** Whether a binding with "hide" chosen has nothing to show. */
  hidden: boolean
  /** The node's broken bindings: property and reason. */
  broken: { prop: string; reason: string }[]
}

/** Whether `prop` of `node` is bound. */
export const isBound = (node: ContentNode, prop: string): boolean => {
  const bind = node.props.bind
  return isObject(bind) && prop in bind
}

/** The value a bound property takes in the node's props, by the kind of property it is. */
function propValue(kind: string, bound: Extract<Bound, { state: 'value' }>): Json | undefined {
  switch (kind) {
    case 'text': return bound.text
    case 'richText': return bound.richText
    case 'image': return bound.media ? { mediaId: bound.media } : undefined
    case 'link': return bound.link ? { type: 'url', href: bound.link.href } : undefined
    default: return undefined
  }
}

/** The node as the canvas draws it, with its bindings resolved against `data` and `scopes`. */
export function displayNode(node: ContentNode, data: ResolveData | null, scopes: readonly OpenScope[]): DisplayedNode {
  const bind = node.props.bind
  if (!isObject(bind) || !data) return { node, hidden: false, broken: [] }

  const props: Record<string, Json> = { ...node.props }
  const broken: DisplayedNode['broken'] = []
  let hidden = false

  for (const prop of Object.keys(bind)) {
    const kind = kindOf(node.type, prop)
    const bound = resolveBinding(node, prop, data, scopes)
    if (kind && bound.state === 'value') {
      const value = propValue(kind, bound)
      if (value !== undefined) props[prop] = value
    } else if (bound.state === 'hide' || bound.state === 'fallback') {
      if (bound.state === 'hide') hidden = true
      if (bound.broken) broken.push({ prop, reason: bound.broken })
    }
  }

  return { node: { ...node, props }, hidden, broken }
}
