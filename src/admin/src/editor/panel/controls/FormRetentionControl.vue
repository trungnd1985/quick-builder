<script setup lang="ts">
// How long a form's submissions are kept (018 FR-023a, clarification 2): until deleted (the
// default), or 30, 90, 180 or 365 days, after which they are deleted automatically once a day.
// The setting takes effect when the page is published.
import { useI18n } from 'vue-i18n'
import type { ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const PERIODS = [30, 90, 180, 365] as const

function change(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  emit('change', value === '' ? undefined : Number(value))
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
      :aria-describedby="`${id}-help`"
      @change="change"
    >
      <option value="">
        {{ t('editor.form.retention.off') }}
      </option>
      <option
        v-for="days in PERIODS"
        :key="days"
        :value="String(days)"
      >
        {{ t('editor.form.retention.days', { days }) }}
      </option>
    </select>
    <p
      :id="`${id}-help`"
      class="help"
    >
      {{ t('editor.form.retention.help') }}
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
