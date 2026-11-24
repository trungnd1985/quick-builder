// Typed client for a site's redirects (017 contracts/seo-api.yaml).

import { API_ROOT, request } from './request'

export { ApiError } from './request'

export type RedirectMatch = 'exact' | 'prefix'
export type RedirectState = 'active' | 'off' | 'hidden' | 'broken'

export type RedirectTarget =
  | { kind: 'page'; pageId: string }
  | { kind: 'item'; collectionId: string; itemId: string }
  | { kind: 'path'; path: string }
  | { kind: 'url'; url: string }

export interface RedirectHistoryEntry {
  action: string
  origin: string
  actorUserId: string | null
  at: string
}

export interface Redirect {
  id: string
  /** The normalised path, with its canonical query when it has one. */
  source: string
  match: RedirectMatch
  target: RedirectTarget
  /** Where it sends visitors now; null when its page or item has no live address. */
  targetAddress: string | null
  statusCode: 301 | 302
  enabled: boolean
  note: string | null
  state: RedirectState
  hiddenBy: { kind: 'page' | 'item'; id: string } | null
  origin: 'manual' | 'import' | 'offer'
  hits: number
  lastUsedAt: string | null
  createdAt: string
  updatedAt: string
  version: number
  /** Create and update responses. */
  shortened?: boolean
  warnings?: string[]
  /** Get responses. */
  history?: RedirectHistoryEntry[]
}

export interface RedirectInput {
  source: string
  match: RedirectMatch
  target: RedirectTarget
  statusCode: 301 | 302
  enabled: boolean
  note: string | null
}

export interface RedirectList {
  items: Redirect[]
  total: number
  limit: number
}

export interface ImportResult {
  added: number
  shortened: number
  refused: { line: number; errorCode: string; detail: string | null }[]
}

export interface ListOptions {
  q?: string
  state?: RedirectState
  sort?: 'source' | 'hits' | 'lastUsed' | 'updated'
  dir?: 'asc' | 'desc'
  page?: number
}

const root = (siteId: string) => `/pages/sites/${siteId}/redirects`

export const redirectsApi = {
  list: (siteId: string, options: ListOptions = {}): Promise<RedirectList> => {
    const query = new URLSearchParams()
    for (const [name, value] of Object.entries(options)) {
      if (value !== undefined && value !== '') query.set(name, String(value))
    }
    const suffix = query.toString()
    return request<RedirectList>(`${root(siteId)}${suffix ? `?${suffix}` : ''}`)
  },

  get: (siteId: string, id: string): Promise<Redirect> => request<Redirect>(`${root(siteId)}/${id}`),

  create: (siteId: string, input: RedirectInput): Promise<Redirect> =>
    request<Redirect>(root(siteId), { method: 'POST', body: JSON.stringify(input) }),

  update: (siteId: string, id: string, input: RedirectInput & { version: number }): Promise<Redirect> =>
    request<Redirect>(`${root(siteId)}/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  remove: (siteId: string, id: string, version: number): Promise<void> =>
    request<void>(`${root(siteId)}/${id}?version=${version}`, { method: 'DELETE' }),

  /** The file goes as it is: the server reads it as UTF-8 CSV (FR-019). */
  import: (siteId: string, file: Blob): Promise<ImportResult> =>
    request<ImportResult>(`${root(siteId)}/import`, { method: 'POST', body: file, headers: { 'content-type': 'text/csv' } }),

  /** Where the browser downloads the export from. */
  exportUrl: (siteId: string): string => `${API_ROOT}${root(siteId)}/export`,
}
