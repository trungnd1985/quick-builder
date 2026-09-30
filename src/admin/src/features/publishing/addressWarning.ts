// How many published pages a move or slug change gives a new address (012 FR-011,
// clarification: a published page moves at once, with a warning first). Counted from the
// tree the screen already has: the page and everything under it that visitors can reach.

import type { PageNode } from '../../api/pages'

/** The number of live pages among `pageId` and its descendants. */
export function liveAddressCount(pages: readonly PageNode[], pageId: string): number {
  const subtree = new Set([pageId])
  // The tree is depth-first with parents before children, so one pass collects the subtree.
  for (const page of pages) {
    if (page.parentId !== null && subtree.has(page.parentId)) subtree.add(page.id)
  }

  return pages.filter((p) => subtree.has(p.id) && p.publication?.state === 'published').length
}
