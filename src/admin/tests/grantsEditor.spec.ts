import { describe, expect, it, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { defineComponent, h, ref } from 'vue'
import axe from 'axe-core'
import GrantsEditor from '../src/features/users/GrantsEditor.vue'
import type { Grants } from '../src/api/identity'
import en from '../src/i18n/en.json'

// 002 FR-020, FR-032: choosing roles — Administrator for everything, or one role per site.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const sites = [
  { id: 's1', name: 'Alpha', hostNames: ['alpha.test'], createdAtUtc: '2026-09-24T10:00:00Z' },
  { id: 's2', name: 'Beta', hostNames: ['beta.test'], createdAtUtc: '2026-09-24T10:00:00Z' },
]

/** Renders the editor bound to a value the test can read back. */
function renderEditor(initial: Grants) {
  const value = ref<Grants>(initial)
  const Host = defineComponent({
    setup: () => () => h(GrantsEditor, { modelValue: value.value, 'onUpdate:modelValue': (v: Grants) => (value.value = v) }),
  })
  const view = render(Host, { global: { plugins: [i18n] } })
  return { ...view, value }
}

describe('GrantsEditor', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, headers: new Headers(), json: async () => sites }))
  })

  it('meets the accessibility baseline and labels every site', async () => {
    const { container } = renderEditor({ administrator: false, siteRoles: [] })
    await waitFor(() => expect(screen.getByLabelText('Alpha')).toBeTruthy())

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])
    expect(Array.from(container.querySelectorAll('input, select')).filter((el) => el.getAttribute('tabindex') === '-1')).toEqual([])
  })

  it('records one role per site', async () => {
    const { value } = renderEditor({ administrator: false, siteRoles: [] })
    await waitFor(() => expect(screen.getByLabelText('Alpha')).toBeTruthy())

    await fireEvent.update(screen.getByLabelText('Alpha'), 'editor')
    await fireEvent.update(screen.getByLabelText('Alpha'), 'designer')
    await fireEvent.update(screen.getByLabelText('Beta'), 'editor')

    expect(value.value.siteRoles).toEqual([
      { siteId: 's1', role: 'designer' },
      { siteId: 's2', role: 'editor' },
    ])

    await fireEvent.update(screen.getByLabelText('Alpha'), '')
    expect(value.value.siteRoles).toEqual([{ siteId: 's2', role: 'editor' }])
  })

  it('sets site roles aside while Administrator is ticked', async () => {
    const { value } = renderEditor({ administrator: false, siteRoles: [] })
    await waitFor(() => expect(screen.getByLabelText('Alpha')).toBeTruthy())

    await fireEvent.click(screen.getByLabelText(en.identity.grants.administrator))

    expect(value.value.administrator).toBe(true)
    await waitFor(() => expect((screen.getByLabelText('Alpha') as HTMLSelectElement).disabled).toBe(true))
  })
})
