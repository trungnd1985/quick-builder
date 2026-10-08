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
const PLAIN_MARKS: ReadonlySet<string> = new Set(['bold', 'italic', 'underline', 'strike', 'code'])

/** The longest alternative text, in code points (`rtImage.attrs.alt.maxLength`). */
const MAX_ALT = 300

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

const ALIGNMENTS: ReadonlySet<string> = new Set(['start', 'center', 'end'])

/**
 * A paragraph's or heading's own alignment: TipTap's `textAlign`, or the contract's `align`
 * (035 US6). Anything else, and none, means the block's own.
 */
function alignOf(attrs: unknown): string | null {
  if (!isObject(attrs)) return null
  const value = attrs.textAlign ?? attrs.align
  return typeof value === 'string' && ALIGNMENTS.has(value) ? value : null
}

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
    const align = alignOf(node.attrs)
    const heading: { [key: string]: Json } = { type: 'heading', attrs: align ? { level: headingLevel(node.attrs), align } : { level: headingLevel(node.attrs) } }
    if (text.length > 0) heading.content = text
    return [heading]
  }

  // An image lives at the top level only, and only once it names a file (an upload in progress
  // does not). Anything but its media, alternative text and decorative flag is dropped (035 US3).
  if (node.type === 'image') {
    const attrs = isObject(node.attrs) ? node.attrs : {}
    const media = isObject(attrs.media) && typeof attrs.media.mediaId === 'string' ? attrs.media.mediaId : null
    if (place === 'nested' || media === null) return []
    const kept: { [key: string]: Json } = { media: { mediaId: media } }
    if (typeof attrs.alt === 'string' && attrs.alt !== '') kept.alt = [...attrs.alt].slice(0, MAX_ALT).join('')
    if (attrs.decorative === true) kept.decorative = true
    return [{ type: 'image', attrs: kept }]
  }

  if (node.type === 'table') {
    const rows = tableRows(node)
    if (place === 'nested') return rows.flat(2)
    return rows.length === 0 ? [] : [table(rows, firstRowIsHeader(node))]
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
  const align = node.type === 'paragraph' || node.type === 'heading' ? alignOf(node.attrs) : null
  const paragraph: { [key: string]: Json } = align ? { type: 'paragraph', attrs: { align } } : { type: 'paragraph' }
  if (text.length > 0) paragraph.content = text
  return [paragraph]
}

/** The most rows and columns a table may have (`rtTable`, `rtTableRow`); more is cut off. */
const MAX_ROWS = 200
const MAX_COLUMNS = 20

/** A cell's paragraphs: lists, headings and quotes inside a cell become paragraphs. */
function cellParagraphs(content: Json[]): Json[] {
  const flat = (b: Json): Json[] =>
    isObject(b) && b.type === 'paragraph' ? [b] : isObject(b) && Array.isArray(b.content) ? b.content.flatMap((c) => flat(c)) : []
  return content.flatMap((child) => block(child, 'nested')).flatMap(flat)
}

/**
 * A table's rows as a grid of cell contents (each a list of paragraphs), with merged cells
 * split — the content in the first position, empty cells for the rest — and rows padded to
 * the widest, so the table is rectangular (035 US4 scenario 5, rule 11).
 */
function tableRows(node: { [key: string]: Json }): Json[][][] {
  const rows = (Array.isArray(node.content) ? node.content : []).filter((r) => isObject(r) && r.type === 'tableRow')
  const pending = new Map<number, number>()
  const grid: Json[][][] = []

  for (const row of rows.slice(0, MAX_ROWS)) {
    const cells: Json[][] = []
    let column = 0
    const fill = (): void => {
      while ((pending.get(column) ?? 0) > 0) {
        pending.set(column, (pending.get(column) ?? 1) - 1)
        cells.push([])
        column++
      }
    }
    for (const cell of isObject(row) && Array.isArray(row.content) ? row.content : []) {
      if (!isObject(cell)) continue
      fill()
      const attrs = isObject(cell.attrs) ? cell.attrs : {}
      const span = typeof attrs.colspan === 'number' && attrs.colspan > 1 ? Math.floor(attrs.colspan) : 1
      const down = typeof attrs.rowspan === 'number' && attrs.rowspan > 1 ? Math.floor(attrs.rowspan) : 1
      cells.push(cellParagraphs(Array.isArray(cell.content) ? cell.content : []))
      for (let extra = 1; extra < span; extra++) cells.push([])
      if (down > 1) for (let k = 0; k < span; k++) pending.set(column + k, down - 1)
      column += span
    }
    fill()
    grid.push(cells)
  }

  const width = Math.min(MAX_COLUMNS, Math.max(0, ...grid.map((r) => r.length)))
  return width === 0 ? [] : grid.map((r) => [...r.slice(0, width), ...Array.from({ length: Math.max(0, width - r.length) }, () => [])])
}

/** Whether the first row is a header row: any header cell in it makes all of it one (rule 12). */
function firstRowIsHeader(node: { [key: string]: Json }): boolean {
  const first = Array.isArray(node.content) ? node.content[0] : undefined
  return isObject(first) && Array.isArray(first.content) && first.content.some((c) => isObject(c) && c.type === 'tableHeader')
}

function table(rows: Json[][][], header: boolean): Json {
  return {
    type: 'table',
    content: rows.map((cells, r) => ({
      type: 'tableRow',
      content: cells.map((paragraphs) => ({
        type: header && r === 0 ? 'tableHeader' : 'tableCell',
        content: paragraphs.length > 0 ? paragraphs.slice(0, 50) : [{ type: 'paragraph' }],
      })),
    })),
  }
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

/** A stored node as TipTap content: the contract's `align` is TipTap's `textAlign` (035 US6). */
function toTipTap(node: Json): Json {
  if (!isObject(node)) return node
  const content = Array.isArray(node.content) ? node.content.map(toTipTap) : undefined
  const attrs = isObject(node.attrs) && 'align' in node.attrs
    ? Object.fromEntries(Object.entries(node.attrs).map(([k, v]) => (k === 'align' ? ['textAlign', v] : [k, v])))
    : node.attrs
  return { ...node, ...(attrs === undefined ? {} : { attrs }), ...(content === undefined ? {} : { content }) }
}

/** Stored rich text as TipTap content: the contract is a subset of it, but for alignment's name. */
export function fromContractRichText(value: unknown): RichTextDoc {
  return isObject(value) && value.type === 'doc' && Array.isArray(value.content)
    ? { type: 'doc', content: value.content.map(toTipTap) }
    : { type: 'doc', content: [] }
}
