// Typed client for the Editor module (011 contracts/editor-api.yaml).

import { request } from './request'
import type { UserReference } from './pages'
import type { TokenSet } from '../editor/tokens/defaultTokens'

export { ApiError } from './request'

/** A site's design token set: what it saved, or the defaults with version 0. */
export interface DesignTokens {
  siteId: string
  version: number
  isDefault: boolean
  document: TokenSet
  updatedAtUtc: string | null
  updatedBy: UserReference | null
  /** A courtesy for the UI; the server checks every save. */
  permissions: { canManage: boolean }
}

const tokens = (siteId: string) => `/editor/sites/${encodeURIComponent(siteId)}/design-tokens`

export const editorApi = {
  designTokens: (siteId: string): Promise<DesignTokens> => request<DesignTokens>(tokens(siteId)),

  /** Replaces the site's token set; `version` 0 only before the first save. */
  saveDesignTokens: (siteId: string, document: TokenSet, version: number): Promise<DesignTokens> =>
    request<DesignTokens>(tokens(siteId), { method: 'PUT', body: JSON.stringify({ document, version }) }),
}
