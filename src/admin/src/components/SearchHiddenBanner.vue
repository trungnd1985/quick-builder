<script setup lang="ts">
// 017 FR-027, research SE-17: while a site discourages search engines, every screen of that
// site says so, for every member, so nobody launches a site still hidden from search.
import { computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useSitesStore } from '../app/sitesStore'

const { t } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const site = computed(() => {
  const id = route.params.siteId
  return typeof id === 'string' ? sites.sites.find((s) => s.id === id) ?? null : null
})

const hidden = computed(() => site.value?.searchVisibility?.discourageSearchEngines === true)

onMounted(() => {
  if (typeof route.params.siteId === 'string' && sites.sites.length === 0 && !sites.loading) void sites.load()
})
</script>

<template>
  <p
    v-if="hidden"
    class="search-hidden-banner"
    role="status"
  >
    {{ t('sites.searchVisibility.hiddenBanner', { name: site!.name }) }}
  </p>
</template>

<style scoped>
.search-hidden-banner {
  margin: 0;
  padding: 0.5rem 1rem;
  background: #fef3c7;
  color: #78350f;
  border-bottom: 1px solid #f59e0b;
  font-weight: 600;
}
</style>
