// One open editor's editing session (023 contracts/admin-editing.md §1 and §2).
//
//   open ─▶ viewer ──first change, claim──▶ holder
//   viewer ──someone else holds──▶ locked / locked-stale / locked-self
//   locked… ──holder leaves or lapses──▶ free ──Edit──▶ (reload if changed) holder
//   holder ──taken over / moved / save refused as lock_held──▶ taken-over / moved
//
// A heartbeat every 10 seconds, and at once when the tab becomes visible, keeps the session
// alive and brings the presence up to date (research CE-03). Leaving releases it: on route
// leave and unmount, and on `pagehide` with a keepalive request (CE-13). All times shown come
// from the server.

import { computed, ref, shallowRef } from 'vue'
import { editingApi, editingHeaders, type EditingKind, type Presence } from '../api/editing'
import { ApiError } from '../api/request'

export const HEARTBEAT_MS = 10_000

/** Why this session may not change the document right now; `editing` when it may. */
export type EditingStatus = 'editing' | 'locked' | 'locked-stale' | 'locked-self' | 'taken-over' | 'moved' | 'view-only' | 'free'

export interface EditingTarget {
  siteId: string
  kind: EditingKind
  documentId: string
  collectionId?: string
}

export function useEditingSession(target: () => EditingTarget) {
  const presence = shallowRef<Presence | null>(null)
  /** Seen locked by someone else since the last time this session could edit. */
  const wasBlocked = ref(false)
  /** A save was refused as `editing.lock_held`: someone else holds the lock now. */
  const refusedAsLockHeld = ref(false)
  /** The loss of the lock the member has already dealt with (reloaded after it). */
  const acknowledgedLoss = ref<string | null>(null)
  const stopped = ref(false)

  let timer: ReturnType<typeof setInterval> | null = null
  let active = false
  let opening: Promise<void> | null = null
  let claiming: Promise<boolean> | null = null
  /** Moves on with every start and stop, so a session that opens after its editor moved on is let go. */
  let generation = 0

  const sessionId = computed(() => presence.value?.sessionId ?? null)

  const status = computed<EditingStatus>(() => {
    const now = presence.value
    if (!now) return 'editing'
    if (now.access === 'view-only') return 'view-only'
    if (now.role === 'holder') return 'editing'
    if (now.lostLock && now.lostLock.at !== acknowledgedLoss.value) return now.lostLock.reason === 'moved' ? 'moved' : 'taken-over'
    if (refusedAsLockHeld.value) return 'taken-over'
    if (now.lock) return now.lock.isYourOtherSession ? 'locked-self' : now.lock.stale ? 'locked-stale' : 'locked'
    return wasBlocked.value ? 'free' : 'editing'
  })

  const mayEdit = computed(() => status.value === 'editing')

  function take(next: Presence): void {
    presence.value = next
    if (next.role === 'holder') {
      wasBlocked.value = false
      refusedAsLockHeld.value = false
    } else if (next.lock && !next.lock.isYou) {
      wasBlocked.value = true
    }
  }

  async function beat(): Promise<void> {
    const current = presence.value
    if (!current || stopped.value) return
    const wasActive = active
    active = false
    try {
      take(await editingApi.heartbeat(target().siteId, current.sessionId, wasActive))
    } catch (error) {
      if (error instanceof ApiError && error.errorCode === 'editing.session_gone') {
        // Expired (a sleeping laptop, a restart) or the document went: open a new session.
        presence.value = null
        await openSession()
      } else if (error instanceof ApiError && error.status === 401) {
        stop()
      } else {
        // A network or server hiccup: try again on the next beat.
        active = active || wasActive
      }
    }
  }

  function onVisibility(): void {
    if (document.visibilityState === 'visible') void beat()
  }

  function onPageHide(): void {
    const current = presence.value
    if (current) void editingApi.release(target().siteId, current.sessionId, true).catch(() => undefined)
  }

  async function openSession(): Promise<void> {
    const mine = generation
    const { siteId, kind, documentId, collectionId } = target()
    try {
      const opened = await editingApi.open(siteId, kind, documentId, collectionId)
      if (mine === generation && !stopped.value) take(opened)
      else void editingApi.release(siteId, opened.sessionId).catch(() => undefined)
    } catch {
      // Without a session the editor still works; saves are protected by version (FR-026).
      if (mine === generation) presence.value = null
    }
  }

  /** Opens the session and starts the heartbeat. */
  async function start(): Promise<void> {
    const mine = ++generation
    stopped.value = false
    presence.value = null
    opening = openSession()
    await opening
    // Left, or started again, before the session even opened.
    if (stopped.value || mine !== generation) return
    timer = setInterval(() => void beat(), HEARTBEAT_MS)
    document.addEventListener('visibilitychange', onVisibility)
    window.addEventListener('pagehide', onPageHide)
  }

  /** Leaves the document: releases the session and stops the heartbeat (FR-015). */
  function stop(): void {
    generation++
    stopped.value = true
    if (timer) clearInterval(timer)
    timer = null
    document.removeEventListener('visibilitychange', onVisibility)
    window.removeEventListener('pagehide', onPageHide)
    const current = presence.value
    if (current) void editingApi.release(target().siteId, current.sessionId).catch(() => undefined)
  }

  /** A change or editing action happened: it counts for the stale period (research CE-05). */
  function noteActivity(): void {
    active = true
  }

  /**
   * Takes the lock if this session may: true when it holds it afterwards (research CE-04).
   * A network failure answers true: the save that follows is protected by version (FR-026).
   */
  function ensureLock(): Promise<boolean> {
    // The first change and the save after it both ask; one claim answers both.
    claiming ??= claim().finally(() => (claiming = null))
    return claiming
  }

  async function claim(): Promise<boolean> {
    if (opening) await opening
    const current = presence.value
    if (!current) return true
    if (current.role === 'holder') return true
    if (!mayEdit.value) return false
    try {
      take(await editingApi.claim(target().siteId, current.sessionId))
      return true
    } catch (error) {
      if (error instanceof ApiError && error.errorCode === 'editing.lock_held') {
        wasBlocked.value = true
        await beat()
        return false
      }
      if (error instanceof ApiError && (error.status === 403 || error.status === 404)) return false
      return true
    }
  }

  /** "Edit here instead": moves this member's own lock to this session (FR-014). */
  async function moveHere(): Promise<boolean> {
    const current = presence.value
    if (!current?.lock) return false
    try {
      take(await editingApi.move(target().siteId, current.sessionId, current.lock.holderSessionId))
      return true
    } catch {
      await beat()
      return false
    }
  }

  /** Takes over a stale lock (FR-017). False when it is no longer stale, or held by someone else now. */
  async function takeOver(): Promise<boolean> {
    const current = presence.value
    if (!current?.lock) return false
    try {
      take(await editingApi.takeOver(target().siteId, current.sessionId, current.lock.holderSessionId))
      return true
    } catch {
      await beat()
      return false
    }
  }

  /** A save was refused because another session holds the lock (research CE-07). */
  function lockLost(): void {
    refusedAsLockHeld.value = true
    void beat()
  }

  /** The member reloaded after a loss of the lock, or after the document became free. */
  function acknowledge(): void {
    acknowledgedLoss.value = presence.value?.lostLock?.at ?? acknowledgedLoss.value
    refusedAsLockHeld.value = false
    wasBlocked.value = false
  }

  /** The header the editor's draft saves carry. */
  const headers = (): Record<string, string> => editingHeaders(sessionId.value)

  return { presence, status, mayEdit, sessionId, start, stop, beat, noteActivity, ensureLock, moveHere, takeOver, lockLost, acknowledge, headers }
}

export type EditingSession = ReturnType<typeof useEditingSession>

/** The statuses that come from locks, which the presence banner explains rather than 011's banner. */
export const LOCK_STATUSES: ReadonlySet<EditingStatus> = new Set(['locked', 'locked-stale', 'locked-self', 'taken-over', 'moved', 'view-only', 'free'])
