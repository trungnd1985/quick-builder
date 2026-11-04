// Typed client for site export and import (020 contracts/transfer-api.yaml).
//
// Every operation is for Administrators only; the server checks each one. Labels in check
// results and reports are titles and names of the archive's content, shown to the
// Administrator and nowhere else.

import { API_ROOT, ApiError, REQUEST_HEADER, refusal, request } from './request'

export type TransferKind = 'export' | 'import'
export type TransferTrigger = 'admin' | 'cli' | 'pre_restore'
export type ImportMode = 'new_site' | 'merge' | 'restore'
export type ConflictPolicy = 'replace' | 'skip' | 'rename'
export type TransferState =
  | 'uploading'
  | 'uploaded'
  | 'queued'
  | 'checking'
  | 'refused'
  | 'awaiting_confirmation'
  | 'confirmed'
  | 'running'
  | 'succeeded'
  | 'failed'
  | 'cancelled'
  | 'expired'

/** States a transfer does not leave (data-model §1.1). */
export const FINAL_STATES: ReadonlySet<TransferState> = new Set(['refused', 'succeeded', 'failed', 'cancelled', 'expired'])

/** States in which a worker is acting on the transfer, so progress moves. */
export const ACTIVE_STATES: ReadonlySet<TransferState> = new Set(['queued', 'checking', 'confirmed', 'running'])

export interface TransferSubject {
  kind: string
  id: string
  label: string
}

export interface CheckProblem {
  severity: 'error' | 'warning'
  code: string
  subject: TransferSubject | null
  detail: string
}

export interface CheckConflict {
  archive: TransferSubject
  targetId: string
  matchedBy: 'identity' | 'address' | 'name' | 'slug' | 'source' | 'content'
  clash: TransferSubject | null
}

/** data-model §3. Lists hold at most 1,000 entries; `totals` gives the full counts. */
export interface CheckResult {
  archive: {
    format: number
    release: string
    createdAtUtc: string
    source: { siteId: string; siteName: string; enabled: boolean }
    sameSiteAsTarget: boolean
  }
  versions: { contract: string; archiveVersion: number; currentVersion: number }[]
  counts: Record<string, number>
  problems: CheckProblem[]
  hostNamesLeftOut: string[]
  /** New site: the archive's host names the site will take. */
  hostNamesKept: string[]
  conflicts: CheckConflict[]
  schemaChanges: { collectionId: string; field: string; change: string; valuesConverted: number; valuesLost: number }[]
  removals: TransferSubject[]
  returns: TransferSubject[]
  changes: TransferSubject[]
  pastSchedules: { subject: TransferSubject; atUtc: string; action: string }[]
  allowedModes: ImportMode[]
  totals: Record<string, number>
}

/** FR-009. */
export interface ExportSummary {
  counts: Record<string, number>
  archiveBytes: number
  problems: { code: string; kind: string; id: string }[]
}

/** data-model §4. */
export interface ImportReport {
  mode: ImportMode
  startFresh: boolean
  policy: ConflictPolicy | null
  siteId: string
  imported: Record<string, number>
  resolved: { archive: TransferSubject; targetId: string; outcome: string; newLabel: string | null }[]
  leftOut: { subject: TransferSubject; reason: string }[]
  broken: { owner: TransferSubject; missingKind: string; missingId: string }[]
  droppedSchedules: { subject: TransferSubject; atUtc: string }[]
  removed: Record<string, number>
  preRestoreTransferId: string | null
  historicalAuthors: number
}

export interface Transfer {
  id: string
  kind: TransferKind
  trigger: TransferTrigger
  mode: ImportMode | null
  startFresh: boolean
  conflictPolicy: ConflictPolicy | null
  siteId: string | null
  newSiteName: string | null
  state: TransferState
  step: string | null
  percent: number
  cancelRequested: boolean
  archiveBytes: number | null
  archiveExpiresAtUtc: string | null
  downloadable: boolean
  checkResult: CheckResult | null
  checkFingerprint: string | null
  checkedAtUtc: string | null
  summary: ExportSummary | null
  report: ImportReport | null
  failureCode: string | null
  preRestoreTransferId: string | null
  startedBy: { id: string; displayName: string } | null
  createdAtUtc: string
  finishedAtUtc: string | null
}

export interface ConfirmOptions {
  newSiteName?: string
  /** New site: needed when every host name of the archive is taken here. */
  newSiteHostName?: string
  startFresh?: boolean
  conflictPolicy?: ConflictPolicy
  acceptWarnings?: boolean
  confirmSiteName?: string
  checkFingerprint?: string | null
}

/**
 * Streams an archive as the request body (research ST-17). XMLHttpRequest for upload
 * progress, as media uploads do (003 M-10), with the same proof-of-origin header and cookie.
 */
function sendArchive(url: string, body: Blob, onProgress: (fraction: number) => void): { done: Promise<void>; abort: () => void } {
  const xhr = new XMLHttpRequest()
  const done = new Promise<void>((resolve, reject) => {
    xhr.open('PUT', url)
    xhr.withCredentials = true
    xhr.setRequestHeader(REQUEST_HEADER, '1')
    xhr.setRequestHeader('Content-Type', 'application/zip')

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable && event.total > 0) {
        onProgress(event.loaded / event.total)
      }
    }

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        onProgress(1)
        resolve()
      } else {
        reject(refusal(xhr.status, xhr.statusText, xhr.responseText, xhr.getResponseHeader('Retry-After')))
      }
    }

    xhr.onerror = () => reject(new ApiError(xhr.statusText || 'Network error', 0))
    xhr.onabort = () => reject(new ApiError('Upload cancelled', 0))

    xhr.send(body)
  })

  return { done, abort: () => xhr.abort() }
}

export const transfersApi = {
  /** Queues an export of the site (FR-006). */
  startExport: (siteId: string): Promise<Transfer> =>
    request<Transfer>(`/sites/${siteId}/exports`, { method: 'POST' }),

  /** The site's transfers of the last 90 days, newest first. */
  list: (siteId: string): Promise<Transfer[]> => request<Transfer[]>(`/sites/${siteId}/transfers`),

  get: (transferId: string): Promise<Transfer> => request<Transfer>(`/transfers/${transferId}`),

  cancel: (transferId: string): Promise<Transfer> =>
    request<Transfer>(`/transfers/${transferId}/cancel`, { method: 'POST' }),

  /** Deletes the stored archive now (FR-008). */
  deleteArchive: (transferId: string): Promise<void> =>
    request<void>(`/transfers/${transferId}`, { method: 'DELETE' }),

  /** A plain link: the browser downloads it, resuming with ranges if interrupted. */
  downloadUrl: (transferId: string): string => `${API_ROOT}/transfers/${transferId}/archive`,

  /** Reserves an upload, then streams the archive into it. */
  async upload(file: Blob, onProgress: (fraction: number) => void): Promise<{ transfer: Transfer; abort: () => void; done: Promise<Transfer> }> {
    const transfer = await request<Transfer>('/transfers/uploads', { method: 'POST' })
    const sent = sendArchive(`${API_ROOT}/transfers/uploads/${transfer.id}`, file, onProgress)
    return {
      transfer,
      abort: sent.abort,
      done: sent.done.then(() => transfersApi.get(transfer.id)),
    }
  },

  /** Checks an uploaded archive against a target; poll the transfer for the result. */
  check: (transferId: string, mode: ImportMode, targetSiteId?: string): Promise<Transfer> =>
    request<Transfer>(`/transfers/${transferId}/check`, {
      method: 'POST',
      body: JSON.stringify({ mode, targetSiteId: targetSiteId ?? null }),
    }),

  confirm: (transferId: string, options: ConfirmOptions): Promise<Transfer> =>
    request<Transfer>(`/transfers/${transferId}/confirm`, { method: 'POST', body: JSON.stringify(options) }),
}
