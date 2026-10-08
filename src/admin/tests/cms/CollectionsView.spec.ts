import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import CollectionsView from '../../src/features/cms/CollectionsView.vue'
import type { CollectionList } from '../../src/api/cms'
import { blogSchema, collection, en, i18n, problem, SITE, stubFetch, violations, type Call, type Reply } from './harness'

// 014 US1, US5: the site's collections — listed, created with a suggested address name, and
// read-only for an Editor.

const list = (canManage: boolean, count = 1): CollectionList => ({
  canManage,
  collections: count === 0 ? [] : [{ id: 'c1', name: 'Blog posts', slug: 'blog-posts', fieldCount: 3, itemCount: 300, revision: 2, updatedAtUtc: '2026-10-07T08:00:00Z' }],
})

async function renderView(reply: (call: Call) => Reply) {
  const calls = stubFetch(reply)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/collections', name: 'site-collections', component: CollectionsView },
      { path: '/sites/:siteId/collections/:collectionId', name: 'site-collection', component: { template: '<div>builder</div>' } },
      { path: '/sites/:siteId/collections/:collectionId/items', name: 'site-collection-items', component: { template: '<div>items</div>' } },
    ],
  })
  await router.push(`/sites/${SITE}/collections`)
  await router.isReady()

  const result = render(CollectionsView, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.queryByText(en.cms.list.loading)).toBeNull())
  return { ...result, calls, router }
}

describe('CollectionsView', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('lists collections and creates one with a suggested address name', async () => {
    const { calls, router, container } = await renderView((call) =>
      call.url === '/api/v1/sites' ? { status: 200, body: [] }
        : call.method === 'POST' ? { status: 201, body: collection(blogSchema(), { id: 'new' }) }
          : { status: 200, body: list(true) })

    expect(screen.getByRole('link', { name: 'Blog posts' })).toBeTruthy()
    expect(await violations(container)).toEqual([])

    await fireEvent.update(screen.getByLabelText(en.cms.create.name), 'Job openings')
    expect((screen.getByLabelText(en.cms.create.slug) as HTMLInputElement).value).toBe('job-openings')

    await fireEvent.click(screen.getByRole('button', { name: en.cms.create.create }))
    await waitFor(() => expect(router.currentRoute.value.name).toBe('site-collection'))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Job openings', slug: 'job-openings', description: null })
  })

  it('keeps an address name typed by hand and shows a refusal', async () => {
    await renderView((call) =>
      call.url === '/api/v1/sites' ? { status: 200, body: [] }
        : call.method === 'POST' ? problem(409, 'cms.slug_taken')
          : { status: 200, body: list(true) })

    await fireEvent.update(screen.getByLabelText(en.cms.create.slug), 'blog')
    await fireEvent.update(screen.getByLabelText(en.cms.create.name), 'Blog posts')
    expect((screen.getByLabelText(en.cms.create.slug) as HTMLInputElement).value).toBe('blog')

    await fireEvent.click(screen.getByRole('button', { name: en.cms.create.create }))
    await waitFor(() => expect(screen.getByText(en.cms.errors.slug_taken)).toBeTruthy())
  })

  it('is read-only for an editor and has an empty state', async () => {
    await renderView((call) => (call.url === '/api/v1/sites' ? { status: 200, body: [] } : { status: 200, body: list(false, 0) }))

    expect(screen.getByText(en.cms.list.readOnly)).toBeTruthy()
    expect(screen.getByText(en.cms.list.empty)).toBeTruthy()
    expect(screen.queryByRole('button', { name: en.cms.create.create })).toBeNull()
  })
})
