import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { duplicate, move, remove, setProp } from '../../src/editor/ops'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { COLLECTION, SITE, documentWith, mountEditor, stubServer } from './harness'

// 016 US4 (tasks T063, T068): a Collection list arrives from the palette with its card and an
// empty state, shows the site's published items in its cards on the canvas, keeps its card and
// empty state, and is refused on a site without collections.

const card = (...blocks: object[]) => ({ id: 'card', type: 'list-card', props: {}, children: blocks })
const empty = { id: 'empty', type: 'list-empty', props: {}, children: [{ id: 'none', type: 'text', props: { text: 'Nothing yet' }, children: [] }] }
const title = { id: 'title', type: 'heading', props: { text: 'Own title', level: 3, bind: { text: { value: { field: 'f1' } } } }, children: [] }
const list = (props: object = {}) => ({ id: 'list', type: 'collection-list', props: { source: { collectionId: COLLECTION }, ...props }, children: [card(title), empty] })
const page = (...blocks: object[]) => ({ ...documentWith({ id: 's1', type: 'section', props: {}, children: blocks }), bindingVersion: 1 })

const postsSchema = {
  schemaVersion: 1,
  titleFieldId: 'f1',
  fields: [{ id: 'f1', key: 'title', name: 'Title', help: '', required: true, type: 'text', multiline: false, maxLength: 200 }],
}

const previewItem = (n: number) => ({
  id: `0192f5a1-0000-7000-8000-00000000a00${n}`,
  collectionId: COLLECTION,
  slug: `post-${n}`,
  name: `Post ${n}`,
  values: { schemaVersion: 1, values: { f1: `Post ${n}` } },
  address: `/blog/post-${n}`,
  firstPublishedAt: '2026-10-01T00:00:00Z',
  lastPublishedAt: '2026-10-01T00:00:00Z',
})

const option = (container: Element, type: string) => container.querySelector<HTMLElement>(`[role="option"][data-block-type="${type}"]`)!

describe('the Collection list on the canvas', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('arrives from the palette with its card and an empty state that says so', async () => {
    stubServer(documentWith({ id: 's1', type: 'section', props: {}, children: [] }))
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('s1')

    const item = option(container, 'collection-list')
    item.focus()
    await fireEvent.keyDown(item, { key: 'Enter' })

    const added = store.root!.children[0].children[0]
    expect(added.type).toBe('collection-list')
    expect(added.props.source).toEqual({ collectionId: COLLECTION })
    expect(added.children.map((c) => c.type)).toEqual(['list-card', 'list-empty'])
    expect(added.children[1].children[0].props.text).toBe(en.editor.list.emptyText)

    // FR-022: the card starts with the item's name, linked to the item's page.
    const [title] = added.children[0].children
    expect(title.type).toBe('heading')
    expect(title.props.bind).toEqual({ text: { value: { field: 'f1' } }, link: { value: { item: 'url' } } })
  })

  it('starts the card with the item’s slug when the collection has no name field', async () => {
    const server = stubServer(documentWith({ id: 's1', type: 'section', props: {}, children: [] }))
    server.script(`GET /api/v1/cms/sites/${SITE}/collections/${COLLECTION}`, {
      status: 200,
      body: { id: COLLECTION, name: 'Posts', slug: 'posts', revision: 1, schemaRevision: 1, schema: { ...postsSchema, titleFieldId: null } },
    })
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('s1')

    const item = option(container, 'collection-list')
    item.focus()
    await fireEvent.keyDown(item, { key: 'Enter' })

    const title = store.root!.children[0].children[0].children[0].children[0]
    expect(title.props.bind).toEqual({ text: { value: { item: 'slug' } }, link: { value: { item: 'url' } } })
  })

  it('is refused, with a reason, on a site without collections', async () => {
    const server = stubServer(documentWith({ id: 's1', type: 'section', props: {}, children: [] }))
    server.script(`GET /api/v1/cms/sites/${SITE}/collections`, { status: 200, body: { collections: [], canManage: true } })
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('s1')
    const before = store.root

    const item = option(container, 'collection-list')
    item.focus()
    await fireEvent.keyDown(item, { key: 'Enter' })

    await waitFor(() => expect(screen.getByText(en.editor.errors.ops.no_collections)).toBeTruthy())
    expect(store.root).toBe(before)
  })

  it('shows a card per published item, bound to it, and only the first card is the one edited', async () => {
    const server = stubServer(page(list()))
    server.script(`POST /api/v1/cms/sites/${SITE}/list-preview`, { status: 200, body: { items: [1, 2, 3].map(previewItem), total: 7, referenced: {}, schemas: { [COLLECTION]: postsSchema } } })
    const { container } = await mountEditor()

    await waitFor(() => expect(container.textContent).toContain('Post 3'))
    expect(container.textContent).toContain('Post 1')
    expect(container.textContent).not.toContain('Own title')
    expect(screen.getByText(en.editor.list.showing.replace('{shown}', '3').replace('{total}', '7'))).toBeTruthy()
    // One editable card; the others are drawn from it.
    expect(container.querySelectorAll('.qb-node[data-node-id="title"]')).toHaveLength(1)
    expect(container.querySelectorAll('.qb-list__card')).toHaveLength(3)

    const asked = server.calls.find((c) => c.url.endsWith('/list-preview'))!
    expect(asked.body).toEqual({ list: { source: { collectionId: COLLECTION } } })
  })

  it('asks again only when what the list holds changes, not when its layout does', async () => {
    const server = stubServer(page(list()))
    const { container } = await mountEditor()
    await waitFor(() => expect(container.textContent).toContain(en.editor.list.noItems))
    const store = useEditorStore()
    const previews = () => server.calls.filter((c) => c.url.endsWith('/list-preview')).length
    const before = previews()

    store.apply(setProp(store.root!, 'list', 'columns', 2))
    await new Promise((resolve) => setTimeout(resolve, 400))
    expect(previews()).toBe(before)

    store.apply(setProp(store.root!, 'list', 'limit', 6))
    await waitFor(() => expect(previews()).toBe(before + 1))
  })

  it('keeps its card and empty state: they cannot be removed, moved or duplicated alone', async () => {
    stubServer(page(list()))
    await mountEditor()
    const store = useEditorStore()

    for (const result of [remove(store.root!, 'card'), remove(store.root!, 'empty'), duplicate(store.root!, 'card'), move(store.root!, 'empty', 's1', 0, 'page')]) {
      expect(result.ok ? null : result.refusal.code).toBe('list_slot')
    }
    expect(remove(store.root!, 'list').ok).toBe(true)
  })

  it('previews the pager below the list in the layout chosen, and none for a single page (FR-027)', async () => {
    const server = stubServer(page(list({ paging: { pageSize: 3 } })))
    server.script(`POST /api/v1/cms/sites/${SITE}/list-preview`, { status: 200, body: { items: [1, 2, 3].map(previewItem), total: 7, referenced: {}, schemas: { [COLLECTION]: postsSchema } } })
    const { container } = await mountEditor()
    await waitFor(() => expect(container.querySelector('.qb-pager--canvas')).not.toBeNull())

    const pager = () => container.querySelector('.qb-pager--canvas')!
    expect([...pager().querySelectorAll('.qb-pager__page')].map((n) => n.textContent)).toEqual(['1', '2', '3'])
    expect(pager().previousElementSibling!.classList.contains('qb-list')).toBe(true)

    const store = useEditorStore()
    store.apply(setProp(store.root!, 'list', 'paging', { pageSize: 3, layout: 'steps' }))
    await waitFor(() => expect(pager().classList.contains('qb-pager--steps')).toBe(true))
    expect(pager().querySelectorAll('.qb-pager__page')).toHaveLength(0)
    expect(pager().textContent).toContain(en.editor.list.paging.next)

    store.apply(setProp(store.root!, 'list', 'paging', { pageSize: 10, layout: 'steps' }))
    await waitFor(() => expect(container.querySelector('.qb-pager--canvas')).toBeNull())
  })

  it('passes axe checks with items shown', async () => {
    const server = stubServer(page(list()))
    server.script(`POST /api/v1/cms/sites/${SITE}/list-preview`, { status: 200, body: { items: [1, 2].map(previewItem), total: 2, referenced: {}, schemas: { [COLLECTION]: postsSchema } } })
    const { container } = await mountEditor()
    await waitFor(() => expect(container.textContent).toContain('Post 2'))

    const results = await axe.run(container as HTMLElement, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
    expect(within(container as HTMLElement).getAllByRole('status').length).toBeGreaterThan(0)
  })
})
