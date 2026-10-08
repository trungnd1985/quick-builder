// The rich text rules the schema cannot say (page-schema.md §3 rules 10–12, 035 research
// RT-02), and what the editor needs to know about a rich text. The twin of the server's
// RichTextRules: both sides must give the same answer for every document (SC-007).

import { isObject, type Json } from './subsetValidator'
import { MAX_RICH_TEXT_IMAGES, MAX_RICH_TEXT_TABLES } from './limits'

export interface RichTextFailure {
  rule: string
  pointer: string
}

interface Located {
  node: { [key: string]: Json }
  pointer: string
}

const EXTENDED_NODES: ReadonlySet<string> = new Set(['heading', 'blockquote', 'image', 'table'])
const EXTENDED_MARKS: ReadonlySet<string> = new Set(['underline', 'strike', 'code'])

const contentOf = (node: Json): Json[] => (isObject(node) && Array.isArray(node.content) ? node.content : [])

/** Every node object below the document, with its pointer, in document order. */
function nodes(document: Json, pointer: string): Located[] {
  const found: Located[] = []
  const visit = (node: Json, at: string): void => {
    if (!isObject(node)) return
    found.push({ node, pointer: at })
    contentOf(node).forEach((child, i) => visit(child, `${at}/content/${i}`))
  }
  visit(document, pointer)
  return found
}

/**
 * The first of rules 10 (image and table counts), 11 (rectangular tables) and 12 (header cells
 * in the first row only) that the document breaks, in that order; null when it meets them all.
 */
export function checkRichTextRules(document: Json, pointer: string): RichTextFailure | null {
  const all = nodes(document, pointer)
  const tables = all.filter((n) => n.node.type === 'table')

  if (all.filter((n) => n.node.type === 'image').length > MAX_RICH_TEXT_IMAGES) {
    return { rule: 'rich_text_too_many_images', pointer }
  }
  if (tables.length > MAX_RICH_TEXT_TABLES) return { rule: 'rich_text_too_many_tables', pointer }

  for (const table of tables) {
    const rows = contentOf(table.node)
    for (let r = 1; r < rows.length; r++) {
      if (contentOf(rows[r]).length !== contentOf(rows[0]).length) {
        return { rule: 'table_not_rectangular', pointer: `${table.pointer}/content/${r}` }
      }
    }
  }

  for (const table of tables) {
    const rows = contentOf(table.node)
    for (let r = 0; r < rows.length; r++) {
      const cells = contentOf(rows[r])
      const headerRow = r === 0 && cells.some((c) => isObject(c) && c.type === 'tableHeader')
      for (let c = 0; c < cells.length; c++) {
        const cell = cells[c]
        if ((isObject(cell) && cell.type === 'tableHeader') !== headerRow) {
          return { rule: 'table_header_misplaced', pointer: `${table.pointer}/content/${r}/content/${c}` }
        }
      }
    }
  }

  return null
}

function hasAltOrIsDecorative(image: { [key: string]: Json }): boolean {
  const attrs = image.attrs
  if (!isObject(attrs)) return false
  if (attrs.decorative === true) return true
  return typeof attrs.alt === 'string' && attrs.alt.trim() !== ''
}

/** The images that may not be published as they are: no alternative text and not decorative (FR-014). */
export function imagesWithoutAlt(document: Json, pointer: string): string[] {
  return nodes(document, pointer)
    .filter((n) => n.node.type === 'image' && !hasAltOrIsDecorative(n.node))
    .map((n) => n.pointer)
}

/** Whether the document uses anything an admin from before 035 cannot keep (RT-13). */
export function usesExtendedFormats(document: Json): boolean {
  return nodes(document, '').some(({ node }) =>
    (typeof node.type === 'string' && EXTENDED_NODES.has(node.type))
    || (isObject(node.attrs) && 'align' in node.attrs)
    || (Array.isArray(node.marks) && node.marks.some((m) => isObject(m) && typeof m.type === 'string' && EXTENDED_MARKS.has(m.type))))
}
