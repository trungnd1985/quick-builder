// The editor's validator of whole content documents (011 research ED-08, ED-13).
//
// A port of the Pages module's PageDocumentValidator, run before every save so the editor
// never sends a document the server is certain to refuse, and on load so a draft that no
// longer validates opens read-only. It runs the same checks in the same order and reports
// the same rule and pointer for every golden file.
//
// Not ported: malformed JSON and duplicate property names. The editor validates objects it
// built or parsed, where neither can occur.

import { codePointLength, isObject, type Json } from './subsetValidator'
import { MAX_DOCUMENT_BYTES, MAX_NODE_DEPTH, MAX_NODES, MAX_RICH_TEXT_CHARS, MAX_RICH_TEXT_IMAGES, MAX_RICH_TEXT_TABLES, SUPPORTED_SCHEMA_VERSIONS } from './limits'
import { nodeTypes, pageSchema, type DocumentKind } from './schema'
import { checkRichTextRules } from './richTextRules'

export type DocumentValidation =
  | { valid: true }
  | { valid: false; rule: string; pointer: string; message: string }

const invalid = (rule: string, pointer: string, message: string): DocumentValidation => ({ valid: false, rule, pointer, message })

const NODE_TYPES: ReadonlySet<string> = new Set(nodeTypes)

/** The UTF-8 size of a value's JSON, as the server measures the saved document. */
export const utf8Size = (value: unknown): number => new TextEncoder().encode(JSON.stringify(value)).length

export function validateDocument(document: unknown, kind: DocumentKind): DocumentValidation {
  if (utf8Size(document) > MAX_DOCUMENT_BYTES) {
    return invalid('too_large', '', `A document may be at most ${MAX_DOCUMENT_BYTES} bytes.`)
  }

  if (isObject(document) && typeof document.schemaVersion === 'number' && Number.isInteger(document.schemaVersion)) {
    if (!SUPPORTED_SCHEMA_VERSIONS.has(document.schemaVersion)) {
      return invalid('unsupported_schema_version', '/schemaVersion', 'This installation does not support that schema version.')
    }
  }

  if (isObject(document) && 'root' in document) {
    const structural = walk(document.root, '/root', 1, { ids: new Set(), count: 0 })
    if (structural) return structural
  }

  const failure = pageSchema.validate(document as Json)
  if (failure) return fromSchema(failure.location, failure.keyword, failure.message, document)

  const root = (document as { root: Json }).root
  return componentRules(root, '/root', kind) ?? richTextLength(root, '/root') ?? richTextRules(root, '/root') ?? { valid: true }
}

/** Depth, count and id uniqueness, over whatever node-shaped values exist. */
function walk(node: Json, pointer: string, depth: number, state: { ids: Set<string>; count: number }): DocumentValidation | null {
  if (!isObject(node)) return null
  if (depth > MAX_NODE_DEPTH) return invalid('too_deep', pointer, `Nodes may be nested at most ${MAX_NODE_DEPTH} deep.`)
  if (++state.count > MAX_NODES) return invalid('too_many_nodes', pointer, `A document may hold at most ${MAX_NODES} nodes.`)

  if (typeof node.id === 'string') {
    if (state.ids.has(node.id)) return invalid('duplicate_node_id', `${pointer}/id`, 'Another node in this document has the same id.')
    state.ids.add(node.id)
  }

  if (Array.isArray(node.children)) {
    for (let i = 0; i < node.children.length; i++) {
      const failure = walk(node.children[i], `${pointer}/children/${i}`, depth + 1, state)
      if (failure) return failure
    }
  }
  return null
}

/** The contract's rule names for a schema failure (page-schema.md §3, step 5). */
function fromSchema(location: string, keyword: string, message: string, document: unknown): DocumentValidation {
  if (location.split('/').includes('props')) {
    return invalid('props_invalid', keyword === 'discriminator' ? `${location}/type` : location, `The node's properties are not valid: ${message}`)
  }

  if (keyword === 'discriminator') {
    const node = at(document, location)
    const type = isObject(node) && typeof node.type === 'string' ? node.type : null
    if (type === 'root') return invalid('root_misplaced', location, 'A root node may only be the document\'s root.')
    return type !== null && NODE_TYPES.has(type)
      ? invalid('nesting_not_allowed', `${location}/type`, 'This kind of node is not allowed here.')
      : invalid('unknown_type', `${location}/type`, 'This is not a known node type.')
  }

  if (keyword === 'maxItems' && location.endsWith('/children')) {
    return invalid('children_not_allowed', location, 'This kind of node cannot have children.')
  }

  return invalid('schema_violation', location, message)
}

function componentRules(node: Json, pointer: string, kind: DocumentKind): DocumentValidation | null {
  if (!isObject(node)) return null
  if (node.type === 'component-ref' && kind === 'component') {
    return invalid('component_ref_not_allowed', pointer, 'A component cannot contain other components.')
  }
  const children = Array.isArray(node.children) ? node.children : []
  for (let i = 0; i < children.length; i++) {
    const failure = componentRules(children[i], `${pointer}/children/${i}`, kind)
    if (failure) return failure
  }
  return null
}

/** Contract §3 rule 9: a Rich Text block's text, over all its text nodes, in code points. */
export function richTextChars(value: Json): number {
  if (!isObject(value)) return 0
  let length = typeof value.text === 'string' ? codePointLength(value.text) : 0
  if (Array.isArray(value.content)) for (const child of value.content) length += richTextChars(child)
  return length
}

function richTextLength(node: Json, pointer: string): DocumentValidation | null {
  if (!isObject(node)) return null
  if (node.type === 'rich-text' && isObject(node.props) && richTextChars(node.props.content) > MAX_RICH_TEXT_CHARS) {
    return invalid('rich_text_too_long', `${pointer}/props/content`, `A Rich Text block may hold at most ${MAX_RICH_TEXT_CHARS} characters of text.`)
  }
  const children = Array.isArray(node.children) ? node.children : []
  for (let i = 0; i < children.length; i++) {
    const failure = richTextLength(children[i], `${pointer}/children/${i}`)
    if (failure) return failure
  }
  return null
}

const RULE_MESSAGES: Record<string, string> = {
  rich_text_too_many_images: `A rich text may show at most ${MAX_RICH_TEXT_IMAGES} images.`,
  rich_text_too_many_tables: `A rich text may hold at most ${MAX_RICH_TEXT_TABLES} tables.`,
  table_not_rectangular: 'Every row of a table must have the same number of cells.',
  table_header_misplaced: 'Header cells may only make up the whole first row of a table.',
}

/** Contract §3 rules 10–12 (035), for every Rich Text block in document order. */
function richTextRules(node: Json, pointer: string): DocumentValidation | null {
  if (!isObject(node)) return null
  if (node.type === 'rich-text' && isObject(node.props)) {
    const broken = checkRichTextRules(node.props.content, `${pointer}/props/content`)
    if (broken) return invalid(broken.rule, broken.pointer, RULE_MESSAGES[broken.rule] ?? 'The rich text breaks a rule of the page schema.')
  }
  const children = Array.isArray(node.children) ? node.children : []
  for (let i = 0; i < children.length; i++) {
    const failure = richTextRules(children[i], `${pointer}/children/${i}`)
    if (failure) return failure
  }
  return null
}

function at(document: unknown, pointer: string): unknown {
  let current: unknown = document
  for (const raw of pointer.split('/').slice(1)) {
    const token = raw.replace(/~1/g, '/').replace(/~0/g, '~')
    if (Array.isArray(current)) current = current[Number(token)]
    else if (isObject(current)) current = current[token]
    else return undefined
  }
  return current
}
