import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ItemPublishBar from '../../../src/features/cms/items/ItemPublishBar.vue'
import ItemStatusBadge from '../../../src/features/cms/items/ItemStatusBadge.vue'
import type { Item } from '../../../src/api/items'
import { COLLECTION, en, i18n, item, problem, SITE, stubFetch, violations, type Call, type Reply } from './itemHarness'

// 015 US2: publishing and unpublishing, each confirmed with what it does; a conflict retried once.

async function renderBar(current: Item, reply: (call: Call) => Reply, dirty = false) {
  const calls = stubFetch(reply)
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })
  await router.push('/')
  const result = render(ItemPublishBar, { props: { siteId: SITE, collectionId: COLLECTION, item: current, dirty }, global: { plugins: [i18n, router] } })
  return { ...result, calls }
}

describe('ItemPublishBar', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('publishes after confirming, says which references are not published, and passes the accessibility check', async () => {
    const published = item({ status: 'published', liveVersion: 1, unpublishedReferences: [{ id: 'p', collectionId: 'c', name: 'Ann', slug: 'ann', status: 'draft' }] })
    const { emitted, container } = await renderBar(item(), (call) => (call.url.endsWith('/publish') ? { status: 200, body: published } : { status: 200, body: {} }))

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.publish }))
    expect(screen.getByRole('alertdialog')).toBeTruthy()
    expect(await violations(container)).toEqual([])
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    expect(screen.getByRole('status').textContent).toContain('Ann')
  })

  it('warns when the published address changes and when a scheduled publish is cancelled', async () => {
    const current = item({ status: 'changed', slug: 'new-slug', publishedSlug: 'old-slug', schedules: [{ action: 'publish', dueAtUtc: '2030-01-01T09:00:00Z', setBy: { id: 'u', name: 'Dana' } }] })
    const { calls } = await renderBar(current, () => ({ status: 200, body: item({ status: 'published' }) }))

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.publish }))
    const body = screen.getByRole('alertdialog').textContent ?? ''
    expect(body).toContain('old-slug')
    expect(body).toContain('new-slug')
    expect(body).toContain('This cancels the publish scheduled')

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))
    await waitFor(() => expect(calls.find((c) => c.url.endsWith('/publish'))?.body).toEqual({ revision: 3, cancelSchedule: true, redirectOldAddress: false }))
  })

  it('offers to redirect a live item’s old address, ticked, and says the redirect was asked for', async () => {
    const current = item({ status: 'changed', slug: 'new-slug', publishedSlug: 'old-slug', address: { path: '/blog/old-slug', reason: null } })
    const { calls } = await renderBar(current, () => ({ status: 200, body: item({ status: 'published', redirectRequested: true }) }))

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.publish }))
    expect(screen.getByRole<HTMLInputElement>('checkbox').checked).toBe(true)
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))

    await waitFor(() => expect(calls.find((c) => c.url.endsWith('/publish'))?.body).toEqual({ revision: 3, cancelSchedule: false, redirectOldAddress: true }))
    expect((await screen.findByRole('status')).textContent).toContain(en.redirects.offer.itemRequested)
  })

  it('offers no redirect when the item has no address visitors use', async () => {
    const current = item({ status: 'changed', slug: 'new-slug', publishedSlug: 'old-slug', address: { path: null, reason: 'no_template' } })
    await renderBar(current, () => ({ status: 200, body: item({ status: 'published' }) }))

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.publish }))

    expect(screen.queryByRole('checkbox')).toBeNull()
  })

  it('retries a publish once after a conflict when the content is unchanged', async () => {
    let attempt = 0
    const { calls } = await renderBar(item(), (call) => {
      if (call.url.endsWith('/publish')) return attempt++ === 0 ? problem(409, 'cms.item_conflict', { currentRevision: 4 }) : { status: 200, body: item({ status: 'published', revision: 5, liveVersion: 2 }) }
      return { status: 200, body: item({ revision: 4 }) }
    })

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.publish }))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))

    await waitFor(() => expect(screen.getByRole('status').textContent).toContain(en.cms.items.publish.retried))
    expect(calls.filter((c) => c.url.endsWith('/publish')).map((c) => c.body?.revision)).toEqual([3, 4])
  })

  it('does not retry when the other change altered the content', async () => {
    const { calls } = await renderBar(item(), (call) =>
      call.url.endsWith('/publish') ? problem(409, 'cms.item_conflict') : { status: 200, body: item({ revision: 4, slug: 'changed-by-someone' }) })

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.publish }))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain(en.cms.items.publish.retriedChanged))
    expect(calls.filter((c) => c.url.endsWith('/publish'))).toHaveLength(1)
  })

  it('counts the published items that refer to it before unpublishing', async () => {
    await renderBar(item({ status: 'published' }), (call) =>
      call.url.includes('/referrers?published=true') ? { status: 200, body: { referrers: [], total: 2 } } : { status: 200, body: item() })

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.unpublish }))
    await waitFor(() => expect(screen.getByRole('alertdialog').textContent).toContain('2 published items refer to it.'))
  })

  it('cannot publish unsaved changes', async () => {
    await renderBar(item(), () => ({ status: 200, body: {} }), true)
    expect((screen.getByRole('button', { name: en.cms.items.publish.publish }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('shows a refusal that needs attention', async () => {
    await renderBar(item(), (call) => (call.url.endsWith('/publish') ? problem(409, 'cms.item_needs_attention', { report: { problems: [] } }) : { status: 200, body: {} }))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.publish }))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(en.cms.items.errors.item_needs_attention))
  })
})

describe('ItemStatusBadge', () => {
  it.each(['draft', 'published', 'changed'] as const)('shows %s', (status) => {
    render(ItemStatusBadge, { props: { item: item({ status, attention: status === 'changed' }) }, global: { plugins: [i18n] } })
    expect(screen.getByText(en.cms.items.status[status])).toBeTruthy()
    if (status === 'changed') expect(screen.getByText(en.cms.items.status.attention)).toBeTruthy()
  })
})
