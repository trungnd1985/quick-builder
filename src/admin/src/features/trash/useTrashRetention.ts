// Until when deleted content can be restored (021 FR-002): every delete dialog says so before
// the member confirms. The retention period is the operator's (clarification Q3) and comes with
// the single-site read; it is read once per site and kept for the session.
import { ref, type Ref } from 'vue'
import { sitesApi } from '../../api/sites'

const cache = new Map<string, Promise<number | null>>()

/** The site's retention period in days, or null until it is known (or when it cannot be read). */
export function useTrashRetention(siteId: string): Ref<number | null> {
  const days = ref<number | null>(null)
  let pending = cache.get(siteId)
  if (!pending) {
    pending = sitesApi
      .get(siteId)
      .then((site) => site.trashRetentionDays ?? null)
      .catch(() => null)
    cache.set(siteId, pending)
  }

  void pending.then((value) => {
    days.value = value
  })
  return days
}

/** The date content deleted now can be restored until, formatted by the caller. */
export function restorableUntil(days: number, now: Date = new Date()): Date {
  return new Date(now.getTime() + days * 24 * 60 * 60 * 1000)
}
