// 020 FR-028, contracts/admin-transfer.md §4: which sites an import is applying to right now.
//
// While a site is listed, the server refuses its content writes with 423
// `site.import_in_progress`, so the item form turns read-only and the banner says why. The
// banner fills the list from the site's `transfer` field; a refused write adds to it too.

import { reactive } from 'vue'
import { ApiError } from '../../api/request'

const paused = reactive(new Set<string>())

export const IMPORT_IN_PROGRESS = 'site.import_in_progress'

/** Whether an import is applying to the site, as far as this tab knows. */
export function isImportPaused(siteId: string | null | undefined): boolean {
  return !!siteId && paused.has(siteId)
}

export function setImportPaused(siteId: string, value: boolean): void {
  if (value) paused.add(siteId)
  else paused.delete(siteId)
}

/** A write refused because an import holds the site. */
export function isImportInProgress(error: unknown): boolean {
  return error instanceof ApiError && error.status === 423 && error.errorCode === IMPORT_IN_PROGRESS
}
