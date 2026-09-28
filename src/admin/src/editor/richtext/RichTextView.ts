// Draws stored rich text (docs/contracts/page-schema.md §2a) with Vue render functions.
//
// Never v-html: every text node goes through Vue's escaping, and only the contract's own node
// and mark types produce elements (Principle IX). Links carry no href in the editor — nothing
// on the canvas ever navigates or reaches outside the installation (SC-008). A link to a page
// that no longer exists is marked, with the given label as its title (spec Edge Cases).

import { defineComponent, h, type PropType, type VNode } from 'vue'
import { isObject, type Json } from '../schema/subsetValidator'

type Children = (VNode | string)[]

/** How links are drawn: which are broken, and what to call them. */
interface LinkMarking {
  isBroken: (to: Json | undefined) => boolean
  label: string
}

const NO_MARKING: LinkMarking = { isBroken: () => false, label: '' }

function renderInline(node: Json, key: number, links: LinkMarking): VNode | string | null {
  if (!isObject(node)) return null
  if (node.type === 'hardBreak') return h('br', { key })
  if (node.type !== 'text' || typeof node.text !== 'string') return null

  let content: VNode | string = node.text
  const marks = Array.isArray(node.marks) ? node.marks : []
  for (const mark of marks) {
    if (!isObject(mark)) continue
    if (mark.type === 'bold') content = h('strong', null, [content])
    else if (mark.type === 'italic') content = h('em', null, [content])
    else if (mark.type === 'link') {
      const to = isObject(mark.attrs) ? mark.attrs.to : undefined
      content = links.isBroken(to)
        ? h('span', null, [
            h('a', { class: 'qb-link qb-editor-broken-link-text', title: links.label, 'data-broken-link': '' }, [content]),
            h('span', { class: 'qb-editor-broken-link' }, links.label),
          ])
        : h('a', { class: 'qb-link' }, [content])
    }
  }
  return typeof content === 'string' ? h('span', { key }, content) : h(content, { key })
}

function renderBlock(node: Json, key: number, links: LinkMarking): VNode | null {
  if (!isObject(node)) return null
  const content = Array.isArray(node.content) ? node.content : []

  switch (node.type) {
    case 'paragraph':
      return h('p', { key }, content.map((c, i) => renderInline(c, i, links)).filter((c): c is VNode | string => c !== null) as Children)
    case 'bulletList':
      return h('ul', { key }, content.map((c, i) => renderBlock(c, i, links)).filter((c): c is VNode => c !== null))
    case 'orderedList': {
      const start = isObject(node.attrs) && typeof node.attrs.start === 'number' ? node.attrs.start : undefined
      return h('ol', { key, start }, content.map((c, i) => renderBlock(c, i, links)).filter((c): c is VNode => c !== null))
    }
    case 'listItem':
      return h('li', { key }, content.map((c, i) => renderBlock(c, i, links)).filter((c): c is VNode => c !== null))
    default:
      return null
  }
}

export default defineComponent({
  name: 'RichTextView',
  props: {
    doc: { type: [Object, null] as PropType<Json>, default: null },
    links: { type: Object as PropType<LinkMarking>, default: () => NO_MARKING },
  },
  setup(props) {
    return () => {
      const links = props.links
      const content = isObject(props.doc) && Array.isArray(props.doc.content) ? props.doc.content : []
      return h('div', { class: 'qb-rich-text__body' }, content.map((c, i) => renderBlock(c, i, links)).filter((c): c is VNode => c !== null))
    }
  },
})
