import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import SiteDetailView from '../src/features/sites/SiteDetailView.vue'
import type { Site, SitePermissions } from '../src/api/sites'
import en from '../src/i18n/en.json'

// 009 US1, US2, US4, FR-027, SC-009: one site's settings, host names and availability.
// Every control is keyboard-usable and labelled, every string comes from the lookup, and
// what is offered follows the permissions the server returns — never the other way round.

const SITE = '22222222-2222-2222-2222-222222222222'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const everything: SitePermissions = { canManageSettings: true, canManageHostNames: true, canManageAvailability: true }
const nothing: SitePermissions = { canManageSettings: false, canManageHostNames: false, canManageAvailability: false }

function site(overrides: Partial<Site> = {}): Site {
  return {
    id: SITE,
    name: 'Client',
    hostNames: ['client.test', 'xn--bcher-kva.test'],
    createdAtUtc: '2026-09-26T08:00:00Z',
    primaryHostName: 'client.test',
    hostNameDetails: [
      { hostName: 'client.test', displayName: 'client.test', isPrimary: true },
      { hostName: 'xn--bcher-kva.test', displayName: 'bücher.test', isPrimary: false },
    ],
    scheme: 'https',
    baseUrl: 'https://client.test/',
    defaultLanguage: 'de',
    status: 'enabled',
    version: 7,
    updatedAtUtc: '2026-09-26T08:00:00Z',
    permissions: everything,
    ...overrides,
  }
}

type Reply = { status: number; body?: unknown }
type Call = { url: string; method: string; body?: unknown }

/**
 * Answers the Sites API by method and path. `replies` overrides the default for one
 * "METHOD path" key, consumed in order.
 */
function stubFetch(initial: Site, replies: Record<string, Reply[]> = {}) {
  const calls: Call[] = []
  let current = initial

  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ url, method, body })

    const key = `${method} ${url}`
    const scripted = replies[key]?.shift()
    const reply: Reply = scripted ?? (
      url === '/api/v1/sites/languages'
        ? { status: 200, body: { languages: ['de', 'en', 'fr', 'pt-BR'], default: 'en' } }
        : { status: 200, body: current })

    if (reply.status < 300 && reply.body && typeof reply.body === 'object' && 'id' in reply.body) {
      current = reply.body as Site
    }

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
      { path: '/sites', name: 'sites', component: { template: '<div />' } },
      { path: '/sites/:siteId', name: 'site', component: SiteDetailView },
      { path: '/sites/:siteId/pages', name: 'site-pages', component: { template: '<div />' } },
      { path: '/sites/:siteId/components', name: 'site-components', component: { template: '<div />' } },
      { path: '/sites/:siteId/collections', name: 'site-collections', component: { template: '<div />' } },
      { path: '/sites/:siteId/media', name: 'site-media', component: { template: '<div />' } },
      { path: '/sites/:siteId/design', name: 'site-design', component: { template: '<div />' } },
      { path: '/sites/:siteId/redirects', name: 'site-redirects', component: { template: '<div />' } },
      { path: '/sites/:siteId/menus', name: 'site-menus', component: { template: '<div />' } },
    ],
  })
  await router.push(`/sites/${SITE}`)
  await router.isReady()

  const result = render(SiteDetailView, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1 })).toBeTruthy())
  return result
}

async function baselineViolations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: {
      type: 'rule',
      values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'],
    },
  })
  return results.violations
}

describe('SiteDetailView — settings (US1)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('loads the site and saves every setting with the version it was loaded with', async () => {
    const calls = stubFetch(site())
    await renderView()

    await fireEvent.update(screen.getByLabelText(en.sites.settings.name), 'Client GmbH')
    await fireEvent.click(screen.getByRole('button', { name: en.sites.settings.save }))

    await waitFor(() => expect(screen.getByText(en.sites.settings.saved)).toBeTruthy())
    const put = calls.find((c) => c.method === 'PUT')
    expect(put?.url).toBe(`/api/v1/sites/${SITE}/settings`)
    expect(put?.body).toEqual({
      name: 'Client GmbH',
      scheme: 'https',
      defaultLanguage: 'de',
      primaryHostName: 'client.test',
      version: 7,
    })
  })

  it('offers to reload when someone else saved first, and reloading clears the prompt', async () => {
    stubFetch(site(), {
      [`PUT /api/v1/sites/${SITE}/settings`]: [{ status: 409, body: { title: 'Conflict', status: 409, errorCode: 'sites.out_of_date' } }],
    })
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: en.sites.settings.save }))

    await waitFor(() => expect(screen.getByText(en.sites.settings.outOfDate)).toBeTruthy())
    await fireEvent.click(screen.getByRole('button', { name: en.sites.settings.reload }))
    await waitFor(() => expect(screen.queryByText(en.sites.settings.outOfDate)).toBeNull())
  })

  it('warns when HTTP is chosen', async () => {
    stubFetch(site())
    await renderView()

    expect(screen.queryByText(en.sites.settings.httpWarning)).toBeNull()
    await fireEvent.update(screen.getByLabelText(en.sites.settings.scheme), 'http')

    await waitFor(() => expect(screen.getByText(en.sites.settings.httpWarning)).toBeTruthy())
  })

  it('names languages in the shell language rather than showing bare tags', async () => {
    stubFetch(site())
    await renderView()

    const select = screen.getByLabelText(en.sites.settings.language) as HTMLSelectElement
    await waitFor(() => expect(select.options.length).toBe(4))
    const labels = Array.from(select.options).map((o) => o.textContent ?? '')
    expect(labels.some((l) => l.startsWith('German'))).toBe(true)
    expect(select.value).toBe('de')
  })

  it('shows every setting read-only when the server says the user may change nothing', async () => {
    stubFetch(site({ permissions: nothing }))
    await renderView()

    expect(screen.getByText(en.sites.detail.readOnly)).toBeTruthy()
    expect((screen.getByLabelText(en.sites.settings.name) as HTMLInputElement).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: en.sites.settings.save })).toBeNull()
    expect(screen.queryByLabelText(en.sites.hostNames.add)).toBeNull()
    expect(screen.queryByRole('button', { name: en.sites.availability.disable })).toBeNull()
  })

  it('meets the accessibility baseline and keeps every control in the tab order (SC-009)', async () => {
    stubFetch(site())
    const { container } = await renderView()

    const violations = await baselineViolations(container)
    expect(violations.map((v) => `${v.id}: ${v.description}`)).toEqual([])

    const removed = Array.from(container.querySelectorAll('button, input, a[href], select')).filter(
      (el) => el.getAttribute('tabindex') === '-1',
    )
    expect(removed).toEqual([])
  })
})

describe('SiteDetailView — host names (US2)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('shows internationalised names readably and marks the primary', async () => {
    stubFetch(site())
    await renderView()

    const list = screen.getByRole('list')
    expect(within(list).getByText('bücher.test')).toBeTruthy()
    expect(within(list).getByText(en.sites.hostNames.primary)).toBeTruthy()
  })

  it('explains why the primary host name cannot be removed, before anyone tries', async () => {
    stubFetch(site())
    await renderView()

    const remove = screen.getByRole('button', { name: `Remove client.test` }) as HTMLButtonElement
    expect(remove.disabled).toBe(true)
    expect(remove.getAttribute('aria-describedby')).toBeTruthy()
    expect(screen.getAllByText(en.sites.hostNames.cannotRemovePrimary).length).toBeGreaterThan(0)
  })

  it('adds a host name, normalised to composed Unicode before it is sent', async () => {
    const calls = stubFetch(site(), {
      [`POST /api/v1/sites/${SITE}/host-names`]: [{ status: 201, body: { hostName: 'xn--nu-kia.test', displayName: 'nü.test', isPrimary: false } }],
    })
    await renderView()

    // "u" followed by a combining diaeresis: what some keyboards produce.
    await fireEvent.update(screen.getByLabelText(en.sites.hostNames.add), ' nü.test ')
    await fireEvent.click(screen.getByRole('button', { name: en.sites.hostNames.addButton }))

    await waitFor(() => expect(screen.getByText('nü.test added.')).toBeTruthy())
    const post = calls.find((c) => c.method === 'POST')
    expect(post?.body).toEqual({ hostName: 'nü.test' })
  })

  it('shows the reason when a host name is taken, without naming who holds it', async () => {
    stubFetch(site(), {
      [`POST /api/v1/sites/${SITE}/host-names`]: [{ status: 409, body: { title: 'Conflict', status: 409, errorCode: 'sites.host_name_taken' } }],
    })
    await renderView()

    await fireEvent.update(screen.getByLabelText(en.sites.hostNames.add), 'taken.test')
    await fireEvent.click(screen.getByRole('button', { name: en.sites.hostNames.addButton }))

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain(en.sites.errors.host_name_taken))
  })

  it('removes a host name only after confirming, and returns focus', async () => {
    const calls = stubFetch(site(), {
      [`DELETE /api/v1/sites/${SITE}/host-names/xn--bcher-kva.test`]: [{ status: 204 }],
    })
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: 'Remove bücher.test' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText('Remove bücher.test?')).toBeTruthy()
    expect(document.activeElement?.textContent?.trim()).toBe(en.sites.hostNames.cancel)

    await fireEvent.click(within(dialog).getByRole('button', { name: en.sites.hostNames.confirm }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(calls.some((c) => c.method === 'DELETE')).toBe(true)
  })

  it('makes a host name primary by saving the settings with the current version', async () => {
    const calls = stubFetch(site())
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: 'Make bücher.test primary' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
      primaryHostName: 'xn--bcher-kva.test',
      version: 7,
    })
  })
})

describe('SiteDetailView — availability (US4)', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('says what visitors will see before disabling, then marks the site disabled', async () => {
    const calls = stubFetch(site(), {
      [`POST /api/v1/sites/${SITE}/disable`]: [{ status: 200, body: site({ status: 'disabled' }) }],
    })
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: en.sites.availability.disable }))
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText(en.sites.availability.confirmDisableBody)).toBeTruthy()

    await fireEvent.click(within(dialog).getByRole('button', { name: en.sites.availability.confirmDisable }))

    await waitFor(() => expect(screen.getByText(en.sites.detail.disabledNotice)).toBeTruthy())
    expect(calls.some((c) => c.url === `/api/v1/sites/${SITE}/disable`)).toBe(true)
    expect(screen.getByRole('button', { name: en.sites.availability.enable })).toBeTruthy()
  })

  it('cancelling with Escape changes nothing', async () => {
    const calls = stubFetch(site())
    await renderView()

    await fireEvent.click(screen.getByRole('button', { name: en.sites.availability.disable }))
    const dialog = await screen.findByRole('alertdialog')
    await fireEvent.keyDown(dialog, { key: 'Escape' })

    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('keeps a disabled site open and marked, with enabling offered', async () => {
    stubFetch(site({ status: 'disabled' }))
    await renderView()

    expect(screen.getByText(en.sites.detail.disabledNotice)).toBeTruthy()
    expect(screen.getByText(en.sites.availability.disabledBody)).toBeTruthy()
    expect(screen.getByRole('button', { name: en.sites.availability.enable })).toBeTruthy()
  })

  it('hides availability controls when the server does not allow them', async () => {
    stubFetch(site({ permissions: { ...everything, canManageAvailability: false } }))
    await renderView()

    expect(screen.queryByRole('button', { name: en.sites.availability.disable })).toBeNull()
  })
})
