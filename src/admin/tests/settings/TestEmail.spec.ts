import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import SettingsView from '../../src/features/sites/SettingsView.vue'
import { useAuthStore } from '../../src/app/authStore'
import en from '../../src/i18n/en.json'

// 018 FR-037: Administrators see whether outgoing email is set up and can send themselves a
// test email, with the mail server's answer when it refuses.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

type Reply = { status: number; body?: unknown }

function stubFetch(replies: Record<string, Reply>): string[] {
  const calls: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${(init?.method ?? 'GET').toUpperCase()} ${url}`
    calls.push(key)
    const reply = replies[key] ?? { status: 404, body: {} }
    return { ok: reply.status < 300, status: reply.status, statusText: 'x', json: async () => reply.body, text: async () => JSON.stringify(reply.body ?? {}) }
  }))
  return calls
}

function signIn(isAdministrator: boolean) {
  setActivePinia(createPinia())
  useAuthStore().signedIn({ id: 'u1', displayName: 'Dana', email: 'dana@example.test', isAdministrator, siteRoles: [] })
}

const STATUS = 'GET /api/v1/forms/email-status'
const TEST = 'POST /api/v1/forms/email-test'
const renderView = () => render(SettingsView, { global: { plugins: [i18n] } })

describe('outgoing email in settings', () => {
  beforeEach(() => signIn(true))
  afterEach(() => vi.unstubAllGlobals())

  it('sends a test email and says where it went', async () => {
    const calls = stubFetch({ [STATUS]: { status: 200, body: { configured: true } }, [TEST]: { status: 200, body: { serverReply: '250 OK' } } })
    renderView()

    expect(await screen.findByText(en.settings.email.on)).toBeTruthy()
    await fireEvent.click(screen.getByRole('button', { name: en.settings.email.send }))

    expect((await screen.findByRole('status')).textContent).toContain('dana@example.test')
    expect(calls).toContain(TEST)
  })

  it('shows the mail server reply when it refuses', async () => {
    stubFetch({
      [STATUS]: { status: 200, body: { configured: true } },
      [TEST]: { status: 502, body: { title: 'x', status: 502, errorCode: 'forms.email_failed', serverReply: '550 Relay denied' } },
    })
    renderView()

    await fireEvent.click(await screen.findByRole('button', { name: en.settings.email.send }))

    expect((await screen.findByRole('alert')).textContent).toContain('550 Relay denied')
  })

  it('explains that email is off and offers no test', async () => {
    stubFetch({ [STATUS]: { status: 200, body: { configured: false } } })
    renderView()

    expect(await screen.findByText(en.settings.email.off)).toBeTruthy()
    expect(screen.queryByRole('button', { name: en.settings.email.send })).toBeNull()
  })

  it('is not shown to anyone but an Administrator', async () => {
    signIn(false)
    const calls = stubFetch({})
    renderView()

    expect(screen.getByText(en.settings.placeholder)).toBeTruthy()
    expect(screen.queryByRole('heading', { name: en.settings.email.heading })).toBeNull()
    expect(calls).toEqual([])
  })

  it('has no accessibility violations', async () => {
    stubFetch({ [STATUS]: { status: 200, body: { configured: true } } })
    const { container } = renderView()
    await screen.findByRole('button', { name: en.settings.email.send })

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['button-name', 'heading-order', 'aria-valid-attr-value'] } })
    expect(results.violations).toEqual([])
  })
})
