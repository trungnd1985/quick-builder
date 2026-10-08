import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import DeleteItemDialog from '../../../src/features/cms/items/DeleteItemDialog.vue'
import type { Item } from '../../../src/api/items'
import { COLLECTION, en, i18n, item, problem, SITE, stubFetch, violations, type Reply } from './itemHarness'

// 015 US5: deleting is confirmed, says when the item is live, and lists the items that refer to it.

async function renderDialog(current: Item, reply: Reply) {
  const calls = stubFetch(() => reply)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/sites/:siteId/collections/:collectionId/items/:itemId', name: 'site-collection-item', component: { template: '<div />' } },
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
    expect(calls[0]!.url).toContain('?revision=3')
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
