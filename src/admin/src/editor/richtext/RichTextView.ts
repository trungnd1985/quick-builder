// Draws stored rich text (docs/contracts/page-schema.md §2a) with Vue render functions.
//
// Never v-html: every text node goes through Vue's escaping, and only the contract's own node
// and mark types produce elements (Principle IX). Links carry no href in the editor — nothing
// on the canvas ever navigates or reaches outside the installation (SC-008).

import { defineComponent, h, type PropType, type VNode } from 'vue'
import { isObject, type Json } from '../schema/subsetValidator'

type Children = (VNode | string)[]

function renderInline(node: Json, key: number): VNode | string | null {
  if (!isObject(node)) return null
  if (node.type === 'hardBreak') return h('br', { key })
  if (node.type !== 'text' || typeof node.text !== 'string') return null

  let content: VNode | string = node.text
  const marks = Array.isArray(node.marks) ? node.marks : []
  for (const mark of marks) {
    if (!isObject(mark)) continue
    if (mark.type === 'bold') content = h('strong', null, [content])
    else if (mark.type === 'italic') content = h('em', null, [content])
    else if (mark.type === 'link') content = h('a', { class: 'qb-link' }, [content])
  }
  return typeof content === 'string' ? h('span', { key }, content) : h(content, { key })
}

function renderBlock(node: Json, key: number): VNode | null {
  if (!isObject(node)) return null
  const content = Array.isArray(node.content) ? node.content : []

  switch (node.type) {
    case 'paragraph':
      return h('p', { key }, content.map(renderInline).filter((c): c is VNode | string => c !== null) as Children)
    case 'bulletList':
      return h('ul', { key }, content.map(renderBlock).filter((c): c is VNode => c !== null))
    case 'orderedList': {
      const start = isObject(node.attrs) && typeof node.attrs.start === 'number' ? node.attrs.start : undefined
      return h('ol', { key, start }, content.map(renderBlock).filter((c): c is VNode => c !== null))
    }
    case 'listItem':
      return h('li', { key }, content.map(renderBlock).filter((c): c is VNode => c !== null))
    default:
      return null
  }
}

export default defineComponent({
  name: 'RichTextView',
  props: {
    doc: { type: [Object, null] as PropType<Json>, default: null },
  },
  setup(props) {
    return () => {
      const content = isObject(props.doc) && Array.isArray(props.doc.content) ? props.doc.content : []
      return h('div', { class: 'qb-rich-text__body' }, content.map(renderBlock).filter((c): c is VNode => c !== null))
    }
  },
})
