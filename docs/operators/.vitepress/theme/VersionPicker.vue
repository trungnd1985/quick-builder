<script setup lang="ts">
// 026 contracts/docs-publishing.md §4. Reads /versions.json at the site root (one level above
// this minor line's base) and lists the published lines. Read offline, or from a build that was
// never published, the file is absent: the picker hides and the footer still says which release
// the pages describe. No request ever leaves the documentation's own host.
import { onMounted, ref } from 'vue'
import { useData, withBase } from 'vitepress'

declare const __QB_DOCS_RELEASE__: string

interface PublishedLine {
  line: string
  release: string
  published: string
}

const { site } = useData()
const lines = ref<PublishedLine[]>([])
const release = __QB_DOCS_RELEASE__

function current(): string {
  const parts = site.value.base.split('/').filter((p) => p.length > 0)
  return parts.length > 0 ? parts[parts.length - 1] : ''
}

onMounted(async () => {
  try {
    const response = await fetch(withBase('../versions.json'))
    if (response.ok) {
      lines.value = (await response.json()) as PublishedLine[]
    }
  } catch {
    lines.value = []
  }
})

function go(event: Event) {
  const line = (event.target as HTMLSelectElement).value
  window.location.href = withBase(`../${line}/`)
}
</script>

<template>
  <div v-if="lines.length > 0" class="qb-version">
    <label>
      <span class="visually-hidden">Documentation version</span>
      <select :value="current()" @change="go">
        <option v-for="entry in lines" :key="entry.line" :value="entry.line">
          {{ entry.line }} ({{ entry.release }})
        </option>
      </select>
    </label>
  </div>
  <div v-else class="qb-version qb-version--static" :title="`Built from QuickBuilder ${release}`">
    {{ release }}
  </div>
</template>

<style scoped>
.qb-version {
  display: flex;
  align-items: center;
  margin-left: 12px;
  font-size: 13px;
  color: var(--vp-c-text-2);
}

.qb-version select {
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  padding: 2px 6px;
  background: var(--vp-c-bg);
  color: var(--vp-c-text-1);
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}
</style>
