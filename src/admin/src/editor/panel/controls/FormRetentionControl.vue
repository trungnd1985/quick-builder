<script setup lang="ts">
// How long a form's submissions are kept (018 FR-023a, clarification 2): until deleted (the
// default), or 30, 90, 180 or 365 days, after which they are deleted automatically once a day.
// The setting takes effect when the page is published. When a period is turned on or
// shortened, the control asks how many submissions already received it would delete and says
// so before the page is published (the spec's edge case on shortening retention).
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'
import { formsApi, RETENTION_PERIODS, type RetentionDays } from '../../../api/forms'

const props = defineProps<ControlProps & { formId?: string }>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

// What would be deleted; null while unknown or when nothing shorter was chosen.
const impact = ref<number | null>(null)
let asked = 0

const days = (value: Json | undefined): number | null => (typeof value === 'number' ? value : null)

async function checkImpact(next: number | null, previous: number | null): Promise<void> {
  const ask = ++asked
  impact.value = null
  const shortened = next !== null && (previous === null || next < previous)
  if (!shortened || !props.formId) return
  try {
    const { count } = await formsApi.retentionImpact(props.siteId, props.formId, next as RetentionDays)
    if (ask === asked) impact.value = count
  } catch {
    // A form never published has no submissions; any other failure only loses the warning.
  }
}

// Another block, or an undo, resets the warning.
watch(() => props.formId, () => (impact.value = null))

function change(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  const next = value === '' ? null : Number(value)
  void checkImpact(next, days(props.value))
  emit('change', next ?? undefined)
}
</script>

<template>
  <div
    class="editor-field"
    :data-prop="prop"
  >
    <label
      class="editor-field__label"
      :for="id"
    >{{ label }}</label>
    <select
      :id="id"
      :value="typeof props.value === 'number' ? String(props.value) : ''"
      :aria-describedby="`${id}-help${impact ? ` ${id}-impact` : ''}`"
      @change="change"
    >
      <option value="">
        {{ t('editor.form.retention.off') }}
      </option>
      <option
        v-for="period in RETENTION_PERIODS"
        :key="period"
        :value="String(period)"
      >
        {{ t('editor.form.retention.days', { days: period }) }}
      </option>
    </select>
    <p
      :id="`${id}-help`"
      class="help"
    >
      {{ t('editor.form.retention.help') }}
    </p>
    <p
      v-if="impact"
      :id="`${id}-impact`"
      class="warning"
      role="status"
    >
      {{ t('editor.form.retention.impact', { count: impact }) }}
    </p>
    <p
      v-if="error"
      :id="`${id}-error`"
      class="field-error"
    >
      {{ error }}
    </p>
  </div>
</template>
