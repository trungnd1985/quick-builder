import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, markRaw, type PropType } from 'vue'
import { fireEvent, render, screen } from '@testing-library/vue'
import axe from 'axe-core'
import { Editor } from '@tiptap/core'
import en from '../../src/i18n/en.json'
import RichTextToolbar, { type RichTextHistory } from '../../src/editor/richtext/RichTextToolbar.vue'
import { richTextExtensions } from '../../src/editor/richtext/tiptap'
import type { AllowedFormats } from '../../src/editor/richtext/formats'
import { i18n } from './harness'

// The rich text toolbar (035 FR-006, FR-007, FR-027, research RT-07): grouped icon buttons
// with one tab stop, names and shortcuts, and the pressed state of the format at the cursor.

let editor: Editor | null = null

afterEach(() => {
  editor?.destroy()
  editor = null
  document.body.innerHTML = ''
})

function mount(options: { content?: unknown; history?: RichTextHistory; allowed?: AllowedFormats } = {}) {
  const element = document.createElement('div')
  document.body.appendChild(element)
  editor = markRaw(new Editor({
    element,
    extensions: richTextExtensions(),
    content: (options.content ?? { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Hello' }] }] }) as object,
  }))

  const Host = defineComponent({
    props: {
      history: { type: Object as PropType<RichTextHistory | undefined>, default: undefined },
      allowed: { type: Object as PropType<AllowedFormats | undefined>, default: undefined },
    },
    setup: (props) => () => h(RichTextToolbar, { editor: editor!, siteId: 'site', history: props.history, allowed: props.allowed }),
  })
  return render(Host, { props: { history: options.history, allowed: options.allowed }, global: { plugins: [i18n] } })
}

const controls = (): HTMLElement[] => [...screen.getByRole('toolbar').querySelectorAll<HTMLElement>('[data-rt-control]')]

describe('RichTextToolbar', () => {
  it('is one tab stop, moved through with the arrow keys, Home and End', async () => {
    mount()
    screen.getByRole('toolbar', { name: en.editor.richText.toolbar })
    const all = controls()
    // Keys go to the focused control and bubble to the toolbar, as in a browser.
    const press = (key: string) => fireEvent.keyDown(document.activeElement!, { key })

    expect(all.filter((c) => c.tabIndex === 0)).toHaveLength(1)
    expect(all[0]!.tabIndex).toBe(0)

    all[0]!.focus()
    await press('ArrowRight')
    expect(document.activeElement).toBe(all[1])
    expect(all[1]!.tabIndex).toBe(0)
    expect(all[0]!.tabIndex).toBe(-1)

    await press('End')
    expect(document.activeElement).toBe(all.at(-1))
    await press('ArrowRight')
    expect(document.activeElement).toBe(all[0])
    await press('ArrowLeft')
    expect(document.activeElement).toBe(all.at(-1))
    await press('Home')
    expect(document.activeElement).toBe(all[0])
  })

  it('names every control, shows its shortcut, and separates the groups', () => {
    mount()

    const bold = screen.getByRole('button', { name: en.editor.richText.bold })
    expect(bold.getAttribute('data-tooltip')).toBe(`${en.editor.richText.bold} (Ctrl+B)`)
    expect(screen.getByRole('button', { name: en.editor.richText.italic }).getAttribute('aria-keyshortcuts')).toBe('Control+I')
    expect(screen.getByRole('button', { name: en.editor.richText.bulletList })).toBeTruthy()
    expect(screen.getByRole('button', { name: en.editor.richText.orderedList })).toBeTruthy()
    expect(screen.getByRole('button', { name: en.editor.richText.link })).toBeTruthy()
    expect(screen.getAllByRole('separator').length).toBeGreaterThan(0)
  })

  it('shows which formats are on at the cursor', async () => {
    mount({ content: { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Plain ' }, { type: 'text', text: 'Bold', marks: [{ type: 'bold' }] }] }] } })
    const bold = screen.getByRole('button', { name: en.editor.richText.bold })
    expect(bold.getAttribute('aria-pressed')).toBe('false')

    editor!.commands.setTextSelection(9)
    await Promise.resolve()

    expect(bold.getAttribute('aria-pressed')).toBe('true')
  })

  it('offers undo and redo only when the host has a history, and follows it', async () => {
    const undo = vi.fn()
    mount({ history: { undo, redo: vi.fn(), canUndo: true, canRedo: false } })

    const undoButton = screen.getByRole('button', { name: en.editor.richText.undo })
    expect((undoButton as HTMLButtonElement).disabled).toBe(false)
    expect((screen.getByRole('button', { name: en.editor.richText.redo }) as HTMLButtonElement).disabled).toBe(true)
    expect(undoButton.getAttribute('aria-keyshortcuts')).toBe('Control+Z')

    await fireEvent.click(undoButton)
    expect(undo).toHaveBeenCalledOnce()
  })

  it('has no history group without a history', () => {
    mount()

    expect(screen.queryByRole('button', { name: en.editor.richText.undo })).toBeNull()
  })

  it('passes the accessibility checks', async () => {
    const { container } = mount({ history: { undo: vi.fn(), redo: vi.fn(), canUndo: false, canRedo: false } })

    expect((await axe.run(container)).violations).toEqual([])
  })
})

describe('RichTextToolbar: headings and quotes (035 US1)', () => {
  const firstBlock = () => editor!.getJSON().content![0]!

  it('sets paragraph and headings 2 to 4 from the block style menu, which follows the cursor', async () => {
    mount()
    const style = screen.getByRole('combobox', { name: en.editor.richText.blockStyle }) as HTMLSelectElement
    expect(style.value).toBe('paragraph')

    await fireEvent.update(style, 'heading-2')
    expect(firstBlock()).toMatchObject({ type: 'heading', attrs: { level: 2 } })
    expect(style.value).toBe('heading-2')

    await fireEvent.update(style, 'heading-4')
    expect(firstBlock()).toMatchObject({ type: 'heading', attrs: { level: 4 } })

    await fireEvent.update(style, 'paragraph')
    expect(firstBlock()).toMatchObject({ type: 'paragraph' })
    expect([...style.options].map((o) => o.textContent?.trim())).toEqual([
      en.editor.richText.paragraph, en.editor.richText.heading2, en.editor.richText.heading3, en.editor.richText.heading4,
    ])
  })

  it('toggles a quote around the block', async () => {
    mount()
    const quote = screen.getByRole('button', { name: en.editor.richText.quote })

    await fireEvent.click(quote)
    expect(firstBlock()).toMatchObject({ type: 'blockquote' })
    expect(quote.getAttribute('aria-pressed')).toBe('true')

    await fireEvent.click(quote)
    expect(firstBlock()).toMatchObject({ type: 'paragraph' })
  })

  it.each([['2', 2], ['3', 3], ['4', 4]])('makes a heading with Ctrl+Alt+%s, and a paragraph again with Ctrl+Alt+0', async (key, level) => {
    mount()

    await fireEvent.keyDown(editor!.view.dom, { key, code: `Digit${key}`, ctrlKey: true, altKey: true })
    expect(firstBlock()).toMatchObject({ type: 'heading', attrs: { level } })

    await fireEvent.keyDown(editor!.view.dom, { key: '0', code: 'Digit0', ctrlKey: true, altKey: true })
    expect(firstBlock()).toMatchObject({ type: 'paragraph' })
  })

  it('leaves AltGr typing alone: a character typed with Ctrl+Alt+2 changes no heading', async () => {
    mount()

    await fireEvent.keyDown(editor!.view.dom, { key: '²', code: 'Digit2', keyCode: 50, ctrlKey: true, altKey: true })

    expect(firstBlock()).toMatchObject({ type: 'paragraph' })
  })

  it('hides headings and quotes when the field does not allow them', () => {
    mount({ allowed: { headings: false, images: true, tables: true } })

    expect(screen.queryByRole('combobox', { name: en.editor.richText.blockStyle })).toBeNull()
    expect(screen.queryByRole('button', { name: en.editor.richText.quote })).toBeNull()
  })
})

describe('RichTextToolbar: inline styles (035 US2)', () => {
  const marksOfFirstText = () => (editor!.getJSON().content![0]!.content![0]!.marks ?? []).map((m) => m.type)

  it.each([
    ['underline', en.editor.richText.underline, 'Control+U'],
    ['strike', en.editor.richText.strike, 'Control+Shift+X'],
    ['code', en.editor.richText.code, 'Control+E'],
  ])('%s: a button that toggles the mark on the selection and shows it is on', async (mark, name, shortcut) => {
    mount()
    editor!.commands.setTextSelection({ from: 1, to: 6 })
    const button = screen.getByRole('button', { name })
    expect(button.getAttribute('aria-keyshortcuts')).toBe(shortcut)

    await fireEvent.click(button)
    expect(marksOfFirstText()).toEqual([mark])
    expect(button.getAttribute('aria-pressed')).toBe('true')

    await fireEvent.click(button)
    expect(marksOfFirstText()).toEqual([])
  })

  it.each([
    ['underline', { key: 'u', code: 'KeyU', keyCode: 85 }],
    ['strike', { key: 'X', code: 'KeyX', keyCode: 88, shiftKey: true }],
    ['code', { key: 'e', code: 'KeyE', keyCode: 69 }],
  ])('%s has its keyboard shortcut', async (mark, key) => {
    mount()
    editor!.commands.setTextSelection({ from: 1, to: 6 })

    await fireEvent.keyDown(editor!.view.dom, { ...key, ctrlKey: true })

    expect(marksOfFirstText()).toEqual([mark])
  })
})

