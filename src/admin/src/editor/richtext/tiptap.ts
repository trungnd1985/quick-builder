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

import { Editor, Mark, type Extensions, type JSONContent } from '@tiptap/core'
import Document from '@tiptap/extension-document'
import Paragraph from '@tiptap/extension-paragraph'
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

export function richTextExtensions(): Extensions {
  return [Document, Paragraph, Text, HardBreak, Bold, Italic, BulletList, OrderedList, ListItem, ContractLink]
}

export interface RichTextEditorOptions {
  element: HTMLElement
  content: unknown
  /** Called with the contract document after every change to it. */
  onChange: (doc: RichTextDoc) => void
  onBlur?: () => void
  label: string
}

export function createRichTextEditor(options: RichTextEditorOptions): Editor {
  return new Editor({
    element: options.element,
    extensions: richTextExtensions(),
    content: fromContractRichText(options.content) as JSONContent,
    editorProps: {
      attributes: { 'aria-label': options.label, 'aria-multiline': 'true', role: 'textbox', class: 'qb-rich-text__body' },
    },
    onUpdate: ({ editor }) => options.onChange(toContractRichText(editor.getJSON())),
    onBlur: () => options.onBlur?.(),
  })
}
