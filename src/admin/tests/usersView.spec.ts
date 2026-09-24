import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import UsersView from '../src/features/users/UsersView.vue'
import en from '../src/i18n/en.json'

// 002 US3 and FR-032: the users screen lists accounts, creates one, and shows its
// set-password link once, built from this admin's own address (FR-011b).

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const existing = {
  id: 'u1',
  displayName: 'Ada Owner',
  email: 'ada@example.test',
  status: 'active',
  hasPassword: true,
  grants: { administrator: true, siteRoles: [] },
  createdAtUtc: '2026-09-24T10:00:00Z',
  lastSignInAtUtc: null,
}

function respond(routes: Record<string, unknown>) {
  return vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${url}`
    const body = routes[key]
    if (body === undefined) {
      throw new Error(`Unexpected request: ${key}`)
    }
    return { ok: true, status: 200, statusText: '', headers: new Headers(), json: async () => body }
  })
}

async function renderUsers() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/users', name: 'users', component: UsersView },
      { path: '/users/:id', name: 'user', component: { template: '<p />' } },
    ],
  })
  await router.push('/users')
  await router.isReady()
  return render(UsersView, { global: { plugins: [i18n, router] } })
}

describe('UsersView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.unstubAllGlobals()
  })

  it('lists accounts and meets the accessibility baseline with the form open', async () => {
    vi.stubGlobal('fetch', respond({ 'GET /api/v1/identity/users': [existing], 'GET /api/v1/sites': [] }))
    const { container } = await renderUsers()

    await waitFor(() => expect(screen.getByText('Ada Owner')).toBeTruthy())
    await fireEvent.click(screen.getByRole('button', { name: en.identity.users.create.title }))

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])

    const removed = Array.from(container.querySelectorAll('button, input, select, a[href]'))
      .filter((el) => el.getAttribute('tabindex') === '-1')
    expect(removed).toEqual([])
  })

  it('shows the new user\'s link once, on this admin\'s own address', async () => {
    const created = { ...existing, id: 'u2', displayName: 'Bea New', email: 'bea@example.test', hasPassword: false, grants: { administrator: false, siteRoles: [] } }
    vi.stubGlobal(
      'fetch',
      respond({
        'GET /api/v1/identity/users': [existing],
        'GET /api/v1/sites': [],
        'POST /api/v1/identity/users': { user: created, setPasswordLink: { token: 'tok-123', expiresAtUtc: '2026-09-25T10:00:00Z' } },
      }),
    )
    await renderUsers()
    await waitFor(() => expect(screen.getByText('Ada Owner')).toBeTruthy())

    await fireEvent.click(screen.getByRole('button', { name: en.identity.users.create.title }))
    await fireEvent.update(screen.getByLabelText(en.identity.users.fields.displayName), 'Bea New')
    await fireEvent.update(screen.getByLabelText(en.identity.users.fields.email), 'bea@example.test')
    await fireEvent.click(screen.getByRole('button', { name: en.identity.users.create.submit }))

    const link = await screen.findByLabelText(en.identity.users.link.label)
    expect((link as HTMLInputElement).value).toBe(`${window.location.origin}${import.meta.env.BASE_URL}set-password#token=tok-123`)
    expect(screen.getByText('Bea New')).toBeTruthy()
  })
})
