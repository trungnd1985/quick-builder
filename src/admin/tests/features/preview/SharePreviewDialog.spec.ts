import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import SharePreviewDialog from '../../../src/features/preview/SharePreviewDialog.vue'
import en from '../../../src/i18n/en.json'

// 022 FR-005, FR-014, FR-018, contracts/admin-preview.md §2: create a link with a label and an
// expiry (7 days by default), see it once, copy it, list the active links, revoke them.

const SITE = '11111111-1111-1111-1111-111111111111'
const PAGE = '22222222-2222-2222-2222-222222222222'
const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: 'numeric' } } },
})

function link(id: string, extra: Record<string, unknown> = {}) {
  return {
    id,
    documentKind: 'page',
    documentId: PAGE,
    versionNumber: null,
    sampleItemId: null,
    label: null,
    createdBy: { id: 'u', displayName: 'Lan' },
    createdAtUtc: '2026-11-19T10:00:00Z',
    expiresAtUtc: '2026-11-26T10:00:00Z',
    lastOpenedAtUtc: null,
    ...extra,
  }
}

type Call = { url: string; method: string; body: unknown }

function serve(initial: unknown[], create: { status: number; body: unknown } = { status: 201, body: {} }) {
  const calls: Call[] = []
  let links = initial
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    const body = init?.body ? JSON.parse(String(init.body)) : null
    calls.push({ url: String(url), method, body })
    if (method === 'POST' && String(url).endsWith('/preview-links')) {
      if (create.status === 201) links = [...links, (create.body as { link: unknown }).link]
      return { ok: create.status < 300, status: create.status, json: async () => create.body, text: async () => JSON.stringify(create.body), headers: new Headers() }
    }
    if (method === 'POST') {
      links = String(url).endsWith('/revoke-all') ? [] : links.filter((l) => !String(url).includes((l as { id: string }).id))
      return { ok: true, status: String(url).endsWith('/revoke-all') ? 200 : 204, json: async () => ({ revoked: 1 }), headers: new Headers() }
    }
    return { ok: true, status: 200, json: async () => links, headers: new Headers() }
  }))
  return calls
}

const renderDialog = () => render(SharePreviewDialog, { props: { siteId: SITE, kind: 'page', documentId: PAGE }, global: { plugins: [i18n] } })

describe('SharePreviewDialog', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('creates a seven-day link by default, shows it once and copies it, accessibly', async () => {
    const created = link('l1', { label: 'Client' })
    const calls = serve([], { status: 201, body: { link: created, url: 'https://example.com/_preview/l/secret' } })
    const writeText = vi.fn(async () => undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const { container } = renderDialog()

    expect((screen.getByLabelText(en.preview.share.expiry.week) as HTMLInputElement).checked).toBe(true)
    await fireEvent.update(screen.getByLabelText(en.preview.share.label), ' Client ')
    await fireEvent.click(screen.getByRole('button', { name: en.preview.share.create }))

    const field = (await screen.findByLabelText(en.preview.share.linkLabel)) as HTMLInputElement
    expect(field.value).toBe('https://example.com/_preview/l/secret')
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.body).toMatchObject({ documentKind: 'page', documentId: PAGE, label: 'Client' })
    const days = (Date.parse((post.body as { expiresAtUtc: string }).expiresAtUtc) - Date.now()) / 86_400_000
    expect(days).toBeGreaterThan(6.9)
    expect(days).toBeLessThan(7.1)

    await fireEvent.click(screen.getByRole('button', { name: en.preview.share.copy }))
    expect(writeText).toHaveBeenCalledWith('https://example.com/_preview/l/secret')
    expect(await screen.findByText(en.preview.share.copied)).toBeTruthy()
    expect(await screen.findByText('Client')).toBeTruthy()

    const results = await axe.run(container.ownerDocument.body, { runOnly: { type: 'rule', values: ['label', 'button-name', 'aria-dialog-name', 'aria-valid-attr-value'] } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('shows each refusal in words', async () => {
    serve([], { status: 409, body: { title: 'Conflict', status: 409, errorCode: 'preview.no_template' } })
    renderDialog()

    await fireEvent.click(screen.getByRole('button', { name: en.preview.share.create }))

    expect(await screen.findByText(en.preview.share.errors.no_template)).toBeTruthy()
  })

  it('disables creating at twenty active links', async () => {
    serve(Array.from({ length: 20 }, (_, i) => link(`l${i}`)))
    renderDialog()

    await waitFor(() => expect((screen.getByRole('button', { name: en.preview.share.create }) as HTMLButtonElement).disabled).toBe(true))
  })

  it('revokes one link after confirming, and all of them', async () => {
    const calls = serve([link('aaa', { label: 'One' }), link('bbb', { label: 'Two' })])
    renderDialog()

    const row = (await screen.findByText('One')).closest('tr')!
    await fireEvent.click(within(row).getByRole('button', { name: en.preview.share.revoke }))
    await fireEvent.click(within(row).getAllByRole('button', { name: en.preview.share.revoke })[0]!)
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/aaa/revoke'))).toBe(true))
    await waitFor(() => expect(screen.queryByText('One')).toBeNull())

    expect(screen.queryByRole('button', { name: en.preview.share.revokeAll })).toBeNull()
  })
})
