<script setup lang="ts">
// Emptying the trash (021 FR-021, research TR-14, contracts/admin-trash.md §5). The counts are
// loaded first and remembered with the moment they were read: only entries trashed by then go,
// and only while the counts still hold, so nothing deleted after the dialog opened is lost. The
// Administrator types the total to confirm. Pages, Cms and Media are emptied in turn.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError } from '../../api/request'
import { trashPurgeApi, type TrashCounts } from '../../api/trash'

const props = defineProps<{ siteId: string }>()
const emit = defineEmits<{ emptied: [removed: number]; cancel: [] }>()
const { t } = useI18n()

const counts = ref<TrashCounts | null>(null)
const trashedUpTo = ref('')
const typed = ref('')
const busy = ref(false)
const error = ref<string | null>(null)
const changed = ref(false)
const input = ref<HTMLInputElement | null>(null)

const total = computed(() => (counts.value ? counts.value.page + counts.value.component + counts.value.item + counts.value.file : 0))
const confirmed = computed(() => counts.value !== null && total.value > 0 && typed.value.trim() === String(total.value))

async function loadCounts(): Promise<void> {
  trashedUpTo.value = new Date().toISOString()
  counts.value = await trashPurgeApi.counts(props.siteId, trashedUpTo.value)
  typed.value = ''
}

onMounted(async () => {
  await loadCounts()
  await nextTick()
  input.value?.focus()
})

async function empty(): Promise<void> {
  if (!counts.value || !confirmed.value) return
  busy.value = true
  error.value = null
  let removed = 0
  try {
    if (counts.value.page + counts.value.component > 0) {
      const result = await trashPurgeApi.empty(props.siteId, 'pages', trashedUpTo.value, counts.value)
      removed += (result.pages ?? 0) + (result.components ?? 0)
    }
    if (counts.value.item > 0) {
      removed += (await trashPurgeApi.empty(props.siteId, 'cms', trashedUpTo.value, counts.value)).items ?? 0
    }
    if (counts.value.file > 0) {
      removed += (await trashPurgeApi.empty(props.siteId, 'media', trashedUpTo.value, counts.value)).files ?? 0
    }
    emit('emptied', removed)
  } catch (caught) {
    if (caught instanceof ApiError && caught.errorCode === 'trash.count_changed') {
      // FR-021: the trash changed since the counts were shown; show the new ones and ask again.
      changed.value = true
      await loadCounts()
    } else {
      error.value = t('trash.emptyTrash.failed')
    }
  } finally {
    busy.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      class="dialog-backdrop"
      @click.self="emit('cancel')"
    >
      <div
        class="card dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="empty-trash-title"
        aria-describedby="empty-trash-body"
        @keydown="onKeydown"
      >
        <h2 id="empty-trash-title">
          {{ t('trash.emptyTrash.title') }}
        </h2>
        <div id="empty-trash-body">
          <p
            v-if="changed"
            role="status"
          >
            {{ t('trash.emptyTrash.changed') }}
          </p>
          <p v-if="counts">
            {{ t('trash.emptyTrash.body', { total }) }}
          </p>
          <p v-else>
            {{ t('trash.loading') }}
          </p>
        </div>

        <label for="empty-trash-count">{{ t('trash.emptyTrash.typeCount', { total }) }}</label>
        <input
          id="empty-trash-count"
          ref="input"
          v-model="typed"
          inputmode="numeric"
          autocomplete="off"
        >

        <p
          v-if="error"
          class="field-error"
          role="alert"
        >
          {{ error }}
        </p>

        <div class="dialog-actions">
          <button
            type="button"
            @click="emit('cancel')"
          >
            {{ t('trash.restore.cancel') }}
          </button>
          <button
            type="button"
            class="danger"
            :disabled="busy || !confirmed"
            @click="empty"
          >
            {{ t('trash.emptyTrash.confirm') }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
