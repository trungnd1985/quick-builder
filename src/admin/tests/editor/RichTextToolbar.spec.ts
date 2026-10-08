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
