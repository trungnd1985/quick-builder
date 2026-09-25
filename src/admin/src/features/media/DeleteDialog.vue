<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { MediaFile } from '../../api/media'

// 003 US3: deletion is permanent (clarification Q2), so it is confirmed in a modal that
// says so. Focus moves in, stays in (Tab cycles between the two buttons), Escape cancels,
// and the caller returns focus to the row's Delete button.

defineProps<{ file: MediaFile; busy: boolean }>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()

const { t } = useI18n()
const cancelButton = ref<HTMLButtonElement | null>(null)
const confirmButton = ref<HTMLButtonElement | null>(null)

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

  if (event.key !== 'Tab') {
    return
  }

  const order = [cancelButton.value, confirmButton.value].filter((b): b is HTMLButtonElement => b !== null && !b.disabled)
  if (order.length === 0) {
    return
  }

  const index = order.indexOf(document.activeElement as HTMLButtonElement)
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
        {{ t('media.delete.body') }}
      </p>
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
          ref="confirmButton"
          type="button"
          class="danger"
          :disabled="busy"
          @click="emit('confirm')"
        >
          {{ busy ? t('media.delete.deleting') : t('media.delete.confirm') }}
        </button>
      </div>
    </div>
  </div>
</template>
