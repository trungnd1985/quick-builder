// TipTap's JSON and the page schema's rich text (011 research ED-03, ED-06).
//
// TipTap stores ProseMirror JSON, the format the constitution names (Principle IX), but it
// adds attributes the contract does not have (null alignments, link targets, list types).
// Everything TipTap produces passes through `toContractRichText` before it reaches the
// document, so only the contract's nodes, marks and attributes are ever stored, whatever was
// typed or pasted. `fromContractRichText` goes the other way, to load stored text into TipTap.

import { isObject, type Json } from '../schema/subsetValidator'

export interface RichTextDoc {
  type: 'doc'
  content: Json[]
}


function link(value: unknown): Json | null {
  if (!isObject(value)) return null
  if (value.type === 'page' && typeof value.pageId === 'string') return { type: 'page', pageId: value.pageId }
  if (value.type === 'url' && typeof value.href === 'string') return { type: 'url', href: value.href }
  return null
}

/** The marks without attributes the contract allows; `link` is handled on its own. Later formats join here. */
const PLAIN_MARKS: ReadonlySet<string> = new Set(['bold', 'italic'])

/** The most marks one text node may carry (`rtText.marks.maxItems`). */
const MAX_MARKS = 6

function marks(value: unknown): Json[] {
  if (!Array.isArray(value)) return []
  const kept: Json[] = []
  const seen = new Set<string>()
  for (const mark of value) {
    if (!isObject(mark) || typeof mark.type !== 'string' || seen.has(mark.type)) continue
    seen.add(mark.type)
    if (PLAIN_MARKS.has(mark.type)) kept.push({ type: mark.type })
    if (mark.type === 'link') {
      const to = link(isObject(mark.attrs) ? mark.attrs.to : undefined)
      if (to) kept.push({ type: 'link', attrs: { to } })
    }
  }
  return kept.slice(0, MAX_MARKS)
}

function inline(node: unknown): Json[] {
  if (!isObject(node)) return []
  if (node.type === 'hardBreak') return [{ type: 'hardBreak' }]
  if (node.type === 'text' && typeof node.text === 'string' && node.text.length > 0) {
    const kept = marks(node.marks)
    return [kept.length > 0 ? { type: 'text', text: node.text, marks: kept } : { type: 'text', text: node.text }]
  }
  // Anything else inline (an image, a mention) keeps whatever text it holds.
  return Array.isArray(node.content) ? node.content.flatMap(inline) : []
}

/** Whether a node holds blocks rather than text — a quote or a stray list item, say. */
const holdsBlocks = (content: Json[]): boolean =>
  content.length > 0 && content.every((c) => isObject(c) && c.type !== 'text' && c.type !== 'hardBreak')

/**
 * Where a block sits: at the top of the document, or inside a list item or a quote, which hold
 * only paragraphs and lists (`rtListBlock`, 035 research RT-01).
 */
type Place = 'top' | 'nested'

/** A heading level the contract allows: 1 reads as 2, and 5 and below as 4 (035 US4 scenario 1). */
function headingLevel(attrs: unknown): number {
  const level = isObject(attrs) && typeof attrs.level === 'number' ? attrs.level : 2
  return Math.min(4, Math.max(2, Math.round(level)))
}

function block(node: unknown, place: Place = 'top'): Json[] {
  if (!isObject(node)) return []
  const content = Array.isArray(node.content) ? node.content : []

  // Headings and quotes live at the top level only; anywhere else they become their paragraphs.
  if (node.type === 'heading') {
    const text = content.flatMap(inline)
    if (place === 'nested') return [text.length > 0 ? { type: 'paragraph', content: text } : { type: 'paragraph' }]
    const heading: { [key: string]: Json } = { type: 'heading', attrs: { level: headingLevel(node.attrs) } }
    if (text.length > 0) heading.content = text
    return [heading]
  }

  if (node.type === 'blockquote') {
    const inner = content.flatMap((child) => block(child, 'nested'))
    if (place === 'nested') return inner
    return [{ type: 'blockquote', content: inner.length > 0 ? inner : [{ type: 'paragraph' }] }]
  }

  if (node.type === 'bulletList' || node.type === 'orderedList') {
    const items = content.map(listItem).filter((i): i is Json => i !== null)
    if (items.length === 0) return []
    const start = isObject(node.attrs) && typeof node.attrs.start === 'number' ? node.attrs.start : 1
    return node.type === 'orderedList' && start > 1
      ? [{ type: 'orderedList', attrs: { start: Math.min(start, 9999) }, content: items }]
      : [{ type: node.type, content: items }]
  }

  // An unknown wrapper of blocks is unwrapped; any other unknown block (a heading, code)
  // becomes a paragraph of its text.
  if (node.type !== 'paragraph' && holdsBlocks(content)) return content.flatMap((child) => block(child, place))

  const text = content.flatMap(inline)
  return [text.length > 0 ? { type: 'paragraph', content: text } : { type: 'paragraph' }]
}

function listItem(node: unknown): Json | null {
  if (!isObject(node) || node.type !== 'listItem') return null
  const content = (Array.isArray(node.content) ? node.content : []).flatMap((child) => block(child, 'nested'))
  return { type: 'listItem', content: content.length > 0 ? content : [{ type: 'paragraph' }] }
}

/** Normalises any TipTap or ProseMirror document to the contract's rich text. */
export function toContractRichText(value: unknown): RichTextDoc {
  const content = isObject(value) && Array.isArray(value.content) ? value.content : []
  return { type: 'doc', content: content.flatMap((child) => block(child, 'top')) }
}

/** Stored rich text as TipTap content: identical, since the contract is a subset of it. */
export function fromContractRichText(value: unknown): RichTextDoc {
  return isObject(value) && value.type === 'doc' && Array.isArray(value.content)
    ? (value as unknown as RichTextDoc)
    : { type: 'doc', content: [] }
}
