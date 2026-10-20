// What every block component needs: its properties with their schema defaults, token
// values as CSS, and in-place text editing that leaves the caret alone (011 FR-019).

import { computed, inject, onMounted, watch, type Ref } from 'vue'
import { STATIC_CANVAS } from '../canvas/staticCanvas'
import { setProp } from '../ops'
import { defaultOf, type ContentNode } from '../schema/schema'
import type { Json } from '../schema/subsetValidator'
import { useEditorStore } from '../state/editorStore'
import { tokenValue, tokenValueIfSet, type TokenKind } from '../tokens/tokenCss'
import { isBound } from '../bindings/canvasBindings'

export function useBlock(node: () => ContentNode) {
  const store = useEditorStore()

  /** A property's value, or its schema default when missing (data-model §1). */
  const prop = <T = Json>(name: string): T => (node().props[name] ?? defaultOf(node().type, name)) as T

  /** A token property as CSS, falling back to the property default when the token is gone. */
  const token = (name: string, kind: TokenKind, fallback = 'initial'): string =>
    tokenValue(node().props[name], kind, store.tokens, defaultOf(node().type, name), fallback)

  /**
   * A token property as CSS only when the block sets it and its token exists, else null: for
   * props without a schema default, whose absence lets a lower layer decide (024 NV-26).
   */
  const tokenIfSet = (name: string, kind: TokenKind): string | null => tokenValueIfSet(node().props[name], kind, store.tokens)

  // Content of a shared component shown on a page is never edited there (FR-038).
  const isStatic = inject(STATIC_CANVAS, false)
  const editable = computed(() => !store.readOnly && !isStatic)

  return { store, prop, token, tokenIfSet, editable }
}

/** The shortcuts that still reach the editor while text has the focus. */
export const isEditorShortcutInText = (event: KeyboardEvent): boolean =>
  (event.ctrlKey || event.metaKey) && ['z', 'y', 's'].includes(event.key.toLowerCase())

/**
 * Edits one plain-text property in place with `contenteditable="plaintext-only"`.
 *
 * The element's text is written only when the stored value changes from elsewhere (the
 * panel, undo), never while typing, so the caret never jumps. Each input is committed as an
 * operation, grouped with the typing before it for undo (research ED-12).
 */
export function useInlineText(
  element: Ref<HTMLElement | null>,
  node: () => ContentNode,
  prop: string,
  options: { singleLine?: boolean; minLength?: number } = {},
) {
  const store = useEditorStore()
  const value = () => (typeof node().props[prop] === 'string' ? (node().props[prop] as string) : '')
  /** 016: a bound property shows the item's value and is edited through its binding, not in place. */
  const bound = computed(() => isBound(node(), prop))

  const sync = () => {
    const el = element.value
    if (el && el.textContent !== value()) el.textContent = value()
  }

  onMounted(sync)
  watch(value, sync)

  function onInput(): void {
    const el = element.value
    if (!el) return
    if (bound.value) {
      sync()
      return
    }
    let text = el.textContent ?? ''
    if (options.singleLine && /[\r\n]/.test(text)) text = text.replace(/[\r\n]+/g, ' ')
    if (options.minLength && text.length < options.minLength) return

    const refused = store.apply(setProp(store.root!, node().id, prop, text), { coalesce: `${node().id}:${prop}` })
    // A refused value (too long, say) is put back to the last good one (FR-021).
    if (refused) sync()
  }

  function onKeydown(event: KeyboardEvent): void {
    if (options.singleLine && event.key === 'Enter') event.preventDefault()
    if (event.key === 'Escape') {
      event.preventDefault()
      ;(event.target as HTMLElement).blur()
    }
    // Keys typed into text belong to the text — except undo, redo and save, which the
    // editor handles everywhere (research ED-11). Copy and paste stay text operations here.
    if (!isEditorShortcutInText(event)) event.stopPropagation()
  }

  return { onInput, onKeydown, sync, bound }
}
