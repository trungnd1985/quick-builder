// Typed client for the Pages module (010 contracts/pages-api.yaml).

import type { Publication } from './publishing'
import { request } from './request'

export { ApiError, type ProblemDetails } from './request'

/**
 * What the signed-in user may do on this site, decided by the server for this request
 * (010 research P-13). Controls are hidden from it; the server checks every action regardless.
 */
export interface PagePermissions {
  canManageStructure: boolean
  canEditPageDrafts: boolean
  canManageComponents: boolean
  /** Publish, unpublish, roll back and schedule (012 `pages.publishing.manage`). */
  canPublish: boolean
  /** Read the site's publish log (012 `pages.publish_log.view`, Administrator only). */
  canViewPublishLog: boolean
}

/** A page's or component's publication state (012; see publishing.ts). */
export type { Publication }

/** One page in the flat, depth-first tree: parents before children, siblings by position. */
export interface PageNode {
  id: string
  parentId: string | null
  position: number
  /** 1 for a top-level page. */
  depth: number
  title: string
  slug: string
  /** The page's address within the site, e.g. `/services/hosting`; `/` for the home page. */
  path: string
  isHome: boolean
  childCount: number
  /** Opaque. Send it back with a change to this page. */
  version: number
  /** Whether and what visitors see (012). */
  publication: Publication
}

export interface PageTree {
  pages: PageNode[]
  permissions: PagePermissions
}

export interface UserReference {
  id: string
  displayName: string | null
}

export interface DraftSummary {
  schemaVersion: number
  version: number
  updatedAtUtc: string
  updatedBy: UserReference
}

export interface PageDetail extends PageNode {
  draft: DraftSummary
  createdAtUtc: string
  updatedAtUtc: string
  permissions: PagePermissions
}

export interface CreatePageRequest {
  title: string
  slug: string
  parentId?: string | null
  /** Omit for last among the siblings; `null` for first; an id to follow that page. */
  afterPageId?: string | null
  isHome?: boolean
}

export interface Component {
  id: string
  name: string
  usageCount: number
  version: number
  draft: DraftSummary
  createdAtUtc: string
  updatedAtUtc: string
  /** Whether and what visitors see (012). */
  publication: Publication
}

export interface ComponentList {
  components: Component[]
  permissions: PagePermissions
}

/** A content document (docs/contracts/page-schema.md). Its shape is the editor's business. */
export type ContentDocumentJson = Record<string, unknown>

/** A draft with its document (010 pages-api.yaml, Draft). */
export interface Draft extends DraftSummary {
  document: ContentDocumentJson
}

export interface SaveDraftRequest {
  document: unknown
  version: number
}

export interface TokenUsage {
  tokenId: string
  blockCount: number
  documentCount: number
}

export interface PageReference {
  id: string
  title: string
  path: string
}

const root = (siteId: string) => `/pages/sites/${encodeURIComponent(siteId)}`
const page = (siteId: string, pageId: string) => `${root(siteId)}/pages/${encodeURIComponent(pageId)}`
const component = (siteId: string, componentId: string) => `${root(siteId)}/components/${encodeURIComponent(componentId)}`

export const pagesApi = {
  tree: (siteId: string): Promise<PageTree> =>
    request<PageTree>(`${root(siteId)}/pages`),

  get: (siteId: string, pageId: string): Promise<PageDetail> =>
    request<PageDetail>(page(siteId, pageId)),

  create: (siteId: string, body: CreatePageRequest): Promise<PageDetail> =>
    request<PageDetail>(`${root(siteId)}/pages`, { method: 'POST', body: JSON.stringify(body) }),

  update: (siteId: string, pageId: string, body: { title: string; slug: string; version: number }): Promise<PageDetail> =>
    request<PageDetail>(page(siteId, pageId), { method: 'PUT', body: JSON.stringify(body) }),

  /** Moves a page under `parentId` (null: top level), after `afterPageId` (null: first). */
  move: (siteId: string, pageId: string, body: { parentId: string | null; afterPageId: string | null; version: number }): Promise<PageTree> =>
    request<PageTree>(`${page(siteId, pageId)}/move`, { method: 'POST', body: JSON.stringify(body) }),

  makeHome: (siteId: string, pageId: string, version: number): Promise<PageTree> =>
    request<PageTree>(`${page(siteId, pageId)}/make-home`, { method: 'POST', body: JSON.stringify({ version }) }),

  remove: (siteId: string, pageId: string, version: number): Promise<void> =>
    request<void>(`${page(siteId, pageId)}?version=${version}`, { method: 'DELETE' }),

  components: (siteId: string): Promise<ComponentList> =>
    request<ComponentList>(`${root(siteId)}/components`),

  createComponent: (siteId: string, name: string): Promise<Component> =>
    request<Component>(`${root(siteId)}/components`, { method: 'POST', body: JSON.stringify({ name }) }),

  renameComponent: (siteId: string, componentId: string, name: string, version: number): Promise<Component> =>
    request<Component>(component(siteId, componentId), { method: 'PUT', body: JSON.stringify({ name, version }) }),

  removeComponent: (siteId: string, componentId: string, version: number): Promise<void> =>
    request<void>(`${component(siteId, componentId)}?version=${version}`, { method: 'DELETE' }),

  usages: (siteId: string, componentId: string): Promise<PageReference[]> =>
    request<PageReference[]>(`${component(siteId, componentId)}/usages`),

  // --- Drafts (010 pages-api.yaml; the editor, 011) -------------------------------------

  pageDraft: (siteId: string, pageId: string): Promise<Draft> =>
    request<Draft>(`${page(siteId, pageId)}/draft`),

  savePageDraft: (siteId: string, pageId: string, body: SaveDraftRequest): Promise<Draft> =>
    request<Draft>(`${page(siteId, pageId)}/draft`, { method: 'PUT', body: JSON.stringify(body) }),

  componentDraft: (siteId: string, componentId: string): Promise<Draft> =>
    request<Draft>(`${component(siteId, componentId)}/draft`),

  saveComponentDraft: (siteId: string, componentId: string, body: SaveDraftRequest): Promise<Draft> =>
    request<Draft>(`${component(siteId, componentId)}/draft`, { method: 'PUT', body: JSON.stringify(body) }),

  getComponent: (siteId: string, componentId: string): Promise<Component> =>
    request<Component>(component(siteId, componentId)),

  /** How many blocks in the site's drafts use a design token, counted now (011 ED-16). */
  tokenUsage: (siteId: string, tokenId: string): Promise<TokenUsage> =>
    request<TokenUsage>(`${root(siteId)}/token-usages/${encodeURIComponent(tokenId)}`),
}
