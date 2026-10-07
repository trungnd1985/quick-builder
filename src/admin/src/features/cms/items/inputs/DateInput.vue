<script setup lang="ts">
// A date field (015 FR-009, Edge Cases). A date-only value has no time zone and is kept as
// YYYY-MM-DD; a date and time is an exact moment, entered in the member's own time zone and
// stored in UTC (YYYY-MM-DDTHH:MM:SSZ).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../../api/cms'

const props = defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t } = useI18n()

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone
const pad = (n: number): string => String(n).padStart(2, '0')

/** The stored UTC moment as the member's local "YYYY-MM-DDTHH:MM" for the input. */
const local = computed(() => {
  if (typeof props.value !== 'string') return ''
  if (!props.field.withTime) return props.value
  const date = new Date(props.value)
  if (Number.isNaN(date.getTime())) return ''
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
})

function update(raw: string): void {
  if (raw === '') {
    emit('update:value', undefined)
    return
  }

  if (!props.field.withTime) {
    emit('update:value', raw)
    return
  }

  const date = new Date(raw)
  emit('update:value', Number.isNaN(date.getTime()) ? raw : date.toISOString().replace(/\.\d{3}Z$/, 'Z'))
}
</script>

<template>
  <div class="item-input item-date">
    <input
      :id="id"
      :type="field.withTime ? 'datetime-local' : 'date'"
      :value="local"
      :aria-describedby="describedBy + (field.withTime ? ` ${id}-zone` : '')"
      :aria-invalid="invalid"
      :aria-required="field.required"
      @change="update(($event.target as HTMLInputElement).value)"
    >
    <span
      v-if="field.withTime"
      :id="`${id}-zone`"
      class="help"
    >{{ t('cms.items.form.timeZone', { zone }) }}</span>
  </div>
</template>
