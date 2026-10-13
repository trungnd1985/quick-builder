import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import SearchSettingsPanel from '../../src/features/sites/SearchSettingsPanel.vue'
import type { Site } from '../../src/api/sites'
import en from '../../src/i18n/en.json'

// 017 US1 (tasks T034): the site's title suffix, default sharing image and organisation.
// Designers and Administrators change them; everyone else sees them read-only.

const SITE = '22222222-2222-2222-2222-222222222222'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function site(canManageSearch = true): Site {
  return {
    id: SITE,
    name: 'Client',
    hostNames: ['client.test'],
    createdAtUtc: '2026-09-26T08:00:00Z',
    primaryHostName: 'client.test',
    hostNameDetails: [{ hostName: 'client.test', displayName: 'client.test', isPrimary: true }],
    scheme: 'https',
    baseUrl: 'https://client.test/',
    defaultLanguage: 'en',
    status: 'enabled',
    version: 7,
    updatedAtUtc: '2026-09-26T08:00:00Z',
    permissions: { canManageSettings: false, canManageHostNames: false, canManageAvailability: false, canManageSearch },
    search: { titleSuffix: ' | Client', defaultImageId: null, organisationName: 'Client Ltd', organisationLogoId: null, updatedBy: null },
    searchVisibility: { discourageSearchEngines: false, robotsRules: null, redirectOtherHostNames: true },
  }
}

function stubFetch(reply: { status: number; body: unknown }) {
  const calls: { url: string; method: string; body?: unknown }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, method: (init?.method ?? 'GET').toUpperCase(), body: init?.body ? JSON.parse(String(init.body)) : undefined })
    return { ok: reply.status < 300, status: reply.status, statusText: 'x', json: async () => reply.body, text: async () => JSON.stringify(reply.body) }
  }))
  return calls
}

afterEach(() => vi.unstubAllGlobals())

describe('Search settings panel', () => {
  it('shows the settings and saves a changed suffix and organisation with the site version', async () => {
    const saved = { ...site(), search: { ...site().search!, titleSuffix: ' | Acme', organisationName: 'Acme' }, version: 8 }
    const calls = stubFetch({ status: 200, body: saved })
    const { emitted } = render(SearchSettingsPanel, { props: { site: site() }, global: { plugins: [i18n] } })

    const suffix = screen.getByLabelText<HTMLInputElement>(en.sites.search.titleSuffix)
    expect(suffix.value).toBe(' | Client')

    await fireEvent.update(suffix, ' | Acme')
    await fireEvent.update(screen.getByLabelText(en.sites.search.organisationName), 'Acme')
    await fireEvent.click(screen.getByRole('button', { name: en.sites.search.save }))

    await waitFor(() => expect(emitted().updated).toBeTruthy())
    expect(calls.at(-1)).toEqual({
      url: `/api/v1/sites/${SITE}/search-settings`,
      method: 'PUT',
      body: { titleSuffix: ' | Acme', defaultImageId: null, organisationName: 'Acme', organisationLogoId: null, version: 7 },
    })
  })

  it('shows a refusal by its code', async () => {
    stubFetch({ status: 400, body: { status: 400, errorCode: 'sites.search_invalid', detail: 'x' } })
    render(SearchSettingsPanel, { props: { site: site() }, global: { plugins: [i18n] } })

    await fireEvent.click(screen.getByRole('button', { name: en.sites.search.save }))

    expect((await screen.findByRole('alert')).textContent).toContain(en.sites.errors.search_invalid)
  })

  it('is read-only for members who may not change it', () => {
    stubFetch({ status: 200, body: site(false) })
    render(SearchSettingsPanel, { props: { site: site(false) }, global: { plugins: [i18n] } })

    expect(screen.getByLabelText<HTMLInputElement>(en.sites.search.titleSuffix).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: en.sites.search.save })).toBeNull()
  })

  it('has no axe violations', async () => {
    stubFetch({ status: 200, body: site() })
    const { container } = render(SearchSettingsPanel, { props: { site: site() }, global: { plugins: [i18n] } })

    const results = await axe.run(container, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })
})
