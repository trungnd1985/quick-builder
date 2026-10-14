// Which of a node's bindings are broken, and why, from the document's scopes and the schemas
// alone (016 FR-018): the scope gone, the field or reference step gone, or a type that no
// longer fits. Works whether or not an item is at hand, so the panel and the layer view can say
// so on any page.

import type { ContentNode } from '../schema/schema'
import { isObject, type JsonObject } from '../schema/subsetValidator'
import { fits, fitsBuiltIn, kindOf, type BrokenReason, type PropKind, type SchemaInfo } from './bindingRules'
import type { ScopeOption } from './scopes'
import { bindingOf } from './resolve'

/** Why one reader is broken, or null. `scopes` are the node's, nearest first. */
export function readerBroken(
  reader: JsonObject,
  kind: PropKind,
  scopes: readonly ScopeOption[],
  schemas: Readonly<Record<string, SchemaInfo>>,
): BrokenReason | null {
  const scope = typeof reader.scope === 'string' ? scopes.find((s) => s.name === reader.scope) : scopes[0]
  if (!scope) return 'scope_missing'
  let schema = scope.collectionId ? schemas[scope.collectionId] : undefined
  // A schema not loaded yet is not a reason to call a binding broken.
  if (!schema) return null
  if (typeof reader.via === 'string') {
    const reference = schema.fields[reader.via]
    if (!reference) return 'field_missing'
    if (reference.type !== 'reference') return 'via_not_reference'
    schema = reference.target ? schemas[reference.target] : undefined
    if (!schema) return null
  }
  if (typeof reader.item === 'string') return fitsBuiltIn(kind, reader.item) ? null : 'type_mismatch'
  const field = typeof reader.field === 'string' ? schema.fields[reader.field] : undefined
  if (!field) return 'field_missing'
  return fits(kind, field) ? null : 'type_mismatch'
}

/** Why the binding of `prop` is broken, or null (also null when it is not bound). */
export function propBroken(
  node: ContentNode,
  prop: string,
  scopes: readonly ScopeOption[],
  schemas: Readonly<Record<string, SchemaInfo>>,
): BrokenReason | null {
  const binding = bindingOf(node, prop)
  const value = isObject(binding) && isObject(binding.value) ? binding.value : null
  const kind = kindOf(node.type, prop)
  if (!value || !kind) return null
  if (Array.isArray(value.parts)) {
    const partKind: PropKind = kind === 'metaText' ? 'metaText' : 'text'
    for (const part of value.parts) {
      const reason = isObject(part) ? readerBroken(part, partKind, scopes, schemas) : null
      if (reason) return reason
    }
    return null
  }
  return readerBroken(value, kind, scopes, schemas)
}

/** Every broken binding of a node: property and reason; a page's metadata as `seo.<field>` (017). */
export function nodeBroken(
  node: ContentNode,
  scopes: readonly ScopeOption[],
  schemas: Readonly<Record<string, SchemaInfo>>,
): { prop: string; reason: BrokenReason }[] {
  const bind = node.props.bind
  const seo = node.props.seo
  const seoBind = isObject(seo) && isObject(seo.bind) ? seo.bind : {}
  const props = [...(isObject(bind) ? Object.keys(bind) : []), ...Object.keys(seoBind).map((field) => `seo.${field}`)]
  return props
    .map((prop) => ({ prop, reason: propBroken(node, prop, scopes, schemas) }))
    .filter((b): b is { prop: string; reason: BrokenReason } => b.reason !== null)
}
