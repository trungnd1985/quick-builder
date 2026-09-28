// Shared components as a page's canvas shows them (011 US7, FR-038): each one's name and
// current draft, loaded once per editing session and shared by every reference to it.

import { reactive } from 'vue'
import { pagesApi, type Component } from '../../api/pages'
import type { ContentDocument, ContentNode } from '../schema/schema'

export interface SharedComponent {
  state: 'loading' | 'ready' | 'missing'
  name: string
  root: ContentNode | null
}

const entries = new Map<string, SharedComponent>()
let listed: Promise<Component[]> | null = null
let listedFor = ''

/** The site's components, once per site per session (names for references and the palette). */
export function siteComponents(siteId: string): Promise<Component[]> {
  if (!listed || listedFor !== siteId) {
    listedFor = siteId
    listed = pagesApi.components(siteId).then((list) => list.components).catch(() => [])
  }
  return listed
}

export function useSharedComponent(siteId: string, componentId: string): SharedComponent {
  const key = `${siteId}/${componentId}`
  const existing = entries.get(key)
  if (existing) return existing

  const entry = reactive<SharedComponent>({ state: 'loading', name: '', root: null })
  entries.set(key, entry)

  void siteComponents(siteId).then((list) => {
    entry.name = list.find((c) => c.id === componentId)?.name ?? ''
  })
  pagesApi
    .componentDraft(siteId, componentId)
    .then((draft) => {
      entry.root = (draft.document as unknown as ContentDocument).root
      entry.state = 'ready'
    })
    .catch(() => {
      entry.state = 'missing'
    })

  return entry
}

/** Forgets everything: the editor opens each document with fresh components. */
export function resetComponentCache(): void {
  entries.clear()
  listed = null
  listedFor = ''
}
