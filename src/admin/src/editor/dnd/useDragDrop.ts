// Drag and drop for the palette, the canvas and the layer view (011 research ED-02).
//
// Native drag events, one shared state. A view reports where the pointer is over which node;
// this decides the drop target — before or after a sibling, or inside a Section or Container
// — by the same `canDrop` the schema defines, walking up to the nearest place that allows the
// block. Every drag action also has a keyboard path (keyboard.ts), so dragging is never the
// only way.

import { reactive } from 'vue'
import { insert, move } from '../ops'
import { contains, locate, parentOf } from '../ops/tree'
import { canDrop, takesChildren, type ContentNode, type DocumentKind } from '../schema/schema'
import { newBlock, type InsertContext } from '../blocks/registry'
import { refuse } from '../ops/types'
import type { Json } from '../schema/subsetValidator'

export type DragSource = { source: 'palette'; type: string; initialProps?: Record<string, unknown> } | { source: 'node'; id: string }

export type DropPosition = 'before' | 'after' | 'inside'

export interface DropTarget {
  parentId: string
  index: number
  allowed: boolean
  /** The node the indicator is drawn against, and how. */
  anchorId: string
  position: DropPosition
}

export const MIME = 'application/x-quickbuilder-block'

const state = reactive<{ dragging: DragSource | null; target: DropTarget | null }>({ dragging: null, target: null })

export const dragState = state as Readonly<typeof state>

export function startDrag(event: DragEvent, source: DragSource): void {
  state.dragging = source
  state.target = null
  if (event.dataTransfer) {
    // Firefox starts a drag only when some data is set.
    event.dataTransfer.setData(MIME, JSON.stringify(source))
    event.dataTransfer.setData('text/plain', '')
    event.dataTransfer.effectAllowed = source.source === 'palette' ? 'copy' : 'move'
  }
}

export function endDrag(): void {
  state.dragging = null
  state.target = null
}

/** The dragged block's type, whatever the source. */
function draggedType(root: ContentNode, source: DragSource): string | null {
  return source.source === 'palette' ? source.type : locate(root, source.id)?.node.type ?? null
}

/**
 * Where a drop over `overId` at `position` would put the block.
 *
 * Into a Section or Container (`inside`): there, or refused if it may not hold the block.
 * Before or after a block: beside it if its parent may hold the block, else beside the nearest
 * ancestor whose parent may — a Section dragged to the edge of a heading lands beside that
 * heading's Section.
 */
export function resolveTarget(
  root: ContentNode,
  source: DragSource,
  overId: string,
  position: DropPosition,
  kind: DocumentKind,
): DropTarget | null {
  const type = draggedType(root, source)
  const over = locate(root, overId)
  if (!type || !over) return null

  const intoItself = (parentId: string) => source.source === 'node' && contains(locate(root, source.id)!.node, parentId)

  if (position === 'inside' && takesChildren(over.node.type)) {
    // Dropping into the middle of a Section or Container means "into this one": if it may
    // not hold the block, the drop is refused rather than moved elsewhere (US1 #3).
    const allowed = canDrop(over.node.type, type, kind) && !intoItself(overId)
    return { parentId: overId, index: over.node.children.length, anchorId: overId, position, allowed }
  }

  let anchorId = overId
  let side: 'before' | 'after' = position === 'before' ? 'before' : 'after'
  for (;;) {
    const parent = parentOf(root, anchorId)
    if (!parent) break
    if (canDrop(parent.parent.type, type, kind) && !intoItself(parent.parent.id)) {
      return {
        parentId: parent.parent.id,
        index: side === 'before' ? parent.index : parent.index + 1,
        allowed: true,
        anchorId,
        position: side,
      }
    }
    // Not allowed at this level: try beside the parent itself, on the side of the drop.
    anchorId = parent.parent.id
    side = side === 'before' && parent.index === 0 ? 'before' : 'after'
  }

  return { parentId: overId, index: 0, allowed: false, anchorId: overId, position }
}

/** Records the target the pointer is over, for the indicator; returns it. */
export function setTarget(target: DropTarget | null): DropTarget | null {
  state.target = target
  return target
}

/** Applies the drop: an insert from the palette or a move of a node. */
export function dropResult(
  root: ContentNode, source: DragSource, target: DropTarget, kind: DocumentKind, t: (key: string) => string, context: InsertContext = { collectionIds: [] },
) {
  if (source.source === 'palette') {
    const block = newBlock(source.type, t, context, source.initialProps as Record<string, Json> | undefined)
    if ('refused' in block) return refuse(block.refused)
    return insert(root, target.parentId, target.index, source.type, kind, block.props, block.children)
  }
  return move(root, source.id, target.parentId, target.index, kind)
}

/** Reads the drag source of an event, from the shared state or, across windows, the data. */
export function sourceOf(event: DragEvent): DragSource | null {
  if (state.dragging) return state.dragging
  try {
    const raw = event.dataTransfer?.getData(MIME)
    return raw ? (JSON.parse(raw) as DragSource) : null
  } catch {
    return null
  }
}
