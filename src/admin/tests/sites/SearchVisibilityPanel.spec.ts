import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import SearchVisibilityPanel from '../../src/features/sites/SearchVisibilityPanel.vue'
import type { Site } from '../../src/api/sites'
import en from '../../src/i18n/en.json'

// 017 US3, US6 (tasks T055, T063): discouraging search engines, the Administrator's own
// robots.txt rules and the host name redirect. Administrators only; turning the redirect on
// asks first.

const SITE = '22222222-2222-2222-2222-222222222222'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function site(canManageSearchVisibility = true, redirectOtherHostNames = false): Site {
  return {
    id: SITE,
    name: 'Client',
    hostNames: ['client.test', 'www.client.test'],
    createdAtUtc: '2026-09-26T08:00:00Z',
    primaryHostName: 'client.test',
    hostNameDetails: [
      { hostName: 'client.test', displayName: 'client.test', isPrimary: true },
      { hostName: 'www.client.test', displayName: 'www.client.test', isPrimary: false },
    ],
    scheme: 'https',
    baseUrl: 'https://client.test/',
    defaultLanguage: 'en',
    status: 'enabled',
    version: 7,
    updatedAtUtc: '2026-09-26T08:00:00Z',
    permissions: { canManageSettings: true, canManageHostNames: true, canManageAvailability: true, canManageSearch: true, canManageSearchVisibility },
    search: { titleSuffix: null, defaultImageId: null, organisationName: null, organisationLogoId: null, updatedBy: null },
    searchVisibility: { discourageSearchEngines: false, robotsRules: null, redirectOtherHostNames },
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

describe('Search visibility panel', () => {
  it('saves discouraging search engines and custom rules with the site version', async () => {
    const calls = stubFetch({ status: 200, body: { ...site(), version: 8 } })
    const { emitted } = render(SearchVisibilityPanel, { props: { site: site() }, global: { plugins: [i18n] } })

    await fireEvent.click(screen.getByLabelText(en.sites.searchVisibility.discourage))
    await fireEvent.update(screen.getByLabelText(en.sites.searchVisibility.robotsRules), 'User-agent: BadBot\nDisallow: /')
    await fireEvent.click(screen.getByRole('button', { name: en.sites.searchVisibility.save }))

    await waitFor(() => expect(emitted().updated).toBeTruthy())
    expect(calls.at(-1)).toEqual({
      url: `/api/v1/sites/${SITE}/search-visibility`,
      method: 'PUT',
      body: { discourageSearchEngines: true, robotsRules: 'User-agent: BadBot\nDisallow: /', redirectOtherHostNames: false, version: 7 },
    })
  })

  it('asks before redirecting other host names, and saves only on confirmation', async () => {
    const calls = stubFetch({ status: 200, body: { ...site(true, true), version: 8 } })
    render(SearchVisibilityPanel, { props: { site: site() }, global: { plugins: [i18n] } })

    await fireEvent.click(screen.getByLabelText(en.sites.searchVisibility.redirectOtherHosts))
    await fireEvent.click(screen.getByRole('button', { name: en.sites.searchVisibility.save }))

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('www.client.test')
    expect(calls).toHaveLength(0)

    await fireEvent.click(screen.getByRole('button', { name: en.sites.searchVisibility.confirmAction }))
    await waitFor(() => expect(calls).toHaveLength(1))
    expect((calls[0]!.body as { redirectOtherHostNames: boolean }).redirectOtherHostNames).toBe(true)
  })

  it('cancelling the question saves nothing', async () => {
    const calls = stubFetch({ status: 200, body: site() })
    render(SearchVisibilityPanel, { props: { site: site() }, global: { plugins: [i18n] } })

    await fireEvent.click(screen.getByLabelText(en.sites.searchVisibility.redirectOtherHosts))
    await fireEvent.click(screen.getByRole('button', { name: en.sites.searchVisibility.save }))
    await fireEvent.click(await screen.findByRole('button', { name: en.sites.searchVisibility.cancel }))

    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(calls).toHaveLength(0)
  })

  it('is read-only for members who are not Administrators', () => {
    stubFetch({ status: 200, body: site(false) })
    render(SearchVisibilityPanel, { props: { site: site(false) }, global: { plugins: [i18n] } })

    expect(screen.getByLabelText<HTMLInputElement>(en.sites.searchVisibility.discourage).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: en.sites.searchVisibility.save })).toBeNull()
  })

  it('has no axe violations', async () => {
    stubFetch({ status: 200, body: site() })
    const { container } = render(SearchVisibilityPanel, { props: { site: site() }, global: { plugins: [i18n] } })

    const results = await axe.run(container, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })
})
