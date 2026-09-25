<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { mediaApi, type MediaFile } from '../../api/media'
import { useSitesStore } from '../../app/sitesStore'
import DeleteDialog from './DeleteDialog.vue'
import MediaList from './MediaList.vue'
import UploadArea from './UploadArea.vue'

// 003: one site's media library — upload (US1), browse, copy and delete (US3). Loading,
// error and empty are distinct states, as on every other screen (001 FR-045).

const { t } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const site = computed(() => sites.sites.find((s) => s.id === siteId.value))
const hostNames = computed(() => site.value?.hostNames ?? [])

const files = ref<MediaFile[]>([])
const totalCount = ref(0)
const nextCursor = ref<string | null>(null)
const loading = ref(true)
const loadingMore = ref(false)
const error = ref<string | null>(null)

const list = ref<InstanceType<typeof MediaList> | null>(null)
const pendingDelete = ref<MediaFile | null>(null)
const deleteTrigger = ref<HTMLElement | null>(null)
const deleting = ref(false)
const heading = ref<HTMLElement | null>(null)

onMounted(async () => {
  if (sites.sites.length === 0) {
    await sites.load()
  }

  await loadFirstPage()
})

async function loadFirstPage(): Promise<void> {
  loading.value = true
  error.value = null

  try {
    const page = await mediaApi.list(siteId.value)
    files.value = page.items
    totalCount.value = page.totalCount
    nextCursor.value = page.nextCursor
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : String(caught)
  } finally {
    loading.value = false
  }
}

async function loadMore(): Promise<void> {
  if (!nextCursor.value) {
    return
  }

  loadingMore.value = true
  try {
    const page = await mediaApi.list(siteId.value, nextCursor.value)
    // Keyset paging: files uploaded meanwhile sit before the cursor, so nothing repeats.
    const known = new Set(files.value.map((f) => f.id))
    const added = page.items.filter((f) => !known.has(f.id))
    files.value = [...files.value, ...added]
    totalCount.value = page.totalCount
    nextCursor.value = page.nextCursor

    const first = added[0]
    if (first) {
      await list.value?.focusRow(first.id)
    }
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : String(caught)
  } finally {
    loadingMore.value = false
  }
}

function onUploaded(file: MediaFile): void {
  files.value = [file, ...files.value]
  totalCount.value += 1
}

function askToDelete(file: MediaFile, trigger: HTMLElement): void {
  pendingDelete.value = file
  deleteTrigger.value = trigger
}

async function closeDialog(): Promise<void> {
  pendingDelete.value = null
  deleting.value = false
  await nextTick()
  deleteTrigger.value?.focus()
}

async function confirmDelete(): Promise<void> {
  const file = pendingDelete.value
  if (!file) {
    return
  }

  deleting.value = true
  try {
    await mediaApi.remove(siteId.value, file.id)
    files.value = files.value.filter((f) => f.id !== file.id)
    totalCount.value = Math.max(0, totalCount.value - 1)
    list.value?.announce(t('media.delete.done', { name: file.fileName }))

    // The trigger row is gone, so focus returns to the page heading instead.
    deleteTrigger.value = null
    pendingDelete.value = null
    deleting.value = false
    await nextTick()
    heading.value?.focus()
  } catch {
    list.value?.announce(t('media.delete.failed', { name: file.fileName }))
    await closeDialog()
  }
}
</script>

<template>
  <section aria-labelledby="media-heading">
    <header class="page-header">
      <div>
        <h1
          id="media-heading"
          ref="heading"
          tabindex="-1"
        >
          {{ t('media.title', { site: site?.name ?? '' }) }}
        </h1>
        <p class="subtitle">
          {{ t('media.subtitle') }}
        </p>
      </div>
      <RouterLink :to="{ name: 'sites' }">
        {{ t('media.back') }}
      </RouterLink>
    </header>

    <p
      v-if="site && hostNames.length === 0"
      class="card notice"
    >
      {{ t('media.noHost') }}
    </p>

    <UploadArea
      :site-id="siteId"
      @uploaded="onUploaded"
    />

    <p
      v-if="loading"
      class="status"
      role="status"
    >
      {{ t('media.loading') }}
    </p>

    <div
      v-else-if="error"
      class="card error-card"
      role="alert"
    >
      <h2>{{ t('media.error.title') }}</h2>
      <p>{{ error }}</p>
      <button
        type="button"
        @click="loadFirstPage()"
      >
        {{ t('media.error.retry') }}
      </button>
    </div>

    <div
      v-else-if="files.length === 0"
      class="card empty"
    >
      <h2>{{ t('media.empty.title') }}</h2>
      <p>{{ t('media.empty.body') }}</p>
    </div>

    <template v-else>
      <p class="help">
        {{ t('media.count', { shown: files.length, total: totalCount }) }}
      </p>

      <MediaList
        ref="list"
        :site-id="siteId"
        :files="files"
        :host-names="hostNames"
        @delete="askToDelete"
      />

      <div
        v-if="nextCursor"
        class="actions"
      >
        <button
          type="button"
          :disabled="loadingMore"
          @click="loadMore"
        >
          {{ t('media.loadMore') }}
        </button>
      </div>
    </template>

    <DeleteDialog
      v-if="pendingDelete"
      :file="pendingDelete"
      :busy="deleting"
      @cancel="closeDialog"
      @confirm="confirmDelete"
    />
  </section>
</template>
