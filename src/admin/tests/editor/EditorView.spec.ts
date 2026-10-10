import 'fake-indexeddb/auto'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { validateDocument } from '../../src/editor/schema/documentValidator'
import { insertableTypes as blockTypes } from '../../src/editor/blocks/registry'
import { locate } from '../../src/editor/ops/tree'
import { enableRenderProbe, renderCounts } from '../../src/editor/canvas/renderProbe'
import { documentWith, drag, emptyDocument, mountEditor, PAGE, press, stubServer } from './harness'

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

  it('undoes and redoes from the keyboard and the top bar (FR-024)', async () => {
    stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text: 'Hi' }, children: [] }] }))
    const { editorRoot } = await mountEditor()
    const store = useEditorStore()

    store.select('h')
    press(editorRoot(), 'Delete')
    expect(locate(store.root!, 'h')).toBeNull()

    press(editorRoot(), 'z', { ctrl: true })
    expect(locate(store.root!, 'h')).not.toBeNull()
    expect(store.selection).toBe('h')

    await fireEvent.click(screen.getByRole('button', { name: en.editor.history.redo }))
    expect(locate(store.root!, 'h')).toBeNull()
  })

  it('undoes from the Rich Text toolbar with the one history of the page (035 FR-009)', async () => {
    const before = { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Hello' }] }] }
    stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [{ id: 'r', type: 'rich-text', props: { content: before }, children: [] }] }))
    const { container } = await mountEditor()
    const store = useEditorStore()

    await fireEvent.dblClick(container.querySelector('[data-node-id="r"] .qb-rich-text')!)
    const toolbar = await screen.findByRole('toolbar', { name: en.editor.richText.toolbar })
    const undo = await within(toolbar).findByRole('button', { name: en.editor.richText.undo })
    expect((undo as HTMLButtonElement).disabled).toBe(true)

    const textbox = container.querySelector<HTMLElement & { editor: import('@tiptap/core').Editor }>('[data-node-id="r"] [role="textbox"]')!
    textbox.editor.commands.insertContentAt(6, ' world')
    expect(locate(store.root!, 'r')!.node.props.content).not.toEqual(before)

    await waitFor(() => expect((undo as HTMLButtonElement).disabled).toBe(false))
    await fireEvent.click(undo)

    expect(locate(store.root!, 'r')!.node.props.content).toEqual(before)
    await waitFor(() => expect((within(toolbar).getByRole('button', { name: en.editor.richText.redo }) as HTMLButtonElement).disabled).toBe(false))
  })

  it('on a conflict: read-only, no overwrite, copy, Reload latest, paste back (FR-028)', async () => {
    const server = stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text: 'Mine' }, children: [] }] }))
    const { container, editorRoot } = await mountEditor()
    const store = useEditorStore()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    server.script('PUT /api/v1/pages/sites/33333333-3333-3333-3333-333333333333/pages/44444444-4444-4444-4444-444444444444/draft',
      { status: 409, body: { title: 'Conflict', status: 409, errorCode: 'pages.out_of_date' } })

    await fireEvent.keyDown(option(container, 'text'), { key: 'Enter' })
    await vi.advanceTimersByTimeAsync(2100)

    await waitFor(() => expect(screen.getByText(en.editor.readOnly.conflict)).toBeTruthy())
    expect(screen.queryByRole('button', { name: /overwrite/i })).toBeNull()

    // Still read-only: nothing changes, but copying works.
    const text = store.root!.children[0].children.find((c) => c.type === 'text')!
    store.select(text.id)
    press(editorRoot(), 'Delete')
    expect(locate(store.root!, text.id)).not.toBeNull()
    press(editorRoot(), 'c', { ctrl: true })

    await fireEvent.click(screen.getByRole('button', { name: en.editor.conflict.reload }))
    await waitFor(() => expect(store.readOnly).toBeNull())
    expect(store.root!.children[0].children.map((c) => c.type)).toEqual(['heading'])

    store.select('h')
    press(editorRoot(), 'v', { ctrl: true })
    expect(store.root!.children[0].children.map((c) => c.type)).toEqual(['heading', 'text'])
  })

  it('offers unsaved changes from an earlier visit, and restores them only when asked (US3 #8)', async () => {
    const { writeRecovery, clearRecoveryForUser } = await import('../../src/editor/state/recovery')
    const { useAuthStore } = await import('../../src/app/authStore')
    stubServer(emptyDocument(), { version: 7 })

    // The member is signed in; their copy is based on the version the server still has.
    const { setActivePinia, createPinia } = await import('pinia')
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().signedIn({ id: 'u1', displayName: 'Dana', email: 'd@example.test', isAdministrator: true, siteRoles: [] })
    await clearRecoveryForUser('u1')
    await writeRecovery({
      userId: 'u1', siteId: '33333333-3333-3333-3333-333333333333', kind: 'page', documentId: '44444444-4444-4444-4444-444444444444',
      document: documentWith({ id: 'r', type: 'section', props: {}, children: [] }) as never, baseVersion: 7,
    })

    await mountEditor('page', pinia)
    const store = useEditorStore()

    await waitFor(() => expect(screen.getByRole('alertdialog')).toBeTruthy())
    expect(store.root!.children).toHaveLength(0)

    await fireEvent.click(screen.getByRole('button', { name: en.editor.recovery.restore }))
    expect(store.root!.children.map((c) => c.id)).toEqual(['r'])
    await clearRecoveryForUser('u1')
  })

  it('previews at desktop, tablet and mobile widths, and remembers the choice per member (US5, FR-036)', async () => {
    const { useAuthStore } = await import('../../src/app/authStore')
    const { createPinia, setActivePinia } = await import('pinia')
    stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text: 'Hi' }, children: [] }] }))
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().signedIn({ id: 'u-preview', displayName: 'Dana', email: 'd@example.test', isAdministrator: true, siteRoles: [] })
    localStorage.removeItem('qb.editor.previewWidth.u-preview')
    const { container } = await mountEditor('page', pinia)
    const frame = () => container.querySelector<HTMLElement>('.qb-canvas')!
    const widths = screen.getByRole('group', { name: en.editor.preview.label })

    expect(frame().style.width).toBe('1280px')
    await fireEvent.click(within(widths).getByRole('button', { name: en.editor.preview.tablet }))
    expect(frame().style.width).toBe('768px')
    await fireEvent.click(within(widths).getByRole('button', { name: en.editor.preview.mobile }))
    expect(frame().style.width).toBe('375px')
    expect(within(widths).getByRole('button', { name: en.editor.preview.mobile }).getAttribute('aria-pressed')).toBe('true')
    expect(localStorage.getItem('qb.editor.previewWidth.u-preview')).toBe('mobile')

    // Editing still works at this width.
    const heading = container.querySelector<HTMLElement>('[data-node-id="h"] h2')!
    heading.textContent = 'Small screen'
    await fireEvent.input(heading)
    expect(locate(useEditorStore().root!, 'h')!.node.props.text).toBe('Small screen')
  })

  it('draws Section widths, Container maximum widths and column patterns from the site widths (FR-013, FR-031)', async () => {
    const text = (id: string) => ({ id, type: 'text', props: { text: id }, children: [] })
    stubServer(documentWith(
      { id: 's-narrow', type: 'section', props: { width: 'narrow' }, children: [text('a')] },
      { id: 's-normal', type: 'section', props: {}, children: [
        { id: 'c-cols', type: 'container', props: { direction: 'horizontal', columns: '2-1', maxWidth: 'wide', align: 'center' }, children: [text('b'), text('c'), text('d')] },
        { id: 'c-equal', type: 'container', props: { direction: 'horizontal' }, children: [text('e')] },
        { id: 'c-vertical', type: 'container', props: { columns: '2-1' }, children: [text('f')] },
      ] },
    ))
    const { container } = await mountEditor()
    const block = (id: string) => container.querySelector<HTMLElement>(`[data-node-id="${id}"] > *`)!

    expect(block('s-narrow').classList).toContain('qb-section--narrow')
    expect(block('s-normal').classList).toContain('qb-section--contained')

    const columns = block('c-cols').classList
    expect([...columns]).toEqual(expect.arrayContaining(['qb-container--grid', 'qb-columns--2-1', 'qb-container--max-wide', 'qb-align--center', 'qb-container--stack-on-mobile']))
    expect(block('c-equal').classList).not.toContain('qb-container--grid')
    expect(block('c-equal').classList).toContain('qb-container--max-full')
    expect(block('c-vertical').classList).not.toContain('qb-container--grid')

    // The frame carries the site's widths; the defaults here.
    const frame = container.querySelector<HTMLElement>('.qb-canvas')!
    expect(frame.style.getPropertyValue('--qb-width-narrow')).toBe('720px')
    expect(frame.style.getPropertyValue('--qb-width-wide')).toBe('1440px')

    // A patterned row still stacks at mobile width when stackOnMobile is on.
    const { readFileSync } = await import('node:fs')
    const { resolve } = await import('node:path')
    const css = readFileSync(resolve(process.cwd(), 'src/editor/blocks/blocks.css'), 'utf8')
    const mobile = css.slice(css.indexOf('@container page (max-width: 480px)'))
    expect(mobile).toMatch(/\.qb-container--grid\.qb-container--stack-on-mobile\s*\{\s*grid-template-columns: minmax\(0, 1fr\);/)
  })

  it('marks links to pages that no longer exist, on buttons, images and rich text (spec Edge Cases)', async () => {
    const gone = { type: 'page', pageId: '99999999-9999-4999-8999-999999999999' }
    const here = { type: 'page', pageId: PAGE }
    const rich = (to: unknown, text: string) => ({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text, marks: [{ type: 'link', attrs: { to } }] }] }] })
    stubServer(documentWith({ id: 's', type: 'section', props: {}, children: [
      { id: 'b-gone', type: 'button', props: { label: 'Gone', link: gone }, children: [] },
      { id: 'b-here', type: 'button', props: { label: 'Here', link: here }, children: [] },
      { id: 'b-url', type: 'button', props: { label: 'Out', link: { type: 'url', href: 'https://example.com' } }, children: [] },
      { id: 'i-gone', type: 'image', props: { decorative: true, link: gone }, children: [] },
      { id: 'r-gone', type: 'rich-text', props: { content: rich(gone, 'old page') }, children: [] },
      { id: 'r-here', type: 'rich-text', props: { content: rich(here, 'about') }, children: [] },
    ] }))
    const { container } = await mountEditor()
    const marked = (id: string) => container.querySelector(`[data-node-id="${id}"] .qb-editor-broken-link`)

    await waitFor(() => expect(marked('b-gone')?.textContent).toBe(en.editor.canvas.brokenLink))
    expect(marked('b-here')).toBeNull()
    expect(marked('b-url')).toBeNull()
    expect(marked('i-gone')).not.toBeNull()
    expect(marked('r-gone')).not.toBeNull()
    expect(container.querySelector('[data-node-id="r-gone"] a[data-broken-link]')?.getAttribute('title')).toBe(en.editor.canvas.brokenLink)
    expect(marked('r-here')).toBeNull()

    // Marking is an editing aid: the stored document is untouched and still valid.
    expect(validateDocument({ schemaVersion: 1, root: useEditorStore().root! }, 'page')).toEqual({ valid: true })
  })

  it('opens at the width the member chose last time, and at desktop when storage fails', async () => {
    const { useAuthStore } = await import('../../src/app/authStore')
    const { createPinia, setActivePinia } = await import('pinia')
    stubServer()
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().signedIn({ id: 'u-remembered', displayName: 'Dana', email: 'd@example.test', isAdministrator: true, siteRoles: [] })
    localStorage.setItem('qb.editor.previewWidth.u-remembered', 'tablet')

    const first = await mountEditor('page', pinia)
    expect(first.container.querySelector<HTMLElement>('.qb-canvas')!.style.width).toBe('768px')

    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled')
    })
    const other = createPinia()
    setActivePinia(other)
    useAuthStore().signedIn({ id: 'u-remembered', displayName: 'Dana', email: 'd@example.test', isAdministrator: true, siteRoles: [] })
    const second = await mountEditor('page', other)
    expect(second.container.querySelector<HTMLElement>('.qb-canvas')!.style.width).toBe('1280px')
    getItem.mockRestore()
  })

  it('edits a shared component with the same editor, without offering components (US7 #1)', async () => {
    const server = stubServer(emptyDocument(), { title: 'Header', components: [{ id: 'c1', name: 'Header' }] })
    const { container } = await mountEditor('component')
    vi.useFakeTimers({ shouldAdvanceTime: true })

    expect(await screen.findByRole('heading', { level: 1, name: 'Header' })).toBeTruthy()
    expect(screen.queryByRole('listbox', { name: en.editor.palette.components })).toBeNull()

    await fireEvent.keyDown(option(container, 'section'), { key: 'Enter' })
    await vi.advanceTimersByTimeAsync(2100)
    await waitFor(() => expect(server.calls.some((c) => c.method === 'PUT' && c.url.includes('/components/'))).toBe(true))
  })

  it('shows a shared component on a page, read-only, as one unit, with a way to open it (US7 #2, #3)', async () => {
    stubServer(
      documentWith({ id: 'ref', type: 'component-ref', props: { componentId: '55555555-5555-5555-5555-555555555555' }, children: [] }),
      { components: [{ id: '55555555-5555-5555-5555-555555555555', name: 'Header' }] },
    )
    // The component's own draft: what the reference shows.
    const componentDraft = documentWith({ id: 'h', type: 'section', props: {}, children: [{ id: 'x', type: 'heading', props: { text: 'Shared title' }, children: [] }] })
    const fetchMock = globalThis.fetch as unknown as ReturnType<typeof vi.fn>
    const original = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (url: string, init?: RequestInit) =>
      url.endsWith('/components/55555555-5555-5555-5555-555555555555/draft')
        ? { ok: true, status: 200, statusText: 'x', headers: { get: () => null }, json: async () => ({ schemaVersion: 1, version: 1, document: componentDraft }), text: async () => '' }
        : original(url, init))

    const { container } = await mountEditor()
    const store = useEditorStore()

    const ref = container.querySelector<HTMLElement>('[data-node-id="ref"]')!
    await waitFor(() => expect(ref.textContent).toContain('Shared title'))
    expect(ref.textContent).toContain(en.editor.components.shared)
    // The component's blocks are not blocks of this page: not addressable, not editable.
    expect(ref.querySelector('[data-node-id="x"]')).toBeNull()
    expect(ref.querySelector('[contenteditable="plaintext-only"]')).toBeNull()

    await fireEvent.click(ref.querySelector('.qb-heading')!)
    expect(store.selection).toBe('ref')
    const panel = screen.getByRole('region', { name: en.editor.panel.title })
    expect(within(panel).getByRole('link', { name: en.editor.components.open })).toBeTruthy()
  })

  it('inserts a shared component from the palette at the page level only (US7 #2)', async () => {
    stubServer(
      documentWith({ id: 's', type: 'section', props: {}, children: [] }),
      { components: [{ id: '55555555-5555-5555-5555-555555555555', name: 'Footer' }] },
    )
    const { container } = await mountEditor()
    const store = useEditorStore()

    const components = await screen.findByRole('listbox', { name: en.editor.palette.components })
    const footer = await within(components).findByRole('option', { name: /Footer/ })
    store.select('s')
    await fireEvent.keyDown(footer, { key: 'Enter' })

    expect(store.root!.children.map((c) => c.type)).toEqual(['section', 'component-ref'])
    expect(store.root!.children[1].props).toEqual({ componentId: '55555555-5555-5555-5555-555555555555' })

    // Dropped inside a Section: refused.
    drag(footer, 'dragstart')
    drag(container.querySelector('[data-empty-parent="s"]')!, 'dragover')
    drag(container.querySelector('[data-empty-parent="s"]')!, 'drop')
    await waitFor(() => expect(screen.getByText(en.editor.errors.ops.nesting_not_allowed)).toBeTruthy())
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
