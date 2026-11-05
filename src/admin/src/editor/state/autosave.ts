// Autosave (011 FR-026 to FR-029, research ED-13).
//
//   saved ──change──▶ dirty ──2 s idle / Save now──▶ saving
//   saving ──ok──▶ saved (or dirty, if it changed meanwhile)
//   saving ──network, 429, 502–504──▶ retrying ──backoff / Retry now──▶ saving
//   saving ──409 out of date──▶ conflict   } each stops autosave and makes the
//   saving ──409 lock held──▶ locked        } (023: another session holds the lock)
//   saving ──403──▶ forbidden              } editor read-only; the member's
//   saving ──404──▶ gone                   } version stays on screen
//   saving ──400 invalid document──▶ invalid, or before sending when the pre-check fails
//
// Whole-document saves, one in flight at a time, always against the version last loaded or
// saved, so a concurrent save by someone else is refused rather than overwritten.

import { ref, watch, type Ref } from 'vue'
import { ApiError } from '../../api/request'
import { conflictFrom, type ConflictDetails } from '../../editing/conflict'
import { validateDocument } from '../schema/documentValidator'
import { contentDocument } from '../schema/bindingDocumentRules'
import type { ContentDocument, ContentNode } from '../schema/schema'
import type { useEditorStore } from './editorStore'

export type SaveState = 'saved' | 'dirty' | 'saving' | 'retrying' | 'conflict' | 'locked' | 'forbidden' | 'gone' | 'invalid' | 'outdated'

export const STOPPED_STATES: ReadonlySet<SaveState> = new Set(['conflict', 'locked', 'forbidden', 'gone', 'invalid', 'outdated'])

/** How a document reaches the server. */
export type SaveDraft = (document: ContentDocument, version: number) => Promise<{ version: number; updatedAtUtc?: string }>

export interface AutosaveOptions {
  debounceMs?: number
  /** Waits between retries of a temporary failure; the last repeats. */
  backoffMs?: readonly number[]
  /** Told when the store's document has been saved (the recovery copy is dropped then). */
  onSaved?: (saved: ContentNode) => void
  /**
   * 023 research CE-04: asked before every save; false holds it back. The editor answers by
   * taking the editing lock, and reverts the change itself when the lock is someone else's.
   */
  beforeSave?: () => Promise<boolean>
  /** 023 research CE-07: a save was refused because another session holds the lock. */
  onLockLost?: () => void
}

export interface Autosave {
  state: Ref<SaveState>
  /** When the next retry happens, while retrying. */
  retryAt: Ref<number | null>
  /** The error code of the last refusal, when stopped. */
  reason: Ref<string | null>
  /** After a conflict: who saved the newer version and when (023 FR-003), when the server says. */
  conflict: Ref<ConflictDetails | null>
  saveNow: () => Promise<void>
  retryNow: () => Promise<void>
  /** After Reload latest: back to saved, autosave running again. */
  reset: () => void
  dispose: () => void
}

// 423: an import is applying to the site (020 FR-028). The changes stay on screen and are
// sent again after it; a document the import replaced is then refused as out of date.
const TEMPORARY_STATUSES = new Set([0, 408, 423, 429, 500, 502, 503, 504])

export function createAutosave(
  store: ReturnType<typeof useEditorStore>,
  save: SaveDraft,
  options: AutosaveOptions = {},
): Autosave {
  const debounceMs = options.debounceMs ?? 2000
  const backoffMs = options.backoffMs ?? [2000, 4000, 8000, 16000, 30000]

  const state = ref<SaveState>('saved')
  const retryAt = ref<number | null>(null)
  const reason = ref<string | null>(null)
  const conflict = ref<ConflictDetails | null>(null)

  let timer: ReturnType<typeof setTimeout> | null = null
  let attempt = 0
  let inFlight = false
  let changedWhileSaving = false

  const stopped = () => STOPPED_STATES.has(state.value)

  function clearTimer(): void {
    if (timer) clearTimeout(timer)
    timer = null
    retryAt.value = null
  }

  function schedule(): void {
    // Read-only means nothing on screen may reach the server: a recovery copy opened for
    // copying, say, is based on an older version and must never overwrite the newer one.
    if (stopped() || store.readOnly) return
    if (store.root === store.savedRoot) {
      if (!inFlight && state.value !== 'retrying') state.value = 'saved'
      return
    }
    if (inFlight) {
      changedWhileSaving = true
      return
    }
    if (state.value === 'retrying') return
    state.value = 'dirty'
    clearTimer()
    timer = setTimeout(() => void saveNow(), debounceMs)
  }

  function stop(next: SaveState, code: string | null, detail: { rule?: string; pointer?: string } = {}): void {
    clearTimer()
    state.value = next
    reason.value = code
    const readOnlyReason = next === 'conflict' || next === 'forbidden' || next === 'gone' || next === 'invalid' || next === 'outdated' ? next : null
    if (readOnlyReason) store.setReadOnly({ reason: readOnlyReason, ...detail })
  }

  async function saveNow(): Promise<void> {
    if (stopped() || store.readOnly || !store.root) return
    if (inFlight) {
      changedWhileSaving = true
      return
    }
    clearTimer()

    const sending = store.root
    if (sending === store.savedRoot) {
      state.value = 'saved'
      return
    }

    // 016 rule 13: a tree with bindings or lists states its binding format version.
    const document: ContentDocument = contentDocument(sending)
    const check = validateDocument(document, store.kind, store.validationContext)
    if (!check.valid) {
      // An editor bug, not the member's: nothing is sent (research ED-13).
      stop('invalid', check.rule, { rule: check.rule, pointer: check.pointer })
      return
    }

    inFlight = true
    changedWhileSaving = false
    if (options.beforeSave && !(await options.beforeSave())) {
      inFlight = false
      if (!stopped()) state.value = store.root === store.savedRoot ? 'saved' : 'dirty'
      return
    }
    state.value = 'saving'
    try {
      const saved = await save(document, store.baseVersion)
      store.markSaved(sending, saved.version, saved.updatedAtUtc)
      attempt = 0
      reason.value = null
      options.onSaved?.(sending)
      state.value = store.root === sending ? 'saved' : 'dirty'
    } catch (error) {
      handleFailure(error)
    } finally {
      inFlight = false
    }

    // handleFailure may have moved the state on; read it afresh.
    const after = state.value as SaveState
    if (!stopped() && after !== 'retrying' && (changedWhileSaving || store.root !== store.savedRoot)) {
      changedWhileSaving = false
      schedule()
    }
  }

  function handleFailure(error: unknown): void {
    if (error instanceof ApiError) {
      const code = error.errorCode ?? null
      // 035 FR-020: this tab's admin is older than the content it would save over.
      if (error.status === 409 && code === 'pages.admin_outdated') return stop('outdated', code)
      if (error.status === 409 && code === 'editing.lock_held') {
        clearTimer()
        state.value = 'locked'
        reason.value = code
        options.onLockLost?.()
        return
      }
      if (error.status === 409) {
        conflict.value = conflictFrom(error)
        return stop('conflict', code)
      }
      if (error.status === 403) return stop('forbidden', code)
      if (error.status === 404) return stop('gone', code)
      if (error.status === 400 || error.status === 413) {
        return stop('invalid', code, { rule: error.problem?.rule, pointer: error.problem?.pointer })
      }
      if (!TEMPORARY_STATUSES.has(error.status)) return stop('invalid', code)
    }

    // A network failure or a temporary server one: keep the changes and try again (FR-027).
    const wait = backoffMs[Math.min(attempt, backoffMs.length - 1)]
    attempt++
    state.value = 'retrying'
    reason.value = error instanceof ApiError ? error.errorCode ?? String(error.status) : 'network'
    retryAt.value = Date.now() + wait
    timer = setTimeout(() => void saveNow(), wait)
  }

  async function retryNow(): Promise<void> {
    if (state.value !== 'retrying') return
    clearTimer()
    await saveNow()
  }

  function reset(): void {
    clearTimer()
    attempt = 0
    reason.value = null
    conflict.value = null
    state.value = 'saved'
  }

  const unwatch = watch(() => store.root, schedule, { flush: 'sync' })

  function dispose(): void {
    clearTimer()
    unwatch()
  }

  return { state, retryAt, reason, conflict, saveNow, retryNow, reset, dispose }
}
