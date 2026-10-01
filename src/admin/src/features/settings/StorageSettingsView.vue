<script setup lang="ts">
// 013 MV-21: the configured storage provider and the consistency check, for Administrators.
// After media has been copied between providers, the check lists the library files whose
// originals the provider does not have. Ids link to the file in its site's library; file
// names are never part of the check.
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, mediaApi, type StorageStatus } from '../../api/media'
import { useSitesStore } from '../../app/sitesStore'

/** How often a running check is polled. */
const POLL_MS = 5000

const { t, d } = useI18n()
const sites = useSitesStore()

const status = ref<StorageStatus | null>(null)
const loading = ref(true)
const starting = ref(false)
const error = ref<string | null>(null)
let timer: ReturnType<typeof setTimeout> | null = null

const latest = computed(() => status.value?.latestCheck ?? null)
const running = computed(() => latest.value?.status === 'running')
const listedMissing = computed(() => latest.value?.missing ?? [])

function siteName(siteId: string): string {
  return sites.sites.find((s) => s.id === siteId)?.name ?? siteId
}

function schedule(): void {
  if (timer) clearTimeout(timer)
  timer = running.value ? setTimeout(() => void load(), POLL_MS) : null
}

async function load(): Promise<void> {
  try {
    status.value = await mediaApi.storageCheck.get()
    error.value = null
  } catch {
    error.value = t('storageSettings.errors.load')
  } finally {
    loading.value = false
    schedule()
  }
}

async function start(): Promise<void> {
  starting.value = true
  error.value = null
  try {
    const check = await mediaApi.storageCheck.start()
    status.value = { provider: status.value?.provider ?? check.provider, latestCheck: { ...check, missing: [] } }
  } catch (caught) {
    error.value = caught instanceof ApiError && caught.errorCode === 'media.storage_check_running'
      ? t('storageSettings.errors.running')
      : t('storageSettings.errors.start')
    await load()
  } finally {
    starting.value = false
    schedule()
  }
}

onMounted(async () => {
  if (sites.sites.length === 0) await sites.load().catch(() => undefined)
  await load()
})

onBeforeUnmount(() => {
  if (timer) clearTimeout(timer)
})
</script>

<template>
  <section aria-labelledby="storage-heading">
    <header class="page-header">
      <div>
        <h1 id="storage-heading">
          {{ t('storageSettings.title') }}
        </h1>
        <p class="subtitle">
          {{ t('storageSettings.intro') }}
        </p>
      </div>
      <button
        type="button"
        class="primary"
        :disabled="loading || running || starting"
        @click="start"
      >
        {{ running ? t('storageSettings.running') : t('storageSettings.run') }}
      </button>
    </header>

    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>

    <dl
      v-if="status"
      class="card"
    >
      <dt>{{ t('storageSettings.provider') }}</dt>
      <dd>{{ t(`storageSettings.providers.${status.provider}`) }}</dd>
    </dl>

    <p
      v-if="status && !latest"
      class="card"
    >
      {{ t('storageSettings.empty') }}
    </p>

    <div
      v-if="latest"
      class="card"
      aria-live="polite"
    >
      <h2>{{ t('storageSettings.latest') }}</h2>
      <dl>
        <dt>{{ t('storageSettings.status') }}</dt>
        <dd>{{ t(`storageSettings.statuses.${latest.status}`) }}</dd>
        <dt>{{ t('storageSettings.started') }}</dt>
        <dd>{{ d(new Date(latest.startedAtUtc), 'short') }} — {{ t(`storageSettings.triggers.${latest.trigger}`) }}</dd>
        <template v-if="latest.finishedAtUtc">
          <dt>{{ t('storageSettings.finished') }}</dt>
          <dd>{{ d(new Date(latest.finishedAtUtc), 'short') }}</dd>
        </template>
        <dt>{{ t('storageSettings.checked') }}</dt>
        <dd>{{ latest.filesChecked }}</dd>
        <dt>{{ t('storageSettings.missingCount') }}</dt>
        <dd>{{ latest.missingCount }}</dd>
      </dl>

      <p v-if="latest.status === 'completed' && latest.missingCount === 0">
        {{ t('storageSettings.allPresent') }}
      </p>
    </div>

    <template v-if="listedMissing.length > 0">
      <h2>{{ t('storageSettings.missingHeading') }}</h2>
      <p
        v-if="latest && latest.missingCount > listedMissing.length"
        class="subtitle"
      >
        {{ t('storageSettings.capped', { shown: listedMissing.length, total: latest.missingCount }) }}
      </p>
      <table class="sites-table">
        <caption class="visually-hidden">
          {{ t('storageSettings.missingHeading') }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              {{ t('storageSettings.columns.site') }}
            </th>
            <th scope="col">
              {{ t('storageSettings.columns.file') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in listedMissing"
            :key="`${item.siteId}/${item.fileId}`"
          >
            <th scope="row">
              {{ siteName(item.siteId) }}
            </th>
            <td>
              <RouterLink :to="{ name: 'site-media', params: { siteId: item.siteId }, query: { file: item.fileId } }">
                {{ item.fileId }}
              </RouterLink>
            </td>
          </tr>
        </tbody>
      </table>
    </template>
  </section>
</template>
