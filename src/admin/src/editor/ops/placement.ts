// Where a block goes when it is added without a pointer: from the palette with Enter, or
// pasted (research ED-11). Inside the selected block if it takes that type, else right after
// it, else after the nearest ancestor where the type is allowed; with nothing selected, at the
// end of the page — or of its last Section, for content blocks.

import { canDrop, type ContentNode, type DocumentKind } from '../schema/schema'
import { locate, parentOf } from './tree'

export interface Placement {
  parentId: string
  index: number
}

export function placementFor(root: ContentNode, selection: string | null, type: string, kind: DocumentKind): Placement | null {
  if (selection) {
    const selected = locate(root, selection)
    if (selected) {
      if (canDrop(selected.node.type, type, kind)) {
        return { parentId: selected.node.id, index: selected.node.children.length }
      }

      let anchor = selected.node.id
      for (let parent = parentOf(root, anchor); parent; parent = parentOf(root, anchor)) {
        if (canDrop(parent.parent.type, type, kind)) return { parentId: parent.parent.id, index: parent.index + 1 }
        anchor = parent.parent.id
      }
    }
  }

  if (canDrop(root.type, type, kind)) return { parentId: root.id, index: root.children.length }

  const lastSection = [...root.children].reverse().find((child) => canDrop(child.type, type, kind))
  return lastSection ? { parentId: lastSection.id, index: lastSection.children.length } : null
}
