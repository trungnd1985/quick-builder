// The items a Collection list shows on the canvas (016 US4, research TB-14): the site's
// published items, as visitors would see them, fetched through the list preview and cached per
// list settings for the open document, so editing a card never asks again and editing a filter
// asks once it settles.

import { onBeforeUnmount, shallowRef, watch, type ShallowRef } from 'vue'
import { cmsApi, type ListPreviewItem, type ListPreviewRequest } from '../../api/cms'
import type { ContentNode } from '../schema/schema'
import { isObject, type Json } from '../schema/subsetValidator'
import { useEditorStore } from '../state/editorStore'
import type { BoundItem } from './resolve'

/** How long a list's settings must stay still before the canvas asks for its items. */
export const PREVIEW_DELAY_MS = 300

/** What a list shows on the canvas. */
export interface ListPreviewState {
  /** Nothing asked: the list has no source yet. */
  idle: boolean
  loading: boolean
  /** The preview could not be fetched; the card shows once, unbound. */
  failed: boolean
  items: readonly BoundItem[]
  /** Each item's page, by item id, when its collection's template is live. */
  addresses: Readonly<Record<string, string | null>>
  total: number
}

const IDLE: ListPreviewState = { idle: true, loading: false, failed: false, items: [], addresses: {}, total: 0 }

/** Settings that change what a list holds; layout settings do not. */
const QUERY_PROPS = ['source', 'filters', 'sort', 'limit', 'paging'] as const

/**
 * A live template's pattern, read back from an item's address: `/blog/hello` for slug
 * `hello` is `/blog/{slug}`. Null when the item has no page.
 */
export function patternOf(item: ListPreviewItem): string | null {
  if (!item.address || !item.address.endsWith(`/${item.slug}`)) return null
  return `${item.address.slice(0, item.address.length - item.slug.length)}{slug}`
}

export const toBoundItem = (item: ListPreviewItem): BoundItem => ({
  id: item.id,
  collectionId: item.collectionId,
  slug: item.slug,
  name: item.name,
  firstPublishedAt: item.firstPublishedAt,
  lastPublishedAt: item.lastPublishedAt,
  values: item.values as Json,
})

/** The reference fields the bindings under `node` follow (`via`), so their items come back too. */
export function viaFields(node: ContentNode): string[] {
  const fields = new Set<string>()
  const visit = (n: ContentNode): void => {
    const bind = n.props.bind
    if (isObject(bind)) {
      for (const binding of Object.values(bind)) {
        const value = isObject(binding) ? binding.value : undefined
        const readers = isObject(value) && Array.isArray(value.parts) ? value.parts : [value]
        for (const reader of readers) {
          if (isObject(reader) && typeof reader.via === 'string') fields.add(reader.via)
        }
      }
    }
    n.children.forEach(visit)
  }
  visit(node)
  return [...fields].sort()
}

/** The preview request for a list, or null when it has no source to ask about. */
export function previewRequest(list: ContentNode, sampleItemId: string | null): ListPreviewRequest | null {
  const source = list.props.source
  if (!isObject(source) || (typeof source.collectionId !== 'string' && typeof source.field !== 'string')) return null

  const props: Record<string, Json> = {}
  for (const prop of QUERY_PROPS) {
    if (list.props[prop] !== undefined) props[prop] = list.props[prop]
  }

  const via = viaFields(list)
  return {
    list: props,
    ...(sampleItemId ? { scopes: { page: sampleItemId } } : {}),
    ...(via.length > 0 ? { via } : {}),
  }
}

/** The published items `list()` shows, kept current as its settings and the sample item change. */
export function useListPreview(list: () => ContentNode): ShallowRef<ListPreviewState> {
  const store = useEditorStore()
  const state = shallowRef<ListPreviewState>(IDLE)
  let timer: ReturnType<typeof setTimeout> | undefined
  let asked = 0

  const cache = store.bindings.previews
  const key = () => {
    const request = previewRequest(list(), store.bindings.sampleItem?.id ?? null)
    return request ? JSON.stringify([store.bindings.siteId, request]) : null
  }

  async function fetchPreview(requestKey: string): Promise<void> {
    const [siteId, request] = JSON.parse(requestKey) as [string, ListPreviewRequest]
    const ticket = ++asked
    let pending = cache.get(requestKey)
    if (!pending) {
      pending = cmsApi.listPreview(siteId, request)
      cache.set(requestKey, pending)
      // A failure is not kept: the next look asks again.
      pending.catch(() => cache.delete(requestKey))
    }

    try {
      const preview = await pending
      if (ticket !== asked) return
      const referenced = Object.fromEntries(Object.entries(preview.referenced).map(([id, item]) => [id, toBoundItem(item)]))
      const patterns: Record<string, string> = {}
      for (const item of [...preview.items, ...Object.values(preview.referenced)]) {
        const pattern = patternOf(item)
        if (pattern) patterns[item.collectionId] = pattern
      }
      store.bindings.addPreview(preview.schemas as Record<string, Json>, referenced, patterns)
      state.value = {
        idle: false,
        loading: false,
        failed: false,
        items: preview.items.map(toBoundItem),
        addresses: Object.fromEntries(preview.items.map((item) => [item.id, item.address])),
        total: preview.total,
      }
    } catch {
      if (ticket === asked) state.value = { ...IDLE, idle: false, failed: true }
    }
  }

  watch(key, (requestKey) => {
    clearTimeout(timer)
    if (!requestKey) {
      asked++
      state.value = IDLE
      return
    }

    if (cache.has(requestKey)) {
      void fetchPreview(requestKey)
      return
    }

    state.value = { ...state.value, idle: false, loading: true }
    timer = setTimeout(() => void fetchPreview(requestKey), PREVIEW_DELAY_MS)
  }, { immediate: true })

  onBeforeUnmount(() => clearTimeout(timer))
  return state
}
