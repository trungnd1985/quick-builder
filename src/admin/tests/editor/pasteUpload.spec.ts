import { afterEach, describe, expect, it, vi } from 'vitest'
import { Editor } from '@tiptap/core'
import type { EditorView } from '@tiptap/pm/view'
import { insertUploads, type FileUploads, type RichTextNotice } from '../../src/editor/richtext/paste'
import { createRichTextEditor } from '../../src/editor/richtext/tiptap'
import { toContractRichText, type RichTextDoc } from '../../src/editor/richtext/toContractRichText'
import { ApiError } from '../../src/api/request'

// 035 US4, FR-026, clarification Q2: pasted or dropped image files are uploaded to the media
// library; a placeholder shows while they upload and never reaches a document.

let editor: Editor | null = null

afterEach(() => {
  editor?.destroy()
  editor = null
  vi.unstubAllGlobals()
})

const png = (name = 'shot.png') => new File([new Uint8Array([137, 80, 78, 71])], name, { type: 'image/png' })

function open(onChange: (doc: RichTextDoc) => void = () => undefined, notices: RichTextNotice[] = []) {
  const element = document.createElement('div')
  document.body.appendChild(element)
  editor = createRichTextEditor({
    element,
    content: { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Shop' }] }] },
    label: 'Body',
    siteId: 'site',
    onChange,
    onNotice: (n) => notices.push(n),
  })
  return editor
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0))
const placeholders = (e: Editor) => JSON.stringify(e.getJSON()).split('"uploadId":"upload-').length - 1

function uploads(upload: FileUploads['upload'], notices: RichTextNotice[], allowImages = true): FileUploads {
  return { siteId: 'site', allowImages, onNotice: (n) => notices.push(n), upload }
}

describe('pasted image files', () => {
  it('shows a placeholder, then the uploaded image without alternative text; the placeholder is never stored', async () => {
    const sent: RichTextDoc[] = []
    const e = open((doc) => sent.push(doc))
    let finish: (file: { id: string }) => void = () => undefined
    const upload = vi.fn(() => new Promise<{ id: string }>((resolve) => { finish = resolve }))

    insertUploads(e.view as EditorView, [png()], null, uploads(upload, []))

    expect(placeholders(e)).toBe(1)
    expect(sent.every((doc) => !JSON.stringify(doc).includes('image'))).toBe(true)

    finish({ id: 'file-9' })
    await settle()

    expect(placeholders(e)).toBe(0)
    expect(toContractRichText(e.getJSON()).content).toContainEqual({ type: 'image', attrs: { media: { mediaId: 'file-9' } } })
    expect(upload).toHaveBeenCalledWith('site', expect.any(File))
  })

  it.each([
    ['a type the library refuses', new ApiError(415, 'x', { errorCode: 'media.unsupported_type' } as never)],
    ['a file too large', new ApiError(413, 'x', { errorCode: 'media.too_large' } as never)],
    ['no permission to upload', new ApiError(403, 'x', { errorCode: 'identity.forbidden' } as never)],
  ])('removes the placeholder and says why for %s, keeping the rest', async (_, error) => {
    const notices: RichTextNotice[] = []
    const e = open()

    insertUploads(e.view as EditorView, [png()], null, uploads(() => Promise.reject(error), notices))
    await settle()

    expect(placeholders(e)).toBe(0)
    expect(e.getText()).toContain('Shop')
    expect(notices).toEqual([{ kind: 'uploadFailed', code: error.errorCode ?? null }])
  })

  it('uploads nothing into a field that does not take images, and says so', () => {
    const notices: RichTextNotice[] = []
    const upload = vi.fn()
    const e = open()

    insertUploads(e.view as EditorView, [png(), png('b.png')], null, uploads(upload, notices, false))

    expect(upload).not.toHaveBeenCalled()
    expect(placeholders(e)).toBe(0)
    expect(notices).toEqual([{ kind: 'imagesLeftOut', count: 2 }])
  })

  it('takes image files on paste and drop, and leaves other files to the browser', () => {
    const e = open()
    const handled = (prop: 'handlePaste' | 'handleDrop', files: File[]) =>
      e.view.someProp(prop, (handler) => prop === 'handlePaste'
        ? (handler as (v: EditorView, ev: ClipboardEvent) => boolean)(e.view as EditorView, { clipboardData: { files, getData: () => '' } } as unknown as ClipboardEvent)
        : (handler as (v: EditorView, ev: DragEvent, s: unknown, m: boolean) => boolean)(e.view as EditorView, { dataTransfer: { files }, clientX: 0, clientY: 0 } as unknown as DragEvent, null, false))
    vi.stubGlobal('XMLHttpRequest', class { open() {} setRequestHeader() {} send() {} upload = {} } as unknown as typeof XMLHttpRequest)

    expect(handled('handlePaste', [new File(['%PDF'], 'a.pdf', { type: 'application/pdf' })])).toBeFalsy()
    expect(handled('handlePaste', [png()])).toBe(true)
    expect(handled('handleDrop', [png()])).toBe(true)
    expect(placeholders(e)).toBe(2)
  })

  it('cleans pasted HTML and says how many web images it left out', () => {
    const notices: RichTextNotice[] = []
    const e = open(undefined, notices)

    const html = e.view.someProp('transformPastedHTML', (transform) => transform('<p>a<img src="https://example.com/x.png"></p>', e.view as EditorView))

    expect(html).toBe('<p>a</p>')
    expect(notices).toEqual([{ kind: 'imagesLeftOut', count: 1 }])
  })
})
