// Undo and redo for the menu editor (024 FR-015): every change keeps the document it replaced,
// so undoing is restoring it. Saving starts a new history, because the saved draft is the new
// starting point. The stacks are bounded so a long session does not grow without end.

import { computed, ref, type Ref } from 'vue'
import type { MenuDocument } from '../../editor/schema/menuValidator'

const LIMIT = 100

export function useMenuHistory(document: Ref<MenuDocument | null>) {
  const past = ref<MenuDocument[]>([])
  const future = ref<MenuDocument[]>([])

  /** Replaces the document, remembering the old one. */
  function change(next: MenuDocument): void {
    if (document.value) {
      past.value = [...past.value.slice(-(LIMIT - 1)), document.value]
    }
    future.value = []
    document.value = next
  }

  function undo(): boolean {
    const previous = past.value[past.value.length - 1]
    if (!previous || !document.value) return false
    past.value = past.value.slice(0, -1)
    future.value = [document.value, ...future.value]
    document.value = previous
    return true
  }

  function redo(): boolean {
    const next = future.value[0]
    if (!next || !document.value) return false
    future.value = future.value.slice(1)
    past.value = [...past.value, document.value]
    document.value = next
    return true
  }

  /** Forgets the history, as after a save or a reload. */
  function reset(): void {
    past.value = []
    future.value = []
  }

  return {
    change,
    undo,
    redo,
    reset,
    canUndo: computed(() => past.value.length > 0),
    canRedo: computed(() => future.value.length > 0),
  }
}
