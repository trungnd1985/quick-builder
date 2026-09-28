import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import DesignTokensView from '../../src/features/design/DesignTokensView.vue'
import en from '../../src/i18n/en.json'
import { defaultTokens, type TokenSet } from '../../src/editor/tokens/defaultTokens'
import { i18n, SITE } from '../editor/harness'

// 011 US6, FR-031 to FR-035, clarification Q4: the site's design tokens — colours, fonts,
// text sizes and spacing — edited by Designers, read-only for Editors, and a used token
// deleted only after a warning that says how many blocks use it.

interface Call {
  method: string
  url: string
  body?: { document: TokenSet; version: number }
}

function stub(options: { canManage?: boolean; usage?: number; saveStatus?: number } = {}) {
  const calls: Call[] = []
  let version = 0
  let document: TokenSet = JSON.parse(JSON.stringify(defaultTokens))

  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ method, url, body })

    let status = 200
    let reply: unknown
    if (url.includes('/token-usages/')) {
      reply = { tokenId: url.split('/').at(-1), blockCount: options.usage ?? 0, documentCount: options.usage ? 2 : 0 }
    } else if (method === 'PUT') {
      status = options.saveStatus ?? 200
      if (status === 200) {
        document = body.document
        version += 1
        reply = { siteId: SITE, version, isDefault: false, document, updatedAtUtc: null, updatedBy: null, permissions: { canManage: true } }
      } else {
        reply = { title: 'Conflict', status, errorCode: 'editor.out_of_date' }
      }
    } else {
      reply = { siteId: SITE, version, isDefault: version === 0, document, updatedAtUtc: null, updatedBy: null, permissions: { canManage: options.canManage ?? true } }
    }

    return { ok: status < 300, status, statusText: 'x', headers: { get: () => null }, json: async () => reply, text: async () => JSON.stringify(reply) }
  }))
  return calls
}

async function renderView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/design', name: 'site-design', component: DesignTokensView },
    ],
  })
  await router.push(`/sites/${SITE}/design`)
  await router.isReady()
  const result = render(DesignTokensView, { global: { plugins: [i18n, router, createPinia()] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: en.design.title })).toBeTruthy())
  await waitFor(() => expect(screen.queryByText(en.design.loading)).toBeNull())
  return result
}

const colours = () => screen.getByRole('group', { name: en.design.colors.title })

describe('DesignTokensView (US6)', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('lists the colours, fonts, text sizes and spacing steps', async () => {
    stub()
    const { container } = await renderView()

    expect(within(colours()).getByDisplayValue('Brand')).toBeTruthy()
    expect(within(colours()).getByDisplayValue('#2563eb')).toBeTruthy()
    expect(screen.getByLabelText(en.design.typography.headingFont)).toBeTruthy()
    expect(within(screen.getByRole('group', { name: en.design.sizes.title })).getByDisplayValue('Normal')).toBeTruthy()
    expect(within(screen.getByRole('group', { name: en.design.spacing.title })).getByDisplayValue('Medium')).toBeTruthy()

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'button-name', 'aria-allowed-attr', 'select-name'] } })
    expect(results.violations).toEqual([])
  })

  it('adds, renames and recolours a colour, and saves the whole set against its version', async () => {
    const calls = stub()
    await renderView()

    await fireEvent.update(within(colours()).getByDisplayValue('Brand'), 'Primary')
    await fireEvent.update(within(colours()).getByDisplayValue('#2563eb'), '#16a34a')
    await fireEvent.click(screen.getByRole('button', { name: en.design.colors.add }))
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    const saved = calls.find((c) => c.method === 'PUT')!.body!
    expect(saved.version).toBe(0)
    expect(saved.document.colors[0]).toEqual({ id: 'color-brand', name: 'Primary', value: '#16a34a' })
    expect(saved.document.colors).toHaveLength(7)
    expect(saved.document.colors[6].id).toMatch(/^color-[a-z0-9]{8}$/)
    await waitFor(() => expect(screen.getByText(en.design.saved)).toBeTruthy())
  })

  it('asks how many blocks use a colour before deleting it, and says so', async () => {
    const calls = stub({ usage: 5 })
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await renderView()

    const row = within(colours()).getByDisplayValue('Brand light').closest('li')!
    await fireEvent.click(within(row).getByRole('button', { name: en.design.remove }))

    await waitFor(() => expect(confirm).toHaveBeenCalled())
    expect(calls.some((c) => c.url.endsWith('/token-usages/color-brand-light'))).toBe(true)
    expect(confirm.mock.calls[0][0]).toContain('5')
    expect(within(colours()).queryByDisplayValue('Brand light')).toBeNull()
  })

  it('keeps the colour when the warning is declined', async () => {
    stub({ usage: 2 })
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    await renderView()

    const row = within(colours()).getByDisplayValue('Brand light').closest('li')!
    await fireEvent.click(within(row).getByRole('button', { name: en.design.remove }))

    await waitFor(() => expect(within(colours()).getByDisplayValue('Brand light')).toBeTruthy())
  })

  it('does not offer to delete the last colour', async () => {
    stub()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    await renderView()

    let buttons = within(colours()).getAllByRole('button', { name: en.design.remove })
    while (buttons.length > 1) {
      await fireEvent.click(buttons[0])
      await waitFor(() => expect(within(colours()).getAllByRole('button', { name: en.design.remove }).length).toBeLessThan(buttons.length))
      buttons = within(colours()).getAllByRole('button', { name: en.design.remove })
    }
    expect((buttons[0] as HTMLButtonElement).disabled).toBe(true)
  })

  it('refuses a colour value that is not #rrggbb, before saving', async () => {
    const calls = stub()
    await renderView()

    await fireEvent.update(within(colours()).getByDisplayValue('#2563eb'), 'blue')
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))

    await waitFor(() => expect(screen.getByText(en.editor.errors.server.editor_invalid_tokens)).toBeTruthy())
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
  })

  it('says the tokens changed elsewhere when a save is out of date', async () => {
    stub({ saveStatus: 409 })
    await renderView()

    await fireEvent.update(within(colours()).getByDisplayValue('Brand'), 'Primary')
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))

    await waitFor(() => expect(screen.getByText(en.editor.errors.server.editor_out_of_date)).toBeTruthy())
    expect(screen.getByRole('button', { name: en.design.reload })).toBeTruthy()
  })

  it('shows the tokens read-only to a member who may not change them', async () => {
    stub({ canManage: false })
    await renderView()

    expect((within(colours()).getByDisplayValue('Brand') as HTMLInputElement).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: en.design.save })).toBeNull()
    expect(screen.queryByRole('button', { name: en.design.colors.add })).toBeNull()
    expect(screen.getByText(en.design.readOnly)).toBeTruthy()
  })
})
