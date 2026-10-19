// The site's menus, for the Navigation block's menu picker and for inserting a new block with a
// menu already chosen (024 research NV-16). Loaded once per site and shared; a failed load leaves
// the list empty, and the block then cannot be inserted until there is a menu.

import { ref, type Ref } from 'vue'
import { menusApi, type MenuList } from '../../api/menus'

const cache = new Map<string, Ref<MenuList | null>>()
const loading = new Set<string>()

export function useSiteMenus(siteId: string): Ref<MenuList | null> {
  let menus = cache.get(siteId)
  if (!menus) {
    menus = ref<MenuList | null>(null)
    cache.set(siteId, menus)
  }

  if (!loading.has(siteId) && menus.value === null) {
    loading.add(siteId)
    const target = menus
    menusApi
      .list(siteId)
      // Only a menu list is kept: anything else leaves the editor without menus, as a failure does.
      .then((list) => (target.value = Array.isArray(list?.items) ? list : null))
      .catch(() => undefined)
      .finally(() => loading.delete(siteId))
  }

  return menus
}

/** The ids of the site's menus already loaded, in name order: a new block starts with the first. */
export function menuIdsOf(siteId: string): string[] {
  return cache.get(siteId)?.value?.items.map((m) => m.id) ?? []
}

/** Forgets every site's menus: the editor reloads them when it opens another document. */
export function resetSiteMenus(): void {
  cache.clear()
}
