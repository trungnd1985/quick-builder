import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { reactive } from 'vue'
import axe from 'axe-core'
import MenuStyleSection from '../../src/features/design/MenuStyleSection.vue'
import DesignTokensView from '../../src/features/design/DesignTokensView.vue'
import en from '../../src/i18n/en.json'
import { defaultTokens, type TokenSet } from '../../src/editor/tokens/defaultTokens'
import { i18n, SITE } from '../editor/harness'

// 024 FR-017a to FR-017d (tasks T093): the site-wide menu style in the Design view — a look and
// eight colours, each "site default" until chosen; failing contrast pairs named, never
// refused; read-only for members who may not change the design.

const copy = (extra: Partial<TokenSet> = {}): TokenSet => ({ ...JSON.parse(JSON.stringify(defaultTokens)), ...extra })

function mountSection(tokens: TokenSet, readOnly = false) {
  const state = reactive({ tokens })
  // The Design view applies the change to its draft, as here.
  const onChange = (style: TokenSet['menuStyle']) => {
    if (style) state.tokens.menuStyle = style
    else delete state.tokens.menuStyle
  }
  const result = render(MenuStyleSection, { props: { tokens: state.tokens, readOnly, onChange }, global: { plugins: [i18n] } })
  return { ...result, state }
}

const section = () => screen.getByRole('group', { name: en.design.menus.title })

describe('the Menus section of the Design view', () => {
  it('starts with the plain link look and every colour following the built-in style', () => {
    mountSection(copy())

    expect((within(section()).getByLabelText(en.design.menus.looks.link) as HTMLInputElement).checked).toBe(true)
    for (const key of ['background', 'text', 'hoverText', 'hoverBackground', 'currentText', 'currentBackground', 'focus', 'panelBackground'] as const) {
      const select = within(section()).getByLabelText(en.design.menus.colors[key]) as HTMLSelectElement
      expect(select.value).toBe('')
      expect(select.options[0].textContent?.trim()).toBe(en.design.menus.siteDefault)
    }
  })

  it('writes only what is chosen into the token set, and "default" removes it again', async () => {
    const { state } = mountSection(copy())

    await fireEvent.click(within(section()).getByLabelText(en.design.menus.looks.pill))
    await fireEvent.update(within(section()).getByLabelText(en.design.menus.colors.background), 'color-text')
    expect(state.tokens.menuStyle).toEqual({ look: 'pill', colors: { background: 'color-text' } })

    await fireEvent.update(within(section()).getByLabelText(en.design.menus.colors.background), '')
    expect(state.tokens.menuStyle).toEqual({ look: 'pill' })

    await fireEvent.click(within(section()).getByLabelText(en.design.menus.looks.link))
    expect(state.tokens.menuStyle).toBeUndefined()
  })

  it('names each colour pair below the contrast minimum, and only warns', async () => {
    mountSection(copy())
    expect(within(section()).queryByText(en.design.menus.contrast.heading)).toBeNull()

    // Light text on the light page background.
    await fireEvent.update(within(section()).getByLabelText(en.design.menus.colors.text), 'color-border')

    const warning = await within(section()).findByRole('status')
    expect(warning.textContent).toContain(en.design.menus.contrast.heading)
    expect(warning.textContent).toContain(en.design.menus.contrast.pairs.text)
    expect(warning.textContent).toContain(en.design.menus.contrast.pairs.panel)
    expect(warning.textContent).not.toContain(en.design.menus.contrast.pairs.hover)
  })

  it('shows the values read-only to members who may not change the design', () => {
    mountSection(copy({ menuStyle: { look: 'button', colors: { text: 'color-brand' } } }), true)

    expect((within(section()).getByLabelText(en.design.menus.looks.button) as HTMLInputElement).disabled).toBe(true)
    expect((within(section()).getByLabelText(en.design.menus.colors.text) as HTMLSelectElement).disabled).toBe(true)
    expect((within(section()).getByLabelText(en.design.menus.colors.text) as HTMLSelectElement).value).toBe('color-brand')
  })

  it('has no accessibility violations', async () => {
    const { container } = mountSection(copy({ menuStyle: { colors: { text: 'color-border' } } }))
    const results = await axe.run(container, { rules: { region: { enabled: false } } })
    expect(results.violations).toEqual([])
  })
})

describe('the Design view saves the menu style with the token set', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('sends menuStyle in the saved document', async () => {
    setActivePinia(createPinia())
    const calls: { method: string; body?: { document: TokenSet } }[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase()
      const body = init?.body ? JSON.parse(String(init.body)) : undefined
      calls.push({ method, body })
      const document = method === 'PUT' ? body.document : copy()
      const reply = { siteId: SITE, version: method === 'PUT' ? 1 : 0, isDefault: method !== 'PUT', document, updatedAtUtc: null, updatedBy: null, permissions: { canManage: true } }
      return { ok: true, status: 200, statusText: 'x', headers: { get: () => null }, json: async () => reply, text: async () => JSON.stringify(reply) }
    }))
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
        { path: '/sites/:siteId/design', name: 'site-design', component: DesignTokensView },
      ],
    })
    await router.push(`/sites/${SITE}/design`)
    await router.isReady()
    render(DesignTokensView, { global: { plugins: [i18n, router, createPinia()] } })
    await waitFor(() => expect(screen.queryByText(en.design.loading)).toBeNull())

    await fireEvent.click(within(section()).getByLabelText(en.design.menus.looks.underline))
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')!.body!.document.menuStyle).toEqual({ look: 'underline' })
  })
})
