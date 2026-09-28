// The editor's keyboard model (011 FR-008, research ED-11). Bound on the editor's root, not
// the window, so the rest of the admin keeps its keys. Keys typed into text or a form field
// never arrive here, apart from undo, redo and save (useBlock.ts).
//
//   ↑ ↓            previous / next block, in reading order
//   ← →            parent / first child
//   Enter          edit the block's text in place
//   Escape         clear the selection
//   Alt+↑ Alt+↓    move among siblings
//   Alt+→          into the previous sibling, when it takes the block
//   Alt+←          out, to just after the parent
//   Ctrl/⌘+D       duplicate          Delete, Backspace   remove
//   Ctrl/⌘+C / V   copy / paste       Ctrl/⌘+S            save now
//   Ctrl/⌘+Z       undo               Ctrl/⌘+Shift+Z, Ctrl/⌘+Y   redo

import { duplicate, move, paste, remove } from '../ops'
import { placementFor } from '../ops/placement'
import { locate, parentOf, walkNodes } from '../ops/tree'
import { canDrop, type ContentNode } from '../schema/schema'
import { definitionOf } from '../blocks/registry'
import { copyBlocks, readBlocks } from './clipboard'
import type { useEditorStore } from './editorStore'

export interface KeyboardContext {
  store: ReturnType<typeof useEditorStore>
  userId: string
  /** Says a result aloud through the live region. */
  announce: (message: string) => void
  /** Reports a refused operation by its code. */
  refused: (code: string) => void
  saveNow: () => void
  undo?: () => void
  redo?: () => void
  t: (key: string, values?: Record<string, unknown>) => string
}

const FORM_FIELDS = new Set(['INPUT', 'TEXTAREA', 'SELECT'])

/** Whether a key event comes from somewhere that types text rather than commands. */
export const isTyping = (target: EventTarget | null): boolean =>
  target instanceof HTMLElement && (FORM_FIELDS.has(target.tagName) || target.isContentEditable)

/** The node's name as the member sees it: its layer label, else its type's name. */
export function nodeName(node: ContentNode, t: KeyboardContext['t']): string {
  if (typeof node.props.layerLabel === 'string') return node.props.layerLabel
  const definition = definitionOf(node.type)
  return definition ? t(definition.labelKey) : node.type
}

/** Handles one key; returns true when it was an editor command. */
export function handleEditorKey(event: KeyboardEvent, context: KeyboardContext): boolean {
  const { store, t } = context
  const root = store.root
  if (!root) return false

  const mod = event.ctrlKey || event.metaKey
  const key = event.key.toLowerCase()

  // Undo, redo and save work even while typing.
  if (mod && key === 's') return run(event, () => context.saveNow())
  if (mod && key === 'z' && !event.shiftKey && context.undo) return run(event, () => context.undo!())
  if (mod && ((key === 'z' && event.shiftKey) || key === 'y') && context.redo) return run(event, () => context.redo!())

  if (isTyping(event.target)) return false

  const selected = store.selection ? locate(root, store.selection) : null

  if (mod && key === 'c') {
    if (!selected || selected.path.length === 0) return false
    return run(event, () => {
      copyBlocks(context.userId, store.siteId, [selected.node])
      context.announce(t('editor.announce.copied', { name: nodeName(selected.node, t) }))
    })
  }

  if (mod && key === 'v') {
    return run(event, () => {
      const nodes = readBlocks(context.userId, store.siteId)
      if (!nodes || nodes.length === 0) return context.refused('clipboard_empty')
      const at = placementFor(root, store.selection, nodes[0].type, store.kind)
      if (!at) return context.refused('nesting_not_allowed')
      const refused = store.apply(paste(root, at.parentId, at.index, nodes, store.kind))
      if (refused) context.refused(refused.code)
      else context.announce(t('editor.announce.pasted'))
    })
  }

  if (mod && key === 'd') {
    if (!selected) return false
    return run(event, () => {
      const refused = store.apply(duplicate(root, selected.node.id, store.kind))
      if (refused) context.refused(refused.code)
      else context.announce(t('editor.announce.duplicated', { name: nodeName(selected.node, t) }))
    })
  }

  if ((event.key === 'Delete' || event.key === 'Backspace') && selected && !mod) {
    return run(event, () => {
      const refused = store.apply(remove(root, selected.node.id))
      if (refused) context.refused(refused.code)
      else context.announce(t('editor.announce.removed', { name: nodeName(selected.node, t) }))
    })
  }

  if (event.altKey && selected && selected.path.length > 0) {
    const place = parentOf(root, selected.node.id)!
    let target: { parentId: string; index: number } | null = null

    if (event.key === 'ArrowUp' && place.index > 0) target = { parentId: place.parent.id, index: place.index - 1 }
    if (event.key === 'ArrowDown' && place.index < place.parent.children.length - 1) target = { parentId: place.parent.id, index: place.index + 2 }
    if (event.key === 'ArrowRight' && place.index > 0) {
      const previous = place.parent.children[place.index - 1]
      if (canDrop(previous.type, selected.node.type, store.kind)) target = { parentId: previous.id, index: previous.children.length }
    }
    if (event.key === 'ArrowLeft') {
      const outer = parentOf(root, place.parent.id)
      if (outer) target = { parentId: outer.parent.id, index: outer.index + 1 }
    }

    if (event.key.startsWith('Arrow')) {
      return run(event, () => {
        if (!target) return context.refused('nesting_not_allowed')
        const refused = store.apply(move(root, selected.node.id, target.parentId, target.index, store.kind))
        if (refused) return context.refused(refused.code)
        announcePosition(context, selected.node.id)
      })
    }
  }

  if (!event.altKey && !mod && event.key.startsWith('Arrow')) {
    const order = [...walkNodes(root)].slice(1)
    if (order.length === 0) return false
    const index = selected ? order.findIndex((n) => n.id === selected.node.id) : -1
    let next: ContentNode | undefined

    if (event.key === 'ArrowDown') next = order[Math.min(index + 1, order.length - 1)]
    if (event.key === 'ArrowUp') next = index < 0 ? order[0] : order[Math.max(index - 1, 0)]
    if (event.key === 'ArrowLeft' && selected) next = selected.ancestors.length > 1 ? selected.ancestors[selected.ancestors.length - 1] : undefined
    if (event.key === 'ArrowRight' && selected) next = selected.node.children[0]

    if (!next) return false
    const target = next
    return run(event, () => {
      store.select(target.id)
      context.announce(nodeName(target, t))
    })
  }

  if (event.key === 'Enter' && selected) {
    return run(event, () => {
      // Node ids are [A-Za-z0-9_-], so they need no escaping in a selector.
      const scope = `[data-node-id="${selected.node.id}"]`
      const editable = document.querySelector<HTMLElement>(`${scope} [contenteditable="plaintext-only"], ${scope} [contenteditable="true"]`)
      editable?.focus()
    })
  }

  if (event.key === 'Escape' && store.selection) return run(event, () => store.select(null))

  return false
}

function run(event: KeyboardEvent, action: () => void): true {
  event.preventDefault()
  event.stopPropagation()
  action()
  return true
}

/** "Heading moved into Section 2, position 1." */
function announcePosition(context: KeyboardContext, id: string): void {
  const root = context.store.root!
  const place = parentOf(root, id)
  if (!place) return
  const node = locate(root, id)!.node
  context.announce(
    context.t('editor.announce.moved', {
      name: nodeName(node, context.t),
      parent: place.parent.type === 'root' ? context.t('editor.announce.page') : nodeName(place.parent, context.t),
      position: place.index + 1,
    }),
  )
}
