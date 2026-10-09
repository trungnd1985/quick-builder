import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import PublishMenu from '../../src/features/publishing/PublishMenu.vue'
import type { Publication } from '../../src/api/publishing'
import en from '../../src/i18n/en.json'
import { i18n } from '../editor/harness'

// 012 FR-039, research PR-20: the publishing actions every page, component and the editor offer.

const SITE = 'site-1'
const PAGE = 'page-1'

const draft: Publication = {
  state: 'draft',
  liveNumber: null,
  liveSinceUtc: null,
  liveBy: null,
  hasUnpublishedChanges: false,
  liveUrl: null,
  pendingSchedules: [],
  failedSchedule: null,
}

const published = (number: number): Publication => ({
  ...draft,
  state: 'published',
  liveNumber: number,
  liveSinceUtc: '2026-09-29T09:00:00Z',
  liveBy: { id: 'u', displayName: 'Dana' },
  liveUrl: 'https://example.test/about',
})

interface Call { url: string; method: string; body?: Record<string, unknown> }

function stubFetch(publishReply: { status: number; body: unknown }): Call[] {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    calls.push({ url, method, body: init?.body ? JSON.parse(String(init.body)) : undefined })
    if (url.endsWith('/draft')) {
      return new Response(JSON.stringify({ schemaVersion: 1, version: 42, updatedAtUtc: '', updatedBy: { id: 'u', displayName: 'Dana' }, document: {} }), { status: 200 })
    }
    return new Response(JSON.stringify(publishReply.body), { status: publishReply.status, headers: { 'Content-Type': 'application/json' } })
  }))
  return calls
}

function mount(props: Partial<InstanceType<typeof PublishMenu>['$props']> = {}) {
  return render(PublishMenu, {
    props: { siteId: SITE, kind: 'page', subjectId: PAGE, title: 'About', publication: draft, canPublish: true, ...props },
    global: { plugins: [i18n] },
  })
}

describe('PublishMenu', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('publishes the saved draft version and reports the new version', async () => {
    const calls = stubFetch({ status: 200, body: published(1) })
    const { emitted } = mount()

    await fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    const publish = calls.find((c) => c.method === 'POST')!
    expect(publish.url).toBe(`/api/v1/pages/sites/${SITE}/pages/${PAGE}/publish`)
    expect(publish.body).toEqual({ draftVersion: 42 })
    expect((emitted().changed as Publication[][])[0]![0]!.liveNumber).toBe(1)
    expect(screen.getByRole('status').textContent).toContain('Published version 1.')
  })

  it('publishes what the editor saved when it saves first', async () => {
    const calls = stubFetch({ status: 200, body: published(2) })
    const beforePublish = vi.fn(async () => 7)
    mount({ beforePublish })

    await fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(beforePublish).toHaveBeenCalledOnce()
    expect(calls.find((c) => c.method === 'POST')!.body).toEqual({ draftVersion: 7 })
    expect(calls.some((c) => c.url.endsWith('/draft'))).toBe(false)
  })

  it('publishes a component from its own draft and address', async () => {
    const calls = stubFetch({ status: 200, body: published(1) })
    mount({ kind: 'component', subjectId: 'component-1', title: 'Header' })

    await fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls[0]!.url).toBe(`/api/v1/pages/sites/${SITE}/components/component-1/draft`)
    expect(calls.find((c) => c.method === 'POST')!.url).toBe(`/api/v1/pages/sites/${SITE}/components/component-1/publish`)
  })

  it('says how many published pages still use a component', async () => {
    stubFetch({ status: 409, body: { title: 'Conflict', status: 409, errorCode: 'pages.component_in_use_by_published_pages', pageCount: 3 } })
    mount({ kind: 'component', subjectId: 'component-1', title: 'Header' })

    await fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

    expect(await screen.findByRole('alert')).toHaveProperty('textContent', '3 published page(s) use this component. Unpublish them or remove it from them first.')
  })

  it('unpublishes only after confirmation', async () => {
    const calls = stubFetch({ status: 200, body: draft })
    const { emitted } = mount({ publication: published(2) })

    await fireEvent.click(screen.getByRole('button', { name: 'Unpublish' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Unpublish About? Visitors get the not-found page from the next request.')
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Unpublish' }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    expect(calls.find((c) => c.method === 'POST')!.url).toBe(`/api/v1/pages/sites/${SITE}/pages/${PAGE}/unpublish`)
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(screen.getByRole('status').textContent).toContain('Unpublished.')
  })

  it('offers unpublishing only for what is live, and only to publishers', () => {
    stubFetch({ status: 200, body: {} })
    mount({ publication: draft })
    expect(screen.queryByRole('button', { name: 'Unpublish' })).toBeNull()
  })

  it('shows a refusal in words, from the translation lookup', async () => {
    stubFetch({ status: 409, body: { title: 'Conflict', status: 409, errorCode: 'pages.unpublished_components', componentIds: ['a', 'b'] } })
    mount()

    await fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

    expect(await screen.findByRole('alert')).toHaveProperty('textContent', '2 shared component(s) this uses are not published. Publish them first.')
  })

  it('names how many images need alternative text and reports where they are (035 FR-014)', async () => {
    const pointers = ['/root/children/0/children/1/props/content/content/0', '/root/children/0/children/1/props/content/content/3']
    stubFetch({ status: 422, body: { title: 'x', status: 422, errorCode: 'pages.image_alt_missing', problems: pointers.map((pointer) => ({ pointer })) } })
    const { emitted } = mount()

    await fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

    expect(await screen.findByRole('alert')).toHaveProperty('textContent', en.publishing.errors.pages.image_alt_missing.replace('{count}', '2'))
    expect(emitted().refused).toEqual([[pointers]])
  })

  it('offers publishing only to publishers, and the live page once published', () => {
    stubFetch({ status: 200, body: {} })
    mount({ canPublish: false, publication: published(3) })

    expect(screen.queryByRole('button', { name: 'Publish' })).toBeNull()
    const live = screen.getByRole('link', { name: 'View live page' })
    expect(live.getAttribute('href')).toBe('https://example.test/about')
    expect(live.getAttribute('rel')).toBe('noopener noreferrer')
    expect(screen.getByText('Published')).toBeTruthy()
  })

  it('has no accessibility violations', async () => {
    stubFetch({ status: 200, body: {} })
    const { container } = mount({ publication: { ...published(1), hasUnpublishedChanges: true } })

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'link-name'] },
    })

    expect(results.violations).toEqual([])
    expect(screen.getByText('Published — changes')).toBeTruthy()
  })
})
