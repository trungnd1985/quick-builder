import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import SearchHiddenBanner from '../../src/components/SearchHiddenBanner.vue'
import { useSitesStore } from '../../src/app/sitesStore'
import type { Site } from '../../src/api/sites'
import en from '../../src/i18n/en.json'

// 017 US3 (task T055, FR-027): every screen of a site hidden from search engines says so.

const SITE = '22222222-2222-2222-2222-222222222222'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function site(discourageSearchEngines: boolean): Site {
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
    searchVisibility: { discourageSearchEngines, robotsRules: null, redirectOtherHostNames: true },
  }
}

async function mount(hidden: boolean, path = `/sites/${SITE}`) {
  vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, status: 200, statusText: 'OK', json: async () => [site(hidden)], text: async () => '[]' })))
  const pinia = createPinia()
  setActivePinia(pinia)
  useSitesStore().sites = [site(hidden)]
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', component: { template: '<div />' } },
      { path: '/', component: { template: '<div />' } },
    ],
  })
  await router.push(path)
  await router.isReady()
  return render(SearchHiddenBanner, { global: { plugins: [i18n, pinia, router] } })
}

afterEach(() => vi.unstubAllGlobals())

describe('Search hidden banner', () => {
  it('names a site hidden from search engines', async () => {
    await mount(true)

    expect(screen.getByRole('status').textContent).toContain('Client is hidden from search engines')
  })

  it('says nothing for a visible site', async () => {
    await mount(false)

    expect(screen.queryByRole('status')).toBeNull()
  })

  it('says nothing outside a site', async () => {
    await mount(true, '/')

    expect(screen.queryByRole('status')).toBeNull()
  })

  it('has no axe violations', async () => {
    const { container } = await mount(true)

    const results = await axe.run(container, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })
})
