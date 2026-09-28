// The site's pages, for link pickers (011 FR-015). Loaded once per site and shared by every
// picker in the editor; a failed load leaves the list empty and the address option usable.

import { ref, type Ref } from 'vue'
import { pagesApi, type PageNode } from '../../api/pages'

const cache = new Map<string, Ref<PageNode[]>>()
const loading = new Set<string>()

export function useSitePages(siteId: string): Ref<PageNode[]> {
  let pages = cache.get(siteId)
  if (!pages) {
    pages = ref<PageNode[]>([])
    cache.set(siteId, pages)
  }

  if (!loading.has(siteId) && pages.value.length === 0) {
    loading.add(siteId)
    const target = pages
    pagesApi
      .tree(siteId)
      .then((tree) => (target.value = tree.pages))
      .catch(() => undefined)
      .finally(() => loading.delete(siteId))
  }

  return pages
}

/** Forgets every site's pages: the editor reloads them when it opens another document. */
export function resetSitePages(): void {
  cache.clear()
}
