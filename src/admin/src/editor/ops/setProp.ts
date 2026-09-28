import { pageSchema, propNames, propSchema, requiredProps, type ContentNode } from '../schema/schema'
import { MAX_RICH_TEXT_CHARS } from '../schema/limits'
import { richTextChars } from '../schema/documentValidator'
import type { Json } from '../schema/subsetValidator'
import { checkSize } from './limits'
import { locate, rebuild } from './tree'
import { done, refuse, type OpResult } from './types'

/**
 * Sets one property of a block to `value`, or removes it when `value` is undefined so the
 * schema default applies. The value is checked against the property's schema first, and a
 * refused value leaves the block as it was (FR-021).
 */
export function setProp(root: ContentNode, nodeId: string, prop: string, value: Json | undefined): OpResult {
  const found = locate(root, nodeId)
  if (!found) return refuse('not_found')
  if (found.path.length === 0) return refuse('cannot_change_root')
  if (!propNames(found.node.type).includes(prop)) return refuse('invalid_value', { keyword: 'additionalProperties' })

  if (value === undefined) {
    if (requiredProps(found.node.type).includes(prop)) return refuse('required')
    if (!(prop in found.node.props)) return done(root, nodeId)
    const rest = Object.fromEntries(Object.entries(found.node.props).filter(([name]) => name !== prop))
    return done(rebuild(root, found.path, (node) => ({ ...node, props: rest })), nodeId)
  }

  const failure = pageSchema.checkAgainst(propSchema(found.node.type, prop), value)
  if (failure) return refuse('invalid_value', { keyword: failure.keyword, pointer: failure.location })
  if (found.node.type === 'rich-text' && prop === 'content' && richTextChars(value) > MAX_RICH_TEXT_CHARS) {
    return refuse('rich_text_too_long')
  }

  const next = rebuild(root, found.path, (node) => ({ ...node, props: { ...node.props, [prop]: value } }))
  return checkSize(next) ?? done(next, nodeId)
}

/** Names a block in the layer view; an empty or blank label removes the name (FR-023). */
export const setLabel = (root: ContentNode, nodeId: string, label: string): OpResult =>
  setProp(root, nodeId, 'layerLabel', label.trim() === '' ? undefined : label.trim())
