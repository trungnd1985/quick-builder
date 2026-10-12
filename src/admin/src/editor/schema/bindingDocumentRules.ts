// Rules 13–20 of the page schema contract (016 research TB-06): bindings and Collection lists.
//
// A port of the Pages module's BindingDocumentRules, run by documentValidator.ts after the
// schema. It refuses only what makes a document meaningless or unsafe; a binding to a field
// that no longer exists, or of a type that does not fit, is broken, never refused
// (binding-expression.md §4). The same golden files run through both.

import { isObject, type Json, type JsonObject } from './subsetValidator'
import { CURRENT_BINDING_VERSION, CURRENT_SCHEMA_VERSION, MAX_LIST_DEPTH, MAX_LISTS_PER_DOCUMENT } from './limits'
import type { ContentDocument, ContentNode, DocumentKind } from './schema'

export type RuleFailure = { rule: string; pointer: string; message: string }

export const LIST_TYPE = 'collection-list'
export const CARD_TYPE = 'list-card'
export const EMPTY_TYPE = 'list-empty'
export const PAGE_SCOPE = 'page'
export const DEFAULT_PAGE_PARAM = 'page'

const fail = (rule: string, pointer: string, message: string): RuleFailure => ({ rule, pointer, message })
const propsInvalid = (pointer: string, message: string): RuleFailure =>
  fail('props_invalid', pointer, `The node's properties are not valid: ${message}`)

const escape = (token: string): string => token.replace(/~/g, '~0').replace(/\//g, '~1')
const has = (value: JsonObject, key: string): boolean => key in value
const childrenOf = (node: JsonObject): Json[] => (Array.isArray(node.children) ? node.children : [])
const propsOf = (node: JsonObject): JsonObject => (isObject(node.props) ? node.props : {})

/** Rule 20: every Collection list holds exactly its card and then its empty state. */
export function checkListChildren(node: Json, pointer: string): RuleFailure | null {
  if (!isObject(node)) return null
  if (node.type === LIST_TYPE) {
    const children = childrenOf(node)
    const ok =
      children.length === 2 &&
      isObject(children[0]) && children[0].type === CARD_TYPE &&
      isObject(children[1]) && children[1].type === EMPTY_TYPE
    if (!ok) return fail('list_children_invalid', `${pointer}/children`, 'A Collection list holds exactly its card and then its empty state.')
  }
  const children = childrenOf(node)
  for (let i = 0; i < children.length; i++) {
    const failure = checkListChildren(children[i], `${pointer}/children/${i}`)
    if (failure) return failure
  }
  return null
}

/**
 * The document saved for a tree: it states its binding format version exactly when it uses
 * bindings or lists (rule 13). The one place documents are built, so a save never differs from
 * what the editor checks.
 */
export function contentDocument(root: ContentNode): ContentDocument {
  return usesBindings(root as unknown as Json)
    ? { schemaVersion: CURRENT_SCHEMA_VERSION, bindingVersion: CURRENT_BINDING_VERSION, root }
    : { schemaVersion: CURRENT_SCHEMA_VERSION, root }
}

/** Whether a document holds a binding or a Collection list anywhere. */
export function usesBindings(node: Json): boolean {
  if (!isObject(node)) return false
  const bind = propsOf(node).bind
  if (node.type === LIST_TYPE || (isObject(bind) && Object.keys(bind).length > 0)) return true
  return childrenOf(node).some(usesBindings)
}

/**
 * Rules 13, 15–19 and the "exactly one of" shapes the schema cannot say. Collects each list's
 * source collection, with the pointer of its first source, for rule 14.
 */
export function checkBindingRules(document: JsonObject, kind: DocumentKind, collections: Map<string, string>): RuleFailure | null {
  const root = document.root
  if (usesBindings(root) && !has(document, 'bindingVersion')) {
    return fail('binding_version_required', '', 'A document with bindings or collection lists must state its binding format version.')
  }
  return new Walk(kind, collections).visit(root, '/root')
}

class Walk {
  /** The enclosing lists whose card the walk is inside, innermost last. */
  private readonly scopes: string[] = []
  private readonly pageParams = new Set<string>()
  private lists = 0
  /** How many lists the walk is inside, through their card or their empty state. */
  private listDepth = 0

  private readonly kind: DocumentKind
  private readonly collections: Map<string, string>

  constructor(kind: DocumentKind, collections: Map<string, string>) {
    this.kind = kind
    this.collections = collections
  }

  visit(node: Json, pointer: string): RuleFailure | null {
    if (!isObject(node)) return null
    const props = propsOf(node)

    if (node.type === LIST_TYPE) {
      const failure = this.checkList(props, pointer)
      if (failure) return failure
    }

    if (isObject(props.bind)) {
      for (const [name, binding] of Object.entries(props.bind)) {
        if (!isObject(binding) || !isObject(binding.value)) continue
        const failure = this.checkBindingValue(binding.value, `${pointer}/props/bind/${escape(name)}/value`)
        if (failure) return failure
      }
    }

    if (node.type === LIST_TYPE) this.listDepth++
    const children = childrenOf(node)
    for (let i = 0; i < children.length; i++) {
      // A card's blocks bind to the card's item, so the card opens the list's scope; the empty
      // state has no item and opens none.
      const child = children[i]
      const opensScope = node.type === LIST_TYPE && isObject(child) && child.type === CARD_TYPE
      if (opensScope) this.scopes.push(String(node.id))
      const failure = this.visit(child, `${pointer}/children/${i}`)
      if (opensScope) this.scopes.pop()
      if (failure) return failure
    }
    if (node.type === LIST_TYPE) this.listDepth--
    return null
  }

  private checkList(props: JsonObject, pointer: string): RuleFailure | null {
    if (++this.lists > MAX_LISTS_PER_DOCUMENT) {
      return fail('too_many_lists', `${pointer}/type`, `A document may hold at most ${MAX_LISTS_PER_DOCUMENT} collection lists.`)
    }
    if (this.listDepth + 1 > MAX_LIST_DEPTH) {
      return fail('list_too_deep', `${pointer}/type`, `Collection lists may be nested at most ${MAX_LIST_DEPTH} deep.`)
    }

    const source = isObject(props.source) ? props.source : {}
    const sourcePointer = `${pointer}/props/source`
    if (has(source, 'collectionId') === has(source, 'field')) {
      return propsInvalid(sourcePointer, "A list's source names either a collection or a multi-reference field.")
    }
    if (typeof source.collectionId === 'string') {
      if (!this.collections.has(source.collectionId)) this.collections.set(source.collectionId, sourcePointer)
    } else {
      const failure = this.checkScope(source, sourcePointer)
      if (failure) return failure
    }

    const filters = Array.isArray(props.filters) ? props.filters : []
    for (let i = 0; i < filters.length; i++) {
      const filter = filters[i]
      if (!isObject(filter)) continue
      const here = `${pointer}/props/filters/${i}`
      if (has(filter, 'field') === has(filter, 'item')) return propsInvalid(here, 'A filter names either a field or an item value.')
      if (isObject(filter.value)) {
        const valuePointer = `${here}/value`
        if (has(filter.value, 'field') === has(filter.value, 'current')) {
          return propsInvalid(valuePointer, 'A filter value from a scope names either a field or the current item.')
        }
        const failure = this.checkScope(filter.value, valuePointer)
        if (failure) return failure
      }
    }

    const sorts = Array.isArray(props.sort) ? props.sort : []
    for (let i = 0; i < sorts.length; i++) {
      const sort = sorts[i]
      if (isObject(sort) && has(sort, 'field') === has(sort, 'item')) {
        return propsInvalid(`${pointer}/props/sort/${i}`, 'A sort key names either a field or an item value.')
      }
    }

    if (isObject(props.paging)) {
      const named = typeof props.paging.param === 'string'
      const name = named ? (props.paging.param as string) : DEFAULT_PAGE_PARAM
      if (this.pageParams.has(name)) {
        return fail('duplicate_page_param', named ? `${pointer}/props/paging/param` : `${pointer}/props/paging`,
          'Two paginated lists in one document use the same page parameter.')
      }
      this.pageParams.add(name)
    }
    return null
  }

  private checkBindingValue(value: JsonObject, pointer: string): RuleFailure | null {
    const named = ['field', 'item', 'parts'].filter((key) => has(value, key)).length
    if (named !== 1) return propsInvalid(pointer, 'A binding names exactly one of a field, an item value or text parts.')

    if (Array.isArray(value.parts)) {
      if (Object.keys(value).some((key) => key !== 'parts')) return propsInvalid(pointer, 'A text template holds only its parts.')
      for (let i = 0; i < value.parts.length; i++) {
        const part = value.parts[i]
        if (!isObject(part)) continue
        const here = `${pointer}/parts/${i}`
        if (has(part, 'field') === has(part, 'item')) return propsInvalid(here, 'A value in a text template names either a field or an item value.')
        const failure = this.checkScope(part, here)
        if (failure) return failure
      }
      return null
    }
    return this.checkScope(value, pointer)
  }

  /** Rules 15 and 16: the scope a reader names, or the nearest one, must exist here. */
  private checkScope(reader: JsonObject, pointer: string): RuleFailure | null {
    let scope: string
    let where: string
    if (typeof reader.scope === 'string') {
      scope = reader.scope
      where = `${pointer}/scope`
      if (scope !== PAGE_SCOPE && !this.scopes.includes(scope)) {
        return fail('unknown_scope', where, 'The scope names no enclosing collection list.')
      }
    } else {
      scope = this.scopes.length > 0 ? this.scopes[this.scopes.length - 1] : PAGE_SCOPE
      where = pointer
    }
    if (scope === PAGE_SCOPE && this.kind === 'component') {
      return fail('page_scope_not_allowed', where, 'A component cannot read the item of the page it is shown on.')
    }
    return null
  }
}
