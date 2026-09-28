// The editor's clipboard (011 research ED-18).
//
// Copied blocks go to localStorage with the user and site they came from, so they survive a
// reload — which the conflict flow relies on: copy, Reload latest, paste (FR-028) — and paste
// across pages and components of the same site (FR-007), in any tab. Pasting into another
// site, or as another user, finds nothing. Sign-out clears it.
//
// As a courtesy the blocks' plain text also goes to the system clipboard, for pasting into
// other applications. Every storage call is guarded: private windows and full quotas simply
// make the clipboard session-only.

import type { ContentNode } from '../schema/schema'
import { walkNodes } from '../ops/tree'

const KEY = 'qb.editor.clipboard'

interface Entry {
  userId: string
  siteId: string
  nodes: ContentNode[]
}

let memory: Entry | null = null

export function copyBlocks(userId: string, siteId: string, nodes: readonly ContentNode[]): void {
  const entry: Entry = { userId, siteId, nodes: [...nodes] }
  memory = entry
  try {
    localStorage.setItem(KEY, JSON.stringify(entry))
  } catch {
    // Storage unavailable or full: the in-memory copy still works in this tab.
  }

  const text = nodes.flatMap((node) => [...walkNodes(node)]).flatMap(plainText).join('\n')
  if (text && typeof navigator !== 'undefined' && navigator.clipboard) {
    navigator.clipboard.writeText(text).catch(() => undefined)
  }
}

/** The copied blocks, if they were copied by this user on this site. */
export function readBlocks(userId: string, siteId: string): ContentNode[] | null {
  let entry = memory
  try {
    const stored = localStorage.getItem(KEY)
    if (stored) entry = JSON.parse(stored) as Entry
  } catch {
    // Fall back to the in-memory copy.
  }
  return entry && entry.userId === userId && entry.siteId === siteId && Array.isArray(entry.nodes) ? entry.nodes : null
}

export function clearClipboard(): void {
  memory = null
  try {
    localStorage.removeItem(KEY)
  } catch {
    // Nothing stored.
  }
}

function plainText(node: ContentNode): string[] {
  const texts: string[] = []
  for (const name of ['text', 'label', 'title']) {
    const value = node.props[name]
    if (typeof value === 'string' && value) texts.push(value)
  }
  return texts
}
