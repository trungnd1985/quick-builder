import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import ComponentsView from '../src/features/pages/ComponentsView.vue'
import type { Component, ComponentList, PagePermissions } from '../src/api/pages'
import en from '../src/i18n/en.json'

// 010 US4, FR-028, FR-033: shared components — create, rename, see who uses them, delete
// the unused — keyboard-usable, and read-only for an editor.

const SITE = '44444444-4444-4444-4444-444444444444'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const designer: PagePermissions = { canManageStructure: true, canEditPageDrafts: true, canManageComponents: true, canPublish: true, canViewPublishLog: false }
const editor: PagePermissions = { canManageStructure: false, canEditPageDrafts: true, canManageComponents: false, canPublish: false, canViewPublishLog: false }

function component(id: string, name: string, usageCount: number): Component {
  return {
    id,
    name,
    usageCount,
    version: 3,
    draft: { schemaVersion: 1, version: 4, updatedAtUtc: '2026-09-27T08:00:00Z', updatedBy: { id: 'u', displayName: 'Dana' } },
    createdAtUtc: '2026-09-27T08:00:00Z',
    updatedAtUtc: '2026-09-27T08:00:00Z',
    publication: { state: 'draft', liveNumber: null, liveSinceUtc: null, liveBy: null, hasUnpublishedChanges: false, liveUrl: null, pendingSchedules: [], failedSchedule: null },
  }
}

type Reply = { status: number; body?: unknown }
type Call = { url: string; method: string; body?: unknown }

function stubFetch(list: ComponentList, replies: Record<string, Reply[]> = {}) {
  const calls: Call[] = []

  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ url, method, body })

    const scripted = replies[`${method} ${url.split('?')[0]}`]?.shift()
    const reply: Reply = scripted
      ?? (url === '/api/v1/sites' ? { status: 200, body: [] }
        : url.endsWith('/usages') ? { status: 200, body: [{ id: 'p1', title: 'Home', path: '/' }] }
          : method === 'DELETE' ? { status: 204 }
            : method === 'GET' ? { status: 200, body: list }
              : { status: 200, body: component('new', body?.name ?? 'x', 0) })

    return {
      ok: reply.status < 300,
      status: reply.status,
      statusText: 'x',
      json: async () => reply.body,
      text: async () => JSON.stringify(reply.body ?? {}),
    }
  }))

  return calls
}

async function renderView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/pages', name: 'site-pages', component: { template: '<div />' } },
      { path: '/sites/:siteId/components', name: 'site-components', component: ComponentsView },
      { path: '/sites/:siteId/components/:componentId/edit', name: 'component-editor', component: { template: '<div />' } },
    ],
  })
  await router.push(`/sites/${SITE}/components`)
  await router.isReady()

  const result = render(ComponentsView, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: en.components.heading })).toBeTruthy())
  await waitFor(() => expect(screen.queryByText(en.components.loading)).toBeNull())
  return result
}

async function violations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'] },
  })
  return results.violations
}

describe('ComponentsView (US4)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('lists components with their usage and creates one', async () => {
    const calls = stubFetch({ components: [component('h', 'Header', 2)], permissions: designer })
    const { container } = await renderView()

    await waitFor(() => expect(screen.getByText('Header')).toBeTruthy())
    expect(screen.getByRole('button', { name: en.components.showUsages.replace('{name}', 'Header') }).textContent).toContain('2 pages')
    expect(await violations(container)).toEqual([])

    await fireEvent.update(screen.getByLabelText(en.components.create.name), 'Footer')
    await fireEvent.click(screen.getByRole('button', { name: en.components.create.create }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Footer' })
  })

  it('shows the empty state and a duplicate-name refusal', async () => {
    stubFetch({ components: [], permissions: designer }, {
      [`POST /api/v1/pages/sites/${SITE}/components`]: [{ status: 409, body: { errorCode: 'pages.duplicate_component_name', title: 'x', status: 409 } }],
    })
    await renderView()

    await waitFor(() => expect(screen.getByText(en.components.empty)).toBeTruthy())
    await fireEvent.update(screen.getByLabelText(en.components.create.name), 'Header')
    await fireEvent.click(screen.getByRole('button', { name: en.components.create.create }))

    await waitFor(() => expect(screen.getByText(en.pages.errors.duplicate_component_name)).toBeTruthy())
  })

  it('renames with the version it was loaded with', async () => {
    const calls = stubFetch({ components: [component('h', 'Header', 0)], permissions: designer })
    await renderView()

    await fireEvent.click(await screen.findByRole('button', { name: en.components.rename.replace('{name}', 'Header') }))
    const input = screen.getByDisplayValue('Header')
    expect(document.activeElement).toBe(input)
    await fireEvent.update(input, 'Site header')
    await fireEvent.click(screen.getByRole('button', { name: en.components.save }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ name: 'Site header', version: 3 })
  })

  it('refuses to delete a used component with its count, and lists the pages using it', async () => {
    stubFetch({ components: [component('h', 'Header', 1)], permissions: designer }, {
      [`DELETE /api/v1/pages/sites/${SITE}/components/h`]: [{ status: 409, body: { errorCode: 'pages.component_in_use', title: 'x', status: 409, usageCount: 1 } }],
    })
    const { container } = await renderView()

    await fireEvent.click(await screen.findByRole('button', { name: en.components.delete.replace('{name}', 'Header') }))
    await fireEvent.click(screen.getByRole('button', { name: en.components.confirm.delete }))
    await waitFor(() => expect(screen.getByText(en.pages.errors.component_in_use.replace('{count}', '1'))).toBeTruthy())
    await fireEvent.click(screen.getByRole('button', { name: en.components.confirm.cancel }))

    await fireEvent.click(screen.getByRole('button', { name: en.components.showUsages.replace('{name}', 'Header') }))
    await waitFor(() => expect(screen.getByText('Home')).toBeTruthy())
    expect(screen.getByText('/')).toBeTruthy()
    expect(await violations(container)).toEqual([])
  })

  it('is read-only for an editor', async () => {
    stubFetch({ components: [component('h', 'Header', 0)], permissions: editor })
    await renderView()

    await waitFor(() => expect(screen.getByText(en.components.readOnly)).toBeTruthy())
    expect(screen.queryByLabelText(en.components.create.name)).toBeNull()
    expect(screen.queryByRole('button', { name: en.components.delete.replace('{name}', 'Header') })).toBeNull()
    expect(screen.queryByRole('button', { name: en.publishing.menu.publish })).toBeNull()
    expect(screen.getByText(en.publishing.badge.draft)).toBeTruthy()
  })

  it('shows each component’s publication and publishes it from its row (012 US2)', async () => {
    const calls = stubFetch({ components: [component('h', 'Header', 1), component('f', 'Footer', 0)], permissions: designer }, {
      'GET /api/v1/pages/sites/44444444-4444-4444-4444-444444444444/components/h/draft': [{ status: 200, body: { schemaVersion: 1, version: 4, document: {} } }],
      'POST /api/v1/pages/sites/44444444-4444-4444-4444-444444444444/components/h/publish': [{ status: 200, body: {} }],
    })
    await renderView()

    const header = screen.getByRole('row', { name: /Header/ })
    expect(within(header).getByText(en.publishing.badge.draft)).toBeTruthy()

    await fireEvent.click(within(header).getByRole('button', { name: en.publishing.menu.publish }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.url.endsWith('/components/h/publish'))).toBe(true))
    expect(calls.find((c) => c.url.endsWith('/components/h/publish'))!.body).toEqual({ draftVersion: 4 })
  })
})
