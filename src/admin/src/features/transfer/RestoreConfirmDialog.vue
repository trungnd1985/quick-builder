<script setup lang="ts">
// 020 FR-027b, contracts/admin-transfer.md §1 and §5: before a restore, the Administrator sees
// what it removes, brings back and changes, learns that the site as it is now is exported
// first, and types the site's name. Focus moves in, stays in, and Escape cancels; the caller
// returns focus to the button that opened it.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CheckResult, TransferSubject } from '../../api/transfers'

const props = defineProps<{ result: CheckResult; siteName: string; busy: boolean }>()
const emit = defineEmits<{ confirm: [typedName: string]; cancel: [] }>()

const { t } = useI18n()
const typed = ref('')
const touched = ref(false)
const dialog = ref<HTMLElement | null>(null)
const nameInput = ref<HTMLInputElement | null>(null)

const matches = computed(() => typed.value === props.siteName)
const groups = computed(() =>
  [
    { key: 'removals', items: props.result.removals, total: props.result.totals.removals ?? props.result.removals.length },
    { key: 'returns', items: props.result.returns, total: props.result.totals.returns ?? props.result.returns.length },
    { key: 'changes', items: props.result.changes, total: props.result.totals.changes ?? props.result.changes.length },
  ] as { key: 'removals' | 'returns' | 'changes'; items: TransferSubject[]; total: number }[],
)

onMounted(async () => {
  await nextTick()
  nameInput.value?.focus()
})

function submit(): void {
  touched.value = true
  if (matches.value && !props.busy) emit('confirm', typed.value)
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
    return
  }

  if (event.key !== 'Tab' || !dialog.value) return
  const focusable = [...dialog.value.querySelectorAll<HTMLElement>('summary, input, button:not([disabled])')]
  if (focusable.length === 0) return
  const index = focusable.indexOf(document.activeElement as HTMLElement)
  const next = event.shiftKey
    ? focusable[(index - 1 + focusable.length) % focusable.length]
    : focusable[(index + 1) % focusable.length]
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
      class="card dialog restore-dialog"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="restore-title"
      aria-describedby="restore-body"
      @keydown="onKeydown"
    >
      <h2 id="restore-title">
        {{ t('transfer.restore.title', { name: siteName }) }}
      </h2>
      <p id="restore-body">
        {{ t('transfer.restore.body') }}
      </p>

      <details
        v-for="group in groups"
        :key="group.key"
      >
        <summary>{{ t(`transfer.restore.${group.key}`, { count: group.total }, group.total) }}</summary>
        <ul v-if="group.items.length">
          <li
            v-for="item in group.items"
            :key="`${item.kind}-${item.id}`"
          >
            {{ item.label }}
          </li>
        </ul>
      </details>

      <ul
        v-if="result.schemaChanges.length"
        class="restore-dialog__fields"
      >
        <li
          v-for="(change, i) in result.schemaChanges"
          :key="`f${i}`"
        >
          {{ t('transfer.restore.fieldChange', { field: change.field, lost: change.valuesLost }) }}
        </li>
      </ul>

      <p>{{ t('transfer.restore.backupFirst') }}</p>

      <form
        class="restore-dialog__form"
        @submit.prevent="submit"
      >
        <label for="restore-name">{{ t('transfer.restore.typeName', { name: siteName }) }}</label>
        <input
          id="restore-name"
          ref="nameInput"
          v-model="typed"
          type="text"
          autocomplete="off"
          :aria-invalid="touched && !matches"
          :aria-describedby="touched && !matches ? 'restore-name-error' : undefined"
        >
        <p
          v-if="touched && !matches"
          id="restore-name-error"
          class="error"
        >
          {{ t('transfer.restore.nameMismatch') }}
        </p>
        <div class="actions">
          <button
            type="button"
            :disabled="busy"
            @click="emit('cancel')"
          >
            {{ t('transfer.restore.cancel') }}
          </button>
          <button
            type="submit"
            class="danger"
            :disabled="busy"
          >
            {{ busy ? t('transfer.restore.restoring') : t('transfer.restore.confirm') }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<style scoped>
.restore-dialog {
  display: grid;
  gap: 0.75rem;
  max-width: 36rem;
}

.restore-dialog__form {
  display: grid;
  gap: 0.5rem;
}
</style>
