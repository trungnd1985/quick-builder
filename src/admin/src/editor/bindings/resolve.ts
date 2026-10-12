// What a bound property shows on the canvas (016 research TB-09, TB-14).
//
// A port of the Rendering module's BindingResolver, so the canvas shows what visitors will see.
// It reads only the data handed to it: the template's sample item, items reached through a
// reference, the schemas, live template patterns and media. A binding that cannot be read is
// broken, and shows as an empty value does (binding-expression.md §4).

import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'
import type { ContentNode } from '../schema/schema'
import { BROKEN, fits, fitsBuiltIn, kindOf, type BrokenReason, type PropKind, type SchemaInfo } from './bindingRules'
import { formatDate, formatMoment, formatNumber } from './format'

/** One item's published data (or, for the canvas's sample item, its draft). */
export interface BoundItem {
  id: string
  collectionId: string
  slug: string
  name: string | null
  firstPublishedAt: string
  lastPublishedAt: string
  /** An item values document: `{ schemaVersion, values }`. */
  values: Json
}

/** Everything a resolution reads. */
export interface ResolveData {
  pageItem: BoundItem | null
  items: Readonly<Record<string, BoundItem>>
  schemas: Readonly<Record<string, SchemaInfo>>
  /** Live template patterns, such as `/blog/{slug}`, by collection id. */
  templates: Readonly<Record<string, string>>
  /** Public paths of the media files that exist, by id. */
  media: Readonly<Record<string, string>>
  language: string
  basePath: string
}

/** An open list card: the list's node id, and the card's item. */
export interface OpenScope {
  name: string
  item: BoundItem
}

export type Bound =
  | { state: 'unbound' }
  | { state: 'value'; text?: string; richText?: Json; media?: string; link?: { href: string; external: boolean } }
  | { state: 'fallback' | 'hide'; broken?: BrokenReason }

const EMPTY: Bound = { state: 'fallback' }
const broken = (reason: BrokenReason): Bound => ({ state: 'fallback', broken: reason })
const text = (value: string | null | undefined): Bound => (value && value.trim() ? { state: 'value', text: value } : EMPTY)

// The page schema's link pattern for an address (page-schema.md §2a).
// eslint-disable-next-line no-control-regex
const ALLOWED_ADDRESS = /^(https?:\/\/[^\u0000- \u007f]+|mailto:[^\u0000- \u007f]+|tel:[+0-9 ().-]+)$/u

const trimBase = (basePath: string): string => (basePath === '/' ? '' : basePath.replace(/\/$/, ''))

/** The item a reader means: the named scope's, or the nearest one's. */
function findScope(name: string | undefined, data: ResolveData, scopes: readonly OpenScope[]): BoundItem | null {
  if (name === undefined) return scopes.length > 0 ? scopes[scopes.length - 1].item : data.pageItem
  if (name === 'page') return data.pageItem
  for (let i = scopes.length - 1; i >= 0; i--) if (scopes[i].name === name) return scopes[i].item
  return null
}

/** The value stored under a field in an item's values, if any. */
export function readValue(item: BoundItem, fieldId: string): Json | undefined {
  return isObject(item.values) && isObject(item.values.values) ? item.values.values[fieldId] : undefined
}

/** What property `prop` of `node` shows when bound; `unbound` when it is not. */
export function resolveBinding(node: ContentNode, prop: string, data: ResolveData, scopes: readonly OpenScope[]): Bound {
  const bind = node.props.bind
  const binding = isObject(bind) ? bind[prop] : undefined
  const kind = kindOf(node.type, prop)
  if (!isObject(binding) || !isObject(binding.value) || kind === null) return { state: 'unbound' }

  const value = binding.value
  const resolved = Array.isArray(value.parts) ? template(value.parts, node, data, scopes) : one(value, kind, node, data, scopes)
  if (resolved.state === 'value') return resolved

  const hide = binding.empty === 'hide'
  const reason = 'broken' in resolved ? resolved.broken : undefined
  return reason ? { state: hide ? 'hide' : 'fallback', broken: reason } : { state: hide ? 'hide' : 'fallback' }
}

/** Whether a binding of `node` leaves it out: nothing to show, and "hide" chosen. */
export function isHidden(node: ContentNode, data: ResolveData, scopes: readonly OpenScope[]): boolean {
  const bind = node.props.bind
  return isObject(bind) && Object.keys(bind).some((prop) => resolveBinding(node, prop, data, scopes).state === 'hide')
}

function template(parts: Json[], node: ContentNode, data: ResolveData, scopes: readonly OpenScope[]): Bound {
  let output = ''
  let anyValue = false
  let firstBroken: BrokenReason | undefined
  for (const part of parts) {
    if (typeof part === 'string') {
      output += part
      continue
    }
    if (!isObject(part)) continue
    const resolved = one(part, 'text', node, data, scopes)
    if (resolved.state === 'value') {
      output += resolved.text ?? ''
      anyValue = true
    } else if ('broken' in resolved && resolved.broken && !firstBroken) {
      firstBroken = resolved.broken
    }
  }
  if (anyValue) return { state: 'value', text: output }
  return firstBroken ? broken(firstBroken) : EMPTY
}

function one(value: JsonObject, kind: PropKind, node: ContentNode, data: ResolveData, scopes: readonly OpenScope[]): Bound {
  let item = findScope(typeof value.scope === 'string' ? value.scope : undefined, data, scopes)
  if (!item) return broken(BROKEN.scopeMissing)

  if (typeof value.via === 'string') {
    const reference = data.schemas[item.collectionId]?.fields[value.via]
    if (!reference) return broken(BROKEN.fieldMissing)
    if (reference.type !== 'reference') return broken(BROKEN.viaNotReference)
    const target = readValue(item, reference.id)
    const referenced = typeof target === 'string' ? data.items[target] : undefined
    // A referenced item without published data reads as empty, not broken.
    if (!referenced) return EMPTY
    item = referenced
  }

  const format = isObject(value.format) ? value.format : undefined
  const dateStyle = format && typeof format.date === 'string' ? format.date : undefined

  if (typeof value.item === 'string') {
    if (!fitsBuiltIn(kind, value.item)) return broken(BROKEN.typeMismatch)
    switch (value.item) {
      case 'slug': return text(item.slug)
      case 'firstPublishedAt': return text(formatMoment(item.firstPublishedAt, dateStyle, data.language))
      case 'lastPublishedAt': return text(formatMoment(item.lastPublishedAt, dateStyle, data.language))
      default: return itemLink(item, data)
    }
  }

  const field = typeof value.field === 'string' ? data.schemas[item.collectionId]?.fields[value.field] : undefined
  if (!field) return broken(BROKEN.fieldMissing)
  if (!fits(kind, field)) return broken(BROKEN.typeMismatch)

  const stored = readValue(item, field.id)
  if (stored === undefined || stored === null) {
    // No key means no value; a boolean never set reads as false.
    return field.type === 'boolean' && kind === 'text' ? text(booleanText(false, format)) : EMPTY
  }

  switch (kind) {
    case 'text': return text(fieldText(field.type, field, stored, format, node.type, data))
    case 'richText': return richTextOf(field.type, stored)
    case 'image': return mediaOf(stored, data)
    case 'link': return linkOf(field.type, stored, data)
  }
}

/** The address of an item's page, when its collection has a live template. */
export function itemLink(item: BoundItem, data: ResolveData): Bound {
  const pattern = data.templates[item.collectionId]
  if (!pattern) return EMPTY
  return { state: 'value', link: { href: trimBase(data.basePath) + pattern.replace('{slug}', item.slug), external: false } }
}

function fieldText(type: string, field: SchemaInfo['fields'][string], stored: Json, format: JsonObject | undefined, blockType: string, data: ResolveData): string | null {
  switch (type) {
    case 'text':
      if (typeof stored !== 'string') return null
      // A heading is one line: a multi-line value reads with its breaks as spaces.
      return blockType === 'heading' ? stored.replace(/\r\n|\r|\n/g, ' ') : stored
    case 'number': {
      if (typeof stored !== 'number') return null
      const places = format && isObject(format.number) && typeof format.number.decimals === 'number' ? format.number.decimals : field.decimals
      return formatNumber(stored, places, data.language)
    }
    case 'date':
      return typeof stored === 'string' ? formatDate(stored, format && typeof format.date === 'string' ? format.date : undefined, data.language) : null
    case 'option':
      return typeof stored === 'string' ? field.options[stored] ?? null : null
    case 'boolean':
      return booleanText(stored === true, format)
    case 'reference':
      return typeof stored === 'string' ? data.items[stored]?.name ?? null : null
    default:
      return null
  }
}

/** A boolean reads as the yes or no text the binding sets; without them it reads as nothing. */
function booleanText(value: boolean, format: JsonObject | undefined): string | null {
  const chosen = format ? format[value ? 'yes' : 'no'] : undefined
  return typeof chosen === 'string' ? chosen : null
}

/** Whether a rich text holds any text or image: one with neither is no value (035). */
function hasContent(node: Json): boolean {
  if (!isObject(node)) return false
  if (node.type === 'text' || node.type === 'image') return true
  return Array.isArray(node.content) && node.content.some(hasContent)
}

function richTextOf(type: string, stored: Json): Bound {
  if (type === 'richText') return hasContent(stored) ? { state: 'value', richText: stored } : EMPTY
  if (typeof stored !== 'string' || !stored.trim()) return EMPTY
  // Multi-line text: one paragraph per line, an empty line an empty paragraph.
  const content: Json[] = stored.replace(/\r\n|\r/g, '\n').split('\n').map((line): Json =>
    line.length > 0 ? { type: 'paragraph', content: [{ type: 'text', text: line }] } : { type: 'paragraph' })
  return { state: 'value', richText: { type: 'doc', content } }
}

const mediaIdOf = (stored: Json): string | null => (isObject(stored) && typeof stored.mediaId === 'string' ? stored.mediaId : null)

function mediaOf(stored: Json, data: ResolveData): Bound {
  const id = mediaIdOf(stored)
  return id && id in data.media ? { state: 'value', media: id } : EMPTY
}

function linkOf(type: string, stored: Json, data: ResolveData): Bound {
  if (type === 'file') {
    const id = mediaIdOf(stored)
    const path = id ? data.media[id] : undefined
    return path ? { state: 'value', link: { href: trimBase(data.basePath) + path, external: false } } : EMPTY
  }
  // A text field holding an address: only the schemes links may use, else nothing.
  return typeof stored === 'string' && stored.length > 0 && stored.length <= 2048 && ALLOWED_ADDRESS.test(stored)
    ? { state: 'value', link: { href: stored, external: true } }
    : EMPTY
}
