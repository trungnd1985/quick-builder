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
})
