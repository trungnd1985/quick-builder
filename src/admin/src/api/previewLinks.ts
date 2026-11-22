// Preview links (specs/022-draft-preview-links/contracts/preview-api.yaml): a secret,
// time-limited address that shows one page or item to someone without an account.

import { request } from './request'

export type PreviewKind = 'page' | 'item'

export interface PreviewLink {
  id: string
  documentKind: PreviewKind
  documentId: string
  versionNumber: number | null
  sampleItemId: string | null
  label: string | null
  createdBy: { id: string; displayName: string }
  createdAtUtc: string
  expiresAtUtc: string
  lastOpenedAtUtc: string | null
}

/** A created link: `url` holds the secret and is never shown again (FR-014). */
export interface CreatedPreviewLink {
  link: PreviewLink
  url: string
}

export interface SitePreviewLink {
  link: PreviewLink
  targetLabel: string | null
  collectionId: string | null
}

export interface SitePreviewLinks {
  items: SitePreviewLink[]
  nextCursor: string | null
}

export interface CreatePreviewLink {
  documentKind: PreviewKind
  documentId: string
  versionNumber?: number | null
  sampleItemId?: string | null
  label?: string | null
  expiresAtUtc: string
}

/** The most active links one page or item may have (FR-018). */
export const MAX_ACTIVE_LINKS = 20

const base = (siteId: string) => `/editor/sites/${encodeURIComponent(siteId)}/preview-links`

export const previewLinksApi = {
  create: (siteId: string, body: CreatePreviewLink) =>
    request<CreatedPreviewLink>(base(siteId), { method: 'POST', body: JSON.stringify(body) }),

  list: (siteId: string, kind: PreviewKind, documentId: string) =>
    request<PreviewLink[]>(`${base(siteId)}?documentKind=${kind}&documentId=${encodeURIComponent(documentId)}`),

  site: (siteId: string, cursor?: string | null) =>
    request<SitePreviewLinks>(`${base(siteId)}/all${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`),

  revoke: (siteId: string, linkId: string) =>
    request<void>(`${base(siteId)}/${encodeURIComponent(linkId)}/revoke`, { method: 'POST' }),

  /** One page's or item's links, or, with no target, every link of the site (Administrators). */
  revokeAll: (siteId: string, target?: { documentKind: PreviewKind; documentId: string }) =>
    request<{ revoked: number }>(`${base(siteId)}/revoke-all`, { method: 'POST', body: JSON.stringify(target ?? {}) }),
}
