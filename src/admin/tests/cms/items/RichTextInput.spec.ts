import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import type { Editor } from '@tiptap/core'
import { closeHistory } from '@tiptap/pm/history'
import RichTextInput from '../../../src/features/cms/items/inputs/RichTextInput.vue'
import type { SchemaField } from '../../../src/api/cms'
import { en, i18n, SITE } from './itemHarness'

// An item rich text field has its own undo history, with buttons and the usual shortcuts
// (035 US2, FR-009, research RT-14).

const field: SchemaField = { id: 'f20', key: 'body', name: 'Body', help: '', required: false, type: 'richText' }

const doc = (text: string) => ({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text }] }] })

async function mount(value: unknown = doc('Start')) {
  const view = render(RichTextInput, {
    props: { field, value, id: 'f20-input', describedBy: 'f20-help', invalid: false, siteId: SITE, label: 'Body' },
    global: { plugins: [i18n] },
  })
  await screen.findByRole('button', { name: en.editor.richText.undo })
  const textbox = view.container.querySelector<HTMLElement & { editor: Editor }>('[role="textbox"]')!
  return { ...view, textbox, editor: textbox.editor }
}

/** One change in its own undo step, as separate bursts of typing are. */
const type = (editor: Editor, text: string) =>
  editor.chain().command(({ tr }) => {
    closeHistory(tr)
    return true
  }).insertContentAt(editor.state.doc.content.size - 1, text).run()

const textOf = (editor: Editor) => editor.state.doc.textContent
const undoButton = () => screen.getByRole('button', { name: en.editor.richText.undo }) as HTMLButtonElement
const redoButton = () => screen.getByRole('button', { name: en.editor.richText.redo }) as HTMLButtonElement

describe('RichTextInput history', () => {
  it('undoes and redoes the field\'s changes in order, by button and by shortcut', async () => {
    const { editor, textbox } = await mount()
    expect(undoButton().disabled).toBe(true)
    expect(redoButton().disabled).toBe(true)

    for (const text of ['1', '2', '3', '4', '5']) type(editor, text)
    expect(textOf(editor)).toBe('Start12345')
    await waitFor(() => expect(undoButton().disabled).toBe(false))

    for (let i = 0; i < 3; i++) await fireEvent.click(undoButton())
    expect(textOf(editor)).toBe('Start12')
    await waitFor(() => expect(redoButton().disabled).toBe(false))

    await fireEvent.keyDown(textbox, { key: 'y', code: 'KeyY', keyCode: 89, ctrlKey: true })
    expect(textOf(editor)).toBe('Start123')

    await fireEvent.keyDown(textbox, { key: 'z', code: 'KeyZ', keyCode: 90, ctrlKey: true })
    await fireEvent.keyDown(textbox, { key: 'Z', code: 'KeyZ', keyCode: 90, ctrlKey: true, shiftKey: true })
    expect(textOf(editor)).toBe('Start123')

    for (let i = 0; i < 3; i++) await fireEvent.click(undoButton())
    expect(textOf(editor)).toBe('Start')
    await waitFor(() => expect(undoButton().disabled).toBe(true))
  })

  it('adds no undo step when the form puts another value in', async () => {
    const { editor, rerender } = await mount()

    await rerender({ value: doc('Reloaded') })

    await waitFor(() => expect(textOf(editor)).toBe('Reloaded'))
    expect(undoButton().disabled).toBe(true)
  })

  it('sends a value that holds only an image: it is not empty (035 spec edge case "Empty structures")', async () => {
    const onlyImage = { type: 'doc', content: [{ type: 'image', attrs: { media: { mediaId: 'file-1' } } }] }
    const { editor, emitted } = await mount(onlyImage)

    editor.commands.command(({ tr }) => {
      tr.setNodeMarkup(0, undefined, { media: { mediaId: 'file-1' }, alt: 'Front', decorative: false, uploadId: null })
      return true
    })

    const sent = (emitted()['update:value'] as unknown[][]).at(-1)![0]
    expect(sent).toEqual({ type: 'doc', content: [{ type: 'image', attrs: { media: { mediaId: 'file-1' }, alt: 'Front' } }] })
  })

  it('follows the field’s allowed formats: its toolbar, paste and shortcuts (035 FR-023)', async () => {
    const restricted: SchemaField = { ...field, formats: { headings: false, images: false, tables: false } }
    const view = render(RichTextInput, {
      props: { field: restricted, value: doc('Start'), id: 'f20-input', describedBy: 'f20-help', invalid: false, siteId: SITE, label: 'Body' },
      global: { plugins: [i18n] },
    })
    await screen.findByRole('button', { name: en.editor.richText.undo })
    const textbox = view.container.querySelector<HTMLElement & { editor: Editor }>('[role="textbox"]')!
    const editor = textbox.editor

    for (const name of [en.editor.richText.quote, en.editor.richText.image.insert, en.editor.richText.table.insert]) {
      expect(screen.queryByRole('button', { name })).toBeNull()
    }
    expect(screen.queryByRole('combobox', { name: en.editor.richText.blockStyle })).toBeNull()

    const html = editor.view.someProp('transformPastedHTML', (transform) => transform('<h2>Title</h2><blockquote><p>Said</p></blockquote><table><tr><td>A</td><td>B</td></tr></table>', editor.view))
    expect(html).toBe('<p>Title</p><p>Said</p><p>A B</p>')

    await fireEvent.keyDown(textbox, { key: '2', code: 'Digit2', ctrlKey: true, altKey: true })
    editor.commands.setHeading({ level: 2 })
    editor.commands.insertContent({ type: 'image', attrs: { media: { mediaId: 'file-1' } } })
    expect(JSON.stringify(editor.getJSON())).not.toMatch(/"heading"|"image"/)
  })
})

describe('RichTextInput limits', () => {
  // 20,000 words of paragraphs: far over the 20,000 characters a rich text holds.
  const longPaste = Array.from({ length: 400 }, () => `<p>${'word '.repeat(50)}</p>`).join('')

  it('refuses a paste past 20,000 characters whole, and says why (035 FR-005)', async () => {
    const { editor, emitted } = await mount()

    // jsdom has no ClipboardEvent; the guard filters every change, a paste or a command alike.
    editor.commands.insertContent(longPaste)

    expect(textOf(editor)).toBe('Start')
    expect(emitted()['update:value']).toBeUndefined()
    await screen.findByText(en.editor.richText.notice.charactersLimit)
  })

  it('still lets a text already over the limit be shortened', async () => {
    const { editor, emitted } = await mount(doc('x'.repeat(20_005)))

    editor.commands.deleteRange({ from: 1, to: 4 })

    expect(textOf(editor)).toHaveLength(20_002)
    expect(emitted()['update:value']).toHaveLength(1)
  })
})
