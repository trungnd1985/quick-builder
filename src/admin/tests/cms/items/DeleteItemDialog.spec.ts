import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import DeleteItemDialog from '../../../src/features/cms/items/DeleteItemDialog.vue'
import type { Item } from '../../../src/api/items'
import { COLLECTION, en, i18n, item, ITEM, problem, SITE, stubFetch, violations, type Reply } from './itemHarness'

// 015 US5: deleting is confirmed, says when the item is live, and lists the items that refer to it.

const NO_MENUS: Reply = { status: 200, body: { items: [] } }

async function renderDialog(current: Item, reply: Reply, menus: Reply = NO_MENUS) {
  const calls = stubFetch((call) => (call.url.includes('/menus/references') ? menus : reply))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/sites/:siteId/collections/:collectionId/items/:itemId', name: 'site-collection-item', component: { template: '<div />' } },
      { path: '/sites/:siteId/menus/:menuId', name: 'site-menu', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  const result = render(DeleteItemDialog, { props: { siteId: SITE, collectionId: COLLECTION, item: current }, global: { plugins: [i18n, router] } })
  return { ...result, calls }
}

describe('DeleteItemDialog', () => {
  it('says the item is live, deletes it with its revision, and passes the accessibility check', async () => {
    const { emitted, calls, baseElement } = await renderDialog(item({ status: 'published' }), { status: 204 })

    expect(screen.getByText(en.cms.items.delete.live)).toBeTruthy()
    expect(await violations(baseElement)).toEqual([])
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.delete.delete }))

    await waitFor(() => expect(emitted().deleted).toBeTruthy())
    expect(calls.find((c) => c.method === 'DELETE')!.url).toContain('?revision=3')
  })

  it('names the menus that point at the item before it is deleted (024 FR-026)', async () => {
    const menus = [
      { menuId: 'm1', name: 'Footer', inDraft: true, inLive: false },
      { menuId: 'm2', name: 'Main', inDraft: true, inLive: true },
    ]
    const { calls } = await renderDialog(item({ status: 'published' }), { status: 204 }, { status: 200, body: { items: menus } })

    expect(await screen.findByRole('link', { name: 'Main' })).toBeTruthy()
    expect(screen.getByRole('link', { name: en.menus.references.draftOnly.replace('{name}', 'Footer') })).toBeTruthy()
    expect(screen.getByText(en.menus.references.delete.split(' | ')[1].replace('{count}', '2'))).toBeTruthy()
    expect(calls.some((c) => c.url.endsWith(`/menus/references?itemId=${ITEM}`))).toBe(true)
  })

  it('lists the referring items when refused, with links', async () => {
    const referrers = [{ itemId: 'r1', itemName: 'A post', itemSlug: 'a-post', collectionId: COLLECTION, collectionName: 'Blog posts', fieldId: 'f9', fieldName: 'Author', inDraft: true, inPublished: true, inScheduled: false }]
    await renderDialog(item(), problem(409, 'cms.item_referenced', { referrers, total: 7 }))

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.delete.delete }))

    await waitFor(() => expect(screen.getByRole('link', { name: 'A post' })).toBeTruthy())
    expect(screen.getByText(/Blog posts, Author: draft, published/)).toBeTruthy()
    expect(screen.getByText('and 6 more')).toBeTruthy()
    expect(screen.queryByRole('button', { name: en.cms.items.delete.delete })).toBeNull()
  })
})
