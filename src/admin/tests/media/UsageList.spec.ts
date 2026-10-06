import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import UsageList from '../../src/features/media/UsageList.vue'
import type { MediaUsage } from '../../src/api/media'
import en from '../../src/i18n/en.json'

// 013 US5 (tasks T095): where a file is used, grouped and badged, with links to the editors.

const SITE = '11111111-1111-1111-1111-111111111111'
const FILE = '22222222-2222-2222-2222-222222222222'
const PAGE = '33333333-3333-3333-3333-333333333333'
const COMPONENT = '44444444-4444-4444-4444-444444444444'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const usage: MediaUsage = {
  summary: { places: 3, draftPlaces: 2, livePlaces: 2, historyOnlyPlaces: 1, inLiveUse: true },
  places: [
    { kind: 'page', id: PAGE, title: 'Launch <b>day</b>', inDraft: true, live: true, inHistory: false, livePagesThroughComponent: null },
    { kind: 'page', id: '55555555-5555-5555-5555-555555555555', title: 'Archive', inDraft: false, live: false, inHistory: true, livePagesThroughComponent: null },
    { kind: 'component', id: COMPONENT, title: 'Banner', inDraft: true, live: true, inHistory: false, livePagesThroughComponent: 4 },
  ],
}

function stub(body: MediaUsage) {
  vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, json: async () => body })))
}

async function renderList(canDelete = true) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/sites/:siteId/pages/:pageId/edit', name: 'page-editor', component: { template: '<div />' } },
      { path: '/sites/:siteId/components/:componentId/edit', name: 'component-editor', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  return render(UsageList, {
    props: { siteId: SITE, file: { id: FILE, contentVersion: 1, canDelete } },
    global: { plugins: [i18n, router] },
  })
}

describe('UsageList', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('groups pages and components with draft, live and history badges and editor links, accessibly', async () => {
    stub(usage)
    const { container } = await renderList()

    const launch = await screen.findByRole('link', { name: 'Launch <b>day</b>' })
    expect(launch.getAttribute('href')).toBe(`/sites/${SITE}/pages/${PAGE}/edit`)
    expect(screen.getByRole('link', { name: 'Banner' }).getAttribute('href')).toBe(`/sites/${SITE}/components/${COMPONENT}/edit`)

    const launchItem = launch.closest('li')!
    expect(within(launchItem).getByText(en.media.usage.badges.live)).toBeTruthy()
    expect(within(launchItem).getByText(en.media.usage.badges.draft)).toBeTruthy()
    expect(within(screen.getByRole('link', { name: 'Archive' }).closest('li')!).getByText(en.media.usage.badges.history)).toBeTruthy()
    expect(screen.getByText('on 4 live pages')).toBeTruthy()

    expect(screen.getByRole('heading', { name: en.media.usage.pages })).toBeTruthy()
    expect(screen.getByRole('heading', { name: en.media.usage.components })).toBeTruthy()
    expect(container.querySelector('b')).toBeNull()

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['link-name', 'list', 'listitem', 'heading-order'] } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('shows counts alone to someone who may not see the pages, and the live rule when it applies', async () => {
    stub({ summary: usage.summary, places: [] })
    await renderList(false)

    await waitFor(() => expect(screen.getByText(/Used in 3 place/)).toBeTruthy())
    expect(screen.queryAllByRole('link')).toHaveLength(0)
    expect(screen.getByText(en.media.usage.liveRule)).toBeTruthy()
  })

  it('says when nothing uses the file', async () => {
    stub({ summary: { places: 0, draftPlaces: 0, livePlaces: 0, historyOnlyPlaces: 0, inLiveUse: false }, places: [] })
    await renderList()

    await waitFor(() => expect(screen.getByText(en.media.usage.none)).toBeTruthy())
    expect(screen.queryByText(en.media.usage.liveRule)).toBeNull()
  })
})
