import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import SignInView from '../src/features/identity/SignInView.vue'
import { safeReturnTo } from '../src/app/returnTo'
import { useAuthStore } from '../src/app/authStore'
import en from '../src/i18n/en.json'

// 002 US2, FR-019, FR-032: the sign-in screen meets the baseline, says one thing for every
// failure, explains throttling, and returns the user only to where they meant to go.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
const Blank = { template: '<p />' }

function jsonResponse(status: number, body: unknown, headers: Record<string, string> = {}) {
  return { ok: status >= 200 && status < 300, status, statusText: '', headers: new Headers(headers), json: async () => body }
}

function testRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sign-in', name: 'sign-in', component: SignInView },
      { path: '/setup', name: 'setup', component: Blank },
      { path: '/sites', name: 'sites', component: Blank },
      { path: '/settings', name: 'settings', component: Blank },
    ],
  })
}

async function renderSignIn(url = '/sign-in') {
  const router = testRouter()
  await router.push(url)
  await router.isReady()
  const view = render(SignInView, { global: { plugins: [i18n, router] } })
  return { ...view, router }
}

async function submit(email = 'owner@example.test', password = 'a long enough passphrase') {
  await fireEvent.update(screen.getByLabelText(en.identity.signIn.email), email)
  await fireEvent.update(screen.getByLabelText(en.identity.signIn.password), password)
  await fireEvent.click(screen.getByRole('button', { name: en.identity.signIn.submit }))
}

describe('SignInView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.unstubAllGlobals()
  })

  it('meets the accessibility baseline and keeps every control reachable (FR-032)', async () => {
    const { container } = await renderSignIn()

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])

    const removed = Array.from(container.querySelectorAll('button, input, a[href]'))
      .filter((el) => el.getAttribute('tabindex') === '-1')
    expect(removed).toEqual([])
  })

  it('says the same thing for every failed sign-in (FR-014)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(401, { title: 'Sign-in required', status: 401, errorCode: 'identity.invalid_credentials' })),
    )
    await renderSignIn()

    await submit()

    await waitFor(() => expect(screen.getByText(en.identity.errors.identity.invalid_credentials)).toBeTruthy())
  })

  it('tells a throttled user how long to wait', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(429, { title: 'Too many attempts', status: 429, errorCode: 'identity.throttled' }, { 'Retry-After': '8' }),
      ),
    )
    await renderSignIn()

    await submit()

    await waitFor(() => expect(screen.getByText('Too many attempts. Wait 8 seconds, then try again.')).toBeTruthy())
  })

  it('returns to the admin page the user was heading for', async () => {
    const me = { id: '1', displayName: 'Owner', email: 'owner@example.test', isAdministrator: true, siteRoles: [] }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(200, me)))
    const { router } = await renderSignIn('/sign-in?returnTo=%2Fsettings')

    await submit()

    await waitFor(() => expect(router.currentRoute.value.name).toBe('settings'))
    expect(useAuthStore().isSignedIn).toBe(true)
  })

  it('never returns anywhere outside the admin app (FR-019)', () => {
    const router = testRouter()

    for (const hostile of ['//evil.example/sites', 'https://evil.example', '/\\evil.example', 'javascript:alert(1)', '/no-such-page', '/sign-in', undefined, 42]) {
      expect(safeReturnTo(router, hostile)).toEqual({ name: 'sites' })
    }

    expect(safeReturnTo(router, '/settings')).toBe('/settings')
  })
})
