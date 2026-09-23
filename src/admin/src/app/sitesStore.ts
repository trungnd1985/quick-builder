import { defineStore } from 'pinia'
import { ref } from 'vue'
import { ApiError, sitesApi, type Site } from '../api/sites'

/**
 * Sites state for the admin shell.
 *
 * Models three states explicitly — loading, error and loaded — rather than inferring them
 * from whether the array happens to be empty. That distinction is the whole of FR-045:
 * an empty list on a fresh install and a failed request look identical if you only track
 * the data, and showing "no sites yet" when the server is actually down is worse than
 * showing nothing.
 */
export const useSitesStore = defineStore('sites', () => {
  const sites = ref<Site[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function load(): Promise<void> {
    loading.value = true
    error.value = null

    try {
      sites.value = await sitesApi.list()
    } catch (caught) {
      error.value = caught instanceof Error ? caught.message : String(caught)
    } finally {
      loading.value = false
    }
  }

  /** Creates a site. Returns field errors on a validation failure rather than throwing. */
  async function create(
    name: string,
    hostName: string,
  ): Promise<{ ok: true } | { ok: false; message: string; fields: Record<string, string[]> }> {
    try {
      const created = await sitesApi.create(name, [hostName])
      sites.value = [created, ...sites.value]
      return { ok: true }
    } catch (caught) {
      if (caught instanceof ApiError) {
        return { ok: false, message: caught.message, fields: caught.fieldErrors }
      }
      return {
        ok: false,
        message: caught instanceof Error ? caught.message : String(caught),
        fields: {},
      }
    }
  }

  return { sites, loading, error, load, create }
})
