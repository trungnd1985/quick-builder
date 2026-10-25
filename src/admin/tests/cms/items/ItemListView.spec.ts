import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import ItemListView from '../../../src/features/cms/items/ItemListView.vue'
import { fromRoute, toRoute } from '../../../src/features/cms/items/listQuery'
import type { ItemList } from '../../../src/api/items'
import { COLLECTION, en, loaded, postCollection, renderAt, SITE, summary, violations, type Call, type Reply } from './itemHarness'

// 015 US3: search, filters and sorts in the page address, numbered pages, and the empty states.

const BASE = `/sites/${SITE}/collections/${COLLECTION}/items`

function server(list: (call: Call) => ItemList): (call: Call) => Reply {
  return (call) => {
    if (call.url.endsWith(`/collections/${COLLECTION}`)) return { status: 200, body: postCollection() }
    if (call.url.includes('/items')) return { status: 200, body: list(call) }
    return { status: 200, body: {} }
  }
}

const page = (count: number, total = count): ItemList =>
  ({ items: Array.from({ length: count }, (_, i) => summary({ id: `id-${i}`, name: `Post ${i}`, slug: `post-${i}` })), total, page: 1, pageSize: 25 })

describe('ItemListView', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('lists items with status and last change, and passes the accessibility check', async () => {
    const { container } = await renderAt(ItemListView, BASE, server(() => page(2)))
    await loaded(en.cms.items.list.loading)

    expect(screen.getByRole('link', { name: 'Post 0' })).toBeTruthy()
    expect(screen.getAllByText(en.cms.items.status.draft).length).toBeGreaterThan(0)
    expect(await violations(container)).toEqual([])
  })

  it('marks the items someone is editing, for this collection only (023 FR-012)', async () => {
    const holders = { serverNow: '2026-10-25T10:00:00Z', holders: [{ documentId: 'id-1', holder: { id: 'u-anna', displayName: 'Anna Tran' }, since: '2026-10-25T09:50:00Z', stale: true }] }
    const list = server(() => page(2))
    const { container, calls } = await renderAt(ItemListView, BASE, (call) => (call.url.includes('/editing?') ? { status: 200, body: holders } : list(call)))

    await waitFor(() => expect(container.querySelector('.editing-marker')).toBeTruthy())
    expect(container.querySelector('.editing-marker')!.closest('tr')!.textContent).toContain('Post 1')
    expect(container.querySelector('.editing-marker--stale')).toBeTruthy()
    expect(calls.find((c) => c.url.includes('/editing?'))!.url).toContain(`kind=item&collectionId=${COLLECTION}`)
  })

  it('keeps search, filters, sort and page in the address and asks the server for them', async () => {
    const { calls, router } = await renderAt(ItemListView, `${BASE}?q=linen&status=changed&field.f6=o2&sort=name&dir=asc&page=2&size=50`, server(() => page(1, 120)))
    await loaded(en.cms.items.list.loading)

    const request = calls.filter((c) => c.url.includes('/items?')).at(-1)!.url
    expect(decodeURIComponent(request)).toContain('q=linen&status=changed&field[f6]=o2&sort=name&dir=asc&page=2&pageSize=50')
    expect((screen.getByLabelText(en.cms.items.list.searchLabel) as HTMLInputElement).value).toBe('linen')
    expect(screen.getByRole('columnheader', { name: en.cms.items.list.columns.name }).getAttribute('aria-sort')).toBe('ascending')

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.list.columns.name }))
    await waitFor(() => expect(router.currentRoute.value.query.dir).toBe('desc'))
    expect(router.currentRoute.value.query.page).toBeUndefined()
  })

  it('waits for a pause in typing before searching', async () => {
    const { router } = await renderAt(ItemListView, BASE, server(() => page(1)))
    await loaded(en.cms.items.list.loading)

    await fireEvent.update(screen.getByLabelText(en.cms.items.list.searchLabel), 'héllo')
    expect(router.currentRoute.value.query.q).toBeUndefined()
    await waitFor(() => expect(router.currentRoute.value.query.q).toBe('héllo'), { timeout: 2000 })
  })

  it('builds filters from the schema and says when nothing matches', async () => {
    const { router } = await renderAt(ItemListView, `${BASE}?status=draft`, server(() => page(0)))
    await loaded(en.cms.items.list.loading)

    expect(screen.getByRole('combobox', { name: 'Category' })).toBeTruthy()
    expect(screen.getByRole('combobox', { name: 'Featured' })).toBeTruthy()
    expect(screen.getByText(en.cms.items.list.noResults)).toBeTruthy()

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.list.clearFilters }))
    await waitFor(() => expect(router.currentRoute.value.query.status).toBeUndefined())
  })

  it('says the collection is empty when no filter narrows it', async () => {
    await renderAt(ItemListView, BASE, server(() => page(0)))
    await loaded(en.cms.items.list.loading)
    expect(screen.getByText(en.cms.items.list.empty)).toBeTruthy()
  })

  it('maps the address and the list query both ways', () => {
    const query = { q: 'a', status: 'published' as const, attention: true, scheduled: 'any' as const, fields: { f4: 'true' }, sort: 'field.f3', dir: 'asc' as const, page: 3, pageSize: 100 as const }
    expect(fromRoute(toRoute(query) as never)).toEqual(query)
    expect(fromRoute({ status: 'nonsense', size: '30', page: '-2' })).toMatchObject({ status: undefined, pageSize: 25, page: 1 })
  })
})
