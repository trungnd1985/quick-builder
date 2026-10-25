import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../src/api/request'
import type { Presence } from '../../src/api/editing'

// 023 contracts/admin-editing.md §1 and §2, research CE-03 to CE-05, CE-13.

const api = vi.hoisted(() => ({
  open: vi.fn(),
  heartbeat: vi.fn(),
  claim: vi.fn(),
  takeOver: vi.fn(),
  move: vi.fn(),
  release: vi.fn(async () => undefined),
  releaseAll: vi.fn(),
  holders: vi.fn(),
}))
vi.mock('../../src/api/editing', () => ({
  editingApi: api,
  editingHeaders: (id: string | null) => (id ? { 'X-QB-Editing-Session': id } : {}),
}))

const { useEditingSession, HEARTBEAT_MS } = await import('../../src/editing/useEditingSession')

const anna = { id: 'u-anna', displayName: 'Anna Tran' }
const target = () => ({ siteId: 'site', kind: 'page' as const, documentId: 'page' })

function presence(extra: Partial<Presence> = {}): Presence {
  return { sessionId: 's-mine', serverNow: '2026-10-25T10:00:00Z', role: 'viewer', access: 'edit', lock: null, lostLock: null, others: [], ...extra }
}

const heldBy = (extra: Partial<NonNullable<Presence['lock']>> = {}): Presence['lock'] => ({
  holderSessionId: 's-anna', holder: anna, since: '2026-10-25T09:50:00Z', stale: false, inactiveSeconds: 10, isYou: false, isYourOtherSession: false, ...extra,
})

const refusal = (status: number, errorCode: string) => new ApiError('refused', status, { title: 'x', status, errorCode })

describe('useEditingSession', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    for (const fn of Object.values(api)) fn.mockReset()
    api.release.mockResolvedValue(undefined)
  })
  afterEach(() => vi.useRealTimers())

  it('opens as a viewer and beats every ten seconds, reporting activity only after a change', async () => {
    api.open.mockResolvedValue(presence())
    api.heartbeat.mockResolvedValue(presence())
    const session = useEditingSession(target)

    await session.start()
    expect(api.open).toHaveBeenCalledWith('site', 'page', 'page', undefined)
    expect(session.status.value).toBe('editing')
    expect(session.headers()).toEqual({ 'X-QB-Editing-Session': 's-mine' })

    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS)
    expect(api.heartbeat).toHaveBeenLastCalledWith('site', 's-mine', false)

    session.noteActivity()
    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS)
    expect(api.heartbeat).toHaveBeenLastCalledWith('site', 's-mine', true)
    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS)
    expect(api.heartbeat).toHaveBeenLastCalledWith('site', 's-mine', false)
    session.stop()
  })

  it('beats at once when the tab becomes visible again', async () => {
    api.open.mockResolvedValue(presence())
    api.heartbeat.mockResolvedValue(presence())
    const session = useEditingSession(target)
    await session.start()

    document.dispatchEvent(new Event('visibilitychange'))
    await vi.advanceTimersByTimeAsync(0)
    expect(api.heartbeat).toHaveBeenCalledTimes(1)
    session.stop()
  })

  it('claims once on the first change, and a refused claim leaves the document locked', async () => {
    api.open.mockResolvedValue(presence())
    api.claim.mockRejectedValueOnce(refusal(409, 'editing.lock_held'))
    api.heartbeat.mockResolvedValue(presence({ lock: heldBy() }))
    const session = useEditingSession(target)
    await session.start()

    expect(await session.ensureLock()).toBe(false)
    expect(api.claim).toHaveBeenCalledTimes(1)
    expect(session.status.value).toBe('locked')
    expect(session.mayEdit.value).toBe(false)

    // While locked, no further claim is even tried.
    expect(await session.ensureLock()).toBe(false)
    expect(api.claim).toHaveBeenCalledTimes(1)
    session.stop()
  })

  it('holds the lock after a successful claim, and does not claim again', async () => {
    api.open.mockResolvedValue(presence())
    api.claim.mockResolvedValue(presence({ role: 'holder', lock: heldBy({ isYou: true, holderSessionId: 's-mine' }) }))
    const session = useEditingSession(target)
    await session.start()

    expect(await session.ensureLock()).toBe(true)
    expect(await session.ensureLock()).toBe(true)
    expect(api.claim).toHaveBeenCalledTimes(1)
    expect(session.status.value).toBe('editing')
    session.stop()
  })

  it('shows who holds it, stale or not, and says when it became free', async () => {
    api.open.mockResolvedValue(presence({ lock: heldBy() }))
    api.heartbeat
      .mockResolvedValueOnce(presence({ lock: heldBy({ stale: true, inactiveSeconds: 1900 }) }))
      .mockResolvedValueOnce(presence())
    const session = useEditingSession(target)
    await session.start()
    expect(session.status.value).toBe('locked')

    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS)
    expect(session.status.value).toBe('locked-stale')

    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS)
    expect(session.status.value).toBe('free')
    session.acknowledge()
    expect(session.status.value).toBe('editing')
    session.stop()
  })

  it('says when its own lock was taken over or moved, until the member reloads', async () => {
    api.open.mockResolvedValue(presence({ role: 'holder', lock: heldBy({ isYou: true }) }))
    api.heartbeat.mockResolvedValue(presence({ lock: heldBy(), lostLock: { at: '2026-10-25T10:30:00Z', by: anna, reason: 'takeover' } }))
    const session = useEditingSession(target)
    await session.start()

    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS)
    expect(session.status.value).toBe('taken-over')
    session.acknowledge()
    expect(session.status.value).toBe('locked')
    session.stop()
  })

  it('treats a save refused as lock_held as a lost lock', async () => {
    api.open.mockResolvedValue(presence({ role: 'holder', lock: heldBy({ isYou: true }) }))
    api.heartbeat.mockResolvedValue(presence({ lock: heldBy() }))
    const session = useEditingSession(target)
    await session.start()

    session.lockLost()
    await vi.advanceTimersByTimeAsync(0)
    expect(session.status.value).toBe('taken-over')
    expect(await session.ensureLock()).toBe(false)
    session.stop()
  })

  it('offers its own other session, and moves the lock here', async () => {
    api.open.mockResolvedValue(presence({ lock: heldBy({ isYourOtherSession: true, holder: { id: 'u-me', displayName: 'Me' } }) }))
    api.move.mockResolvedValue(presence({ role: 'holder', lock: heldBy({ isYou: true, holderSessionId: 's-mine' }) }))
    const session = useEditingSession(target)
    await session.start()

    expect(session.status.value).toBe('locked-self')
    expect(await session.moveHere()).toBe(true)
    expect(api.move).toHaveBeenCalledWith('site', 's-mine', 's-anna')
    expect(session.status.value).toBe('editing')
    session.stop()
  })

  it('reopens a session that ended, and releases on leave and on pagehide', async () => {
    api.open.mockResolvedValueOnce(presence()).mockResolvedValueOnce(presence({ sessionId: 's-new' }))
    api.heartbeat.mockRejectedValueOnce(refusal(404, 'editing.session_gone'))
    const session = useEditingSession(target)
    await session.start()

    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS)
    expect(api.open).toHaveBeenCalledTimes(2)
    expect(session.sessionId.value).toBe('s-new')

    window.dispatchEvent(new Event('pagehide'))
    expect(api.release).toHaveBeenLastCalledWith('site', 's-new', true)

    session.stop()
    expect(api.release).toHaveBeenLastCalledWith('site', 's-new')
    await vi.advanceTimersByTimeAsync(HEARTBEAT_MS * 3)
    expect(api.heartbeat).toHaveBeenCalledTimes(1)
  })

  it('is view-only without the edit capability', async () => {
    api.open.mockResolvedValue(presence({ access: 'view-only' }))
    const session = useEditingSession(target)
    await session.start()

    expect(session.status.value).toBe('view-only')
    expect(await session.ensureLock()).toBe(false)
    expect(api.claim).not.toHaveBeenCalled()
    session.stop()
  })
})
