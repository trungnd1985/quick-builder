import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { validateDocument } from '../../src/editor/schema/documentValidator'
import { blockTypes } from '../../src/editor/schema/schema'
import { locate } from '../../src/editor/ops/tree'
import { enableRenderProbe, renderCounts } from '../../src/editor/canvas/renderProbe'
import { documentWith, drag, emptyDocument, mountEditor, press, stubServer } from './harness'

// 011 US1, FR-001 to FR-008, SC-002, SC-006: building a page by dragging blocks or by
// keyboard, typing in place, and the draft saving itself.

async function violations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: {
      type: 'rule',
      values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order', 'aria-allowed-attr', 'list', 'listitem', 'aria-required-children', 'aria-required-parent'],
    },
  })
  return results.violations
}

const option = (container: Element, type: string) => container.querySelector<HTMLElement>(`[role="option"][data-block-type="${type}"]`)!

describe('EditorView — building a page (US1)', () => {
  beforeEach(() => vi.useRealTimers())
  afterEach(() => vi.unstubAllGlobals())

  it('opens the page with the palette, the canvas and an empty-page invitation', async () => {
    stubServer()
    const { container } = await mountEditor()

    expect(await screen.findByRole('heading', { level: 1, name: 'About' })).toBeTruthy()
    const palette = screen.getByRole('listbox', { name: en.editor.palette.title })
    expect(within(palette).getAllByRole('option')).toHaveLength(blockTypes.length)
    expect(container.querySelector('.qb-canvas')).not.toBeNull()
    expect(screen.getByText(en.editor.canvas.emptyPage.title)).toBeTruthy()
    expect(await violations(container)).toEqual([])
  })

  it('drops a Section from the palette onto the empty page, and selects it', async () => {
    stubServer()
    const { container } = await mountEditor()
    const store = useEditorStore()

    drag(option(container, 'section'), 'dragstart')
    drag(container.querySelector('.qb-canvas')!, 'dragover')
    drag(container.querySelector('.qb-canvas')!, 'drop')

    await waitFor(() => expect(store.root!.children).toHaveLength(1))
    expect(store.root!.children[0].type).toBe('section')
    expect(store.selection).toBe(store.root!.children[0].id)
  })

  it('refuses a Section dropped into a Section, and changes nothing', async () => {
    stubServer(documentWith({ id: 's1', type: 'section', props: {}, children: [] }))
    const { container } = await mountEditor()
    const store = useEditorStore()
    const before = store.root

    drag(option(container, 'section'), 'dragstart')
    drag(container.querySelector('[data-empty-parent="s1"]')!, 'dragover')
    drag(container.querySelector('[data-empty-parent="s1"]')!, 'drop')

    await waitFor(() => expect(screen.getByText(en.editor.errors.ops.nesting_not_allowed)).toBeTruthy())
    expect(store.root).toBe(before)
  })

  it('adds, moves, duplicates and removes every block type with the keyboard alone (SC-006)', async () => {
    stubServer()
    const { container, editorRoot } = await mountEditor()
    const store = useEditorStore()

    // A Section first, then each content block after the last one, all by Enter.
    for (const type of blockTypes) {
      const item = option(container, type)
      item.focus()
      await fireEvent.keyDown(item, { key: 'Enter' })
      const added = locate(store.root!, store.selection!)!.node
      expect(added.type, type).toBe(type)
      // Clear the selection, so the next block goes to the end of the page's last Section
      // rather than inside a Container just added.
      press(editorRoot(), 'Escape')
    }
    const section = store.root!.children[0]
    expect(section.children.map((c) => c.type)).toEqual(blockTypes.filter((t) => t !== 'section'))

    // Each content block: move up, duplicate, then remove the duplicate.
    for (const type of blockTypes.filter((t) => t !== 'section')) {
      const node = store.root!.children[0].children.find((c) => c.type === type)!
      store.select(node.id)
      const index = store.root!.children[0].children.indexOf(node)

      press(editorRoot(), 'ArrowUp', { alt: true })
      const afterMove = store.root!.children[0].children.findIndex((c) => c.id === node.id)
      expect(afterMove, type).toBe(Math.max(index - 1, 0))

      press(editorRoot(), 'd', { ctrl: true })
      expect(store.selection).not.toBe(node.id)
      const copy = store.selection!

      press(editorRoot(), 'Delete')
      expect(locate(store.root!, copy), type).toBeNull()
    }

    expect(validateDocument({ schemaVersion: 1, root: store.root! }, 'page')).toEqual({ valid: true })
  })

  it('moves a block into and out of a Container with Alt+arrows', async () => {
    stubServer(documentWith({
      id: 's', type: 'section', props: {}, children: [
        { id: 'c', type: 'container', props: {}, children: [] },
        { id: 'h', type: 'heading', props: { text: 'Hi' }, children: [] },
      ],
    }))
    const { editorRoot } = await mountEditor()
    const store = useEditorStore()

    store.select('h')
    press(editorRoot(), 'ArrowRight', { alt: true })
    expect(locate(store.root!, 'c')!.node.children.map((c) => c.id)).toEqual(['h'])

    press(editorRoot(), 'ArrowLeft', { alt: true })
    expect(locate(store.root!, 's')!.node.children.map((c) => c.id)).toEqual(['c', 'h'])
  })

  it('types a heading in place', async () => {
    stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text: '' }, children: [] }] }))
    const { container } = await mountEditor()
    const store = useEditorStore()

    const heading = container.querySelector<HTMLElement>('[data-node-id="h"] h2')!
    heading.textContent = 'About us'
    await fireEvent.input(heading)

    expect(locate(store.root!, 'h')!.node.props.text).toBe('About us')
  })

  it('saves the draft by itself after two quiet seconds, as a valid document (FR-026, SC-002)', async () => {
    const server = stubServer()
    const { container } = await mountEditor()
    const store = useEditorStore()
    vi.useFakeTimers({ shouldAdvanceTime: true })

    await fireEvent.keyDown(option(container, 'section'), { key: 'Enter' })
    expect(screen.getByText(en.editor.save.state.dirty)).toBeTruthy()

    await vi.advanceTimersByTimeAsync(2100)
    await waitFor(() => expect(screen.getByText(en.editor.save.state.saved)).toBeTruthy())

    const put = server.calls.find((c) => c.method === 'PUT')!
    expect(put.url).toContain('/draft')
    expect(put.body!.version).toBe(7)
    expect(validateDocument(put.body!.document, 'page')).toEqual({ valid: true })
    expect(store.baseVersion).toBe(8)
  })

  it('opens a draft that no longer validates read-only, and never saves it (analysis C2)', async () => {
    const server = stubServer({ ...emptyDocument(), schemaVersion: 2 })
    const { container } = await mountEditor()
    const store = useEditorStore()

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain(en.editor.errors.rules.unsupported_schema_version))
    expect(store.readOnly?.reason).toBe('invalid')

    await fireEvent.keyDown(option(container, 'section'), { key: 'Enter' })
    expect(store.root!.children).toHaveLength(0)
    expect(server.calls.some((c) => c.method === 'PUT')).toBe(false)
  })

  it('re-renders only the blocks on the path of an edit (research ED-10, SC-005)', async () => {
    stubServer(documentWith(
      { id: 's1', type: 'section', props: {}, children: [{ id: 'a', type: 'heading', props: { text: 'A' }, children: [] }] },
      { id: 's2', type: 'section', props: {}, children: [{ id: 'b', type: 'heading', props: { text: 'B' }, children: [] }] },
    ))
    const { container } = await mountEditor()
    enableRenderProbe()

    const heading = container.querySelector<HTMLElement>('[data-node-id="b"] h2')!
    heading.textContent = 'Bee'
    await fireEvent.input(heading)
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(renderCounts().get('s1') ?? 0).toBe(0)
    expect(renderCounts().get('a') ?? 0).toBe(0)
    expect(renderCounts().get('b') ?? 0).toBeGreaterThan(0)
  })
})
