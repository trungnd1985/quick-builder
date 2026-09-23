import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import SetupView from '../src/features/identity/SetupView.vue'
import { useAuthStore } from '../src/app/authStore'
import en from '../src/i18n/en.json'

// 002 US1 and FR-032: the setup wizard meets the accessibility baseline, shows the server's
// field errors next to their fields, and leaves for the app when setup is done — by us or,
// in a race, by someone else.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function jsonResponse(status: number, body: unknown) {
  return { ok: status >= 200 && status < 300, status, statusText: '', headers: new Headers(), json: async () => body }
}

async function renderSetup() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/setup', name: 'setup', component: SetupView },
      { path: '/sites', name: 'sites', component: { template: '<p />' } },
    ],
  })
  await router.push('/setup')
  await router.isReady()

  const view = render(SetupView, { global: { plugins: [i18n, router] } })
  return { ...view, router }
}

async function fillAndSubmit() {
  await fireEvent.update(screen.getByLabelText(en.identity.setup.code), 'abcd-efgh-jkmn')
  await fireEvent.update(screen.getByLabelText(en.identity.setup.displayName), 'Site Owner')
  await fireEvent.update(screen.getByLabelText(en.identity.setup.email), 'owner@example.test')
  await fireEvent.update(screen.getByLabelText(en.identity.setup.password), 'a long enough passphrase')
  await fireEvent.click(screen.getByRole('button', { name: en.identity.setup.submit }))
}

describe('SetupView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.unstubAllGlobals()
  })

  it('meets the accessibility baseline (FR-032)', async () => {
    const { container } = await renderSetup()

    const results = await axe.run(container, {
      runOnly: {
        type: 'rule',
        values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order'],
      },
    })

    expect(results.violations.map((v) => `${v.id}: ${v.description}`)).toEqual([])
  })

  it('labels every control and keeps them all in the tab order', async () => {
    const { container } = await renderSetup()

    for (const label of [
      en.identity.setup.code,
      en.identity.setup.displayName,
      en.identity.setup.email,
      en.identity.setup.password,
    ]) {
      expect(screen.getByLabelText(label)).toBeTruthy()
    }

    const removedFromTabOrder = Array.from(container.querySelectorAll('button, input, a[href]'))
      .filter((el) => el.getAttribute('tabindex') === '-1')
    expect(removedFromTabOrder).toEqual([])
  })

  it('shows each field error next to its field', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(400, {
          title: 'The request failed validation',
          status: 400,
          errorCode: 'request.invalid',
          errors: { Password: ['That password is too common. Choose one that is harder to guess.'] },
        }),
      ),
    )
    await renderSetup()

    await fillAndSubmit()

    await waitFor(() => {
      expect(screen.getByText('That password is too common. Choose one that is harder to guess.')).toBeTruthy()
    })
    expect(screen.getByLabelText(en.identity.setup.password).getAttribute('aria-invalid')).toBe('true')
  })

  it('explains a wrong setup code in plain words', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(403, { title: 'Forbidden', status: 403, errorCode: 'identity.setup_code_invalid' }),
      ),
    )
    await renderSetup()

    await fillAndSubmit()

    await waitFor(() => {
      expect(screen.getByText(en.identity.errors.identity.setup_code_invalid)).toBeTruthy()
    })
  })

  it('signs the new administrator in and opens the app', async () => {
    const me = { id: '1', displayName: 'Site Owner', email: 'owner@example.test', isAdministrator: true, siteRoles: [] }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(201, me)))
    const { router } = await renderSetup()

    await fillAndSubmit()

    await waitFor(() => expect(router.currentRoute.value.name).toBe('sites'))
    expect(useAuthStore().user?.isAdministrator).toBe(true)
    expect(useAuthStore().setupRequired).toBe(false)
  })

  it('leaves the wizard when someone else finished setup first', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(409, { title: 'Conflict', status: 409, errorCode: 'identity.setup_already_completed' }),
      ),
    )
    const { router } = await renderSetup()

    await fillAndSubmit()

    await waitFor(() => expect(router.currentRoute.value.name).toBe('sites'))
    expect(useAuthStore().setupRequired).toBe(false)
  })
})
