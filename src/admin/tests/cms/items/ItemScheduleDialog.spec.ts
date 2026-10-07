import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import ItemScheduleDialog from '../../../src/features/cms/items/ItemScheduleDialog.vue'
import type { Item } from '../../../src/api/items'
import { COLLECTION, en, i18n, item, problem, SITE, stubFetch, violations, type Reply } from './itemHarness'

// 015 US6: schedules set, moved with or without recapturing the draft, and cancelled.

function renderDialog(current: Item, reply: Reply = { status: 200, body: current }) {
  const calls = stubFetch(() => reply)
  const result = render(ItemScheduleDialog, { props: { siteId: SITE, collectionId: COLLECTION, item: current }, global: { plugins: [i18n] } })
  return { ...result, calls }
}

const pending = (action: 'publish' | 'unpublish', dueAtUtc = '2030-01-01T09:00:00Z') => ({ action, dueAtUtc, setBy: { id: 'u', name: 'Dana' } })

describe('ItemScheduleDialog', () => {
  it('sets a publish with the revision, in UTC, and passes the accessibility check', async () => {
    const { calls, emitted, baseElement } = renderDialog(item())
    expect(await violations(baseElement)).toEqual([])

    const publish = screen.getByRole('group', { name: en.cms.items.schedule.publishAt })
    await fireEvent.update(within(publish).getByLabelText('Date'), '2030-06-01')
    await fireEvent.update(within(publish).getByLabelText('Time'), '09:30')
    await fireEvent.click(within(publish).getByRole('button', { name: en.cms.items.schedule.set }))

    await waitFor(() => expect(emitted().changed).toBeTruthy())
    expect(calls[0]!.body).toEqual({ dueAtUtc: new Date('2030-06-01T09:30:00').toISOString(), revision: 3, recapture: false })
    expect(calls[0]!.url).toMatch(/\/schedules\/publish$/)
  })

  it('refuses a time in the past before asking the server', async () => {
    const { calls } = renderDialog(item())
    const publish = screen.getByRole('group', { name: en.cms.items.schedule.publishAt })
    await fireEvent.update(within(publish).getByLabelText('Date'), '2020-01-01')
    await fireEvent.click(within(publish).getByRole('button', { name: en.cms.items.schedule.set }))
    expect(screen.getByRole('alert').textContent).toBe(en.cms.items.schedule.inPast)
    expect(calls).toHaveLength(0)
  })

  it('moves a pending publish, recapturing the draft when asked, and cancels it', async () => {
    const current = item({ status: 'published', schedules: [pending('publish')] })
    const { calls } = renderDialog(current)

    expect(screen.getByText(en.cms.items.schedule.captured)).toBeTruthy()
    await fireEvent.click(screen.getByRole('checkbox'))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.schedule.move }))
    await waitFor(() => expect(calls[0]?.body?.recapture).toBe(true))

    await fireEvent.click(screen.getAllByRole('button', { name: en.cms.items.schedule.cancel })[0]!)
    await waitFor(() => expect(calls[1]?.method).toBe('DELETE'))
    expect(calls[1]!.url).toMatch(/\/schedules\/publish\?revision=3$/)
  })

  it('offers an unpublish only for something published or about to be', () => {
    renderDialog(item())
    expect(screen.queryByRole('group', { name: en.cms.items.schedule.unpublishAt })).toBeNull()
  })

  it('shows the server refusal', async () => {
    renderDialog(item({ status: 'published' }), problem(400, 'cms.schedule_order'))
    const unpublish = screen.getByRole('group', { name: en.cms.items.schedule.unpublishAt })
    await fireEvent.update(within(unpublish).getByLabelText('Date'), '2030-06-01')
    await fireEvent.update(within(unpublish).getByLabelText('Time'), '09:30')
    await fireEvent.click(within(unpublish).getByRole('button', { name: en.cms.items.schedule.set }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(en.cms.items.errors.schedule_order))
  })
})
