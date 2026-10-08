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
import { Plugin } from '@tiptap/pm/state'
import Document from '@tiptap/extension-document'
import Paragraph from '@tiptap/extension-paragraph'
import Heading from '@tiptap/extension-heading'
import Blockquote from '@tiptap/extension-blockquote'
import Underline from '@tiptap/extension-underline'
import Strike from '@tiptap/extension-strike'
import Code from '@tiptap/extension-code'
import Text from '@tiptap/extension-text'
import HardBreak from '@tiptap/extension-hard-break'
import Bold from '@tiptap/extension-bold'
import Italic from '@tiptap/extension-italic'
import { BulletList, ListItem, OrderedList } from '@tiptap/extension-list'
import { fromContractRichText, toContractRichText, type RichTextDoc } from './toContractRichText'

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

export function richTextExtensions(): Extensions {
  return [
    Document, ContractParagraph, Text, HardBreak, Bold, Italic, Underline, ContractStrike, Code,
    BulletList, OrderedList, ContractListItem, ContractLink,
    ContractHeading, ContractBlockquote, BlockStyleKeys,
  ]
}

export interface RichTextEditorOptions {
  element: HTMLElement
  content: unknown
  /** Extensions only this editor needs: the item field's own undo history (035 RT-14). */
  extensions?: Extensions
  /** Called with the contract document after every change to it. */
  onChange: (doc: RichTextDoc) => void
  onBlur?: () => void
  label: string
}

export function createRichTextEditor(options: RichTextEditorOptions): Editor {
  return new Editor({
    element: options.element,
    extensions: [...richTextExtensions(), ...(options.extensions ?? [])],
    content: fromContractRichText(options.content) as JSONContent,
    editorProps: {
      attributes: { 'aria-label': options.label, 'aria-multiline': 'true', role: 'textbox', class: 'qb-rich-text__body' },
    },
    onUpdate: ({ editor }) => options.onChange(toContractRichText(editor.getJSON())),
    onBlur: () => options.onBlur?.(),
  })
}
