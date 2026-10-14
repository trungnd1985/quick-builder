<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'

// A confirmation for site actions that change what visitors can reach — removing a host
// name, disabling or enabling a site (009 FR-019). Same behaviour as Media's delete
// dialog: focus moves in and stays in, the safe choice has focus first, Escape cancels,
// and the caller returns focus to the button that opened it.

defineProps<{
  dialogId: string
  title: string
  body: string
  confirmLabel: string
  cancelLabel: string
  busyLabel: string
  busy: boolean
  danger?: boolean
}>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()

const cancelButton = ref<HTMLButtonElement | null>(null)
const confirmButton = ref<HTMLButtonElement | null>(null)

onMounted(async () => {
  await nextTick()
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
      :aria-labelledby="`${dialogId}-title`"
      :aria-describedby="`${dialogId}-body`"
      @keydown="onKeydown"
    >
      <h2 :id="`${dialogId}-title`">
        {{ title }}
      </h2>
      <p :id="`${dialogId}-body`">
        {{ body }}
      </p>
      <slot />
      <div class="actions">
        <button
          ref="cancelButton"
          type="button"
          :disabled="busy"
          @click="emit('cancel')"
        >
          {{ cancelLabel }}
        </button>
        <button
          ref="confirmButton"
          type="button"
          :class="danger ? 'danger' : 'primary'"
          :disabled="busy"
          @click="emit('confirm')"
        >
          {{ busy ? busyLabel : confirmLabel }}
        </button>
      </div>
    </div>
  </div>
</template>
