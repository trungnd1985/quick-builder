// Typed client for the Media module (003 contracts/media-api.yaml, extended by
// 013 contracts/media-api.yaml).

import { API_ROOT, ApiError, REQUEST_HEADER, refusal, request } from './request'

export { ApiError } from './request'

export interface UserSummary {
  id: string
  displayName: string
}

/** How many pages and components use a file (013 FR-007). */
export interface MediaUsageSummary {
  places: number
  draftPlaces: number
  livePlaces: number
  historyOnlyPlaces: number
  /** Visitors can see it: deleting or replacing it needs publishing rights. */
  inLiveUse: boolean
}

export interface MediaVariant {
  width: number
  /** Admin address of the resized preview, including any base path. */
  path: string
}

export type MetadataState = 'pending' | 'recorded' | 'not_applicable' | 'over_limit' | 'unreadable'

export interface MediaFile {
  id: string
  siteId: string
  /** The original name, cleaned for display. Always render as text. */
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedAtUtc: string
  /** Who first uploaded it. Never changes, even when the file is replaced. */
  uploadedBy: UserSummary
  /** Path on the site's public surface, including any base path. */
  publicPath: string
  /** False when the stored bytes are missing. */
  available: boolean
  /** Whether this user may delete it. A courtesy: the server re-checks (003 M-18, 013 MV-06). */
  canDelete: boolean
  folderId: string | null
  /** Images only. Always render as text. */
  altText: string | null
  width: number | null
  height: number | null
  contentVersion: number
  metadataState: MetadataState
  replacedAtUtc: string | null
  replacedBy: UserSummary | null
  /** Resized admin previews, ascending; empty when the file has none (013 MV-16). */
  variants: MediaVariant[]
  /** Where to load the thumbnail from, including any base path. */
  thumbnailPath: string
  usage: MediaUsageSummary
  /** Whether this user may replace its contents. A courtesy: the server re-checks. */
  canReplace: boolean
}

export interface MediaFilePage {
  items: MediaFile[]
  /** Files matching the query. */
  totalCount: number
  nextCursor: string | null
}

export type MediaKind = 'image' | 'document'
export type MediaSort = 'uploaded' | 'name' | 'size'

/** What to list (013 MV-03). Without `q`, one folder; with `q`, a search. */
export interface MediaListQuery {
  /** The folder to browse, or to search under with `scope: 'subtree'`; absent means the top level. */
  folderId?: string | null
  scope?: 'folder' | 'subtree'
  /** Literal text, matched ignoring case and accents against name and alternative text. */
  q?: string
  kind?: MediaKind
  sort?: MediaSort
  direction?: 'asc' | 'desc'
  limit?: number
  cursor?: string | null
}

export interface MediaFolder {
  id: string
  /** Always render as text. */
  name: string
  parentId: string | null
  /** Files directly in the folder. */
  fileCount: number
  createdAtUtc: string
}

export interface MediaFolderTree {
  folders: MediaFolder[]
  topLevelFileCount: number
  revision: number
}

export interface MediaUsagePlace {
  kind: 'page' | 'component'
  id: string
  /** Page title or component name. Always render as text. */
  title: string
  inDraft: boolean
  live: boolean
  inHistory: boolean
  /** For a live component, how many live pages show it. */
  livePagesThroughComponent: number | null
}

export interface MediaUsage {
  summary: MediaUsageSummary
  /** Empty when the caller may not see the pages (only counts are given then). */
  places: MediaUsagePlace[]
}

export interface StorageCheck {
  id: string
  trigger: 'startup' | 'admin' | 'cli'
  provider: 'Local' | 'S3' | 'AzureBlob'
  status: 'running' | 'completed' | 'failed'
  startedAtUtc: string
  finishedAtUtc: string | null
  filesChecked: number
  missingCount: number
  missing?: { siteId: string; fileId: string }[]
}

export interface StorageStatus {
  provider: 'Local' | 'S3' | 'AzureBlob'
  latestCheck: StorageCheck | null
}

/** Extensions the upload picker offers. The server decides from content regardless (003 M-07). */
export const ACCEPTED_EXTENSIONS = '.jpg,.jpeg,.png,.gif,.webp,.avif,.pdf'

/** Extensions offered when replacing a file of the given kind (013 FR-017). */
export const ACCEPTED_BY_KIND: Record<MediaKind, string> = {
  image: '.jpg,.jpeg,.png,.gif,.webp,.avif',
  document: '.pdf',
}

export function kindOf(file: Pick<MediaFile, 'contentType'>): MediaKind {
  return file.contentType.startsWith('image/') ? 'image' : 'document'
}

const files = (siteId: string) => `/media/sites/${encodeURIComponent(siteId)}/files`
const file = (siteId: string, fileId: string) => `${files(siteId)}/${encodeURIComponent(fileId)}`
const folders = (siteId: string) => `/media/sites/${encodeURIComponent(siteId)}/folders`

function queryString(query: MediaListQuery): string {
  const params = new URLSearchParams()
  if (query.folderId) params.set('folderId', query.folderId)
  if (query.scope && query.scope !== 'folder') params.set('scope', query.scope)
  if (query.q) params.set('q', query.q)
  if (query.kind) params.set('kind', query.kind)
  if (query.sort) params.set('sort', query.sort)
  if (query.direction) params.set('direction', query.direction)
  if (query.limit) params.set('limit', String(query.limit))
  if (query.cursor) params.set('cursor', query.cursor)
  const text = params.toString()
  return text ? `?${text}` : ''
}

/**
 * Sends a file as the raw request body (003 research M-10).
 *
 * XMLHttpRequest rather than fetch, because fetch still reports no upload progress. It
 * sends the same proof-of-origin header and cookie as every other call, and refusals go
 * through the same handling (sign-in, setup).
 */
function sendFile(method: 'POST' | 'PUT', url: string, body: File, onProgress: (fraction: number) => void): Promise<MediaFile> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open(method, url)
    xhr.withCredentials = true
    xhr.setRequestHeader(REQUEST_HEADER, '1')
    xhr.setRequestHeader('X-QuickBuilder-File-Name', encodeURIComponent(body.name))
    // Browsers leave type empty for extensions the OS does not know; the server treats
    // octet-stream as "not declared" (003 M-08).
    xhr.setRequestHeader('Content-Type', body.type || 'application/octet-stream')

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable && event.total > 0) {
        onProgress(event.loaded / event.total)
      }
    }

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        onProgress(1)
        resolve(JSON.parse(xhr.responseText) as MediaFile)
      } else {
        reject(refusal(xhr.status, xhr.statusText, xhr.responseText, xhr.getResponseHeader('Retry-After')))
      }
    }

    xhr.onerror = () => reject(new ApiError(xhr.statusText || 'Network error', 0))
    xhr.onabort = () => reject(new ApiError('Upload cancelled', 0))

    xhr.send(body)
  })
}

export const mediaApi = {
  /** One page of a folder or a search. A bare string is a cursor, for 003-era callers. */
  list: (siteId: string, query?: MediaListQuery | string | null): Promise<MediaFilePage> => {
    const q: MediaListQuery = typeof query === 'string' ? { cursor: query } : (query ?? {})
    return request<MediaFilePage>(files(siteId) + queryString(q))
  },

  get: (siteId: string, fileId: string): Promise<MediaFile> => request<MediaFile>(file(siteId, fileId)),

  /** Sets or clears an image's alternative text (013 FR-008). */
  updateAltText: (siteId: string, fileId: string, altText: string | null): Promise<MediaFile> =>
    request<MediaFile>(file(siteId, fileId), { method: 'PATCH', body: JSON.stringify({ altText }) }),

  /** Files the given files in a folder, or at the top level with null (FR-003: addresses do not change). */
  move: (siteId: string, fileIds: string[], folderId: string | null): Promise<void> =>
    request<void>(`${files(siteId)}/move`, { method: 'POST', body: JSON.stringify({ fileIds, folderId }) }),

  usage: (siteId: string, fileId: string): Promise<MediaUsage> => request<MediaUsage>(`${file(siteId, fileId)}/usage`),

  /**
   * Deletes a file. A file in use is refused with `media.in_use` (carrying `usage`) unless
   * `confirmInUse` is set (013 FR-015).
   */
  remove: (siteId: string, fileId: string, options: { confirmInUse?: boolean } = {}): Promise<void> =>
    request<void>(file(siteId, fileId) + (options.confirmInUse ? '?confirmInUse=true' : ''), { method: 'DELETE' }),

  /** Where the admin shell previews a file: authenticated, and independent of host names (FR-018). */
  previewUrl: (siteId: string, fileId: string): string => `${API_ROOT}${file(siteId, fileId)}/content`,

  /** Uploads one file, optionally into a folder (013 FR-010). */
  upload(siteId: string, body: File, onProgress: (fraction: number) => void, folderId?: string | null): Promise<MediaFile> {
    const url = `${API_ROOT}${files(siteId)}${folderId ? `?folderId=${encodeURIComponent(folderId)}` : ''}`
    return sendFile('POST', url, body, onProgress)
  },

  /** Replaces a file's contents, keeping its identity (013 FR-017). */
  replace(siteId: string, fileId: string, body: File, expectedContentVersion: number, onProgress: (fraction: number) => void): Promise<MediaFile> {
    const url = `${API_ROOT}${file(siteId, fileId)}/content?expectedContentVersion=${expectedContentVersion}`
    return sendFile('PUT', url, body, onProgress)
  },

  folders: {
    list: (siteId: string): Promise<MediaFolderTree> => request<MediaFolderTree>(folders(siteId)),

    create: (siteId: string, name: string, parentId: string | null): Promise<MediaFolder> =>
      request<MediaFolder>(folders(siteId), { method: 'POST', body: JSON.stringify({ name, parentId }) }),

    /** Renames and/or moves; pass `parentId: null` to move to the top level. */
    update: (siteId: string, folderId: string, change: { name?: string; parentId?: string | null }): Promise<MediaFolder> =>
      request<MediaFolder>(`${folders(siteId)}/${encodeURIComponent(folderId)}`, { method: 'PATCH', body: JSON.stringify(change) }),

    remove: (siteId: string, folderId: string): Promise<void> =>
      request<void>(`${folders(siteId)}/${encodeURIComponent(folderId)}`, { method: 'DELETE' }),
  },

  storageCheck: {
    get: (): Promise<StorageStatus> => request<StorageStatus>('/media/storage-check'),
    start: (): Promise<StorageCheck> => request<StorageCheck>('/media/storage-check', { method: 'POST' }),
  },
}

/**
 * A resized admin preview of `file` at the smallest width of at least `minWidth`, or its
 * thumbnail when it has no variants (013 MV-16).
 */
export function variantUrl(file: Pick<MediaFile, 'variants' | 'thumbnailPath'>, minWidth: number): string {
  const variant = file.variants.find((v) => v.width >= minWidth) ?? file.variants.at(-1)
  return variant?.path ?? file.thumbnailPath
}

/**
 * The address visitors use: the admin page's own scheme, the site's first host name and the
 * file's public path (003 research M-21). The admin shell and the sites sit behind the same
 * proxy, so they share a scheme; the server never guesses either.
 */
export function publicUrl(file: MediaFile, hostName: string, protocol: string = window.location.protocol): string {
  return `${protocol}//${hostName}${file.publicPath}`
}
