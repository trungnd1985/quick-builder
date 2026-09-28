<script setup lang="ts">
// A property with a fixed set of values (the schema's enum), or a small integer range such
// as a heading's level (minimum..maximum).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FieldShell from './FieldShell.vue'
import { describedBy, type ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t, te } = useI18n()

const numeric = computed(() => props.schema.type === 'integer')

const options = computed<Json[]>(() => {
  if (Array.isArray(props.schema.enum)) return props.schema.enum
  const min = typeof props.schema.minimum === 'number' ? props.schema.minimum : 0
  const max = typeof props.schema.maximum === 'number' ? props.schema.maximum : min
  return Array.from({ length: max - min + 1 }, (_, i) => min + i)
})

const current = computed(() => String(props.value ?? props.defaultValue ?? ''))

const optionLabel = (value: Json): string => {
  const key = `editor.panel.values.${props.prop}.${String(value)}`
  const name = te(key) ? t(key) : String(value)
  return value === props.defaultValue ? `${name} ${t('editor.panel.defaultMark')}` : name
}

function onChange(event: Event): void {
  const raw = (event.target as HTMLSelectElement).value
  emit('change', numeric.value ? Number(raw) : raw)
}
</script>

<template>
  <FieldShell
    :id="id"
    :prop="prop"
    :label="label"
    :error="error"
  >
    <select
      :id="id"
      :value="current"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="describedBy(id, error)"
      @change="onChange"
    >
      <option
        v-for="option in options"
        :key="String(option)"
        :value="String(option)"
      >
        {{ optionLabel(option) }}
      </option>
    </select>
  </FieldShell>
</template>
