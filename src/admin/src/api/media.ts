// Typed client for the Media module (003 contracts/media-api.yaml).

import { API_ROOT, ApiError, REQUEST_HEADER, refusal, request } from './request'

export { ApiError } from './request'

export interface UserSummary {
  id: string
  displayName: string
}

export interface MediaFile {
  id: string
  siteId: string
  /** The original name, cleaned for display. Always render as text. */
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedAtUtc: string
  uploadedBy: UserSummary
  /** Path on the site's public surface, including any base path. */
  publicPath: string
  /** False when the stored bytes are missing. */
  available: boolean
  /** Whether this user may delete it. A courtesy: the server re-checks (003 M-18). */
  canDelete: boolean
}

export interface MediaFilePage {
  items: MediaFile[]
  totalCount: number
  nextCursor: string | null
}

/** Extensions the upload picker offers. The server decides from content regardless (003 M-07). */
export const ACCEPTED_EXTENSIONS = '.jpg,.jpeg,.png,.gif,.webp,.avif,.pdf'

const files = (siteId: string) => `/media/sites/${encodeURIComponent(siteId)}/files`

export const mediaApi = {
  list: (siteId: string, cursor?: string | null): Promise<MediaFilePage> =>
    request<MediaFilePage>(files(siteId) + (cursor ? `?cursor=${encodeURIComponent(cursor)}` : '')),

  remove: (siteId: string, fileId: string): Promise<void> =>
    request<void>(`${files(siteId)}/${encodeURIComponent(fileId)}`, { method: 'DELETE' }),

  /** Where the admin shell previews a file: authenticated, and independent of host names (FR-018). */
  previewUrl: (siteId: string, fileId: string): string =>
    `${API_ROOT}${files(siteId)}/${encodeURIComponent(fileId)}/content`,

  /**
   * Uploads one file as the raw request body (003 research M-10).
   *
   * XMLHttpRequest rather than fetch, because fetch still reports no upload progress. It
   * sends the same proof-of-origin header and cookie as every other call, and refusals go
   * through the same handling (sign-in, setup).
   */
  upload(siteId: string, file: File, onProgress: (fraction: number) => void): Promise<MediaFile> {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()
      xhr.open('POST', `${API_ROOT}${files(siteId)}`)
      xhr.withCredentials = true
      xhr.setRequestHeader(REQUEST_HEADER, '1')
      xhr.setRequestHeader('X-QuickBuilder-File-Name', encodeURIComponent(file.name))
      // Browsers leave type empty for extensions the OS does not know; the server treats
      // octet-stream as "not declared" (003 M-08).
      xhr.setRequestHeader('Content-Type', file.type || 'application/octet-stream')

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

      xhr.send(file)
    })
  },
}

/**
 * The address visitors use: the admin page's own scheme, the site's first host name and the
 * file's public path (003 research M-21). The admin shell and the sites sit behind the same
 * proxy, so they share a scheme; the server never guesses either.
 */
export function publicUrl(file: MediaFile, hostName: string, protocol: string = window.location.protocol): string {
  return `${protocol}//${hostName}${file.publicPath}`
}
