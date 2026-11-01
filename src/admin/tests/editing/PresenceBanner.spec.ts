import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import PresenceBanner from '../../src/editing/PresenceBanner.vue'
import type { Presence } from '../../src/api/editing'
import { i18n } from '../editor/harness'

// 023 US2, US3, contracts/admin-editing.md §2 and §4, research CE-19.

const anna = { id: 'u-anna', displayName: 'Anna Tran' }
const lock = (extra: Partial<NonNullable<Presence['lock']>> = {}): Presence['lock'] => ({
  holderSessionId: 's-anna', holder: anna, since: '2026-10-25T09:50:00Z', stale: false, inactiveSeconds: 30, isYou: false, isYourOtherSession: false, ...extra,
})
const presence = (extra: Partial<Presence> = {}): Presence =>
  ({ sessionId: 's-me', serverNow: '2026-10-25T10:00:00Z', role: 'viewer', access: 'edit', lock: null, lostLock: null, others: [], ...extra })

function show(status: string, value: Presence, extra: Record<string, unknown> = {}) {
  return render(PresenceBanner, { props: { kind: 'page', status, presence: value, ...extra }, global: { plugins: [i18n] } })
}

describe('PresenceBanner', () => {
  afterEach(() => vi.restoreAllMocks())

  it('names who is editing and since when, read-only, with no way to take over an active lock', async () => {
    const { container } = show('locked', presence({ lock: lock() }))

    expect(container.textContent).toContain('Anna Tran is editing this page')
    expect(container.textContent).toContain(en.editing.presence.readOnlyHelp)
    expect(screen.queryByRole('button', { name: en.editing.presence.takeOver })).toBeNull()
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('offers a takeover of a stale lock, after confirming', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    const { container, emitted } = show('locked-stale', presence({ lock: lock({ stale: true, inactiveSeconds: 62 * 60 }) }))

    expect(container.textContent).toContain('62 minutes')
    await fireEvent.click(screen.getByRole('button', { name: en.editing.presence.takeOver }))
    expect(emitted().takeOver).toBeUndefined()
    await fireEvent.click(screen.getByRole('button', { name: en.editing.presence.takeOver }))
    expect(emitted().takeOver).toHaveLength(1)
    expect(confirm).toHaveBeenCalledTimes(2)
  })

  it('offers Edit here instead for the member’s own other session', async () => {
    const { emitted } = show('locked-self', presence({ lock: lock({ isYourOtherSession: true }) }))

    await fireEvent.click(screen.getByRole('button', { name: en.editing.presence.editHere }))
    expect(emitted().moveHere).toHaveLength(1)
  })

  it('tells the holder who else is viewing, and a viewer without edit rights that it is view-only', () => {
    const { container, unmount } = show('editing', presence({ role: 'holder', others: [{ member: { id: 'u-ben', displayName: 'Ben Hale' }, since: '2026-10-25T09:55:00Z', holdsLock: false }] }))
    expect(container.textContent).toContain('Also viewing: Ben Hale.')
    unmount()

    const second = show('view-only', presence({ access: 'view-only' }))
    expect(second.container.textContent).toContain('You can view this page, but not change it.')
  })

  it('says who took over, and when the document is free again', async () => {
    const { container, emitted, rerender } = show('taken-over', presence({ lostLock: { at: '2026-10-25T10:30:00Z', by: { id: 'u-ben', displayName: 'Ben Hale' }, reason: 'takeover' } }))
    expect(container.textContent).toContain('Ben Hale took over editing')
    await fireEvent.click(screen.getByRole('button', { name: en.editing.presence.reload }))
    expect(emitted().reload).toHaveLength(1)

    await rerender({ status: 'free', presence: presence() })
    await fireEvent.click(screen.getByRole('button', { name: en.editing.presence.edit }))
    expect(emitted().edit).toHaveLength(1)
  })

  it('announces changes in a polite live region', async () => {
    const { container, rerender } = show('editing', presence())
    const region = container.querySelector('[aria-live="polite"]')!

    await rerender({ status: 'locked', presence: presence({ lock: lock() }) })
    await waitFor(() => expect(region.textContent).toContain('Anna Tran is editing this page'))
  })
})
