<script setup lang="ts">
// A number field: whole or with its decimal places, its range shown (015 FR-009).
import { computed, ref, watch } from 'vue'
import type { SchemaField } from '../../../../api/cms'

const props = defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()

// What the member typed, kept even while it is not a number yet ("1.", "-").
const typed = ref(typeof props.value === 'number' ? String(props.value) : '')
watch(() => props.value, (value) => {
  if (typeof value === 'number' && Number(typed.value) !== value) typed.value = String(value)
  if (value === undefined && typed.value !== '' && Number.isFinite(Number(typed.value))) typed.value = ''
})

const step = computed(() => (props.field.decimals ? String(10 ** -props.field.decimals) : '1'))
const range = computed(() =>
  [props.field.min, props.field.max].some((v) => v !== undefined && v !== null)
    ? `${props.field.min ?? '…'} – ${props.field.max ?? '…'}`
    : '',
)

function update(raw: string): void {
  typed.value = raw
  const trimmed = raw.trim()
  if (trimmed === '') {
    emit('update:value', undefined)
    return
  }

  // Something that is not a number is sent as typed, so the field reports it as malformed.
  const number = Number(trimmed)
  emit('update:value', Number.isFinite(number) && /^-?\d+(\.\d+)?$/.test(trimmed) ? number : trimmed)
}
</script>

<template>
  <div class="item-input item-number">
    <input
      :id="id"
      type="text"
      inputmode="decimal"
      :value="typed"
      :step="step"
      :aria-describedby="describedBy + (range ? ` ${id}-range` : '')"
      :aria-invalid="invalid"
      :aria-required="field.required"
      @input="update(($event.target as HTMLInputElement).value)"
    >
    <span
      v-if="range"
      :id="`${id}-range`"
      class="help"
    >{{ range }}</span>
  </div>
</template>
