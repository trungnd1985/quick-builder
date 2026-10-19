import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import axe from 'axe-core'
import MenuEditorView from '../../src/features/menus/MenuEditorView.vue'
import type { MenuDocument } from '../../src/api/menus'
import en from '../../src/i18n/en.json'
import { COLLECTION, designer, editor, ITEM, menu, MENU, PAGE_ABOUT, PAGE_HOME, pageTree, problem, SITE, stubFetch, type Call, type Reply } from './menuFixtures'

// 024 US1 (FR-006 to FR-011, FR-015, FR-025): building a menu's entries — every target kind,
// three levels at most, labels where needed, anchors without #, every invalid entry marked —
// saving with the draft version, and a read-only view for Editors.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
const DRAFT = `/api/v1/pages/sites/${SITE}/menus/${MENU}/draft`

afterEach(() => vi.unstubAllGlobals())

const empty: MenuDocument = { schemaVersion: 1, entries: [] }

function serve(document: MenuDocument, permissions = designer, extra: Partial<ReturnType<typeof menu>> = {}) {
  return (method: string, url: string, body: unknown): Reply => {
    if (url === '/api/v1/sites') return { status: 200, body: [{ id: SITE, name: 'Acme', hostNames: ['acme.test', 'www.acme.test'] }] }
    if (url.endsWith('/pages')) return { status: 200, body: pageTree }
    if (url.endsWith('/cms/sites/' + SITE + '/collections')) return { status: 200, body: { collections: [{ id: COLLECTION, name: 'Posts', slug: 'posts', fieldCount: 2, itemCount: 1, revision: 1, updatedAtUtc: 'x' }], canManage: false } }
    if (url.includes('/items')) return { status: 200, body: { items: [{ id: ITEM, name: 'Hello world', slug: 'hello-world', status: 'published' }], total: 1, page: 1, pageSize: 25 } }
    if (method === 'PUT' && url.endsWith('/draft')) return { status: 200, body: menu((body as { document: MenuDocument }).document, permissions, { draftVersion: 10 }) }
    return { status: 200, body: menu(document, permissions, extra) }
  }
}

async function renderEditor() {
  setActivePinia(createPinia())
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/menus', name: 'site-menus', component: { template: '<div />' } },
      { path: '/sites/:siteId/menus/:menuId', name: 'site-menu', component: MenuEditorView },
    ],
  })
  await router.push(`/sites/${SITE}/menus/${MENU}`)
  await router.isReady()
  // Mounted through RouterView, so the editor's leave guard runs as in the app.
  const result = render({ components: { RouterView }, template: '<RouterView />' }, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: 'Menu: Main' })).toBeTruthy())
  return result
}

async function addEntry(fill: (dialog: HTMLElement) => Promise<void>, under?: string) {
  if (under) {
    await fireEvent.click(screen.getByRole('treeitem', { name: new RegExp(under) }))
    await fireEvent.click(screen.getByRole('button', { name: en.menus.tree.addChild }))
  } else {
    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.addEntry }))
  }
  const dialog = await screen.findByRole('dialog')
  await fill(dialog)
  await fireEvent.click(within(dialog).getByRole('button', { name: en.menus.entry.save }))
}

const savedDocument = (calls: Call[]) => (calls.filter((c) => c.method === 'PUT' && c.url === DRAFT).at(-1)?.body as { document: MenuDocument; version: number } | undefined)

describe('the menu editor', () => {
  it('builds entries of every kind across three levels and saves them with the draft version', async () => {
    const calls = stubFetch(serve(empty))
    await renderEditor()

    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.page), PAGE_HOME)
    })
    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.label), 'Company')
      await fireEvent.click(within(d).getByLabelText(en.menus.entry.kinds.none))
    })
    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.page), PAGE_ABOUT)
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.pageAnchor), '#team')
    }, 'Company')
    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.label), 'Status')
      await fireEvent.click(within(d).getByLabelText(en.menus.entry.kinds.url))
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.address), 'https://status.example')
      await fireEvent.click(within(d).getByLabelText(en.menus.entry.newTab))
    }, 'About')
    await addEntry(async (d) => {
      await fireEvent.click(within(d).getByLabelText(en.menus.entry.kinds.item))
      await fireEvent.update(await within(d).findByLabelText(en.menus.entry.collection), COLLECTION)
      await waitFor(() => expect(within(d).getByRole('option', { name: /Hello world/ })).toBeTruthy())
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.itemField), ITEM)
    })

    // The third level takes no children.
    await fireEvent.click(screen.getByRole('treeitem', { name: /Status/ }))
    expect((screen.getByRole('button', { name: en.menus.tree.addChild }) as HTMLButtonElement).disabled).toBe(true)

    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.save }))
    await waitFor(() => expect(savedDocument(calls)).toBeDefined())

    const sent = savedDocument(calls)!
    expect(sent.version).toBe(9)
    const [home, company, item] = sent.document.entries
    expect(home.target).toEqual({ type: 'page', pageId: PAGE_HOME })
    expect(home.label).toBeUndefined()
    expect(company).toMatchObject({ label: 'Company', target: { type: 'none' } })
    expect(company.children[0].target).toEqual({ type: 'page', pageId: PAGE_ABOUT, anchor: 'team' })
    expect(company.children[0].children[0]).toMatchObject({ label: 'Status', target: { type: 'url', href: 'https://status.example' }, newTab: true })
    expect(item.target).toEqual({ type: 'item', collectionId: COLLECTION, itemId: ITEM })
    expect(await screen.findByText(en.menus.editor.saved)).toBeTruthy()
  })

  it('does not offer template pages', async () => {
    stubFetch(serve(empty))
    await renderEditor()

    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.addEntry }))
    const pages = within(await screen.findByRole('dialog')).getByLabelText(en.menus.entry.page)
    expect(within(pages).queryByRole('option', { name: /Post template/ })).toBeNull()
  })

  it('needs a label where the contract does, and a valid section name', async () => {
    stubFetch(serve(empty))
    await renderEditor()

    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.addEntry }))
    const dialog = await screen.findByRole('dialog')
    await fireEvent.click(within(dialog).getByLabelText(en.menus.entry.kinds.url))
    await fireEvent.update(within(dialog).getByLabelText(en.menus.entry.address), 'https://example.com')
    await fireEvent.click(within(dialog).getByRole('button', { name: en.menus.entry.save }))
    expect(within(dialog).getByRole('alert').textContent).toBe(en.menus.entry.problems.label_required)

    await fireEvent.click(within(dialog).getByLabelText(en.menus.entry.kinds.anchor))
    await fireEvent.update(within(dialog).getByLabelText(en.menus.entry.label), 'Pricing')
    await fireEvent.update(within(dialog).getByLabelText(en.menus.entry.anchor), '1st')
    await fireEvent.click(within(dialog).getByRole('button', { name: en.menus.entry.save }))
    expect(within(dialog).getByRole('alert').textContent).toBe(en.menus.entry.problems.anchor_invalid)
  })

  it('hints at a page target for an address on the site itself', async () => {
    stubFetch(serve(empty))
    await renderEditor()

    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.addEntry }))
    const dialog = await screen.findByRole('dialog')
    await fireEvent.click(within(dialog).getByLabelText(en.menus.entry.kinds.url))
    await waitFor(() => expect(within(dialog).queryByText(en.menus.entry.ownHostHint)).toBeNull())

    await fireEvent.update(within(dialog).getByLabelText(en.menus.entry.address), 'https://www.acme.test/about')
    expect(await within(dialog).findByText(en.menus.entry.ownHostHint)).toBeTruthy()

    await fireEvent.update(within(dialog).getByLabelText(en.menus.entry.address), 'https://elsewhere.example')
    await waitFor(() => expect(within(dialog).queryByText(en.menus.entry.ownHostHint)).toBeNull())
  })

  it('marks every invalid entry and refuses to save until they are fixed', async () => {
    const calls = stubFetch(serve({
      schemaVersion: 1,
      entries: [
        { id: 'a', target: { type: 'url', href: 'https://example.com' }, children: [] },
        { id: 'b', label: 'Fine', target: { type: 'none' }, children: [] },
        { id: 'c', label: 'Group', target: { type: 'none' }, newTab: true, children: [] },
      ],
    }))
    await renderEditor()

    expect(screen.getAllByText(/^Problem:/)).toHaveLength(2)
    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.save }))
    expect(screen.getAllByRole('alert').some((a) => a.textContent === en.menus.editor.problems)).toBe(true)
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
  })

  it('reports a stale draft and offers to reload', async () => {
    stubFetch(serve(empty), { [`PUT ${DRAFT}`]: [problem(409, 'pages.out_of_date')] })
    await renderEditor()

    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.page), PAGE_HOME)
    })
    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.save }))

    expect((await screen.findByText(en.menus.outOfDate))).toBeTruthy()
  })

  it('summarises entries whose page was deleted', async () => {
    stubFetch(serve(
      { schemaVersion: 1, entries: [{ id: 'gone', target: { type: 'page', pageId: PAGE_ABOUT }, children: [] }] },
      designer,
      { targets: [{ kind: 'page', id: PAGE_ABOUT, state: 'deleted', title: null }] },
    ))
    await renderEditor()

    expect(screen.getByRole('note').textContent).toContain('1 entry points at a page or item that was deleted')
    expect(within(screen.getByRole('treeitem')).getByText(en.menus.tree.states.deleted)).toBeTruthy()
  })

  it('publishes what the designer sees: pending changes are saved first, then that draft version', async () => {
    const PUBLISH = `/api/v1/pages/sites/${SITE}/menus/${MENU}/publish`
    const published = { state: 'published', liveNumber: 1, liveSinceUtc: 'x', liveBy: null, hasUnpublishedChanges: false, liveUrl: null, pendingSchedules: [], failedSchedule: null }
    const calls = stubFetch(serve(empty), { [`POST ${PUBLISH}`]: [{ status: 200, body: published }] })
    await renderEditor()

    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.page), PAGE_HOME)
    })
    await fireEvent.click(screen.getByRole('button', { name: en.publishing.menu.publish }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.url === PUBLISH)).toBe(true))
    const saveAt = calls.findIndex((c) => c.method === 'PUT' && c.url === DRAFT)
    const publishAt = calls.findIndex((c) => c.method === 'POST' && c.url === PUBLISH)
    expect(saveAt).toBeGreaterThanOrEqual(0)
    expect(saveAt).toBeLessThan(publishAt)
    // The version the save returned, not the one the editor opened.
    expect(calls[publishAt].body).toEqual({ draftVersion: 10 })
  })

  it('does not publish when the pending changes cannot be saved', async () => {
    const PUBLISH = `/api/v1/pages/sites/${SITE}/menus/${MENU}/publish`
    const calls = stubFetch(serve(empty), { [`PUT ${DRAFT}`]: [problem(409, 'pages.out_of_date')] })
    await renderEditor()

    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.page), PAGE_HOME)
    })
    await fireEvent.click(screen.getByRole('button', { name: en.publishing.menu.publish }))

    await waitFor(() => expect(screen.getByText(en.menus.outOfDate)).toBeTruthy())
    expect(calls.some((c) => c.url === PUBLISH)).toBe(false)
  })

  describe('moving entries (US3)', () => {
    const entry = (id: string, label: string, ...children: MenuDocument['entries']) => ({ id, label, target: { type: 'none' as const }, children })
    // Alpha; Beta > Beta one > Beta deep; Gamma
    const nested: MenuDocument = { schemaVersion: 1, entries: [entry('a', 'Alpha'), entry('b', 'Beta', entry('b1', 'Beta one', entry('b11', 'Beta deep'))), entry('c', 'Gamma')] }
    const row = (container: Element, id: string) => container.querySelector<HTMLElement>(`[role="treeitem"][data-entry-id="${id}"]`)!
    const order = (container: Element) => [...container.querySelectorAll<HTMLElement>('[role="treeitem"]')].map((r) => `${r.dataset.entryId}@${r.getAttribute('aria-level')}`)
    const liveText = (container: Element) => container.querySelector('[role="status"][aria-live="polite"]')?.textContent ?? ''

    it('shows where a dragged entry lands, marks a refused place, and moves it on drop', async () => {
      stubFetch(serve(nested))
      const { container } = await renderEditor()

      // Beta cannot go inside its own subtree: the place is marked as refused.
      await fireEvent.dragStart(row(container, 'b'))
      await fireEvent.dragOver(row(container, 'b11'))
      expect(row(container, 'b11').classList.contains('menu-entry--drop-refused')).toBe(true)
      await fireEvent.drop(row(container, 'b11'))
      await waitFor(() => expect(liveText(container)).toBe(en.menus.tree.moveRefused))
      expect(order(container)).toEqual(['a@1', 'b@1', 'b1@2', 'b11@3', 'c@1'])

      // Alpha after Gamma (jsdom rows have no height, so a drop lands after the row).
      await fireEvent.dragStart(row(container, 'a'))
      await fireEvent.dragOver(row(container, 'c'))
      expect(row(container, 'c').classList.contains('menu-entry--drop-after')).toBe(true)
      expect(row(container, 'c').classList.contains('menu-entry--drop-refused')).toBe(false)
      await fireEvent.drop(row(container, 'c'))

      expect(order(container)).toEqual(['b@1', 'b1@2', 'b11@3', 'c@1', 'a@1'])
      await waitFor(() => expect(liveText(container)).toContain('Alpha moved: position 3 of 3, level 1.'))
      expect(container.querySelector('.menu-entry--drop-after')).toBeNull()
    })

    it('moves the focused entry with Alt and the arrows, announces each move, and refuses a fourth level', async () => {
      const calls = stubFetch(serve(nested))
      const { container } = await renderEditor()

      await fireEvent.keyDown(row(container, 'c'), { key: 'ArrowUp', altKey: true })
      expect(order(container)).toEqual(['a@1', 'c@1', 'b@1', 'b1@2', 'b11@3'])
      await waitFor(() => expect(liveText(container)).toContain('Gamma moved: position 2 of 3, level 1.'))

      await fireEvent.keyDown(row(container, 'c'), { key: 'ArrowRight', altKey: true })
      expect(order(container)).toEqual(['a@1', 'c@2', 'b@1', 'b1@2', 'b11@3'])

      await fireEvent.keyDown(row(container, 'c'), { key: 'ArrowLeft', altKey: true })
      await fireEvent.keyDown(row(container, 'c'), { key: 'ArrowDown', altKey: true })
      expect(order(container)).toEqual(['a@1', 'b@1', 'b1@2', 'b11@3', 'c@1'])

      // Beta into Alpha would put Beta deep at a fourth level.
      await fireEvent.keyDown(row(container, 'b'), { key: 'ArrowRight', altKey: true })
      await waitFor(() => expect(liveText(container)).toBe(en.menus.tree.moveRefused))
      expect(order(container)).toEqual(['a@1', 'b@1', 'b1@2', 'b11@3', 'c@1'])

      await fireEvent.keyDown(row(container, 'b1'), { key: 'ArrowLeft', altKey: true })
      await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.save }))
      await waitFor(() => expect(savedDocument(calls)).toBeDefined())
      expect(savedDocument(calls)!.document.entries.map((e) => e.id)).toEqual(['a', 'b', 'b1', 'c'])
      expect(savedDocument(calls)!.document.entries[2].children.map((e) => e.id)).toEqual(['b11'])
    })

    it('undoes and redoes moves with Ctrl+Z and Ctrl+Shift+Z', async () => {
      stubFetch(serve(nested))
      const { container } = await renderEditor()

      await fireEvent.keyDown(row(container, 'c'), { key: 'ArrowUp', altKey: true })
      expect(order(container)[1]).toBe('c@1')

      await fireEvent.keyDown(row(container, 'c'), { key: 'z', ctrlKey: true })
      expect(order(container)).toEqual(['a@1', 'b@1', 'b1@2', 'b11@3', 'c@1'])
      await waitFor(() => expect(liveText(container)).toBe(en.menus.tree.undone))

      await fireEvent.keyDown(row(container, 'c'), { key: 'Z', ctrlKey: true, shiftKey: true })
      expect(order(container)[1]).toBe('c@1')
      await waitFor(() => expect(liveText(container)).toBe(en.menus.tree.redone))
    })

    it('has no accessibility violations while dragging', async () => {
      stubFetch(serve(nested))
      const { container } = await renderEditor()
      await fireEvent.dragStart(row(container, 'a'))
      await fireEvent.dragOver(row(container, 'c'))

      const results = await axe.run(container, { runOnly: { type: 'rule', values: ['aria-required-children', 'aria-required-parent', 'aria-valid-attr-value', 'aria-allowed-attr', 'nested-interactive'] } })
      expect(results.violations).toEqual([])
    })
  })

  it('is read-only for an Editor', async () => {
    stubFetch(serve({ schemaVersion: 1, entries: [{ id: 'a', target: { type: 'page', pageId: PAGE_HOME }, children: [] }] }, editor))
    await renderEditor()

    expect(screen.getByText(en.menus.editor.readOnly)).toBeTruthy()
    expect(screen.queryByRole('button', { name: en.menus.editor.save })).toBeNull()
    expect(screen.queryByRole('button', { name: en.menus.editor.addEntry })).toBeNull()
  })

  it('warns before leaving with unsaved changes', async () => {
    stubFetch(serve(empty))
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await renderEditor()

    await addEntry(async (d) => {
      await fireEvent.update(within(d).getByLabelText(en.menus.entry.page), PAGE_HOME)
    })
    await fireEvent.click(screen.getByRole('link', { name: en.menus.editor.back }))

    await waitFor(() => expect(confirm).toHaveBeenCalledWith(en.menus.editor.leaveWarning))
  })

  it('has no accessibility violations', async () => {
    stubFetch(serve({ schemaVersion: 1, entries: [{ id: 'a', label: 'Home', target: { type: 'page', pageId: PAGE_HOME }, children: [] }] }))
    const { container } = await renderEditor()

    await fireEvent.click(screen.getByRole('button', { name: en.menus.editor.addEntry }))
    await screen.findByRole('dialog')
    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'aria-required-children', 'button-name', 'heading-order'] } })
    expect(results.violations).toEqual([])
  })
})
