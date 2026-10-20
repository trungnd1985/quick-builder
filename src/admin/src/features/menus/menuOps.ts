// Pure operations on a menu document's entry tree (024 research NV-17).
//
// Every change the menu editor makes — adding, editing, removing, and moving by drag or by
// key — goes through here, so the three-level rule (FR-014) and "never inside itself" are
// decided once, and undo is just keeping the previous document. Nothing here mutates its input.

import { MAX_MENU_LEVELS } from '../../editor/schema/limits'
import type { MenuDocument, MenuEntry } from '../../editor/schema/menuValidator'

export interface Located {
  entry: MenuEntry
  /** The parent entry, or null at the top level. */
  parent: MenuEntry | null
  /** The list the entry sits in: the document's entries or its parent's children. */
  siblings: MenuEntry[]
  index: number
  /** 1 at the top level. */
  level: number
}

export type DropPosition = 'before' | 'after' | 'inside'

// Through JSON, not structuredClone: the editor passes Vue reactive proxies, which cannot be
// structurally cloned, and a menu document is plain JSON anyway.
const clone = (document: MenuDocument): MenuDocument => JSON.parse(JSON.stringify(document)) as MenuDocument

/** The entry with <paramref name="id"/>, where it sits, or null. */
export function locate(document: MenuDocument, id: string): Located | null {
  const walk = (siblings: MenuEntry[], parent: MenuEntry | null, level: number): Located | null => {
    for (let index = 0; index < siblings.length; index++) {
      const entry = siblings[index]
      if (entry.id === id) return { entry, parent, siblings, index, level }
      const found = walk(entry.children, entry, level + 1)
      if (found) return found
    }
    return null
  }
  return walk(document.entries, null, 1)
}

/** Every entry, depth first, with its level: the order the tree shows them. */
export function flatten(document: MenuDocument): { entry: MenuEntry; level: number; parentId: string | null }[] {
  const out: { entry: MenuEntry; level: number; parentId: string | null }[] = []
  const walk = (entries: MenuEntry[], level: number, parentId: string | null) => {
    for (const entry of entries) {
      out.push({ entry, level, parentId })
      walk(entry.children, level + 1, entry.id)
    }
  }
  walk(document.entries, 1, null)
  return out
}

/** How many levels an entry and its descendants take: 1 for an entry without children. */
export function height(entry: MenuEntry): number {
  return 1 + Math.max(0, ...entry.children.map(height))
}

/** A fresh id, unique in the document. */
export function newEntryId(document: MenuDocument): string {
  const taken = new Set(flatten(document).map((e) => e.entry.id))
  for (;;) {
    const id = `e${Math.random().toString(36).slice(2, 10)}`
    if (!taken.has(id)) return id
  }
}

/** Whether an entry may get a child: it sits above the last level. */
export function canAddChild(document: MenuDocument, id: string): boolean {
  const found = locate(document, id)
  return found !== null && found.level < MAX_MENU_LEVELS
}

/** Adds <paramref name="entry"/> as the last entry at the top, or as the last child of <paramref name="parentId"/>. */
export function addEntry(document: MenuDocument, entry: MenuEntry, parentId: string | null = null): MenuDocument {
  const next = clone(document)
  if (parentId === null) {
    next.entries.push(entry)
    return next
  }
  const parent = locate(next, parentId)
  if (!parent || parent.level >= MAX_MENU_LEVELS) throw new Error('This entry cannot take children.')
  parent.entry.children.push(entry)
  return next
}

/** Replaces an entry's own fields; its children stay. */
export function updateEntry(document: MenuDocument, id: string, fields: Omit<MenuEntry, 'id' | 'children'>): MenuDocument {
  const next = clone(document)
  const found = locate(next, id)
  if (!found) return document
  const { children } = found.entry
  found.siblings[found.index] = { id, ...fields, children }
  return next
}

/** Removes an entry with its children. */
export function removeEntry(document: MenuDocument, id: string): MenuDocument {
  const next = clone(document)
  const found = locate(next, id)
  if (!found) return document
  found.siblings.splice(found.index, 1)
  return next
}

/** Whether <paramref name="id"/> is <paramref name="ancestorId"/> or lies under it. */
function isWithin(document: MenuDocument, id: string, ancestorId: string): boolean {
  const ancestor = locate(document, ancestorId)
  if (!ancestor) return false
  const walk = (entry: MenuEntry): boolean => entry.id === id || entry.children.some(walk)
  return walk(ancestor.entry)
}

/**
 * Whether moving <paramref name="id"/> next to or into <paramref name="targetId"/> is allowed
 * (FR-014): never into itself or below itself, and never past the last level, counting the
 * moved entry's children.
 */
export function canMove(document: MenuDocument, id: string, targetId: string, position: DropPosition): boolean {
  const moving = locate(document, id)
  const target = locate(document, targetId)
  if (!moving || !target) return false
  if (isWithin(document, targetId, id)) return false
  const level = position === 'inside' ? target.level + 1 : target.level
  return level + height(moving.entry) - 1 <= MAX_MENU_LEVELS
}

/** Moves an entry with its children before, after or inside another; unchanged when not allowed. */
export function move(document: MenuDocument, id: string, targetId: string, position: DropPosition): MenuDocument {
  if (id === targetId || !canMove(document, id, targetId, position)) return document
  const next = clone(document)
  const moving = locate(next, id)!
  moving.siblings.splice(moving.index, 1)

  const target = locate(next, targetId)!
  if (position === 'inside') target.entry.children.push(moving.entry)
  else target.siblings.splice(position === 'before' ? target.index : target.index + 1, 0, moving.entry)
  return next
}

/** Alt+↑: before its previous sibling. */
export function moveUp(document: MenuDocument, id: string): MenuDocument {
  const found = locate(document, id)
  if (!found || found.index === 0) return document
  return move(document, id, found.siblings[found.index - 1].id, 'before')
}

/** Alt+↓: after its next sibling. */
export function moveDown(document: MenuDocument, id: string): MenuDocument {
  const found = locate(document, id)
  if (!found || found.index === found.siblings.length - 1) return document
  return move(document, id, found.siblings[found.index + 1].id, 'after')
}

/** Alt+→: the last child of its previous sibling. */
export function indent(document: MenuDocument, id: string): MenuDocument {
  const found = locate(document, id)
  if (!found || found.index === 0) return document
  return move(document, id, found.siblings[found.index - 1].id, 'inside')
}

/** Alt+←: out of its parent, just after it. */
export function outdent(document: MenuDocument, id: string): MenuDocument {
  const found = locate(document, id)
  if (!found || found.parent === null) return document
  return move(document, id, found.parent.id, 'after')
}
