import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import ScheduleDialog from '../../src/features/publishing/ScheduleDialog.vue'
import PublishMenu from '../../src/features/publishing/PublishMenu.vue'
import PublicationBadge from '../../src/features/publishing/PublicationBadge.vue'
import type { Publication, Schedule } from '../../src/api/publishing'
import en from '../../src/i18n/en.json'
import { i18n } from '../editor/harness'

// 012 US5, FR-018 to FR-021: scheduling in the member's own time zone, refusing the past,
// changing or cancelling what is set, and a manual publish saying it cancels the schedule.

const SITE = 'site-1'
const PAGE = 'page-1'
const dana = { id: 'u', displayName: 'Dana' }

const draft: Publication = {
  state: 'draft', liveNumber: null, liveSinceUtc: null, liveBy: null, hasUnpublishedChanges: false, liveUrl: null, pendingSchedules: [], failedSchedule: null,
}
const live: Publication = { ...draft, state: 'published', liveNumber: 1, liveUrl: 'https://example.test/about' }

function pending(action: Schedule['action'], dueAtUtc: string): Schedule {
  return { action, dueAtUtc, state: 'pending', failureCode: null, setBy: dana, capturedDraftVersion: action === 'publish' ? 4 : null }
}

interface Call { url: string; method: string; body?: Record<string, unknown> }

function stubFetch(): Call[] {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ url, method, body })
    let reply: unknown = {}
    let status = 200
    if (url.endsWith('/draft')) reply = { schemaVersion: 1, version: 42, document: {} }
    else if (method === 'PUT') reply = pending(body.draftVersion ? 'publish' : 'unpublish', body.dueAtUtc)
    else if (method === 'DELETE') status = 204
    else if (method === 'GET') reply = { id: PAGE, title: 'About', publication: { ...draft, pendingSchedules: [pending('publish', '2030-01-01T10:00:00Z')] } }
    else reply = live
    return { ok: status < 300, status, statusText: 'x', headers: { get: () => null }, json: async () => reply, text: async () => JSON.stringify(reply) }
  }))
  return calls
}

function mountDialog(publication: Publication = draft, draftVersion = vi.fn(async () => 42)) {
  return render(ScheduleDialog, {
    props: { dialogId: 's', siteId: SITE, kind: 'page', subjectId: PAGE, title: 'About', publication, draftVersion },
    global: { plugins: [i18n] },
  })
}

describe('ScheduleDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('names the time zone and sends the local date and time as UTC with the draft version', async () => {
    const calls = stubFetch()
    const { emitted } = mountDialog()

    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone
    expect(screen.getByText(en.publishing.schedule.timeZone.replace('{zone}', zone))).toBeTruthy()

    await fireEvent.update(screen.getByLabelText(en.publishing.schedule.date), '2030-06-15')
    await fireEvent.update(screen.getByLabelText(en.publishing.schedule.time), '14:30')
    await fireEvent.click(screen.getByRole('button', { name: en.publishing.schedule.save }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    const put = calls.find((c) => c.method === 'PUT')!
    expect(put.url).toBe(`/api/v1/pages/sites/${SITE}/pages/${PAGE}/schedules/publish`)
    expect(put.body).toEqual({ dueAtUtc: new Date('2030-06-15T14:30:00').toISOString(), draftVersion: 42, redirectToPageId: null })
  })

  it('refuses a time in the past without asking the server', async () => {
    const calls = stubFetch()
    mountDialog()

    await fireEvent.update(screen.getByLabelText(en.publishing.schedule.date), '2020-01-01')
    await fireEvent.update(screen.getByLabelText(en.publishing.schedule.time), '09:00')
    await fireEvent.click(screen.getByRole('button', { name: en.publishing.schedule.save }))

    expect(screen.getByRole('alert').textContent).toBe(en.publishing.schedule.inPast)
    expect(calls).toHaveLength(0)
  })

  it('offers unpublishing only for a live page, and sends no draft version for it', async () => {
    const calls = stubFetch()
    const draftVersion = vi.fn(async () => 42)
    mountDialog(live, draftVersion)

    await fireEvent.update(screen.getByLabelText(en.publishing.schedule.action), 'unpublish')
    await fireEvent.update(screen.getByLabelText(en.publishing.schedule.date), '2030-06-15')
    await fireEvent.click(screen.getByRole('button', { name: en.publishing.schedule.save }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    const put = calls.find((c) => c.method === 'PUT')!
    expect(put.url.endsWith('/schedules/unpublish')).toBe(true)
    expect(put.body!.draftVersion).toBeUndefined()
    // 017 FR-032: no redirect unless one is chosen.
    expect(put.body!.redirectToPageId).toBeNull()
    expect(draftVersion).not.toHaveBeenCalled()
  })

  it('has no unpublish choice for a draft page', () => {
    stubFetch()
    mountDialog(draft)
    expect(screen.queryByLabelText(en.publishing.schedule.action)).toBeNull()
  })

  it('shows an existing schedule with Change and Cancel', async () => {
    const calls = stubFetch()
    const existing = { ...draft, pendingSchedules: [pending('publish', '2030-01-01T10:00:00Z')] }
    const { emitted } = mountDialog(existing)

    expect(screen.getByText(/Currently scheduled for/)).toBeTruthy()
    expect(screen.getByRole('button', { name: en.publishing.menu.changeSchedule })).toBeTruthy()
    const local = new Date('2030-01-01T10:00:00Z')
    expect((screen.getByLabelText(en.publishing.schedule.date) as HTMLInputElement).value)
      .toBe(`${local.getFullYear()}-${String(local.getMonth() + 1).padStart(2, '0')}-${String(local.getDate()).padStart(2, '0')}`)

    await fireEvent.click(screen.getByRole('button', { name: en.publishing.menu.cancelSchedule }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    expect(calls[0]).toMatchObject({ method: 'DELETE', url: `/api/v1/pages/sites/${SITE}/pages/${PAGE}/schedules/publish` })
  })

  it('closes on Escape, focuses the first field, and has no accessibility violations', async () => {
    stubFetch()
    const { emitted, container } = mountDialog(live)

    await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText(en.publishing.schedule.action)))
    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'select-name'] },
    })
    expect(results.violations).toEqual([])

    await fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(emitted().close).toBeTruthy()
  })
})

describe('PublishMenu with schedules', () => {
  afterEach(() => vi.unstubAllGlobals())

  const mountMenu = (publication: Publication) => render(PublishMenu, {
    props: { siteId: SITE, kind: 'page', subjectId: PAGE, title: 'About', publication, canPublish: true },
    global: { plugins: [i18n] },
  })

  it('warns that a manual publish cancels the pending scheduled one', async () => {
    const calls = stubFetch()
    mountMenu({ ...draft, pendingSchedules: [pending('publish', '2030-01-01T10:00:00Z')] })

    await fireEvent.click(screen.getByRole('button', { name: en.publishing.menu.publish }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('This cancels the publish scheduled for')
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await fireEvent.click(within(dialog).getByRole('button', { name: en.publishing.menu.publish }))
    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/publish') && c.method === 'POST')).toBe(true))
  })

  it('publishes at once when nothing is scheduled', async () => {
    const calls = stubFetch()
    mountMenu(draft)

    await fireEvent.click(screen.getByRole('button', { name: en.publishing.menu.publish }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('opens the dialog from Schedule, and refreshes the publication after scheduling', async () => {
    const calls = stubFetch()
    const { emitted } = mountMenu(draft)

    await fireEvent.click(screen.getByRole('button', { name: en.publishing.menu.schedule }))
    await fireEvent.update(screen.getByLabelText(en.publishing.schedule.date), '2030-06-15')
    await fireEvent.click(screen.getByRole('button', { name: en.publishing.schedule.save }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    const refreshed = (emitted().changed as Publication[][])[0]![0]!
    expect(refreshed.pendingSchedules).toHaveLength(1)
    expect(calls.at(-1)!.url).toBe(`/api/v1/pages/sites/${SITE}/pages/${PAGE}`)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('offers Change schedule when something is pending', () => {
    stubFetch()
    mountMenu({ ...draft, pendingSchedules: [pending('publish', '2030-01-01T10:00:00Z')] })
    expect(screen.getByRole('button', { name: en.publishing.menu.changeSchedule })).toBeTruthy()
  })
})

describe('PublicationBadge with schedules', () => {
  const badge = (publication: Publication) => render(PublicationBadge, { props: { publication }, global: { plugins: [i18n] } })

  it('shows a pending schedule, an overdue one, and a failure', () => {
    badge({
      ...live,
      pendingSchedules: [pending('publish', '2099-01-01T10:00:00Z'), pending('unpublish', '2000-01-01T10:00:00Z')],
      failedSchedule: { ...pending('publish', '2000-01-01T10:00:00Z'), state: 'failed', failureCode: 'pages.unpublished_components' },
    })

    expect(screen.getByText(/^Publishes/)).toBeTruthy()
    expect(screen.getByText(/^Unpublish overdue since/)).toBeTruthy()
    expect(screen.getByText(en.publishing.badge.failed).getAttribute('title')).toBe(en.publishing.badge.failures.unpublished_components)
  })
})
