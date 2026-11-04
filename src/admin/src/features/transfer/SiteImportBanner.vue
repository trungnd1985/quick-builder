<script setup lang="ts">
// 020 FR-028, contracts/admin-transfer.md §4: while an import applies to a site, every screen
// of that site says so, and its forms are read-only. When the import ends, it offers a reload,
// because what is on screen may no longer be what the site holds.
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { sitesApi, type SiteTransfer } from '../../api/sites'
import { setImportPaused } from './importLock'

const props = withDefaults(defineProps<{ pollMs?: number; idlePollMs?: number }>(), { pollMs: 5_000, idlePollMs: 30_000 })

const { t, locale } = useI18n()
const route = useRoute()

const siteId = computed(() => (typeof route.params.siteId === 'string' ? route.params.siteId : null))
const active = ref<SiteTransfer | null>(null)
const finished = ref(false)
let timer: ReturnType<typeof setTimeout> | null = null

const importing = computed(() => active.value?.kind === 'import' && (active.value.state === 'confirmed' || active.value.state === 'running'))
const since = computed(() =>
  active.value ? new Intl.DateTimeFormat(locale.value, { timeStyle: 'short' }).format(new Date(active.value.startedAtUtc)) : '',
)

function clear(): void {
  if (timer) clearTimeout(timer)
  timer = null
}

async function poll(id: string): Promise<void> {
  clear()
  try {
    const site = await sitesApi.get(id)
    if (id !== siteId.value) return
    const wasImporting = importing.value
    active.value = site.transfer ?? null
    if (wasImporting && !importing.value) finished.value = true
    setImportPaused(id, importing.value)
  } catch {
    // The next poll tries again; the server refuses writes either way.
  }

  if (id === siteId.value) timer = setTimeout(() => void poll(id), importing.value ? props.pollMs : props.idlePollMs)
}

watch(siteId, (next, previous) => {
  if (previous) setImportPaused(previous, false)
  active.value = null
  finished.value = false
  clear()
  if (next) void poll(next)
}, { immediate: true })

onBeforeUnmount(clear)

function reload(): void {
  window.location.reload()
}
</script>

<template>
  <div
    v-if="importing || finished"
    class="site-import-banner"
    role="status"
  >
    <p v-if="importing">
      {{ t('transfer.banner.importing', { when: since }) }}
    </p>
    <template v-else>
      <p>{{ t('transfer.banner.finished') }}</p>
      <button
        type="button"
        @click="reload"
      >
        {{ t('transfer.banner.reload') }}
      </button>
    </template>
  </div>
</template>

<style scoped>
.site-import-banner {
  display: flex;
  gap: 1rem;
  align-items: center;
  margin: 0;
  padding: 0.5rem 1rem;
  background: #dbeafe;
  color: #1e3a8a;
  border-bottom: 1px solid #3b82f6;
  font-weight: 600;
}

.site-import-banner p {
  margin: 0;
}
</style>
