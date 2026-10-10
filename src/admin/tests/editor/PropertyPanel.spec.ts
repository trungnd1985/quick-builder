import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { insertableTypes as blockTypes, registry } from '../../src/editor/blocks/registry'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { locate } from '../../src/editor/ops/tree'
import { propNames } from '../../src/editor/schema/schema'
import { documentWith, mountEditor, stubServer, PAGE } from './harness'

// 011 US2, FR-018 to FR-021: a property panel generated from each block's definition,
// showing exactly its properties, applying valid changes and refusing invalid ones with the
// reason while the block keeps its last valid value.

const one = (node: object) => documentWith({ id: 's', type: 'section', props: {}, children: [node] })

const panel = () => screen.getByRole('region', { name: en.editor.panel.title })

async function violations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'aria-allowed-attr', 'select-name'] },
  })
  return results.violations
}

describe('PropertyPanel (US2)', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('says what to do when nothing is selected', async () => {
    stubServer()
    await mountEditor()

    expect(within(panel()).getByText(en.editor.panel.nothingSelected)).toBeTruthy()
  })

  it.each(blockTypes)('shows exactly the properties of a %s, plus its layer label', async (type) => {
    const initial = registry[type].initialProps((key) => key)
    stubServer(type === 'section'
      ? documentWith({ id: 'x', type, props: initial, children: [] })
      : one({ id: 'x', type, props: initial, children: [] }))
    const { container } = await mountEditor()
    useEditorStore().select('x')

    await waitFor(() => expect(panel().querySelector('[data-prop]')).not.toBeNull())
    const shown = [...panel().querySelectorAll<HTMLElement>('[data-prop]')].map((e) => e.dataset.prop).sort()

    // `bind` is edited through the binding control beside each bindable property (016 TB-14).
    expect(shown).toEqual(propNames(type).filter((p) => p !== 'bind').sort())
    expect(await violations(container)).toEqual([])
  })

  it('applies a change to the block at once', async () => {
    stubServer(one({ id: 'h', type: 'heading', props: { text: 'Hello' }, children: [] }))
    await mountEditor()
    const store = useEditorStore()
    store.select('h')

    const level = await within(panel()).findByLabelText(en.editor.panel.props.level)
    await fireEvent.update(level, '3')

    expect(locate(store.root!, 'h')!.node.props.level).toBe(3)
    expect(document.querySelector('[data-node-id="h"] h3')).not.toBeNull()
  })

  it('keeps in-place text and the panel in step', async () => {
    stubServer(one({ id: 'h', type: 'heading', props: { text: 'Hello' }, children: [] }))
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('h')

    const field = await within(panel()).findByLabelText<HTMLInputElement>(en.editor.panel.props.text)
    await fireEvent.update(field, 'From the panel')
    expect(container.querySelector('[data-node-id="h"] h2')!.textContent).toBe('From the panel')

    const inPlace = container.querySelector<HTMLElement>('[data-node-id="h"] h2')!
    inPlace.textContent = 'From the canvas'
    await fireEvent.input(inPlace)
    await waitFor(() => expect(field.value).toBe('From the canvas'))
  })

  it.each([
    ['a javascript link', { id: 'b', type: 'button', props: { label: 'Go' }, children: [] }, 'link'],
    ['an embed from another site', { id: 'e', type: 'embed', props: {}, children: [] }, 'url'],
    ['a heading over 300 characters', { id: 'h', type: 'heading', props: { text: 'Hi' }, children: [] }, 'text'],
  ])('refuses %s with the reason, and keeps the last valid value (FR-021)', async (_, node, prop) => {
    stubServer(one(node))
    await mountEditor()
    const store = useEditorStore()
    store.select(node.id)
    const before = locate(store.root!, node.id)!.node

    if (prop === 'link') {
      await fireEvent.click(await within(panel()).findByLabelText(en.editor.panel.link.address))
      await fireEvent.update(within(panel()).getByLabelText(en.editor.panel.link.addressField), 'javascript:alert(1)')
    } else if (prop === 'url') {
      await fireEvent.update(await within(panel()).findByLabelText(en.editor.panel.props.url), 'https://example.com/video')
    } else {
      await fireEvent.update(await within(panel()).findByLabelText(en.editor.panel.props.text), 'x'.repeat(301))
    }

    const field = panel().querySelector<HTMLElement>(`[data-prop="${prop}"]`)!
    await waitFor(() => expect(field.querySelector('[aria-invalid="true"]')).not.toBeNull())
    expect(field.querySelector('.field-error')!.textContent!.length).toBeGreaterThan(0)
    expect(locate(store.root!, node.id)!.node).toBe(before)
  })

  it('offers the site colours and stores a reference to the chosen token', async () => {
    stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [] }))
    await mountEditor()
    const store = useEditorStore()
    store.select('s')

    const background = await within(panel()).findByLabelText(en.editor.panel.props.background)
    expect(within(background as HTMLElement).getByRole('option', { name: 'Brand light' })).toBeTruthy()
    await fireEvent.update(background, 'color-brand-light')

    expect(locate(store.root!, 's')!.node.props.background).toEqual({ token: 'color-brand-light' })
  })

  it('marks a reference to a deleted colour and says the default is used (clarification Q4)', async () => {
    stubServer(documentWith({ id: 's', type: 'section', props: { background: { token: 'color-gone' } }, children: [] }))
    await mountEditor()
    useEditorStore().select('s')

    await waitFor(() => expect(within(panel()).getByText(en.editor.panel.token.deleted)).toBeTruthy())
  })

  it('links a button to a page of the site by its id (FR-015)', async () => {
    stubServer(one({ id: 'b', type: 'button', props: { label: 'Go' }, children: [] }))
    await mountEditor()
    const store = useEditorStore()
    store.select('b')

    await fireEvent.click(await within(panel()).findByLabelText(en.editor.panel.link.page))
    const pages = await within(panel()).findByLabelText(en.editor.panel.link.pageField)
    await waitFor(() => expect(within(pages as HTMLElement).getByRole('option', { name: /About/ })).toBeTruthy())
    await fireEvent.update(pages, PAGE)

    expect(locate(store.root!, 'b')!.node.props.link).toEqual({ type: 'page', pageId: PAGE })
  })

  it('names any block for the layer view, and clears the name when emptied', async () => {
    stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [] }))
    await mountEditor()
    const store = useEditorStore()
    store.select('s')

    const label = await within(panel()).findByLabelText(en.editor.panel.props.layerLabel)
    await fireEvent.update(label, 'Hero')
    expect(locate(store.root!, 's')!.node.props.layerLabel).toBe('Hero')

    await fireEvent.update(label, '')
    expect(locate(store.root!, 's')!.node.props).not.toHaveProperty('layerLabel')
  })
})
