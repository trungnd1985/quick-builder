import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import axe from 'axe-core'
import SubmissionDetail from '../../src/features/forms/SubmissionDetail.vue'
import en from '../../src/i18n/en.json'
import { detail, page, SITE, stubFetch, SUB_A, summary, type Reply } from './formsFixtures'

// 018 US3 (FR-026, FR-028): one submission — values under their labels, a checkbox group as a
// list, the notification state in words, mark unread, and delete only for those allowed.

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' } } },
})
const ONE = `/api/v1/forms/sites/${SITE}/submissions/${SUB_A}`

afterEach(() => vi.unstubAllGlobals())

function serve(submission: Reply, canDelete = true) {
  return (method: string, url: string): Reply => {
    if (url === ONE) return submission
    if (method === 'PUT' || method === 'DELETE') return { status: method === 'PUT' ? 204 : 200, body: { deleted: 1 } }
    return { status: 200, body: page([summary(SUB_A)], canDelete) }
  }
}

async function renderDetail() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/submissions', name: 'site-submissions', component: { template: '<p>list</p>' } },
      { path: '/sites/:siteId/submissions/:submissionId', name: 'site-submission', component: SubmissionDetail },
    ],
  })
  await router.push(`/sites/${SITE}/submissions/${SUB_A}?q=anna`)
  await router.isReady()
  const result = render({ components: { RouterView }, template: '<RouterView />' }, { global: { plugins: [i18n, router] } })
  return { ...result, router }
}

describe('a submission', () => {
  it('shows every value under its label and the notification state in words', async () => {
    stubFetch(serve({ status: 200, body: detail({ notification: 'failed', notificationError: '550 Mailbox unavailable' }) }))
    await renderDetail()

    expect(await screen.findByRole('heading', { level: 1, name: 'Submission from Contact' })).toBeTruthy()
    const values = screen.getByText('Email').closest('dl')!
    expect(values.textContent).toContain('anna@example.com')
    expect(values.textContent).toContain('Hello\nthere')
    expect(values.textContent).toContain('Sales, Support')
    expect(values.textContent).toContain(en.forms.detail.noValue)
    expect(screen.getByText(en.forms.notification.failed)).toBeTruthy()
    expect(screen.getByText('550 Mailbox unavailable')).toBeTruthy()
    expect(screen.getByText('Kept 90 days')).toBeTruthy()
    expect(screen.getByRole('link', { name: en.forms.detail.back }).getAttribute('href')).toBe(`/sites/${SITE}/submissions?q=anna`)
  })

  it('marks the submission unread', async () => {
    const calls = stubFetch(serve({ status: 200, body: detail() }))
    await renderDetail()

    await fireEvent.click(await screen.findByRole('button', { name: en.forms.detail.markUnread }))

    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')).toEqual({ url: `${ONE}/read`, method: 'PUT', body: { read: false } }))
    expect(await screen.findByRole('button', { name: en.forms.detail.markRead })).toBeTruthy()
  })

  it('deletes after confirming and returns to the list with its filters', async () => {
    const calls = stubFetch(serve({ status: 200, body: detail() }))
    vi.stubGlobal('confirm', vi.fn(() => true))
    const { router } = await renderDetail()

    await fireEvent.click(await screen.findByRole('button', { name: en.forms.detail.delete }))

    await waitFor(() => expect(router.currentRoute.value.fullPath).toBe(`/sites/${SITE}/submissions?q=anna`))
    expect(calls.find((c) => c.method === 'DELETE')?.body).toEqual({ ids: [SUB_A] })
  })

  it('offers no delete to those who may not delete', async () => {
    stubFetch(serve({ status: 200, body: detail() }, false))
    await renderDetail()

    await screen.findByRole('heading', { level: 1 })
    await waitFor(() => expect(screen.queryByRole('button', { name: en.forms.detail.delete })).toBeNull())
  })

  it('explains a submission that no longer exists', async () => {
    stubFetch(serve({ status: 404, body: { title: 'x', status: 404, errorCode: 'forms.submission_not_found' } }))
    await renderDetail()

    expect((await screen.findByRole('alert')).textContent).toContain(en.forms.errors.submission_not_found)
  })

  it('has no accessibility violations', async () => {
    stubFetch(serve({ status: 200, body: detail() }))
    const { container } = await renderDetail()
    await screen.findByRole('heading', { level: 1 })

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order', 'link-name', 'definition-list', 'dlitem'] } })
    expect(results.violations).toEqual([])
  })
})
