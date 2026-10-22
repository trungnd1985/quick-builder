import { onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'
import { formsApi } from '../../api/forms'

/** How often the count is refreshed while the admin is in view (018 FS-21). */
export const UNREAD_REFRESH_MS = 60_000

/** The server caps the count; at the cap the badge says "more than 999". */
export const UNREAD_CAP = 1000

/**
 * A site's unread submissions, for the badge beside **Submissions** (018 FS-21). Loaded when the
 * component mounts and when the site changes, then every minute while the page is visible; a
 * hidden tab does not poll. A refused or failed call leaves the badge empty rather than wrong.
 */
export function useUnreadCount(siteId: Ref<string | null | undefined>): { count: Ref<number> } {
  const count = ref(0)
  let timer: ReturnType<typeof setInterval> | undefined

  async function refresh(): Promise<void> {
    if (!siteId.value || document.visibilityState === 'hidden') return
    try {
      count.value = (await formsApi.unreadCount(siteId.value)).count
    } catch {
      count.value = 0
    }
  }

  const onVisibility = () => {
    if (document.visibilityState === 'visible') void refresh()
  }

  onMounted(() => {
    void refresh()
    timer = setInterval(() => void refresh(), UNREAD_REFRESH_MS)
    document.addEventListener('visibilitychange', onVisibility)
  })

  onBeforeUnmount(() => {
    clearInterval(timer)
    document.removeEventListener('visibilitychange', onVisibility)
  })

  watch(siteId, () => void refresh())

  return { count }
}
