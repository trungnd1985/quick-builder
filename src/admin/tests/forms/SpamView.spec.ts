import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import axe from 'axe-core'
import SpamView from '../../src/features/forms/SpamView.vue'
import en from '../../src/i18n/en.json'
import { page, SITE, stubFetch, SUB_A, SUB_B, summary, type Reply } from './formsFixtures'

// 018 US5 (FR-016a, clarification 5): the Spam list — restore to the inbox, delete only for
// those allowed, an empty state.

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' } } },
})
const LIST = `/api/v1/forms/sites/${SITE}/submissions`

afterEach(() => vi.unstubAllGlobals())

function serve(items = [summary(SUB_A), summary(SUB_B)], canDelete = true) {
  return (method: string, url: string): Reply => {
    if (url === '/api/v1/sites') return { status: 200, body: [{ id: SITE, name: 'Acme', hostNames: ['acme.test'] }] }
    if (url.endsWith('/restore')) return { status: 200, body: {} }
    if (method === 'DELETE') return { status: 200, body: { deleted: 1 } }
    return { status: 200, body: page(items, canDelete) }
  }
}

async function renderView() {
  setActivePinia(createPinia())
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/submissions', name: 'site-submissions', component: { template: '<div />' } },
      { path: '/sites/:siteId/submissions/spam', name: 'site-spam', component: SpamView },
    ],
  })
  await router.push(`/sites/${SITE}/submissions/spam`)
  await router.isReady()
  return render({ components: { RouterView }, template: '<RouterView />' }, { global: { plugins: [i18n, router] } })
}

describe('the Spam list', () => {
  it('lists spam and restores a submission to the inbox', async () => {
    const calls = stubFetch(serve())
    await renderView()

    await waitFor(() => expect(calls.some((c) => c.url === `${LIST}?box=spam`)).toBe(true))
    const restore = await screen.findAllByRole('button', { name: /^Restore the submission of/ })
    await fireEvent.click(restore[0]!)

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.url === `${LIST}/${SUB_A}/restore`)).toBe(true))
    expect(await screen.findByText(en.forms.spam.restored)).toBeTruthy()
    expect(screen.getAllByRole('button', { name: /^Restore the submission of/ })).toHaveLength(1)
  })

  it('deletes after confirming, for those allowed', async () => {
    const calls = stubFetch(serve())
    vi.stubGlobal('confirm', vi.fn(() => true))
    await renderView()

    await fireEvent.click((await screen.findAllByRole('button', { name: /^Delete the submission of/ }))[1]!)

    await waitFor(() => expect(calls.find((c) => c.method === 'DELETE')?.body).toEqual({ ids: [SUB_B] }))
  })

  it('offers no delete to Editors', async () => {
    stubFetch(serve(undefined, false))
    await renderView()

    await screen.findAllByRole('button', { name: /^Restore the submission of/ })
    expect(screen.queryByRole('button', { name: /^Delete the submission of/ })).toBeNull()
  })

  it('says when there is no spam', async () => {
    stubFetch(serve([]))
    await renderView()

    expect(await screen.findByText(en.forms.spam.empty)).toBeTruthy()
  })

  it('has no accessibility violations', async () => {
    stubFetch(serve())
    const { container } = await renderView()
    await screen.findAllByRole('button', { name: /^Restore the submission of/ })

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['button-name', 'heading-order', 'link-name', 'aria-valid-attr-value'] } })
    expect(results.violations).toEqual([])
  })
})
