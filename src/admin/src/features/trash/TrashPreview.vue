<script setup lang="ts">
// A read-only look at a trashed entry before deciding to restore it (021 FR-011): who deleted
// it, and the text of its draft. Trashed content is deleted for the editor, so this reads the
// trash's own entry and never opens an editing session.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { entryOf, trashApi, type TrashEntry } from '../../api/trash'

const props = defineProps<{ siteId: string; entry: TrashEntry }>()
const emit = defineEmits<{ close: []; restore: [] }>()
const { t, d } = useI18n()

const draft = ref<unknown>(null)
const failed = ref(false)
const closeButton = ref<HTMLButtonElement | null>(null)

/** Every piece of text in a draft, in document order; ids and settings are not text. */
function textOf(value: unknown, key = ''): string[] {
  if (typeof value === 'string') {
    return ['id', 'type', 'kind', 'component', 'token', 'mediaId', 'schemaVersion'].includes(key) || value.length === 0 ? [] : [value]
  }

  if (Array.isArray(value)) return value.flatMap((v) => textOf(v))
  if (value && typeof value === 'object') return Object.entries(value).flatMap(([k, v]) => textOf(v, k))
  return []
}

const lines = computed(() => textOf(draft.value).slice(0, 200))

onMounted(async () => {
  try {
    const response = await trashApi.get(props.siteId, props.entry.kind, props.entry.id)
    entryOf(response)
    const text = 'draft' in response ? response.draft : null
    draft.value = text ? JSON.parse(text) : null
  } catch {
    failed.value = true
  }

  await nextTick()
  closeButton.value?.focus()
})

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      class="dialog-backdrop"
      @click.self="emit('close')"
    >
      <div
        class="card dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="trash-preview-title"
        @keydown="onKeydown"
      >
        <h2 id="trash-preview-title">
          {{ entry.label }}
        </h2>
        <p>{{ t('trash.preview.banner', { name: entry.trashedBy.displayName, date: d(new Date(entry.trashedAtUtc), 'short') }) }}</p>
        <p
          v-if="failed"
          role="alert"
        >
          {{ t('trash.preview.failed') }}
        </p>
        <ul v-else-if="lines.length > 0">
          <li
            v-for="(line, index) in lines"
            :key="index"
          >
            {{ line }}
          </li>
        </ul>
        <p v-else-if="entry.kind === 'file'">
          {{ entry.address }}
        </p>
        <p v-else>
          {{ t('trash.preview.empty') }}
        </p>
        <div class="dialog-actions">
          <button
            ref="closeButton"
            type="button"
            @click="emit('close')"
          >
            {{ t('trash.preview.close') }}
          </button>
          <button
            v-if="entry.canRestore"
            type="button"
            class="primary"
            @click="emit('restore')"
          >
            {{ t('trash.restoreAction') }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
