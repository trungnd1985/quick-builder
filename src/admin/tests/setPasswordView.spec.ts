import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import SetPasswordView from '../src/features/identity/SetPasswordView.vue'
import en from '../src/i18n/en.json'

// 002 FR-011a, research R-10: the token comes from the URL fragment, is checked before a
// password is asked for, and is then removed from the address.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function reply(status: number, body: unknown) {
  return { ok: status >= 200 && status < 300, status, statusText: '', headers: new Headers(), json: async () => body }
}

async function renderAt(url: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/set-password', name: 'set-password', component: SetPasswordView },
      { path: '/sign-in', name: 'sign-in', component: { template: '<p />' } },
    ],
  })
  await router.push(url)
  await router.isReady()
  const view = render(SetPasswordView, { global: { plugins: [i18n, router] } })
  return { ...view, router }
}

describe('SetPasswordView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.unstubAllGlobals()
  })

  it('checks the link, clears the fragment, and meets the baseline', async () => {
    const fetch = vi.fn().mockResolvedValue(reply(200, { expiresAtUtc: '2026-09-25T10:00:00Z' }))
    vi.stubGlobal('fetch', fetch)
    const { container, router } = await renderAt('/set-password#token=abc123')

    await screen.findByLabelText(en.identity.setPassword.password)
    expect(router.currentRoute.value.hash).toBe('')
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ token: 'abc123' })

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('says plainly when a link cannot be used', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(410, { title: 'No longer valid', status: 410, errorCode: 'identity.link_invalid' })))
    await renderAt('/set-password#token=spent')

    await waitFor(() => expect(screen.getByText(en.identity.setPassword.invalid)).toBeTruthy())
    expect(screen.queryByLabelText(en.identity.setPassword.password)).toBeNull()
  })

  it('treats a missing token as an unusable link without calling the server', async () => {
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    await renderAt('/set-password')

    await waitFor(() => expect(screen.getByText(en.identity.setPassword.invalid)).toBeTruthy())
    expect(fetch).not.toHaveBeenCalled()
  })

  it('sets the password and offers sign-in', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn()
        .mockResolvedValueOnce(reply(200, { expiresAtUtc: '2026-09-25T10:00:00Z' }))
        .mockResolvedValueOnce({ ok: true, status: 204, statusText: '', headers: new Headers(), json: async () => ({}) }),
    )
    await renderAt('/set-password#token=abc123')

    await fireEvent.update(await screen.findByLabelText(en.identity.setPassword.password), 'a strong new passphrase')
    await fireEvent.click(screen.getByRole('button', { name: en.identity.setPassword.submit }))

    await waitFor(() => expect(screen.getByText(en.identity.setPassword.done)).toBeTruthy())
  })
})
