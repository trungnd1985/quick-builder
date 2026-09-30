import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import PageTreeView from '../../src/features/pages/PageTreeView.vue'
import PageFormDialog from '../../src/features/pages/PageFormDialog.vue'
import MoveDialog from '../../src/features/pages/MoveDialog.vue'
import { liveAddressCount } from '../../src/features/publishing/addressWarning'
import type { PageNode, PagePermissions } from '../../src/api/pages'
import type { Publication } from '../../src/api/publishing'
import en from '../../src/i18n/en.json'
import { i18n } from '../editor/harness'

// 012 US4, FR-006, FR-011: a published page moves to its new address at once, so a move or
// slug change that affects published pages warns first; a published page cannot be deleted.

const SITE = '33333333-3333-3333-3333-333333333333'

const draft: Publication = {
  state: 'draft', liveNumber: null, liveSinceUtc: null, liveBy: null, hasUnpublishedChanges: false, liveUrl: null, pendingSchedules: [], failedSchedule: null,
}
const live: Publication = { ...draft, state: 'published', liveNumber: 1, liveUrl: 'https://example.test/x' }

function node(id: string, path: string, parentId: string | null, publication: Publication, position = 0): PageNode {
  const segments = path.split('/').filter(Boolean)
  return {
    id, parentId, position, depth: Math.max(segments.length, 1), title: segments.at(-1) ?? 'Home', slug: segments.at(-1) ?? 'home',
    path, isHome: path === '/', childCount: 0, version: 1, publication,
  }
}

// services (live) > hosting (live) > vps (draft); contact (draft).
const pages: PageNode[] = [
  node('home', '/', null, live, 0),
  node('services', '/services', null, live, 1),
  node('hosting', '/services/hosting', 'services', live),
  node('vps', '/services/hosting/vps', 'hosting', draft),
  node('contact', '/contact', null, draft, 2),
]

const warning = (count: number) => en.publishing.address.warning.replace('{count}', String(count))

describe('liveAddressCount', () => {
  it('counts the published pages in the subtree from the tree data', () => {
    expect(liveAddressCount(pages, 'services')).toBe(2)
    expect(liveAddressCount(pages, 'hosting')).toBe(1)
    expect(liveAddressCount(pages, 'vps')).toBe(0)
    expect(liveAddressCount(pages, 'contact')).toBe(0)
  })
})

describe('The address warning in the dialogs', () => {
  it('appears on a slug change only when published pages move', async () => {
    const props = { dialogId: 'f', heading: 'Rename', submitLabel: 'Save', addressPrefix: '', initialTitle: 'services', initialSlug: 'services', busy: false, error: null }
    const { rerender } = render(PageFormDialog, { props: { ...props, liveAddressCount: 2 }, global: { plugins: [i18n] } })

    expect(screen.queryByText(warning(2))).toBeNull()
    await fireEvent.update(screen.getByLabelText(en.pages.form.slug), 'offers')
    expect(screen.getByText(warning(2))).toBeTruthy()

    await rerender({ ...props, liveAddressCount: 0 })
    expect(screen.queryByText(warning(0))).toBeNull()
  })

  it('appears on a move only when it changes the parent of published pages', async () => {
    render(MoveDialog, {
      props: { dialogId: 'm', page: pages[2]!, pages, busy: false, error: null, liveAddressCount: 1 },
      global: { plugins: [i18n] },
    })

    expect(screen.queryByText(warning(1))).toBeNull()
    await fireEvent.update(screen.getByLabelText(en.pages.move.parent), '')
    expect(screen.getByText(warning(1))).toBeTruthy()
  })

  it('stays away from a move of unpublished pages', async () => {
    render(MoveDialog, {
      props: { dialogId: 'm', page: pages[4]!, pages, busy: false, error: null, liveAddressCount: 0 },
      global: { plugins: [i18n] },
    })

    await fireEvent.update(screen.getByLabelText(en.pages.move.parent), 'services')
    expect(screen.queryByRole('note')).toBeNull()
  })
})

describe('Deleting in the page tree', () => {
  beforeEach(() => setActivePinia(createPinia()))

  const permissions: PagePermissions = { canManageStructure: true, canEditPageDrafts: true, canManageComponents: true, canPublish: true, canViewPublishLog: false }

  async function renderTree() {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      const body = url === '/api/v1/sites' ? [] : { pages, permissions }
      return { ok: true, status: 200, statusText: 'x', json: async () => body, text: async () => JSON.stringify(body) }
    }))
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
        { path: '/sites/:siteId/pages', name: 'site-pages', component: PageTreeView },
        { path: '/sites/:siteId/pages/:pageId/edit', name: 'page-editor', component: { template: '<div />' } },
      ],
    })
    await router.push(`/sites/${SITE}/pages`)
    await router.isReady()
    render(PageTreeView, { global: { plugins: [i18n, router] } })
    await waitFor(() => expect(screen.getByText('/services/hosting/vps')).toBeTruthy())
  }

  const select = (title: string) => fireEvent.click(screen.getByRole('button', { name: en.pages.table.select.replace('{title}', title) }))

  it('is disabled for a published page, with the reason', async () => {
    await renderTree()
    await select('hosting')

    const del = screen.getByRole('button', { name: en.pages.actions.delete })
    expect(del.hasAttribute('disabled')).toBe(true)
    const reason = document.getElementById(del.getAttribute('aria-describedby')!)!
    expect(reason.textContent).toBe(en.publishing.errors.pages.page_published)
  })

  it('stays available for a page visitors cannot reach', async () => {
    await renderTree()
    await select('vps')

    const del = screen.getByRole('button', { name: en.pages.actions.delete })
    expect(del.hasAttribute('disabled')).toBe(false)
    expect(del.hasAttribute('aria-describedby')).toBe(false)
  })

  it('warns before renaming a published subtree', async () => {
    await renderTree()
    await select('services')

    await fireEvent.click(screen.getByRole('button', { name: en.pages.actions.rename }))
    const dialog = await screen.findByRole('dialog')
    await fireEvent.update(within(dialog).getByLabelText(en.pages.form.slug), 'offers')

    expect(within(dialog).getByText(warning(2))).toBeTruthy()
  })
})
