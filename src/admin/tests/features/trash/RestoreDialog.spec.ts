import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import RestoreDialog from '../../../src/features/trash/RestoreDialog.vue'
import type { RestorePlan, TrashEntry } from '../../../src/api/trash'
import en from '../../../src/i18n/en.json'

// 021 US1, US4 and contracts/admin-trash.md §4: the plan is shown before the restore, the
// restore confirms the address the member saw, "Restore and publish" is offered only with a
// version that was live, and a changed plan is shown again rather than acted on.

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const entry: TrashEntry = {
  kind: 'page',
  id: '22222222-2222-2222-2222-222222222222',
  label: 'Team',
  address: '/about/team',
  place: { parentId: '33333333-3333-3333-3333-333333333333', parentLabel: 'About', parentState: 'live' },
  trashedAtUtc: '2026-11-01T10:00:00Z',
  trashedBy: { userId: 'u1', displayName: 'Dana' },
  purgeOnUtc: '2026-12-01T10:00:00Z',
  wasLive: false,
  hadSchedule: false,
  canRestore: true,
  canPurge: false,
}

function plan(overrides: Partial<RestorePlan> = {}): RestorePlan {
  return {
    placement: 'original',
    placementOptions: ['original'],
    target: { parentId: '33333333-3333-3333-3333-333333333333', label: 'About', position: 0 },
    address: '/about/team',
    previousAddress: '/about/team',
    addressChanged: false,
    becomesHome: false,
    templateDropped: false,
    wasLive: false,
    hadSchedule: false,
    canRestoreAndPublish: false,
    ...overrides,
  }
}

function respond(...bodies: { status: number; body: unknown }[]) {
  const queue = [...bodies]
  vi.stubGlobal('fetch', vi.fn(async () => {
    const next = queue.shift() ?? { status: 200, body: {} }
    return { ok: next.status < 300, status: next.status, json: async () => next.body, headers: new Headers() }
  }))
}

describe('RestoreDialog', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows where the page goes, then restores confirming the address it showed', async () => {
    respond({ status: 200, body: plan() }, { status: 200, body: { restored: [{ kind: 'page', id: entry.id, address: '/about/team' }], published: false } })
    const { emitted, container } = render(RestoreDialog, { props: { siteId: SITE, entry }, global: { plugins: [i18n] } })

    await screen.findByText(en.trash.restore.under.replace('{parent}', 'About'))
    expect(screen.queryByRole('button', { name: en.trash.restore.andPublish })).toBeNull()

    await fireEvent.click(screen.getByRole('button', { name: en.trash.restore.confirm }))
    await waitFor(() => expect(emitted().restored?.[0]).toEqual([false]))

    const restore = vi.mocked(fetch).mock.calls[1]!
    expect(String(restore[0])).toContain(`/trash/pages/${entry.id}/restore`)
    expect(JSON.parse(String((restore[1] as RequestInit).body))).toMatchObject({ confirmedAddress: '/about/team', publish: false })

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'button-name', 'aria-valid-attr-value', 'aria-dialog-name'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('offers "Restore and publish" only when a version was live', async () => {
    respond({ status: 200, body: plan({ wasLive: true, canRestoreAndPublish: true }) })
    render(RestoreDialog, { props: { siteId: SITE, entry: { ...entry, wasLive: true } }, global: { plugins: [i18n] } })

    expect(await screen.findByRole('button', { name: en.trash.restore.andPublish })).toBeTruthy()
    expect(screen.getByText(en.trash.restore.wasLive)).toBeTruthy()
  })

  it('shows a taken address and its replacement before anything happens', async () => {
    respond({ status: 200, body: plan({ address: '/about/team-2', addressChanged: true }) })
    render(RestoreDialog, { props: { siteId: SITE, entry }, global: { plugins: [i18n] } })

    expect(await screen.findByText(en.trash.restore.addressChanged.replace('{old}', '/about/team').replace('{new}', '/about/team-2'))).toBeTruthy()
  })

  it('shows the new plan when it changed while the member was deciding, and restores nothing', async () => {
    respond(
      { status: 200, body: plan() },
      {
        status: 409,
        body: { title: 'Conflict', status: 409, errorCode: 'trash.plan_changed', plan: plan({ address: '/about/team-2', addressChanged: true }) },
      },
    )
    const { emitted } = render(RestoreDialog, { props: { siteId: SITE, entry }, global: { plugins: [i18n] } })

    await screen.findByText(en.trash.restore.under.replace('{parent}', 'About'))
    await fireEvent.click(screen.getByRole('button', { name: en.trash.restore.confirm }))

    expect(await screen.findByText(en.trash.restore.changed)).toBeTruthy()
    expect(screen.getByText(en.trash.restore.addressChanged.replace('{old}', '/about/team').replace('{new}', '/about/team-2'))).toBeTruthy()
    expect(emitted().restored).toBeUndefined()
  })
})
