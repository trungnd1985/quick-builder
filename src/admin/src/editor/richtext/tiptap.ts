// TipTap, configured with exactly the contract's rich text (011 research ED-03, FR-013):
// paragraphs, hard breaks, bold, italic, links and lists — nothing else can be typed or
// pasted, because the editor's schema has nothing else to parse it into.
//
// No history extension: undo is the editor's own, so the rich text shares one history with
// every other change (research ED-12).
//
// Links carry the contract's `to` — a page by id or an address — and no href. So a link in
// the editor never navigates and never points outside the installation (SC-008); the
// renderer (#12) turns `to` into an address.

import { Editor, Extension, Mark, type Extensions, type JSONContent } from '@tiptap/core'
import type { Node as ProseMirrorNode } from '@tiptap/pm/model'
import { Plugin } from '@tiptap/pm/state'
import Document from '@tiptap/extension-document'
import Paragraph from '@tiptap/extension-paragraph'
import Heading from '@tiptap/extension-heading'
import Blockquote from '@tiptap/extension-blockquote'
import Underline from '@tiptap/extension-underline'
import Strike from '@tiptap/extension-strike'
import Code from '@tiptap/extension-code'
import TextAlign from '@tiptap/extension-text-align'
import Text from '@tiptap/extension-text'
import HardBreak from '@tiptap/extension-hard-break'
import Bold from '@tiptap/extension-bold'
import Italic from '@tiptap/extension-italic'
import { BulletList, ListItem, OrderedList } from '@tiptap/extension-list'
import { fromContractRichText, toContractRichText, type RichTextDoc } from './toContractRichText'
import { ContractImage, type ContractImageLabels } from './ContractImage'
import { ContractTable, ContractTableCell, ContractTableHeader, ContractTableRow } from './ContractTable'
import { mediaApi } from '../../api/media'
import { imageFiles, insertUploads, preparePastedHtml, type FileUploads, type RichTextNotice } from './paste'
import { ALL_FORMATS, type AllowedFormats } from './formats'
import { MAX_RICH_TEXT_CHARS } from '../schema/limits'
import { codePointLength } from '../schema/subsetValidator'

/** The link mark: a `to` attribute, drawn as an anchor with no address. */
export const ContractLink = Mark.create({
  name: 'link',
  priority: 1000,
  keepOnSplit: false,
  exitable: true,
  inclusive: false,

  addAttributes() {
    return {
      to: {
        default: null,
        parseHTML: (element) => {
          const raw = element.getAttribute('data-qb-to')
          try {
            return raw ? JSON.parse(raw) : null
          } catch {
            return null
          }
        },
        renderHTML: (attributes) => (attributes.to ? { 'data-qb-to': JSON.stringify(attributes.to) } : {}),
      },
    }
  },

  // Only links made by this editor come back as links; pasted anchors become plain text.
  parseHTML() {
    return [{ tag: 'a[data-qb-to]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['a', { ...HTMLAttributes, class: 'qb-link' }, 0]
  },
})

/** What list items and quotes may hold (`rtListBlock`, 035 research RT-01). */
const LIST_BLOCKS = '(paragraph|bulletList|orderedList)'

/**
 * Headings of levels 2 to 4 (035 US1). Pasted `h1` reads as level 2 and `h5`, `h6` as 4 (US4
 * scenario 1). The shortcuts are BlockStyleKeys', so AltGr typing is never taken for one.
 */
export const ContractHeading = Heading.extend({
  parseHTML() {
    return [
      { tag: 'h1', attrs: { level: 2 } },
      { tag: 'h2', attrs: { level: 2 } },
      { tag: 'h3', attrs: { level: 3 } },
      { tag: 'h4', attrs: { level: 4 } },
      { tag: 'h5', attrs: { level: 4 } },
      { tag: 'h6', attrs: { level: 4 } },
    ]
  },
  addKeyboardShortcuts() {
    return {}
  },
}).configure({ levels: [2, 3, 4] })

const ContractParagraph = Paragraph.extend({
  addKeyboardShortcuts() {
    return {}
  },
})

const ContractBlockquote = Blockquote.extend({ content: `${LIST_BLOCKS}+` })

const ContractListItem = ListItem.extend({ content: `paragraph ${LIST_BLOCKS}*` })

/** Strikethrough on Ctrl/Cmd+Shift+X (035 FR-008), not TipTap's Ctrl/Cmd+Shift+S. */
const ContractStrike = Strike.extend({
  addKeyboardShortcuts() {
    return { 'Mod-Shift-x': () => this.editor.commands.toggleStrike() }
  },
})

/**
 * Ctrl/Cmd+Alt+2, 3 and 4 for headings, and 0 for a paragraph (035 FR-008). On Windows, Ctrl+Alt
 * is AltGr, and on some layouts AltGr+2 types a character: the key pressed must be the digit
 * itself, so that typing wins (research RT-07). On macOS, Option changes `key`, so the
 * physical key is read instead; there is no AltGr there.
 */
const BlockStyleKeys = Extension.create({
  name: 'blockStyleKeys',
  addProseMirrorPlugins() {
    const editor = this.editor
    return [
      new Plugin({
        props: {
          handleKeyDown: (_view, event) => {
            if (!(event.ctrlKey || event.metaKey) || !event.altKey || event.shiftKey) return false
            const digit = event.metaKey ? /^Digit(d)$/.exec(event.code)?.[1] : event.key
            if (digit === '0') return editor.commands.setParagraph()
            if (digit === '2' || digit === '3' || digit === '4') return editor.commands.toggleHeading({ level: Number(digit) as 2 | 3 | 4 })
            return false
          },
        },
      }),
    ]
  },
})

/** What the image node needs from its host: the site, and its words in the member's language. */
export interface RichTextSetup {
  siteId?: string
  imageLabels?: ContractImageLabels
  /** Told what a paste, a drop or a table action left out or refused (035 FR-012, FR-026). */
  onNotice?: (notice: RichTextNotice) => void
  /** The formats this rich text may hold (035 FR-022); every format by default. */
  allowed?: AllowedFormats
}

/**
 * A field that does not allow a format group (035 FR-022) gets no more of it, whichever way it
 * would come in — a shortcut, typing "## ", a drop: a change that adds such a node is refused.
 * What the field held before its formats were narrowed stays until the member removes it.
 */
function allowedFormatsGuard(allowed: AllowedFormats): Extension {
  const disallowed = (name: string): boolean =>
    ((name === 'heading' || name === 'blockquote') && !allowed.headings)
    || (name === 'image' && !allowed.images)
    || (name === 'table' && !allowed.tables)
  const count = (doc: ProseMirrorNode): number => {
    let n = 0
    doc.descendants((node) => {
      if (disallowed(node.type.name)) n++
    })
    return n
  }
  return Extension.create({
    name: 'allowedFormatsGuard',
    addProseMirrorPlugins: () => [new Plugin({ filterTransaction: (tr, state) => !tr.docChanged || count(tr.doc) <= count(state.doc) })],
  })
}

/** Characters of text, counted as the contract counts them: by code point. */
function characters(doc: ProseMirrorNode): number {
  let n = 0
  doc.descendants((node) => {
    if (node.isText) n += codePointLength(node.text ?? '')
  })
  return n
}

/**
 * A rich text holds at most 20,000 characters (page-schema.md §3 rule 9): a paste or typing that
 * would go past it is refused whole, and said so, rather than leaving a value that can't be saved
 * (035 FR-005). A text already over the limit can still be shortened.
 */
function lengthGuard(notify: (notice: RichTextNotice) => void): Extension {
  return Extension.create({
    name: 'lengthGuard',
    addProseMirrorPlugins: () => [new Plugin({
      filterTransaction: (tr, state) => {
        if (!tr.docChanged) return true
        const after = characters(tr.doc)
        if (after <= MAX_RICH_TEXT_CHARS || after <= characters(state.doc)) return true
        notify({ kind: 'limit', limit: 'characters' })
        return false
      },
    })],
  })
}

export function richTextExtensions(setup: RichTextSetup = {}): Extensions {
  const image = setup.imageLabels
    ? ContractImage.configure({ siteId: setup.siteId ?? '', labels: setup.imageLabels })
    : ContractImage.configure({ siteId: setup.siteId ?? '' })
  return [
    Document, ContractParagraph, Text, HardBreak, Bold, Italic, Underline, ContractStrike, Code,
    BulletList, OrderedList, ContractListItem, ContractLink,
    ContractHeading, ContractBlockquote, BlockStyleKeys, image,
    ContractTable.configure({ onNotice: setup.onNotice }), ContractTableRow, ContractTableHeader, ContractTableCell,
    // Alignment on paragraphs and headings (035 US6); none means the block's own.
    TextAlign.configure({ types: ['heading', 'paragraph'], alignments: ['start', 'center', 'end'], defaultAlignment: null }),
    ...(setup.allowed && !(setup.allowed.headings && setup.allowed.images && setup.allowed.tables) ? [allowedFormatsGuard(setup.allowed)] : []),
  ]
}

export interface RichTextEditorOptions extends RichTextSetup {
  element: HTMLElement
  content: unknown
  /** Extensions only this editor needs: the item field's own undo history (035 RT-14). */
  extensions?: Extensions
  /** Called with the contract document after every change to it. */
  onChange: (doc: RichTextDoc) => void
  onBlur?: () => void
  label: string
}

/** Where a drop lands; the cursor when the point can't be resolved. */
function dropPosition(view: { posAtCoords: (coords: { left: number; top: number }) => { pos: number } | null }, event: DragEvent): number | null {
  try {
    return view.posAtCoords({ left: event.clientX, top: event.clientY })?.pos ?? null
  } catch {
    return null
  }
}

export function createRichTextEditor(options: RichTextEditorOptions): Editor {
  const notify = (notice: RichTextNotice): void => options.onNotice?.(notice)
  const uploads: FileUploads = {
    siteId: options.siteId ?? '',
    allowImages: (options.allowed ?? ALL_FORMATS).images,
    onNotice: notify,
    upload: (siteId, file) => mediaApi.upload(siteId, file, () => undefined),
  }

  return new Editor({
    element: options.element,
    extensions: [...richTextExtensions(options), lengthGuard(notify), ...(options.extensions ?? [])],
    content: fromContractRichText(options.content) as JSONContent,
    editorProps: {
      attributes: { 'aria-label': options.label, 'aria-multiline': 'true', role: 'textbox', class: 'qb-rich-text__body' },
      // 035 US4: pasted HTML loses its address-only images and merged cells before parsing.
      transformPastedHTML: (html) => {
        const prepared = preparePastedHtml(html, options.allowed ?? ALL_FORMATS)
        if (prepared.imagesLeftOut > 0) notify({ kind: 'imagesLeftOut', count: prepared.imagesLeftOut })
        return prepared.html
      },
      // Pasted and dropped image files are uploaded to the library (clarification Q2).
      handlePaste: (view, event) => {
        const files = imageFiles(event.clipboardData?.files)
        if (files.length === 0) return false
        insertUploads(view, files, null, uploads)
        return true
      },
      handleDrop: (view, event, _slice, moved) => {
        if (moved) return false
        const files = imageFiles(event.dataTransfer?.files)
        if (files.length === 0) return false
        insertUploads(view, files, dropPosition(view, event), uploads)
        return true
      },
    },
    onUpdate: ({ editor }) => options.onChange(toContractRichText(editor.getJSON())),
    onBlur: () => options.onBlur?.(),
  })
}
