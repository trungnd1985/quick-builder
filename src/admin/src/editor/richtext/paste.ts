// Pasted content, made ready for the editor's schema (035 US4, FR-012, research RT-08).
//
// ProseMirror parses pasted HTML with the editor's schema, so anything without a rule (colours,
// fonts, classes, scripts) is dropped there and never stored. This step does only what parsing
// cannot: it leaves out images that exist only as addresses — pasted files are uploaded instead
// (clarification Q2) — and counts them for a notice; it splits merged table cells, which the
// contract does not have; it turns Word for the web's heading paragraphs into headings; and it
// removes Word's list bullets, which arrive as text. The HTML is parsed into an inert document,
// never attached, so nothing in it runs or loads.

import type { Node as ProseMirrorNode } from '@tiptap/pm/model'
import type { Transaction } from '@tiptap/pm/state'
import type { EditorView } from '@tiptap/pm/view'

export interface PreparedPaste {
  html: string
  /** Images left out because they exist only as addresses. */
  imagesLeftOut: number
}

const NEVER_CONTENT = 'script, style, iframe, object, embed, noscript, template, link, meta, title, svg, canvas, video, audio, picture source'

export function preparePastedHtml(html: string): PreparedPaste {
  const document = new DOMParser().parseFromString(html, 'text/html')
  const body = document.body

  body.querySelectorAll(NEVER_CONTENT).forEach((element) => element.remove())

  // Images: never fetched; the member adds them from the library, or pastes the file itself.
  const images = body.querySelectorAll('img, picture')
  const emptied = new Set<Element>()
  let imagesLeftOut = 0
  images.forEach((image) => {
    if (image.tagName === 'IMG') imagesLeftOut++
    if (image.parentElement) emptied.add(image.parentElement)
    image.remove()
  })

  // A paragraph that held only an image goes with it, rather than staying as an empty line.
  for (const parent of emptied) {
    let element: Element | null = parent
    while (element && element !== body && element.isConnected && isEmptyElement(element)) {
      const next: Element | null = element.parentElement
      element.remove()
      element = next
    }
  }

  // Browsers add this line break after a copied selection; it is not the member's content.
  body.querySelectorAll('br.Apple-interchange-newline').forEach((element) => element.remove())

  // Word desktop marks list items with a bullet in a span it hides from itself.
  body.querySelectorAll('[style*="mso-list:Ignore" i], [style*="mso-list: Ignore" i]').forEach((element) => element.remove())

  // Word for the web writes headings as paragraphs with a heading role.
  body.querySelectorAll('[role="heading"][aria-level]').forEach((element) => {
    const level = Number(element.getAttribute('aria-level'))
    if (!Number.isInteger(level) || level < 1 || level > 6) return
    const heading = document.createElement(`h${level}`)
    heading.append(...element.childNodes)
    element.replaceWith(heading)
  })

  body.querySelectorAll('table').forEach(splitMergedCells)

  return { html: body.innerHTML, imagesLeftOut }
}

/** No text, and nothing that stands for content without text (a break, a rule, a table). */
const isEmptyElement = (element: Element): boolean =>
  (element.textContent ?? '').trim() === '' && element.querySelector('br, hr, table, ul, ol') === null

/**
 * A table without spans (US4 scenario 5): each merged cell keeps its content in its first
 * position, and empty cells take the rest, so every row has as many cells as the grid is wide.
 */
function splitMergedCells(table: HTMLTableElement): void {
  const pending = new Map<number, { rows: number; tag: string }>()

  for (const row of Array.from(table.rows)) {
    const cells = Array.from(row.cells)
    const rebuilt: HTMLTableCellElement[] = []
    let column = 0

    const fillPending = (): void => {
      let next = pending.get(column)
      while (next && next.rows > 0) {
        rebuilt.push(row.ownerDocument.createElement(next.tag) as HTMLTableCellElement)
        next.rows--
        if (next.rows === 0) pending.delete(column)
        column++
        next = pending.get(column)
      }
    }

    for (const cell of cells) {
      fillPending()
      const span = Math.max(1, cell.colSpan || 1)
      const rows = Math.max(1, cell.rowSpan || 1)
      cell.removeAttribute('colspan')
      cell.removeAttribute('rowspan')
      rebuilt.push(cell)
      for (let extra = 1; extra < span; extra++) rebuilt.push(row.ownerDocument.createElement(cell.tagName.toLowerCase()) as HTMLTableCellElement)
      if (rows > 1) {
        for (let k = 0; k < span; k++) pending.set(column + k, { rows: rows - 1, tag: 'td' })
      }
      column += span
    }
    fillPending()

    row.replaceChildren(...rebuilt)
  }
}

/** What the member is told after a paste or drop (035 FR-012, FR-026). */
export type RichTextNotice =
  | { kind: 'imagesLeftOut'; count: number }
  | { kind: 'uploadFailed'; code: string | null }

export interface FileUploads {
  siteId: string
  /** Whether the field takes images (035 FR-022); a Rich Text block always does. */
  allowImages: boolean
  onNotice: (notice: RichTextNotice) => void
  /** Uploads one file to the site's library, at its top level. */
  upload: (siteId: string, file: File) => Promise<{ id: string }>
}

const IMAGE_TYPES: ReadonlySet<string> = new Set(['image/jpeg', 'image/png', 'image/gif', 'image/webp', 'image/avif'])

/** The image files among pasted or dropped files; anything else is not the editor's to take. */
export const imageFiles = (files: FileList | null | undefined): File[] =>
  Array.from(files ?? []).filter((file) => IMAGE_TYPES.has(file.type))

let uploads = 0

/**
 * Pasted or dropped image files (035 FR-026): each shows a placeholder where it lands, is
 * uploaded through the library's usual checks, and then becomes an image without alternative
 * text, so publishing asks for it. A refused upload removes its placeholder and says why. The
 * placeholder names no file, so no document ever stores it (toContractRichText).
 */
export function insertUploads(view: EditorView, files: File[], at: number | null, options: FileUploads): void {
  if (!options.allowImages) {
    options.onNotice({ kind: 'imagesLeftOut', count: files.length })
    return
  }

  const type = view.state.schema.nodes.image
  if (!type) return

  for (const file of files) {
    const uploadId = `upload-${++uploads}`
    const node = type.create({ uploadId })
    const tr = view.state.tr
    if (at === null) tr.replaceSelectionWith(node)
    else tr.insert(Math.min(at, tr.doc.content.size), node)
    view.dispatch(tr)

    options.upload(options.siteId, file).then(
      (stored) => replacePlaceholder(view, uploadId, (tr, pos, placeholder) =>
        tr.setNodeMarkup(pos, undefined, { ...placeholder.attrs, media: { mediaId: stored.id }, alt: null, uploadId: null })),
      (caught: unknown) => {
        replacePlaceholder(view, uploadId, (tr, pos, placeholder) => tr.delete(pos, pos + placeholder.nodeSize))
        const code = typeof caught === 'object' && caught !== null && 'errorCode' in caught ? (caught as { errorCode?: string }).errorCode ?? null : null
        options.onNotice({ kind: 'uploadFailed', code })
      },
    )
  }
}

function replacePlaceholder(
  view: EditorView,
  uploadId: string,
  change: (tr: Transaction, pos: number, placeholder: ProseMirrorNode) => Transaction,
): void {
  if (view.isDestroyed) return
  let found: { pos: number; node: ProseMirrorNode } | null = null
  view.state.doc.descendants((node, pos) => {
    if (found) return false
    if (node.type.name === 'image' && node.attrs.uploadId === uploadId) found = { pos, node }
    return !found
  })
  if (!found) return
  const { pos, node } = found as { pos: number; node: ProseMirrorNode }
  view.dispatch(change(view.state.tr, pos, node))
}
