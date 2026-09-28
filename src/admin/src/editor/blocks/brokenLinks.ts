// Links to pages that no longer exist (011 spec, Edge Cases). The draft keeps them and still
// saves; the canvas marks them so they are noticed. The mark is an editing aid, like a
// placeholder, and never part of the page.

import { computed, type ComputedRef } from 'vue'
import { useSitePages } from '../state/useSitePages'
import { isObject, type Json } from '../schema/subsetValidator'

/** Whether a link value names a page the site no longer has. */
export type BrokenLinkCheck = (link: Json | undefined) => boolean

/**
 * The check for one site. Until the site's pages have loaded — or when they could not be —
 * nothing is marked: an unknown page is not a missing one.
 */
export function useBrokenLinks(siteId: () => string): ComputedRef<BrokenLinkCheck> {
  const pages = useSitePages(siteId())
  return computed(() => {
    const ids = new Set(pages.value.map((p) => p.id))
    return (link) =>
      ids.size > 0 && isObject(link) && link.type === 'page' && typeof link.pageId === 'string' && !ids.has(link.pageId)
  })
}
