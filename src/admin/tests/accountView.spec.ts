import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import AccountView from '../src/features/identity/AccountView.vue'
import en from '../src/i18n/en.json'

// 002 FR-012, FR-012a, FR-032: changing one's own password and ending other sessions.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function reply(status: number, body: unknown = {}) {
  return { ok: status >= 200 && status < 300, status, statusText: '', headers: new Headers(), json: async () => body }
}

describe('AccountView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.unstubAllGlobals()
  })

  it('meets the accessibility baseline and keeps every control reachable', async () => {
    const { container } = render(AccountView, { global: { plugins: [i18n] } })

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])
    expect(Array.from(container.querySelectorAll('button, input')).filter((el) => el.getAttribute('tabindex') === '-1')).toEqual([])
  })

  it('says when the current password is wrong', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(403, { title: 'Forbidden', status: 403, errorCode: 'identity.current_password_incorrect' })))
    render(AccountView, { global: { plugins: [i18n] } })

    await fireEvent.update(screen.getByLabelText(en.identity.account.current), 'not it')
    await fireEvent.update(screen.getByLabelText(en.identity.account.next), 'a strong new passphrase')
    await fireEvent.click(screen.getByRole('button', { name: en.identity.account.submit }))

    await waitFor(() => expect(screen.getByText(en.identity.errors.identity.current_password_incorrect)).toBeTruthy())
  })

  it('confirms that other sessions were signed out', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(204)))
    render(AccountView, { global: { plugins: [i18n] } })

    await fireEvent.click(screen.getByRole('button', { name: en.identity.account.signOutOthers }))

    await waitFor(() => expect(screen.getByText(en.identity.account.othersSignedOut)).toBeTruthy())
  })
})
