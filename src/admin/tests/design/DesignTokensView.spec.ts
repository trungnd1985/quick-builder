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

function stub(options: { canManage?: boolean; usage?: number; saveStatus?: number; document?: TokenSet } = {}) {
  const calls: Call[] = []
  let version = 0
  let document: TokenSet = options.document ?? JSON.parse(JSON.stringify(defaultTokens))

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

  it('edits the three content widths and saves them with the set', async () => {
    const calls = stub()
    await renderView()

    const widths = screen.getByRole('group', { name: en.design.widths.title })
    expect(within(widths).getAllByRole('spinbutton')).toHaveLength(3)
    expect(within(widths).queryByRole('button')).toBeNull()

    await fireEvent.update(screen.getByLabelText(en.design.widths.normal), '1100')
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')!.body!.document.contentWidths).toEqual({ narrow: 720, normal: 1100, wide: 1440 })
  })

  it('refuses content widths out of order, before saving', async () => {
    const calls = stub()
    await renderView()

    await fireEvent.update(screen.getByLabelText(en.design.widths.narrow), '1300')
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))

    await waitFor(() => expect(screen.getByText(en.editor.errors.server.editor_content_widths_order)).toBeTruthy())
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
  })

  it('shows the default widths for a set saved before content widths existed', async () => {
    // 011 spec, Edge Cases.
    const legacy = JSON.parse(JSON.stringify(defaultTokens)) as TokenSet
    delete legacy.contentWidths
    stub({ document: legacy })
    await renderView()

    expect((screen.getByLabelText(en.design.widths.narrow) as HTMLInputElement).value).toBe('720')
    expect((screen.getByLabelText(en.design.widths.normal) as HTMLInputElement).value).toBe('1200')
    expect((screen.getByLabelText(en.design.widths.wide) as HTMLInputElement).value).toBe('1440')
  })

  // 066 US3, FR-013 to FR-016: the corner radii, a list beside spacing, edited the same way.
  const radii = () => screen.getByRole('group', { name: en.design.radii.title })
  const radiusNames = () => within(radii()).getAllByRole('textbox').map((input) => (input as HTMLInputElement).value)

  it('shows the default corner radii, after spacing, for a set saved before them', async () => {
    const legacy = JSON.parse(JSON.stringify(defaultTokens)) as TokenSet
    delete legacy.radii
    stub({ document: legacy })
    await renderView()

    expect(radiusNames()).toEqual(['None', 'Small', 'Medium', 'Large', 'Full'])
    const legends = [...document.querySelectorAll('legend')].map((l) => l.textContent?.trim())
    expect(legends.indexOf(en.design.radii.title)).toBe(legends.indexOf(en.design.spacing.title) + 1)
  })

  it('adds, renames and resizes a corner radius, and saves the list with the set', async () => {
    const calls = stub()
    await renderView()

    await fireEvent.click(within(radii()).getByRole('button', { name: en.design.radii.add }))
    const inputs = within(radii()).getAllByRole('textbox')
    await fireEvent.update(inputs.at(-1)!, 'Card')
    const sizes = within(radii()).getAllByRole('spinbutton')
    await fireEvent.update(sizes.at(-1)!, '0.75')
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    const sent = calls.find((c) => c.method === 'PUT')!.body!.document.radii!
    expect(sent).toHaveLength(6)
    expect(sent.at(-1)).toMatchObject({ name: 'Card', rem: 0.75 })
    expect(sent.at(-1)!.id).toMatch(/^radius-[a-z0-9]{8}$/)
  })

  it('asks how many blocks use a corner radius before deleting it', async () => {
    const calls = stub({ usage: 3 })
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await renderView()

    const row = within(radii()).getByDisplayValue('Medium').closest('li')!
    await fireEvent.click(within(row).getByRole('button', { name: en.design.remove }))

    await waitFor(() => expect(confirm).toHaveBeenCalled())
    expect(calls.some((c) => c.url.endsWith('/token-usages/radius-md'))).toBe(true)
    expect(confirm.mock.calls[0][0]).toContain('3')
    expect(radiusNames()).not.toContain('Medium')
  })

  it('does not offer to delete the last corner radius', async () => {
    const one = JSON.parse(JSON.stringify(defaultTokens)) as TokenSet
    one.radii = [{ id: 'radius-only', name: 'Only', rem: 0.5 }]
    stub({ document: one })
    await renderView()

    expect((within(radii()).getByRole('button', { name: en.design.remove }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('shows the corner radii read-only to a member who may not change them', async () => {
    stub({ canManage: false })
    await renderView()

    expect((within(radii()).getByDisplayValue('Medium') as HTMLInputElement).disabled).toBe(true)
    expect(within(radii()).queryByRole('button', { name: en.design.radii.add })).toBeNull()
  })

  it('shows the tokens read-only to a member who may not change them', async () => {
    stub({ canManage: false })
    await renderView()

    expect((within(colours()).getByDisplayValue('Brand') as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByLabelText(en.design.widths.wide) as HTMLInputElement).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: en.design.save })).toBeNull()
    expect(screen.queryByRole('button', { name: en.design.colors.add })).toBeNull()
    expect(screen.getByText(en.design.readOnly)).toBeTruthy()
  })

  it('publishes what a save in flight stores, instead of racing it with a second save (#68)', async () => {
    const calls: Call[] = []
    let version = 0
    let document: TokenSet = JSON.parse(JSON.stringify(defaultTokens))
    let release: () => void = () => {}
    const held = new Promise<void>((resolve) => { release = resolve })
    const publication = (liveNumber: number) => ({ liveNumber, liveSinceUtc: null, liveBy: null, hasUnpublishedChanges: false })
    const tokens = () => ({
      siteId: SITE, version, isDefault: version === 0, document, updatedAtUtc: null, updatedBy: null,
      permissions: { canManage: true, canPublish: true }, publication: publication(0),
    })

    // Like the server: a save against a version that is no longer current is refused.
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase()
      const body = init?.body ? JSON.parse(String(init.body)) : undefined
      calls.push({ method, url, body })

      let status = 200
      let reply: unknown
      if (method === 'PUT') {
        if (body.version !== version) {
          status = 409
          reply = { title: 'Conflict', status, errorCode: 'editor.out_of_date' }
        } else {
          version += 1
          document = body.document
          await held
          reply = tokens()
        }
      } else if (url.endsWith('/publish')) {
        reply = publication(1)
      } else if (url.endsWith('/versions')) {
        reply = { publication: publication(0), versions: [] }
      } else {
        reply = tokens()
      }
      return { ok: status < 300, status, statusText: 'x', headers: { get: () => null }, json: async () => reply, text: async () => JSON.stringify(reply) }
    }))
    await renderView()

    await fireEvent.update(within(colours()).getByDisplayValue('Brand'), 'Primary')
    await fireEvent.click(screen.getByRole('button', { name: en.design.save }))
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT')).toHaveLength(1))
    await fireEvent.click(screen.getByRole('button', { name: en.publishing.design.publish }))
    release()

    await waitFor(() => expect(screen.getByText(en.publishing.design.published.replace('{number}', '1'))).toBeTruthy())
    expect(calls.filter((c) => c.method === 'PUT')).toHaveLength(1)
    expect(calls.find((c) => c.url.endsWith('/publish'))!.body).toEqual({ version: 1 })
    expect(screen.queryByText(en.editor.errors.server.editor_out_of_date)).toBeNull()
  })
})
