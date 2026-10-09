// Undo and redo (011 FR-024, FR-025, research ED-12).
//
// Each committed change pushes the state from before it — the document and the selection —
// onto the undo stack. Documents are immutable and share every untouched subtree, so a step
// costs little however large the page. Continuous typing in one field merges into one step
// until a second passes without typing, a word ends, or anything else happens, so undo takes
// back words, not keystrokes. History lasts for one editing session of one document.

import { computed, ref, type ComputedRef, type InjectionKey, type ShallowRef } from 'vue'
import { locate } from '../ops/tree'
import type { Snapshot, useEditorStore } from './editorStore'

const LIMIT = 200
const PAUSE_MS = 1000
const WORD_END = /[\s.,;:!?)\]}"'…]$/

/** The page editor's one history, for blocks that offer undo and redo themselves (035 FR-009). */
export const pageHistoryKey: InjectionKey<ShallowRef<History | null>> = Symbol('pageHistory')

export interface History {
  undo: () => void
  redo: () => void
  clear: () => void
  canUndo: ComputedRef<boolean>
  canRedo: ComputedRef<boolean>
  dispose: () => void
}

export function createHistory(store: ReturnType<typeof useEditorStore>, options: { now?: () => number } = {}): History {
  const now = options.now ?? (() => Date.now())
  const undoStack = ref<Snapshot[]>([])
  const redoStack = ref<Snapshot[]>([])

  let lastKey: string | undefined
  let lastAt = 0
  let wordEnded = false

  /** The text a coalescing key ("nodeId:prop") names, now. */
  function textOf(key: string): string {
    const [id, prop] = key.split(':')
    const value = store.root ? locate(store.root, id)?.node.props[prop] : undefined
    if (typeof value === 'string') return value
    return value === undefined ? '' : JSON.stringify(value)
  }

  const unsubscribe = store.onChange((before, coalesceKey) => {
    const at = now()
    const merges = coalesceKey !== undefined && coalesceKey === lastKey && at - lastAt < PAUSE_MS && !wordEnded && undoStack.value.length > 0

    if (!merges) {
      undoStack.value.push(before)
      if (undoStack.value.length > LIMIT) undoStack.value.splice(0, undoStack.value.length - LIMIT)
    }
    redoStack.value = []

    lastKey = coalesceKey
    lastAt = at
    wordEnded = coalesceKey !== undefined && WORD_END.test(textOf(coalesceKey))
  })

  const current = (): Snapshot => ({ root: store.root!, selection: store.selection })

  function breakGroup(): void {
    lastKey = undefined
  }

  function undo(): void {
    if (store.readOnly || undoStack.value.length === 0) return
    redoStack.value.push(current())
    store.restore(undoStack.value.pop()!)
    breakGroup()
  }

  function redo(): void {
    if (store.readOnly || redoStack.value.length === 0) return
    undoStack.value.push(current())
    store.restore(redoStack.value.pop()!)
    breakGroup()
  }

  function clear(): void {
    undoStack.value = []
    redoStack.value = []
    breakGroup()
  }

  return {
    undo,
    redo,
    clear,
    canUndo: computed(() => undoStack.value.length > 0),
    canRedo: computed(() => redoStack.value.length > 0),
    dispose: unsubscribe,
  }
}
