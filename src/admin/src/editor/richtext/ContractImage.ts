// A rich text image (035 US3, research RT-06, RT-17): a media file of the site, with its
// alternative text or marked decorative. Only `media`, `alt` and `decorative` reach a document
// (toContractRichText); `uploadId` marks an image whose file is still uploading (RT-08).
//
// The node parses only `img[data-qb-media]`, which this editor writes when copying, so a
// pasted web image never becomes one (clarification Q2). It is drawn by a plain DOM node view:
// the canvas's and the item form's editors are not mounted through EditorContent, which a Vue
// node view would need.

import { Node } from '@tiptap/core'
import type { Node as ProseMirrorNode } from '@tiptap/pm/model'
import { mediaApi } from '../../api/media'
import { defaultSource, mediaDetails } from '../blocks/image/mediaDetails'

export interface ContractImageLabels {
  /** Shown in place of an image whose file is gone. */
  missing: string
  /** Shown while a pasted file uploads. */
  uploading: string
  /** The marker on an image that needs alternative text before publishing (FR-014). */
  addAlt: string
}

export interface ContractImageOptions {
  siteId: string
  labels: ContractImageLabels
}

interface MediaValue {
  mediaId: string
}

const mediaOf = (value: unknown): MediaValue | null =>
  typeof value === 'object' && value !== null && typeof (value as { mediaId?: unknown }).mediaId === 'string'
    ? { mediaId: (value as MediaValue).mediaId }
    : null

function parseMedia(element: HTMLElement): MediaValue | null {
  try {
    return mediaOf(JSON.parse(element.getAttribute('data-qb-media') ?? 'null'))
  } catch {
    return null
  }
}

/** Whether publishing would refuse this image: no alternative text, and not decorative. */
export const needsAlt = (attrs: Record<string, unknown>): boolean =>
  attrs.decorative !== true && (typeof attrs.alt !== 'string' || attrs.alt.trim() === '')

export const ContractImage = Node.create<ContractImageOptions>({
  name: 'image',
  group: 'block',
  atom: true,
  selectable: true,
  draggable: true,

  addOptions() {
    return { siteId: '', labels: { missing: 'Missing image', uploading: 'Uploading…', addAlt: 'Add alternative text' } }
  },

  addAttributes() {
    return {
      media: {
        default: null,
        parseHTML: (element) => parseMedia(element),
        renderHTML: (attributes) => (attributes.media ? { 'data-qb-media': JSON.stringify(attributes.media) } : {}),
      },
      alt: {
        default: null,
        parseHTML: (element) => element.getAttribute('alt'),
        renderHTML: (attributes) => (typeof attributes.alt === 'string' ? { alt: attributes.alt } : {}),
      },
      decorative: {
        default: false,
        parseHTML: (element) => element.hasAttribute('data-qb-decorative'),
        renderHTML: (attributes) => (attributes.decorative ? { 'data-qb-decorative': '' } : {}),
      },
      uploadId: { default: null, rendered: false },
    }
  },

  parseHTML() {
    return [{ tag: 'img[data-qb-media]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['img', HTMLAttributes]
  },

  addNodeView() {
    const { siteId, labels } = this.options
    return ({ node }) => {
      const dom = document.createElement('figure')
      dom.className = 'qb-rt-image'
      dom.contentEditable = 'false'
      let current: ProseMirrorNode = node
      let shownId: string | null = null

      const draw = (): void => {
        const media = mediaOf(current.attrs.media)
        const decorative = current.attrs.decorative === true
        const alt = decorative ? '' : (typeof current.attrs.alt === 'string' ? current.attrs.alt : '')
        dom.classList.toggle('qb-editor-rt-image--needs-alt', !current.attrs.uploadId && needsAlt(current.attrs))

        if (!media) {
          shownId = null
          dom.replaceChildren(placeholder(current.attrs.uploadId ? labels.uploading : labels.missing))
          return
        }

        const marker = needsAlt(current.attrs) ? [badge(labels.addAlt)] : []
        if (shownId === media.mediaId) {
          const image = dom.querySelector('img')
          if (image) image.alt = alt
          dom.querySelector('.qb-editor-rt-image__alt')?.remove()
          dom.append(...marker)
          return
        }

        shownId = media.mediaId
        const image = document.createElement('img')
        image.alt = alt
        image.draggable = false
        image.src = mediaApi.previewUrl(siteId, media.mediaId)
        image.addEventListener('error', () => dom.replaceChildren(placeholder(labels.missing), ...marker))
        dom.replaceChildren(image, ...marker)

        // The canvas draws images as a published page does: the largest copy up to 1,280 pixels.
        void mediaDetails(siteId, media.mediaId).then((file) => {
          if (shownId !== media.mediaId) return
          if (file === null) dom.replaceChildren(placeholder(labels.missing), ...(needsAlt(current.attrs) ? [badge(labels.addAlt)] : []))
          else image.src = defaultSource(file) ?? image.src
        })
      }

      draw()

      return {
        dom,
        update: (next) => {
          if (next.type !== current.type) return false
          current = next
          draw()
          return true
        },
        selectNode: () => dom.classList.add('qb-editor-rt-image--selected'),
        deselectNode: () => dom.classList.remove('qb-editor-rt-image--selected'),
        ignoreMutation: () => true,
      }
    }
  },
})

function placeholder(text: string): HTMLElement {
  const element = document.createElement('div')
  element.className = 'qb-editor-placeholder'
  element.textContent = text
  return element
}

function badge(text: string): HTMLElement {
  const element = document.createElement('span')
  element.className = 'qb-editor-rt-image__alt'
  element.textContent = text
  return element
}
