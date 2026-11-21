import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import PreviewLinksView from '../../../src/features/preview/PreviewLinksView.vue'
import en from '../../../src/i18n/en.json'

// 022 FR-015, contracts/admin-preview.md §3: every active link of the site, with its target,
// revocable one by one or all at once.

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric' } } },
})

function entry(id: string, target: string, kind: 'page' | 'item' = 'page') {
  return {
    link: {
      id,
      documentKind: kind,
      documentId: 'd',
      versionNumber: null,
      sampleItemId: null,
      label: null,
      createdBy: { id: 'u', displayName: 'Lan' },
      createdAtUtc: '2026-11-19T10:00:00Z',
      expiresAtUtc: '2026-11-26T10:00:00Z',
      lastOpenedAtUtc: null,
    },
    targetLabel: target,
    collectionId: kind === 'item' ? 'c' : null,
  }
}

function serve(initial: ReturnType<typeof entry>[]) {
  const calls: string[] = []
  let items = initial
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    calls.push(`${init?.method ?? 'GET'} ${url}`)
    if (init?.method === 'POST') {
      items = String(url).endsWith('/revoke-all') ? [] : items.filter((i) => !String(url).includes(i.link.id))
      return { ok: true, status: String(url).endsWith('/revoke-all') ? 200 : 204, json: async () => ({ revoked: 2 }), headers: new Headers() }
    }
    return { ok: true, status: 200, json: async () => ({ items, nextCursor: null }), headers: new Headers() }
  }))
  return calls
}

async function renderView() {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/sites/:siteId/preview-links', component: PreviewLinksView }] })
  await router.push(`/sites/${SITE}/preview-links`)
  return render(PreviewLinksView, { global: { plugins: [i18n, router] } })
}

describe('PreviewLinksView', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists every active link with what it shows, accessibly', async () => {
    serve([entry('aaa', 'Pricing'), entry('bbb', 'Hello world', 'item')])
    const { container } = await renderView()

    expect(await screen.findByText('Pricing')).toBeTruthy()
    expect(screen.getByText('Hello world')).toBeTruthy()
    expect(screen.getByText(en.preview.links.kindItem)).toBeTruthy()

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['button-name', 'th-has-data-cells', 'aria-valid-attr-value'] } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('revokes one after confirming, and all after confirming the count', async () => {
    const calls = serve([entry('aaa', 'Pricing'), entry('bbb', 'About')])
    await renderView()

    const row = (await screen.findByText('Pricing')).closest('tr')!
    await fireEvent.click(within(row).getByRole('button', { name: en.preview.share.revoke }))
    await fireEvent.click(within(row).getAllByRole('button', { name: en.preview.share.revoke })[0]!)
    await waitFor(() => expect(calls).toContain(`POST /api/v1/editor/sites/${SITE}/preview-links/aaa/revoke`))
    await waitFor(() => expect(screen.queryByText('Pricing')).toBeNull())

    await fireEvent.click(screen.getByRole('button', { name: en.preview.share.revokeAll }))
    expect(screen.getByText(en.preview.links.revokeAllConfirm.replace('{count}', '1'))).toBeTruthy()
    await fireEvent.click(screen.getAllByRole('button', { name: en.preview.share.revokeAll })[0]!)
    expect(await screen.findByText(en.preview.links.empty)).toBeTruthy()
  })

  it('says when there is nothing to show', async () => {
    serve([])
    await renderView()

    expect(await screen.findByText(en.preview.links.empty)).toBeTruthy()
  })
})
