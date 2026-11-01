// Editing sessions, presence and soft locks (023 contracts/editing-api.yaml).

import { API_ROOT, REQUEST_HEADER, request } from './request'

export type EditingKind = 'page' | 'component' | 'item'

/** The header every draft save of an open editor carries (research CE-07). */
export const EDITING_SESSION_HEADER = 'X-QB-Editing-Session'

export interface EditingMember {
  id: string
  displayName: string
}

export interface EditingLock {
  holderSessionId: string
  holder: EditingMember
  since: string
  stale: boolean
  inactiveSeconds: number
  isYou: boolean
  isYourOtherSession: boolean
}

export interface Presence {
  sessionId: string
  serverNow: string
  role: 'viewer' | 'holder'
  access: 'edit' | 'view-only'
  lock: EditingLock | null
  lostLock: { at: string; by: EditingMember; reason: 'takeover' | 'moved' } | null
  others: { member: EditingMember; since: string; holdsLock: boolean }[]
}

export interface Holders {
  serverNow: string
  holders: { documentId: string; holder: EditingMember; since: string; stale: boolean }[]
}

const sessions = (siteId: string) => `/editor/sites/${siteId}/editing-sessions`
const session = (siteId: string, sessionId: string) => `${sessions(siteId)}/${sessionId}`

/** The sessions this tab has open, so signing out can release them all (FR-015). */
const open = new Map<string, string>()

export const editingApi = {
  open: async (siteId: string, kind: EditingKind, documentId: string, collectionId?: string): Promise<Presence> => {
    const presence = await request<Presence>(sessions(siteId), {
      method: 'POST',
      body: JSON.stringify({ kind, documentId, collectionId: collectionId ?? null }),
    })
    open.set(presence.sessionId, siteId)
    return presence
  },

  heartbeat: (siteId: string, sessionId: string, active: boolean): Promise<Presence> =>
    request<Presence>(`${session(siteId, sessionId)}/heartbeat`, { method: 'POST', body: JSON.stringify({ active }) }),

  claim: (siteId: string, sessionId: string): Promise<Presence> =>
    request<Presence>(`${session(siteId, sessionId)}/claim`, { method: 'POST' }),

  takeOver: (siteId: string, sessionId: string, holderSessionId: string): Promise<Presence> =>
    request<Presence>(`${session(siteId, sessionId)}/takeover`, { method: 'POST', body: JSON.stringify({ holderSessionId }) }),

  move: (siteId: string, sessionId: string, holderSessionId: string): Promise<Presence> =>
    request<Presence>(`${session(siteId, sessionId)}/move`, { method: 'POST', body: JSON.stringify({ holderSessionId }) }),

  /**
   * Leaves the document. With `keepalive`, the request outlives the page, for `pagehide`;
   * unlike `sendBeacon`, it carries the request header the admin API requires (research CE-13).
   */
  release: async (siteId: string, sessionId: string, keepalive = false): Promise<void> => {
    open.delete(sessionId)
    if (!keepalive) {
      await request<void>(session(siteId, sessionId), { method: 'DELETE' })
      return
    }
    await fetch(`${API_ROOT}${session(siteId, sessionId)}`, {
      method: 'DELETE',
      credentials: 'same-origin',
      keepalive: true,
      headers: { [REQUEST_HEADER]: '1' },
    })
  },

  /** Releases every session this tab opened; never fails, so it never blocks signing out. */
  releaseAll: async (): Promise<void> => {
    const all = [...open.entries()]
    open.clear()
    await Promise.all(all.map(([sessionId, siteId]) => request<void>(session(siteId, sessionId), { method: 'DELETE' }).catch(() => undefined)))
  },

  holders: (siteId: string, kind: EditingKind, collectionId?: string): Promise<Holders> =>
    request<Holders>(`/editor/sites/${siteId}/editing?kind=${kind}${collectionId ? `&collectionId=${encodeURIComponent(collectionId)}` : ''}`),
}

/** The header for a draft save from this session, or none. */
export const editingHeaders = (sessionId: string | null | undefined): Record<string, string> =>
  sessionId ? { [EDITING_SESSION_HEADER]: sessionId } : {}
