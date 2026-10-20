// Typed client for menus (024 contracts/menus-api.yaml). Publishing a menu goes through
// publishing.ts with the subject kind `menu`, as for pages and components.

import type { Publication } from './publishing'
import { request } from './request'
import type { MenuDocument } from '../editor/schema/menuValidator'

export type { MenuDocument, MenuEntry, MenuTarget, MenuTargetType } from '../editor/schema/menuValidator'

/** What the signed-in user may do with this site's menus, decided by the server. */
export interface MenuPermissions {
  /** Create, rename, duplicate, delete menus and save their drafts (`pages.menus.manage`). */
  canManage: boolean
  /** Publish, unpublish, roll back and schedule (`pages.publishing.manage`). */
  canPublish: boolean
}

/** Where a menu entry's page or item stands (024 data-model §4.3). */
export type MenuTargetState = 'live' | 'not_published' | 'deleted' | 'no_page'

export interface MenuTargetStatus {
  kind: 'page' | 'item'
  id: string
  state: MenuTargetState
  /** The page's current title or the item's current name; null when deleted. */
  title: string | null
}

/** One menu, as the list shows it. */
export interface MenuSummary {
  id: string
  name: string
  entryCount: number
  /** Entries whose page or item was deleted. */
  brokenCount: number
  /** Pages and components that show it, in a draft or live version. */
  usedBy: number
  /** Opaque. Send it back to rename or delete. */
  version: number
  /** Opaque. Send it back to save or publish the draft. */
  draftVersion: number
  createdAtUtc: string
  updatedAtUtc: string
  publication: Publication
}

/** One menu with its draft and the state of every target. */
export interface Menu extends MenuSummary {
  draft: MenuDocument
  targets: MenuTargetStatus[]
  permissions: MenuPermissions
}

export interface MenuList {
  items: MenuSummary[]
  /** The most menus a site may hold. */
  limit: number
  permissions: MenuPermissions
}

/** One entry of the draft, resolved for the editor canvas (FR-018). */
export interface MenuPreviewEntry {
  id: string
  label: string
  kind: 'page' | 'item' | 'url' | 'anchor' | 'none'
  state: MenuTargetState
  hidden: boolean
  newTab: boolean
  children: MenuPreviewEntry[]
}

export interface MenuPreview {
  menuId: string
  name: string
  entries: MenuPreviewEntry[]
}

/** A page or component that shows a menu. */
export interface MenuUsage {
  kind: 'page' | 'component'
  id: string
  label: string
  inDraft: boolean
  inLive: boolean
}

/** A menu that points at a page or an item (FR-026). */
export interface MenuReference {
  menuId: string
  name: string
  inDraft: boolean
  inLive: boolean
}

const root = (siteId: string) => `/pages/sites/${encodeURIComponent(siteId)}/menus`
const one = (siteId: string, menuId: string) => `${root(siteId)}/${encodeURIComponent(menuId)}`
const json = (method: string, body: unknown): RequestInit => ({ method, body: JSON.stringify(body) })

export const menusApi = {
  list: (siteId: string): Promise<MenuList> => request<MenuList>(root(siteId)),

  get: (siteId: string, menuId: string): Promise<Menu> => request<Menu>(one(siteId, menuId)),

  create: (siteId: string, name: string): Promise<Menu> => request<Menu>(root(siteId), json('POST', { name })),

  duplicate: (siteId: string, menuId: string, name: string): Promise<Menu> =>
    request<Menu>(root(siteId), json('POST', { name, duplicateOf: menuId })),

  rename: (siteId: string, menuId: string, name: string, version: number): Promise<Menu> =>
    request<Menu>(one(siteId, menuId), json('PUT', { name, version })),

  remove: (siteId: string, menuId: string, version: number): Promise<void> =>
    request<void>(`${one(siteId, menuId)}?version=${version}`, { method: 'DELETE' }),

  saveDraft: (siteId: string, menuId: string, document: MenuDocument, version: number): Promise<Menu> =>
    request<Menu>(`${one(siteId, menuId)}/draft`, json('PUT', { document, version })),

  preview: (siteId: string, menuId: string): Promise<MenuPreview> => request<MenuPreview>(`${one(siteId, menuId)}/preview`),

  usages: (siteId: string, menuId: string): Promise<{ items: MenuUsage[] }> => request(`${one(siteId, menuId)}/usages`),

  /** The menus whose draft or live version points at a page or an item (FR-026). */
  references: (siteId: string, target: { pageId: string } | { itemId: string }): Promise<{ items: MenuReference[] }> =>
    request(`${root(siteId)}/references?${'pageId' in target ? `pageId=${encodeURIComponent(target.pageId)}` : `itemId=${encodeURIComponent(target.itemId)}`}`),
}
