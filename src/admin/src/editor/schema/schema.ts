// The page schema as the editor sees it (011 research ED-05, ED-08).
//
// The editor imports docs/contracts/page-schema/v1.schema.json itself — the file the server
// embeds — and reads from it everything a block type's single definition holds: which types
// exist, where each may sit, their properties, limits and defaults. Nothing here is
// repeated by hand, so the editor cannot drift from the contract.

import rawSchema from '@contracts/page-schema/v1.schema.json'
import { isObject, SchemaSubsetValidator, type Json, type JsonObject } from './subsetValidator'

export type DocumentKind = 'page' | 'component'

/** One node of a content document (docs/contracts/page-schema.md §1). */
export interface ContentNode {
  readonly id: string
  readonly type: string
  readonly props: Readonly<Record<string, Json>>
  readonly children: readonly ContentNode[]
}

/** A content document: the envelope and its root. */
export interface ContentDocument {
  readonly schemaVersion: number
  /** The binding format version, present when the document holds bindings or lists (016 rule 13). */
  readonly bindingVersion?: number
  readonly root: ContentNode
}

export const pageSchema = SchemaSubsetValidator.load(rawSchema as unknown as JsonObject)

const branchTypes = (name: string): ReadonlySet<string> => new Set(pageSchema.branchTypes(name))

/**
 * The placement lists: what may sit under the root, inside a section or container, and inside
 * a Collection list (its card and empty state, 016).
 */
export const placement = {
  rootChild: branchTypes('rootChild'),
  contentChild: branchTypes('contentChild'),
  listChild: branchTypes('listChild'),
} as const

/** Every node type other than `root`, in schema order. */
export const nodeTypes: readonly string[] = [...placement.rootChild, ...placement.contentChild, ...placement.listChild]

/**
 * The content block types: every node type but the component reference and a list's card and
 * empty state, which exist only inside their list.
 */
export const blockTypes: readonly string[] = [...placement.rootChild, ...placement.contentChild].filter((type) => type !== 'component-ref')

const nodeDefinition = (type: string): JsonObject => {
  if (type !== 'root' && !nodeTypes.includes(type)) throw new Error(`Unknown node type '${type}'.`)
  return pageSchema.definition(type)
}

const propertiesOf = (type: string): JsonObject => {
  const node = nodeDefinition(type)
  const props = isObject(node.properties) ? node.properties.props : undefined
  if (!isObject(props)) throw new Error(`The schema's '${type}' has no props.`)
  return props
}

/** The schema of a type's `props` object. */
export const propsSchema = (type: string): JsonObject => propertiesOf(type)

/** The names of a type's properties, in schema order. */
export const propNames = (type: string): string[] => {
  const properties = propertiesOf(type).properties
  return isObject(properties) ? Object.keys(properties) : []
}

/** The schema of one property, as written (it may be a `$ref` with a `default` beside it). */
export const propSchema = (type: string, prop: string): Json => {
  const properties = propertiesOf(type).properties
  if (!isObject(properties) || !(prop in properties)) throw new Error(`'${type}' has no property '${prop}'.`)
  return properties[prop]
}

/** The schema of one property with any `$ref` followed, merged with what sits beside it. */
export const resolvedPropSchema = (type: string, prop: string): JsonObject => {
  const written = propSchema(type, prop)
  if (!isObject(written)) return {}
  const target = pageSchema.resolve(written)
  const beside = Object.fromEntries(Object.entries(written).filter(([key]) => key !== '$ref'))
  return { ...(isObject(target) ? target : {}), ...beside }
}

/** A property's `default`, from beside a `$ref` or from its definition; undefined if none. */
export const defaultOf = (type: string, prop: string): Json | undefined => {
  const resolved = resolvedPropSchema(type, prop)
  return 'default' in resolved ? resolved.default : undefined
}

/** A type's required properties. */
export const requiredProps = (type: string): string[] => {
  const required = propertiesOf(type).required
  return Array.isArray(required) ? required.filter((r): r is string => typeof r === 'string') : []
}

/** Whether a type holds children at all. */
export const takesChildren = (type: string): boolean => {
  if (type === 'root') return true
  const node = nodeDefinition(type)
  const children = isObject(node.properties) ? pageSchema.resolve(node.properties.children) : undefined
  return !(isObject(children) && children.maxItems === 0)
}

/** Which placement list a parent's children are checked against. */
const listFor = (parentType: string): ReadonlySet<string> | null => {
  if (parentType === 'root') return placement.rootChild
  const node = nodeDefinition(parentType)
  const children = isObject(node.properties) ? pageSchema.resolve(node.properties.children) : undefined
  const items = isObject(children) ? children.items : undefined
  if (isObject(items) && typeof items.$ref === 'string') {
    const name = items.$ref.slice('#/$defs/'.length)
    if (name === 'rootChild') return placement.rootChild
    if (name === 'contentChild') return placement.contentChild
    if (name === 'listChild') return placement.listChild
  }
  return null
}

/**
 * Whether a node of `childType` may sit directly inside `parentType` in a document of
 * `kind` — exactly the rule the schema enforces, plus contract rule 7 (no component
 * references inside a component).
 */
export function canDrop(parentType: string, childType: string, kind: DocumentKind): boolean {
  if (childType === 'component-ref' && kind === 'component') return false
  return listFor(parentType)?.has(childType) ?? false
}
