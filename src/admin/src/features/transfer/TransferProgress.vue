<script setup lang="ts">
// A running export or import: what it is, the step, how far, who started it, and Cancel
// (020 contracts/admin-transfer.md §1, §5). Step changes and every 10 percent are announced
// politely; the end is announced assertively, so a screen reader user hears it however long
// it took.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ACTIVE_STATES, type Transfer } from '../../api/transfers'

const props = defineProps<{ transfer: Transfer; busy?: boolean }>()
const emit = defineEmits<{ cancel: [] }>()
const { t, locale } = useI18n()

const active = computed(() => ACTIVE_STATES.has(props.transfer.state))

const title = computed(() =>
  props.transfer.kind === 'export'
    ? t('transfer.progress.exportTitle')
    : t(`transfer.progress.importTitle.${props.transfer.mode ?? 'new_site'}`),
)

const step = computed(() => {
  const s = props.transfer.step
  if (props.transfer.state === 'queued' || props.transfer.state === 'confirmed') return t('transfer.progress.waiting')
  if (props.transfer.state === 'checking' && !s) return t('transfer.steps.checking')
  return s ? t(`transfer.steps.${s}`, s) : ''
})

const started = computed(() => {
  const when = new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(props.transfer.createdAtUtc))
  return props.transfer.startedBy
    ? t('transfer.progress.startedBy', { name: props.transfer.startedBy.displayName, when })
    : t('transfer.progress.startedByOperator', { when })
})

const outcome = computed(() => (active.value ? '' : t(`transfer.states.${props.transfer.state}`)))

// Announced politely when the step or the tens digit changes.
const progressAnnouncement = ref('')
watch(
  () => [step.value, Math.floor(props.transfer.percent / 10)] as const,
  ([s], previous) => {
    if (!active.value || (previous && previous[0] === s && previous[1] === Math.floor(props.transfer.percent / 10))) return
    progressAnnouncement.value = t('transfer.progress.announce', { step: s, percent: props.transfer.percent })
  },
)

function cancel(): void {
  if (window.confirm(t(props.transfer.kind === 'export' ? 'transfer.progress.confirmCancelExport' : 'transfer.progress.confirmCancelImport'))) {
    emit('cancel')
  }
}
</script>

<template>
  <section
    class="transfer-progress"
    :aria-labelledby="`transfer-${transfer.id}-title`"
  >
    <h3 :id="`transfer-${transfer.id}-title`">
      {{ title }}
    </h3>
    <p class="transfer-progress__meta">
      {{ started }}
    </p>

    <template v-if="active">
      <p class="transfer-progress__step">
        {{ step }}
      </p>
      <progress
        class="transfer-progress__bar"
        max="100"
        :value="transfer.percent"
        :aria-label="t('transfer.progress.label', { step })"
      >
        {{ transfer.percent }}%
      </progress>
      <span
        class="transfer-progress__percent"
        aria-hidden="true"
      >{{ transfer.percent }}%</span>
      <p
        v-if="transfer.cancelRequested"
        class="transfer-progress__note"
      >
        {{ t('transfer.progress.cancelling') }}
      </p>
      <button
        v-else
        type="button"
        :disabled="busy"
        @click="cancel"
      >
        {{ t('transfer.progress.cancel') }}
      </button>
    </template>

    <p
      class="visually-hidden"
      aria-live="polite"
    >
      {{ progressAnnouncement }}
    </p>
    <p
      class="transfer-progress__outcome"
      role="status"
      aria-live="assertive"
    >
      {{ outcome }}
    </p>
  </section>
</template>

<style scoped>
.transfer-progress {
  display: grid;
  gap: 0.5rem;
  padding: 1rem;
  border: 1px solid var(--border, #d0d4da);
  border-radius: 6px;
}

.transfer-progress h3,
.transfer-progress p {
  margin: 0;
}

.transfer-progress__meta {
  color: var(--text-muted, #555b63);
}

.transfer-progress__bar {
  width: 100%;
  height: 0.75rem;
}

.transfer-progress__percent {
  font-variant-numeric: tabular-nums;
}
</style>
