import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import SettingsView from '../../src/features/sites/SettingsView.vue'
import { useAuthStore } from '../../src/app/authStore'
import en from '../../src/i18n/en.json'

// 019 FR-006: Administrators see the release this installation runs and the one its database
// records; nobody else sees a version number.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const INSTALLATION = 'GET /api/v1/identity/installation'

function stubFetch(installation: unknown): string[] {
  const calls: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${(init?.method ?? 'GET').toUpperCase()} ${url}`
    calls.push(key)
    const reply = key === INSTALLATION ? { status: 200, body: installation } : { status: 404, body: {} }
    return { ok: reply.status < 300, status: reply.status, statusText: 'x', json: async () => reply.body, text: async () => JSON.stringify(reply.body) }
  }))
  return calls
}

function signIn(isAdministrator: boolean) {
  setActivePinia(createPinia())
  useAuthStore().signedIn({ id: 'u1', displayName: 'Dana', email: 'dana@example.test', isAdministrator, siteRoles: [] })
}

const renderView = () => render(SettingsView, { global: { plugins: [i18n] } })

describe('about this installation', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the running release, its revision and the database version', async () => {
    signIn(true)
    stubFetch({ version: '0.2.0', revision: '5f549ae1c2d3', databaseVersion: '0.2.0', upgradingTo: null, releaseNotesUrl: 'https://github.com/trungnd1985/quick-builder/releases/tag/v0.2.0' })
    const { container } = renderView()

    expect(await screen.findByText(/QuickBuilder 0\.2\.0/)).toBeTruthy()
    expect(screen.getByText(/5f549ae1c2d3/)).toBeTruthy()
    const link = screen.getByRole('link', { name: en.settings.about.releaseNotes }) as HTMLAnchorElement
    expect(link.href).toContain('/releases/tag/v0.2.0')
    expect(screen.queryByRole('status')).toBeNull()
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('has no release notes link for a build that is not a release', async () => {
    signIn(true)
    stubFetch({ version: '0.3.0-dev', revision: null, databaseVersion: null, upgradingTo: null, releaseNotesUrl: null })
    renderView()

    expect(await screen.findByText(/QuickBuilder 0\.3\.0-dev/)).toBeTruthy()
    expect(screen.getByText(en.settings.about.databaseNone)).toBeTruthy()
    expect(screen.queryByRole('link', { name: en.settings.about.releaseNotes })).toBeNull()
  })

  it('says when an upgrade is in progress', async () => {
    signIn(true)
    stubFetch({ version: '0.3.0', revision: null, databaseVersion: '0.2.0', upgradingTo: '0.3.0', releaseNotesUrl: null })
    renderView()

    expect((await screen.findByRole('status')).textContent).toContain('0.3.0')
  })

  it('is not shown, and not asked for, when the member is not an Administrator', async () => {
    signIn(false)
    const calls = stubFetch({ version: '0.2.0', revision: null, databaseVersion: '0.2.0', upgradingTo: null, releaseNotesUrl: null })
    renderView()

    expect(await screen.findByText(en.settings.placeholder)).toBeTruthy()
    expect(screen.queryByText(en.settings.about.heading)).toBeNull()
    expect(calls).not.toContain(INSTALLATION)
  })
})
