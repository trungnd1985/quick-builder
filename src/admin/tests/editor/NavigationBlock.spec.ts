import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { MENU, SITE, documentWith, mountEditor, stubServer } from './harness'

// 024 US2 (tasks T040): a Navigation block arrives from the palette with the site's first menu,
// draws that menu's draft with the published page's classes, never navigates, and says so when
// its menu is gone or not yet published. A site without menus cannot have one yet.

const OTHER = '0192f5a0-0000-7000-8000-00000000d002'
const MENUS = `GET /api/v1/pages/sites/${SITE}/menus`

const publication = (state: 'draft' | 'published') => ({
  state,
  liveNumber: state === 'published' ? 1 : null,
  liveSinceUtc: null,
  liveBy: null,
  hasUnpublishedChanges: false,
  liveUrl: null,
  pendingSchedules: [],
  failedSchedule: null,
})

const summary = (id: string, name: string, state: 'draft' | 'published' = 'published') => ({
  id, name, entryCount: 2, brokenCount: 0, usedBy: 0, version: 1, draftVersion: 1, createdAtUtc: 'x', updatedAtUtc: 'x', publication: publication(state),
})

const menuList = (...items: object[]) => ({ status: 200, body: { items, limit: 50, permissions: { canManage: true, canPublish: true } } })

const preview = {
  status: 200,
  body: {
    menuId: MENU,
    name: 'Main',
    entries: [
      { id: 'a', label: 'Home', kind: 'page', state: 'live', hidden: false, newTab: false, children: [] },
      {
        id: 'b', label: 'Products', kind: 'none', state: 'live', hidden: false, newTab: false,
        children: [{ id: 'c', label: 'Chairs', kind: 'page', state: 'not_published', hidden: false, newTab: false, children: [] }],
      },
    ],
  },
}

const nav = (props: object = {}) => ({ id: 'nav', type: 'navigation', props: { menuId: MENU, ...props }, children: [] })
const page = (...blocks: object[]) => documentWith({ id: 's1', type: 'section', props: {}, children: blocks })
const option = (container: Element, type: string) => container.querySelector<HTMLElement>(`[role="option"][data-block-type="${type}"]`)!
const panel = () => screen.getByRole('region', { name: en.editor.panel.title })
/** The panel's field for a prop, once the panel shows it. */
const field = (prop: string) => waitFor(() => {
  const found = panel().querySelector<HTMLElement>(`[data-prop="${prop}"]`)
  if (!found) throw new Error(`no ${prop} field yet`)
  return found
})
const canvasNav = (container: Element) => container.querySelector<HTMLElement>('.editor-canvas-scroll nav.qb-nav')

describe('the Navigation block in the editor', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('arrives from the palette with the site\'s first menu', async () => {
    const server = stubServer(page())
    server.script(MENUS, menuList(summary(MENU, 'Main'), summary(OTHER, 'Footer')))
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('s1')

    const item = option(container, 'navigation')
    await waitFor(() => expect(server.calls.some((c) => c.url.endsWith('/menus'))).toBe(true))
    item.focus()
    await fireEvent.keyDown(item, { key: 'Enter' })

    const added = store.root!.children[0].children[0]
    expect(added.type).toBe('navigation')
    expect(added.props.menuId).toBe(MENU)
  })

  it('is refused, with a reason, on a site without menus', async () => {
    const server = stubServer(page())
    server.script(MENUS, menuList())
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('s1')
    await waitFor(() => expect(server.calls.some((c) => c.url.endsWith('/menus'))).toBe(true))
    const before = store.root

    const item = option(container, 'navigation')
    item.focus()
    await fireEvent.keyDown(item, { key: 'Enter' })

    await waitFor(() => expect(screen.getByText(en.editor.errors.ops.no_menus)).toBeTruthy())
    expect(store.root).toBe(before)
  })

  it('draws the menu\'s draft with the renderer\'s classes, and nothing on it navigates', async () => {
    const server = stubServer(page(nav({ layout: 'horizontal', align: 'start' })))
    server.script(`GET /api/v1/pages/sites/${SITE}/menus/${MENU}/preview`, preview)
    const { container } = await mountEditor()

    await waitFor(() => expect(canvasNav(container)?.textContent).toContain('Chairs'))
    const drawn = canvasNav(container)!
    expect(drawn.classList.contains('qb-nav--horizontal')).toBe(true)
    expect(drawn.getAttribute('aria-label')).toBe('Main')
    expect(drawn.querySelector('.qb-nav__list--l1')).not.toBeNull()
    expect(drawn.querySelector('.qb-nav__list--l2')).not.toBeNull()
    // A label-only parent opens its submenu with its own label.
    expect(drawn.querySelector('summary.qb-nav__expander--label')?.textContent?.trim()).toBe('Products')
    // A target that is not live is marked, so the designer sees what visitors will not.
    expect(drawn.querySelector('.qb-editor-nav-not-live')?.textContent).toContain('Chairs')
    // Inert: no links at all.
    expect(drawn.querySelectorAll('a').length).toBe(0)
  })

  it('collapses behind its menu button when asked, below the chosen width', async () => {
    stubServer(page(nav({ collapse: { enabled: true, below: 'tablet', buttonLabel: 'Browse' } })))
    const { container } = await mountEditor()

    await waitFor(() => expect(canvasNav(container)?.querySelector('.qb-nav__toggle')).not.toBeNull())
    const drawn = canvasNav(container)!
    expect(drawn.classList.contains('qb-nav--collapse-tablet')).toBe(true)
    expect(drawn.querySelector('.qb-nav__toggle')!.textContent?.trim()).toBe('Browse')
  })

  it('marks a menu that no longer exists, and keeps it chosen in the picker', async () => {
    const server = stubServer(page(nav({ menuId: OTHER })))
    server.script(MENUS, menuList(summary(MENU, 'Main')))
    const { container } = await mountEditor()
    const store = useEditorStore()

    await waitFor(() => expect(canvasNav(container)?.textContent).toContain(en.editor.navigation.missing))

    store.select('nav')
    const select = within(await field('menuId')).getByRole('combobox') as HTMLSelectElement
    expect(select.value).toBe(OTHER)
    expect(select.getAttribute('aria-invalid')).toBe('true')
    expect(within(select).getByText(en.editor.navigation.missingOption)).toBeTruthy()
  })

  it('says a menu that was never published must be published first', async () => {
    const server = stubServer(page(nav()))
    server.script(MENUS, menuList(summary(MENU, 'Main', 'draft')))
    const { container } = await mountEditor()

    await waitFor(() => expect(canvasNav(container)?.textContent).toContain(en.editor.navigation.unpublished))
  })

  it('changes menu from the picker, and sets the collapse from its control', async () => {
    const server = stubServer(page(nav()))
    server.script(MENUS, menuList(summary(MENU, 'Main'), summary(OTHER, 'Footer', 'draft')))
    await mountEditor()
    const store = useEditorStore()
    store.select('nav')

    const select = within(await field('menuId')).getByRole('combobox') as HTMLSelectElement
    await waitFor(() => expect(select.options.length).toBe(2))
    expect(select.options[1].textContent?.trim()).toBe(en.editor.navigation.draftOption.replace('{name}', 'Footer'))
    await fireEvent.update(select, OTHER)
    expect(store.root!.children[0].children[0].props.menuId).toBe(OTHER)

    const collapseField = await field('collapse')
    await fireEvent.click(within(collapseField).getByLabelText(en.editor.navigation.collapse))
    expect(store.root!.children[0].children[0].props.collapse).toEqual({ enabled: true, below: 'mobile' })

    await fireEvent.update(within(collapseField).getByLabelText(en.editor.navigation.below), 'tablet')
    expect(store.root!.children[0].children[0].props.collapse).toEqual({ enabled: true, below: 'tablet' })
  })

  it('has no accessibility violations on the canvas or in its panel', async () => {
    const server = stubServer(page(nav({ collapse: { enabled: true, below: 'mobile' } })))
    server.script(`GET /api/v1/pages/sites/${SITE}/menus/${MENU}/preview`, preview)
    const { container } = await mountEditor()
    const store = useEditorStore()
    await waitFor(() => expect(canvasNav(container)?.textContent).toContain('Home'))
    store.select('nav')
    await field('menuId')

    const results = await axe.run(container as HTMLElement, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })
})
