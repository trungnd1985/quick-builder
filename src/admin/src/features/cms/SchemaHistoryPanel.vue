<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { cmsApi, type HistoryChange, type HistoryEntry } from '../../api/cms'
import { cmsErrorMessage } from './cmsErrors'

// 014 US6, FR-033: a collection's saved revisions, newest first, each with who, when and what
// changed. The server stores the changes as kinds and counts, and they are worded here, so the
// history reads in whatever language the admin is shown in.

const props = defineProps<{ siteId: string; collectionId: string; revision: number }>()
const { t, te, d } = useI18n()

const entries = ref<HistoryEntry[]>([])
const nextBefore = ref<number | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

onMounted(() => load())
watch(() => props.revision, () => load())

async function load(before?: number): Promise<void> {
  loading.value = true
  error.value = null

  try {
    const page = await cmsApi.history(props.siteId, props.collectionId, before)
    entries.value = before ? [...entries.value, ...page.entries] : page.entries
    nextBefore.value = page.nextBefore
  } catch (caught) {
    error.value = cmsErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

function sentence(change: HistoryChange): string {
  const type = (name: string | null | undefined) => (name && te(`cms.types.${name}`) ? t(`cms.types.${name}`) : name ?? '')
  const kind = change.kind === 'requiredChanged' ? (change.to === 'required' ? 'requiredOn' : 'requiredOff') : change.kind
  const key = `cms.history.kind.${kind}`
  if (!te(key)) return change.kind

  return t(key, {
    name: change.name ?? '',
    from: change.kind === 'typeChanged' ? type(change.from) : change.from ?? '',
    to: change.kind === 'typeChanged' ? type(change.to) : change.to ?? '',
    setting: change.setting ?? '',
  })
}
</script>

<template>
  <section
    class="card schema-history"
    aria-labelledby="schema-history-heading"
  >
    <h2 id="schema-history-heading">
      {{ t('cms.history.heading') }}
    </h2>
    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-else-if="!loading && entries.length === 0"
      class="help"
    >
      {{ t('cms.history.empty') }}
    </p>
    <ol>
      <li
        v-for="entry in entries"
        :key="entry.revision"
      >
        <p class="help">
          {{ t('cms.history.entry', { revision: entry.revision, when: d(new Date(entry.changedAtUtc), 'short'), who: entry.changedBy.displayName }) }}
        </p>
        <ul>
          <li
            v-for="(change, index) in entry.changes"
            :key="index"
          >
            {{ sentence(change) }}
          </li>
        </ul>
      </li>
    </ol>
    <button
      v-if="nextBefore !== null"
      type="button"
      :disabled="loading"
      @click="load(nextBefore ?? undefined)"
    >
      {{ t('cms.history.loadMore') }}
    </button>
  </section>
</template>
