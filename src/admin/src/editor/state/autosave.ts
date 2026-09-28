// Autosave (011 FR-026 to FR-029, research ED-13).
//
//   saved ──change──▶ dirty ──2 s idle / Save now──▶ saving
//   saving ──ok──▶ saved (or dirty, if it changed meanwhile)
//   saving ──network, 429, 502–504──▶ retrying ──backoff / Retry now──▶ saving
//   saving ──409 out of date──▶ conflict   } each stops autosave and makes the
//   saving ──403──▶ forbidden              } editor read-only; the member's
//   saving ──404──▶ gone                   } version stays on screen
//   saving ──400 invalid document──▶ invalid, or before sending when the pre-check fails
//
// Whole-document saves, one in flight at a time, always against the version last loaded or
// saved, so a concurrent save by someone else is refused rather than overwritten.

import { ref, watch, type Ref } from 'vue'
import { ApiError } from '../../api/request'
import { validateDocument } from '../schema/documentValidator'
import { CURRENT_SCHEMA_VERSION } from '../schema/limits'
import type { ContentDocument, ContentNode } from '../schema/schema'
import type { useEditorStore } from './editorStore'

export type SaveState = 'saved' | 'dirty' | 'saving' | 'retrying' | 'conflict' | 'forbidden' | 'gone' | 'invalid'

export const STOPPED_STATES: ReadonlySet<SaveState> = new Set(['conflict', 'forbidden', 'gone', 'invalid'])

/** How a document reaches the server. */
export type SaveDraft = (document: ContentDocument, version: number) => Promise<{ version: number }>

export interface AutosaveOptions {
  debounceMs?: number
  /** Waits between retries of a temporary failure; the last repeats. */
  backoffMs?: readonly number[]
  /** Told when the store's document has been saved (the recovery copy is dropped then). */
  onSaved?: (saved: ContentNode) => void
}

export interface Autosave {
  state: Ref<SaveState>
  /** When the next retry happens, while retrying. */
  retryAt: Ref<number | null>
  /** The error code of the last refusal, when stopped. */
  reason: Ref<string | null>
  saveNow: () => Promise<void>
  retryNow: () => Promise<void>
  /** After Reload latest: back to saved, autosave running again. */
  reset: () => void
  dispose: () => void
}

const TEMPORARY_STATUSES = new Set([0, 408, 429, 500, 502, 503, 504])

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
    const readOnlyReason = next === 'conflict' || next === 'forbidden' || next === 'gone' || next === 'invalid' ? next : null
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

    const document: ContentDocument = { schemaVersion: CURRENT_SCHEMA_VERSION, root: sending }
    const check = validateDocument(document, store.kind)
    if (!check.valid) {
      // An editor bug, not the member's: nothing is sent (research ED-13).
      stop('invalid', check.rule, { rule: check.rule, pointer: check.pointer })
      return
    }

    inFlight = true
    changedWhileSaving = false
    state.value = 'saving'
    try {
      const saved = await save(document, store.baseVersion)
      store.markSaved(sending, saved.version)
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
      if (error.status === 409) return stop('conflict', code)
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
    state.value = 'saved'
  }

  const unwatch = watch(() => store.root, schedule, { flush: 'sync' })

  function dispose(): void {
    clearTimer()
    unwatch()
  }

  return { state, retryAt, reason, saveNow, retryNow, reset, dispose }
}
