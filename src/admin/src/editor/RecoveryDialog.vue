<script setup lang="ts">
// Unsaved changes from an earlier visit (011 clarification Q5, US3 #8). Offered, never
// applied without asking: Restore when the saved draft has not changed since, else only
// read-only copying; Discard either way.
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { RecoveryOffer } from './state/recovery'

const props = defineProps<{ offer: RecoveryOffer }>()
const emit = defineEmits<{ restore: []; readOnly: []; discard: [] }>()
const { t, locale } = useI18n()

const first = ref<HTMLButtonElement | null>(null)
onMounted(() => first.value?.focus())

const when = props.offer.record.savedAtUtc
  ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(props.offer.record.savedAtUtc))
  : ''
</script>

<template>
  <div
    class="editor-recovery"
    role="alertdialog"
    aria-labelledby="editor-recovery-title"
    aria-describedby="editor-recovery-body"
  >
    <h2 id="editor-recovery-title">
      {{ t('editor.recovery.title') }}
    </h2>
    <p id="editor-recovery-body">
      {{ offer.kind === 'restore' ? t('editor.recovery.restoreBody', { when }) : t('editor.recovery.readOnlyBody', { when }) }}
    </p>
    <div class="editor-recovery__actions">
      <button
        v-if="offer.kind === 'restore'"
        ref="first"
        type="button"
        class="primary"
        @click="emit('restore')"
      >
        {{ t('editor.recovery.restore') }}
      </button>
      <button
        v-else
        ref="first"
        type="button"
        class="primary"
        @click="emit('readOnly')"
      >
        {{ t('editor.recovery.openReadOnly') }}
      </button>
      <button
        type="button"
        @click="emit('discard')"
      >
        {{ t('editor.recovery.discard') }}
      </button>
    </div>
  </div>
</template>
