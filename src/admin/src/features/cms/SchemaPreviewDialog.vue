<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Outcome, Preview, PreviewChange } from '../../api/cms'

// 014 US3, US4, FR-023: what a save does to the stored items, before it happens — counts per
// change, up to ten examples by title, and, when values would be cleared or shortened, the
// warning that this cannot be undone (clarification Q2). Confirming sends the preview's digest,
// so the server can tell whether the items changed since (research CS-08).

const props = defineProps<{ preview: Preview; saving: boolean }>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()
const { t } = useI18n()

const OUTCOMES: Outcome[] = ['cleared', 'partlyLost', 'nowInvalid', 'stillMissing', 'missing', 'filled', 'converted', 'kept']
const DESCRIBED = new Set(['fieldAdded', 'fieldRemoved', 'requiredChanged', 'typeChanged', 'settingsChanged', 'optionRemoved'])

const changes = computed(() => props.preview.changes.filter((c) => c.rule !== 'free'))
const mentionsDates = computed(() =>
  changes.value.some((c) => c.setting === 'withTime' || c.from === 'date' || c.to === 'date'),
)
const cancelButton = ref<HTMLButtonElement | null>(null)

onMounted(async () => {
  await nextTick()
  cancelButton.value?.focus()
})

function describe(change: PreviewChange): string {
  const type = (name: string | null) => (name ? t(`cms.types.${name}`) : '')
  const kind = DESCRIBED.has(change.kind) ? change.kind : 'other'
  return t(`cms.preview.kind.${kind}`, {
    name: change.name ?? '',
    from: change.kind === 'typeChanged' ? type(change.from) : change.from ?? '',
    to: change.kind === 'typeChanged' ? type(change.to) : change.to ?? '',
    setting: change.setting ?? '',
  })
}

function counts(change: PreviewChange): { outcome: Outcome; count: number }[] {
  return OUTCOMES.filter((o) => (change.counts[o] ?? 0) > 0).map((o) => ({ outcome: o, count: change.counts[o] ?? 0 }))
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <div
      class="card dialog dialog--wide"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="schema-preview-title"
      aria-describedby="schema-preview-intro"
      @keydown.esc.prevent="emit('cancel')"
    >
      <h2 id="schema-preview-title">
        {{ t('cms.preview.title') }}
      </h2>
      <p id="schema-preview-intro">
        {{ t('cms.preview.intro', { count: preview.itemCount }, preview.itemCount) }}
      </p>

      <ul class="schema-preview-changes">
        <li
          v-for="(change, index) in changes"
          :key="index"
        >
          <p>
            <strong>{{ describe(change) }}</strong>
            <span
              v-if="change.rule !== 'forbidden'"
              class="help"
            > {{ t(`cms.preview.rule.${change.rule}`) }}</span>
          </p>
          <ul v-if="counts(change).length > 0">
            <li
              v-for="entry in counts(change)"
              :key="entry.outcome"
            >
              {{ t(`cms.preview.outcome.${entry.outcome}`, { count: entry.count }) }}
            </li>
          </ul>
          <p
            v-if="change.fieldId && (preview.boundPages?.[change.fieldId] ?? 0) > 0 && (change.kind === 'fieldRemoved' || change.kind === 'typeChanged')"
            class="warning"
            role="note"
          >
            {{ t('cms.preview.boundPages', { count: preview.boundPages![change.fieldId] }, preview.boundPages![change.fieldId]) }}
          </p>
          <p
            v-if="change.examples.length > 0"
            class="help"
          >
            {{ t('cms.preview.examples') }}
            {{ change.examples.map((e) => e.title ?? t('cms.preview.untitled')).join(', ') }}
          </p>
        </li>
      </ul>

      <p
        v-if="mentionsDates"
        class="help"
      >
        {{ t('cms.preview.utc') }}
      </p>
      <p
        v-if="preview.requiresConfirmation"
        class="warning"
        role="note"
      >
        {{ t('cms.preview.undo') }}
      </p>

      <div class="actions">
        <button
          ref="cancelButton"
          type="button"
          :disabled="saving"
          @click="emit('cancel')"
        >
          {{ t('cms.preview.cancel') }}
        </button>
        <button
          type="button"
          :class="preview.requiresConfirmation ? 'danger' : 'primary'"
          :disabled="saving"
          @click="emit('confirm')"
        >
          {{ saving ? t('cms.preview.saving') : preview.requiresConfirmation ? t('cms.preview.confirm') : t('cms.preview.save') }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.schema-preview-changes {
  padding-inline-start: 1.25rem;
}
</style>
