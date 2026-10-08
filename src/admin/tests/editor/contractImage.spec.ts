import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, markRaw } from 'vue'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import axe from 'axe-core'
import { Editor } from '@tiptap/core'
import { NodeSelection } from '@tiptap/pm/state'
import en from '../../src/i18n/en.json'
import RichTextToolbar from '../../src/editor/richtext/RichTextToolbar.vue'
import { richTextExtensions } from '../../src/editor/richtext/tiptap'
import { toContractRichText } from '../../src/editor/richtext/toContractRichText'
import type { AllowedFormats } from '../../src/editor/richtext/formats'
import { i18n } from './harness'

// Images from the media library in rich text (035 US3, FR-010, FR-014, FR-017, research RT-17).
// The library's own dialog is replaced by a stand-in that hands back one file.

vi.mock('../../src/features/media/MediaPickerDialog.vue', () => ({
  default: defineComponent({
    name: 'MediaPickerDialog',
    emits: ['choose', 'close'],
    setup: (_, { emit }) => () => h('div', { role: 'dialog', 'aria-label': 'Media library' }, [
      h('button', { type: 'button', onClick: () => emit('choose', { id: 'file-2', altText: 'From the library' }) }, 'Choose file-2'),
    ]),
  }),
}))

const labels = { missing: en.editor.richText.image.missing, uploading: en.editor.richText.image.uploading, addAlt: en.editor.richText.image.addAlt }
let editor: Editor | null = null

beforeEach(() => {
  // The library knows file-1 and file-2; anything else is gone.
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const known = /files\/(file-[12])$/.exec(String(url))
    return known
      ? new Response(JSON.stringify({ id: known[1], contentType: 'image/png', variants: [], available: true }), { status: 200, headers: { 'content-type': 'application/json' } })
      : new Response('{"status":404,"errorCode":"media.not_found"}', { status: 404, headers: { 'content-type': 'application/problem+json' } })
  }))
})

afterEach(() => {
  editor?.destroy()
  editor = null
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

const image = (mediaId: string, attrs: Record<string, unknown> = {}) => ({ type: 'image', attrs: { media: { mediaId }, ...attrs } })

function mount(content: unknown[] = [{ type: 'paragraph', content: [{ type: 'text', text: 'Shop' }] }], allowed?: AllowedFormats) {
  const element = document.createElement('div')
  document.body.appendChild(element)
  editor = markRaw(new Editor({ element, extensions: richTextExtensions({ siteId: 'site', imageLabels: labels }), content: { type: 'doc', content } }))
  return { ...render(RichTextToolbar, { props: { editor: editor!, siteId: 'site', allowed }, global: { plugins: [i18n] } }), element }
}

const select = (pos: number) => editor!.view.dispatch(editor!.state.tr.setSelection(NodeSelection.create(editor!.state.doc, pos)))
const images = () => toContractRichText(editor!.getJSON()).content.filter((b) => (b as { type: string }).type === 'image')

describe('rich text images', () => {
  it('inserts an image chosen from the library, with the library\'s alternative text', async () => {
    mount()

    await fireEvent.click(screen.getByRole('button', { name: en.editor.richText.image.insert }))
    await fireEvent.click(screen.getByRole('button', { name: 'Choose file-2' }))

    expect(images()).toEqual([image('file-2', { alt: 'From the library' })])
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('edits the selected image: alternative text, decorative, replace and remove', async () => {
    const { container } = mount([image('file-1', { alt: 'Front' }), { type: 'paragraph' }])
    select(0)

    const alt = await screen.findByLabelText(en.editor.richText.image.alt) as HTMLInputElement
    expect(alt.value).toBe('Front')
    await fireEvent.update(alt, 'The shop front')
    expect(images()).toEqual([image('file-1', { alt: 'The shop front' })])

    await fireEvent.click(screen.getByLabelText(en.editor.richText.image.decorative))
    expect(images()).toEqual([image('file-1', { alt: 'The shop front', decorative: true })])
    expect(alt.disabled).toBe(true)
    await fireEvent.click(screen.getByLabelText(en.editor.richText.image.decorative))

    await fireEvent.click(screen.getByRole('button', { name: en.editor.richText.image.replace }))
    await fireEvent.click(screen.getByRole('button', { name: 'Choose file-2' }))
    expect(images()).toEqual([image('file-2', { alt: 'The shop front' })])

    select(0)
    expect((await axe.run(container)).violations).toEqual([])
    await fireEvent.click(await screen.findByRole('button', { name: en.editor.richText.image.remove }))
    expect(images()).toEqual([])
  })

  it('shows the image settings only while an image is selected', async () => {
    mount([{ type: 'paragraph', content: [{ type: 'text', text: 'Shop' }] }, image('file-1', { alt: 'Front' })])
    expect(screen.queryByLabelText(en.editor.richText.image.alt)).toBeNull()

    select(6)
    expect(await screen.findByLabelText(en.editor.richText.image.alt)).toBeTruthy()
  })

  it('marks an image that needs alternative text, and a file that is gone', async () => {
    const { element } = mount([image('file-1'), image('gone')])
    const figures = element.querySelectorAll('figure.qb-rt-image')

    expect(figures[0]!.querySelector('.qb-editor-rt-image__alt')?.textContent).toBe(en.editor.richText.image.addAlt)
    await waitFor(() => expect(figures[1]!.textContent).toContain(en.editor.richText.image.missing))

    editor!.commands.command(({ tr }) => {
      tr.setNodeMarkup(0, undefined, { media: { mediaId: 'file-1' }, alt: 'Front', decorative: false, uploadId: null })
      return true
    })
    expect(figures[0]!.querySelector('.qb-editor-rt-image__alt')).toBeNull()
  })

  it('offers no image button when the field does not allow images', () => {
    mount(undefined, { headings: true, images: false, tables: true })

    expect(screen.queryByRole('button', { name: en.editor.richText.image.insert })).toBeNull()
  })
})

describe('toContractRichText: images', () => {
  it('keeps media, alternative text and decorative, and drops anything else', () => {
    expect(toContractRichText({ type: 'doc', content: [
      { type: 'image', attrs: { media: { mediaId: 'm', extra: 1 }, alt: 'A', decorative: false, uploadId: null, src: 'x' } },
      { type: 'image', attrs: { media: { mediaId: 'm' }, alt: '', decorative: true } },
    ] }).content).toEqual([image('m', { alt: 'A' }), image('m', { decorative: true })])
  })

  it('drops an image still uploading, and an image inside a list or quote', () => {
    const contract = toContractRichText({ type: 'doc', content: [
      { type: 'image', attrs: { media: null, uploadId: 'u1' } },
      { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph' }, image('m', { alt: 'A' })] }] },
      { type: 'blockquote', content: [image('m', { alt: 'A' })] },
    ] })

    expect(JSON.stringify(contract)).not.toContain('image')
  })

  it('never reads a pasted web image as one', () => {
    editor = new Editor({ extensions: richTextExtensions(), content: '<p>a</p><img src="https://example.com/x.png" alt="x"><img data-qb-media="{&quot;mediaId&quot;:&quot;m&quot;}" alt="ours">' })

    expect(toContractRichText(editor.getJSON()).content).toEqual([{ type: 'paragraph', content: [{ type: 'text', text: 'a' }] }, image('m', { alt: 'ours' })])
  })
})
