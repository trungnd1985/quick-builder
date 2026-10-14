import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import ItemHistoryPanel from '../../../src/features/cms/items/ItemHistoryPanel.vue'
import type { ItemHistory } from '../../../src/api/items'
import { COLLECTION, en, i18n, item, problem, SITE, stubFetch, violations, type Call, type Reply } from './itemHarness'

// 015 US7: kept versions with the live one marked, what happened, rollback refused with reasons,
// and copying a version into the draft.

const dana = { id: 'u', name: 'Dana' }
const history: ItemHistory = {
  versions: [
    { number: 2, kind: 'rollback', restoredFrom: 1, publishedAtUtc: '2026-10-08T10:00:00Z', publishedBy: dana, live: true },
    { number: 1, kind: 'publish', restoredFrom: null, publishedAtUtc: '2026-10-08T09:00:00Z', publishedBy: dana, live: false },
  ],
  events: [
    { kind: 'rolledBack', versionNumber: 2, scheduleAction: null, dueAtUtc: null, failureCode: null, atUtc: '2026-10-08T10:00:00Z', by: dana },
    { kind: 'scheduleFailed', versionNumber: null, scheduleAction: 'publish', dueAtUtc: null, failureCode: 'cms.item_needs_attention', atUtc: '2026-10-08T09:30:00Z', by: dana },
  ],
}

function renderPanel(reply: (call: Call) => Reply) {
  const calls = stubFetch(reply)
  const result = render(ItemHistoryPanel, { props: { siteId: SITE, collectionId: COLLECTION, item: item({ status: 'published' }) }, global: { plugins: [i18n] } })
  return { ...result, calls }
}

describe('ItemHistoryPanel', () => {
  it('lists versions with the live one marked, and events with their reasons', async () => {
    const { container } = renderPanel(() => ({ status: 200, body: history }))
    await waitFor(() => expect(screen.getByText(en.cms.items.history.live)).toBeTruthy())

    expect(screen.getByText(/Rolled back to version 1/)).toBeTruthy()
    expect(screen.getByText(/The scheduled publish failed/)).toBeTruthy()
    expect(screen.getAllByRole('button', { name: /Publish again/ })).toHaveLength(1)
    expect(await violations(container)).toEqual([])
  })

  it('rolls back after confirming, and shows the reasons when refused', async () => {
    const { calls } = renderPanel((call) =>
      call.url.endsWith('/rollback') ? problem(409, 'cms.version_not_publishable', { report: { problems: [{ fieldId: 'f7', problem: 'reference_missing' }] } }) : { status: 200, body: history })
    await waitFor(() => expect(screen.getByText(en.cms.items.history.live)).toBeTruthy())

    await fireEvent.click(screen.getByRole('button', { name: /Publish again/ }))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))

    await waitFor(() => expect(screen.getByText(en.cms.items.problems.reference_missing)).toBeTruthy())
    expect(calls.find((c) => c.url.endsWith('/versions/1/rollback'))?.body).toEqual({ revision: 3, cancelSchedule: false, redirectOldAddress: false })
  })

  it('copies a version into the draft', async () => {
    const { emitted, calls } = renderPanel((call) => (call.url.endsWith('/copy-to-draft') ? { status: 200, body: item({ revision: 4 }) } : { status: 200, body: history }))
    await waitFor(() => expect(screen.getByText(en.cms.items.history.live)).toBeTruthy())

    await fireEvent.click(screen.getAllByRole('button', { name: /Copy to draft/ })[1]!)
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.publish.confirm }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    expect(calls.some((c) => c.url.endsWith('/versions/1/copy-to-draft'))).toBe(true)
  })
})
