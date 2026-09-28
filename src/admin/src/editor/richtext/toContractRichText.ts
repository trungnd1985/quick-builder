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

function marks(value: unknown): Json[] {
  if (!Array.isArray(value)) return []
  const kept: Json[] = []
  for (const mark of value) {
    if (!isObject(mark)) continue
    if (mark.type === 'bold' || mark.type === 'italic') kept.push({ type: mark.type })
    if (mark.type === 'link') {
      const to = link(isObject(mark.attrs) ? mark.attrs.to : undefined)
      if (to) kept.push({ type: 'link', attrs: { to } })
    }
  }
  return kept.slice(0, 3)
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

function block(node: unknown): Json[] {
  if (!isObject(node)) return []
  const content = Array.isArray(node.content) ? node.content : []

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
  if (node.type !== 'paragraph' && holdsBlocks(content)) return content.flatMap(block)

  const text = content.flatMap(inline)
  return [text.length > 0 ? { type: 'paragraph', content: text } : { type: 'paragraph' }]
}

function listItem(node: unknown): Json | null {
  if (!isObject(node) || node.type !== 'listItem') return null
  const content = (Array.isArray(node.content) ? node.content : []).flatMap(block)
  return { type: 'listItem', content: content.length > 0 ? content : [{ type: 'paragraph' }] }
}

/** Normalises any TipTap or ProseMirror document to the contract's rich text. */
export function toContractRichText(value: unknown): RichTextDoc {
  const content = isObject(value) && Array.isArray(value.content) ? value.content : []
  return { type: 'doc', content: content.flatMap(block) }
}

/** Stored rich text as TipTap content: identical, since the contract is a subset of it. */
export function fromContractRichText(value: unknown): RichTextDoc {
  return isObject(value) && value.type === 'doc' && Array.isArray(value.content)
    ? (value as unknown as RichTextDoc)
    : { type: 'doc', content: [] }
}
