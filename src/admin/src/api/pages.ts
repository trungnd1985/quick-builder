// Typed client for the Pages module (010 contracts/pages-api.yaml).

import type { Publication } from './publishing'
import { request } from './request'
import type { TrashReceipt } from './trash'

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
  /**
   * The collection this page is the template of (016); null for an ordinary page. A template's
   * `path` is its address pattern, such as `/blog/{slug}`.
   */
  templateCollectionId?: string | null
}

/** 017 FR-028: what an accepted redirect offer did. */
export interface RedirectOutcome {
  created: number
  /** Old addresses left alone because live content now holds them. */
  skipped: number
  retargeted: number
  /** Redirects turned off because they would now loop. */
  turnedOff: string[]
}

export interface PageTree {
  pages: PageNode[]
  permissions: PagePermissions
  /** 016: what a move did to item addresses. */
  warnings?: TemplateWarning[]
  /** 017: present when a redirect offer was accepted. */
  redirects?: RedirectOutcome | null
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

/** 023 FR-021: where a copy came from. */
export interface CopyOrigin {
  sourceId: string
  sourceSavedAtUtc: string
  copiedBy: UserReference
  copiedAtUtc: string
}

/** 023 research CE-11: the member's version, saved as a new document. */
export interface CopyRequest {
  document: unknown
  baseSavedAtUtc: string
  title?: string
  name?: string
}

export interface PageDetail extends PageNode {
  /** 023: set on a page made by "save my version as a copy". */
  copyOrigin?: CopyOrigin | null
  /** 016: what a create or rename did to item addresses. */
  warnings?: TemplateWarning[]
  /** 017: present when a redirect offer was accepted. */
  redirects?: RedirectOutcome | null
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
  /** 023: set on a component made by "save my version as a copy". */
  copyOrigin?: CopyOrigin | null
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

/** What a template change would do, nothing changed (016 contracts/templates-api.yaml). */
export interface TemplateImpact {
  /** The address pattern after the change; null when removing, or when refused. */
  pattern: string | null
  /** The error code the change would be refused with, if any. */
  refusal: string | null
  liveStaticAddressLost: boolean
  itemsLosingAddress: number
  itemsGainingAddress: number
  brokenBindings: number
}

/** A warning about what a change did to item addresses (016 data-model §4). */
export interface TemplateWarning {
  code: 'items_change_address' | 'items_lose_address' | 'item_address_hidden'
  count?: number
  itemId?: string
}

export interface PageWithWarnings {
  page: PageDetail
  warnings: TemplateWarning[]
  /** 017: present when a redirect offer was accepted. */
  redirects?: RedirectOutcome | null
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

  update: (siteId: string, pageId: string, body: { title: string; slug: string; version: number; redirectOldAddresses?: boolean }): Promise<PageDetail> =>
    request<PageDetail>(page(siteId, pageId), { method: 'PUT', body: JSON.stringify(body) }),

  /** Moves a page under `parentId` (null: top level), after `afterPageId` (null: first). */
  move: (siteId: string, pageId: string, body: { parentId: string | null; afterPageId: string | null; version: number; redirectOldAddresses?: boolean }): Promise<PageTree> =>
    request<PageTree>(`${page(siteId, pageId)}/move`, { method: 'POST', body: JSON.stringify(body) }),

  makeHome: (siteId: string, pageId: string, version: number, redirectOldAddresses = false): Promise<PageTree> =>
    request<PageTree>(`${page(siteId, pageId)}/make-home`, { method: 'POST', body: JSON.stringify({ version, redirectOldAddresses }) }),

  /** Moves a page to the trash (021 FR-001); 017 FR-032: with `redirectToPageId`, its address redirects there. */
  remove: (siteId: string, pageId: string, version: number, redirectToPageId: string | null = null): Promise<TrashReceipt> =>
    request<TrashReceipt>(
      `${page(siteId, pageId)}?version=${version}${redirectToPageId ? `&redirectToPageId=${encodeURIComponent(redirectToPageId)}` : ''}`,
      { method: 'DELETE' },
    ),

  components: (siteId: string): Promise<ComponentList> =>
    request<ComponentList>(`${root(siteId)}/components`),

  createComponent: (siteId: string, name: string): Promise<Component> =>
    request<Component>(`${root(siteId)}/components`, { method: 'POST', body: JSON.stringify({ name }) }),

  renameComponent: (siteId: string, componentId: string, name: string, version: number): Promise<Component> =>
    request<Component>(component(siteId, componentId), { method: 'PUT', body: JSON.stringify({ name, version }) }),

  /** Moves a component to the trash (021 FR-001). */
  removeComponent: (siteId: string, componentId: string, version: number): Promise<TrashReceipt> =>
    request<TrashReceipt>(`${component(siteId, componentId)}?version=${version}`, { method: 'DELETE' }),

  usages: (siteId: string, componentId: string): Promise<PageReference[]> =>
    request<PageReference[]>(`${component(siteId, componentId)}/usages`),

  // --- Drafts (010 pages-api.yaml; the editor, 011) -------------------------------------

  pageDraft: (siteId: string, pageId: string): Promise<Draft> =>
    request<Draft>(`${page(siteId, pageId)}/draft`),

  /** 023 research CE-07: an open editor sends its editing session, so a session that lost the lock cannot save. */
  savePageDraft: (siteId: string, pageId: string, body: SaveDraftRequest, headers: Record<string, string> = {}): Promise<Draft> =>
    request<Draft>(`${page(siteId, pageId)}/draft`, { method: 'PUT', body: JSON.stringify(body), headers }),

  componentDraft: (siteId: string, componentId: string): Promise<Draft> =>
    request<Draft>(`${component(siteId, componentId)}/draft`),

  saveComponentDraft: (siteId: string, componentId: string, body: SaveDraftRequest, headers: Record<string, string> = {}): Promise<Draft> =>
    request<Draft>(`${component(siteId, componentId)}/draft`, { method: 'PUT', body: JSON.stringify(body), headers }),

  /** 023 FR-020: a new page after this one, holding the member's version; unpublished. */
  copyPage: (siteId: string, pageId: string, body: CopyRequest): Promise<PageDetail> =>
    request<PageDetail>(`${page(siteId, pageId)}/copies`, { method: 'POST', body: JSON.stringify(body) }),

  /** 023 FR-020: a new component holding the member's version, used by no page. */
  copyComponent: (siteId: string, componentId: string, body: CopyRequest): Promise<Component> =>
    request<Component>(`${component(siteId, componentId)}/copies`, { method: 'POST', body: JSON.stringify(body) }),

  getComponent: (siteId: string, componentId: string): Promise<Component> =>
    request<Component>(component(siteId, componentId)),

  // --- Templates (016 contracts/templates-api.yaml) -------------------------------------

  /** What making the page a template of `collectionId` (or, without one, removing it) would do. */
  templateImpact: (siteId: string, pageId: string, collectionId: string | null): Promise<TemplateImpact> =>
    request<TemplateImpact>(
      `${page(siteId, pageId)}/template${collectionId ? `?collectionId=${encodeURIComponent(collectionId)}` : ''}`,
    ),

  setTemplate: (siteId: string, pageId: string, collectionId: string, version: number, redirectOldAddresses = false): Promise<PageWithWarnings> =>
    request<PageWithWarnings>(`${page(siteId, pageId)}/template`, { method: 'PUT', body: JSON.stringify({ collectionId, version, redirectOldAddresses }) }),

  removeTemplate: (siteId: string, pageId: string, version: number): Promise<PageWithWarnings> =>
    request<PageWithWarnings>(`${page(siteId, pageId)}/template?version=${version}`, { method: 'DELETE' }),

  /** How many blocks in the site's drafts use a design token, counted now (011 ED-16). */
  tokenUsage: (siteId: string, tokenId: string): Promise<TokenUsage> =>
    request<TokenUsage>(`${root(siteId)}/token-usages/${encodeURIComponent(tokenId)}`),
}
