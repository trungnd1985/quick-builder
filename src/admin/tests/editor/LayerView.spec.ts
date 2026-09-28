import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { locate } from '../../src/editor/ops/tree'
import { documentWith, drag, mountEditor, stubServer } from './harness'

// 011 US4, FR-022, FR-023: the layer view is an outline of every block, in step with the
// canvas, where blocks can be selected, moved (pointer or keyboard), collapsed and named.

const page = () => documentWith(
  { id: 's1', type: 'section', props: { layerLabel: 'Hero' }, children: [
    { id: 'h', type: 'heading', props: { text: 'Title' }, children: [] },
    { id: 'c', type: 'container', props: {}, children: [{ id: 'b', type: 'button', props: { label: 'Go' }, children: [] }] },
  ] },
  { id: 's2', type: 'section', props: {}, children: [] },
)

const tree = () => screen.getByRole('tree', { name: en.editor.layers.title })
const item = (id: string) => tree().querySelector<HTMLElement>(`[role="treeitem"][data-node-id="${id}"]`)!

async function violations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: { type: 'rule', values: ['aria-required-children', 'aria-required-parent', 'aria-valid-attr-value', 'aria-allowed-attr', 'button-name', 'label'] },
  })
  return results.violations
}

describe('LayerView (US4)', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists every block nested as on the page, by its layer name or type', async () => {
    stubServer(page())
    const { container } = await mountEditor()

    const items = within(tree()).getAllByRole('treeitem')
    expect(items.map((i) => i.dataset.nodeId)).toEqual(['s1', 'h', 'c', 'b', 's2'])
    expect(item('s1').getAttribute('aria-level')).toBe('1')
    expect(item('b').getAttribute('aria-level')).toBe('3')
    expect(item('s1').textContent).toContain('Hero')
    expect(item('h').textContent).toContain(en.editor.blocks.heading)
    expect(await violations(container)).toEqual([])
  })

  it('selects on the canvas what is selected in the tree, and the reverse', async () => {
    stubServer(page())
    const { container } = await mountEditor()
    const store = useEditorStore()

    await fireEvent.click(within(item('b')).getByText(en.editor.blocks.button))
    expect(store.selection).toBe('b')
    expect(container.querySelector('[data-node-id="b"].qb-node--selected')).not.toBeNull()

    store.select('h')
    await waitFor(() => expect(item('h').getAttribute('aria-selected')).toBe('true'))
  })

  it('moves through the tree with the arrow keys', async () => {
    stubServer(page())
    await mountEditor()
    const store = useEditorStore()
    store.select('s1')

    await fireEvent.keyDown(item('s1'), { key: 'ArrowDown' })
    expect(store.selection).toBe('h')
    await fireEvent.keyDown(item('h'), { key: 'End' })
    expect(store.selection).toBe('s2')
    await fireEvent.keyDown(item('s2'), { key: 'Home' })
    expect(store.selection).toBe('s1')
  })

  it('moves blocks with Alt and the arrows, under the nesting rules, and says where', async () => {
    stubServer(page())
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('h')

    // Below the container, then into it: Alt+→ goes into the previous sibling.
    await fireEvent.keyDown(item('h'), { key: 'ArrowDown', altKey: true })
    expect(locate(store.root!, 's1')!.node.children.map((n) => n.id)).toEqual(['c', 'h'])
    await fireEvent.keyDown(item('h'), { key: 'ArrowRight', altKey: true })
    expect(locate(store.root!, 'c')!.node.children.map((n) => n.id)).toEqual(['b', 'h'])

    await fireEvent.keyDown(item('h'), { key: 'ArrowUp', altKey: true })
    expect(locate(store.root!, 'c')!.node.children.map((n) => n.id)).toEqual(['h', 'b'])

    await waitFor(() => expect(container.querySelector('.editor-announcer')!.textContent).toContain(en.editor.blocks.heading))

    // A Section cannot go into a Section: refused, nothing changes.
    store.select('s2')
    const before = store.root
    await fireEvent.keyDown(item('s2'), { key: 'ArrowRight', altKey: true })
    expect(store.root).toBe(before)
  })

  it('collapses a block in the tree only', async () => {
    stubServer(page())
    const { container } = await mountEditor()

    await fireEvent.keyDown(item('c'), { key: 'ArrowLeft' })
    useEditorStore().select('c')
    await fireEvent.keyDown(item('c'), { key: 'ArrowLeft' })

    await waitFor(() => expect(item('c').getAttribute('aria-expanded')).toBe('false'))
    expect(tree().querySelector('[data-node-id="b"]')).toBeNull()
    expect(container.querySelector('.qb-canvas [data-node-id="b"]')).not.toBeNull()
  })

  it('renames a block with F2, and an empty name brings back the type', async () => {
    stubServer(page())
    await mountEditor()
    const store = useEditorStore()
    store.select('h')

    await fireEvent.keyDown(item('h'), { key: 'F2' })
    const input = await within(tree()).findByLabelText(en.editor.layers.rename)
    await fireEvent.update(input, 'Main title')
    await fireEvent.keyDown(input, { key: 'Enter' })

    expect(locate(store.root!, 'h')!.node.props.layerLabel).toBe('Main title')
    await waitFor(() => expect(item('h').textContent).toContain('Main title'))
  })

  it('moves a block by dragging its row onto another block', async () => {
    stubServer(page())
    await mountEditor()
    const store = useEditorStore()

    drag(item('h'), 'dragstart')
    drag(item('s2'), 'dragover')
    drag(item('s2'), 'drop')

    await waitFor(() => expect(locate(store.root!, 's2')!.node.children.map((n) => n.id)).toEqual(['h']))
  })
})
