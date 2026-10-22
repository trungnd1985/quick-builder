import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import axe from 'axe-core'
import SubmissionsView from '../../src/features/forms/SubmissionsView.vue'
import en from '../../src/i18n/en.json'
import { forms, page, SITE, stubFetch, SUB_A, SUB_B, summary, type Reply } from './formsFixtures'

// 018 US3 (FR-025 to FR-030): the submission list — filters and search sent to the server,
// paging with its cursor, selection and delete for those allowed, the export link carrying the
// same filters, unread and "no longer published" marked, and retention shown per form.

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' } } },
})
const LIST = `/api/v1/forms/sites/${SITE}/submissions`

afterEach(() => vi.unstubAllGlobals())

function serve(listReply: (url: string) => Reply) {
  return (method: string, url: string): Reply => {
    if (url === '/api/v1/sites') return { status: 200, body: [{ id: SITE, name: 'Acme', hostNames: ['acme.test'] }] }
    if (url.endsWith('/forms')) return { status: 200, body: { items: forms } }
    if (method === 'DELETE') return { status: 200, body: { deleted: 1 } }
    return listReply(url)
  }
}

async function renderView(query = '') {
  setActivePinia(createPinia())
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/submissions', name: 'site-submissions', component: SubmissionsView },
      { path: '/sites/:siteId/submissions/:submissionId', name: 'site-submission', component: { template: '<div />' } },
    ],
  })
  await router.push(`/sites/${SITE}/submissions${query}`)
  await router.isReady()
  const result = render({ components: { RouterView }, template: '<RouterView />' }, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: en.forms.submissions.title })).toBeTruthy())
  return { ...result, router }
}

describe('the submissions list', () => {
  it('lists submissions newest first with unread and unpublished forms marked', async () => {
    stubFetch(serve(() => ({ status: 200, body: page([summary(SUB_A, { read: false }), summary(SUB_B, { formName: 'Old enquiry', formPublished: false })]) })))
    await renderView()

    const rows = await screen.findAllByRole('row')
    expect(rows).toHaveLength(3)
    expect(within(rows[1]!).getByText(en.forms.submissions.unread)).toBeTruthy()
    expect(within(rows[2]!).getByText(en.forms.submissions.notPublished)).toBeTruthy()
    expect(within(rows[1]!).getByRole('link').getAttribute('href')).toBe(`/sites/${SITE}/submissions/${SUB_A}`)
  })

  it('sends filters and search to the server and carries them to the export link', async () => {
    const calls = stubFetch(serve(() => ({ status: 200, body: page([summary(SUB_A)]) })))
    const { router } = await renderView()
    await screen.findAllByRole('row')

    await fireEvent.update(screen.getByLabelText(en.forms.filters.search), '  anna smith ')
    await fireEvent.update(screen.getByLabelText(en.forms.filters.form), 'contact')
    await fireEvent.update(screen.getByLabelText(en.forms.filters.read), 'unread')
    await fireEvent.click(screen.getByRole('button', { name: en.forms.filters.apply }))

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ formId: 'contact', read: 'unread', q: 'anna smith' }))
    await waitFor(() => expect(calls.some((c) => c.url === `${LIST}?formId=contact&read=unread&q=anna+smith`)).toBe(true))
    expect(screen.getByRole('link', { name: en.forms.submissions.export }).getAttribute('href'))
      .toBe(`${LIST}/export?formId=contact&read=unread&q=anna+smith`)
  })

  it('says when nothing matches the filters', async () => {
    stubFetch(serve(() => ({ status: 200, body: page([]) })))
    await renderView('?q=nobody')

    expect(await screen.findByText(en.forms.submissions.emptyFiltered)).toBeTruthy()
  })

  it('pages with the server cursor', async () => {
    const calls = stubFetch(serve((url) => ({
      status: 200,
      body: url.includes('after=') ? page([summary(SUB_B)]) : page([summary(SUB_A)], true, 'cursor-1'),
    })))
    await renderView()

    await fireEvent.click(await screen.findByRole('button', { name: en.forms.submissions.more }))

    await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(3))
    expect(calls.some((c) => c.url === `${LIST}?after=cursor-1`)).toBe(true)
    expect(screen.queryByRole('button', { name: en.forms.submissions.more })).toBeNull()
  })

  it('deletes the selected submissions after confirming', async () => {
    const calls = stubFetch(serve(() => ({ status: 200, body: page([summary(SUB_A), summary(SUB_B)]) })))
    vi.stubGlobal('confirm', vi.fn(() => true))
    await renderView()
    await screen.findAllByRole('row')

    const remove = screen.getByRole('button', { name: en.forms.submissions.delete }) as HTMLButtonElement
    expect(remove.disabled).toBe(true)
    await fireEvent.click(screen.getAllByRole('checkbox')[1]!)
    await fireEvent.click(remove)

    await waitFor(() => expect(calls.find((c) => c.method === 'DELETE')?.body).toEqual({ ids: [SUB_A] }))
    expect(await screen.findByText('1 submission(s) deleted.')).toBeTruthy()
  })

  it('hides selection and delete from those who may not delete', async () => {
    stubFetch(serve(() => ({ status: 200, body: page([summary(SUB_A)], false) })))
    await renderView()
    await screen.findAllByRole('row')

    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByRole('button', { name: en.forms.submissions.delete })).toBeNull()
    expect(screen.getByRole('link', { name: en.forms.submissions.export })).toBeTruthy()
  })

  it('shows each form with its retention', async () => {
    stubFetch(serve(() => ({ status: 200, body: page([summary(SUB_A)]) })))
    await renderView()

    const list = within(await screen.findByRole('region', { name: en.forms.submissions.formsHeading }))
    expect(list.getByText(/Contact\s+— Kept 90 days/)).toBeTruthy()
    expect(list.getByText(/Kept until deleted/)).toBeTruthy()
  })

  it('has no accessibility violations', async () => {
    stubFetch(serve(() => ({ status: 200, body: page([summary(SUB_A, { read: false })]) })))
    const { container } = await renderView()
    await screen.findAllByRole('row')

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order', 'link-name'] } })
    expect(results.violations).toEqual([])
  })
})
