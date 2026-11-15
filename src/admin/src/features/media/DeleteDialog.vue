<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { MediaFile } from '../../api/media'
import UsageList from './UsageList.vue'
import { restorableUntil, useTrashRetention } from '../trash/useTrashRetention'

// 003 US3: deletion is permanent (clarification Q2), so it is confirmed in a modal that
// says so. Focus moves in, stays in (Tab cycles within it), Escape cancels, and the caller
// returns focus to the row's Delete button.
//
// 013 US5 (FR-015): a file that pages or components use names them, and Delete stays off
// until the member confirms a second time that those places will show nothing instead. A
// file visitors can see needs a Designer or Administrator; the server enforces both.

const props = defineProps<{ siteId: string; file: MediaFile; busy: boolean }>()
const emit = defineEmits<{ confirm: [options: { confirmInUse: boolean }]; cancel: [] }>()

const { t, d } = useI18n()
// 021 FR-002: until when the file can be restored.
const retentionDays = useTrashRetention(props.siteId)
const dialog = ref<HTMLElement | null>(null)
const cancelButton = ref<HTMLButtonElement | null>(null)
const understood = ref(false)

const inUse = computed(() => props.file.usage.places > 0)
const blocked = computed(() => !props.file.canDelete)
const canConfirm = computed(() => !props.busy && !blocked.value && (!inUse.value || understood.value))

// A refusal that found new usage (a race) asks again.
watch(() => props.file.usage, () => {
  understood.value = false
})

onMounted(async () => {
  await nextTick()
  // The safe choice has focus first: Enter on an accidental open must not delete.
  cancelButton.value?.focus()
})

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
    return
  }

  if (event.key !== 'Tab' || !dialog.value) {
    return
  }

  const order = Array.from(dialog.value.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input:not([disabled])'))
  if (order.length === 0) {
    return
  }

  const index = order.indexOf(document.activeElement as HTMLElement)
  const next = event.shiftKey
    ? order[(index - 1 + order.length) % order.length]
    : order[(index + 1) % order.length]

  event.preventDefault()
  next?.focus()
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <div
      ref="dialog"
      class="card dialog"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="delete-media-title"
      aria-describedby="delete-media-body"
      @keydown="onKeydown"
    >
      <h2 id="delete-media-title">
        {{ t('media.delete.title', { name: file.fileName }) }}
      </h2>
      <p id="delete-media-body">
        {{ blocked ? t('media.errors.in_live_use') : inUse ? t('media.delete.inUseBody') : t('media.delete.body') }}
      </p>
      <p v-if="retentionDays && !blocked">
        {{ t('trash.restorableUntil', { date: d(restorableUntil(retentionDays), 'short') }) }}
      </p>

      <UsageList
        v-if="inUse"
        :site-id="siteId"
        :file="file"
      />

      <div
        v-if="inUse && !blocked"
        class="field field--checkbox"
      >
        <input
          id="delete-media-understood"
          v-model="understood"
          type="checkbox"
          :disabled="busy"
        >
        <label for="delete-media-understood">{{ t('media.delete.understood', { places: file.usage.places }, file.usage.places) }}</label>
      </div>

      <div class="actions">
        <button
          ref="cancelButton"
          type="button"
          :disabled="busy"
          @click="emit('cancel')"
        >
          {{ t('media.delete.cancel') }}
        </button>
        <button
          type="button"
          class="danger"
          :disabled="!canConfirm"
          @click="emit('confirm', { confirmInUse: inUse })"
        >
          {{ busy ? t('media.delete.deleting') : t('media.delete.confirm') }}
        </button>
      </div>
    </div>
  </div>
</template>
