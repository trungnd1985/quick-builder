// Who is editing which document of a list, for markers in the page tree, the component list
// and an item list (023 FR-012, research CE-15). Fetched on start and every 15 seconds while
// the tab is visible; a failure leaves the markers as they were.

import { onBeforeUnmount, shallowRef } from 'vue'
import { editingApi, type EditingKind, type Holders } from '../api/editing'

export const PRESENCE_LIST_MS = 15_000

export type ListHolder = Holders['holders'][number]

export function usePresenceList(target: () => { siteId: string; kind: EditingKind; collectionId?: string }) {
  const holders = shallowRef<ReadonlyMap<string, ListHolder>>(new Map())
  let timer: ReturnType<typeof setInterval> | null = null

  async function refresh(): Promise<void> {
    if (typeof document !== 'undefined' && document.visibilityState === 'hidden') return
    const { siteId, kind, collectionId } = target()
    try {
      const answer = await editingApi.holders(siteId, kind, collectionId)
      holders.value = new Map(answer.holders.map((h) => [h.documentId, h]))
    } catch {
      // Markers are a courtesy: keep the last ones.
    }
  }

  function onVisibility(): void {
    if (document.visibilityState === 'visible') void refresh()
  }

  function start(): void {
    stop()
    void refresh()
    timer = setInterval(() => void refresh(), PRESENCE_LIST_MS)
    document.addEventListener('visibilitychange', onVisibility)
  }

  function stop(): void {
    if (timer) clearInterval(timer)
    timer = null
    document.removeEventListener('visibilitychange', onVisibility)
  }

  onBeforeUnmount(stop)

  /** The holder of one document, if anyone holds it. */
  const holderOf = (documentId: string): ListHolder | undefined => holders.value.get(documentId)

  return { holders, holderOf, refresh, start, stop }
}
