// Names and statuses of referenced items, for reference chips (015 FR-017, research IT-21).
// One lookup call per batch of ids the form shows; ids the server does not know are missing.

import { reactive } from 'vue'
import { itemsApi, type ItemRef } from '../../../api/items'

export type RefState = { kind: 'loading' } | { kind: 'found'; item: ItemRef } | { kind: 'missing' }

const LOOKUP_LIMIT = 200

/** A per-form cache of referenced items. */
export function useItemRefs(siteId: () => string) {
  const known = reactive(new Map<string, RefState>())

  async function load(ids: readonly string[]): Promise<void> {
    const wanted = [...new Set(ids.map((id) => id.toLowerCase()))].filter((id) => !known.has(id))
    if (wanted.length === 0) return
    for (const id of wanted) known.set(id, { kind: 'loading' })

    for (let start = 0; start < wanted.length; start += LOOKUP_LIMIT) {
      const chunk = wanted.slice(start, start + LOOKUP_LIMIT)
      try {
        const found = await itemsApi.lookup(siteId(), chunk)
        for (const item of found.items) known.set(item.id.toLowerCase(), { kind: 'found', item })
        for (const id of found.missing) known.set(id.toLowerCase(), { kind: 'missing' })
      } catch {
        for (const id of chunk) known.delete(id)
      }
    }
  }

  /** Remembers an item just chosen in a picker, so its chip needs no lookup. */
  function remember(item: ItemRef): void {
    known.set(item.id.toLowerCase(), { kind: 'found', item })
  }

  const stateOf = (id: string): RefState => known.get(id.toLowerCase()) ?? { kind: 'loading' }

  return { load, remember, stateOf }
}

/** Everything the module shares, so every picker of a form reads one cache. */
export type ItemRefs = ReturnType<typeof useItemRefs>
