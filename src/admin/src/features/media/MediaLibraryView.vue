<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { mediaApi, type MediaFile } from '../../api/media'
import { useSitesStore } from '../../app/sitesStore'
import MediaBrowser from './MediaBrowser.vue'
import MediaDetailsPanel from './MediaDetailsPanel.vue'

// 003 and 013: one site's media library — folders, search, upload, browse, copy and delete
// (MediaBrowser), and a file's details with its alternative text (MediaDetailsPanel).

const { t } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const site = computed(() => sites.sites.find((s) => s.id === siteId.value))
const hostNames = computed(() => site.value?.hostNames ?? [])

const heading = ref<HTMLElement | null>(null)
const browser = ref<InstanceType<typeof MediaBrowser> | null>(null)
const details = ref<MediaFile | null>(null)
const detailsTrigger = ref<HTMLElement | null>(null)

onMounted(async () => {
  if (sites.sites.length === 0) {
    await sites.load()
  }
})

function openDetails(file: MediaFile, trigger: HTMLElement): void {
  details.value = file
  detailsTrigger.value = trigger
}

async function closeDetails(): Promise<void> {
  details.value = null
  await nextTick()
  detailsTrigger.value?.focus()
}

function onChanged(file: MediaFile): void {
  details.value = file
  browser.value?.update(file)
}

async function reloadDetails(): Promise<void> {
  if (!details.value) return
  const fresh = await mediaApi.get(siteId.value, details.value.id)
  onChanged(fresh)
}

async function onDeleted(file: MediaFile): Promise<void> {
  if (details.value?.id === file.id) {
    details.value = null
  }

  // The trigger row is gone, so focus returns to the page heading instead.
  await nextTick()
  heading.value?.focus()
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

    <div class="media-library">
      <MediaBrowser
        ref="browser"
        :site-id="siteId"
        :host-names="hostNames"
        mode="library"
        @details="openDetails"
        @deleted="onDeleted"
      />

      <MediaDetailsPanel
        v-if="details"
        :key="details.id"
        :site-id="siteId"
        :file="details"
        :can-edit="true"
        @close="closeDetails"
        @changed="onChanged"
        @reload="reloadDetails"
      />
    </div>
  </section>
</template>
